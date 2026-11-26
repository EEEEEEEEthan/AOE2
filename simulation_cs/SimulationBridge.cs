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

    public Godot.Collections.Array<string> KernelSelfTest() => AOE2Framework.KernelSelfTest.Run();

    public bool ProtocolSelfCheck()
    {
        var command = new AoeSim.LockstepCommand(1, AoeSim.LockstepCommand.KindGather, 12, 3, 4, 30, 31);
        var frame = new AoeSim.CommandFrame
        {
            Player = 1,
            Turn = 7,
            Commands = new[] { command },
        };
        AoeSim.CommandFrame decodedCommand = AoeSim.CommandFrame.Decode(frame.Encode());
        if (decodedCommand.Player != 1 || decodedCommand.Turn != 7 || decodedCommand.Commands.Length != 1)
        {
            return false;
        }
        AoeSim.LockstepCommand restored = decodedCommand.Commands[0];
        if (restored.Entity != 12 || restored.TargetEntity != 30 || restored.DropEntity != 31 || restored.Kind != AoeSim.LockstepCommand.KindGather)
        {
            return false;
        }
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        AoeSim.SnapshotFrame snapshot = AoeSim.SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
        AoeSim.SnapshotFrame decodedSnapshot = AoeSim.SnapshotFrame.Decode(snapshot.Encode());
        return decodedSnapshot.Tick == snapshot.Tick
            && decodedSnapshot.Entities.Length == snapshot.Entities.Length
            && decodedSnapshot.Entities.Length > 0;
    }

    public bool ServerClientSelfCheck()
    {
        var session = new AoeSim.LocalSession(AoeSim.DemoScenario.Build());
        long firstChecksum = 0;
        for (int tick = 0; tick < 30; tick++)
        {
            firstChecksum = session.Step();
        }
        long clientTick = session.Client.Tick;
        int clientEntities = session.Client.EntityCount;
        int serverEntities = 0;
        foreach (long id in session.Server.Sim.World.Entities())
        {
            if (session.Server.Sim.World.HasComponent(id, AoeSim.SimComponents.Position))
            {
                serverEntities += 1;
            }
        }
        return clientTick == 30 && clientEntities == serverEntities && clientEntities > 0 && firstChecksum != 0;
    }

    public long DemoChecksum(long ticks)
    {
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        for (long tick = 0; tick < ticks; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
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
