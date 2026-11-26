using Godot;
using AoeSim;

namespace AOE2Framework;

/// <summary>
/// 多人大厅与联机对局共用的网络节点：房主进程内跑带大厅的 DedicatedServer，
/// 自己再以普通客户端连上去，房主与加入者走同一条加入/房间/开始路径。
/// </summary>
[GlobalClass]
public partial class MultiplayerSession : Node
{
    private static readonly Color[] TeamPalette =
    {
        new(0.28f, 0.52f, 0.95f),
        new(0.9f, 0.3f, 0.28f),
        new(0.3f, 0.75f, 0.42f),
        new(0.88f, 0.75f, 0.2f),
        new(0.85f, 0.5f, 0.2f),
        new(0.62f, 0.38f, 0.85f),
        new(0.25f, 0.75f, 0.78f),
        new(0.7f, 0.7f, 0.72f),
    };

    private RemoteClient? _client;
    private DedicatedServer? _hostServer;
    private bool _handoff;

    public string LastError { get; private set; } = "";

    public bool IsHost => _hostServer != null;

    public RemoteClient? Client => _client;

    public bool Connected => _client != null && _client.ViewerTeam >= 0;

    public bool MatchStarted => _client != null && _client.MatchStarted;

    public long MySeat => _client?.ViewerTeam ?? -1;

    public long LobbyVersion => _client?.LobbyVersion ?? 0;

    public Color TeamColor(long team) =>
        TeamPalette[System.Math.Clamp(team, 0, TeamPalette.Length - 1)];

    public override void _Process(double delta) => Poll();

    /// <summary>清空一条连接的待读帧；房间与快照都在这里落地。</summary>
    public void Poll()
    {
        if (_client == null)
        {
            return;
        }
        while (_client.Poll())
        {
        }
    }

    public bool HostRoom(int port, string playerName)
    {
        Teardown();
        try
        {
            _hostServer = new DedicatedServer(port, new AoeSim.LobbyRules());
            _hostServer.Start();
            _client = new RemoteClient();
            _client.ConnectLobby("127.0.0.1", _hostServer.Port);
            _client.SendLobby(new AoeSim.LobbyMessage { Kind = AoeSim.LobbyMessage.KindJoin, Name = playerName });
            LastError = "";
            return true;
        }
        catch (System.Exception exception)
        {
            LastError = exception.Message;
            Teardown();
            return false;
        }
    }

    public bool JoinRoom(string address, int port, string playerName)
    {
        Teardown();
        try
        {
            _client = new RemoteClient();
            _client.ConnectLobby(address, port);
            _client.SendLobby(new AoeSim.LobbyMessage { Kind = AoeSim.LobbyMessage.KindJoin, Name = playerName });
            LastError = "";
            return true;
        }
        catch (System.Exception exception)
        {
            LastError = exception.Message;
            Teardown();
            return false;
        }
    }

    public void SetProfile(string name, long color) =>
        _client?.SendLobby(new AoeSim.LobbyMessage
        {
            Kind = AoeSim.LobbyMessage.KindProfile,
            Name = name,
            Color = color,
        });

    public void SetReady(bool ready) =>
        _client?.SendLobby(new AoeSim.LobbyMessage
        {
            Kind = AoeSim.LobbyMessage.KindReady,
            Ready = ready,
        });

    public void RequestStart() =>
        _client?.SendLobby(new AoeSim.LobbyMessage { Kind = AoeSim.LobbyMessage.KindStart });

    /// <summary>从节点上摘下来、交给对局场景持有；期间离开场景树不触发断线清理。</summary>
    public void PrepareHandoff() => _handoff = true;

    public override void _ExitTree()
    {
        if (_handoff)
        {
            _handoff = false;
            return;
        }
        Teardown();
    }

    public void Leave()
    {
        Teardown();
    }

    private void Teardown()
    {
        if (_client != null)
        {
            _client.Disconnect();
            _client = null;
        }
        if (_hostServer != null)
        {
            _hostServer.Stop();
            _hostServer = null;
        }
    }

    /// <summary>给 GDScript 的房间快照：连接状态、错误、槽位与房主/自己座位。</summary>
    public Godot.Collections.Dictionary Snapshot()
    {
        AoeSim.LobbyMessage? room = _client?.Room;
        var slots = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (room != null)
        {
            foreach (AoeSim.LobbySlot slot in room.Slots)
            {
                slots.Add(new Godot.Collections.Dictionary
                {
                    { "seat", slot.Seat },
                    { "name", slot.Name },
                    { "color", slot.Color },
                    { "ready", slot.Ready },
                    { "host", slot.Seat == room.HostSeat },
                    { "mine", slot.Seat == MySeat },
                });
            }
        }
        string error = room != null && room.Kind == AoeSim.LobbyMessage.KindError
            ? room.Error
            : "";
        return new Godot.Collections.Dictionary
        {
            { "connected", Connected },
            { "started", MatchStarted },
            { "error", error },
            { "map_name", room?.MapName ?? "" },
            { "host_seat", room?.HostSeat ?? -1 },
            { "my_seat", MySeat },
            { "version", LobbyVersion },
            { "slots", slots },
        };
    }
}
