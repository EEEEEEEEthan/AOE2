using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>文明差异、独特单位与科技树裁剪的语义断言；返回空列表表示全过。</summary>
public static class CivilizationSelfTest
{
    private static readonly long[] GenericCivs = { CivilizationCatalog.Generic, CivilizationCatalog.Generic };
    private static readonly long[] LongbowCivs = { CivilizationCatalog.Longbow, CivilizationCatalog.Longbow };

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckCatalog(failures);
        CheckUniqueUnits(failures);
        CheckStatBonus(failures);
        CheckTechClipping(failures);
        CheckChecksum(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static GameSim Build(long seed, long[] civs) =>
        new(new NavGrid(16, 16), seed, (int)civs.Length, 6, null, null, GameSim.DefaultFieldCacheCapacity, civs);

    private static void CheckCatalog(List<string> failures)
    {
        Expect(failures, CivilizationCatalog.IsValid(CivilizationCatalog.Generic), "generic civ valid");
        Expect(failures, CivilizationCatalog.IsValid(CivilizationCatalog.Longbow), "longbow civ valid");
        Expect(failures, !CivilizationCatalog.IsValid(CivilizationCatalog.Count), "unknown civ invalid");
        Expect(failures, CivilizationCatalog.UniqueUnit(CivilizationCatalog.Generic) == -1, "generic has no unique unit");
        Expect(failures,
            CivilizationCatalog.UniqueUnit(CivilizationCatalog.Longbow) == UnitDefs.Longbowman,
            "longbow maps its unique unit");
        Expect(failures, UnitDefs.BaseUnitCount == 4, "four baseline unit types");
    }

    private static void CheckUniqueUnits(List<string> failures)
    {
        GameSim generic = Build(611, GenericCivs);
        GameSim longbow = Build(611, LongbowCivs);
        Expect(failures, generic.SpawnUnit(2, 2, 0, UnitDefs.Longbowman) == -1,
            "generic cannot build foreign unique unit");
        long unique = longbow.SpawnUnit(2, 2, 0, UnitDefs.Longbowman);
        Expect(failures, unique >= 0, "owning civ can build unique unit");
        Expect(failures, longbow.Progress(0).Civ == CivilizationCatalog.Longbow, "civ id persisted");
        Expect(failures, generic.SpawnUnit(2, 2, 0, UnitDefs.Swordsman) >= 0,
            "generic can build baseline unit");
        Expect(failures, longbow.SpawnUnit(3, 2, 0, UnitDefs.Swordsman) >= 0,
            "civ can build baseline unit");
    }

    private static void CheckStatBonus(List<string> failures)
    {
        GameSim generic = Build(612, GenericCivs);
        GameSim longbow = Build(612, LongbowCivs);
        long plain = generic.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        long boosted = longbow.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        long plainDamage = generic.World.GetValue(plain, SimComponents.Combat, SimComponents.Damage);
        long boostedDamage = longbow.World.GetValue(boosted, SimComponents.Combat, SimComponents.Damage);
        Expect(failures, boostedDamage == plainDamage + 1, "civ bonus raises same unit damage");
        Expect(failures,
            generic.World.GetValue(plain, SimComponents.Health, SimComponents.MaxHp)
                == longbow.World.GetValue(boosted, SimComponents.Health, SimComponents.MaxHp),
            "civ bonus leaves untouched stat at base");
        long unique = longbow.SpawnUnit(3, 2, 0, UnitDefs.Longbowman);
        long expected = TechCatalog.EffectiveStat(
            CivilizationCatalog.Longbow, TechCatalog.AgeDark, 0, TechCatalog.StatDamage, 7);
        Expect(failures,
            longbow.World.GetValue(unique, SimComponents.Combat, SimComponents.Damage) == expected,
            "unique unit spawns with civ-effective stats");
    }

    private static void CheckTechClipping(List<string> failures)
    {
        Expect(failures,
            TechCatalog.IsAvailable(CivilizationCatalog.Generic, TechCatalog.TechScaleMail),
            "generic can research scale mail");
        Expect(failures,
            !TechCatalog.IsAvailable(CivilizationCatalog.Longbow, TechCatalog.TechScaleMail),
            "longbow lacks scale mail");
        Expect(failures,
            TechCatalog.IsAvailable(CivilizationCatalog.Longbow, TechCatalog.TechForging),
            "longbow keeps forging");

        GameSim generic = Build(613, GenericCivs);
        GameSim longbow = Build(613, LongbowCivs);
        foreach (GameSim sim in new[] { generic, longbow })
        {
            sim.Progress(0).Age = TechCatalog.AgeFeudal;
            sim.AddResource(0, GameSim.ResourceFood, 5000);
            sim.AddResource(0, GameSim.ResourceGold, 5000);
            SpawnCompleteBuilding(sim, BuildingTypes.Market, 8, 8, 0);
        }
        Expect(failures, generic.Research(0, TechCatalog.TechScaleMail),
            "generic scale mail research starts");
        Expect(failures, !longbow.Research(0, TechCatalog.TechScaleMail),
            "longbow scale mail research rejected");
    }

    private static void CheckChecksum(List<string> failures)
    {
        long first = RunChecksum(GenericCivs);
        long second = RunChecksum(GenericCivs);
        long other = RunChecksum(LongbowCivs);
        Expect(failures, first == second, "same civ setup reproducible");
        Expect(failures, first != other, "different civ changes checksum");
    }

    private static long RunChecksum(long[] civs)
    {
        GameSim sim = Build(614, civs);
        sim.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        sim.SpawnUnit(12, 12, 1, UnitDefs.Archer);
        for (int tick = 0; tick < 20; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }

    private static long SpawnCompleteBuilding(GameSim sim, long type, int cellX, int cellY, long team)
    {
        sim.AddResource(team, GameSim.ResourceWood, BuildingTypes.Cost(type));
        long id = sim.IssuePlaceBuilding(type, cellX, cellY, team);
        sim.World.SetValue(id, SimComponents.Building, SimComponents.Progress, BuildingTypes.BuildTicks(type));
        sim.World.SetValue(id, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
        return id;
    }
}
