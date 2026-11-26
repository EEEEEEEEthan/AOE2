using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>升时代与科技研究语义。</summary>
public static partial class KernelSelfTest
{
    private static void CheckTech(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 401, 2, 6);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeDark, "players start in dark age");
        Expect(failures, sim.PlayerResearch(0) == 0, "players start with no research");
        long darkUnit = sim.SpawnUnit(2, 2, 0, 50, 5, 1.0, 1.0, 0.0);
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Health, SimComponents.MaxHp) == 50
            && sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == 5,
            "dark age spawn uses base stats");

        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeFeudal), "age up without resources rejected");

        sim.AddResource(0, GameSim.ResourceFood, 5000);
        sim.AddResource(0, GameSim.ResourceGold, 5000);
        long foodBeforeAge = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, sim.AgeUp(0, TechCatalog.AgeFeudal), "age up starts research");
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeDark, "age does not apply instantly");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood) == foodBeforeAge - TechCatalog.AgeFoodCostOf(TechCatalog.AgeFeudal),
            "age up deducts food");
        Expect(failures,
            ResearchSystem.ActiveKind(sim.World, 0) == ResearchSystem.KindAge,
            "age research recorded");
        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeFeudal), "duplicate age up rejected while researching");
        Expect(failures, !sim.AgeUp(0, TechCatalog.AgeCastle), "age up cannot skip an age");

        AdvanceTicks(sim, 220);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeFeudal, "age applies after research ticks");
        Expect(failures,
            ResearchSystem.ActiveKind(sim.World, 0) == ResearchSystem.KindNone,
            "research slot clears on age completion");

        long feudalMaxHp = TechCatalog.EffectiveStat(
            TechCatalog.AgeFeudal, 0, TechCatalog.StatMaxHp, 50);
        long feudalDamage = TechCatalog.EffectiveStat(
            TechCatalog.AgeFeudal, 0, TechCatalog.StatDamage, 5);
        long feudalUnit = sim.SpawnUnit(3, 2, 0, 50, 5, 1.0, 1.0, 0.0);
        Expect(failures,
            sim.World.GetValue(feudalUnit, SimComponents.Health, SimComponents.MaxHp) == feudalMaxHp
            && sim.World.GetValue(feudalUnit, SimComponents.Combat, SimComponents.Damage) == feudalDamage,
            "new spawn applies age bonus");
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Health, SimComponents.MaxHp) == feudalMaxHp
            && sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == feudalDamage,
            "existing unit recomputed on age up");

        long foodBeforeResearch = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, !sim.Research(0, TechCatalog.TechForging), "research needs its prerequisite building");
        SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
        Expect(failures, sim.Research(0, TechCatalog.TechForging), "feudal research starts after barracks");
        Expect(failures, !sim.IsResearched(0, TechCatalog.TechForging), "research not applied instantly");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood)
                == foodBeforeResearch - TechCatalog.TechFoodCostOf(TechCatalog.TechForging),
            "research deducts food");
        Expect(failures, !sim.Research(0, TechCatalog.TechForging), "duplicate research rejected");
        AdvanceTicks(sim, 120);
        Expect(failures, sim.IsResearched(0, TechCatalog.TechForging), "research bit set after ticks");
        long researchedDamage = feudalDamage + 2;
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.Damage) == researchedDamage,
            "research recomputes existing unit damage");
        Expect(failures,
            sim.World.GetValue(
                sim.SpawnUnit(4, 2, 0, 50, 5, 1.0, 1.0, 0.0),
                SimComponents.Combat,
                SimComponents.Damage) == researchedDamage,
            "research applies to new spawn");
        Expect(failures, !sim.Research(0, TechCatalog.TechFletching), "age gated research rejected");
        Expect(failures, !sim.Research(0, 999), "unknown tech rejected");
        Expect(failures, !sim.Research(5, TechCatalog.TechForging), "unknown player rejected");

        SpawnCompleteBuilding(sim, BuildingTypes.Market, 10, 9, 0);
        Expect(failures, sim.AgeUp(0, TechCatalog.AgeCastle), "castle age up needs two feudal buildings");
        AdvanceTicks(sim, 280);
        Expect(failures, sim.PlayerAge(0) == TechCatalog.AgeCastle, "castle age applies after research ticks");
        Expect(failures, sim.Research(0, TechCatalog.TechFletching), "castle research starts");
        AdvanceTicks(sim, 140);
        Expect(failures, sim.IsResearched(0, TechCatalog.TechFletching), "castle research completes");
        long expectedRange = TechCatalog.EffectiveStat(
            TechCatalog.AgeCastle,
            sim.PlayerResearch(0),
            TechCatalog.StatRangeMax,
            ToRangeCellsForTest(1.0));
        Expect(failures,
            sim.World.GetValue(darkUnit, SimComponents.Combat, SimComponents.RangeMax) == expectedRange,
            "range bonus recomputes existing unit");

        var dispatch = new GameSim(new NavGrid(12, 12), 405, 2, 6);
        dispatch.AddResource(0, GameSim.ResourceFood, 2000);
        dispatch.AddResource(0, GameSim.ResourceGold, 1000);
        dispatch.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindAgeUp, aux: TechCatalog.AgeFeudal));
        Expect(failures,
            ResearchSystem.ActiveKind(dispatch.World, 0) == ResearchSystem.KindAge,
            "age up command starts research");
        AdvanceTicks(dispatch, 220);
        Expect(failures, dispatch.PlayerAge(0) == TechCatalog.AgeFeudal, "age up command completes");
        SpawnCompleteBuilding(dispatch, BuildingTypes.Barracks, 6, 6, 0);
        dispatch.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindResearch, aux: TechCatalog.TechForging));
        Expect(failures,
            ResearchSystem.ActiveKind(dispatch.World, 0) == ResearchSystem.KindTech,
            "research command starts research");
        AdvanceTicks(dispatch, 120);
        Expect(failures, dispatch.IsResearched(0, TechCatalog.TechForging), "research command completes");

        var cancelSim = new GameSim(new NavGrid(12, 12), 409, 2, 6);
        cancelSim.AddResource(0, GameSim.ResourceFood, 2000);
        long foodBeforeCancel = cancelSim.Resource(0, GameSim.ResourceFood);
        Expect(failures, cancelSim.AgeUp(0, TechCatalog.AgeFeudal), "cancel case starts age up");
        cancelSim.ApplyCommand(new LockstepCommand(
            player: 0, kind: LockstepCommand.KindCancelResearch));
        Expect(failures,
            ResearchSystem.ActiveKind(cancelSim.World, 0) == ResearchSystem.KindNone,
            "cancel command clears research");
        Expect(failures,
            cancelSim.Resource(0, GameSim.ResourceFood) == foodBeforeCancel,
            "cancel refunds paid resources");

        var ruinSim = new GameSim(new NavGrid(12, 12), 410, 2, 6);
        ruinSim.Progress(0).Age = TechCatalog.AgeFeudal;
        ruinSim.AddResource(0, GameSim.ResourceFood, 1000);
        long ruinBarracks = SpawnCompleteBuilding(ruinSim, BuildingTypes.Barracks, 6, 6, 0);
        long foodBeforeRuin = ruinSim.Resource(0, GameSim.ResourceFood);
        Expect(failures, ruinSim.Research(0, TechCatalog.TechForging), "ruin case starts research");
        ruinSim.World.SetValue(
            ruinBarracks, SimComponents.Building, SimComponents.State, BuildingSystem.StateRuins);
        ruinSim.SimulateTick();
        Expect(failures, !ruinSim.IsResearched(0, TechCatalog.TechForging), "ruined carrier aborts research");
        Expect(failures,
            ruinSim.Resource(0, GameSim.ResourceFood) == foodBeforeRuin,
            "ruined carrier refunds paid resources");

        var snapshotSim = new GameSim(new NavGrid(12, 12), 406, 2, 6, MatchConfig.Standard);
        snapshotSim.SpawnUnit(1, 1, 0, 50, 0, 0.0, 0.0, 0.0);
        snapshotSim.SpawnUnit(10, 10, 1, 50, 0, 0.0, 0.0, 0.0);
        snapshotSim.AddResource(0, GameSim.ResourceFood, 2000);
        snapshotSim.AddResource(0, GameSim.ResourceGold, 1000);
        snapshotSim.AgeUp(0, TechCatalog.AgeFeudal);
        SnapshotFrame frame = SnapshotFrame.Build(
            snapshotSim.World, snapshotSim.Lockstep.Turn, snapshotSim.Match);
        Expect(failures, frame.Players.Length == 2, "snapshot carries players");
        Expect(failures,
            frame.Players[0].Age == TechCatalog.AgeDark
            && frame.Players[0].ResearchKind == ResearchSystem.KindAge
            && frame.Players[0].ResearchMaxProgress > 0
            && frame.Players[1].ResearchKind == ResearchSystem.KindNone,
            "snapshot carries in-progress research");
        AdvanceTicks(snapshotSim, 220);
        SpawnCompleteBuilding(snapshotSim, BuildingTypes.Barracks, 6, 6, 0);
        snapshotSim.Research(0, TechCatalog.TechForging);
        AdvanceTicks(snapshotSim, 120);
        frame = SnapshotFrame.Build(
            snapshotSim.World, snapshotSim.Lockstep.Turn, snapshotSim.Match);
        Expect(failures,
            frame.Players[0].Age == TechCatalog.AgeFeudal
            && frame.Players[0].Research == TechCatalog.ResearchBit(TechCatalog.TechForging)
            && frame.Players[1].Age == TechCatalog.AgeDark,
            "snapshot carries completed age and research");
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        Expect(failures,
            decoded.Players[0].Age == TechCatalog.AgeFeudal
            && decoded.Players[0].Research == TechCatalog.ResearchBit(TechCatalog.TechForging)
            && decoded.Players[0].ResearchKind == ResearchSystem.KindNone,
            "snapshot roundtrip keeps age and research");

        Expect(failures,
            TechProgressionChecksum(true) == TechProgressionChecksum(true),
            "tech progression deterministic");
        Expect(failures,
            TechProgressionChecksum(true) != TechProgressionChecksum(false),
            "tech progression participates in checksum");
    }

    private static long SpawnCompleteBuilding(GameSim sim, long type, int cellX, int cellY, long team)
    {
        ResourceCost cost = BuildingTypes.Cost(type);
        for (long resourceKind = GameSim.ResourceWood; resourceKind <= GameSim.ResourceStone; resourceKind++)
        {
            sim.AddResource(team, resourceKind, cost.AmountOf(resourceKind));
        }
        long id = sim.IssuePlaceBuilding(type, cellX, cellY, team);
        sim.World.SetValue(id, SimComponents.Building, SimComponents.Progress, BuildingTypes.BuildTicks(type));
        sim.World.SetValue(id, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        return id;
    }

    private static long ToRangeCellsForTest(double tiles) =>
        (long)System.Math.Round(tiles, System.MidpointRounding.AwayFromZero);

    private static long TechProgressionChecksum(bool advanced)
    {
        var sim = new GameSim(new NavGrid(16, 16), 408, 2, 6);
        sim.SpawnUnit(2, 2, 0, 50, 5, 1.0, 1.0, 0.1);
        if (advanced)
        {
            sim.AddResource(0, GameSim.ResourceFood, 5000);
            sim.AddResource(0, GameSim.ResourceGold, 5000);
            sim.AgeUp(0, TechCatalog.AgeFeudal);
            AdvanceTicks(sim, 220);
            SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
            sim.Research(0, TechCatalog.TechForging);
            AdvanceTicks(sim, 120);
        }
        sim.SpawnUnit(4, 2, 0, 50, 5, 1.0, 2.0, 0.1);
        for (int tick = 0; tick < 30; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }
}
