using System;
using System.Collections.Generic;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>指令状态机语义断言；返回空列表表示全过。</summary>
public static class OrderSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckAttackMove(failures);
        CheckPatrol(failures);
        CheckGuard(failures);
        CheckFollow(failures);
        CheckHold(failures);
        CheckRally(failures);
        CheckQueue(failures);
        CheckTargetDeath(failures);
        CheckDeterminism(failures);
        return failures;
    }

    private static long CellX(GameSim sim, long id) =>
        Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.X));

    private static long CellY(GameSim sim, long id) =>
        Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.Y));

    private static void CheckAttackMove(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 301);
        long attacker = sim.SpawnUnit(2, 10, 0, 100, 10, 1.0, 1.0, 0.5);
        long victim = sim.SpawnUnit(8, 10, 1, 30, 0, 1.0, 1.0, 0.0);
        sim.IssueAttackMove(attacker, 20, 10);
        Expect(failures,
            sim.World.GetValue(attacker, SimComponents.Order, SimComponents.OrderType) == OrderSystem.TypeAttackMove,
            "attack move attaches order type");
        bool engaged = false;
        for (int tick = 0; tick < 500; tick++)
        {
            sim.SimulateTick();
            if (sim.World.GetValue(attacker, SimComponents.Order, SimComponents.OrderState) == OrderSystem.StateEngaging)
            {
                engaged = true;
            }
        }
        Expect(failures, engaged, "attack move engages enemy on the way");
        Expect(failures, !sim.World.IsAlive(victim), "attack move kills engaged enemy");
        Expect(failures, CellX(sim, attacker) == 20 && CellY(sim, attacker) == 10, "attack move resumes to destination");
    }

    private static void CheckPatrol(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 302);
        long id = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0, 0.5);
        sim.IssuePatrol(id, 3, 5, 7, 5);
        bool reachedLow = false;
        bool reachedHigh = false;
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
            long x = CellX(sim, id);
            reachedLow |= x <= 3;
            reachedHigh |= x >= 7;
        }
        Expect(failures, reachedLow && reachedHigh, "patrol reaches both endpoints");
    }

    private static void CheckGuard(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 303);
        long guard = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0, 0.5);
        sim.IssueGuard(guard, -1);
        sim.World.SetValue(guard, SimComponents.Position, SimComponents.X, Fixed.FromInt(15));
        sim.World.SetValue(guard, SimComponents.Position, SimComponents.Y, Fixed.FromInt(15));
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        long distance = Math.Max(Math.Abs(CellX(sim, guard) - 5), Math.Abs(CellY(sim, guard) - 5));
        Expect(failures, distance <= OrderSystem.DefaultLeashCells, "guard returns inside leash radius");
    }

    private static void CheckFollow(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 304);
        long follower = sim.SpawnUnit(2, 5, 0, 100, 0, 0.0, 0.0, 0.5);
        long leader = sim.SpawnUnit(3, 5, 0, 100, 0, 0.0, 0.0, 0.5);
        sim.IssueFollow(follower, leader);
        sim.IssueMove(leader, 14, 5);
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        long distance = Math.Max(Math.Abs(CellX(sim, follower) - CellX(sim, leader)),
            Math.Abs(CellY(sim, follower) - CellY(sim, leader)));
        Expect(failures, CellX(sim, leader) == 14, "follow leader reaches move target");
        Expect(failures, distance <= OrderSystem.DefaultFollowCells, "follower keeps follow distance");
    }

    private static void CheckHold(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 305);
        long holder = sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.5);
        sim.SpawnUnit(9, 5, 1, 40, 0, 1.0, 1.0, 0.0);
        sim.IssueHold(holder);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(holder, SimComponents.Move, SimComponents.Active) == 0,
            "hold keeps unit stationary");
        Expect(failures, CellX(sim, holder) == 5 && CellY(sim, holder) == 5, "hold unit does not chase enemy");
    }

    private static void CheckRally(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 306);
        long building = sim.SpawnBuilding(5, 5, 0, 100);
        long unit = sim.SpawnUnit(5, 7, 0, 100, 0, 0.0, 0.0, 0.5);
        sim.IssueSetRally(building, 16, 16, -1);
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Rally, SimComponents.RallyX) == 16,
            "rally point stored on building");
        Expect(failures, sim.ApplyRallyTo(building, unit), "rally applies to produced unit");
        Expect(failures,
            sim.World.GetValue(unit, SimComponents.Order, SimComponents.OrderType) == OrderSystem.TypeAttackMove,
            "rally issues attack move on unit");
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CellX(sim, unit) == 16 && CellY(sim, unit) == 16, "rallied unit reaches rally point");
    }

    private static void CheckQueue(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 307);
        long id = sim.SpawnUnit(2, 2, 0, 100, 0, 0.0, 0.0, 0.5);
        OrderSystem.IssueAttackMove(sim.World, id, 6, 2, 0, false);
        OrderSystem.IssueAttackMove(sim.World, id, 16, 2, 0, true);
        Expect(failures,
            sim.World.GetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[0]) == OrderSystem.TypeAttackMove,
            "queued order stored in queue slot");
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CellX(sim, id) == 16 && CellY(sim, id) == 2, "queued order runs after current finishes");
    }

    private static void CheckTargetDeath(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 308);
        long follower = sim.SpawnUnit(2, 2, 0, 100, 0, 0.0, 0.0, 0.3);
        long leader = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0, 0.0);
        sim.IssueFollow(follower, leader);
        sim.World.Despawn(leader);
        sim.SimulateTick();
        Expect(failures,
            sim.World.GetValue(follower, SimComponents.Order, SimComponents.OrderType) == OrderSystem.TypeNone,
            "follow cancels when target dies");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        Expect(failures, OrderChecksum() == OrderChecksum(), "order scenario deterministic");
        Expect(failures, OrderChecksum() != 0, "order scenario checksum populated");
    }

    private static long OrderChecksum()
    {
        var sim = new GameSim(new NavGrid(24, 24), 309, 2, 6);
        long attacker = sim.SpawnUnit(2, 10, 0, 100, 8, 1.0, 1.0, 0.4);
        long victim = sim.SpawnUnit(7, 10, 1, 40, 6, 1.0, 1.0, 0.0);
        long patroller = sim.SpawnUnit(3, 3, 0, 80, 4, 1.0, 1.0, 0.4);
        long guarder = sim.SpawnUnit(4, 5, 0, 80, 4, 1.0, 1.0, 0.4);
        sim.IssueAttackMove(attacker, 20, 10);
        sim.IssuePatrol(patroller, 3, 2, 3, 12);
        sim.IssueGuard(guarder, attacker);
        long checksum = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
    }
}
