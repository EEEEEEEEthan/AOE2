using Godot;

namespace AOE2Framework;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public long RngDigest(long seed, long count) => AoeSim.DeterminismScenarios.RngDigest(seed, count);

    public Godot.Collections.Array<string> KernelSelfTest() => ToGodotArray(AOE2Framework.KernelSelfTest.Run());

    public Godot.Collections.Array<string> ServerTeamsSelfTest() => ToGodotArray(AOE2Framework.ServerSelfTest.Run());

    public Godot.Collections.Array<string> MatchSelfTest() => ToGodotArray(AOE2Framework.MatchSelfTest.Run());

    public Godot.Collections.Array<string> OrderSelfTest() => ToGodotArray(AOE2Framework.OrderSelfTest.Run());

    public Godot.Collections.Array<string> EconomySelfTest() => ToGodotArray(AOE2Framework.EconomySelfTest.Run());

    public Godot.Collections.Array<string> PopulationSelfTest() => ToGodotArray(AOE2Framework.PopulationSelfTest.Run());

    public Godot.Collections.Array<string> BuildingSelfTest() => ToGodotArray(AOE2Framework.BuildingSelfTest.Run());

    public Godot.Collections.Array<string> MapSelfTest() => ToGodotArray(AOE2Framework.MapSelfTest.Run());

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
        if (decodedSnapshot.Tick != snapshot.Tick
            || decodedSnapshot.Phase != snapshot.Phase
            || decodedSnapshot.Players.Length != snapshot.Players.Length
            || decodedSnapshot.Entities.Length != snapshot.Entities.Length
            || decodedSnapshot.Entities.Length == 0)
        {
            return false;
        }
        if (!StaticMapRoundTrips())
        {
            return false;
        }
        if (!SnapshotExcludesTerrain())
        {
            return false;
        }
        return VisionRoundTrips(sim);
    }

    private static bool SnapshotExcludesTerrain()
    {
        var grid = new AoeSim.NavGrid(64, 64);
        var sim = new AoeSim.GameSim(grid, 7, 1, 6, null, AoeSim.TerrainGrid.Flat(64, 64));
        sim.SimulateTick();
        AoeSim.SnapshotFrame snapshot = AoeSim.SnapshotFrame.Build(
            sim.World, sim.Lockstep.Turn, sim.Match, sim.VisionFor(0), 0);
        AoeSim.StaticMapFrame map = AoeSim.StaticMapFrame.FromTerrain(sim.Terrain);
        return map.TerrainTypes.Length == 64 * 64
            && map.TerrainHeights.Length == 64 * 64
            && snapshot.Encode().Length < 64 * 64 * 2;
    }

    private static bool StaticMapRoundTrips()
    {
        AoeSim.TerrainGrid terrain = AoeSim.TerrainGrid.Flat(9, 7);
        terrain.Set(2, 3, AoeSim.TerrainGrid.TypeWater, 4);
        AoeSim.StaticMapFrame decoded = AoeSim.StaticMapFrame.Decode(
            AoeSim.StaticMapFrame.FromTerrain(terrain).Encode());
        if (decoded.Width != 9 || decoded.Height != 7
            || decoded.TerrainTypes.Length != terrain.Types.Length
            || decoded.TerrainHeights.Length != terrain.Heights.Length)
        {
            return false;
        }
        for (int index = 0; index < terrain.Types.Length; index++)
        {
            if (decoded.TerrainTypes[index] != terrain.Types[index]
                || decoded.TerrainHeights[index] != terrain.Heights[index])
            {
                return false;
            }
        }
        return true;
    }

    private static bool VisionRoundTrips(AoeSim.GameSim sim)
    {
        sim.SimulateTick();
        AoeSim.SnapshotFrame snapshot = AoeSim.SnapshotFrame.Build(
            sim.World, sim.Lockstep.Turn, sim.Match, sim.VisionFor(0), 0);
        AoeSim.SnapshotFrame decoded = AoeSim.SnapshotFrame.Decode(snapshot.Encode());
        if (decoded.VisionStates.Length != snapshot.VisionStates.Length
            || decoded.VisionStates.Length == 0)
        {
            return false;
        }
        for (int index = 0; index < snapshot.VisionStates.Length; index++)
        {
            if (decoded.VisionStates[index] != snapshot.VisionStates[index])
            {
                return false;
            }
        }
        return true;
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
        AoeSim.VisionGrid? vision = session.Server.Sim.VisionFor(session.Server.ViewTeam);
        int expectedEntities = 0;
        foreach (long id in session.Server.Sim.World.Entities())
        {
            if (!session.Server.Sim.World.HasComponent(id, AoeSim.SimComponents.Position))
            {
                continue;
            }
            if (vision == null || AoeSim.SnapshotFrame.ShouldInclude(session.Server.Sim.World, id, vision, session.Server.ViewTeam))
            {
                expectedEntities += 1;
            }
        }
        bool hasVision = session.Client.Latest != null && session.Client.Latest.VisionStates.Length > 0;
        bool hasMap = session.Client.Map != null && session.Client.Map.TerrainTypes.Length > 0;
        return clientTick == 30
            && clientEntities == expectedEntities
            && expectedEntities > 0
            && hasVision
            && hasMap
            && firstChecksum != 0;
    }

    public long DemoChecksum(long ticks) => AoeSim.DeterminismScenarios.DemoChecksum(ticks);

    public long ChecksumScenario(long scenarioId) => AoeSim.DeterminismScenarios.ChecksumScenario(scenarioId);
}
