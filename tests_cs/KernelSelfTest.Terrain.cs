using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>地形高度、悬崖对寻路、建筑放置与地图序列化的影响。</summary>
public static partial class KernelSelfTest
{
    private static void CheckTerrain(List<string> failures)
    {
        var terrain = new TerrainGrid(8, 8);
        Expect(failures, terrain.CanTraverse(2, 3, 3, 3), "flat terrain traversable");
        terrain.SetVertexHeight(3, 3, 1);
        Expect(failures, terrain.CanTraverse(2, 3, 3, 3), "gentle vertex slope traversable");
        terrain.SetVertexHeight(3, 3, 8);
        Expect(failures, !terrain.CanTraverse(2, 3, 3, 3), "steep vertex slope blocks traversal");
        Expect(failures, terrain.IsSteep(3, 3), "cell spanning a cliff is steep");

        var buildingTerrain = TerrainGrid.Flat(8, 8);
        buildingTerrain.SetVertexHeight(3, 3, 8);
        Expect(failures,
            !BuildingSystem.CanPlace(
                new NavGrid(8, 8), buildingTerrain, BuildingTypes.House, 3, 3),
            "building cannot straddle a cliff");
        var gentleTerrain = TerrainGrid.Flat(8, 8);
        gentleTerrain.SetVertexHeight(3, 3, 1);
        Expect(failures,
            BuildingSystem.CanPlace(
                new NavGrid(8, 8), gentleTerrain, BuildingTypes.House, 3, 3),
            "building fits on a gentle slope");

        terrain.Set(4, 4, TerrainGrid.TypeWater, 0);
        Expect(failures, !terrain.CanTraverse(4, 3, 4, 4), "water blocks traversal");
        terrain.Set(5, 4, TerrainGrid.TypeCliff, 0);
        Expect(failures, !terrain.CanTraverse(4, 4, 5, 4), "cliff type blocks traversal");
        Expect(failures, terrain.Checksum() == terrain.Clone().Checksum(), "terrain checksum clone stable");

        var nav = new NavGrid(8, 8);
        var wall = new TerrainGrid(8, 8);
        for (int y = 0; y < 8; y++)
        {
            wall.Set(3, y, TerrainGrid.TypeCliff, 3);
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
}
