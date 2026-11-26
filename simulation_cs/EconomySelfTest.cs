using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>经济系统语义与快照暴露自测；返回空列表表示全过。</summary>
public static class EconomySelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckGatherConservation(failures);
        CheckSnapshotExposesEconomy(failures);
        CheckSnapshotEconomyRoundtrip(failures);
        CheckDepletedNodeEndsCycle(failures);
        CheckFarmSustainsFood(failures);
        CheckChecksumTracksEconomy(failures);
        CheckDeterministic(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static void CheckGatherConservation(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 901, 2, 6);
        long initialAmount = 100;
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceWood, initialAmount);
        long drop = sim.SpawnDropSite(10, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 4, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
            Expect(failures, GetGather(sim, villager, SimComponents.CarryAmount) <= 4, "gather carry stays within capacity");
        }
        long inventory = sim.Resource(0, GameSim.ResourceWood);
        long carried = GetGather(sim, villager, SimComponents.CarryAmount);
        long remaining = sim.World.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining);
        Expect(failures, inventory > 0, "gather delivers into player inventory");
        Expect(failures, inventory + carried + remaining == initialAmount, "gather conserves wood");
    }

    private static void CheckSnapshotExposesEconomy(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 902, 2, 6, MatchConfig.Standard);
        long initialAmount = 100;
        sim.SpawnUnit(15, 15, 1, 50, 0, 0.0, 0.0, 0.0);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceGold, initialAmount);
        long drop = sim.SpawnDropSite(10, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 4, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn, sim.Match);
        EntitySnapshot nodeSnapshot = FindEntity(frame, node);
        Expect(failures, nodeSnapshot.NodeType == GameSim.ResourceGold, "snapshot carries node type");
        Expect(failures,
            nodeSnapshot.NodeRemaining > 0 && nodeSnapshot.NodeRemaining < initialAmount,
            "snapshot carries decreasing node remaining");
        EntitySnapshot villagerSnapshot = FindEntity(frame, villager);
        Expect(failures,
            villagerSnapshot.GatherState != GatherSystem.GatherStateIdle,
            "snapshot carries villager gather state");
        Expect(failures, frame.Players.Length == 2, "snapshot carries both players");
        Expect(failures, frame.Players[0].Gold > 0, "snapshot carries player gold inventory");
        Expect(failures, frame.Players[1].Gold == 0, "other player inventory stays empty");
    }

    private static void CheckSnapshotEconomyRoundtrip(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 903, 2, 6, MatchConfig.Standard);
        sim.SpawnUnit(14, 14, 1, 50, 0, 0.0, 0.0, 0.0);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceStone, 50);
        long drop = sim.SpawnDropSite(12, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 3; tick++)
        {
            sim.SimulateTick();
        }
        sim.AddResource(0, GameSim.ResourceStone, 7);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn, sim.Match);
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        EntitySnapshot nodeSnapshot = FindEntity(frame, node);
        EntitySnapshot nodeRestored = FindEntity(decoded, node);
        EntitySnapshot villagerSnapshot = FindEntity(frame, villager);
        EntitySnapshot villagerRestored = FindEntity(decoded, villager);
        Expect(failures,
            nodeRestored.NodeType == nodeSnapshot.NodeType
            && nodeRestored.NodeRemaining == nodeSnapshot.NodeRemaining,
            "snapshot roundtrip keeps node type and remaining");
        Expect(failures, villagerSnapshot.CarryAmount > 0, "villager is carrying before roundtrip");
        Expect(failures,
            villagerRestored.CarryAmount == villagerSnapshot.CarryAmount
            && villagerRestored.CarryType == villagerSnapshot.CarryType
            && villagerRestored.GatherState == villagerSnapshot.GatherState,
            "snapshot roundtrip keeps villager gather fields");
        Expect(failures,
            decoded.Players[0].Stone == frame.Players[0].Stone && frame.Players[0].Stone == 7,
            "snapshot roundtrip keeps player inventory");
    }

    private static void CheckDepletedNodeEndsCycle(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 904, 2, 6);
        long initialAmount = 3;
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceStone, initialAmount);
        long drop = sim.SpawnDropSite(9, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining) == 0,
            "depleted node has no resources left");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceStone) == initialAmount,
            "inventory equals the depleted node amount");
        Expect(failures,
            GetGather(sim, villager, SimComponents.GatherState) == GatherSystem.GatherStateIdle,
            "villager idles after the node is depleted");
        Expect(failures, GetGather(sim, villager, SimComponents.CarryAmount) == 0, "villager unloads before idling");
    }

    private static void CheckChecksumTracksEconomy(List<string> failures)
    {
        Expect(failures, EconomyChecksum(50) != EconomyChecksum(51), "resource node remaining participates in checksum");
        Expect(failures, EconomyChecksum(50) == EconomyChecksum(50), "economy checksum is reproducible");
        Expect(failures, FarmChecksum(250) != FarmChecksum(100), "farm food remaining participates in checksum");
    }

    private static void CheckFarmSustainsFood(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 907);
        sim.AddResource(0, GameSim.ResourceWood, 500);
        long farm = sim.IssuePlaceBuilding(BuildingTypes.Farm, 4, 4, 0);
        long builder = sim.SpawnVillager(10, 5, 0, 0.5, 20, 0.5);
        sim.IssueConstruct(builder, farm);
        for (int tick = 0; tick < 400; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.NodeType)
                == GameSim.ResourceFood,
            "farm node feeds the harvest loop");
        long drop = sim.SpawnDropSite(10, 10, 0b1111);
        long villager = sim.SpawnVillager(7, 4, 0, 0.2, 10, 1.0);
        sim.IssueGather(villager, farm, drop);
        for (int tick = 0; tick < 60; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) > 0, "farm harvest reaches player inventory");
        long remainingAfterHarvest =
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining);
        Expect(failures, remainingAfterHarvest < BuildingTypes.FarmFoodAmount, "harvest drains farm food");
        sim.World.SetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining, 0);
        long wood = sim.Resource(0, GameSim.ResourceWood);
        Expect(failures, wood >= BuildingTypes.FarmReseedCost, "harvest leaves enough wood to reseed");
        Expect(failures, sim.IssueReseedFarm(farm, 0), "empty farm reseeds");
        Expect(failures,
            sim.World.GetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining)
                == BuildingTypes.FarmFoodAmount,
            "reseeded farm refills to full");
    }

    private static long FarmChecksum(long remaining)
    {
        var sim = new GameSim(new NavGrid(16, 16), 908);
        sim.AddResource(0, GameSim.ResourceWood, 100);
        long farm = sim.IssuePlaceBuilding(BuildingTypes.Farm, 4, 4, 0);
        sim.World.SetValue(
            farm, SimComponents.Building, SimComponents.Progress, BuildingTypes.BuildTicks(BuildingTypes.Farm));
        sim.World.SetValue(
            farm, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        BuildingSystem.ReseedFarm(sim.World, farm);
        sim.World.SetValue(farm, SimComponents.ResourceNode, SimComponents.Remaining, remaining);
        return sim.World.Checksum();
    }

    private static long EconomyChecksum(long nodeAmount)
    {
        var sim = new GameSim(new NavGrid(16, 16), 905, 2, 6);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceWood, nodeAmount);
        long drop = sim.SpawnDropSite(8, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.0, 10, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 5; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }

    private static void CheckDeterministic(List<string> failures)
    {
        Expect(failures, EconomyRunChecksum() == EconomyRunChecksum(), "economy run is deterministic");
        Expect(failures, EconomyRunChecksum() != 0, "economy checksum is populated");
    }

    private static long EconomyRunChecksum()
    {
        var sim = new GameSim(new NavGrid(24, 24), 906, 2, 6);
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

    private static long GetGather(GameSim sim, long id, string column) =>
        sim.World.GetValue(id, SimComponents.Gather, column);

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
}
