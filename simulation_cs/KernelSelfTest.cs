using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>把原 GDScript 用例的语义断言移植到 C# 内核；返回空列表表示全过。</summary>
public static class KernelSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckFixed(failures);
        CheckRng(failures);
        CheckEcs(failures);
        CheckPathfinding(failures);
        CheckFlowFieldCache(failures);
        CheckCombat(failures);
        CheckUnitTypes(failures);
        CheckOccupancy(failures);
        CheckGridRange(failures);
        CheckProjectile(failures);
        CheckAttack(failures);
        CheckStance(failures);
        CheckGather(failures);
        CheckMonk(failures);
        CheckRelic(failures);
        CheckLockstep(failures);
        CheckVision(failures);
        CheckTech(failures);
        CheckTerrain(failures);
        return failures;
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
        long id = sim.SpawnUnit(2, 8, 0, 50, 0, 0.0, 0.0, 0.6);
        sim.IssueMove(id, 13, 8);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.X)) == 13
            && Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.Y)) == 8,
            "unit routes around wall");

        var gapField = new FlowField(walled);
        Expect(failures, gapField.Compute(walled, 13, 8) && gapField.Reachable(2, 8),
            "flow field crosses single-cell gap");

        var cornerGrid = new NavGrid(8, 8);
        cornerGrid.SetBlocked(6, 5, true);
        var cornerField = new FlowField(cornerGrid);
        cornerField.Compute(cornerGrid, 3, 3);
        GridDirection cornerDirection = cornerField.DirectionAt(5, 5);
        Expect(failures, !(cornerDirection.X == 1 && cornerDirection.Y == 1),
            "flow field does not cut blocked corner");

        var openSim = new GameSim(new NavGrid(16, 16), 17);
        long walker = openSim.SpawnUnit(2, 2, 0, 50, 0, 0.0, 0.0, 0.5);
        openSim.IssueMove(walker, 12, 12);
        int previousCost = int.MaxValue;
        bool costMonotonic = true;
        for (int tick = 0; tick < 400; tick++)
        {
            openSim.SimulateTick();
            int walkerX = (int)Fixed.ToInt(
                openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X));
            int walkerY = (int)Fixed.ToInt(
                openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y));
            int walkerCost = openSim.FieldFor(12, 12).CostAt(walkerX, walkerY);
            if (walkerCost < 0 || walkerCost > previousCost)
            {
                costMonotonic = false;
            }
            previousCost = walkerCost;
        }
        long settledX = openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X);
        long settledY = openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y);
        for (int tick = 0; tick < 10; tick++)
        {
            openSim.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(settledX) == 12 && Fixed.ToInt(settledY) == 12
            && openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X) == settledX
            && openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y) == settledY,
            "unit reaches open-field goal without jitter");
        Expect(failures, costMonotonic, "unit flow-field cost never increases");
    }

    private static void CheckFlowFieldCache(List<string> failures)
    {
        var shared = new GameSim(new NavGrid(32, 32), 901, 1, 6);
        const int goalX = 16;
        const int goalY = 16;
        for (int index = 0; index < 200; index++)
        {
            int spawnX = index % 32;
            int spawnY = index / 32 + 1;
            long id = shared.SpawnUnit(spawnX, spawnY, 0, 10, 0, 0.0, 0.0, 0.5);
            shared.IssueMove(id, goalX, goalY);
        }
        shared.SimulateTick();
        Expect(failures, shared.FieldsCreatedThisTick == 1, "same goal shares one flow field per tick");
        Expect(failures, shared.FieldCacheCount == 1, "same goal keeps a single cache entry");

        var bounded = new GameSim(new NavGrid(24, 24), 902, 1, 6, null, null, 4);
        for (int index = 0; index < 12; index++)
        {
            bounded.FieldFor(2 + index, 12);
        }
        Expect(failures, bounded.FieldCacheCount == 4, "flow field cache respects capacity");
        Expect(failures, bounded.FieldsCreatedTotal == 12, "distinct goals each created once");
        Expect(failures, bounded.FieldFor(2, 12).Reachable(0, 0), "evicted goal rebuilds reachable field");
    }

    private static void CheckCombat(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(12, 12), 21);
        long attacker = sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0);
        long victim = sim.SpawnUnit(6, 5, 1, 30, 10, 1.0, 1.0, 0.0);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "combat enemy dies");

        var friendly = new GameSim(new NavGrid(12, 12), 22);
        friendly.SpawnUnit(5, 5, 0, 40, 10, 1.0, 1.0, 0.0);
        friendly.SpawnUnit(6, 5, 0, 40, 10, 1.0, 1.0, 0.0);
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

    private static void CheckUnitTypes(List<string> failures)
    {
        var armored = new GameSim(new NavGrid(12, 12), 401);
        long armoredAttacker = armored.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long armoredTarget = armored.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(armored, armoredAttacker, SimComponents.Damage, 10);
        SetStat(armored, armoredAttacker, SimComponents.AttackClass, UnitAttackClass.Melee);
        SetStat(armored, armoredTarget, SimComponents.ArmorMelee, 3);
        long armoredFull = armored.World.GetValue(armoredTarget, SimComponents.Health, SimComponents.MaxHp);
        armored.IssueAttack(armoredAttacker, armoredTarget);
        armored.SimulateTick();
        Expect(failures,
            armored.World.GetValue(armoredTarget, SimComponents.Health, SimComponents.Hp) == armoredFull - 7,
            "armor reduces matching attack class");

        var piercing = new GameSim(new NavGrid(12, 12), 402);
        long piercer = piercing.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long drilled = piercing.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(piercing, piercer, SimComponents.Damage, 10);
        SetStat(piercing, piercer, SimComponents.AttackClass, UnitAttackClass.Pierce);
        SetStat(piercing, drilled, SimComponents.ArmorPierce, 4);
        SetStat(piercing, drilled, SimComponents.ArmorMelee, 0);
        long drilledFull = piercing.World.GetValue(drilled, SimComponents.Health, SimComponents.MaxHp);
        piercing.IssueAttack(piercer, drilled);
        piercing.SimulateTick();
        Expect(failures,
            piercing.World.GetValue(drilled, SimComponents.Health, SimComponents.Hp) == drilledFull - 6,
            "only the matching armor column applies");

        var minimum = new GameSim(new NavGrid(12, 12), 403);
        long weak = minimum.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long wall = minimum.SpawnUnit(6, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        SetStat(minimum, weak, SimComponents.Damage, 2);
        SetStat(minimum, weak, SimComponents.AttackClass, UnitAttackClass.Melee);
        SetStat(minimum, wall, SimComponents.ArmorMelee, 50);
        long wallFull = minimum.World.GetValue(wall, SimComponents.Health, SimComponents.MaxHp);
        minimum.IssueAttack(weak, wall);
        minimum.SimulateTick();
        Expect(failures,
            minimum.World.GetValue(wall, SimComponents.Health, SimComponents.Hp) == wallFull - 1,
            "damage never drops below one");

        var counter = new GameSim(new NavGrid(12, 12), 404);
        long spearman = counter.SpawnUnit(5, 5, 0, UnitDefs.Spearman, CombatStance.NoAttack);
        long knight = counter.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        counter.IssueAttack(spearman, knight);
        counter.SimulateTick();
        long spearDamage = 100 - counter.World.GetValue(knight, SimComponents.Health, SimComponents.Hp);
        Expect(failures, spearDamage == 19, "spearman bonus vs cavalry");

        var plain = new GameSim(new NavGrid(12, 12), 405);
        long swordsman = plain.SpawnUnit(5, 5, 0, UnitDefs.Swordsman, CombatStance.NoAttack);
        long plainKnight = plain.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        plain.IssueAttack(swordsman, plainKnight);
        plain.SimulateTick();
        long swordDamage = 100 - plain.World.GetValue(plainKnight, SimComponents.Health, SimComponents.Hp);
        Expect(failures, spearDamage > swordDamage, "counter unit outdamages plain unit");

        var archerSim = new GameSim(new NavGrid(16, 16), 406);
        long archer = archerSim.SpawnUnit(5, 5, 0, UnitDefs.Archer, CombatStance.NoAttack);
        long prey = archerSim.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        Expect(failures, archerSim.World.GetValue(archer, SimComponents.Combat, SimComponents.IsRanged) == 1, "archer marked ranged");
        archerSim.IssueAttack(archer, prey);
        archerSim.SimulateTick();
        Expect(failures,
            archerSim.World.GetValue(prey, SimComponents.Health, SimComponents.Hp) == 100,
            "ranged damage travels as projectile");
        archerSim.SimulateTick();
        Expect(failures,
            archerSim.World.GetValue(prey, SimComponents.Health, SimComponents.Hp) < 100,
            "projectile lands on later tick");

        Expect(failures, UnitTypeChecksum() == UnitTypeChecksum(), "unit types deterministic");
        Expect(failures, UnitTypeChecksum() != 0, "unit type checksum populated");
    }

    private static void SetStat(GameSim sim, long id, string column, long value) =>
        sim.World.SetValue(id, SimComponents.Combat, column, value);

    private static long UnitTypeChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 407, 2, 6);
        for (long type = 0; type < 4; type++)
        {
            sim.SpawnUnit(3 + (int)type, 4, 0, type);
            sim.SpawnUnit(3 + (int)type, 16, 1, type);
        }
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static void CheckOccupancy(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(12, 12), 61);
        long follower = sim.SpawnUnit(2, 5, 0, 40, 0, 0.0, 0.0, 0.4);
        long leader = sim.SpawnUnit(3, 5, 0, 40, 0, 0.0, 0.0, 0.4);
        sim.IssueMove(follower, 8, 5);
        sim.IssueMove(leader, 8, 5);
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        long leaderX = Fixed.ToInt(sim.World.GetValue(leader, SimComponents.Position, SimComponents.X));
        long leaderY = Fixed.ToInt(sim.World.GetValue(leader, SimComponents.Position, SimComponents.Y));
        long followerX = Fixed.ToInt(sim.World.GetValue(follower, SimComponents.Position, SimComponents.X));
        long followerY = Fixed.ToInt(sim.World.GetValue(follower, SimComponents.Position, SimComponents.Y));
        Expect(failures, leaderX != followerX || leaderY != followerY, "occupancy units never overlap");
        Expect(failures, leaderX == 8 && leaderY == 5, "occupancy leading unit reaches goal");
    }

    private static void CheckGridRange(List<string> failures)
    {
        var meleeFar = new GameSim(new NavGrid(16, 16), 62);
        long meleeAttacker = meleeFar.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long meleeTarget = meleeFar.SpawnUnit(7, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 60; tick++)
        {
            meleeFar.SimulateTick();
        }
        Expect(failures, meleeFar.World.IsAlive(meleeAttacker) && meleeFar.World.IsAlive(meleeTarget), "melee misses two cells away");

        var meleeNear = new GameSim(new NavGrid(16, 16), 63);
        meleeNear.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long adjacent = meleeNear.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 20; tick++)
        {
            meleeNear.SimulateTick();
        }
        Expect(failures, !meleeNear.World.IsAlive(adjacent), "melee hits adjacent cell");

        var ranged = new GameSim(new NavGrid(16, 16), 64);
        ranged.SpawnUnit(5, 5, 0, 100, 10, 2.0, 2.0, 0.0, CombatStance.StandGround);
        long tooClose = ranged.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        long inBand = ranged.SpawnUnit(7, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 30; tick++)
        {
            ranged.SimulateTick();
        }
        Expect(failures, ranged.World.IsAlive(tooClose), "ranged respects minimum range");
        Expect(failures, !ranged.World.IsAlive(inBand), "ranged hits within max range");
    }

    private static void CheckProjectile(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(32, 32), 65);
        sim.SpawnUnit(2, 5, 0, 100, 10, 1.0, 6.0, 0.0);
        long victim = sim.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        sim.SimulateTick();
        sim.SimulateTick();
        bool aliveBeforeImpact = sim.World.IsAlive(victim);
        sim.SimulateTick();
        bool deadAfterImpact = !sim.World.IsAlive(victim);
        Expect(failures, aliveBeforeImpact, "projectile damage delayed by travel ticks");
        Expect(failures, deadAfterImpact, "projectile applies damage on arrival");
    }

    private static void CheckAttack(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 51);
        long attacker = sim.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5);
        long victim = sim.SpawnUnit(12, 2, 1, 30, 0, 1.0, 1.0, 0.0);
        sim.IssueAttack(attacker, victim);
        Expect(failures, sim.World.HasComponent(attacker, SimComponents.AttackTarget), "attack locks explicit target");
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.World.IsAlive(attacker) && !sim.World.IsAlive(victim), "attack chases and kills explicit target");

        var friendly = new GameSim(new NavGrid(12, 12), 52);
        long ally = friendly.SpawnUnit(4, 4, 0, 60, 5, 1.0, 1.0, 0.0);
        long friend = friendly.SpawnUnit(5, 4, 0, 60, 5, 1.0, 1.0, 0.0);
        friendly.IssueAttack(ally, friend);
        Expect(failures, !friendly.World.HasComponent(ally, SimComponents.AttackTarget), "attack ignores friendly target");

        var priority = new GameSim(new NavGrid(12, 12), 53);
        long hunter = priority.SpawnUnit(5, 5, 0, 100, 10, 1.0, 2.0, 0.0);
        long near = priority.SpawnUnit(5, 6, 1, 100, 0, 1.0, 1.0, 0.0);
        long far = priority.SpawnUnit(6, 6, 1, 100, 0, 1.0, 1.0, 0.0);
        priority.IssueAttack(hunter, far);
        priority.SimulateTick();
        priority.SimulateTick();
        long farHp = priority.World.GetValue(far, SimComponents.Health, SimComponents.Hp);
        long nearHp = priority.World.GetValue(near, SimComponents.Health, SimComponents.Hp);
        Expect(failures, farHp < 100 && nearHp == 100, "explicit attack beats nearest auto target");

        var release = new GameSim(new NavGrid(20, 20), 54);
        long chaser = release.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5);
        long doomed = release.SpawnUnit(10, 2, 1, 30, 0, 1.0, 1.0, 0.0);
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
        long attacker = sim.SpawnUnit(3, 3, 0, 60, 8, 1.0, 1.0, 0.15);
        long victim = sim.SpawnUnit(12, 3, 1, 60, 8, 1.0, 1.0, 0.0);
        sim.IssueAttack(attacker, victim);
        long checksum = 0;
        for (int tick = 0; tick < 90; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
    }

    private static void CheckStance(List<string> failures)
    {
        var silent = new GameSim(new NavGrid(12, 12), 71);
        silent.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        long bystander = silent.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            silent.SimulateTick();
        }
        Expect(failures,
            silent.World.GetValue(bystander, SimComponents.Health, SimComponents.Hp) == 100,
            "no attack stance does not auto fire");

        var standGround = new GameSim(new NavGrid(16, 16), 72);
        long guard = standGround.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.StandGround);
        standGround.SpawnUnit(9, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            standGround.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(standGround.World.GetValue(guard, SimComponents.Position, SimComponents.X)) == 2
            && Fixed.ToInt(standGround.World.GetValue(guard, SimComponents.Position, SimComponents.Y)) == 2,
            "stand ground never chases");

        var standFire = new GameSim(new NavGrid(16, 16), 73);
        long sentry = standFire.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long intruder = standFire.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 10; tick++)
        {
            standFire.SimulateTick();
        }
        Expect(failures,
            standFire.World.GetValue(intruder, SimComponents.Health, SimComponents.Hp) < 100,
            "stand ground fires at in-range target");
        Expect(failures,
            standFire.World.GetValue(sentry, SimComponents.Move, SimComponents.Active) == 0,
            "stand ground does not move to engage");

        var aggressive = new GameSim(new NavGrid(24, 24), 74);
        long hunter = aggressive.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Aggressive);
        aggressive.SpawnUnit(8, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            aggressive.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(aggressive.World.GetValue(hunter, SimComponents.Position, SimComponents.X)) > 2,
            "aggressive stance chases into larger aggro radius");

        var defensive = new GameSim(new NavGrid(24, 24), 75);
        long defender = defensive.SpawnUnit(2, 2, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Defensive);
        defensive.SpawnUnit(8, 2, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        for (int tick = 0; tick < 40; tick++)
        {
            defensive.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(defensive.World.GetValue(defender, SimComponents.Position, SimComponents.X)) == 2,
            "defensive stance does not chase beyond its short aggro radius");

        var marching = new GameSim(new NavGrid(20, 20), 76);
        long marcher = marching.SpawnUnit(2, 5, 0, 100, 10, 1.0, 1.0, 0.5, CombatStance.Aggressive);
        long roadblock = marching.SpawnUnit(6, 5, 1, 100, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        marching.IssueMove(marcher, 15, 5);
        for (int tick = 0; tick < 40; tick++)
        {
            marching.SimulateTick();
        }
        Expect(failures,
            marching.World.GetValue(roadblock, SimComponents.Health, SimComponents.Hp) == 100,
            "moving unit does not auto engage");

        var ordered = new GameSim(new NavGrid(12, 12), 77);
        long commanded = ordered.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        long victim = ordered.SpawnUnit(6, 5, 1, 30, 0, 1.0, 1.0, 0.0, CombatStance.NoAttack);
        ordered.IssueAttack(commanded, victim);
        ordered.SimulateTick();
        Expect(failures,
            ordered.World.GetValue(victim, SimComponents.Health, SimComponents.Hp) == 20,
            "explicit attack overrides no attack stance");

        var setter = new GameSim(new NavGrid(12, 12), 78);
        long trooper = setter.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0);
        Expect(failures,
            setter.World.GetValue(trooper, SimComponents.Combat, SimComponents.Stance) == CombatStance.Aggressive,
            "unit spawns with default stance");
        setter.ApplyCommand(new LockstepCommand(0, LockstepCommand.KindStance, trooper, 0, 0, -1, -1, CombatStance.StandGround));
        Expect(failures,
            setter.World.GetValue(trooper, SimComponents.Combat, SimComponents.Stance) == CombatStance.StandGround,
            "stance command updates stance");

        Expect(failures,
            StanceChecksum(CombatStance.Aggressive) != StanceChecksum(CombatStance.NoAttack),
            "stance participates in checksum");
        Expect(failures,
            StanceChecksum(CombatStance.Defensive) == StanceChecksum(CombatStance.Defensive),
            "stance checksum deterministic");
    }

    private static long StanceChecksum(long stance)
    {
        var sim = new GameSim(new NavGrid(12, 12), 79);
        sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, stance);
        sim.SpawnUnit(6, 5, 1, 100, 10, 1.0, 1.0, 0.0, stance);
        return sim.World.Checksum();
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
        long ally = heal.SpawnUnit(5, 6, 0, 100, 0, 0.0, 0.0, 0.0);
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

        long enemy = heal.SpawnUnit(6, 5, 1, 100, 0, 0.0, 0.0, 0.0);
        heal.World.SetValue(enemy, SimComponents.Health, SimComponents.Hp, 40);
        heal.IssueHeal(healer, enemy);
        Expect(failures,
            heal.World.GetValue(healer, SimComponents.Monk, SimComponents.State) == MonkSystem.StateIdle,
            "heal ignores enemy target");

        var convert = new GameSim(new NavGrid(16, 16), 102);
        long monk = convert.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long victim = convert.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0, 0.0);
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
        long prey = interrupt.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0, 0.0);
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
        long patient = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0, 0.0);
        sim.World.SetValue(patient, SimComponents.Health, SimComponents.Hp, 30);
        long foe = sim.SpawnUnit(10, 8, 1, 100, 0, 0.0, 0.0, 0.0);
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

        var stanceCommand = new LockstepCommand(0, LockstepCommand.KindStance, 4, 0, 0, -1, -1, CombatStance.StandGround);
        LockstepCommand restoredStance = LockstepCommand.FromArray(stanceCommand.ToArray());
        Expect(failures,
            restoredStance.Kind == LockstepCommand.KindStance && restoredStance.Entity == 4
            && restoredStance.Stance == CombatStance.StandGround,
            "lockstep stance command roundtrip");

        Expect(failures,
            new LockstepCommand(0, LockstepCommand.KindHeal, 3, 0, 0, 8, -1).ToArray().Length == LockstepCommand.FieldCount,
            "command serialized with fixed width");
        Expect(failures, LockstepCommand.KindHeal == 5 && LockstepCommand.KindConvert == 6
            && LockstepCommand.KindPickupRelic == 7 && LockstepCommand.KindDepositRelic == 8,
            "monk command kinds stable");
        Expect(failures, LockstepCommand.KindStance == 9, "stance command kind avoids reserved ids");
        Expect(failures, LockstepCommand.KindAttackMove == 10 && LockstepCommand.KindPatrol == 11
            && LockstepCommand.KindGuard == 12 && LockstepCommand.KindFollow == 13
            && LockstepCommand.KindHold == 14 && LockstepCommand.KindSetRally == 15,
            "order command kinds stable after stance");

        var patrol = new LockstepCommand(
            0, LockstepCommand.KindPatrol, 6, 2, 3, -1, -1,
            stance: -1, cell2X: 8, cell2Y: 9, aux: 0, queue: 1);
        LockstepCommand restoredPatrol = LockstepCommand.FromArray(patrol.ToArray());
        Expect(failures,
            restoredPatrol.Kind == LockstepCommand.KindPatrol && restoredPatrol.Entity == 6
            && restoredPatrol.CellX == 2 && restoredPatrol.CellY == 3
            && restoredPatrol.Cell2X == 8 && restoredPatrol.Cell2Y == 9 && restoredPatrol.Queue == 1,
            "patrol command roundtrip keeps second endpoint");

        var frame = new CommandFrame { Player = 0, Turn = 3, Commands = new[] { patrol } };
        LockstepCommand decodedPatrol = CommandFrame.Decode(frame.Encode()).Commands[0];
        Expect(failures,
            decodedPatrol.Cell2X == 8 && decodedPatrol.Cell2Y == 9
            && decodedPatrol.Aux == 0 && decodedPatrol.Queue == 1,
            "command frame encodes patrol endpoints and queue flag");
    }

    private static void CheckVision(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 301);
        long scout = sim.SpawnUnit(5, 5, 0, 40, 0, 0.0, 0.0, 0.0);
        VisionGrid grid = sim.VisionFor(0)!;
        Expect(failures, grid.StateAt(5, 5) == VisionGrid.StateUnexplored, "vision starts unexplored");
        sim.SimulateTick();
        Expect(failures, grid.StateAt(5, 5) == VisionGrid.StateVisible, "unit tile becomes visible");
        Expect(failures, grid.VisibleAt(6, 6), "tile inside sight radius is visible");
        Expect(failures, grid.StateAt(15, 15) == VisionGrid.StateUnexplored, "far tile stays unexplored");

        SetTile(sim, scout, 12, 12);
        sim.SimulateTick();
        Expect(failures, grid.StateAt(5, 5) == VisionGrid.StateExplored, "left tile becomes explored");
        Expect(failures, !grid.VisibleAt(5, 5), "left tile is no longer visible");
        Expect(failures, grid.StateAt(12, 12) == VisionGrid.StateVisible, "new tile is visible");

        var shared = new GameSim(new NavGrid(24, 24), 302);
        long blue = shared.SpawnUnit(5, 5, 0, 40, 0, 0.0, 0.0, 0.0);
        long red = shared.SpawnUnit(18, 18, 1, 40, 0, 0.0, 0.0, 0.0);
        shared.SimulateTick();
        VisionGrid blueGrid = shared.VisionFor(0)!;
        VisionGrid redGrid = shared.VisionFor(1)!;
        Expect(failures, blueGrid.VisibleAt(5, 5) && !blueGrid.VisibleAt(18, 18), "blue vision independent");
        Expect(failures, redGrid.VisibleAt(18, 18) && !redGrid.VisibleAt(5, 5), "red vision independent");

        SnapshotFrame filtered = SnapshotFrame.Build(shared.World, shared.Lockstep.Turn, shared.Match, blueGrid, 0);
        Expect(failures, ContainsEntity(filtered, blue), "own unit stays in viewer snapshot");
        Expect(failures, !ContainsEntity(filtered, red), "hidden enemy filtered from snapshot");
        SnapshotFrame redView = SnapshotFrame.Build(shared.World, shared.Lockstep.Turn, shared.Match, redGrid, 1);
        Expect(failures, ContainsEntity(redView, red), "red viewer keeps its own unit");
        Expect(failures, !ContainsEntity(redView, blue), "red viewer filters hidden blue unit");
        Expect(failures, !SameStates(filtered.VisionStates, redView.VisionStates),
            "different viewer teams carry different vision bitmaps");
        var server = new SimulationServer(shared);
        SnapshotFrame blueServer = server.Snapshot(0);
        SnapshotFrame redServer = server.Snapshot(1);
        Expect(failures, ContainsEntity(blueServer, blue) && !ContainsEntity(blueServer, red),
            "server snapshot overload clips for team 0");
        Expect(failures, ContainsEntity(redServer, red) && !ContainsEntity(redServer, blue),
            "server snapshot overload clips for team 1");
        SnapshotFrame full = SnapshotFrame.Build(shared.World, shared.Lockstep.Turn, shared.Match);
        Expect(failures, ContainsEntity(full, blue) && ContainsEntity(full, red), "unfiltered snapshot keeps everyone");
        Expect(failures, filtered.VisionStates.Length == 24 * 24, "snapshot carries vision bitmap");

        Expect(failures, VisionDeterminismChecksum() == VisionDeterminismChecksum(), "vision deterministic");
    }

    private static bool ContainsEntity(SnapshotFrame frame, long id)
    {
        foreach (EntitySnapshot entity in frame.Entities)
        {
            if (entity.Id == id)
            {
                return true;
            }
        }
        return false;
    }

    private static bool SameStates(byte[] left, byte[] right)
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

    private static void SetTile(GameSim sim, long id, int cellX, int cellY)
    {
        sim.World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        sim.World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
    }

    private static long VisionDeterminismChecksum()
    {
        var sim = new GameSim(new NavGrid(24, 24), 303);
        for (int index = 0; index < 4; index++)
        {
            sim.SpawnUnit(4, 4 + index, 0, 50, 7, 1.5, 1.5, 0.2);
            sim.SpawnUnit(18, 4 + index, 1, 50, 7, 1.5, 1.5, 0.2);
        }
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }

    private static void CheckTech(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 401, 2, 6);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeDark, "players start in dark age");
        Expect(failures, sim.PlayerResearch(0) == 0, "players start with no research");
        long darkUnit = sim.SpawnUnit(2, 2, 0, 50, 5, 1.0, 1.0, 0.0);
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Health, SimComponents.MaxHp) == 50
            && sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == 5,
            "dark age spawn uses base stats");

        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeFeudal), "age up without resources rejected");

        sim.AddResource(0, GameSim.ResourceFood, 5000);
        sim.AddResource(0, GameSim.ResourceGold, 5000);
        long foodBeforeAge = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, sim.AgeUp(0, TechCatalog.AgeFeudal), "age up starts research");
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeDark, "age does not apply instantly");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood) == foodBeforeAge - TechCatalog.AgeFoodCostOf(TechCatalog.AgeFeudal),
            "age up deducts food");
        Expect(failures,
            ResearchSystem.ActiveKind(sim.World, 0) == ResearchSystem.KindAge,
            "age research recorded");
        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeFeudal), "duplicate age up rejected while researching");
        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeCastle), "age up cannot skip an age");

        AdvanceTicks(sim, 220);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeFeudal, "age applies after research ticks");
        Expect(failures,
            ResearchSystem.ActiveKind(sim.World, 0) == ResearchSystem.KindNone,
            "research slot clears on age completion");

        long feudalMaxHp = TechCatalog.EffectiveStat(
            TechCatalog.AgeFeudal, 0, TechCatalog.StatMaxHp, 50);
        long feudalDamage = TechCatalog.EffectiveStat(
            TechCatalog.AgeFeudal, 0, TechCatalog.StatDamage, 5);
        long feudalUnit = sim.SpawnUnit(3, 2, 0, 50, 5, 1.0, 1.0, 0.0);
        Expect(failures,
            sim.World.GetValue(feudalUnit, SimComponents.Health, SimComponents.MaxHp) == feudalMaxHp
            && sim.World.GetValue(feudalUnit, SimComponents.Combat, SimComponents.Damage) == feudalDamage,
            "new spawn applies age bonus");
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Health, SimComponents.MaxHp) == feudalMaxHp
            && sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == feudalDamage,
            "existing unit recomputed on age up");

        long foodBeforeResearch = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, !sim.Research(0, TechCatalog.TechForging), "research needs its prerequisite building");
        SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
        Expect(failures, sim.Research(0, TechCatalog.TechForging), "feudal research starts after barracks");
        Expect(failures, !sim.IsResearched(0, TechCatalog.TechForging), "research not applied instantly");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood)
                == foodBeforeResearch - TechCatalog.TechFoodCostOf(TechCatalog.TechForging),
            "research deducts food");
        Expect(failures, !sim.Research(0, TechCatalog.TechForging), "duplicate research rejected");
        AdvanceTicks(sim, 120);
        Expect(failures, sim.IsResearched(0, TechCatalog.TechForging), "research bit set after ticks");
        long researchedDamage = feudalDamage + 2;
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == researchedDamage,
            "research recomputes existing unit damage");
        Expect(failures,
            sim.World.GetValue(
                sim.SpawnUnit(4, 2, 0, 50, 5, 1.0, 1.0, 0.0),
                SimComponents.Combat,
                SimComponents.Damage) == researchedDamage,
            "research applies to new spawn");
        Expect(failures, !sim.Research(0, TechCatalog.TechFletching), "age gated research rejected");
        Expect(failures, !sim.Research(0, 999), "unknown tech rejected");
        Expect(failures, !sim.Research(5, TechCatalog.TechForging), "unknown player rejected");

        SpawnCompleteBuilding(sim, BuildingTypes.Market, 10, 9, 0);
        Expect(failures, sim.AgeUp(0, TechCatalog.AgeCastle), "castle age up needs two feudal buildings");
        AdvanceTicks(sim, 280);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeCastle, "castle age applies after research ticks");
        Expect(failures, sim.Research(0, TechCatalog.TechFletching), "castle research starts");
        AdvanceTicks(sim, 140);
        Expect(failures, sim.IsResearched(0, TechCatalog.TechFletching), "castle research completes");
        long expectedRange = TechCatalog.EffectiveStat(
            TechCatalog.AgeCastle,
            sim.PlayerResearch(0),
            TechCatalog.StatRangeMax,
            ToRangeCellsForTest(1.0));
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.RangeMax) == expectedRange,
            "range bonus recomputes existing unit");

        var dispatch = new GameSim(new NavGrid(12, 12), 405, 2, 6);
        dispatch.AddResource(0, GameSim.ResourceFood, 2000);
        dispatch.AddResource(0, GameSim.ResourceGold, 1000);
        dispatch.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindAgeUp, aux: TechCatalog.AgeFeudal));
        Expect(failures,
            ResearchSystem.ActiveKind(dispatch.World, 0) == ResearchSystem.KindAge,
            "age up command starts research");
        AdvanceTicks(dispatch, 220);
        Expect(failures, dispatch.PlayerAge(0) == TechCatalog.AgeFeudal, "age up command completes");
        SpawnCompleteBuilding(dispatch, BuildingTypes.Barracks, 6, 6, 0);
        dispatch.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindResearch, aux: TechCatalog.TechForging));
        Expect(failures,
            ResearchSystem.ActiveKind(dispatch.World, 0) == ResearchSystem.KindTech,
            "research command starts research");
        AdvanceTicks(dispatch, 120);
        Expect(failures, dispatch.IsResearched(0, TechCatalog.TechForging), "research command completes");

        var cancelSim = new GameSim(new NavGrid(12, 12), 409, 2, 6);
        cancelSim.AddResource(0, GameSim.ResourceFood, 2000);
        long foodBeforeCancel = cancelSim.Resource(0, GameSim.ResourceFood);
        Expect(failures, cancelSim.AgeUp(0, TechCatalog.AgeFeudal), "cancel case starts age up");
        cancelSim.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindCancelResearch));
        Expect(failures,
            ResearchSystem.ActiveKind(cancelSim.World, 0) == ResearchSystem.KindNone,
            "cancel command clears research");
        Expect(failures,
            cancelSim.Resource(0, GameSim.ResourceFood) == foodBeforeCancel,
            "cancel refunds paid resources");

        var ruinSim = new GameSim(new NavGrid(12, 12), 410, 2, 6);
        ruinSim.Progress(0).Age = TechCatalog.AgeFeudal;
        ruinSim.AddResource(0, GameSim.ResourceFood, 1000);
        long ruinBarracks = SpawnCompleteBuilding(ruinSim, BuildingTypes.Barracks, 6, 6, 0);
        long foodBeforeRuin = ruinSim.Resource(0, GameSim.ResourceFood);
        Expect(failures, ruinSim.Research(0, TechCatalog.TechForging), "ruin case starts research");
        ruinSim.World.SetValue(
            ruinBarracks, SimComponents.Building, SimComponents.State, BuildingSystem.StateRuins);
        ruinSim.SimulateTick();
        Expect(failures, !ruinSim.IsResearched(0, TechCatalog.TechForging), "ruined carrier aborts research");
        Expect(failures,
            ruinSim.Resource(0, GameSim.ResourceFood) == foodBeforeRuin,
            "ruined carrier refunds paid resources");

        var snapshotSim = new GameSim(new NavGrid(12, 12), 406, 2, 6, MatchConfig.Standard);
        snapshotSim.SpawnUnit(1, 1, 0, 50, 0, 0.0, 0.0, 0.0);
        snapshotSim.SpawnUnit(10, 10, 1, 50, 0, 0.0, 0.0, 0.0);
        snapshotSim.AddResource(0, GameSim.ResourceFood, 2000);
        snapshotSim.AddResource(0, GameSim.ResourceGold, 1000);
        snapshotSim.AgeUp(0, TechCatalog.AgeFeudal);
        SnapshotFrame frame = SnapshotFrame.Build(
            snapshotSim.World, snapshotSim.Lockstep.Turn, snapshotSim.Match);
        Expect(failures, frame.Players.Length == 2, "snapshot carries players");
        Expect(failures,
            frame.Players[0].Age == TechCatalog.AgeDark
            && frame.Players[0].ResearchKind == ResearchSystem.KindAge
            && frame.Players[0].ResearchMaxProgress > 0
            && frame.Players[1].ResearchKind == ResearchSystem.KindNone,
            "snapshot carries in-progress research");
        AdvanceTicks(snapshotSim, 220);
        SpawnCompleteBuilding(snapshotSim, BuildingTypes.Barracks, 6, 6, 0);
        snapshotSim.Research(0, TechCatalog.TechForging);
        AdvanceTicks(snapshotSim, 120);
        frame = SnapshotFrame.Build(
            snapshotSim.World, snapshotSim.Lockstep.Turn, snapshotSim.Match);
        Expect(failures,
            frame.Players[0].Age == TechCatalog.AgeFeudal
            && frame.Players[0].Research == TechCatalog.ResearchBit(TechCatalog.TechForging)
            && frame.Players[1].Age == TechCatalog.AgeDark,
            "snapshot carries completed age and research");
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        Expect(failures,
            decoded.Players[0].Age == TechCatalog.AgeFeudal
            && decoded.Players[0].Research == TechCatalog.ResearchBit(TechCatalog.TechForging)
            && decoded.Players[0].ResearchKind == ResearchSystem.KindNone,
            "snapshot roundtrip keeps age and research");

        Expect(failures,
            TechProgressionChecksum(true) == TechProgressionChecksum(true),
            "tech progression deterministic");
        Expect(failures,
            TechProgressionChecksum(true) != TechProgressionChecksum(false),
            "tech progression participates in checksum");
    }

    private static void AdvanceTicks(GameSim sim, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            sim.SimulateTick();
        }
    }

    private static long SpawnCompleteBuilding(GameSim sim, long type, int cellX, int cellY, long team)
    {
        sim.AddResource(team, GameSim.ResourceWood, BuildingTypes.Cost(type));
        long id = sim.IssuePlaceBuilding(type, cellX, cellY, team);
        sim.World.SetValue(id, SimComponents.Building, SimComponents.Progress, BuildingTypes.BuildTicks(type));
        sim.World.SetValue(id, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        return id;
    }

    private static void CheckTerrain(List<string> failures)
    {
        var terrain = new TerrainGrid(8, 8);
        Expect(failures, terrain.CanTraverse(2, 3, 3, 3), "flat terrain traversable");
        terrain.SetHeight(3, 3, 1);
        Expect(failures, terrain.CanTraverse(2, 3, 3, 3), "one level step traversable");
        terrain.SetHeight(3, 3, 2);
        Expect(failures, !terrain.CanTraverse(2, 3, 3, 3), "cliff height difference blocks");
        terrain.Set(4, 4, TerrainGrid.TypeWater, 0);
        Expect(failures, !terrain.CanTraverse(4, 3, 4, 4), "water blocks traversal");
        terrain.Set(5, 4, TerrainGrid.TypeCliff, 0);
        Expect(failures, !terrain.CanTraverse(4, 4, 5, 4), "cliff type blocks traversal");
        Expect(failures, terrain.Checksum() == terrain.Clone().Checksum(), "terrain checksum clone stable");

        var nav = new NavGrid(8, 8);
        var wall = new TerrainGrid(8, 8);
        for (int y = 0; y < 8; y++)
        {
            wall.Set(3, y, TerrainGrid.TypeGrass, 3);
        }
        var field = new FlowField(nav, wall);
        Expect(failures,
            field.Compute(nav, 6, 3) && !field.Reachable(1, 3),
            "flow field respects cliff wall");
        var flatField = new FlowField(nav, TerrainGrid.Flat(8, 8));
        Expect(failures, flatField.Compute(nav, 6, 3) && flatField.Reachable(1, 3),
            "flat terrain leaves flow field open");

        var sim = new GameSim(new NavGrid(8, 8), 9, 1, 6, null, wall);
        long id = sim.SpawnUnit(1, 4, 0, 10, 0, 0.0, 0.0, 0.5);
        sim.IssueMove(id, 6, 4);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        int cellX = (int)Fixed.ToInt(sim.World.GetValue(
            id, SimComponents.Position, SimComponents.X));
        Expect(failures, cellX < 3, "unit cannot path across cliff wall");

        var map = new MapData("test", 5, wall.Clone(), nav.Clone());
        MapData restored = MapData.FromJson(map.ToJson());
        Expect(failures,
            restored.Width == 8
            && restored.Height == 8
            && restored.Terrain.HeightAt(3, 3) == 3
            && restored.Nav.Width == 8,
            "map json roundtrip keeps terrain and nav");
        Expect(failures,
            restored.Terrain.Checksum() == wall.Checksum(),
            "map json roundtrip keeps terrain checksum");
    }

    private static long ToRangeCellsForTest(double tiles) =>
        (long)System.Math.Round(tiles, System.MidpointRounding.AwayFromZero);

    private static long TechProgressionChecksum(bool advanced)
    {
        var sim = new GameSim(new NavGrid(16, 16), 408, 2, 6);
        sim.SpawnUnit(2, 2, 0, 50, 5, 1.0, 1.0, 0.1);
        if (advanced)
        {
            sim.AddResource(0, GameSim.ResourceFood, 5000);
            sim.AddResource(0, GameSim.ResourceGold, 5000);
            sim.AgeUp(0, TechCatalog.AgeFeudal);
            AdvanceTicks(sim, 220);
            SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
            sim.Research(0, TechCatalog.TechForging);
            AdvanceTicks(sim, 120);
        }
        sim.SpawnUnit(4, 2, 0, 50, 5, 1.0, 2.0, 0.1);
        for (int tick = 0; tick < 30; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }
}

