using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace AoeSim;

/// <summary>TCP 帧类型：客户端上行指令与 ping，服务器下行快照与 echo。</summary>
public static class NetFrameType
{
    public const byte Command = 0;
    public const byte Snapshot = 1;
    public const byte StaticMap = 2;
    public const byte Welcome = 3;
    public const byte Lobby = 4;
    public const byte Ping = 5;
    public const byte Echo = 6;
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
            Name = $"玩家 {team + 1}";
            Color = team;
        }

        public TcpClient Tcp { get; }
        public NetworkStream Stream { get; }
        public long Team { get; set; }
        public string Name { get; set; }
        public long Color { get; set; }
        public bool Ready { get; set; }
        public SnapshotDelta Delta { get; } = new();

        /// <summary>写线程与读线程回 echo 共用同一 NetworkStream，靠此锁避免五字节分帧交织。</summary>
        public object WriteGate { get; } = new();
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

        /// <summary>入队一帧；返回 false 表示客户端跟不上、旧帧已被清空，调用方须重置增量状态改发关键帧。</summary>
        public bool Enqueue(byte frameType, byte[] payload)
        {
            if (!Alive)
            {
                return true;
            }
            if (_outbound.TryAdd(new Outgoing(frameType, payload)))
            {
                return true;
            }
            // 队列满说明客户端跟不上：清空积压，绝不阻塞模拟线程；增量帧有依赖，必须由调用方改发关键帧。
            while (_outbound.TryTake(out _))
            {
            }
            return false;
        }

        public void Complete() => _outbound.CompleteAdding();

        /// <summary>
        /// 读线程收到 ping 后立即回 echo：走写锁直写而不进出站队列，
        /// 避免快照有界队列满时被丢弃或被大帧推迟导致 RTT 失真。
        /// </summary>
        public void Echo(byte[] payload)
        {
            if (!Alive)
            {
                return;
            }
            try
            {
                lock (WriteGate)
                {
                    NetFraming.WriteFrame(Stream, NetFrameType.Echo, payload);
                }
            }
            catch (System.Exception)
            {
                Alive = false;
            }
        }

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
    private SimulationServer? _server = null;
    private byte[]? _staticMapPayload;
    private readonly LobbyRules? _lobbyRules = null;
    private readonly int _ticksPerSecond;
    private readonly int _snapshotIntervalTicks;
    private readonly object _clientsGate = new();
    private readonly List<ClientChannel> _clients = new();
    private readonly HashSet<long> _assignedTeams = new();
    private Thread? _acceptThread;
    private Thread? _simThread;
    private volatile bool _running;
    private volatile bool _matchRunning;

    public DedicatedServer(int port, GameSim sim, int ticksPerSecond = 30, int snapshotIntervalTicks = 2)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _server = new SimulationServer(sim);
        _staticMapPayload = StaticMapFrame.FromTerrain(sim.Terrain).Encode();
        _ticksPerSecond = Math.Max(1, ticksPerSecond);
        _snapshotIntervalTicks = Math.Max(1, snapshotIntervalTicks);
        _matchRunning = true;
    }

    /// <summary>带大厅的模式：先收人、准备、修好房间配置，房主开始后才构建对局。</summary>
    public DedicatedServer(int port, LobbyRules lobbyRules, int ticksPerSecond = 30, int snapshotIntervalTicks = 2)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _lobbyRules = lobbyRules;
        _ticksPerSecond = Math.Max(1, ticksPerSecond);
        _snapshotIntervalTicks = Math.Max(1, snapshotIntervalTicks);
    }

    /// <summary>实际监听端口：构造时传 0 由系统分配，测试据此连接。</summary>
    public int Port { get; private set; }

    /// <summary>每隔多少个模拟 tick 下发一次快照；模拟仍按 <see cref="TicksPerSimulationSecond"/> 推进。</summary>
    public int SnapshotIntervalTicks => _snapshotIntervalTicks;

    /// <summary>模拟频率，与快照下发频率解耦。</summary>
    public int TicksPerSimulationSecond => _ticksPerSecond;

    public void Start()
    {
        _running = true;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
        if (_server != null)
        {
            StartSimulationLoop();
        }
    }

    private void StartSimulationLoop()
    {
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
            // 已满员：先回一条明确错误再断开，避免客户端只看到无声掉线。
            try
            {
                NetFraming.WriteFrame(
                    client.GetStream(),
                    NetFrameType.Lobby,
                    new LobbyMessage { Kind = LobbyMessage.KindError, Error = "房间已满" }.Encode());
            }
            catch (Exception)
            {
                // 对端已经断开时无需再提示。
            }
            client.Close();
            return;
        }
        var channel = new ClientChannel(client, team);
        bool sentMap = false;
        try
        {
            NetFraming.WriteFrame(channel.Stream, NetFrameType.Welcome, MakeWelcome(team).Encode());
            if (!IsLobby)
            {
                NetFraming.WriteFrame(channel.Stream, NetFrameType.StaticMap, _staticMapPayload!);
                sentMap = true;
            }
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
            if (!sentMap && _server != null && _staticMapPayload != null)
            {
                // 与开局并发接入的客户端：开局循环还没把它算进去，这里补发地图与开始通知。
                channel.Enqueue(
                    NetFrameType.StaticMap,
                    _staticMapPayload);
                channel.Enqueue(NetFrameType.Lobby, new LobbyMessage
                {
                    Kind = LobbyMessage.KindStartMatch,
                    MapName = _lobbyRules?.MapName ?? "",
                    Started = true,
                    HostSeat = 0,
                }.Encode());
            }
        }
        BroadcastRoomState();
    }

    private bool IsLobby => _lobbyRules != null && !_matchRunning;

    /// <summary>握手帧带上本局节奏，客户端把 RTT 换算提前量时不再硬编码单一对局配置。</summary>
    private WelcomeFrame MakeWelcome(long team) => new()
    {
        ViewerTeam = team,
        TicksPerTurn = _server?.Sim.Lockstep.TicksPerTurn ?? 6,
        TicksPerSecond = _ticksPerSecond,
    };

    /// <summary>为连接分配一个空闲玩家位；满员时返回 false。</summary>
    private bool TryReserveTeam(out long team)
    {
        long capacity = _server?.Sim.Lockstep.PlayerCount ?? _lobbyRules!.MaxPlayers;
        lock (_clientsGate)
        {
            for (long candidate = 0; candidate < capacity; candidate++)
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
                    if (_server != null)
                    {
                        _server.Enqueue(CommandFrame.Decode(payload), channel.Team);
                    }
                }
                else if (frameType == NetFrameType.Ping)
                {
                    channel.Echo(payload);
                }
                else if (frameType == NetFrameType.Lobby)
                {
                    HandleLobbyMessage(channel, LobbyMessage.Decode(payload));
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
            RemoveDeadClients();
        }
    }

    private void WriteLoop(ClientChannel channel)
    {
        try
        {
            foreach (ClientChannel.Outgoing outgoing in channel.TakeAll())
            {
                lock (channel.WriteGate)
                {
                    NetFraming.WriteFrame(channel.Stream, outgoing.Type, outgoing.Payload);
                }
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
        SimulationServer? server = _server;
        if (server == null)
        {
            return;
        }
        int tickMilliseconds = 1000 / _ticksPerSecond;
        int ticksSinceSnapshot = _snapshotIntervalTicks;
        while (_running)
        {
            long deadline = Environment.TickCount64 + tickMilliseconds;
            server.Step();
            ticksSinceSnapshot += 1;
            List<ClientChannel> channels = SnapshotClients();
            bool removed = false;
            foreach (ClientChannel channel in channels)
            {
                removed |= !channel.Alive;
            }
            bool due = ticksSinceSnapshot >= _snapshotIntervalTicks;
            if (due)
            {
                ticksSinceSnapshot = 0;
                // 同一 tick 的全量快照只构建一次，各连接仅按自己的队伍裁剪。
                SnapshotFrame? full = null;
                foreach (ClientChannel channel in channels)
                {
                    if (!channel.Alive)
                    {
                        continue;
                    }
                    full ??= server.SnapshotFull();
                    SnapshotFrame snapshot = server.SnapshotFor(full, channel.Team);
                    SnapshotFrame outgoing = channel.Delta.Build(snapshot);
                    if (!channel.Enqueue(NetFrameType.Snapshot, outgoing.Encode()))
                    {
                        // 丢帧后客户端状态不再连续：本帧丢弃，下一次重置并重发关键帧。
                        channel.Delta.Reset();
                    }
                }
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
        bool removed;
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
            removed = dead.Count > 0;
            if (removed)
            {
                foreach (ClientChannel channel in dead)
                {
                    _assignedTeams.Remove(channel.Team);
                }
            }
            if (removed && IsLobby)
            {
                CompactLobbySeats();
            }
        }
        foreach (ClientChannel channel in dead)
        {
            channel.Close();
        }
        if (removed)
        {
            BroadcastRoomState();
        }
    }

    /// <summary>大厅里有人离开后把座位压密：空位不留在中间，剩余玩家依次前移并领到新的观察方编号。</summary>
    private void CompactLobbySeats()
    {
        _clients.Sort((left, right) => left.Team.CompareTo(right.Team));
        _assignedTeams.Clear();
        for (int index = 0; index < _clients.Count; index++)
        {
            ClientChannel channel = _clients[index];
            _assignedTeams.Add(index);
            if (channel.Team == index)
            {
                continue;
            }
            channel.Team = index;
            channel.Enqueue(
                NetFrameType.Welcome,
                MakeWelcome(index).Encode());
        }
    }

    private void HandleLobbyMessage(ClientChannel channel, LobbyMessage message)
    {
        lock (_clientsGate)
        {
            if (_matchRunning || IsLobby == false)
            {
                SendLobbyError(channel, "对局已经开始");
                return;
            }
            if (message.Kind == LobbyMessage.KindJoin || message.Kind == LobbyMessage.KindProfile)
            {
                string name = message.Name.Trim();
                if (name.Length > 0)
                {
                    channel.Name = name.Length > 20 ? name.Substring(0, 20) : name;
                }
                channel.Color = System.Math.Clamp(message.Color, 0, 7);
            }
            else if (message.Kind == LobbyMessage.KindReady)
            {
                channel.Ready = message.Ready;
            }
            else if (message.Kind == LobbyMessage.KindStart)
            {
                if (channel.Team != 0)
                {
                    SendLobbyError(channel, "只有房主可以开始对局");
                    return;
                }
                if (!TryStartMatch(out string error))
                {
                    SendLobbyError(channel, error);
                    return;
                }
            }
            else
            {
                SendLobbyError(channel, "未知的大厅请求");
                return;
            }
        }
        BroadcastRoomState();
    }

    private bool TryStartMatch(out string error)
    {
        if (_clients.Count == 0)
        {
            error = "房间里还没有玩家";
            return false;
        }
        foreach (ClientChannel channel in _clients)
        {
            if (channel.Team != 0 && !channel.Ready)
            {
                error = $"等待 {channel.Name} 准备";
                return false;
            }
        }
        GameSim sim;
        try
        {
            sim = MapSetup.BuildGeneratedMatch(
                MapType.Arabia, _lobbyRules!.Seed, _clients.Count);
        }
        catch (System.Exception exception)
        {
            error = $"开局失败：{exception.Message}";
            return false;
        }
        _server = new SimulationServer(sim);
        _staticMapPayload = StaticMapFrame.FromTerrain(sim.Terrain).Encode();
        _matchRunning = true;
        var started = new LobbyMessage
        {
            Kind = LobbyMessage.KindStartMatch,
            MapName = _lobbyRules.MapName,
            Started = true,
            HostSeat = 0,
        };
        List<ClientChannel> clients = new(_clients);
        clients.Sort((left, right) => left.Team.CompareTo(right.Team));
        foreach (ClientChannel channel in clients)
        {
            channel.Enqueue(
                NetFrameType.Welcome,
                MakeWelcome(channel.Team).Encode());
            channel.Enqueue(NetFrameType.StaticMap, _staticMapPayload!);
            channel.Enqueue(NetFrameType.Lobby, started.Encode());
        }
        StartSimulationLoop();
        error = "";
        return true;
    }

    private void SendLobbyError(ClientChannel channel, string error)
    {
        channel.Enqueue(NetFrameType.Lobby, new LobbyMessage
        {
            Kind = LobbyMessage.KindError,
            Error = error,
        }.Encode());
    }

    private void BroadcastRoomState()
    {
        lock (_clientsGate)
        {
            if (_clients.Count == 0)
            {
                return;
            }
            List<ClientChannel> clients = new(_clients);
            clients.Sort((left, right) => left.Team.CompareTo(right.Team));
            var slots = new LobbySlot[clients.Count];
            for (int index = 0; index < clients.Count; index++)
            {
                ClientChannel channel = clients[index];
                slots[index] = new LobbySlot(channel.Team, channel.Name, channel.Color, channel.Ready);
            }
            byte[] payload = new LobbyMessage
            {
                Kind = LobbyMessage.KindRoom,
                MapName = _lobbyRules?.MapName ?? "",
                HostSeat = clients.Count > 0 ? clients[0].Team : -1,
                Started = _matchRunning,
                Slots = slots,
            }.Encode();
            foreach (ClientChannel channel in clients)
            {
                channel.Enqueue(NetFrameType.Lobby, payload);
            }
        }
    }
}

/// <summary>远程瘦客户端：握手拿自己的观察方 team，之后只向服务器发指令、接收本队视野快照。</summary>
public sealed class RemoteClient
{
    private readonly TcpClient _client = new();
    private readonly System.Func<long> _clock;
    private readonly RttTracker _rttTracker;
    private readonly System.Collections.Generic.Dictionary<long, long> _pendingPings = new();
    private NetworkStream? _stream;
    private long _nextPingSequence;
    private long _lastPingSentAt = long.MinValue;

    public RemoteClient(System.Func<long>? clock = null)
    {
        _clock = clock ?? (static () => System.Environment.TickCount64);
        _rttTracker = new RttTracker(_clock);
    }

    public SimulationClient State { get; } = new();

    /// <summary>服务器在 Welcome 握手帧里分配的观察方 team；未握手时为 -1。</summary>
    public long ViewerTeam { get; private set; } = -1;

    /// <summary>服务器下发的每回合 tick 数，用于把 RTT 换算成提前回合。</summary>
    public int TicksPerTurn { get; private set; } = 6;

    /// <summary>服务器下发的模拟频率，用于把 RTT 换算成提前回合。</summary>
    public int TicksPerSecond { get; private set; } = 30;

    /// <summary>已应用的下行快照帧数，用于验证快照降频后仍持续推进。</summary>
    public long SnapshotFrames { get; private set; }

    /// <summary>累计收到的 echo 采样数，0 表示尚未测到 RTT。</summary>
    public long RttSampleCount { get; private set; }

    /// <summary>平滑往返毫秒；从未采样时为 -1。</summary>
    public double SmoothedRttMilliseconds => _rttTracker.SmoothedRttMilliseconds;

    /// <summary>当前应使用的输入提前量：有新鲜 RTT 样本按其换算，否则回退默认值。</summary>
    public int LookaheadTurns => _rttTracker.LookaheadFor(TicksPerTurn, TicksPerSecond);

    /// <summary>最近一次房间快照；未进大厅时为 null。</summary>
    public LobbyMessage? Room { get; private set; }

    /// <summary>房间快照版本号：内容变化时递增，UI 据此决定是否重建列表。</summary>
    public long LobbyVersion { get; private set; }

    public bool MatchStarted => State.Map != null;

    /// <summary>大厅连接：只建立 TCP 与读超时，不阻塞等待静态地图（对局开始后才下发）。</summary>
    public void ConnectLobby(string host, int port)
    {
        _client.Connect(host, port);
        _client.NoDelay = true;
        _client.ReceiveTimeout = 5000;
        _stream = _client.GetStream();
        _client.ReceiveTimeout = 0;
    }

    /// <summary>向服务器发一条大厅请求：加入/资料/开始。</summary>
    public void SendLobby(LobbyMessage message)
    {
        if (_stream == null)
        {
            return;
        }
        NetFraming.WriteFrame(_stream, NetFrameType.Lobby, message.Encode());
    }

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
        _lastPingSentAt = long.MinValue;
    }

    public void SendCommands(CommandFrame frame)
    {
        if (_stream == null)
        {
            return;
        }
        NetFraming.WriteFrame(_stream, NetFrameType.Command, frame.Encode());
    }

    /// <summary>
    /// 输入转发层：以最近快照的回合为基准加提前量填 <see cref="CommandFrame.Turn"/>，
    /// 再发送指令。提前量默认取实测 RTT 换算值（无新鲜样本时回退
    /// <see cref="InputTiming.DefaultLookaheadTurns"/>），显式传入则覆盖实测。
    /// </summary>
    public void SubmitCommands(
        System.Collections.Generic.IEnumerable<LockstepCommand> commands,
        int? lookaheadTurns = null)
    {
        SnapshotFrame? latest = State.Latest;
        if (_stream == null || latest == null)
        {
            return;
        }
        var batch = new System.Collections.Generic.List<LockstepCommand>(commands);
        if (batch.Count == 0)
        {
            return;
        }
        SendCommands(new CommandFrame
        {
            Player = ViewerTeam,
            Turn = InputTiming.PlanTurn(latest.Turn, lookaheadTurns ?? LookaheadTurns),
            Commands = batch.ToArray(),
        });
    }

    public bool Poll()
    {
        if (_stream == null)
        {
            return false;
        }
        SendPingIfDue();
        if (!_stream.DataAvailable)
        {
            return false;
        }
        if (!NetFraming.TryReadFrame(_stream, out byte frameType, out byte[] payload))
        {
            return false;
        }
        return ApplyFrame(frameType, payload);
    }

    /// <summary>按固定间隔发一条带序号的 ping，并在超时后丢弃未回显的旧序号。</summary>
    private void SendPingIfDue()
    {
        long now = _clock();
        if (_lastPingSentAt != long.MinValue
            && now - _lastPingSentAt < RttTracker.PingIntervalMilliseconds)
        {
            return;
        }
        _lastPingSentAt = now;
        PrunePendingPings(now);
        long sequence = _nextPingSequence++;
        _pendingPings[sequence] = now;
        NetFraming.WriteFrame(_stream!, NetFrameType.Ping, new PingFrame { Sequence = sequence }.Encode());
    }

    private void PrunePendingPings(long now)
    {
        if (_pendingPings.Count == 0)
        {
            return;
        }
        var expired = new System.Collections.Generic.List<long>();
        foreach (System.Collections.Generic.KeyValuePair<long, long> pending in _pendingPings)
        {
            if (now - pending.Value > RttTracker.SampleTimeoutMilliseconds)
            {
                expired.Add(pending.Key);
            }
        }
        foreach (long sequence in expired)
        {
            _pendingPings.Remove(sequence);
        }
    }

    public void Disconnect()
    {
        _stream = null;
        _pendingPings.Clear();
        _client.Close();
    }

    private bool ApplyFrame(byte frameType, byte[] payload)
    {
        if (frameType == NetFrameType.Welcome)
        {
            WelcomeFrame welcome = WelcomeFrame.Decode(payload);
            ViewerTeam = welcome.ViewerTeam;
            TicksPerTurn = welcome.TicksPerTurn;
            TicksPerSecond = welcome.TicksPerSecond;
            return true;
        }
        if (frameType == NetFrameType.Echo)
        {
            long sequence = PingFrame.Decode(payload).Sequence;
            if (_pendingPings.Remove(sequence, out long sentAt))
            {
                _rttTracker.Record(_clock() - sentAt);
                RttSampleCount += 1;
            }
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
            SnapshotFrames += 1;
            return true;
        }
        if (frameType == NetFrameType.Lobby)
        {
            Room = LobbyMessage.Decode(payload);
            LobbyVersion += 1;
            return true;
        }
        return false;
    }
}
