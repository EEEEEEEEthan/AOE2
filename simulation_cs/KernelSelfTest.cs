using System.Collections.Generic;
using AoeSim;
using Godot;

namespace AOE2Framework;

/// <summary>把原 GDScript 用例的语义断言移植到 C# 内核；返回空数组表示全过。</summary>
public static class KernelSelfTest
{
    public static Godot.Collections.Array<string> Run()
    {
        var failures = new List<string>();
        CheckFixed(failures);
        CheckRng(failures);
        CheckEcs(failures);
        CheckPathfinding(failures);
        CheckCombat(failures);
        CheckGather(failures);
        CheckLockstep(failures);
        var result = new Godot.Collections.Array<string>();
        foreach (string failure in failures)
        {
            result.Add(failure);
        }
        return result;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static void CheckFixed(List<string> failures)
    {
        Expect(failures, Fixed.ToInt(Fixed.FromInt(7)) == 7, "fixed int roundtrip");
        Expect(failures, Fixed.ToFloat(Fixed.FromFloat(1.5)) == 1.5, "fixed float roundtrip");
        Expect(failures, Fixed.Mul(Fixed.FromFloat(1.5), Fixed.FromFloat(2.0)) == Fixed.FromFloat(3.0), "fixed multiply");
        Expect(failures, Fixed.Div(Fixed.FromInt(1), Fixed.FromInt(2)) == Fixed.Half, "fixed divide");
        Expect(failures, Fixed.Sqrt(Fixed.FromInt(4)) == Fixed.FromInt(2), "fixed sqrt");
        var point = FixedVec2.FromTiles(3.0, 4.0);
        Expect(failures, point.DistanceSq(new FixedVec2(0, 0)) == Fixed.FromInt(25), "pythagorean distance");
        Expect(failures, point.CellX() == 3 && point.CellY() == 4, "fixed cell index");
    }

    private static void CheckRng(List<string> failures)
    {
        var first = new SimRng(12345);
        var second = new SimRng(12345);
        bool same = true;
        for (int draw = 0; draw < 64; draw++)
        {
            same &= first.NextU32() == second.NextU32();
        }
        Expect(failures, same, "rng same seed identical");
        var source = new SimRng(42);
        for (int draw = 0; draw < 10; draw++)
        {
            source.NextU32();
        }
        SimRng clone = source.Clone();
        bool cloneMatches = true;
        for (int draw = 0; draw < 20; draw++)
        {
            cloneMatches &= source.NextU32() == clone.NextU32();
        }
        Expect(failures, cloneMatches, "rng clone continues stream");
        var ranged = new SimRng(7);
        bool inRange = true;
        for (int draw = 0; draw < 500; draw++)
        {
            long value = ranged.NextIntRange(3, 9);
            inRange &= value >= 3 && value <= 9;
        }
        Expect(failures, inRange, "rng range respected");
    }

    private static void CheckEcs(List<string> failures)
    {
        var world = new SimWorld(1);
        long a = world.Spawn();
        long b = world.Spawn();
        Expect(failures, a != b, "ecs unique ids");
        world.SetValue(a, SimComponents.Position, SimComponents.X, Fixed.FromInt(5));
        Expect(failures, world.GetValue(a, SimComponents.Position, SimComponents.X) == Fixed.FromInt(5), "ecs set get");
        world.Attach(a, SimComponents.Health);
        Expect(failures, world.HasComponent(a, SimComponents.Health) && !world.HasComponent(b, SimComponents.Health), "ecs component isolation");
        world.Despawn(b);
        Expect(failures, !world.IsAlive(b) && !world.HasComponent(b, SimComponents.Position), "ecs despawn clears");
        Expect(failures, SampleChecksum() == SampleChecksum(), "ecs checksum deterministic");
        Expect(failures, SampleChecksum() != SampleChecksumWithExtra(), "ecs checksum tracks state");
    }

    private static long SampleChecksumWithExtra()
    {
        var world = new SimWorld(5);
        for (int index = 0; index < 12; index++)
        {
            long id = world.Spawn();
            world.Attach(id, SimComponents.Position);
            world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.FromInt(index));
            world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.FromInt(index * 2));
        }
        world.Tick = 3;
        world.SetValue(4, SimComponents.Position, SimComponents.X, Fixed.FromInt(400));
        return world.Checksum();
    }

    private static long SampleChecksum()
    {
        var world = new SimWorld(5);
        for (int index = 0; index < 12; index++)
        {
            long id = world.Spawn();
            world.Attach(id, SimComponents.Position);
            world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.FromInt(index));
            world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.FromInt(index * 2));
        }
        world.Tick = 3;
        return world.Checksum();
    }

    private static void CheckPathfinding(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var field = new FlowField(grid);
        Expect(failures, field.Compute(grid, 10, 10) && field.Reachable(2, 2), "flow field reachable goal");
        int x = 2;
        int y = 2;
        int steps = 0;
        while (field.CostAt(x, y) > 0 && steps < 200)
        {
            GridDirection direction = field.DirectionAt(x, y);
            x += direction.X;
            y += direction.Y;
            steps += 1;
        }
        Expect(failures, x == 10 && y == 10 && field.CostAt(x, y) == 0, "flow field greedy descent");

        var walled = new NavGrid(16, 16);
        for (int wallY = 0; wallY < 16; wallY++)
        {
            if (wallY != 3)
            {
                walled.SetBlocked(8, wallY, true);
            }
        }
        var sim = new GameSim(walled, 12);
        long id = sim.SpawnUnit(2, 8, 0, 50, 0, 0.0, 0.6);
        sim.IssueMove(id, 13, 8);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.X)) == 13
            && Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.Y)) == 8,
            "unit routes around wall");
    }

    private static void CheckCombat(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(12, 12), 21);
        long attacker = sim.SpawnUnit(5, 5, 0, 100, 10, 1.2, 0.0);
        long victim = sim.SpawnUnit(6, 5, 1, 30, 10, 1.2, 0.0);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "combat enemy dies");

        var friendly = new GameSim(new NavGrid(12, 12), 22);
        friendly.SpawnUnit(5, 5, 0, 40, 10, 1.2, 0.0);
        friendly.SpawnUnit(6, 5, 0, 40, 10, 1.2, 0.0);
        for (int tick = 0; tick < 40; tick++)
        {
            friendly.SimulateTick();
        }
        int aliveCount = 0;
        foreach (long id in friendly.World.Entities())
        {
            if (friendly.World.IsAlive(id))
            {
                aliveCount += 1;
            }
        }
        Expect(failures, aliveCount == 2, "combat friendly fire disabled");
    }

    private static void CheckGather(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 42);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceWood, 100);
        long drop = sim.SpawnDropSite(10, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 4, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        long inventory = sim.Resource(0, GameSim.ResourceWood);
        long carry = sim.World.GetValue(villager, SimComponents.Gather, SimComponents.CarryAmount);
        long remaining = sim.World.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining);
        Expect(failures, inventory > 0, "gather delivers to inventory");
        Expect(failures, inventory + carry + remaining == 100, "gather conserves resources");
    }

    private static void CheckLockstep(List<string> failures)
    {
        var lockstep = new Lockstep(2, 6);
        var first = new LockstepCommand(0, LockstepCommand.KindMove, 1, 4, 4);
        Expect(failures, lockstep.SubmitTurn(0, 0, new List<LockstepCommand> { first }), "lockstep first accepted");
        Expect(failures, !lockstep.CanAdvance(), "lockstep waits for all");
        Expect(failures, !lockstep.SubmitTurn(0, 0, new List<LockstepCommand>()), "lockstep duplicate rejected");
        Expect(failures, lockstep.SubmitTurn(0, 1, new List<LockstepCommand>()), "lockstep second accepted");
        Expect(failures, lockstep.CanAdvance(), "lockstep advances when ready");

        var original = new LockstepCommand(1, LockstepCommand.KindGather, 12, 0, 0, 30, 31);
        LockstepCommand restored = LockstepCommand.FromArray(original.ToArray());
        Expect(failures,
            restored.Player == 1 && restored.Kind == LockstepCommand.KindGather && restored.Entity == 12
            && restored.TargetEntity == 30 && restored.DropEntity == 31,
            "lockstep command roundtrip");
    }
}
