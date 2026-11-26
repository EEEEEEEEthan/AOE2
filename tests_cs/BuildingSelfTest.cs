using System.Collections.Generic;
using AoeSim;

using static AOE2Framework.SelfTestBase;

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
        CheckMultiResourceShortage(failures);
        CheckPlaceDeductsCost(failures);
        CheckPlaceDeductsByResourceKind(failures);
        CheckFarmLifecycle(failures);
        CheckFarmReseed(failures);
        CheckFarmRuin(failures);
        CheckDropBuildings(failures);
        CheckRepair(failures);
        CheckRepairResourceShortage(failures);
        CheckRepairRejects(failures);
        CheckRepairDeterminism(failures);
        CheckWallTypes(failures);
        CheckGateTogglePathing(failures);
        CheckGateDestroyGap(failures);
        CheckGateSnapshotAndDeterminism(failures);
        CheckDeterminism(failures);
        CheckCommandRoundtrip(failures);
        CheckWallSegmentRasterize(failures);
        CheckWallSegmentPlacement(failures);
        CheckWallSegmentAtomicReject(failures);
        CheckWallSegmentConnections(failures);
        CheckWallSegmentClosedLoop(failures);
        CheckWallSegmentDeterminism(failures);
        CheckWallSegmentCommand(failures);
        return failures;
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
        Expect(failures, sim.IssuePlaceBuilding(BuildingTypes.House, 12, 12, 0) >= 0, "sloped footprint accepted");
        Expect(failures,
            sim.IssuePlaceBuilding(BuildingTypes.House, 10, 10, 0) >= 0,
            "flat plateau accepted");
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
        sim.AddResource(0, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.House).Wood - 1);
        long before = sim.Resource(0, GameSim.ResourceWood);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        Expect(failures, building == -1, "insufficient resources reject placement");
        Expect(failures, !grid.IsBuildingBlocked(4, 4), "rejected placement leaves no foundation");
        Expect(failures, sim.Resource(0, GameSim.ResourceWood) == before, "rejected placement spends nothing");
    }

    private static void CheckMultiResourceShortage(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 513);
        ResourceCost cost = BuildingTypes.Cost(BuildingTypes.Tower);
        sim.AddResource(0, GameSim.ResourceWood, cost.Wood + 100);
        sim.AddResource(0, GameSim.ResourceStone, cost.Stone - 1);
        long woodBefore = sim.Resource(0, GameSim.ResourceWood);
        long stoneBefore = sim.Resource(0, GameSim.ResourceStone);
        long building = sim.IssuePlaceBuilding(BuildingTypes.Tower, 4, 4, 0);
        Expect(failures, building == -1, "short stone rejects tower despite ample wood");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == woodBefore
                && sim.Resource(0, GameSim.ResourceStone) == stoneBefore,
            "rejected multi-resource placement spends nothing");
    }

    private static void CheckPlaceDeductsCost(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 509);
        long cost = BuildingTypes.Cost(BuildingTypes.House).Wood;
        long before = cost + 100;
        sim.AddResource(0, GameSim.ResourceWood, before);
        long building = sim.IssuePlaceBuilding(BuildingTypes.House, 4, 4, 0);
        Expect(failures, building >= 0, "affordable placement accepted");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == before - cost,
            "placement deducts the building cost");
    }

    private static void CheckPlaceDeductsByResourceKind(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 514);
        ResourceCost tower = BuildingTypes.Cost(BuildingTypes.Tower);
        sim.AddResource(0, GameSim.ResourceWood, tower.Wood + 100);
        sim.AddResource(0, GameSim.ResourceStone, tower.Stone + 100);
        long woodBefore = sim.Resource(0, GameSim.ResourceWood);
        long stoneBefore = sim.Resource(0, GameSim.ResourceStone);
        long building = sim.IssuePlaceBuilding(BuildingTypes.Tower, 4, 4, 0);
        Expect(failures, building >= 0, "affordable two-resource tower accepted");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == woodBefore - tower.Wood
                && sim.Resource(0, GameSim.ResourceStone) == stoneBefore - tower.Stone,
            "tower cost deducts wood and stone separately");

        var wallSim = new GameSim(new NavGrid(16, 16), 515);
        ResourceCost wall = BuildingTypes.Cost(BuildingTypes.Wall);
        wallSim.AddResource(0, GameSim.ResourceWood, 100);
        wallSim.AddResource(0, GameSim.ResourceStone, wall.Stone + 100);
        long wallWoodBefore = wallSim.Resource(0, GameSim.ResourceWood);
        long wallStoneBefore = wallSim.Resource(0, GameSim.ResourceStone);
        long wallId = wallSim.IssuePlaceBuilding(BuildingTypes.Wall, 4, 4, 0);
        Expect(failures, wallId >= 0, "wall placeable with stone only");
        Expect(failures,
            wallSim.Resource(0, GameSim.ResourceWood) == wallWoodBefore
                && wallSim.Resource(0, GameSim.ResourceStone) == wallStoneBefore - wall.Stone,
            "wall cost comes from stone, not wood");
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

    private static void CheckDropBuildings(List<string> failures)
    {
        long woodBit = 1L << (int)GameSim.ResourceWood;
        long foodBit = 1L << (int)GameSim.ResourceFood;
        long goldBit = 1L << (int)GameSim.ResourceGold;
        long stoneBit = 1L << (int)GameSim.ResourceStone;

        var sim = new GameSim(new NavGrid(24, 24), 530);
        sim.AddResource(0, GameSim.ResourceWood, 5000);
        sim.AddResource(0, GameSim.ResourceStone, 1000);
        long townCenter = sim.IssuePlaceBuilding(BuildingTypes.TownCenter, 1, 1, 0);
        long lumber = sim.IssuePlaceBuilding(BuildingTypes.LumberCamp, 12, 12, 0);
        long mill = sim.IssuePlaceBuilding(BuildingTypes.Mill, 12, 4, 0);
        long mining = sim.IssuePlaceBuilding(BuildingTypes.MiningCamp, 4, 12, 0);
        Expect(failures,
            townCenter >= 0 && lumber >= 0 && mill >= 0 && mining >= 0,
            "resource drop buildings can be placed");
        Expect(failures,
            !sim.World.HasComponent(lumber, SimComponents.DropSite),
            "unfinished lumber camp provides no drop site");
        BuildingSystem.Complete(sim.World, townCenter);
        BuildingSystem.Complete(sim.World, lumber);
        BuildingSystem.Complete(sim.World, mill);
        BuildingSystem.Complete(sim.World, mining);
        Expect(failures,
            sim.World.GetValue(lumber, SimComponents.DropSite, SimComponents.AcceptMask) == woodBit,
            "completed lumber camp only accepts wood");
        Expect(failures,
            sim.World.GetValue(mill, SimComponents.DropSite, SimComponents.AcceptMask) == foodBit,
            "completed mill only accepts food");
        Expect(failures,
            sim.World.GetValue(mining, SimComponents.DropSite, SimComponents.AcceptMask) == (goldBit | stoneBit),
            "completed mining camp accepts gold and stone");
        Expect(failures,
            sim.World.GetValue(townCenter, SimComponents.DropSite, SimComponents.AcceptMask) == 0b1111,
            "completed town center accepts all resources");

        long nearLumber = GatherSystem.NearestDropSite(sim.World, 0, GameSim.ResourceWood, 14, 13);
        long nearMining = GatherSystem.NearestDropSite(sim.World, 0, GameSim.ResourceGold, 14, 13);
        long nearMill = GatherSystem.NearestDropSite(sim.World, 0, GameSim.ResourceFood, 14, 13);
        long nearMiningStone = GatherSystem.NearestDropSite(sim.World, 0, GameSim.ResourceStone, 14, 13);
        Expect(failures, nearLumber == lumber, "wood pick selects the nearest lumber camp");
        Expect(failures, nearMining == mining, "gold pick selects the nearest mining camp");
        Expect(failures, nearMill == mill, "food pick selects the nearest mill");
        Expect(failures, nearMiningStone == mining, "stone pick selects the nearest mining camp");

        long woodNode = sim.SpawnResourceNode(14, 12, GameSim.ResourceWood, 50);
        long villager = sim.SpawnVillager(14, 13, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, woodNode, -1);
        Expect(failures,
            sim.World.GetValue(villager, SimComponents.Gather, SimComponents.DropEntity) == lumber,
            "gather command without a drop site auto-selects by resource");

        BuildingSystem.Ruin(sim.World, sim.Grid, lumber);
        Expect(failures,
            !sim.World.HasComponent(lumber, SimComponents.DropSite),
            "ruined building loses its drop site");
        Expect(failures,
            GatherSystem.NearestDropSite(sim.World, 0, GameSim.ResourceWood, 14, 13) == townCenter,
            "ruined lumber camp is skipped, town center takes over");

        var mismatch = new GameSim(new NavGrid(16, 16), 531);
        mismatch.AddResource(0, GameSim.ResourceWood, 500);
        long mismatchMill = mismatch.IssuePlaceBuilding(BuildingTypes.Mill, 4, 4, 0);
        BuildingSystem.Complete(mismatch.World, mismatchMill);
        long carrier = mismatch.SpawnVillager(6, 4, 0, 0.2, 10, 1.0);
        long mismatchWoodBefore = mismatch.Resource(0, GameSim.ResourceWood);
        mismatch.World.SetValue(carrier, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateReturning);
        mismatch.World.SetValue(carrier, SimComponents.Gather, SimComponents.DropEntity, mismatchMill);
        mismatch.World.SetValue(carrier, SimComponents.Gather, SimComponents.CarryType, GameSim.ResourceWood);
        mismatch.World.SetValue(carrier, SimComponents.Gather, SimComponents.CarryAmount, 5);
        mismatch.SimulateTick();
        Expect(failures,
            mismatch.Resource(0, GameSim.ResourceWood) == mismatchWoodBefore,
            "mill rejects a wood delivery");
        Expect(failures,
            mismatch.World.GetValue(carrier, SimComponents.Gather, SimComponents.GatherState)
                == GatherSystem.GatherStateIdle,
            "rejected delivery drops the villager to idle");
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
        sim.AddResource(0, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.House).Wood);
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

    private static void CheckWallTypes(List<string> failures)
    {
        Expect(failures, BuildingTypes.IsKnown(BuildingTypes.PalisadeWall), "palisade wall is a known building");
        Expect(failures, BuildingTypes.IsKnown(BuildingTypes.Gate), "gate is a known building");
        Expect(failures, BuildingTypes.IsKnown(BuildingTypes.FortifiedWall), "fortified wall is a known building");
        Expect(failures,
            BuildingTypes.SizeWidth(BuildingTypes.PalisadeWall) == 1
                && BuildingTypes.SizeHeight(BuildingTypes.Gate) == 1,
            "palisade wall and gate use a 1x1 footprint");
        Expect(failures,
            BuildingTypes.Cost(BuildingTypes.PalisadeWall).Wood > 0
                && BuildingTypes.Cost(BuildingTypes.PalisadeWall).Stone == 0,
            "palisade wall costs wood only");
        Expect(failures,
            BuildingTypes.Cost(BuildingTypes.Gate).Stone > 0
                && BuildingTypes.Cost(BuildingTypes.FortifiedWall).Stone
                    > BuildingTypes.Cost(BuildingTypes.Wall).Stone,
            "gate and fortified wall cost stone, fortified above plain wall");
        Expect(failures,
            BuildingTypes.MaxHp(BuildingTypes.PalisadeWall) < BuildingTypes.MaxHp(BuildingTypes.Wall)
                && BuildingTypes.MaxHp(BuildingTypes.FortifiedWall) > BuildingTypes.MaxHp(BuildingTypes.Wall),
            "wall hp ladder: palisade < stone < fortified");

        var sim = new GameSim(new NavGrid(16, 16), 540);
        sim.AddResource(0, GameSim.ResourceWood, 100);
        long palisade = sim.IssuePlaceBuilding(BuildingTypes.PalisadeWall, 3, 3, 0);
        Expect(failures, palisade >= 0, "palisade placed with wood only");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceWood) == 100 - BuildingTypes.Cost(BuildingTypes.PalisadeWall).Wood,
            "palisade deducts wood");
        Expect(failures, sim.Grid.IsBuildingBlocked(3, 3), "palisade blocks pathing");
    }

    private static void CheckGateTogglePathing(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 541);
        sim.AddResource(0, GameSim.ResourceStone, 200);
        long gate = sim.IssuePlaceBuilding(BuildingTypes.Gate, 5, 5, 0);
        Expect(failures, gate >= 0, "gate placed with stone");
        Expect(failures, grid.IsBuildingBlocked(5, 5), "gate foundation blocks pathing");
        Expect(failures,
            !sim.IssueToggleGate(gate, 0, BuildingSystem.GateOpen),
            "foundation gate rejects toggle");
        BuildingSystem.Complete(sim.World, gate);
        Expect(failures,
            sim.World.GetValue(gate, SimComponents.Building, SimComponents.GateState)
                == BuildingSystem.GateClosed,
            "completed gate defaults to closed");
        Expect(failures, grid.IsStructureBlocked(5, 5), "closed gate blocks movement");

        Expect(failures, sim.IssueToggleGate(gate, 0, BuildingSystem.GateOpen), "owning player opens gate");
        Expect(failures, !grid.IsStructureBlocked(5, 5), "open gate lets units through");
        Expect(failures, !grid.IsBlocked(5, 5), "open gate leaves pathing union open");
        Expect(failures, grid.IsBuildingBlocked(5, 5), "open gate keeps its footprint occupied");
        Expect(failures,
            sim.IssuePlaceBuilding(BuildingTypes.House, 5, 5, 0) == -1,
            "open gate footprint still rejects overlapping building");

        Expect(failures, sim.IssueToggleGate(gate, 0, BuildingSystem.GateLocked), "gate can be locked");
        Expect(failures, grid.IsStructureBlocked(5, 5), "locked gate blocks movement");
        Expect(failures,
            sim.World.GetValue(gate, SimComponents.Building, SimComponents.GateState)
                == BuildingSystem.GateLocked,
            "locked state written to building");
        Expect(failures, sim.IssueToggleGate(gate, 0, BuildingSystem.GateClosed), "gate can close again");
        Expect(failures, grid.IsStructureBlocked(5, 5), "closed gate blocks again");
        Expect(failures, !sim.IssueToggleGate(gate, 1, BuildingSystem.GateOpen), "enemy cannot toggle gate");
        Expect(failures, !sim.IssueToggleGate(gate, 0, 3), "invalid gate state rejected");
    }

    private static void CheckGateDestroyGap(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 542);
        sim.AddResource(0, GameSim.ResourceStone, 200);
        long gate = sim.IssuePlaceBuilding(BuildingTypes.Gate, 5, 5, 0);
        BuildingSystem.Complete(sim.World, gate);
        Expect(failures, grid.IsBlocked(5, 5), "closed gate blocks before attack");
        sim.SpawnUnit(6, 5, 1, 100, 500, 1.0, 1.0, 0.0);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(gate, SimComponents.Building, SimComponents.State)
                == BuildingSystem.StateRuins,
            "attacked gate collapses into ruins");
        Expect(failures, !grid.IsBuildingBlocked(5, 5), "ruined gate releases its footprint");
        Expect(failures, !grid.IsBlocked(5, 5), "ruined gate leaves a walkable gap");
        Expect(failures, sim.FieldFor(5, 5).Reachable(8, 5), "path reopens through the breach");
    }

    private static void CheckGateSnapshotAndDeterminism(List<string> failures)
    {
        long first = RunGateCycle();
        long second = RunGateCycle();
        Expect(failures, first == second, "identical gate cycles share checksum");

        var grid = new NavGrid(16, 16);
        var sim = new GameSim(grid, 543);
        sim.AddResource(0, GameSim.ResourceStone, 200);
        long gate = sim.IssuePlaceBuilding(BuildingTypes.Gate, 5, 5, 0);
        BuildingSystem.Complete(sim.World, gate);
        sim.IssueToggleGate(gate, 0, BuildingSystem.GateOpen);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
        EntitySnapshot snapshot = FindEntity(frame, gate);
        Expect(failures, snapshot.GateState == BuildingSystem.GateOpen, "snapshot carries open gate state");
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        EntitySnapshot restored = FindEntity(decoded, gate);
        Expect(failures, restored.GateState == BuildingSystem.GateOpen, "gate state survives snapshot roundtrip");
    }

    private static long RunGateCycle()
    {
        var sim = new GameSim(new NavGrid(16, 16), 544);
        sim.AddResource(0, GameSim.ResourceStone, 200);
        long gate = sim.IssuePlaceBuilding(BuildingTypes.Gate, 5, 5, 0);
        BuildingSystem.Complete(sim.World, gate);
        sim.IssueToggleGate(gate, 0, BuildingSystem.GateOpen);
        sim.IssueToggleGate(gate, 0, BuildingSystem.GateLocked);
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
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

        var toggle = new LockstepCommand(
            0, LockstepCommand.KindToggleGate, 7, 0, 0, -1, -1, -1, 0, 0, BuildingSystem.GateOpen, 0);
        LockstepCommand toggleRestored = LockstepCommand.FromArray(toggle.ToArray());
        Expect(failures,
            toggleRestored.Kind == LockstepCommand.KindToggleGate
                && toggleRestored.Entity == 7
                && toggleRestored.Aux == BuildingSystem.GateOpen,
            "gate toggle command survives array roundtrip");

        var sim = new GameSim(new NavGrid(12, 12), 506);
        sim.AddResource(place.Player, GameSim.ResourceWood, 200);
        sim.AddResource(place.Player, GameSim.ResourceStone, 200);
        sim.ApplyCommand(place);
        Expect(failures, sim.Grid.IsBuildingBlocked(7, 9), "place command reaches building system");
    }

    private static void CheckWallSegmentRasterize(List<string> failures)
    {
        var straight = WallSegment.Rasterize(2, 3, 6, 3);
        Expect(failures, straight.Count == 5, "horizontal segment spans 5 cells");
        Expect(failures,
            straight[0] == (2, 3) && straight[^1] == (6, 3),
            "horizontal segment keeps both endpoints");
        Expect(failures, straight.TrueForAll(cell => cell.Y == 3), "horizontal segment stays on its row");

        var vertical = WallSegment.Rasterize(4, 1, 4, 5);
        Expect(failures,
            vertical.Count == 5 && vertical[^1] == (4, 5),
            "vertical segment spans 5 cells");
        Expect(failures, WallSegment.Rasterize(3, 3, 3, 3).Count == 1, "single cell segment keeps one cell");

        var corner = WallSegment.Rasterize(2, 2, 5, 4);
        Expect(failures, corner.Count == 6, "L segment counts absX + absY + 1");
        Expect(failures,
            corner[0] == (2, 2) && corner[^1] == (5, 4) && corner.Contains((5, 2)),
            "L segment turns at the dominant-axis corner");
        Expect(failures, !corner.Contains((2, 3)), "L segment does not wander off its two axes");

        var dominantY = WallSegment.Rasterize(2, 2, 3, 6);
        Expect(failures,
            dominantY.Count == 6 && dominantY[^1] == (3, 6) && dominantY.Contains((2, 6)),
            "vertical-dominant L turns on its column first");

        var reversed = WallSegment.Rasterize(5, 4, 2, 2);
        Expect(failures,
            reversed.Count == corner.Count && reversed[0] == (5, 4) && reversed[^1] == (2, 2),
            "reversing endpoints keeps the same cell count");
    }

    private static void CheckWallSegmentPlacement(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 510);
        sim.AddResource(0, GameSim.ResourceStone, 1000);
        long[] ids = sim.IssuePlaceWallSegment(BuildingTypes.Wall, 2, 2, 6, 2, 0);
        Expect(failures, ids.Length == 5, "wall segment places one building per cell");
        bool allBlocked = true;
        for (int cellX = 2; cellX <= 6; cellX++)
        {
            allBlocked &= sim.Grid.IsBuildingBlocked(cellX, 2);
        }
        Expect(failures, allBlocked, "every wall cell blocks its footprint");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceStone)
                == 1000 - BuildingTypes.Cost(BuildingTypes.Wall).Stone * 5,
            "wall segment deducts per-cell stone");

        long[] corner = sim.IssuePlaceWallSegment(BuildingTypes.Wall, 8, 8, 10, 10, 0);
        Expect(failures, corner.Length == 5, "corner segment places its L cells");
        Expect(failures,
            sim.Grid.IsBuildingBlocked(8, 8) && sim.Grid.IsBuildingBlocked(10, 10),
            "corner segment blocks both endpoints");

        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.House, 14, 14, 16, 14, 0).Length == 0,
            "segment placement rejects non-wall types");
        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.Wall, 14, 14, 16, 14, 5).Length == 0,
            "segment placement rejects a player outside the match");
    }

    private static void CheckWallSegmentAtomicReject(List<string> failures)
    {
        var broke = new GameSim(new NavGrid(20, 20), 511);
        broke.AddResource(0, GameSim.ResourceStone, 4);
        Expect(failures,
            broke.IssuePlaceWallSegment(BuildingTypes.Wall, 2, 2, 6, 2, 0).Length == 0,
            "segment rejected when total cost is unaffordable");
        Expect(failures, !broke.Grid.IsBuildingBlocked(2, 2), "rejected segment places nothing");
        Expect(failures,
            broke.Resource(0, GameSim.ResourceStone) == 4,
            "rejected segment deducts nothing");

        var blocked = new GameSim(new NavGrid(20, 20), 512);
        blocked.AddResource(0, GameSim.ResourceWood, 100);
        blocked.AddResource(0, GameSim.ResourceStone, 100);
        long house = blocked.IssuePlaceBuilding(BuildingTypes.House, 10, 10, 0);
        Expect(failures, house >= 0, "house sits in the segment path");
        long stoneBefore = blocked.Resource(0, GameSim.ResourceStone);
        Expect(failures,
            blocked.IssuePlaceWallSegment(BuildingTypes.Wall, 8, 10, 12, 10, 0).Length == 0,
            "segment rejected when one cell overlaps a building");
        Expect(failures, !blocked.Grid.IsBuildingBlocked(8, 10), "no partial cells survive rejection");
        Expect(failures,
            blocked.Resource(0, GameSim.ResourceStone) == stoneBefore,
            "overlapping segment refunds the whole cost");
    }

    private static void CheckWallSegmentConnections(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 520);
        sim.AddResource(0, GameSim.ResourceWood, 400);
        sim.AddResource(0, GameSim.ResourceStone, 400);
        long townCenter = sim.IssuePlaceBuilding(BuildingTypes.TownCenter, 2, 2, 0);
        Expect(failures, townCenter >= 0, "town center placed for connection test");

        var abutting = WallSegment.Rasterize(6, 2, 6, 2);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 6, 2, 6, 2, 0);
        Expect(failures,
            WallSegment.EndpointsConnected(sim.Grid, abutting),
            "a wall cell touching a building footprint counts as connected");

        var lone = WallSegment.Rasterize(12, 12, 12, 12);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 12, 12, 12, 12, 0);
        Expect(failures,
            !WallSegment.EndpointsConnected(sim.Grid, lone),
            "an isolated wall cell is not connected");

        var floating = WallSegment.Rasterize(14, 14, 15, 14);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 14, 14, 15, 14, 0);
        Expect(failures,
            !WallSegment.EndpointsConnected(sim.Grid, floating),
            "a segment touching only itself is not externally connected");

        var halfLinked = WallSegment.Rasterize(12, 13, 13, 13);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 12, 13, 13, 13, 0);
        Expect(failures,
            !WallSegment.EndpointsConnected(sim.Grid, halfLinked),
            "one linked endpoint is not enough for a full connection");
    }

    private static void CheckWallSegmentClosedLoop(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 530);
        sim.AddResource(0, GameSim.ResourceStone, 1000);
        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.Wall, 3, 3, 6, 3, 0).Length == 4,
            "ring top segment placed");
        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.Wall, 6, 4, 6, 6, 0).Length == 3,
            "ring right segment placed");
        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.Wall, 3, 6, 5, 6, 0).Length == 3,
            "ring bottom segment placed");
        Expect(failures,
            sim.FieldFor(12, 12).Reachable(4, 4),
            "interior stays reachable while the ring has a gap");

        var closing = WallSegment.Rasterize(3, 4, 3, 5);
        Expect(failures,
            sim.IssuePlaceWallSegment(BuildingTypes.Wall, 3, 4, 3, 5, 0).Length == 2,
            "ring left segment closes the loop");
        Expect(failures,
            WallSegment.EndpointsConnected(sim.Grid, closing),
            "closing segment links both ends to the existing ring");
        Expect(failures, sim.FieldFor(12, 12).Reachable(12, 11), "outside stays walkable");
        Expect(failures,
            !sim.FieldFor(12, 12).Reachable(4, 4)
                && !sim.FieldFor(12, 12).Reachable(5, 5),
            "completed legal ring seals its interior");
    }

    private static void CheckWallSegmentDeterminism(List<string> failures)
    {
        Expect(failures,
            RunWallSegmentCycle() == RunWallSegmentCycle(),
            "identical wall segment cycles share checksum");
    }

    private static long RunWallSegmentCycle()
    {
        var sim = new GameSim(new NavGrid(20, 20), 535);
        sim.AddResource(0, GameSim.ResourceStone, 500);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 3, 3, 6, 3, 0);
        sim.IssuePlaceWallSegment(BuildingTypes.Wall, 6, 4, 6, 6, 0);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }

    private static void CheckWallSegmentCommand(List<string> failures)
    {
        Expect(failures, LockstepCommand.KindPlaceWallSegment == 33, "wall segment kind keeps its wire number");
        var command = new LockstepCommand(
            0,
            LockstepCommand.KindPlaceWallSegment,
            -1,
            2,
            2,
            -1,
            -1,
            -1,
            6,
            2,
            BuildingTypes.Wall,
            0);
        LockstepCommand restored = LockstepCommand.FromArray(command.ToArray());
        Expect(failures,
            restored.Kind == LockstepCommand.KindPlaceWallSegment
                && restored.CellX == 2
                && restored.CellY == 2
                && restored.Cell2X == 6
                && restored.Cell2Y == 2
                && restored.Aux == BuildingTypes.Wall,
            "wall segment command survives array roundtrip");

        var sim = new GameSim(new NavGrid(12, 12), 545);
        sim.AddResource(0, GameSim.ResourceStone, 500);
        Expect(failures, sim.IsCommandAllowed(command), "wall segment command is a player command");
        sim.ApplyCommand(command);
        bool allBlocked = true;
        for (int cellX = 2; cellX <= 6; cellX++)
        {
            allBlocked &= sim.Grid.IsBuildingBlocked(cellX, 2);
        }
        Expect(failures, allBlocked, "wall segment command reaches the building system");
    }
}
