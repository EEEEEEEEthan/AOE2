using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>兵种升级线与克制矩阵：扣费耗时、现存/队列/新造单位替换、附加伤害与决定性。</summary>
public static partial class KernelSelfTest
{
    private static void CheckUnitUpgrades(List<string> failures)
    {
        CheckInfantryUpgrade(failures);
        CheckArcherUpgrade(failures);
        CheckCavalryUpgrade(failures);
        CheckTrainingMapping(failures);
        CheckCounterMatrix(failures);
        CheckUpgradeSnapshotAndChecksum(failures);
    }

    private static void CheckInfantryUpgrade(List<string> failures)
    {
        var sim = NewUpgradeSim(701, TechCatalog.AgeFeudal);
        long barracks = SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
        long swordsman = sim.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        long foodBefore = sim.Resource(0, GameSim.ResourceFood);
        Expect(failures, sim.Research(0, TechCatalog.TechArmoredInfantry), "infantry upgrade starts");
        Expect(failures,
            sim.Resource(0, GameSim.ResourceFood)
                == foodBefore - TechCatalog.TechFoodCostOf(TechCatalog.TechArmoredInfantry),
            "infantry upgrade deducts food");
        Expect(failures,
            !sim.IsResearched(0, TechCatalog.TechArmoredInfantry),
            "infantry upgrade is not instant");
        AdvanceTicks(sim, 110);
        Expect(failures, sim.IsResearched(0, TechCatalog.TechArmoredInfantry), "infantry upgrade completes");
        Expect(failures,
            sim.World.GetValue(swordsman, SimComponents.Combat, SimComponents.UnitType)
                == UnitDefs.ArmoredInfantry,
            "existing swordsman upgraded in place");
        long expectedMax = TechCatalog.EffectiveStat(
            sim.Civ(0), sim.PlayerAge(0), sim.PlayerResearch(0),
            TechCatalog.StatMaxHp, UnitDefs.Get(UnitDefs.ArmoredInfantry).MaxHp);
        Expect(failures,
            sim.World.GetValue(swordsman, SimComponents.Health, SimComponents.MaxHp) == expectedMax,
            "upgraded unit adopts new hp cap");
        Expect(failures,
            sim.World.GetValue(swordsman, SimComponents.Health, SimComponents.Hp) <= expectedMax,
            "upgraded unit hp stays capped");

        long before = CountUnitType(sim, 0, UnitDefs.ArmoredInfantry);
        Expect(failures, sim.Train(barracks, UnitDefs.Swordsman), "barracks still queues base swordsman");
        AdvanceTicks(sim, 40);
        Expect(failures,
            CountUnitType(sim, 0, UnitDefs.ArmoredInfantry) == before + 1,
            "trained unit spawns at current level");
        Expect(failures,
            CountUnitType(sim, 0, UnitDefs.Swordsman) == 0,
            "no base swordsman remains after upgrade");
    }

    private static void CheckArcherUpgrade(List<string> failures)
    {
        var sim = NewUpgradeSim(702, TechCatalog.AgeCastle);
        SpawnCompleteBuilding(sim, BuildingTypes.ArcheryRange, 6, 6, 0);
        long archer = sim.SpawnUnit(2, 2, 0, UnitDefs.Archer);
        Expect(failures, sim.Research(0, TechCatalog.TechCrossbowman), "archer upgrade starts");
        AdvanceTicks(sim, 130);
        Expect(failures,
            sim.World.GetValue(archer, SimComponents.Combat, SimComponents.UnitType)
                == UnitDefs.Crossbowman,
            "archer upgraded to crossbowman");
    }

    private static void CheckCavalryUpgrade(List<string> failures)
    {
        var sim = NewUpgradeSim(703, TechCatalog.AgeCastle);
        SpawnCompleteBuilding(sim, BuildingTypes.Stable, 6, 6, 0);
        long knight = sim.SpawnUnit(2, 2, 0, UnitDefs.Knight);
        Expect(failures, sim.Research(0, TechCatalog.TechCavalier), "cavalry upgrade starts");
        AdvanceTicks(sim, 160);
        Expect(failures,
            sim.World.GetValue(knight, SimComponents.Combat, SimComponents.UnitType)
                == UnitDefs.Cavalier,
            "knight upgraded to cavalier");
    }

    private static void CheckTrainingMapping(List<string> failures)
    {
        var sim = NewUpgradeSim(704, TechCatalog.AgeCastle);
        Expect(failures,
            UnitCatalog.IsProducible(sim, 0, BuildingTypes.Barracks, UnitDefs.Spearman),
            "barracks trains spearman");
        Expect(failures,
            UnitCatalog.IsProducible(sim, 0, BuildingTypes.ArcheryRange, UnitDefs.Skirmisher),
            "archery range trains skirmisher");
        Expect(failures,
            !UnitCatalog.IsProducible(sim, 0, BuildingTypes.ArcheryRange, UnitDefs.Knight),
            "archery range cannot train cavalry");
        Expect(failures,
            UnitCatalog.IsProducible(sim, 0, BuildingTypes.Stable, UnitDefs.Scout)
            && UnitCatalog.IsProducible(sim, 0, BuildingTypes.Stable, UnitDefs.Camel),
            "stable trains scout and camel");
        Expect(failures,
            !UnitCatalog.IsProducible(sim, 0, BuildingTypes.Barracks, UnitDefs.ArmoredInfantry),
            "upgraded units are not directly trainable");
    }

    private static void CheckCounterMatrix(List<string> failures)
    {
        Expect(failures,
            UnitDefs.BonusDamage(UnitDefs.Pikeman, UnitArmorClass.Cavalry) == 22,
            "pikeman bonus vs cavalry");
        Expect(failures,
            UnitDefs.BonusDamage(UnitDefs.Halberdier, UnitArmorClass.Cavalry) == 29,
            "halberdier bonus vs cavalry");
        Expect(failures,
            UnitDefs.BonusDamage(UnitDefs.Camel, UnitArmorClass.Cavalry) == 18,
            "camel bonus vs cavalry");
        Expect(failures,
            UnitDefs.BonusDamage(UnitDefs.Skirmisher, UnitArmorClass.Archer) == 3
            && UnitDefs.BonusDamage(UnitDefs.EliteSkirmisher, UnitArmorClass.Archer) == 5,
            "skirmisher bonus vs archers");
        Expect(failures,
            UnitDefs.BonusDamage(UnitDefs.Swordsman, UnitArmorClass.Cavalry) == 0,
            "plain infantry has no cavalry bonus");

        var sim = NewUpgradeSim(705, TechCatalog.AgeDark);
        long pikeman = sim.SpawnUnit(5, 5, 0, UnitDefs.Pikeman, CombatStance.NoAttack);
        long knight = sim.SpawnUnit(6, 5, 1, UnitDefs.Knight, CombatStance.NoAttack);
        sim.IssueAttack(pikeman, knight);
        sim.SimulateTick();
        long dealt = 100 - sim.World.GetValue(knight, SimComponents.Health, SimComponents.Hp);
        Expect(failures, dealt == 27, "pikeman deals counter damage in combat");
    }

    private static void CheckUpgradeSnapshotAndChecksum(List<string> failures)
    {
        var sim = NewUpgradeSim(706, TechCatalog.AgeFeudal, withMatch: true);
        SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
        sim.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        sim.SpawnUnit(20, 20, 1, UnitDefs.Swordsman);
        sim.Research(0, TechCatalog.TechArmoredInfantry);
        AdvanceTicks(sim, 110);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn, sim.Match);
        Expect(failures,
            (frame.Players[0].Research & TechCatalog.ResearchBit(TechCatalog.TechArmoredInfantry)) != 0,
            "snapshot carries upgrade research bit");

        Expect(failures, UpgradeChecksum(true) == UpgradeChecksum(true), "upgrade run deterministic");
        Expect(failures, UpgradeChecksum(true) != UpgradeChecksum(false), "upgrade participates in checksum");
    }

    private static GameSim NewUpgradeSim(long seed, long age, bool withMatch = false)
    {
        var sim = new GameSim(
            new NavGrid(24, 24), seed, 2, 6, withMatch ? MatchConfig.Standard : null);
        sim.AddResource(0, GameSim.ResourceFood, 5000);
        sim.AddResource(0, GameSim.ResourceGold, 5000);
        sim.AddResource(0, GameSim.ResourceWood, 5000);
        sim.AddPopulationCap(0, 50);
        sim.Progress(0).Age = age;
        return sim;
    }

    private static long CountUnitType(GameSim sim, long team, long unitType)
    {
        long count = 0;
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id) || !sim.World.HasComponent(id, SimComponents.Combat))
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Combat, SimComponents.UnitType) == unitType)
            {
                count += 1;
            }
        }
        return count;
    }

    private static long UpgradeChecksum(bool upgrade)
    {
        var sim = NewUpgradeSim(707, TechCatalog.AgeFeudal);
        SpawnCompleteBuilding(sim, BuildingTypes.Barracks, 6, 6, 0);
        sim.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        if (upgrade)
        {
            sim.Research(0, TechCatalog.TechArmoredInfantry);
            AdvanceTicks(sim, 110);
        }
        AdvanceTicks(sim, 20);
        return sim.World.Checksum();
    }
}
