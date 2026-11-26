namespace AoeSim;

/// <summary>一期建筑类型表：占地、血量与施工耗时全部走固定整数，保证跨端一致。</summary>
public static class BuildingTypes
{
    public const long None = 0;
    public const long TownCenter = 1;
    public const long House = 2;
    public const long Barracks = 3;
    public const long Wall = 4;
    public const long Tower = 5;
    public const long Market = 6;
    public const long Farm = 7;

    /// <summary>一块农田完工后挂载的食物节点总量，耗尽后需重播。</summary>
    public const long FarmFoodAmount = 250;

    /// <summary>空田手动重播消耗的木材。</summary>
    public const long FarmReseedCost = 60;

    public static bool IsKnown(long type) => type >= TownCenter && type <= Farm;

    /// <summary>建筑所属时代：升城堡/帝国时统计上一时代的已完成建筑数量。</summary>
    public static long AgeOf(long type) => type switch
    {
        TownCenter => TechCatalog.AgeDark,
        House => TechCatalog.AgeDark,
        Barracks => TechCatalog.AgeFeudal,
        Wall => TechCatalog.AgeDark,
        Tower => TechCatalog.AgeCastle,
        Market => TechCatalog.AgeFeudal,
        Farm => TechCatalog.AgeDark,
        _ => TechCatalog.AgeDark,
    };

    public static int SizeWidth(long type) => type switch
    {
        TownCenter => 4,
        House => 2,
        Barracks => 3,
        Wall => 1,
        Tower => 2,
        Market => 4,
        Farm => 3,
        _ => 0,
    };

    public static int SizeHeight(long type) => type switch
    {
        TownCenter => 4,
        House => 2,
        Barracks => 3,
        Wall => 1,
        Tower => 2,
        Market => 3,
        Farm => 3,
        _ => 0,
    };

    public static long MaxHp(long type) => type switch
    {
        TownCenter => 2400,
        House => 550,
        Barracks => 1200,
        Wall => 500,
        Tower => 1000,
        Market => 1500,
        Farm => 480,
        _ => 0,
    };

    public static long BuildTicks(long type) => type switch
    {
        TownCenter => 240,
        House => 60,
        Barracks => 150,
        Wall => 24,
        Tower => 120,
        Market => 180,
        Farm => 90,
        _ => 0,
    };

    public static long Cost(long type) => type switch
    {
        TownCenter => 275,
        House => 25,
        Barracks => 175,
        Wall => 5,
        Tower => 125,
        Market => 175,
        Farm => 60,
        _ => 0,
    };
}
