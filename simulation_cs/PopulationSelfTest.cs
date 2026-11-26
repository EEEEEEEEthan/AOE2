using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>人口账本与生成校验的语义断言；返回空列表表示全过。</summary>
public static class PopulationSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckCapAndReserve(failures);
        CheckRejectWhenFull(failures);
        CheckReleaseOnDeath(failures);
        CheckCapClamp(failures);
        CheckUnconfiguredUnlimited(failures);
        CheckVillagerCost(failures);
        CheckLedgerHiddenFromSnapshot(failures);
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

    private static void CheckCapAndReserve(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 401);
        long cap = sim.AddPopulationCap(0, 10);
        long unit = sim.SpawnUnit(4, 4, 0, 50, 5, 1.0, 1.0, 0.0, popCost: 3);
        Expect(failures, cap == 10, "cap added from zero");
        Expect(failures, unit >= 0, "unit within cap spawns");
        Expect(failures, sim.PopulationUsed(0) == 3, "spawn reserves population");
        Expect(failures, sim.World.HasComponent(unit, SimComponents.Pop), "unit carries pop cost component");
    }

    private static void CheckRejectWhenFull(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 402);
        sim.AddPopulationCap(0, 5);
        long filled = sim.SpawnUnit(4, 4, 0, 50, 5, 1.0, 1.0, 0.0, popCost: 5);
        long before = sim.World.NextEntity;
        long rejected = sim.SpawnUnit(5, 5, 0, 50, 5, 1.0, 1.0, 0.0, popCost: 1);
        Expect(failures, filled >= 0, "cap-exact spawn succeeds");
        Expect(failures, rejected == -1, "spawn over cap is rejected");
        Expect(failures, sim.PopulationUsed(0) == 5, "rejection keeps used unchanged");
        Expect(failures, sim.World.NextEntity == before, "rejected spawn allocates no entity");
    }

    private static void CheckReleaseOnDeath(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 403);
        sim.AddPopulationCap(0, 10);
        sim.AddPopulationCap(1, 10);
        long attacker = sim.SpawnUnit(5, 5, 0, 50, 10, 1.0, 1.0, 0.0, popCost: 1);
        long victim = sim.SpawnUnit(6, 5, 1, 10, 0, 1.0, 1.0, 0.0, popCost: 4);
        sim.IssueAttack(attacker, victim);
        for (int tick = 0; tick < 4; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, !sim.World.IsAlive(victim), "victim dies in combat");
        Expect(failures, sim.PopulationUsed(1) == 0, "death releases population");
        Expect(failures, sim.PopulationUsed(0) == 1, "survivor keeps its population");
    }

    private static void CheckCapClamp(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 404);
        Expect(failures, sim.AddPopulationCap(0, 100) == 100, "first cap layer applied");
        Expect(failures, sim.AddPopulationCap(0, 150) == PopulationSystem.DefaultCapMax, "cap clamps to max");
        Expect(failures, sim.AddPopulationCap(0, -50) == 150, "negative delta lowers cap");
    }

    private static void CheckUnconfiguredUnlimited(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 405);
        long unit = sim.SpawnUnit(4, 4, 0, 50, 5, 1.0, 1.0, 0.0, popCost: 5);
        Expect(failures, unit >= 0, "player without ledger is unlimited");
        Expect(failures, !PopulationSystem.HasLedger(sim.World, 0), "no ledger created without a cap");
        Expect(failures, sim.PopulationUsed(0) == 0, "unconfigured player reports zero usage");
    }

    private static void CheckVillagerCost(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 406);
        sim.AddPopulationCap(0, 10);
        long villager = sim.SpawnVillager(4, 4, 0, 0.2, 10, 1.0, popCost: 2);
        Expect(failures, villager >= 0, "villager within cap spawns");
        Expect(failures, sim.PopulationUsed(0) == 2, "villager reserves population");
    }

    private static void CheckLedgerHiddenFromSnapshot(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 407);
        long ledger = PopulationSystem.EnsurePlayer(sim.World, 0);
        long unit = sim.SpawnUnit(4, 4, 0, 50, 5, 1.0, 1.0, 0.0);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn);
        Expect(failures, !ContainsEntity(frame, ledger), "population ledger excluded from snapshot");
        Expect(failures, ContainsEntity(frame, unit), "unit stays in snapshot");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        GameSim first = BuildDeterminismScenario();
        GameSim second = BuildDeterminismScenario();
        for (int tick = 0; tick < 30; tick++)
        {
            first.SimulateTick();
            second.SimulateTick();
        }
        Expect(failures, first.World.Checksum() == second.World.Checksum(), "population state reproducible checksum");
        Expect(failures, first.PopulationUsed(0) == second.PopulationUsed(0), "population usage reproducible");
    }

    private static GameSim BuildDeterminismScenario()
    {
        var sim = new GameSim(new NavGrid(16, 16), 408);
        sim.AddPopulationCap(0, 20);
        sim.AddPopulationCap(1, 20);
        sim.SpawnUnit(4, 5, 0, 50, 6, 1.0, 2.0, 0.1, popCost: 2);
        sim.SpawnUnit(6, 5, 0, 50, 6, 1.0, 2.0, 0.1, popCost: 2);
        sim.SpawnUnit(11, 5, 1, 50, 6, 1.0, 2.0, 0.1, popCost: 3);
        return sim;
    }

    private static bool ContainsEntity(SnapshotFrame frame, long id)
    {
        foreach (EntitySnapshot entity in frame.Entities)
        {
            if (entity.Id == id)
            {
                return true;
            }
        }
        return false;
    }
}
