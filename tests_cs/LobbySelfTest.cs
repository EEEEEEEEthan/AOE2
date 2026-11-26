using System.Collections.Generic;
using System.Threading;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>
/// 多人大厅闭环：建房 → 第二客户端加入 → 未准备时拒绝开局 → 准备后房主开始 →
/// 两端拿到各自观察方与持续推进的快照；满员连接收到明确错误。
/// </summary>
public static class LobbySelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        var rules = new LobbyRules { MaxPlayers = 2, MapName = "阿拉伯" };
        var server = new DedicatedServer(0, rules, 60);
        var host = new RemoteClient();
        var guest = new RemoteClient();
        RemoteClient[] pair = { host, guest };
        server.Start();
        try
        {
            host.ConnectLobby("127.0.0.1", server.Port);
            host.SendLobby(new LobbyMessage { Kind = LobbyMessage.KindJoin, Name = "主机" });
            guest.ConnectLobby("127.0.0.1", server.Port);
            guest.SendLobby(new LobbyMessage { Kind = LobbyMessage.KindJoin, Name = "客人" });

            Expect(failures, WaitUntil(pair, () => SlotCount(host.Room) == 2, 3000),
                "lobby room lists both players");
            Expect(failures, SlotName(host.Room, 0) == "主机" && SlotName(host.Room, 1) == "客人",
                "lobby keeps each player's name");
            Expect(failures, host.Room?.HostSeat == 0, "first player is the host");
            Expect(failures, host.ViewerTeam == 0 && guest.ViewerTeam == 1,
                "lobby seats map to observer teams");

            host.SendLobby(new LobbyMessage { Kind = LobbyMessage.KindStart });
            Expect(failures, WaitUntil(pair, () => RoomError(host.Room).Length > 0, 2000),
                "host cannot start before the guest is ready");
            Expect(failures, !host.MatchStarted, "rejected start keeps the room in the lobby");

            guest.SendLobby(new LobbyMessage
            {
                Kind = LobbyMessage.KindProfile,
                Name = "客人",
                Color = 3,
            });
            guest.SendLobby(new LobbyMessage { Kind = LobbyMessage.KindReady, Ready = true });
            Expect(failures, WaitUntil(pair, () => SlotReady(host.Room, 1), 2000),
                "ready flag reaches the host's room state");
            Expect(failures, SlotColor(host.Room, 1) == 3, "slot color is carried in room state");

            host.SendLobby(new LobbyMessage { Kind = LobbyMessage.KindStart });
            Expect(failures, WaitUntil(
                pair,
                () => host.MatchStarted && guest.MatchStarted
                    && host.State.Latest != null && guest.State.Latest != null,
                5000), "both clients receive the static map and snapshots after start");

            long firstTick = host.State.Latest?.Tick ?? -1;
            Expect(failures, WaitUntil(
                pair, () => (host.State.Latest?.Tick ?? -1) > firstTick, 3000),
                "match snapshots keep advancing after the lobby handoff");
            Expect(failures, host.State.Map != null && guest.State.Map != null,
                "clients decode the generated map");

            var third = new RemoteClient();
            third.ConnectLobby("127.0.0.1", server.Port);
            Expect(failures, WaitUntil(
                new[] { third }, () => RoomError(third.Room) == "房间已满", 2000),
                "full room reports an explicit error to the extra client");
            third.Disconnect();
        }
        catch (System.Exception error)
        {
            failures.Add($"lobby flow threw: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            host.Disconnect();
            guest.Disconnect();
            server.Stop();
        }
        return failures;
    }

    private static int SlotCount(LobbyMessage? room) => room?.Slots.Length ?? 0;

    private static string SlotName(LobbyMessage? room, int index) =>
        room != null && index < room.Slots.Length ? room.Slots[index].Name : "";

    private static bool SlotReady(LobbyMessage? room, int index) =>
        room != null && index < room.Slots.Length && room.Slots[index].Ready;

    private static long SlotColor(LobbyMessage? room, int index) =>
        room != null && index < room.Slots.Length ? room.Slots[index].Color : -1;

    private static string RoomError(LobbyMessage? room) =>
        room != null && room.Kind == LobbyMessage.KindError ? room.Error : "";

    private static bool WaitUntil(
        RemoteClient[] clients, System.Func<bool> condition, int timeoutMilliseconds)
    {
        long deadline = System.Environment.TickCount64 + timeoutMilliseconds;
        while (System.Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }
            foreach (RemoteClient client in clients)
            {
                client.Poll();
            }
            Thread.Sleep(5);
        }
        return condition();
    }
}
