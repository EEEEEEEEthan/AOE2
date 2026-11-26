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

    public static bool IsValidAge(long age) => age >= AgeDark && age <= MaxAge;

    public static bool IsValidTech(long techId) => techId >= 0 && techId < TechCount;

    public static long RequiredAge(long techId) => TechRequiredAge[techId];

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

    /// <summary>资源扣费钩子：A 期恒真，接入经济后在此按时代扣费。</summary>
    public static bool TryPayAgeUp(long player, long targetAge) => true;

    /// <summary>资源扣费钩子：A 期恒真，接入经济后在此按科技扣费。</summary>
    public static bool TryPayResearch(long player, long techId) => true;

    /// <summary>建筑前置钩子：A 期恒真，接入建筑后在此查询前置。</summary>
    public static bool HasPrerequisites(long player, long techId) => true;
}
