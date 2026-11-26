using Godot;

namespace AOE2Framework;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public long RngDigest(long seed, long count)
    {
        var rng = new AoeSim.SimRng(seed);
        long hash = AoeSim.SimWorld.FnvOffset;
        for (long draw = 0; draw < count; draw++)
        {
            hash = AoeSim.SimWorld.HashInt(hash, rng.NextU32());
        }
        return hash;
    }

    public long ChecksumScenario(long scenarioId)
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
            _ => -1,
        };
    }

    private static long PathMarch(long seed, bool withExtraTeam)
    {
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(20, 20), seed);
        for (int index = 0; index < 20; index++)
        {
            long id = sim.SpawnUnit(2 + (index % 4), 3 + (index % 5), 0, 40, 0, 0.0, 0.3);
            sim.IssueMove(id, 16, 16);
        }
        if (withExtraTeam)
        {
            sim.SpawnUnit(1, 1, 1, 40, 0, 0.0, 0.3);
        }
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long CombatBattle()
    {
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(20, 20), 24);
        for (int index = 0; index < 12; index++)
        {
            long attacker = sim.SpawnUnit(4, 4 + index, 0, 50, 7, 1.5, 0.2);
            sim.IssueMove(attacker, 9, 10);
        }
        for (int index = 0; index < 12; index++)
        {
            long defender = sim.SpawnUnit(15, 4 + index, 1, 50, 7, 1.5, 0.2);
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
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(24, 24), 44);
        long wood = sim.SpawnResourceNode(5, 5, AoeSim.GameSim.ResourceWood, 50);
        long gold = sim.SpawnResourceNode(5, 8, AoeSim.GameSim.ResourceGold, 30);
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
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(24, 24), 31, 2, 6);
        for (int index = 0; index < 6; index++)
        {
            sim.SpawnUnit(3 + index, 4, 0, 50, 6, 1.5, 0.2);
            sim.SpawnUnit(3 + index, 16, 1, 50, 6, 1.5, 0.2);
        }
        var playerZero = new System.Collections.Generic.List<AoeSim.LockstepCommand>();
        var playerOne = new System.Collections.Generic.List<AoeSim.LockstepCommand>();
        foreach (long id in sim.World.Entities())
        {
            long team = sim.World.GetValue(id, AoeSim.SimComponents.Team, AoeSim.SimComponents.TeamId);
            if (team == 0)
            {
                playerZero.Add(new AoeSim.LockstepCommand(0, AoeSim.LockstepCommand.KindMove, id, 6, 10));
            }
            else
            {
                playerOne.Add(new AoeSim.LockstepCommand(1, AoeSim.LockstepCommand.KindMove, id, 6, 10));
            }
        }
        sim.SubmitTurn(0, playerZero);
        sim.SubmitTurn(1, playerOne);
        return sim.SimulateTurn();
    }

    private static long UnitReach()
    {
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(16, 16), 11);
        long id = sim.SpawnUnit(2, 2, 0, 50, 0, 0.0, 0.5);
        sim.IssueMove(id, 10, 10);
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static long DiagonalStep()
    {
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(16, 16), 41);
        long id = sim.SpawnUnit(2, 2, 0, 10, 0, 0.0, 0.4);
        sim.IssueMove(id, 3, 3);
        sim.SimulateTick();
        return sim.World.Checksum();
    }

    private static long GatherDeplete()
    {
        var sim = new AoeSim.GameSim(new AoeSim.NavGrid(20, 20), 43);
        long node = sim.SpawnResourceNode(4, 5, AoeSim.GameSim.ResourceStone, 3);
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
