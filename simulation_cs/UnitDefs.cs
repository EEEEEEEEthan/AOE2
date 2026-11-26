namespace AoeSim;

/// <summary>护甲类型：决定某个兵种挨打时吃哪一档附加伤害。</summary>
public static class UnitArmorClass
{
    public const long Infantry = 0;
    public const long Archer = 1;
    public const long Cavalry = 2;
    public const long Building = 3;
}

/// <summary>攻击类型：决定基础攻击抵扣目标哪一档护甲。</summary>
public static class UnitAttackClass
{
    public const long Melee = 0;
    public const long Pierce = 1;
    public const long Siege = 2;
}

/// <summary>单个兵种的全部战斗数值；生成时一次性写进 Combat 列，运行时可由科技改写。</summary>
public sealed class UnitDef
{
    public long MaxHp { get; init; }
    public long AttackDamage { get; init; }
    public long AttackClass { get; init; }
    public long ArmorClass { get; init; }
    public bool IsRanged { get; init; }
    public double MinRangeTiles { get; init; }
    public double MaxRangeTiles { get; init; }
    public double SpeedTilesPerTick { get; init; }
    public long Cooldown { get; init; }
    public long ArmorMelee { get; init; }
    public long ArmorPierce { get; init; }
    public long ArmorSiege { get; init; }
}

/// <summary>
/// 数据驱动的兵种表。0..3 为四条通用家族的基础兵，4 为不列颠独特单位，
/// 其后按家族补齐全套升级等级与侦察/散兵/长戟/骆驼等克制位。
/// 固定 id 顺序，禁止 float，保证锁步校验和稳定。
/// </summary>
public static class UnitDefs
{
    public const long Swordsman = 0;
    public const long Spearman = 1;
    public const long Archer = 2;
    public const long Knight = 3;

    /// <summary>通用基础兵数量：0..BaseUnitCount-1 为四条主家族的基础兵，其后为扩展兵种与独特单位。</summary>
    public const long BaseUnitCount = 4;

    /// <summary>不列颠独特单位：射程更远的精锐长弓兵。</summary>
    public const long Longbowman = 4;

    public const long ArmoredInfantry = 5;
    public const long LongSwordsman = 6;
    public const long TwoHandedSwordsman = 7;
    public const long Champion = 8;

    public const long Pikeman = 9;
    public const long Halberdier = 10;

    public const long Crossbowman = 11;
    public const long Arbalest = 12;

    public const long Skirmisher = 13;
    public const long EliteSkirmisher = 14;

    public const long Scout = 15;
    public const long LightCavalry = 16;

    public const long Cavalier = 17;
    public const long Paladin = 18;

    public const long Camel = 19;

    /// <summary>兵种表总条目数，供家族索引与快照校验使用。</summary>
    public const long Count = 20;

    private static readonly UnitDef[] Table =
    {
        new UnitDef
        {
            MaxHp = 60, AttackDamage = 8, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 1, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 55, AttackDamage = 6, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 40, AttackDamage = 5, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 6.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 100, AttackDamage = 10, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.12, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 1, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 45, AttackDamage = 7, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 7.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 1, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 65, AttackDamage = 9, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 70, AttackDamage = 10, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 75, AttackDamage = 11, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 80, AttackDamage = 12, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 60, AttackDamage = 7, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 65, AttackDamage = 8, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Infantry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.09, Cooldown = 6,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 45, AttackDamage = 6, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 6.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 50, AttackDamage = 7, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 6.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 45, AttackDamage = 4, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 5.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 50, AttackDamage = 5, AttackClass = UnitAttackClass.Pierce, ArmorClass = UnitArmorClass.Archer,
            IsRanged = true, MinRangeTiles = 1.0, MaxRangeTiles = 5.0, SpeedTilesPerTick = 0.09, Cooldown = 8,
            ArmorMelee = 0, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 80, AttackDamage = 6, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.14, Cooldown = 6,
            ArmorMelee = 1, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 90, AttackDamage = 7, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.14, Cooldown = 6,
            ArmorMelee = 1, ArmorPierce = 0, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 120, AttackDamage = 12, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.12, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 2, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 140, AttackDamage = 14, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.12, Cooldown = 6,
            ArmorMelee = 2, ArmorPierce = 2, ArmorSiege = 0,
        },
        new UnitDef
        {
            MaxHp = 110, AttackDamage = 9, AttackClass = UnitAttackClass.Melee, ArmorClass = UnitArmorClass.Cavalry,
            IsRanged = false, MinRangeTiles = 1.0, MaxRangeTiles = 1.0, SpeedTilesPerTick = 0.12, Cooldown = 6,
            ArmorMelee = 1, ArmorPierce = 1, ArmorSiege = 0,
        },
    };

    public const long Villager = -1;

    /// <summary>运输船没有战斗组件，单独用一个负类型表记，生产入口按码头特判。</summary>
    public const long TransportShip = -2;

    public static UnitDef Get(long unitType) =>
        unitType >= 0 && unitType < Table.Length ? Table[(int)unitType] : Table[Swordsman];

    /// <summary>独特单位不属于通用家族，只有其所属文明可造。</summary>
    public static bool IsUnique(long unitType) => unitType == Longbowman;

    public static string DisplayName(long unitType) => unitType switch
    {
        Villager => "村民",
        TransportShip => "运输船",
        Swordsman => "剑士",
        Spearman => "长枪兵",
        Archer => "弓兵",
        Knight => "骑士",
        Longbowman => "长弓兵",
        ArmoredInfantry => "装甲步兵",
        LongSwordsman => "长剑士",
        TwoHandedSwordsman => "双手剑士",
        Champion => "冠军剑士",
        Pikeman => "长戟兵",
        Halberdier => "戟兵",
        Crossbowman => "弩手",
        Arbalest => "劲弩手",
        Skirmisher => "散兵",
        EliteSkirmisher => "精锐散兵",
        Scout => "侦察骑兵",
        LightCavalry => "轻骑兵",
        Cavalier => "重装骑士",
        Paladin => "圣骑士",
        Camel => "骆驼兵",
        _ => "单位",
    };

    public static ResourceCost Cost(long unitType) => unitType switch
    {
        Villager => new ResourceCost(wood: 0, food: 50, gold: 0, stone: 0),
        TransportShip => new ResourceCost(wood: 100, food: 0, gold: 0, stone: 0),
        Swordsman or ArmoredInfantry or LongSwordsman or TwoHandedSwordsman or Champion =>
            new ResourceCost(wood: 0, food: 60, gold: 0, stone: 0),
        Spearman or Pikeman or Halberdier =>
            new ResourceCost(wood: 25, food: 35, gold: 0, stone: 0),
        Archer or Crossbowman or Arbalest =>
            new ResourceCost(wood: 25, food: 0, gold: 45, stone: 0),
        Skirmisher or EliteSkirmisher =>
            new ResourceCost(wood: 25, food: 35, gold: 0, stone: 0),
        Knight or Cavalier or Paladin =>
            new ResourceCost(wood: 0, food: 60, gold: 75, stone: 0),
        Scout or LightCavalry =>
            new ResourceCost(wood: 0, food: 80, gold: 0, stone: 0),
        Camel => new ResourceCost(wood: 0, food: 55, gold: 60, stone: 0),
        Longbowman => new ResourceCost(wood: 35, food: 0, gold: 40, stone: 0),
        _ => default,
    };

    public static long PopulationCost(long unitType) =>
        unitType == Villager || unitType == TransportShip || unitType >= 0 ? 1 : 0;

    /// <summary>通用与升级兵种所有文明可用；独特单位只有其所属文明可造。</summary>
    public static bool IsUnitAvailable(long civ, long unitType)
    {
        if (unitType < 0 || unitType >= Table.Length)
        {
            return false;
        }
        return !IsUnique(unitType) || CivilizationCatalog.UniqueUnit(civ) == unitType;
    }

    /// <summary>
    /// 攻击方兵种对目标护甲类型的附加伤害。克制按家族与等级取值：
    /// 长枪/长戟/戟兵克骑兵，骆驼克骑兵，散兵克弓兵，弓兵系克步兵。
    /// </summary>
    public static long BonusDamage(long attackerType, long targetArmorClass)
    {
        long family = UnitFamilies.FamilyOf(attackerType);
        long level = UnitFamilies.LevelOf(attackerType);
        if (level < 0)
        {
            return 0;
        }
        if (family == UnitFamilies.Spear && targetArmorClass == UnitArmorClass.Cavalry)
        {
            return 15 + level * 7;
        }
        if (family == UnitFamilies.Camel && targetArmorClass == UnitArmorClass.Cavalry)
        {
            return 18;
        }
        if (family == UnitFamilies.Skirmisher && targetArmorClass == UnitArmorClass.Archer)
        {
            return 3 + level * 2;
        }
        if (family == UnitFamilies.Archer && targetArmorClass == UnitArmorClass.Infantry)
        {
            return 1 + level;
        }
        return 0;
    }
}
