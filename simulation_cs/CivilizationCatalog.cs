namespace AoeSim;

/// <summary>单个文明的定义：独特单位、全局属性加成与被禁科技位图。</summary>
public sealed class CivilizationDef
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public long UniqueUnit { get; init; } = -1;
    public long BonusMaxHp { get; init; }
    public long BonusDamage { get; init; }
    public long BonusRangeMax { get; init; }
    public long BonusCooldown { get; init; }
    /// <summary>被本文明禁用的科技位图；位为 1 表示不可研究。</summary>
    public long DisabledTechMask { get; init; }
}

/// <summary>
/// 文明定义表。一期只落两个文明：通用 baseline 与一个带独特单位、缺失科技和
/// 全局攻击加成的示例，后续铺开全文明内容另开 issue。
/// 全部是整数常量，禁止 float，保证锁步校验和稳定。
/// </summary>
public static class CivilizationCatalog
{
    public const long Generic = 0;
    public const long Longbow = 1;
    public const long Count = 2;

    private static readonly CivilizationDef[] Table =
    {
        new CivilizationDef
        {
            Id = Generic,
            Name = "通用",
            UniqueUnit = -1,
            DisabledTechMask = 0,
        },
        new CivilizationDef
        {
            Id = Longbow,
            Name = "不列颠",
            UniqueUnit = UnitDefs.Longbowman,
            BonusDamage = 1,
            DisabledTechMask = TechCatalog.ResearchBit(TechCatalog.TechScaleMail),
        },
    };

    public static bool IsValid(long civ) => civ >= 0 && civ < Count;

    public static CivilizationDef Get(long civ) => IsValid(civ) ? Table[(int)civ] : Table[Generic];

    public static string Name(long civ) => Get(civ).Name;

    public static long UniqueUnit(long civ) => Get(civ).UniqueUnit;

    public static bool IsTechEnabled(long civ, long techId) =>
        TechCatalog.IsValidTech(techId)
        && (Get(civ).DisabledTechMask & TechCatalog.ResearchBit(techId)) == 0;

    /// <summary>文明对某一属性的全局加成，叠加在时代与科技加成之上。</summary>
    public static long StatBonus(long civ, long stat)
    {
        CivilizationDef def = Get(civ);
        return stat switch
        {
            TechCatalog.StatMaxHp => def.BonusMaxHp,
            TechCatalog.StatDamage => def.BonusDamage,
            TechCatalog.StatRangeMax => def.BonusRangeMax,
            TechCatalog.StatCooldown => def.BonusCooldown,
            _ => 0,
        };
    }
}
