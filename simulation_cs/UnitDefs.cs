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
/// 数据驱动的兵种表。先落剑士 / 长枪兵 / 弓兵 / 骑士四种，
/// 用长枪兵克骑兵、弓兵克步兵验证克制关系。
/// </summary>
public static class UnitDefs
{
    public const long Swordsman = 0;
    public const long Spearman = 1;
    public const long Archer = 2;
    public const long Knight = 3;

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
    };

    public static UnitDef Get(long unitType) =>
        unitType >= 0 && unitType < Table.Length ? Table[(int)unitType] : Table[Swordsman];

    /// <summary>攻击方兵种对目标护甲类型的附加伤害。</summary>
    public static long BonusDamage(long attackerType, long targetArmorClass) => attackerType switch
    {
        Spearman when targetArmorClass == UnitArmorClass.Cavalry => 15,
        Archer when targetArmorClass == UnitArmorClass.Infantry => 1,
        _ => 0,
    };
}
