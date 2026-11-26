namespace AoeSim;

/// <summary>
/// 生产目录：补充 <see cref="UnitDefs"/> 未覆盖的生产端参数——耗时、队列上限与可造建筑。
/// 造价、人口与文明可用性直接复用 <see cref="UnitDefs"/>，不重复建模。
/// 训练入口只接受家族基础兵；升级等级在出生结算时按玩家当前研究位图解析。
/// </summary>
public static class UnitCatalog
{
    public const int QueueDepth = 5;

    public const long Villager = UnitDefs.Villager;
    public const long Swordsman = UnitDefs.Swordsman;
    public const long Spearman = UnitDefs.Spearman;
    public const long Archer = UnitDefs.Archer;
    public const long Knight = UnitDefs.Knight;
    public const long TransportShip = UnitDefs.TransportShip;
    public const long BatteringRam = UnitDefs.BatteringRam;
    public const long Mangonel = UnitDefs.Mangonel;

    public static bool IsProducible(GameSim sim, long team, long buildingType, long unitType)
    {
        if (unitType == UnitDefs.Villager)
        {
            return buildingType == BuildingTypes.TownCenter;
        }
        if (unitType == UnitDefs.TransportShip)
        {
            return buildingType == BuildingTypes.Dock;
        }
        long civ = sim.Civ(team);
        if (!UnitDefs.IsUnitAvailable(civ, unitType) || !UnitFamilies.IsTrainableBase(unitType))
        {
            return false;
        }
        return buildingType switch
        {
            BuildingTypes.Barracks =>
                unitType == UnitDefs.Swordsman || unitType == UnitDefs.Spearman,
            BuildingTypes.ArcheryRange =>
                unitType == UnitDefs.Archer || unitType == UnitDefs.Skirmisher,
            BuildingTypes.Stable =>
                unitType == UnitDefs.Knight
                || unitType == UnitDefs.Scout
                || unitType == UnitDefs.Camel,
            BuildingTypes.SiegeWorkshop =>
                unitType == UnitDefs.BatteringRam || unitType == UnitDefs.Mangonel,
            BuildingTypes.Castle => unitType == CivilizationCatalog.UniqueUnit(civ),
            _ => false,
        };
    }

    public static long Ticks(long unitType) => unitType switch
    {
        UnitDefs.Villager => 25,
        UnitDefs.TransportShip => 40,
        UnitDefs.Swordsman => 21,
        UnitDefs.Spearman => 22,
        UnitDefs.Archer => 35,
        UnitDefs.Knight => 30,
        UnitDefs.Skirmisher => 34,
        UnitDefs.Scout => 28,
        UnitDefs.Camel => 32,
        UnitDefs.BatteringRam => 36,
        UnitDefs.Mangonel => 40,
        _ => 35,
    };

    public static long Population(long unitType) => UnitDefs.PopulationCost(unitType);
}
