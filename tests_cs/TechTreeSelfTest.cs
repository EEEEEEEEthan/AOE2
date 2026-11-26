using System.Collections.Generic;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>科技树只读数据的语义断言：目录完整性、文明裁剪与造价来源。</summary>
public static class TechTreeSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckCatalog(failures);
        CheckAvailability(failures);
        CheckCosts(failures);
        return failures;
    }

    private static void CheckCatalog(List<string> failures)
    {
        Expect(failures, TechCatalog.TechCount == 3, "three techs in catalog");
        for (long techId = 0; techId < TechCatalog.TechCount; techId++)
        {
            Expect(failures, TechCatalog.IsValidTech(techId), "tech id valid");
            Expect(failures, TechCatalog.TechName(techId).Length > 0, "tech has display name");
            Expect(failures, TechCatalog.TechDescription(techId).Length > 0, "tech has description");
            long requiredAge = TechCatalog.RequiredAge(techId);
            Expect(failures, TechCatalog.IsValidAge(requiredAge), "tech requires a valid age");
            Expect(failures,
                TechCatalog.AgeName(requiredAge).Length > 0,
                "required age has display name");
            long building = TechCatalog.RequiredBuilding(techId);
            Expect(failures, BuildingTypes.IsKnown(building), "tech requires a known building");
            Expect(failures,
                BuildingTypes.DisplayName(building).Length > 0,
                "required building has display name");
        }
    }

    private static void CheckAvailability(List<string> failures)
    {
        for (long techId = 0; techId < TechCatalog.TechCount; techId++)
        {
            Expect(failures,
                TechCatalog.IsAvailable(CivilizationCatalog.Generic, techId),
                "generic civ keeps every tech");
        }
        Expect(failures,
            !TechCatalog.IsAvailable(CivilizationCatalog.Longbow, TechCatalog.TechScaleMail),
            "longbow civ lacks scale mail");
        Expect(failures,
            TechCatalog.IsAvailable(CivilizationCatalog.Longbow, TechCatalog.TechForging)
            && TechCatalog.IsAvailable(CivilizationCatalog.Longbow, TechCatalog.TechFletching),
            "longbow civ keeps its other techs");
        Expect(failures,
            TechCatalog.IsAvailable(CivilizationCatalog.Count, TechCatalog.TechForging),
            "unknown civ falls back to generic table");
    }

    private static void CheckCosts(List<string> failures)
    {
        for (long techId = 0; techId < TechCatalog.TechCount; techId++)
        {
            Expect(failures, TechCatalog.TechFoodCostOf(techId) >= 0, "food cost non-negative");
            Expect(failures, TechCatalog.TechGoldCostOf(techId) >= 0, "gold cost non-negative");
            Expect(failures, TechCatalog.TechResearchTicksFor(techId) > 0, "research ticks positive");
        }
        Expect(failures,
            TechCatalog.TechResearchTicksFor(TechCatalog.TechFletching)
                > TechCatalog.TechResearchTicksFor(TechCatalog.TechForging),
            "later tech takes longer than an earlier one");
    }
}
