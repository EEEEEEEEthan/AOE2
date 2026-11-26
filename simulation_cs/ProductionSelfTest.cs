using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>
/// 生产队列 C# 内核自测：入队扣费、资源不足拒绝、计时出生、队列顺序、取消退款、摧毁丢队列、
/// 连续生产、人口满员等待、协议快照与确定性。
/// </summary>
public static class ProductionSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckEnqueueDeduct(failures);
        CheckInsufficientResources(failures);
        CheckQueueLimit(failures);
        CheckTimerSpawns(failures);
        CheckQueueOrder(failures);
        CheckCancelRefund(failures);
        CheckDestroyLosesQueue(failures);
        CheckRepeat(failures);
        CheckPopulationWait(failures);
        CheckSnapshot(failures);
        CheckCommandRoundtrip(failures);
        CheckDeterminism(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static GameSim NewSim(long seed, bool withResources = true)
    {
        var sim = new GameSim(new NavGrid(24, 24), seed);
        if (withResources)
        {
            for (long kind = 0; kind < 4; kind++)
            {
                sim.AddResource(0, kind, 2000);
            }
        }
        sim.AddPopulationCap(0, 50);
        return sim;
    }

    private static long Barracks(GameSim sim) =>
        sim.SpawnCompletedBuilding(4, 4, 0, BuildingTypes.Barracks);

    private static long Cost(GameSim sim, long unitType, long kind) =>
        UnitDefs.Cost(unitType).AmountOf(kind);

    private static long Prod(GameSim sim, long building, string column) =>
        sim.World.GetValue(building, SimComponents.Production, column);

    private static long CountUnits(GameSim sim, long team)
    {
        long count = 0;
        foreach (long id in sim.World.Entities())
        {
            if (sim.World.HasComponent(id, SimComponents.Combat)
                && sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                count += 1;
            }
        }
        return count;
    }

    private static long FirstUnitType(GameSim sim, long team)
    {
        foreach (long id in sim.World.Entities())
        {
            if (sim.World.HasComponent(id, SimComponents.Combat)
                && sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                return sim.World.GetValue(id, SimComponents.Combat, SimComponents.UnitType);
            }
        }
        return -1;
    }

    private static void CheckEnqueueDeduct(List<string> failures)
    {
        var sim = NewSim(601);
        long building = Barracks(sim);
        long foodBefore = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, sim.Train(building, UnitDefs.Swordsman), "swordsman training accepted");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood) == foodBefore - Cost(sim, UnitDefs.Swordsman, GameSim.ResourceFood),
            "food paid on enqueue");
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) == UnitDefs.Swordsman, "current unit starts");
        Expect(failures,
            Prod(sim, building, SimComponents.ProdTicksLeft) == UnitCatalog.Ticks(UnitDefs.Swordsman),
            "current unit starts with full timer");
        Expect(failures, !sim.Train(building, UnitDefs.Villager), "barracks cannot train villager");
    }

    private static void CheckInsufficientResources(List<string> failures)
    {
        var sim = NewSim(602, withResources: false);
        long building = Barracks(sim);
        Expect(failures, !sim.Train(building, UnitDefs.Swordsman), "no resources rejects training");
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) == 0, "rejected training charges nothing");
        Expect(failures, !sim.World.HasComponent(building, SimComponents.Production), "rejected training leaves no queue");
    }

    private static void CheckQueueLimit(List<string> failures)
    {
        var sim = NewSim(603);
        long building = Barracks(sim);
        for (int index = 0; index < UnitCatalog.QueueDepth; index++)
        {
            Expect(failures, sim.Train(building, UnitDefs.Swordsman), $"queue slot {index} accepts");
        }
        Expect(failures, !sim.Train(building, UnitDefs.Swordsman), "queue full rejects further training");
    }

    private static void CheckTimerSpawns(List<string> failures)
    {
        var sim = NewSim(604);
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Swordsman);
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CountUnits(sim, 0) == 0, "no spawn before timer ends");
        sim.SimulateTick();
        Expect(failures, CountUnits(sim, 0) == 1, "spawn exactly when timer reaches zero");
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) < 0, "queue clears after spawn");
    }

    private static void CheckQueueOrder(List<string> failures)
    {
        var sim = NewSim(605);
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Swordsman);
        sim.Train(building, UnitDefs.Spearman);
        sim.Train(building, UnitDefs.Archer);
        Expect(failures, Prod(sim, building, SimComponents.ProdQueueCount) == 2, "two units queued behind current");
        for (int tick = 0; tick < 110; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CountUnits(sim, 0) == 3, "all three queued units are produced");
        Expect(failures, FirstUnitType(sim, 0) == UnitDefs.Swordsman, "queue produces in enqueue order");
    }

    private static void CheckCancelRefund(List<string> failures)
    {
        var sim = NewSim(606);
        long building = Barracks(sim);
        long foodBefore = sim.Resource(0, GameSim.ResourceFood);
        sim.Train(building, UnitDefs.Swordsman);
        Expect(failures, sim.CancelTraining(building, 0), "cancel current accepted");
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) == foodBefore, "cancel current refunds food");
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) < 0, "current cleared after cancel");

        long archerGold = sim.Resource(0, GameSim.ResourceGold);
        sim.Train(building, UnitDefs.Spearman);
        sim.Train(building, UnitDefs.Archer);
        Expect(failures, sim.CancelTraining(building, 1), "cancel queued slot accepted");
        Expect(failures, sim.Resource(0, GameSim.ResourceGold) == archerGold, "cancel queued refunds archer gold");
        Expect(failures, Prod(sim, building, SimComponents.ProdQueueCount) == 0, "cancel shifts queue");
    }

    private static void CheckDestroyLosesQueue(List<string> failures)
    {
        var sim = NewSim(607);
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Swordsman);
        BuildingSystem.Ruin(sim.World, sim.Grid, building);
        Expect(failures, !sim.World.HasComponent(building, SimComponents.Production), "ruins drop production queue");
        for (int tick = 0; tick < 60; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CountUnits(sim, 0) == 0, "destroyed building spawns nothing");
    }

    private static void CheckRepeat(List<string> failures)
    {
        var sim = NewSim(608);
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Swordsman);
        Expect(failures, sim.SetRepeatProduction(building, true), "repeat toggled on");
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CountUnits(sim, 0) >= 2, "repeat production keeps training");
    }

    private static void CheckPopulationWait(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 609);
        for (long kind = 0; kind < 4; kind++)
        {
            sim.AddResource(0, kind, 2000);
        }
        sim.AddPopulationCap(0, 1);
        long filler = sim.SpawnUnit(20, 20, 0, UnitDefs.Swordsman, CombatStance.Aggressive, 1);
        Expect(failures, filler >= 0, "filler unit consumes the only population");
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Swordsman);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CountUnits(sim, 0) == 1, "full population blocks spawn");
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) == UnitDefs.Swordsman,
            "blocked item stays at head of queue");
        Expect(failures, Prod(sim, building, SimComponents.ProdTicksLeft) == 0, "blocked item waits at zero");
        PopulationSystem.Release(sim.World, filler);
        sim.World.Despawn(filler);
        sim.SimulateTick();
        Expect(failures, CountUnits(sim, 0) == 1, "spawn resumes after population frees");
    }

    private static void CheckSnapshot(List<string> failures)
    {
        var sim = NewSim(610);
        long building = Barracks(sim);
        sim.Train(building, UnitDefs.Archer);
        sim.Train(building, UnitDefs.Archer);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        bool found = false;
        foreach (EntitySnapshot entity in decoded.Entities)
        {
            if (entity.Id == building)
            {
                found = true;
                Expect(failures, entity.ProdUnitType == UnitDefs.Archer, "snapshot carries current unit");
                Expect(failures, entity.ProdQueueCount == 1, "snapshot carries queue count");
            }
        }
        Expect(failures, found, "building present in snapshot");
    }

    private static void CheckCommandRoundtrip(List<string> failures)
    {
        var sim = NewSim(611);
        long building = Barracks(sim);
        sim.ApplyCommand(new LockstepCommand(
            0, LockstepCommand.KindTrain, building, 0, 0, -1, -1, -1, 0, 0, UnitDefs.Spearman));
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) == UnitDefs.Spearman,
            "train command reaches production system");
        sim.ApplyCommand(new LockstepCommand(
            0, LockstepCommand.KindCancelTrain, building, 0));
        Expect(failures, Prod(sim, building, SimComponents.ProdCurrent) < 0,
            "cancel command clears current unit");

        LockstepCommand command = new LockstepCommand(
            0, LockstepCommand.KindTrain, building, 0, 0, -1, -1, -1, 0, 0, UnitDefs.Archer);
        LockstepCommand restored = LockstepCommand.FromArray(command.ToArray());
        Expect(failures, restored.Kind == LockstepCommand.KindTrain, "train kind survives array roundtrip");
        Expect(failures, restored.Aux == UnitDefs.Archer, "train unit type survives array roundtrip");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        Expect(failures, RunCycle() == RunCycle(), "identical production cycles share checksum");
    }

    private static long RunCycle()
    {
        var sim = NewSim(612);
        long building = Barracks(sim);
        sim.IssueSetRally(building, 12, 12, -1);
        sim.Train(building, UnitDefs.Swordsman);
        sim.Train(building, UnitDefs.Archer);
        sim.SetRepeatProduction(building, true);
        for (int tick = 0; tick < 90; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }
}
