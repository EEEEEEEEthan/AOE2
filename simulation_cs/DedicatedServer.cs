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
}

/// <summary>长度前缀分帧：[type:1][len:4 LE][payload:len]。</summary>
public static class NetFraming
{
    public static void WriteFrame(NetworkStream stream, byte frameType, byte[] payload)
    {
        byte[] header = new byte[5];
        header[0] = frameType;
        int length = payload.Length;
        header[1] = (byte)(length & 0xFF);
        header[2] = (byte)((length >> 8) & 0xFF);
        header[3] = (byte)((length >> 16) & 0xFF);
        header[4] = (byte)((length >> 24) & 0xFF);
        stream.Write(header, 0, header.Length);
        stream.Write(payload, 0, payload.Length);
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

/// <summary>Dedicated server：独立进程跑权威模拟，30Hz 广播快照、接收客户端指令。</summary>
public sealed class DedicatedServer
{
    private readonly TcpListener _listener;
    private readonly SimulationServer _server;
    private readonly int _ticksPerSecond;
    private Thread? _thread;
    private volatile bool _running;

    public DedicatedServer(int port, GameSim sim, int ticksPerSecond = 30)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _server = new SimulationServer(sim);
        _ticksPerSecond = ticksPerSecond;
    }

    public void Start()
    {
        _running = true;
        _listener.Start();
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _listener.Stop();
    }

    private void Run()
    {
        int tickMilliseconds = 1000 / _ticksPerSecond;
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
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                ServeClient(stream, tickMilliseconds);
            }
        }
    }

    private void ServeClient(NetworkStream stream, int tickMilliseconds)
    {
        while (_running)
        {
            long deadline = System.Environment.TickCount64 + tickMilliseconds;
            while (stream.DataAvailable)
            {
                if (!NetFraming.TryReadFrame(stream, out byte frameType, out byte[] payload))
                {
                    return;
                }
                if (frameType == NetFrameType.Command)
                {
                    _server.Enqueue(CommandFrame.Decode(payload));
                }
            }
            _server.Step();
            SnapshotFrame snapshot = _server.Snapshot();
            NetFraming.WriteFrame(stream, NetFrameType.Snapshot, snapshot.Encode());
            long remaining = deadline - System.Environment.TickCount64;
            if (remaining > 0)
            {
                Thread.Sleep((int)remaining);
            }
        }
    }
}

/// <summary>远程瘦客户端：只向服务器发指令、接收快照。</summary>
public sealed class RemoteClient
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public SimulationClient State { get; } = new();

    public void Connect(string host, int port)
    {
        _client.Connect(host, port);
        _client.NoDelay = true;
        _stream = _client.GetStream();
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
        if (frameType == NetFrameType.Snapshot)
        {
            State.ApplySnapshot(payload);
            return true;
        }
        return false;
    }
}
