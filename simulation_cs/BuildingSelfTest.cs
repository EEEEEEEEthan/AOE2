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
        CheckInsufficientResources(failures);
        CheckPlaceDeductsCost(failures);
        CheckFarmLifecycle(failures);
        CheckFarmReseed(failures);
        CheckFarmRuin(failures);
        CheckRepair(failures);
        CheckRepairResourceShortage(failures);
        CheckRepairRejects(failures);
        CheckRepairDeterminism(failures);
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
        sim.AddResource(0, GameSim.ResourceWood, 100);
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
        sim.AddResource(0, GameSim.ResourceWood, 100);
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
        sim.AddResource(0, GameSim.ResourceWood, 100);
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 3, 3, 0) == -1, "water rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 6, 3, 0) == -1, "cliff rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 12, 12, 0) == -1, "sloped footprint rejected");
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 10, 10, 0) >= 0, "flat plateau accepted");
    }

    private static void CheckConstruction(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 503);
        sim.AddResource(0, GameSim.ResourceWood, 100);
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
        sim.AddResource(0, GameSim.ResourceWood, 100);
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

    private static void CheckInsufficientResources(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 508);
        sim.AddResource(0, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.House) - 1);
        long before = sim.Resource(0, GameSim.ResourceWood);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        Expect(failures, building == -1, "insufficient resources reject placement");
        Expect(failures, !grid.IsBuildingBlocked(4, 4), "rejected placement leaves no foundation");
        Expect(failures, sim.Resource(0, GameSim.ResourceWood) == before, "rejected placement spends nothing");
    }

    private static void CheckPlaceDeductsCost(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 509);
        long cost = BuildingTypes.Cost(BuildingTypes.House);
        long before = cost + 100;
        sim.AddResource(0, GameSim.ResourceWood, before);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        Expect(failures, building >= 0, "affordable placement accepted");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == before - cost,
            "placement deducts the building cost");
    }

    private static void CheckFarmLifecycle(List<string> failures)
    {
        var grid = new NavGrid(20, 20);
        var sim = new GameSim(grid, 510);
        sim.AddResource(0, GameSim.ResourceWood, 500);
        long farm = sim.IssuePlaceBuilding(BuildingTypes.Farm, 4, 4, 0);
        Expect(failures, farm >= 0, "farm placed on open ground");
        Expect(failures, grid.IsBuildingBlocked(4, 4) && grid.IsBuildingBlocked(6, 6), "farm occupies its 3x3 footprint");
        Expect(failures, grid.IsBuildingBlocked(4, 4) && !grid.IsBuildingBlocked(7, 4), "farm does not exceed its footprint");
        Expect(failures,
            !sim.World.HasComponent(farm, SimComponents.ResourceNode),
            "farm carries no food node before completion");
        long builder = sim.SpawnVillager(10, 5, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, farm);
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateComplete,
            "farm construction completes");
        Expect(failures, sim.World.HasComponent(farm, SimComponents.ResourceNode), "completed farm grows a food node");
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.NodeType)
                == GameSim.ResourceFood,
            "farm node holds food");
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining)
                == BuildingTypes.FarmFoodAmount,
            "farm node starts full");
    }

    private static void CheckFarmReseed(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 511);
        sim.AddResource(0, GameSim.ResourceWood, 500);
        long farm = sim.IssuePlaceBuilding(BuildingTypes.Farm, 4, 4, 0);
        long builder = sim.SpawnVillager(10, 5, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, farm);
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, !sim.IssueReseedFarm(farm, 0), "half-full farm rejects reseed");
        sim.World.SetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining, 0);
        long before = sim.Resource(0, GameSim.ResourceWood);
        Expect(failures, sim.IssueReseedFarm(farm, 0), "empty farm accepts reseed");
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining)
                == BuildingTypes.FarmFoodAmount,
            "reseed refills the food node");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == before - BuildingTypes.FarmReseedCost,
            "reseed deducts wood");
        sim.World.SetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining, 0);
        sim.AddResource(0, GameSim.ResourceWood, -sim.Resource(0, GameSim.ResourceWood));
        Expect(failures, !sim.IssueReseedFarm(farm, 0), "reseed without wood is rejected");
        long foodBefore = sim.Resource(0, GameSim.ResourceFood);
        long drop = sim.SpawnDropSite(10, 10, 0b1111);
        long villager = sim.SpawnVillager(7, 4, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, farm, drop);
        sim.World.SetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining, 2);
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) == foodBefore + 2, "villagers harvest food from the farm");
    }

    private static void CheckFarmRuin(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 512);
        sim.AddResource(0, GameSim.ResourceWood, 500);
        long farm = sim.IssuePlaceBuilding(BuildingTypes.Farm, 4, 4, 0);
        long builder = sim.SpawnVillager(10, 5, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, farm);
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
        }
        sim.SpawnUnit(9, 4, 1, 100, 600, 1.0, 6.0, 0.0);
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateRuins,
            "destroyed farm turns into ruins");
        Expect(failures, !sim.World.HasComponent(farm, SimComponents.ResourceNode), "ruined farm loses its food node");
    }

    private static void CheckRepair(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 520);
        sim.AddResource(0, GameSim.ResourceWood, 100);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 5, 5, 0);
        long builder = sim.SpawnVillager(9, 6, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, building);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        long maxHp = sim.World.GetValue(building, SimComponents.Health, SimComponents.MaxHp);
        sim.World.SetValue(building, SimComponents.Health, SimComponents.Hp, maxHp - 100);
        long repairer = sim.SpawnVillager(7, 5, 0, 0.5, 20, 0.5);
        Expect(failures, sim.IssueRepair(repairer, building, 0), "villager accepts repair of damaged building");
        Expect(failures, sim.World.HasComponent(repairer, SimComponents.Repair), "repair task attaches to villager");
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
        EntitySnapshot repairSnapshot = FindEntity(frame, repairer);
        Expect(failures,
            repairSnapshot.RepairState == RepairSystem.StateActive && repairSnapshot.RepairTarget == building,
            "snapshot carries repair state and target");
        long woodBefore = sim.Resource(0, GameSim.ResourceWood);
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Health, SimComponents.Hp) == maxHp,
            "repair restores building to full health");
        Expect(failures,
            woodBefore - sim.Resource(0, GameSim.ResourceWood) == 10,
            "repair spends wood per repaired tick");
        Expect(failures, !sim.World.HasComponent(repairer, SimComponents.Repair), "repair task ends at full health");
    }

    private static void CheckRepairResourceShortage(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 521);
        sim.AddResource(0, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.House));
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 5, 5, 0);
        sim.World.SetValue(building, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        long maxHp = sim.World.GetValue(building, SimComponents.Health, SimComponents.MaxHp);
        sim.World.SetValue(building, SimComponents.Health, SimComponents.Hp, maxHp - 100);
        long repairer = sim.SpawnVillager(7, 5, 0, 0.5, 20, 0.5);
        Expect(failures, sim.IssueRepair(repairer, building, 0), "repair accepted regardless of stockpile");
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(building, SimComponents.Health, SimComponents.Hp) == maxHp - 100,
            "repair stalls without wood");
    }

    private static void CheckRepairRejects(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 522);
        sim.AddResource(0, GameSim.ResourceWood, 200);
        long foundation = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        long repairer = sim.SpawnVillager(10, 10, 0, 0.5, 20, 0.5);
        Expect(failures, !sim.IssueRepair(repairer, foundation, 0), "foundation cannot be repaired");
        long complete = sim.IssuePlaceBuilding(BuildingTypes.House, 8, 8, 0);
        sim.World.SetValue(complete, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        Expect(failures, !sim.IssueRepair(repairer, complete, 0), "full health building cannot be repaired");
        sim.World.SetValue(complete, SimComponents.Health, SimComponents.Hp, 10);
        long enemy = sim.SpawnVillager(9, 8, 1, 0.5, 20, 0.5);
        Expect(failures, !sim.IssueRepair(enemy, complete, 1), "enemy cannot repair foreign building");
        Expect(failures, !sim.IssueRepair(repairer, complete, 1), "command player must own the villager");
    }

    private static void CheckRepairDeterminism(List<string> failures)
    {
        Expect(failures, RunRepairCycle(true) == RunRepairCycle(true), "repair cycles are deterministic");
        Expect(failures, RunRepairCycle(true) != RunRepairCycle(false), "repair state participates in checksum");
    }

    private static long RunRepairCycle(bool repair)
    {
        var sim = new GameSim(new NavGrid(16, 16), 523);
        sim.AddResource(0, GameSim.ResourceWood, 300);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 5, 5, 0);
        sim.World.SetValue(building, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        long maxHp = sim.World.GetValue(building, SimComponents.Health, SimComponents.MaxHp);
        sim.World.SetValue(building, SimComponents.Health, SimComponents.Hp, maxHp - 100);
        long repairer = sim.SpawnVillager(7, 5, 0, 0.5, 20, 0.5);
        if (repair)
        {
            sim.IssueRepair(repairer, building, 0);
        }
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }

    private static EntitySnapshot FindEntity(SnapshotFrame frame, long id)
    {
        foreach (EntitySnapshot entity in frame.Entities)
        {
            if (entity.Id == id)
            {
                return entity;
            }
        }
        return default;
    }

    private static void CheckDeterminism(List<string> failures)
    {
        Expect(failures, RunBuildCycle() == RunBuildCycle(), "identical build cycles share checksum");
    }

    private static long RunBuildCycle()
    {
        var sim = new GameSim(new NavGrid(20, 20), 505);
        sim.AddResource(0, GameSim.ResourceWood, 200);
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

        Expect(failures, LockstepCommand.KindRepair == 22, "repair kind keeps its wire number");
        var repair = new LockstepCommand(0, LockstepCommand.KindRepair, 4, 0, 0, 5);
        LockstepCommand repairRestored = LockstepCommand.FromArray(repair.ToArray());
        Expect(failures,
            repairRestored.Kind == LockstepCommand.KindRepair
                && repairRestored.Entity == 4
                && repairRestored.TargetEntity == 5,
            "repair command survives array roundtrip");

        var sim = new GameSim(new NavGrid(12, 12), 506);
        sim.AddResource(place.Player, GameSim.ResourceWood, 200);
        sim.ApplyCommand(place);
        Expect(failures, sim.Grid.IsBuildingBlocked(7, 9), "place command reaches building system");
    }
}
