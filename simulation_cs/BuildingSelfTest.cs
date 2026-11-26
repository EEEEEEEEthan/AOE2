using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>建筑一期 C# 内核自测：多格占地、放置校验、施工、摧毁废墟、寻路恢复与确定性。</summary>
public static class BuildingSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckPlacementBlocking(failures);
        CheckIllegalPlacement(failures);
        CheckTerrainPlacement(failures);
        CheckConstruction(failures);
        CheckDestroyRuins(failures);
        CheckDeterminism(failures);
        CheckCommandRoundtrip(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static void CheckPlacementBlocking(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 501);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        Expect(failures, building >= 0, "house placed on open ground");
        Expect(failures, grid.IsBuildingBlocked(4, 4), "building origin cell blocks pathing");
        Expect(failures, grid.IsBuildingBlocked(5, 5), "building far corner blocks pathing");
        Expect(failures, !grid.IsBuildingBlocked(6, 4), "cell beyond footprint stays open");
        Expect(failures, grid.IsBlocked(4, 4), "building joins pathing blocked union");
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateFoundation,
            "placed building starts as foundation");
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Building, SimComponents.SizeWidth)
                == BuildingTypes.SizeWidth(BuildingTypes.House),
            "footprint width recorded on building");
    }

    private static void CheckIllegalPlacement(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        grid.SetBlocked(2, 2, true);
        var sim = new GameSim(grid, 502);
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 15, 15, 0) == -1, "out of bounds rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 2, 2, 0) == -1, "terrain wall rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.None, 8, 8, 0) == -1, "unknown type rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 8, 8, 0) >= 0, "free spot accepted");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 9, 9, 0) == -1, "overlapping building rejected");
    }

    private static void CheckTerrainPlacement(List<string> failures)
    {
        var terrain = TerrainGrid.Flat(16, 16);
        terrain.SetType(3, 3, TerrainGrid.TypeWater);
        terrain.SetType(6, 3, TerrainGrid.TypeCliff);
        terrain.SetHeight(12, 12, 1);
        for (int cellX = 10; cellX < 12; cellX++)
        {
            for (int cellY = 10; cellY < 12; cellY++)
            {
                terrain.SetHeight(cellX, cellY, 2);
            }
        }
        var sim = new GameSim(new NavGrid(16, 16), 507, terrain: terrain);
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 3, 3, 0) == -1, "water rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 6, 3, 0) == -1, "cliff rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 12, 12, 0) == -1, "sloped footprint rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 10, 10, 0) >= 0, "flat plateau accepted");
    }

    private static void CheckConstruction(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 503);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 5, 5, 0);
        long builder = sim.SpawnVillager(9, 6, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, building);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateComplete,
            "worker finishes construction");
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Building, SimComponents.Progress)
                == BuildingTypes.BuildTicks(BuildingTypes.House),
            "construction progress reaches maximum");
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Health, SimComponents.Hp)
                == BuildingTypes.MaxHp(BuildingTypes.House),
            "completed building holds full health");
        Expect(failures,
            !sim.World.HasComponent(builder, SimComponents.Construct),
            "worker releases construction task");
    }

    private static void CheckDestroyRuins(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        for (int wallY = 0; wallY < 16; wallY++)
        {
            grid.SetBlocked(3, wallY, true);
        }
        var sim = new GameSim(grid, 504);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 6, 6, 0);
        sim.SpawnUnit(8, 7, 1, 100, 60, 1.0, 3.0, 0.0);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateRuins,
            "destroyed building collapses into ruins");
        Expect(failures, sim.World.IsAlive(building), "ruins keep their entity");
        Expect(failures, !grid.IsBuildingBlocked(6, 6), "ruins release building block");
        Expect(failures, !grid.IsBlocked(7, 7), "ruins are walkable");
        Expect(failures, grid.IsTerrainBlocked(3, 3), "ruins keep terrain wall intact");
        Expect(failures,
            !sim.World.HasComponent(building, SimComponents.Health),
            "ruins no longer targetable");
        Expect(failures, sim.FieldFor(6, 6).Reachable(9, 9), "path reopens through ruins");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        Expect(failures, RunBuildCycle() == RunBuildCycle(), "identical build cycles share checksum");
    }

    private static long RunBuildCycle()
    {
        var sim = new GameSim(new NavGrid(20, 20), 505);
        long building = sim.IssuePlaceBuilding(BuildingTypes.Barracks, 4, 4, 0);
        long builder = sim.SpawnVillager(10, 6, 0, 0.4, 20, 0.5);
        sim.IssueConstruct(builder, building);
        for (int tick = 0; tick < 220; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }

    private static void CheckCommandRoundtrip(List<string> failures)
    {
        var place = new LockstepCommand(
            1,
            LockstepCommand.KindPlaceBuilding,
            -1,
            7,
            9,
            -1,
            -1,
            -1,
            0,
            0,
            BuildingTypes.Tower,
            0);
        LockstepCommand restored = LockstepCommand.FromArray(place.ToArray());
        Expect(failures, restored.Kind == LockstepCommand.KindPlaceBuilding, "placement kind survives array roundtrip");
        Expect(failures, restored.Aux == BuildingTypes.Tower, "build type survives array roundtrip");
        Expect(failures, restored.CellX == 7 && restored.CellY == 9, "placement cell survives array roundtrip");
        LockstepCommand legacy = LockstepCommand.FromArray(new long[]
        {
            0, LockstepCommand.KindMove, 3, 4, 5,
        });
        Expect(failures, legacy.Aux == 0, "short payload defaults build type");

        var sim = new GameSim(new NavGrid(12, 12), 506);
        sim.ApplyCommand(place);
        Expect(failures, sim.Grid.IsBuildingBlocked(7, 9), "place command reaches building system");
    }
}
