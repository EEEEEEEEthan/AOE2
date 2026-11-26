using Godot;

namespace AOE2Framework;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public long RngDigest(long seed, long count) => AoeSim.DeterminismScenarios.RngDigest(seed, count);

    public Godot.Collections.Array<string> KernelSelfTest() => ToGodotArray(AOE2Framework.KernelSelfTest.Run());

    public Godot.Collections.Array<string> MatchSelfTest() => ToGodotArray(AOE2Framework.MatchSelfTest.Run());

    private static Godot.Collections.Array<string> ToGodotArray(System.Collections.Generic.List<string> failures)
    {
        var result = new Godot.Collections.Array<string>();
        foreach (string failure in failures)
        {
            result.Add(failure);
        }
        return result;
    }

    public bool ProtocolSelfCheck()
    {
        var command = new AoeSim.LockstepCommand(1, AoeSim.LockstepCommand.KindGather, 12, 3, 4, 30, 31);
        var attack = new AoeSim.LockstepCommand(0, AoeSim.LockstepCommand.KindAttack, 7, 0, 0, 21, -1, AoeSim.CombatStance.Defensive);
        var frame = new AoeSim.CommandFrame
        {
            Player = 1,
            Turn = 7,
            Commands = new[] { command, attack },
        };
        AoeSim.CommandFrame decodedCommand = AoeSim.CommandFrame.Decode(frame.Encode());
        if (decodedCommand.Player != 1 || decodedCommand.Turn != 7 || decodedCommand.Commands.Length != 2)
        {
            return false;
        }
        AoeSim.LockstepCommand restored = decodedCommand.Commands[0];
        if (restored.Entity != 12 || restored.TargetEntity != 30 || restored.DropEntity != 31 || restored.Kind != AoeSim.LockstepCommand.KindGather)
        {
            return false;
        }
        AoeSim.LockstepCommand restoredAttack = decodedCommand.Commands[1];
        if (restoredAttack.Entity != 7 || restoredAttack.TargetEntity != 21 || restoredAttack.Kind != AoeSim.LockstepCommand.KindAttack
            || restoredAttack.Stance != AoeSim.CombatStance.Defensive)
        {
            return false;
        }
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        AoeSim.SnapshotFrame snapshot = AoeSim.SnapshotFrame.Build(sim.World, sim.Lockstep.Turn, sim.Match);
        AoeSim.SnapshotFrame decodedSnapshot = AoeSim.SnapshotFrame.Decode(snapshot.Encode());
        return decodedSnapshot.Tick == snapshot.Tick
            && decodedSnapshot.Phase == snapshot.Phase
            && decodedSnapshot.Players.Length == snapshot.Players.Length
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

    public long DemoChecksum(long ticks) => AoeSim.DeterminismScenarios.DemoChecksum(ticks);

    public long ChecksumScenario(long scenarioId) => AoeSim.DeterminismScenarios.ChecksumScenario(scenarioId);
}
