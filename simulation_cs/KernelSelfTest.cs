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
        CheckAttack(failures);
        CheckGather(failures);
        CheckMonk(failures);
        CheckRelic(failures);
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

    private static void CheckAttack(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 51);
        long attacker = sim.SpawnUnit(2, 2, 0, 100, 10, 1.0, 0.5);
        long victim = sim.SpawnUnit(12, 2, 1, 30, 0, 1.0, 0.0);
        sim.IssueAttack(attacker, victim);
        Expect(failures, sim.World.HasComponent(attacker, SimComponents.AttackTarget), "attack locks explicit target");
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "attack chases and kills explicit target");

        var friendly = new GameSim(new NavGrid(12, 12), 52);
        long ally = friendly.SpawnUnit(4, 4, 0, 60, 5, 1.2, 0.0);
        long friend = friendly.SpawnUnit(5, 4, 0, 60, 5, 1.2, 0.0);
        friendly.IssueAttack(ally, friend);
        Expect(failures, !friendly.World.HasComponent(ally, SimComponents.AttackTarget), "attack ignores friendly target");

        var priority = new GameSim(new NavGrid(12, 12), 53);
        long hunter = priority.SpawnUnit(5, 5, 0, 100, 10, 2.0, 0.0);
        long near = priority.SpawnUnit(5, 6, 1, 100, 0, 1.0, 0.0);
        long far = priority.SpawnUnit(6, 6, 1, 100, 0, 1.0, 0.0);
        priority.IssueAttack(hunter, far);
        priority.SimulateTick();
        long farHp = priority.World.GetValue(far, SimComponents.Health, SimComponents.Hp);
        long nearHp = priority.World.GetValue(near, SimComponents.Health, SimComponents.Hp);
        Expect(failures, farHp < 100 && nearHp == 100, "explicit attack beats nearest auto target");

        var release = new GameSim(new NavGrid(20, 20), 54);
        long chaser = release.SpawnUnit(2, 2, 0, 100, 10, 1.0, 0.5);
        long doomed = release.SpawnUnit(10, 2, 1, 30, 0, 1.0, 0.0);
        release.IssueAttack(chaser, doomed);
        release.World.Despawn(doomed);
        release.SimulateTick();
        Expect(failures, !release.World.HasComponent(chaser, SimComponents.AttackTarget), "attack clears lock when target gone");

        Expect(failures, AttackDeterminismChecksum() == AttackDeterminismChecksum(), "attack lockstep deterministic");
        Expect(failures, AttackDeterminismChecksum() != 0, "attack checksum populated");
    }

    private static long AttackDeterminismChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 61, 2, 6);
        long attacker = sim.SpawnUnit(3, 3, 0, 60, 8, 1.2, 0.15);
        long victim = sim.SpawnUnit(12, 3, 1, 60, 8, 1.2, 0.0);
        sim.IssueAttack(attacker, victim);
        long checksum = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
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

    private static void CheckMonk(List<string> failures)
    {
        var heal = new GameSim(new NavGrid(16, 16), 101);
        long healer = heal.SpawnMonk(5, 5, 0, 60, 0.0, 10, 10, 30, 1.5, 0);
        long ally = heal.SpawnUnit(5, 6, 0, 100, 0, 0.0, 0.0);
        heal.World.SetValue(ally, SimComponents.Health, SimComponents.Hp, 40);
        heal.IssueHeal(healer, ally);
        for (int tick = 0; tick < 3; tick++)
        {
            heal.SimulateTick();
        }
        Expect(failures,
            heal.World.GetValue(ally, SimComponents.Health, SimComponents.Hp) == 70,
            "monk heals friendly up to max");
        for (int tick = 0; tick < 20; tick++)
        {
            heal.SimulateTick();
        }
        Expect(failures,
            heal.World.GetValue(ally, SimComponents.Health, SimComponents.Hp) == 100,
            "monk heal capped at max hp");

        long enemy = heal.SpawnUnit(6, 5, 1, 100, 0, 0.0, 0.0);
        heal.World.SetValue(enemy, SimComponents.Health, SimComponents.Hp, 40);
        heal.IssueHeal(healer, enemy);
        Expect(failures,
            heal.World.GetValue(healer, SimComponents.Monk, SimComponents.State) == MonkSystem.StateIdle,
            "heal ignores enemy target");

        var convert = new GameSim(new NavGrid(16, 16), 102);
        long monk = convert.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long victim = convert.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0);
        convert.IssueConvert(monk, victim);
        for (int tick = 0; tick < 3; tick++)
        {
            convert.SimulateTick();
        }
        Expect(failures,
            convert.World.GetValue(victim, SimComponents.Team, SimComponents.TeamId) == 0,
            "convert flips target team at threshold");
        Expect(failures,
            convert.World.GetValue(monk, SimComponents.Monk, SimComponents.State) == MonkSystem.StateIdle,
            "convert resets monk after success");

        var resist = new GameSim(new NavGrid(16, 16), 103);
        long converter = resist.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long resistant = resist.SpawnMonk(5, 6, 1, 60, 0.0, 0, 10, 30, 1.5, 25);
        resist.IssueConvert(converter, resistant);
        for (int tick = 0; tick < 5; tick++)
        {
            resist.SimulateTick();
        }
        Expect(failures,
            resist.World.GetValue(resistant, SimComponents.Team, SimComponents.TeamId) == 1,
            "conversion resistance slows conversion");
        for (int tick = 0; tick < 40; tick++)
        {
            resist.SimulateTick();
        }
        Expect(failures,
            resist.World.GetValue(resistant, SimComponents.Team, SimComponents.TeamId) == 0,
            "resistant target still converts eventually");

        var interrupt = new GameSim(new NavGrid(16, 16), 104);
        long damaged = interrupt.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long prey = interrupt.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0);
        interrupt.IssueConvert(damaged, prey);
        interrupt.SimulateTick();
        long progressBefore = interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress);
        interrupt.World.SetValue(damaged, SimComponents.Health, SimComponents.Hp, 50);
        interrupt.SimulateTick();
        long progressAfterHit = interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress);
        Expect(failures, progressBefore == 10, "convert progress accumulates");
        Expect(failures, progressAfterHit == 10, "monk damage resets convert progress");

        interrupt.World.SetValue(
            prey,
            SimComponents.Position,
            SimComponents.X,
            AoeSim.Fixed.FromInt(14));
        interrupt.SimulateTick();
        Expect(failures,
            interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress) == 0,
            "target leaving range resets convert progress");

        Expect(failures, MonkDeterminismChecksum() == MonkDeterminismChecksum(), "monk behavior deterministic");
    }

    private static long MonkDeterminismChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 111, 2, 6);
        long first = sim.SpawnMonk(4, 5, 0, 60, 0.1, 5, 6, 24, 2.0, 0);
        long second = sim.SpawnMonk(4, 8, 0, 60, 0.1, 5, 6, 24, 2.0, 0);
        long patient = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0);
        sim.World.SetValue(patient, SimComponents.Health, SimComponents.Hp, 30);
        long foe = sim.SpawnUnit(10, 8, 1, 100, 0, 0.0, 0.0);
        sim.IssueHeal(first, patient);
        sim.IssueConvert(second, foe);
        long checksum = 0;
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
    }

    private static void CheckRelic(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 201);
        long slot = sim.SpawnRelicSlot(7, 5, 0, 1, 5);
        long relic = sim.SpawnRelic(5, 6);
        long monk = sim.SpawnMonk(5, 5, 0, 60, 0.5, 0, 0, 0, 2.0, 0);
        sim.IssuePickupRelic(monk, relic);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) == monk,
            "monk picks up relic");
        Expect(failures, sim.RelicCountOf(0) == 1, "carried relic counts for team");
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Position, SimComponents.X)
                == sim.World.GetValue(monk, SimComponents.Position, SimComponents.X),
            "carried relic follows monk position");

        sim.IssueDepositRelic(monk, slot);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.StoredIn) == slot,
            "monk deposits relic into slot");
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) == -1,
            "deposited relic no longer carried");
        Expect(failures, sim.RelicCountOf(0) == 1, "stored relic counts for slot team");

        long goldBefore = sim.Resource(0, GameSim.ResourceGold);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        long goldAfter = sim.Resource(0, GameSim.ResourceGold);
        Expect(failures, goldAfter - goldBefore == 2, "stored relic generates gold per period");

        long second = sim.SpawnRelic(6, 6);
        sim.World.SetValue(second, SimComponents.Relic, SimComponents.Carrier, -1);
        sim.World.SetValue(second, SimComponents.Relic, SimComponents.StoredIn, slot);
        sim.SimulateTick();
        Expect(failures, sim.RelicCountOf(0) == 2, "multiple relics counted");
        long goldTwoBefore = sim.Resource(0, GameSim.ResourceGold);
        for (int tick = 0; tick < 5; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.Resource(0, GameSim.ResourceGold) - goldTwoBefore == 2,
            "gold generation stacks linearly with relics");
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

        var attackCommand = new LockstepCommand(1, LockstepCommand.KindAttack, 5, 0, 0, 99, -1);
        LockstepCommand restoredAttack = LockstepCommand.FromArray(attackCommand.ToArray());
        Expect(failures,
            restoredAttack.Kind == LockstepCommand.KindAttack && restoredAttack.Entity == 5
            && restoredAttack.TargetEntity == 99,
            "lockstep attack command roundtrip");

        Expect(failures,
            new LockstepCommand(0, LockstepCommand.KindHeal, 3, 0, 0, 8, -1).ToArray().Length == 7,
            "monk command serialized with fixed width");
        Expect(failures, LockstepCommand.KindHeal == 5 && LockstepCommand.KindConvert == 6
            && LockstepCommand.KindPickupRelic == 7 && LockstepCommand.KindDepositRelic == 8,
            "monk command kinds stable");
    }
}
