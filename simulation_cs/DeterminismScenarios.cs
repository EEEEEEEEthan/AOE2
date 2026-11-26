namespace AoeSim;

/// <summary>与旧 GDScript 演示一致的确定性场景，供本地局、dedicated server 与黄金校验和共用。</summary>
public static class DeterminismScenarios
{
    public static long RngDigest(long seed, long count)
    {
        var rng = new SimRng(seed);
        long hash = SimWorld.FnvOffset;
        for (long draw = 0; draw < count; draw++)
        {
            hash = SimWorld.HashInt(hash, rng.NextU32());
        }
        return hash;
    }

    public static long DemoChecksum(long ticks)
    {
        GameSim sim = DemoScenario.Build();
        for (long tick = 0; tick < ticks; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    public static long ChecksumScenario(long scenarioId)
    {
        return scenarioId switch
        {
            0 => PathMarch(77, withExtraTeam: false),
            1 => PathMarch(77, withExtraTeam: true),
            2 => CombatBattle(),
            3 => EconomyRun(),
            4 => LockstepTurn(),
            5 => UnitReach(),
            6 => DiagonalStep(),
            7 => GatherDeplete(),
            8 => AttackBattle(),
            _ => -1,
        };
    }

    private static long PathMarch(long seed, bool withExtraTeam)
    {
        var sim = new GameSim(new NavGrid(20, 20), seed);
        for (int index = 0; index < 20; index++)
        {
            long id = sim.SpawnUnit(2 + (index % 4), 3 + (index % 5), 0, 40, 0, 0.0, 0.0, 0.3);
            sim.IssueMove(id, 16, 16);
        }
        if (withExtraTeam)
        {
            sim.SpawnUnit(1, 1, 1, 40, 0, 0.0, 0.0, 0.3);
        }
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long CombatBattle()
    {
        var sim = new GameSim(new NavGrid(20, 20), 24);
        for (int index = 0; index < 12; index++)
        {
            long attacker = sim.SpawnUnit(4, 4 + index, 0, 50, 7, 1.0, 2.0, 0.2);
            sim.IssueMove(attacker, 9, 10);
        }
        for (int index = 0; index < 12; index++)
        {
            long defender = sim.SpawnUnit(15, 4 + index, 1, 50, 7, 1.0, 2.0, 0.2);
            sim.IssueMove(defender, 10, 10);
        }
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long EconomyRun()
    {
        var sim = new GameSim(new NavGrid(24, 24), 44);
        long wood = sim.SpawnResourceNode(5, 5, GameSim.ResourceWood, 50);
        long gold = sim.SpawnResourceNode(5, 8, GameSim.ResourceGold, 30);
        long drop = sim.SpawnDropSite(10, 6, 0b1111);
        for (int index = 0; index < 4; index++)
        {
            long villager = sim.SpawnVillager(4 + index, 4, 0, 0.2, 5, 1.0);
            sim.IssueGather(villager, index % 2 == 0 ? wood : gold, drop);
        }
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long LockstepTurn()
    {
        var sim = new GameSim(new NavGrid(24, 24), 31, 2, 6);
        for (int index = 0; index < 6; index++)
        {
            sim.SpawnUnit(3 + index, 4, 0, 50, 6, 1.0, 2.0, 0.2);
            sim.SpawnUnit(3 + index, 16, 1, 50, 6, 1.0, 2.0, 0.2);
        }
        var playerZero = new System.Collections.Generic.List<LockstepCommand>();
        var playerOne = new System.Collections.Generic.List<LockstepCommand>();
        foreach (long id in sim.World.Entities())
        {
            long team = sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            if (team == 0)
            {
                playerZero.Add(new LockstepCommand(0, LockstepCommand.KindMove, id, 6, 10));
            }
            else
            {
                playerOne.Add(new LockstepCommand(1, LockstepCommand.KindMove, id, 6, 10));
            }
        }
        sim.SubmitTurn(0, playerZero);
        sim.SubmitTurn(1, playerOne);
        return sim.SimulateTurn();
    }

    private static long UnitReach()
    {
        var sim = new GameSim(new NavGrid(16, 16), 11);
        long id = sim.SpawnUnit(2, 2, 0, 50, 0, 0.0, 0.0, 0.5);
        sim.IssueMove(id, 10, 10);
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long DiagonalStep()
    {
        var sim = new GameSim(new NavGrid(16, 16), 41);
        long id = sim.SpawnUnit(2, 2, 0, 10, 0, 0.0, 0.0, 0.4);
        sim.IssueMove(id, 3, 3);
        sim.SimulateTick();
        return sim.World.Checksum();
    }

    private static long AttackBattle()
    {
        var sim = new GameSim(new NavGrid(24, 24), 66, 2, 6);
        for (int index = 0; index < 6; index++)
        {
            long attacker = sim.SpawnUnit(3, 4 + index, 0, 60, 8, 1.0, 1.0, 0.2);
            long victim = sim.SpawnUnit(14, 4 + index, 1, 40, 8, 1.0, 1.0, 0.0);
            sim.IssueAttack(attacker, victim);
        }
        for (int tick = 0; tick < 150; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long GatherDeplete()
    {
        var sim = new GameSim(new NavGrid(20, 20), 43);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceStone, 3);
        long drop = sim.SpawnDropSite(9, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }
}
