namespace AoeSim;

/// <summary>
/// 生产目录：补充 <see cref="UnitDefs"/> 未覆盖的生产端参数——耗时、队列上限与可造建筑。
/// 造价、人口与文明可用性直接复用 <see cref="UnitDefs"/>，不重复建模。
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
        if (buildingType != BuildingTypes.Barracks)
        {
            return false;
        }
        return UnitDefs.IsUnitAvailable(sim.Civ(team), unitType);
    }

    public static long Ticks(long unitType) => unitType switch
    {
        UnitDefs.Villager => 25,
        UnitDefs.TransportShip => 40,
        UnitDefs.Swordsman => 21,
        UnitDefs.Spearman => 22,
        UnitDefs.Archer => 35,
        UnitDefs.Knight => 30,
        _ => 35,
    };

    public static long Population(long unitType) => UnitDefs.PopulationCost(unitType);
}
