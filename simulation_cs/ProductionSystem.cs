namespace AoeSim;

/// <summary>
/// 生产队列：挂在完工建筑上的 PRODUCTION 组件，按实体 id 升序结算。
/// 入队立即扣费，取消按原价退款；当前项计时归零后在建筑邻格生成单位，
/// 有集结点则复用 <see cref="OrderSystem.ApplyRally"/> 下发一次移动。
/// 全部状态落在组件列里，天然进校验和；结算只用整数与确定性格子扫描。
/// </summary>
public static class ProductionSystem
{
    /// <summary>空槽哨兵：村民类型本身是 -1，因此空槽必须用另一个不可达值。</summary>
    public const long None = -1_000_000;

    /// <summary>某建筑当前生效的生产组件是否存在。</summary>
    public static bool Has(SimWorld world, long building) =>
        world.IsAlive(building) && world.HasComponent(building, SimComponents.Production);

    /// <summary>给建筑补上生产组件；已存在则原样返回。仅对已知生产建筑生效。</summary>
    public static bool Ensure(SimWorld world, long building)
    {
        if (!world.IsAlive(building) || !world.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        if (world.HasComponent(building, SimComponents.Production))
        {
            return true;
        }
        long type = world.GetValue(building, SimComponents.Building, SimComponents.BuildingType);
        if (!IsProductionBuilding(type))
        {
            return false;
        }
        world.Attach(building, SimComponents.Production);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdCurrent, None);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksTotal, 0);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdQueueCount, 0);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdRepeat, 0);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdRepeatType, None);
        foreach (string column in SimComponents.ProdQueueColumns)
        {
            world.SetValue(building, SimComponents.Production, column, None);
        }
        return true;
    }

    private static bool IsProductionBuilding(long type) =>
        type == BuildingTypes.TownCenter || type == BuildingTypes.Barracks;

    /// <summary>入队：校验建筑完工、兵种可造、队列未满、资源足够，立刻扣费后排队或直接开工。</summary>
    public static bool Train(GameSim sim, long building, long unitType)
    {
        SimWorld world = sim.World;
        if (!IsReadyBuilding(world, building))
        {
            return false;
        }
        long buildingType = world.GetValue(building, SimComponents.Building, SimComponents.BuildingType);
        long team = world.GetValue(building, SimComponents.Team, SimComponents.TeamId);
        if (!UnitCatalog.IsProducible(sim, team, buildingType, unitType))
        {
            return false;
        }
        if (QueuedTotal(world, building) >= UnitCatalog.QueueDepth)
        {
            return false;
        }
        if (!CanAfford(sim, team, unitType))
        {
            return false;
        }
        if (!Ensure(world, building))
        {
            return false;
        }
        Pay(sim, team, unitType);
        if (world.GetValue(building, SimComponents.Production, SimComponents.ProdCurrent) == None)
        {
            Start(world, building, unitType);
        }
        else
        {
            Enqueue(world, building, unitType);
        }
        return true;
    }

    /// <summary>取消训练：slot 0 为进行中的当前项，1..n 为排队位置（1 是最前）。按原价退款。</summary>
    public static bool Cancel(GameSim sim, long building, long slot)
    {
        SimWorld world = sim.World;
        if (!Has(world, building))
        {
            return false;
        }
        long team = world.GetValue(building, SimComponents.Team, SimComponents.TeamId);
        if (slot == 0)
        {
            long current = world.GetValue(building, SimComponents.Production, SimComponents.ProdCurrent);
            if (current == None)
            {
                return false;
            }
            Refund(sim, team, current);
            PullFront(world, building);
            return true;
        }
        long count = world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueCount);
        long index = slot - 1;
        if (index < 0 || index >= count)
        {
            return false;
        }
        string column = SimComponents.ProdQueueColumns[(int)index];
        Refund(sim, team, world.GetValue(building, SimComponents.Production, column));
        for (long from = index + 1; from < count; from++)
        {
            world.SetValue(
                building,
                SimComponents.Production,
                SimComponents.ProdQueueColumns[(int)from - 1],
                world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueColumns[(int)from]));
        }
        world.SetValue(
            building,
            SimComponents.Production,
            SimComponents.ProdQueueColumns[(int)count - 1],
            None);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdQueueCount, count - 1);
        return true;
    }

    public static bool SetRepeat(SimWorld world, long building, bool repeat)
    {
        if (!Has(world, building))
        {
            return false;
        }
        world.SetValue(
            building, SimComponents.Production, SimComponents.ProdRepeat, repeat ? 1 : 0);
        return true;
    }

    public static void Step(SimWorld world, GameSim sim)
    {
        ComponentStore store = world.Store(SimComponents.Production);
        if (store.Count == 0)
        {
            return;
        }
        foreach (long id in world.Entities())
        {
            if (!store.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            StepBuilding(world, sim, id);
        }
    }

    private static void StepBuilding(SimWorld world, GameSim sim, long id)
    {
        if (!world.HasComponent(id, SimComponents.Building))
        {
            return;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.State) != BuildingSystem.StateComplete)
        {
            return;
        }
        long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
        long current = world.GetValue(id, SimComponents.Production, SimComponents.ProdCurrent);
        if (current == None)
        {
            if (world.GetValue(id, SimComponents.Production, SimComponents.ProdQueueCount) > 0)
            {
                PullFront(world, id);
                return;
            }
            TryRepeat(world, sim, id, team);
            return;
        }
        long ticksLeft =
            world.GetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft) - 1;
        if (ticksLeft > 0)
        {
            world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft, ticksLeft);
            return;
        }
        CompleteCurrent(world, sim, id, team, current);
    }

    private static void CompleteCurrent(SimWorld world, GameSim sim, long id, long team, long unitType)
    {
        long pop = UnitCatalog.Population(unitType);
        // 人口满员时停在计时 0 等待，绝不吞掉队列项；人口归还后自然继续。
        if (!PopulationSystem.CanSpawn(world, team, pop))
        {
            world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
            return;
        }
        if (!TryFindSpawnCell(sim, id, out int cellX, out int cellY))
        {
            world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
            return;
        }
        long spawned = Spawn(sim, team, unitType, cellX, cellY, pop);
        if (spawned < 0)
        {
            world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
            return;
        }
        sim.ApplyRallyTo(id, spawned);
        world.SetValue(id, SimComponents.Production, SimComponents.ProdCurrent, None);
        world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
        world.SetValue(id, SimComponents.Production, SimComponents.ProdTicksTotal, 0);
        world.SetValue(id, SimComponents.Production, SimComponents.ProdRepeatType, unitType);
    }

    private static long Spawn(GameSim sim, long team, long unitType, int cellX, int cellY, long pop)
    {
        if (unitType == UnitDefs.Villager)
        {
            return sim.SpawnVillager(
                cellX, cellY, team, MapSetup.VillagerSpeed,
                MapSetup.VillagerCarryCapacity, MapSetup.VillagerGatherRate, pop);
        }
        return sim.SpawnUnit(cellX, cellY, team, unitType, CombatStance.Aggressive, pop);
    }

    private static void TryRepeat(SimWorld world, GameSim sim, long id, long team)
    {
        if (world.GetValue(id, SimComponents.Production, SimComponents.ProdRepeat) == 0)
        {
            return;
        }
        long repeatType =
            world.GetValue(id, SimComponents.Production, SimComponents.ProdRepeatType);
        if (repeatType == None || !CanAfford(sim, team, repeatType))
        {
            return;
        }
        Pay(sim, team, repeatType);
        Start(world, id, repeatType);
    }

    private static bool IsReadyBuilding(SimWorld world, long building)
    {
        if (!world.IsAlive(building) || !world.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        return world.GetValue(building, SimComponents.Building, SimComponents.State)
            == BuildingSystem.StateComplete;
    }

    private static long QueuedTotal(SimWorld world, long building)
    {
        long current = world.GetValue(building, SimComponents.Production, SimComponents.ProdCurrent);
        long count = world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueCount);
        return count + (current != None ? 1 : 0);
    }

    private static void Start(SimWorld world, long building, long unitType)
    {
        long ticks = UnitCatalog.Ticks(unitType);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdCurrent, unitType);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksLeft, ticks);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksTotal, ticks);
    }

    private static void PullFront(SimWorld world, long building)
    {
        long count = world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueCount);
        if (count <= 0)
        {
            world.SetValue(building, SimComponents.Production, SimComponents.ProdCurrent, None);
            world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksLeft, 0);
            world.SetValue(building, SimComponents.Production, SimComponents.ProdTicksTotal, 0);
            return;
        }
        long front = world.GetValue(
            building, SimComponents.Production, SimComponents.ProdQueueColumns[0]);
        for (long from = 1; from < count; from++)
        {
            world.SetValue(
                building,
                SimComponents.Production,
                SimComponents.ProdQueueColumns[(int)from - 1],
                world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueColumns[(int)from]));
        }
        world.SetValue(
            building,
            SimComponents.Production,
            SimComponents.ProdQueueColumns[(int)count - 1],
            None);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdQueueCount, count - 1);
        Start(world, building, front);
    }

    private static void Enqueue(SimWorld world, long building, long unitType)
    {
        long count = world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueCount);
        if (count < 0 || count >= UnitCatalog.QueueDepth)
        {
            return;
        }
        world.SetValue(
            building,
            SimComponents.Production,
            SimComponents.ProdQueueColumns[(int)count],
            unitType);
        world.SetValue(building, SimComponents.Production, SimComponents.ProdQueueCount, count + 1);
    }

    private static bool CanAfford(GameSim sim, long team, long unitType)
    {
        for (long kind = 0; kind < 4; kind++)
        {
            if (sim.Resource(team, kind) < UnitDefs.Cost(unitType).AmountOf(kind))
            {
                return false;
            }
        }
        return true;
    }

    private static void Pay(GameSim sim, long team, long unitType)
    {
        for (long kind = 0; kind < 4; kind++)
        {
            long cost = UnitDefs.Cost(unitType).AmountOf(kind);
            if (cost != 0)
            {
                sim.AddResource(team, kind, -cost);
            }
        }
    }

    private static void Refund(GameSim sim, long team, long unitType)
    {
        if (unitType == None)
        {
            return;
        }
        for (long kind = 0; kind < 4; kind++)
        {
            long cost = UnitDefs.Cost(unitType).AmountOf(kind);
            if (cost != 0)
            {
                sim.AddResource(team, kind, cost);
            }
        }
    }

    /// <summary>建筑 footprint 邻格首个可站立格：按 y、x 升序扫描，保证确定性。</summary>
    private static bool TryFindSpawnCell(GameSim sim, long building, out int cellX, out int cellY)
    {
        cellX = -1;
        cellY = -1;
        SimWorld world = sim.World;
        int originX = (int)Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.X));
        int originY = (int)Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.Y));
        int sizeWidth = (int)world.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        int sizeHeight = (int)world.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        for (int cellYIndex = originY - 1; cellYIndex <= originY + sizeHeight; cellYIndex++)
        {
            for (int cellXIndex = originX - 1; cellXIndex <= originX + sizeWidth; cellXIndex++)
            {
                if (Inside(cellXIndex, cellYIndex, originX, originY, sizeWidth, sizeHeight))
                {
                    continue;
                }
                if (!sim.Grid.InBounds(cellXIndex, cellYIndex)
                    || sim.Grid.IsBlocked(cellXIndex, cellYIndex))
                {
                    continue;
                }
                if (!sim.Terrain.IsLandPassable(cellXIndex, cellYIndex))
                {
                    continue;
                }
                cellX = cellXIndex;
                cellY = cellYIndex;
                return true;
            }
        }
        return false;
    }

    private static bool Inside(
        int cellX, int cellY, int originX, int originY, int sizeWidth, int sizeHeight) =>
        cellX >= originX && cellX < originX + sizeWidth
        && cellY >= originY && cellY < originY + sizeHeight;
}
