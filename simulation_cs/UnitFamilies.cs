namespace AoeSim;

/// <summary>
/// 兵种家族与升级等级：同一家族按等级列出成员，等级 1..n 各由一项升级科技解锁。
/// 家族索引与等级是固定整数，随研究位图确定性解析当前等级。
/// </summary>
public static class UnitFamilies
{
    public const long Infantry = 0;
    public const long Spear = 1;
    public const long Archer = 2;
    public const long Skirmisher = 3;
    public const long Scout = 4;
    public const long Cavalry = 5;
    public const long Camel = 6;
    public const long Unique = 7;
    public const long Count = 8;

    private const long MaxUnits = 32;

    /// <summary>每家族按等级升序的成员；家族基础兵即索引 0。</summary>
    private static readonly long[][] Members =
    {
        new long[]
        {
            UnitDefs.Swordsman, UnitDefs.ArmoredInfantry, UnitDefs.LongSwordsman,
            UnitDefs.TwoHandedSwordsman, UnitDefs.Champion,
        },
        new long[] { UnitDefs.Spearman, UnitDefs.Pikeman, UnitDefs.Halberdier },
        new long[] { UnitDefs.Archer, UnitDefs.Crossbowman, UnitDefs.Arbalest },
        new long[] { UnitDefs.Skirmisher, UnitDefs.EliteSkirmisher },
        new long[] { UnitDefs.Scout, UnitDefs.LightCavalry },
        new long[] { UnitDefs.Knight, UnitDefs.Cavalier, UnitDefs.Paladin },
        new long[] { UnitDefs.Camel },
        new long[] { UnitDefs.Longbowman },
    };

    /// <summary>到达某等级所需的升级科技；等级 0 无科技，记 -1。</summary>
    private static readonly long[][] UpgradeTechs =
    {
        new long[]
        {
            -1, TechCatalog.TechArmoredInfantry, TechCatalog.TechLongSwordsman,
            TechCatalog.TechTwoHandedSwordsman, TechCatalog.TechChampion,
        },
        new long[] { -1, TechCatalog.TechPikeman, TechCatalog.TechHalberdier },
        new long[] { -1, TechCatalog.TechCrossbowman, TechCatalog.TechArbalest },
        new long[] { -1, TechCatalog.TechEliteSkirmisher },
        new long[] { -1, TechCatalog.TechLightCavalry },
        new long[] { -1, TechCatalog.TechCavalier, TechCatalog.TechPaladin },
        new long[] { -1 },
        new long[] { -1 },
    };

    private static readonly long[] FamilyIndex = BuildFamilyIndex();
    private static readonly long[] LevelIndex = BuildLevelIndex();

    public static long FamilyOf(long unitType) =>
        unitType >= 0 && unitType < MaxUnits ? FamilyIndex[unitType] : -1;

    public static long LevelOf(long unitType) =>
        unitType >= 0 && unitType < MaxUnits ? LevelIndex[unitType] : -1;

    public static long BaseUnit(long family) =>
        family >= 0 && family < Count ? Members[family][0] : -1;

    public static long UnitAtLevel(long family, long level) =>
        family >= 0 && family < Count && level >= 0 && level < Members[family].Length
            ? Members[family][level]
            : -1;

    /// <summary>当前研究位图下该家族已达成的最高等级。</summary>
    public static long CurrentLevel(long researchMask, long family)
    {
        if (family < 0 || family >= Count)
        {
            return 0;
        }
        long level = 0;
        long[] techs = UpgradeTechs[family];
        for (long index = 1; index < techs.Length; index++)
        {
            if (TechCatalog.IsResearched(researchMask, techs[index]))
            {
                level = index;
            }
        }
        return level;
    }

    /// <summary>把某家族任意等级的单位解析为当前研究位图下的最新等级。</summary>
    public static long ResolveUnit(long researchMask, long unitType)
    {
        long family = FamilyOf(unitType);
        if (family < 0)
        {
            return unitType;
        }
        long resolved = UnitAtLevel(family, CurrentLevel(researchMask, family));
        return resolved >= 0 ? resolved : unitType;
    }

    /// <summary>训练入口只接受家族基础兵；不属于任何家族的独立兵种可直接训练。</summary>
    public static bool IsTrainableBase(long unitType) =>
        FamilyOf(unitType) < 0 || LevelOf(unitType) == 0;

    private static long[] BuildFamilyIndex()
    {
        var index = new long[MaxUnits];
        for (long unit = 0; unit < MaxUnits; unit++)
        {
            index[unit] = -1;
        }
        for (long family = 0; family < Count; family++)
        {
            foreach (long member in Members[family])
            {
                index[member] = family;
            }
        }
        return index;
    }

    private static long[] BuildLevelIndex()
    {
        var index = new long[MaxUnits];
        for (long unit = 0; unit < MaxUnits; unit++)
        {
            index[unit] = -1;
        }
        for (long family = 0; family < Count; family++)
        {
            long[] members = Members[family];
            for (long level = 0; level < members.Length; level++)
            {
                index[members[level]] = level;
            }
        }
        return index;
    }
}
