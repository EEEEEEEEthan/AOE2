namespace AoeSim;

/// <summary>
/// 建筑一期：放置校验、多格占地写入寻路层、村民施工推进、摧毁转废墟并释放占地。
/// 训练/人口/科技/驻军/视野只预留组件与指令接口，由后续任务各自实现。
/// </summary>
public static class BuildingSystem
{
    public const long StateFoundation = 0;
    public const long StateComplete = 1;
    public const long StateRuins = 2;

    public const long ConstructActive = 0;
    public const long ConstructDone = 1;

    public static bool CanPlace(NavGrid grid, TerrainGrid terrain, long type, int originX, int originY)
    {
        if (!BuildingTypes.IsKnown(type))
        {
            return false;
        }
        int sizeWidth = BuildingTypes.SizeWidth(type);
        int sizeHeight = BuildingTypes.SizeHeight(type);
        for (int cellY = originY; cellY < originY + sizeHeight; cellY++)
        {
            for (int cellX = originX; cellX < originX + sizeWidth; cellX++)
            {
                if (!grid.InBounds(cellX, cellY))
                {
                    return false;
                }
                if (grid.IsTerrainBlocked(cellX, cellY) || grid.IsBuildingBlocked(cellX, cellY))
                {
                    return false;
                }
                if (!IsBuildableGround(terrain, cellX, cellY))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsBuildableGround(TerrainGrid terrain, int cellX, int cellY)
    {
        if (!terrain.InBounds(cellX, cellY))
        {
            return false;
        }
        byte type = terrain.TypeAt(cellX, cellY);
        return type != TerrainGrid.TypeWater && type != TerrainGrid.TypeCliff;
    }

    public static long Place(SimWorld world, NavGrid grid, TerrainGrid terrain, long type, int originX, int originY, long team)
    {
        if (!CanPlace(grid, terrain, type, originX, originY))
        {
            return -1;
        }
        long id = world.Spawn();
        world.Attach(id, SimComponents.Position);
        world.Attach(id, SimComponents.Health);
        world.Attach(id, SimComponents.Team);
        world.Attach(id, SimComponents.Building);
        world.Attach(id, SimComponents.Footprint);
        int sizeWidth = BuildingTypes.SizeWidth(type);
        int sizeHeight = BuildingTypes.SizeHeight(type);
        world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(originX), Fixed.Half));
        world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(originY), Fixed.Half));
        world.SetValue(id, SimComponents.Health, SimComponents.Hp, BuildingTypes.MaxHp(type));
        world.SetValue(id, SimComponents.Health, SimComponents.MaxHp, BuildingTypes.MaxHp(type));
        world.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        world.SetValue(id, SimComponents.Building, SimComponents.BuildingType, type);
        world.SetValue(id, SimComponents.Building, SimComponents.SizeWidth, sizeWidth);
        world.SetValue(id, SimComponents.Building, SimComponents.SizeHeight, sizeHeight);
        world.SetValue(id, SimComponents.Building, SimComponents.Progress, 0);
        world.SetValue(id, SimComponents.Building, SimComponents.MaxProgress, BuildingTypes.BuildTicks(type));
        world.SetValue(id, SimComponents.Building, SimComponents.State, StateFoundation);
        world.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, sizeWidth);
        world.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, sizeHeight);
        SetBlock(world, grid, id, true);
        return id;
    }

    public static void Step(SimWorld world, NavGrid grid, TerrainGrid terrain)
    {
        ComponentStore constructStore = world.Store(SimComponents.Construct);
        if (constructStore.Count == 0)
        {
            return;
        }
        foreach (long id in constructStore.ActiveIds())
        {
            StepWorker(world, grid, terrain, id);
        }
    }

    /// <summary>施工完工结算：写满进度、转完成态并一次性发放该建筑的人口上限。</summary>
    public static void Complete(SimWorld world, long id)
    {
        if (!world.HasComponent(id, SimComponents.Building))
        {
            return;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.State) == StateComplete)
        {
            return;
        }
        long maxProgress = world.GetValue(id, SimComponents.Building, SimComponents.MaxProgress);
        world.SetValue(id, SimComponents.Building, SimComponents.Progress, maxProgress);
        world.SetValue(id, SimComponents.Building, SimComponents.State, StateComplete);
        AddPopulationCap(world, id);
    }

    public static void Ruin(SimWorld world, NavGrid grid, long id)
    {
        if (!world.HasComponent(id, SimComponents.Building))
        {
            return;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.State) == StateRuins)
        {
            return;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.State) == StateComplete)
        {
            AddPopulationCap(world, id, -1);
        }
        world.SetValue(id, SimComponents.Building, SimComponents.State, StateRuins);
        world.SetValue(id, SimComponents.Health, SimComponents.Hp, 0);
        world.Detach(id, SimComponents.Health);
        world.Detach(id, SimComponents.Footprint);
        world.Detach(id, SimComponents.ResourceNode);
        SetBlock(world, grid, id, false);
    }

    /// <summary>
    /// 空田重播：只对已完工且已耗尽的农田生效，重置食物节点剩余量。
    /// 扣费与归属校验放在 <see cref="GameSim.IssueReseedFarm"/>。
    /// </summary>
    public static bool ReseedFarm(SimWorld world, long id)
    {
        if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Building))
        {
            return false;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.Farm)
        {
            return false;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.State) != StateComplete)
        {
            return false;
        }
        if (world.HasComponent(id, SimComponents.ResourceNode)
            && world.GetValue(id, SimComponents.ResourceNode, SimComponents.Remaining) > 0)
        {
            return false;
        }
        AttachFoodNode(world, id);
        return true;
    }

    private static void AttachFoodNode(SimWorld world, long id)
    {
        if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.Farm)
        {
            return;
        }
        if (!world.HasComponent(id, SimComponents.ResourceNode))
        {
            world.Attach(id, SimComponents.ResourceNode);
        }
        world.SetValue(id, SimComponents.ResourceNode, SimComponents.NodeType, GameSim.ResourceFood);
        world.SetValue(
            id, SimComponents.ResourceNode, SimComponents.Remaining, BuildingTypes.FarmFoodAmount);
    }

    private static void StepWorker(SimWorld world, NavGrid grid, TerrainGrid terrain, long id)
    {
        long buildingId = world.GetValue(id, SimComponents.Construct, SimComponents.TargetBase);
        if (!IsConstructable(world, buildingId))
        {
            world.Detach(id, SimComponents.Construct);
            return;
        }
        if (!world.HasComponent(id, SimComponents.Position))
        {
            return;
        }
        int originX = (int)Fixed.ToInt(
            world.GetValue(buildingId, SimComponents.Position, SimComponents.X));
        int originY = (int)Fixed.ToInt(
            world.GetValue(buildingId, SimComponents.Position, SimComponents.Y));
        int sizeWidth = (int)world.GetValue(buildingId, SimComponents.Building, SimComponents.SizeWidth);
        int sizeHeight = (int)world.GetValue(buildingId, SimComponents.Building, SimComponents.SizeHeight);
        int workerX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        int workerY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
        if (IsAdjacent(workerX, workerY, originX, originY, sizeWidth, sizeHeight))
        {
            StopWorker(world, id);
            AdvanceProgress(world, id, buildingId);
            return;
        }
        Approach(world, grid, terrain, id, workerX, workerY, originX, originY, sizeWidth, sizeHeight);
    }

    private static bool IsConstructable(SimWorld world, long buildingId)
    {
        if (!world.IsAlive(buildingId) || !world.HasComponent(buildingId, SimComponents.Building))
        {
            return false;
        }
        return world.GetValue(buildingId, SimComponents.Building, SimComponents.State) == StateFoundation;
    }

    private static bool IsAdjacent(
        int workerX, int workerY, int originX, int originY, int sizeWidth, int sizeHeight)
    {
        int nearX = System.Math.Clamp(workerX, originX, originX + sizeWidth - 1);
        int nearY = System.Math.Clamp(workerY, originY, originY + sizeHeight - 1);
        int distanceX = System.Math.Abs(workerX - nearX);
        int distanceY = System.Math.Abs(workerY - nearY);
        if (distanceX == 0 && distanceY == 0)
        {
            return false;
        }
        return distanceX <= 1 && distanceY <= 1;
    }

    private static void AdvanceProgress(SimWorld world, long workerId, long buildingId)
    {
        long progress = world.GetValue(buildingId, SimComponents.Building, SimComponents.Progress) + 1;
        long maxProgress = world.GetValue(buildingId, SimComponents.Building, SimComponents.MaxProgress);
        if (progress >= maxProgress)
        {
            Complete(world, buildingId);
            world.Detach(workerId, SimComponents.Construct);
            AttachFoodNode(world, buildingId);
            return;
        }
        world.SetValue(buildingId, SimComponents.Building, SimComponents.Progress, progress);
    }

    private static void AddPopulationCap(SimWorld world, long id, long sign = 1)
    {
        long type = world.GetValue(id, SimComponents.Building, SimComponents.BuildingType);
        long cap = BuildingTypes.PopulationCap(type);
        if (cap == 0)
        {
            return;
        }
        long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
        PopulationSystem.AddCap(world, team, cap * sign);
    }

    private static void Approach(
        SimWorld world,
        NavGrid grid,
        TerrainGrid terrain,
        long workerId,
        int workerX,
        int workerY,
        int originX,
        int originY,
        int sizeWidth,
        int sizeHeight)
    {
        if (!world.HasComponent(workerId, SimComponents.Move))
        {
            return;
        }
        (int approachX, int approachY) = ApproachCell(
            grid, terrain, workerX, workerY, originX, originY, sizeWidth, sizeHeight);
        if (approachX < 0)
        {
            return;
        }
        world.SetValue(workerId, SimComponents.Move, SimComponents.TargetX, approachX);
        world.SetValue(workerId, SimComponents.Move, SimComponents.TargetY, approachY);
        world.SetValue(workerId, SimComponents.Move, SimComponents.Active, 1);
    }

    private static (int, int) ApproachCell(
        NavGrid grid,
        TerrainGrid terrain,
        int workerX,
        int workerY,
        int originX,
        int originY,
        int sizeWidth,
        int sizeHeight)
    {
        int bestX = -1;
        int bestY = -1;
        int bestDistance = 0;
        for (int cellY = originY - 1; cellY <= originY + sizeHeight; cellY++)
        {
            for (int cellX = originX - 1; cellX <= originX + sizeWidth; cellX++)
            {
                if (Inside(cellX, cellY, originX, originY, sizeWidth, sizeHeight))
                {
                    continue;
                }
                if (!grid.InBounds(cellX, cellY) || grid.IsBlocked(cellX, cellY))
                {
                    continue;
                }
                if (!IsBuildableGround(terrain, cellX, cellY))
                {
                    continue;
                }
                int distance = System.Math.Abs(cellX - workerX) + System.Math.Abs(cellY - workerY);
                bool better = bestX < 0
                    || distance < bestDistance
                    || (distance == bestDistance
                        && (cellX < bestX || (cellX == bestX && cellY < bestY)));
                if (better)
                {
                    bestX = cellX;
                    bestY = cellY;
                    bestDistance = distance;
                }
            }
        }
        return (bestX, bestY);
    }

    private static bool Inside(
        int cellX, int cellY, int originX, int originY, int sizeWidth, int sizeHeight) =>
        cellX >= originX && cellX < originX + sizeWidth
        && cellY >= originY && cellY < originY + sizeHeight;

    private static void StopWorker(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }

    private static void SetBlock(SimWorld world, NavGrid grid, long id, bool value)
    {
        int originX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        int originY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
        int sizeWidth = (int)world.GetValue(id, SimComponents.Building, SimComponents.SizeWidth);
        int sizeHeight = (int)world.GetValue(id, SimComponents.Building, SimComponents.SizeHeight);
        grid.SetBuildingBlockedRect(originX, originY, sizeWidth, sizeHeight, value);
    }
}
