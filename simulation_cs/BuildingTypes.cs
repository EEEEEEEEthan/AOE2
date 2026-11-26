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

    public static bool IsKnown(long type) => type >= TownCenter && type <= Market;

    public static int SizeWidth(long type) => type switch
    {
        TownCenter => 4,
        House => 2,
        Barracks => 3,
        Wall => 1,
        Tower => 2,
        Market => 4,
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
        _ => 0,
    };
}
