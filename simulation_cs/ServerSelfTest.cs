using System.Collections.Generic;
using System.Threading;
using AoeSim;

namespace AOE2Framework;

/// <summary>
/// 服务器权威自测：多连接各拿自己的 observer team 与视野快照；连接身份绑定覆盖客户端自报玩家；
/// 越权指令被拒绝；锁步按回合边界应用指令并推进回合号。
/// </summary>
public static class ServerSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckCommandAuthority(failures);
        CheckConnectionBinding(failures);
        CheckLockstepTurns(failures);
        CheckMultiClient(failures);
        return failures;
    }

    private static void CheckCommandAuthority(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 707, 2, 2);
        long blue = sim.SpawnUnit(3, 3, 0, 50, 0, 0.0, 0.0, 0.5);
        long red = sim.SpawnUnit(12, 12, 1, 50, 0, 0.0, 0.0, 0.5);
        long checksum = sim.Checksum();

        var forbidden = new LockstepCommand(0, LockstepCommand.KindMove, red, 8, 8);
        Expect(failures, !sim.ApplyCommand(forbidden), "command targeting opponent unit is rejected");
        Expect(failures, sim.Checksum() == checksum, "rejected command leaves world checksum unchanged");

        long startX = PositionX(sim, blue);
        long startY = PositionY(sim, blue);
        long redStartX = PositionX(sim, red);
        long redStartY = PositionY(sim, red);
        var own = new LockstepCommand(0, LockstepCommand.KindMove, blue, 6, 6);
        Expect(failures, sim.ApplyCommand(own), "command targeting own unit is accepted");
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            PositionX(sim, blue) != startX || PositionY(sim, blue) != startY,
            "accepted move command takes effect");
        Expect(failures, PositionX(sim, red) == redStartX && PositionY(sim, red) == redStartY,
            "opponent unit never moved from a rejected command");
    }

    private static void CheckConnectionBinding(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 708, 2, 4);
        long blue = sim.SpawnUnit(3, 3, 0, 50, 0, 0.0, 0.0, 0.5);
        long red = sim.SpawnUnit(12, 12, 1, 50, 0, 0.0, 0.0, 0.5);
        var server = new SimulationServer(sim);
        var frame = new CommandFrame
        {
            // 客户端自报为对手玩家 1，服务器必须用连接对应的 team 0 覆盖它。
            Player = 1,
            Turn = server.Turn,
            Commands = new[]
            {
                new LockstepCommand(1, LockstepCommand.KindMove, blue, 6, 6),
                new LockstepCommand(1, LockstepCommand.KindMove, red, 8, 8),
            },
        };
        server.Enqueue(frame, 0);
        Expect(failures, server.AcceptedCommands == 1, "source player overwrites self-reported player");
        Expect(failures, server.RejectedCommands == 1, "opponent unit command rejected at enqueue");

        long blueX = PositionX(sim, blue);
        long blueY = PositionY(sim, blue);
        long redX = PositionX(sim, red);
        long redY = PositionY(sim, red);
        for (int tick = 0; tick < 10; tick++)
        {
            server.Step();
        }
        Expect(failures, PositionX(sim, blue) != blueX || PositionY(sim, blue) != blueY,
            "bound own command still takes effect");
        Expect(failures, PositionX(sim, red) == redX && PositionY(sim, red) == redY,
            "spoofed command cannot move the opponent unit");
    }

    private static void CheckLockstepTurns(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 709, 2, 3);
        long unit = sim.SpawnUnit(2, 2, 0, 50, 0, 0.0, 0.0, 0.0);
        var server = new SimulationServer(sim);
        Expect(failures, server.Turn == 0, "lockstep turn starts at zero");

        var frame = new CommandFrame
        {
            Player = 0,
            Turn = server.Turn,
            Commands = new[] { new LockstepCommand(0, LockstepCommand.KindMove, unit, 8, 8) },
        };
        server.Enqueue(frame, 0);
        server.Step();
        Expect(failures, server.Turn == 0, "turn does not advance before ticksPerTurn ticks");
        server.Step();
        server.Step();
        Expect(failures, server.Turn == 1, "turn advances at the fixed tick boundary");

        long rejected = server.RejectedCommands;
        var stale = new CommandFrame
        {
            Player = 0,
            Turn = 0,
            Commands = new[] { new LockstepCommand(0, LockstepCommand.KindMove, unit, 1, 1) },
        };
        server.Enqueue(stale, 0);
        Expect(failures, server.RejectedCommands == rejected + 1, "stale turn frame is rejected");

        long accepted = server.AcceptedCommands;
        var future = new CommandFrame
        {
            Player = 0,
            Turn = server.Turn + 2,
            Commands = new[] { new LockstepCommand(0, LockstepCommand.KindMove, unit, 5, 5) },
        };
        server.Enqueue(future, 0);
        Expect(failures, server.AcceptedCommands == accepted + 1, "future turn frame is queued");
    }

    private static void CheckMultiClient(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 909, 2, 6);
        long blue = sim.SpawnUnit(4, 4, 0, 50, 0, 0.0, 0.0, 0.0);
        long red = sim.SpawnUnit(19, 19, 1, 50, 0, 0.0, 0.0, 0.0);

        var server = new DedicatedServer(0, sim, 60);
        var first = new RemoteClient();
        var second = new RemoteClient();
        var third = new RemoteClient();
        server.Start();
        try
        {
            first.Connect("127.0.0.1", server.Port);
            second.Connect("127.0.0.1", server.Port);

            Expect(failures,
                first.ViewerTeam != second.ViewerTeam
                && System.Math.Min(first.ViewerTeam, second.ViewerTeam) == 0
                && System.Math.Max(first.ViewerTeam, second.ViewerTeam) == 1,
                "handshake assigns distinct observer teams 0 and 1");

            bool thirdRejected = false;
            try
            {
                third.Connect("127.0.0.1", server.Port);
            }
            catch (System.Exception)
            {
                thirdRejected = true;
            }
            Expect(failures, thirdRejected, "connection beyond player count is refused");
            third.Disconnect();

            bool received = WaitForSnapshots(first, second, 3000);
            Expect(failures, received, "both connections receive snapshots");
            if (received)
            {
                CheckViewer(failures, first, blue, red);
                CheckViewer(failures, second, red, blue);
                Expect(failures,
                    !SameBytes(first.State.Latest!.VisionStates, second.State.Latest!.VisionStates),
                    "each connection carries its own vision bitmap");
            }
        }
        catch (System.Exception error)
        {
            failures.Add($"dedicated server integration threw: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            first.Disconnect();
            second.Disconnect();
            server.Stop();
        }
    }

    private static void CheckViewer(
        List<string> failures, RemoteClient client, long ownUnit, long enemyUnit)
    {
        SnapshotFrame? latest = client.State.Latest;
        if (latest == null)
        {
            failures.Add($"team {client.ViewerTeam} has no snapshot");
            return;
        }
        bool hasOwn = false;
        bool hasEnemy = false;
        foreach (EntitySnapshot entity in latest.Entities)
        {
            hasOwn |= entity.Id == ownUnit;
            hasEnemy |= entity.Id == enemyUnit;
        }
        Expect(failures, hasOwn, $"team {client.ViewerTeam} snapshot keeps its own unit");
        Expect(failures, !hasEnemy, $"team {client.ViewerTeam} snapshot hides the other team");
    }

    private static long PositionX(GameSim sim, long entity) =>
        sim.World.GetValue(entity, SimComponents.Position, SimComponents.X);

    private static long PositionY(GameSim sim, long entity) =>
        sim.World.GetValue(entity, SimComponents.Position, SimComponents.Y);

    private static bool WaitForSnapshots(RemoteClient first, RemoteClient second, int timeoutMilliseconds)
    {
        long deadline = System.Environment.TickCount64 + timeoutMilliseconds;
        while (System.Environment.TickCount64 < deadline)
        {
            first.Poll();
            second.Poll();
            if (first.State.Latest != null && second.State.Latest != null
                && first.State.Latest.Tick > 0 && second.State.Latest.Tick > 0)
            {
                return true;
            }
            Thread.Sleep(5);
        }
        return first.State.Latest != null && second.State.Latest != null;
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }
}
