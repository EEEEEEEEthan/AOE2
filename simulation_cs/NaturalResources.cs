namespace AoeSim;

/// <summary>
/// 陆地自然资源子类型。与 <see cref="SimComponents.NodeType"/>（0..3 的资源列索引）并存：
/// NodeType 决定产什么资源，Subtype 决定长什么样、怎么采。
/// 0 保留给未标注子类型的通用资源点，含农田与旧地图数据。
/// </summary>
public static class ResourceSubtype
{
    public const long None = 0;
    public const long Tree = 1;
    public const long BerryBush = 2;
    public const long GoldMine = 3;
    public const long StoneMine = 4;
    public const long Corpse = 5;
    public const long Fish = 6;

    public static bool IsValid(long subtype) => subtype >= None && subtype <= Fish;

    public static bool IsAnimalFood(long subtype) =>
        subtype == BerryBush || subtype == Corpse || subtype == Fish;

    /// <summary>采集进度倍率（以固定点为单位）：浆果与狩猎尸体比矿石、树木更省力。</summary>
    public static long GatherRate(long baseRate, long subtype)
    {
        if (subtype == Corpse)
        {
            return baseRate * 2;
        }
        if (subtype == BerryBush)
        {
            return baseRate + baseRate / 2;
        }
        return baseRate;
    }
}

/// <summary>可狩猎动物物种：血量、死亡后尸体剩余食物与腐烂倒计时。全程整数、确定性。</summary>
public static class AnimalSpecies
{
    public const long None = 0;
    public const long Sheep = 1;
    public const long Deer = 2;
    public const long Boar = 3;

    public static bool IsValid(long species) => species >= Sheep && species <= Boar;

    public static long MaxHp(long species) => species switch
    {
        Sheep => 20,
        Deer => 30,
        Boar => 60,
        _ => 0,
    };

    /// <summary>尸体可采集食物总量。</summary>
    public static long Meat(long species) => species switch
    {
        Sheep => 100,
        Deer => 140,
        Boar => 200,
        _ => 0,
    };

    /// <summary>尸体腐烂前的剩余 tick 数；归零后消失。</summary>
    public static long RotTicks(long species) => species switch
    {
        Sheep => 2400,
        Deer => 1800,
        Boar => 2400,
        _ => 0,
    };
}
