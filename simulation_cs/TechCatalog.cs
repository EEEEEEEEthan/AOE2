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

    // 步兵线：剑士 → 装甲步兵 → 长剑士 → 双手剑士 → 冠军剑士
    public const long TechArmoredInfantry = 3;
    public const long TechLongSwordsman = 4;
    public const long TechTwoHandedSwordsman = 5;
    public const long TechChampion = 6;

    // 长枪线：长枪兵 → 长戟兵 → 戟兵
    public const long TechPikeman = 7;
    public const long TechHalberdier = 8;

    // 弓兵线：弓兵 → 弩手 → 劲弩手
    public const long TechCrossbowman = 9;
    public const long TechArbalest = 10;

    // 散兵线：散兵 → 精锐散兵
    public const long TechEliteSkirmisher = 11;

    // 骑兵线：骑士 → 重装骑士 → 圣骑士
    public const long TechCavalier = 12;
    public const long TechPaladin = 13;

    // 侦察线：侦察骑兵 → 轻骑兵
    public const long TechLightCavalry = 14;

    public const long TechCount = 15;

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

    private static readonly long[] TechRequiredAge =
    {
        AgeFeudal, AgeFeudal, AgeCastle,
        AgeFeudal, AgeCastle, AgeImperial, AgeImperial,
        AgeFeudal, AgeCastle,
        AgeCastle, AgeImperial,
        AgeCastle,
        AgeCastle, AgeImperial,
        AgeCastle,
    };

    /// <summary>升级科技只替换兵种，不提供全局属性加成，因此全部为 0。</summary>
    private static readonly long[][] TechStatBonus =
    {
        new long[] { 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new long[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new long[] { 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        new long[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
    };

    private static readonly long[] AgeFoodCost = { 0, 500, 800, 1000 };
    private static readonly long[] AgeGoldCost = { 0, 0, 200, 800 };
    private static readonly long[] AgeResearchTicks = { 0, 180, 240, 300 };

    private static readonly long[] TechFoodCost =
    {
        150, 100, 100,
        100, 150, 300, 450,
        125, 250,
        125, 350,
        230,
        300, 500,
        150,
    };

    private static readonly long[] TechGoldCost =
    {
        0, 0, 50,
        40, 65, 100, 200,
        40, 100,
        75, 200,
        100,
        200, 350,
        100,
    };

    private static readonly long[] TechResearchTicks =
    {
        90, 90, 120,
        100, 130, 160, 180,
        110, 150,
        120, 170,
        140,
        150, 180,
        120,
    };

    private static readonly string[] TechDisplayNames =
    {
        "锻造", "鳞甲", "锥形箭",
        "装甲步兵", "长剑士", "双手剑士", "冠军剑士",
        "长戟兵", "戟兵",
        "弩手", "劲弩手",
        "精锐散兵",
        "重装骑士", "圣骑士",
        "轻骑兵",
    };

    private static readonly string[] TechEffectTexts =
    {
        "所有单位攻击 +2。",
        "所有单位最大生命 +10。",
        "所有远程单位射程 +1。",
        "剑士升级为装甲步兵。",
        "装甲步兵升级为长剑士。",
        "长剑士升级为双手剑士。",
        "双手剑士升级为冠军剑士。",
        "长枪兵升级为长戟兵。",
        "长戟兵升级为戟兵。",
        "弓兵升级为弩手。",
        "弩手升级为劲弩手。",
        "散兵升级为精锐散兵。",
        "骑士升级为重装骑士。",
        "重装骑士升级为圣骑士。",
        "侦察骑兵升级为轻骑兵。",
    };

    private static readonly string[] AgeDisplayNames =
    {
        "黑暗时代",
        "封建时代",
        "城堡时代",
        "帝王时代",
    };

    /// <summary>科技承载建筑：锻造/鳞甲在铁匠铺，锥形箭、弓兵/散兵升级在靶场，步兵升级在兵营，骑兵升级在马厩。</summary>
    private static readonly long[] TechRequiredBuilding =
    {
        BuildingTypes.Blacksmith,
        BuildingTypes.Blacksmith,
        BuildingTypes.ArcheryRange,
        BuildingTypes.Barracks,
        BuildingTypes.Barracks,
        BuildingTypes.Barracks,
        BuildingTypes.Barracks,
        BuildingTypes.Barracks,
        BuildingTypes.Barracks,
        BuildingTypes.ArcheryRange,
        BuildingTypes.ArcheryRange,
        BuildingTypes.ArcheryRange,
        BuildingTypes.Stable,
        BuildingTypes.Stable,
        BuildingTypes.Stable,
    };

    public static bool IsValidAge(long age) => age >= AgeDark && age <= MaxAge;

    public static bool IsValidTech(long techId) => techId >= 0 && techId < TechCount;

    public static string TechName(long techId) =>
        IsValidTech(techId) ? TechDisplayNames[(int)techId] : "";

    public static string TechDescription(long techId) =>
        IsValidTech(techId) ? TechEffectTexts[(int)techId] : "";

    public static string AgeName(long age) => IsValidAge(age) ? AgeDisplayNames[(int)age] : "";

    /// <summary>本文明是否可研究该科技：全局合法且不在该文明的禁用清单内。</summary>
    public static bool IsAvailable(long civ, long techId) => CivilizationCatalog.IsTechEnabled(civ, techId);

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

    /// <summary>在时代与科技加成之上再叠加文明全局加成。</summary>
    public static long EffectiveStat(
        long civ, long age, long researchMask, long stat, long baseValue) =>
        EffectiveStat(age, researchMask, stat, baseValue)
        + CivilizationCatalog.StatBonus(civ, stat);
}
