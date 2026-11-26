namespace AoeSim;

/// <summary>时代与科技的静态定义表：整数常量，固定 id 顺序，禁止 float。</summary>
public static class TechCatalog
{
    public const long AgeDark = 0;
    public const long AgeFeudal = 1;
    public const long AgeCastle = 2;
    public const long AgeImperial = 3;
    public const long MaxAge = AgeImperial;

    public const long TechForging = 0;
    public const long TechScaleMail = 1;
    public const long TechFletching = 2;
    public const long TechCount = 3;

    public const long StatMaxHp = 0;
    public const long StatDamage = 1;
    public const long StatRangeMax = 2;
    public const long StatCooldown = 3;
    public const long StatCount = 4;

    private static readonly long[][] AgeStatBonus =
    {
        new long[] { 0, 5, 10, 15 },
        new long[] { 0, 1, 2, 3 },
        new long[] { 0, 0, 0, 0 },
        new long[] { 0, 0, 0, 0 },
    };

    private static readonly long[] TechRequiredAge = { AgeFeudal, AgeFeudal, AgeCastle };

    private static readonly long[][] TechStatBonus =
    {
        new long[] { 0, 10, 0 },
        new long[] { 2, 0, 0 },
        new long[] { 0, 0, 1 },
        new long[] { 0, 0, 0 },
    };

    private static readonly long[] AgeFoodCost = { 0, 500, 800, 1000 };
    private static readonly long[] AgeGoldCost = { 0, 0, 200, 800 };
    private static readonly long[] AgeResearchTicks = { 0, 180, 240, 300 };

    private static readonly long[] TechFoodCost = { 150, 100, 100 };
    private static readonly long[] TechGoldCost = { 0, 0, 50 };
    private static readonly long[] TechResearchTicks = { 90, 90, 120 };

    /// <summary>科技承载建筑：一期建筑表没有铁匠铺，用兵营/市场做最小前置。</summary>
    private static readonly long[] TechRequiredBuilding =
    {
        BuildingTypes.Barracks,
        BuildingTypes.Market,
        BuildingTypes.Barracks,
    };

    public static bool IsValidAge(long age) => age >= AgeDark && age <= MaxAge;

    public static bool IsValidTech(long techId) => techId >= 0 && techId < TechCount;

    public static long RequiredAge(long techId) => TechRequiredAge[techId];

    public static long RequiredBuilding(long techId) => TechRequiredBuilding[techId];

    /// <summary>升到目标时代所需的上一时代已完成建筑数；黑暗升封建无建筑门槛。</summary>
    public static long RequiredBuildings(long targetAge) => targetAge >= AgeCastle ? 2 : 0;

    public static long AgeFoodCostOf(long targetAge) => AgeFoodCost[targetAge];

    public static long AgeGoldCostOf(long targetAge) => AgeGoldCost[targetAge];

    public static long AgeResearchTicksFor(long targetAge) => AgeResearchTicks[targetAge];

    public static long TechFoodCostOf(long techId) => TechFoodCost[techId];

    public static long TechGoldCostOf(long techId) => TechGoldCost[techId];

    public static long TechResearchTicksFor(long techId) => TechResearchTicks[techId];

    public static long ResearchBit(long techId) => 1L << (int)techId;

    public static bool IsResearched(long researchMask, long techId) =>
        (researchMask & ResearchBit(techId)) != 0;

    public static long AgeBonus(long age, long stat) => AgeStatBonus[stat][age];

    public static long TechBonus(long researchMask, long stat)
    {
        long bonus = 0;
        for (long techId = 0; techId < TechCount; techId++)
        {
            if (IsResearched(researchMask, techId))
            {
                bonus += TechStatBonus[stat][techId];
            }
        }
        return bonus;
    }

    public static long EffectiveStat(long age, long researchMask, long stat, long baseValue) =>
        baseValue + AgeBonus(age, stat) + TechBonus(researchMask, stat);
}
