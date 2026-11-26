using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AoeSim;

using static AOE2Framework.SelfTestBase;

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
        CheckHighLatencyCommands(failures);
        CheckLocalServerParity(failures);
        CheckMultiClient(failures);
        CheckDeltaSnapshots(failures);
        CheckSnapshotDownrate(failures);
        CheckSharedSnapshotBuild(failures);
        return failures;
    }

    /// <summary>
    /// 千单位场景：关键帧后只下发变化实体与字段，客户端合并结果必须与全量快照逐实体一致，
    /// 且总字节数显著低于每 tick 全量编码。
    /// </summary>
    private static void CheckDeltaSnapshots(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(48, 48), 4242, 2, 2);
        var ids = new List<long>();
        for (int index = 0; index < 1000; index++)
        {
            long id = sim.SpawnUnit(2 + index % 44, 2 + index / 44 % 44, 0, 50, 0, 0.0, 0.0, 0.0);
            if (id < 0)
            {
                failures.Add("delta snapshot: spawn failed");
                return;
            }
            ids.Add(id);
        }

        var delta = new SnapshotDelta();
        SnapshotFrame? merged = null;
        SnapshotFrame? keyframe = null;
        long fullBytes = 0;
        long deltaBytes = 0;
        bool mismatch = false;
        for (int tick = 0; tick < 20; tick++)
        {
            SnapshotFrame full = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
            SnapshotFrame outgoing = delta.Build(full);
            fullBytes += full.Encode().Length;
            byte[] encoded = outgoing.Encode();
            deltaBytes += encoded.Length;
            SnapshotFrame decoded = SnapshotFrame.Decode(encoded, merged);
            merged = decoded.Delta && merged != null ? decoded.Merge(merged) : decoded;
            if (keyframe == null)
            {
                keyframe = outgoing;
            }
            mismatch |= !SameEntities(full, merged);
            for (int index = 0; index < ids.Count; index++)
            {
                long x = sim.World.GetValue(ids[index], SimComponents.Position, SimComponents.X);
                sim.World.SetValue(ids[index], SimComponents.Position, SimComponents.X, x + Fixed.FromInt(1));
            }
        }
        Expect(failures, keyframe != null && !keyframe.Delta, "delta snapshot: first frame is a keyframe");
        Expect(failures, !mismatch, "delta snapshot: merged client state matches full snapshot every tick");
        Expect(failures, deltaBytes * 2 < fullBytes,
            $"delta snapshot: bandwidth {deltaBytes} should be far below full {fullBytes}");
        double deltaPerFrame = deltaBytes / 20.0;
        double fullPerFrame = fullBytes / 20.0;
        System.Console.WriteLine(
            $"[REPORT] bandwidth per client: full {fullPerFrame:F0} B/frame; delta {deltaPerFrame:F0} B/frame; "
            + $"30Hz {deltaPerFrame * 30 / 1024:F1} KB/s; 15Hz {deltaPerFrame * 15 / 1024:F1} KB/s");
    }

    private static bool SameEntities(SnapshotFrame full, SnapshotFrame merged)
    {
        if (full.Entities.Length != merged.Entities.Length)
        {
            return false;
        }
        var map = new System.Collections.Generic.Dictionary<long, EntitySnapshot>(merged.Entities.Length);
        foreach (EntitySnapshot entity in merged.Entities)
        {
            map[entity.Id] = entity;
        }
        foreach (EntitySnapshot entity in full.Entities)
        {
            if (!map.TryGetValue(entity.Id, out EntitySnapshot candidate) || !candidate.SameAs(entity))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 快照降频：模拟 120Hz 下每 3 tick 才发一次快照，客户端仍持续收到快照并推进 tick，
    /// 但收到的帧数明显少于模拟 tick 数。
    /// </summary>
    private static void CheckSnapshotDownrate(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 930, 2, 6);
        sim.SpawnUnit(3, 3, 0, 50, 0, 0.0, 0.0, 0.5);
        sim.SpawnUnit(12, 12, 1, 50, 0, 0.0, 0.0, 0.5);
        var server = new DedicatedServer(0, sim, 120, 3);
        var client = new RemoteClient();
        server.Start();
        try
        {
            client.Connect("127.0.0.1", server.Port);
            long deadline = System.Environment.TickCount64 + 5000;
            while ((client.State.Latest == null || client.State.Latest.Tick < 90)
                && System.Environment.TickCount64 < deadline)
            {
                client.Poll();
                Thread.Sleep(2);
            }
            SnapshotFrame? latest = client.State.Latest;
            Expect(failures, server.SnapshotIntervalTicks == 3, "snapshot interval is configurable");
            Expect(failures, latest != null && latest.Tick >= 90,
                "downrated snapshots still advance the simulation");
            if (latest != null)
            {
                Expect(failures, client.SnapshotFrames >= 4, "downrated snapshots keep arriving");
                Expect(failures, client.SnapshotFrames <= latest.Tick / 2 + 2,
                    $"snapshot frames {client.SnapshotFrames} should be well below ticks {latest.Tick}");
            }
        }
        catch (System.Exception error)
        {
            failures.Add($"snapshot downrate threw: {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            client.Disconnect();
            server.Stop();
        }
    }

    /// <summary>
    /// 多观察者共享构建：同 tick 全量快照只建一次，按队伍裁剪结果必须与逐观察者直接构建一致；
    /// 同时输出两种做法的单 tick CPU 数据。
    /// </summary>
    private static void CheckSharedSnapshotBuild(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(48, 48), 931, 2, 2);
        for (int index = 0; index < 600; index++)
        {
            sim.SpawnUnit(2 + index % 44, 2 + index / 44 % 44, index % 2, 50, 0, 0.0, 0.0, 0.0);
        }
        var server = new SimulationServer(sim);
        server.Step();

        SnapshotFrame full = server.SnapshotFull();
        for (long team = 0; team < 2; team++)
        {
            SnapshotFrame shared = server.SnapshotFor(full, team);
            SnapshotFrame direct = server.Snapshot(team);
            Expect(failures, SameEntities(direct, shared),
                $"shared snapshot matches direct build for team {team}");
            Expect(failures, SameBytes(direct.VisionStates, shared.VisionStates),
                $"shared snapshot keeps team {team} vision bitmap");
        }

        const int iterations = 30;
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < iterations; index++)
        {
            SnapshotFrame sharedFull = server.SnapshotFull();
            server.SnapshotFor(sharedFull, 0);
            server.SnapshotFor(sharedFull, 1);
        }
        watch.Stop();
        double sharedMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        watch.Restart();
        for (int index = 0; index < iterations; index++)
        {
            server.Snapshot(0);
            server.Snapshot(1);
        }
        watch.Stop();
        double directMicros = watch.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        System.Console.WriteLine(
            $"[REPORT] 2 observers snapshot build per tick: shared {sharedMicros:F0} us; "
            + $"direct {directMicros:F0} us; saving {directMicros - sharedMicros:F0} us");
        Expect(failures, sharedMicros < directMicros * 2.0,
            "shared snapshot build stays within a sane CPU bound");
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

    /// <summary>
    /// 单向延迟回归：客户端按快照回合加提前量提交，帧延迟到达服务器后仍落在未来回合，
    /// 指令被接受并生效，不会因回合已过被静默丢弃。
    /// </summary>
    private static void CheckHighLatencyCommands(List<string> failures)
    {
        const int ticksPerSecond = 30;
        const int oneWayMs = 300;
        int travelTicks = oneWayMs * ticksPerSecond / 1000;
        int ticksPerTurn = 6;
        var sim = new GameSim(new NavGrid(24, 24), 910, 2, ticksPerTurn);
        long unit = sim.SpawnUnit(3, 3, 0, 50, 0, 0.0, 0.0, 0.5);
        var server = new SimulationServer(sim);
        long startX = PositionX(sim, unit);
        long startY = PositionY(sim, unit);

        var pending = new Queue<(long DeliverTick, CommandFrame Frame)>();
        bool sent = false;
        long plannedTurn = -1;
        long turnAtDelivery = -1;
        for (int tick = 0; tick < 90; tick++)
        {
            while (pending.Count > 0 && pending.Peek().DeliverTick <= tick)
            {
                turnAtDelivery = server.Turn;
                server.Enqueue(pending.Dequeue().Frame, 0);
            }
            if (!sent)
            {
                plannedTurn = InputTiming.PlanTurn(server.Turn, InputTiming.DefaultLookaheadTurns);
                pending.Enqueue((tick + travelTicks, new CommandFrame
                {
                    Player = 0,
                    Turn = plannedTurn,
                    Commands = new[] { new LockstepCommand(0, LockstepCommand.KindMove, unit, 14, 14) },
                }));
                sent = true;
            }
            server.Step();
        }

        Expect(failures, server.AcceptedCommands == 1, "delayed command is accepted, not silently dropped");
        Expect(failures, server.RejectedCommands == 0, "300ms delay adds no rejected command");
        Expect(failures, plannedTurn > turnAtDelivery, "planned turn stays ahead of the server after 300ms");
        Expect(failures,
            plannedTurn <= turnAtDelivery + InputTiming.MaxLookaheadTurns,
            "planned turn stays inside the server lookahead window");
        Expect(failures,
            PositionX(sim, unit) != startX || PositionY(sim, unit) != startY,
            "delayed command takes effect within a bounded number of turns");
    }

    /// <summary>本机直推与经服务器回合边界应用走同一套推进语义，得到相同校验和。</summary>
    private static void CheckLocalServerParity(List<string> failures)
    {
        Expect(failures, LocalParityChecksum(viaServer: false) == LocalParityChecksum(viaServer: true),
            "local and server-driven advance produce the same checksum");
    }

    private static long LocalParityChecksum(bool viaServer)
    {
        var sim = new GameSim(new NavGrid(24, 24), 911, 2, 6);
        long blue = sim.SpawnUnit(3, 3, 0, 50, 0, 0.0, 0.0, 0.2);
        long red = sim.SpawnUnit(3, 16, 1, 50, 0, 0.0, 0.0, 0.2);
        var commands = new[]
        {
            new LockstepCommand(0, LockstepCommand.KindMove, blue, 8, 10),
            new LockstepCommand(1, LockstepCommand.KindMove, red, 8, 9),
        };
        if (viaServer)
        {
            var server = new SimulationServer(sim);
            server.Enqueue(commands);
            for (int tick = 0; tick < 60; tick++)
            {
                server.Step();
            }
        }
        else
        {
            foreach (LockstepCommand command in commands)
            {
                sim.ApplyCommand(command);
            }
            for (int tick = 0; tick < 60; tick++)
            {
                sim.SimulateTick();
            }
        }
        return sim.Checksum();
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
}
