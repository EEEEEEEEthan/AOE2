using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace AoeSim;

/// <summary>TCP 帧类型：客户端上行指令、服务器下行快照。</summary>
public static class NetFrameType
{
    public const byte Command = 0;
    public const byte Snapshot = 1;
    public const byte StaticMap = 2;
    public const byte Welcome = 3;
}

/// <summary>长度前缀分帧：[type:1][len:4 LE][payload:len]。</summary>
public static class NetFraming
{
    public static void WriteFrame(NetworkStream stream, byte frameType, byte[] payload) =>
        WriteFrame(stream, frameType, payload, payload.Length);

    public static void WriteFrame(NetworkStream stream, byte frameType, byte[] payload, int length)
    {
        byte[] header = new byte[5];
        header[0] = frameType;
        header[1] = (byte)(length & 0xFF);
        header[2] = (byte)((length >> 8) & 0xFF);
        header[3] = (byte)((length >> 16) & 0xFF);
        header[4] = (byte)((length >> 24) & 0xFF);
        stream.Write(header, 0, header.Length);
        stream.Write(payload, 0, length);
        stream.Flush();
    }

    public static bool TryReadFrame(NetworkStream stream, out byte frameType, out byte[] payload)
    {
        frameType = 0;
        payload = System.Array.Empty<byte>();
        byte[]? header = ReadExact(stream, 5);
        if (header == null)
        {
            return false;
        }
        frameType = header[0];
        int length = header[1] | (header[2] << 8) | (header[3] << 16) | (header[4] << 24);
        byte[]? body = ReadExact(stream, length);
        if (body == null)
        {
            return false;
        }
        payload = body;
        return true;
    }

    private static byte[]? ReadExact(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                return null;
            }
            offset += read;
        }
        return buffer;
    }
}

/// <summary>
/// Dedicated server：独立进程跑权威模拟，30Hz 为每个连接按其 team 下发快照、接收客户端指令。
/// 连接层负责把「连接 → 玩家」绑定好，指令入队时以该玩家覆写自报身份；
/// 模拟按锁步回合推进：每 <see cref="Lockstep.TicksPerTurn"/> 个 tick 为一次回合边界，回合开始时
/// 统一应用该回合收到的全部输入，缺输入的玩家按空输入推进（服务器不会因等待输入而停顿）。
/// </summary>
public sealed class DedicatedServer
{
    /// <summary>单条连接的收发状态：读线程收指令，写线程按有界队列发下行帧，慢客户端只丢旧快照。</summary>
    private sealed class ClientChannel
    {
        private readonly BlockingCollection<Outgoing> _outbound =
            new(new ConcurrentQueue<Outgoing>(), 8);

        public ClientChannel(TcpClient client, long team)
        {
            Tcp = client;
            Stream = client.GetStream();
            Team = team;
        }

        public TcpClient Tcp { get; }
        public NetworkStream Stream { get; }
        public long Team { get; }
        public volatile bool Alive = true;
        public Thread? Reader { get; set; }
        public Thread? Writer { get; set; }

        public readonly struct Outgoing
        {
            public readonly byte Type;
            public readonly byte[] Payload;

            public Outgoing(byte type, byte[] payload)
            {
                Type = type;
                Payload = payload;
            }
        }

        public void Enqueue(byte frameType, byte[] payload)
        {
            if (!Alive)
            {
                return;
            }
            if (!_outbound.TryAdd(new Outgoing(frameType, payload)))
            {
                // 队列满说明客户端跟不上：丢掉最旧一帧，保留最新快照，绝不阻塞模拟线程。
                _outbound.TryTake(out _);
                _outbound.TryAdd(new Outgoing(frameType, payload));
            }
        }

        public void Complete() => _outbound.CompleteAdding();

        public IEnumerable<Outgoing> TakeAll() => _outbound.GetConsumingEnumerable();

        public void Close()
        {
            Alive = false;
            Complete();
            try
            {
                Tcp.Close();
            }
            catch (Exception)
            {
                // 关闭时对端已断开属于正常路径。
            }
        }
    }

    private readonly TcpListener _listener;
    private readonly SimulationServer _server;
    private readonly byte[] _staticMapPayload;
    private readonly int _ticksPerSecond;
    private readonly object _clientsGate = new();
    private readonly List<ClientChannel> _clients = new();
    private readonly HashSet<long> _assignedTeams = new();
    private Thread? _acceptThread;
    private Thread? _simThread;
    private volatile bool _running;

    public DedicatedServer(int port, GameSim sim, int ticksPerSecond = 30)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _server = new SimulationServer(sim);
        _staticMapPayload = StaticMapFrame.FromTerrain(sim.Terrain).Encode();
        _ticksPerSecond = Math.Max(1, ticksPerSecond);
    }

    /// <summary>实际监听端口：构造时传 0 由系统分配，测试据此连接。</summary>
    public int Port { get; private set; }

    public void Start()
    {
        _running = true;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
        _simThread = new Thread(SimulateLoop) { IsBackground = true };
        _simThread.Start();
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // 已停止时重复 Stop 视为成功。
        }
        List<ClientChannel> channels;
        lock (_clientsGate)
        {
            channels = new List<ClientChannel>(_clients);
            _clients.Clear();
            _assignedTeams.Clear();
        }
        foreach (ClientChannel channel in channels)
        {
            channel.Close();
        }
        _acceptThread?.Join(1000);
        _simThread?.Join(1000);
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            RegisterClient(client);
        }
    }

    private void RegisterClient(TcpClient client)
    {
        client.NoDelay = true;
        if (!TryReserveTeam(out long team))
        {
            // 已满员：不再分配可复用 team，避免第三个连接冒充已有玩家。
            client.Close();
            return;
        }
        var channel = new ClientChannel(client, team);
        try
        {
            // 握手必须先于任何快照写入，客户端据此知道自己观察哪一方，也据此提交本回合指令。
            NetFraming.WriteFrame(channel.Stream, NetFrameType.Welcome, new WelcomeFrame { ViewerTeam = team }.Encode());
            NetFraming.WriteFrame(channel.Stream, NetFrameType.StaticMap, _staticMapPayload);
        }
        catch (Exception)
        {
            channel.Close();
            ReleaseTeam(team);
            return;
        }
        channel.Reader = new Thread(() => ReadLoop(channel)) { IsBackground = true };
        channel.Writer = new Thread(() => WriteLoop(channel)) { IsBackground = true };
        channel.Reader.Start();
        channel.Writer.Start();
        lock (_clientsGate)
        {
            _clients.Add(channel);
        }
    }

    /// <summary>为连接分配一个空闲玩家位；满员时返回 false。</summary>
    private bool TryReserveTeam(out long team)
    {
        lock (_clientsGate)
        {
            for (long candidate = 0; candidate < _server.Sim.Lockstep.PlayerCount; candidate++)
            {
                if (!_assignedTeams.Contains(candidate))
                {
                    _assignedTeams.Add(candidate);
                    team = candidate;
                    return true;
                }
            }
        }
        team = -1;
        return false;
    }

    private void ReleaseTeam(long team)
    {
        lock (_clientsGate)
        {
            _assignedTeams.Remove(team);
        }
    }

    private void ReadLoop(ClientChannel channel)
    {
        try
        {
            while (_running && channel.Alive)
            {
                if (!NetFraming.TryReadFrame(channel.Stream, out byte frameType, out byte[] payload))
                {
                    break;
                }
                if (frameType == NetFrameType.Command)
                {
                    // 指令来源以连接分配的 team 为准，客户端自报的 Player 不可信。
                    _server.Enqueue(CommandFrame.Decode(payload), channel.Team);
                }
            }
        }
        catch (Exception)
        {
            // 连接中断：清理由写线程与模拟线程统一完成。
        }
        finally
        {
            channel.Alive = false;
            try
            {
                channel.Tcp.Close();
            }
            catch (Exception)
            {
                // 已关闭。
            }
        }
    }

    private void WriteLoop(ClientChannel channel)
    {
        try
        {
            foreach (ClientChannel.Outgoing outgoing in channel.TakeAll())
            {
                NetFraming.WriteFrame(channel.Stream, outgoing.Type, outgoing.Payload);
            }
        }
        catch (Exception)
        {
            // 写失败说明客户端已断开：摘除连接，绝不拖慢整体 tick。
        }
        finally
        {
            channel.Alive = false;
            try
            {
                channel.Tcp.Close();
            }
            catch (Exception)
            {
                // 已关闭。
            }
        }
    }

    private void SimulateLoop()
    {
        int tickMilliseconds = 1000 / _ticksPerSecond;
        while (_running)
        {
            long deadline = Environment.TickCount64 + tickMilliseconds;
            _server.Step();
            List<ClientChannel> channels = SnapshotClients();
            bool removed = false;
            foreach (ClientChannel channel in channels)
            {
                if (!channel.Alive)
                {
                    removed = true;
                    continue;
                }
                SnapshotFrame snapshot = _server.Snapshot(channel.Team);
                channel.Enqueue(NetFrameType.Snapshot, snapshot.Encode());
            }
            if (removed)
            {
                RemoveDeadClients();
            }
            long remaining = deadline - Environment.TickCount64;
            if (remaining > 0)
            {
                Thread.Sleep((int)remaining);
            }
        }
    }

    private List<ClientChannel> SnapshotClients()
    {
        lock (_clientsGate)
        {
            return new List<ClientChannel>(_clients);
        }
    }

    private void RemoveDeadClients()
    {
        List<ClientChannel> dead = new();
        lock (_clientsGate)
        {
            for (int index = _clients.Count - 1; index >= 0; index--)
            {
                if (!_clients[index].Alive)
                {
                    dead.Add(_clients[index]);
                    _clients.RemoveAt(index);
                }
            }
        }
        foreach (ClientChannel channel in dead)
        {
            channel.Close();
            ReleaseTeam(channel.Team);
        }
    }
}

/// <summary>远程瘦客户端：握手拿自己的观察方 team，之后只向服务器发指令、接收本队视野快照。</summary>
public sealed class RemoteClient
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public SimulationClient State { get; } = new();

    /// <summary>服务器在 Welcome 握手帧里分配的观察方 team；未握手时为 -1。</summary>
    public long ViewerTeam { get; private set; } = -1;

    public void Connect(string host, int port)
    {
        _client.Connect(host, port);
        _client.NoDelay = true;
        _client.ReceiveTimeout = 5000;
        _stream = _client.GetStream();
        while (ViewerTeam < 0 || State.Map == null)
        {
            if (!NetFraming.TryReadFrame(_stream, out byte frameType, out byte[] payload))
            {
                throw new System.IO.IOException("server closed during handshake");
            }
            ApplyFrame(frameType, payload);
        }
        _client.ReceiveTimeout = 0;
    }

    public void SendCommands(CommandFrame frame)
    {
        if (_stream == null)
        {
            return;
        }
        NetFraming.WriteFrame(_stream, NetFrameType.Command, frame.Encode());
    }

    public bool Poll()
    {
        if (_stream == null || !_stream.DataAvailable)
        {
            return false;
        }
        if (!NetFraming.TryReadFrame(_stream, out byte frameType, out byte[] payload))
        {
            return false;
        }
        return ApplyFrame(frameType, payload);
    }

    public void Disconnect()
    {
        _stream = null;
        _client.Close();
    }

    private bool ApplyFrame(byte frameType, byte[] payload)
    {
        if (frameType == NetFrameType.Welcome)
        {
            ViewerTeam = WelcomeFrame.Decode(payload).ViewerTeam;
            return true;
        }
        if (frameType == NetFrameType.StaticMap)
        {
            State.ApplyStaticMap(payload);
            return true;
        }
        if (frameType == NetFrameType.Snapshot)
        {
            State.ApplySnapshot(payload);
            return true;
        }
        return false;
    }
}
