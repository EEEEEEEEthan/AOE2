using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>视野揭示、双玩家独立与快照裁剪语义。</summary>
public static partial class KernelSelfTest
{
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
}
