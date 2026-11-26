using System;

namespace AoeSim;

public static class GatherSystem
{
    public const long GatherStateIdle = 0;
    public const long GatherStateGathering = 1;
    public const long GatherStateReturning = 2;

    public const long GatherRangeSq = 147456;

    public static void Step(SimWorld world, Func<long, long> inventoryProvider, NavGrid grid, TerrainGrid? terrain = null)
    {
        ComponentStore gatherStore = world.Store(SimComponents.Gather);
        if (gatherStore.Count == 0)
        {
            return;
        }
        foreach (long id in gatherStore.ActiveIds())
        {
            long domain = Traversal.DomainOf(world, id);
            long state = gatherStore.GetValue(id, SimComponents.GatherState);
            if (state == GatherStateGathering)
            {
                StepGathering(world, id, grid, terrain, domain);
            }
            else if (state == GatherStateReturning)
            {
                StepReturning(world, id, inventoryProvider, grid, terrain, domain);
            }
        }
    }

    private static void StepGathering(SimWorld world, long id, NavGrid grid, TerrainGrid? terrain, long domain)
    {
        long node = world.GetValue(id, SimComponents.Gather, SimComponents.NodeEntity);
        long remaining = 0;
        if (IsResourceNode(world, node))
        {
            remaining = world.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining);
        }
        if (remaining <= 0)
        {
            LeaveGathering(world, id);
            return;
        }
        if (!InRange(world, id, node))
        {
            MoveToCell(world, id, node, grid, terrain, domain);
            return;
        }
        Stop(world, id);
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long carryCapacity = world.GetValue(id, SimComponents.Gather, SimComponents.CarryCapacity);
        if (carryAmount == 0)
        {
            world.SetValue(
                id,
                SimComponents.Gather,
                SimComponents.CarryType,
                world.GetValue(node, SimComponents.ResourceNode, SimComponents.NodeType));
        }
        long subtype = world.GetValue(node, SimComponents.ResourceNode, SimComponents.Subtype);
        long progress = world.GetValue(id, SimComponents.Gather, SimComponents.Progress)
            + ResourceSubtype.GatherRate(world.GetValue(id, SimComponents.Gather, SimComponents.Rate), subtype);
        while (progress >= Fixed.Scale && carryAmount < carryCapacity && remaining > 0)
        {
            progress -= Fixed.Scale;
            carryAmount += 1;
            remaining -= 1;
        }
        world.SetValue(id, SimComponents.Gather, SimComponents.Progress, progress);
        world.SetValue(id, SimComponents.Gather, SimComponents.CarryAmount, carryAmount);
        world.SetValue(node, SimComponents.ResourceNode, SimComponents.Remaining, remaining);
        if (carryAmount >= carryCapacity || remaining <= 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateReturning);
        }
    }

    private static void StepReturning(
        SimWorld world, long id, Func<long, long> inventoryProvider, NavGrid grid, TerrainGrid? terrain, long domain)
    {
        long drop = world.GetValue(id, SimComponents.Gather, SimComponents.DropEntity);
        if (!IsDropSite(world, drop))
        {
            drop = ReselectDropSite(world, id);
            if (drop < 0)
            {
                world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
                return;
            }
            world.SetValue(id, SimComponents.Gather, SimComponents.DropEntity, drop);
        }
        if (!InRange(world, id, drop))
        {
            MoveToCell(world, id, drop, grid, terrain, domain);
            return;
        }
        Stop(world, id);
        long carryType = world.GetValue(id, SimComponents.Gather, SimComponents.CarryType);
        long acceptMask = world.GetValue(drop, SimComponents.DropSite, SimComponents.AcceptMask);
        if ((acceptMask & (1L << (int)carryType)) == 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
            return;
        }
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long inventory = inventoryProvider(world.GetValue(id, SimComponents.Team, SimComponents.TeamId));
        if (inventory < 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
            return;
        }
        string column = SimComponents.ResourceColumns[(int)carryType];
        long stored = world.GetValue(inventory, SimComponents.Resource, column);
        world.SetValue(inventory, SimComponents.Resource, column, stored + carryAmount);
        world.SetValue(id, SimComponents.Gather, SimComponents.CarryAmount, 0);
        world.SetValue(id, SimComponents.Gather, SimComponents.Progress, 0);
        long node = world.GetValue(id, SimComponents.Gather, SimComponents.NodeEntity);
        if (IsResourceNode(world, node)
            && world.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining) > 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateGathering);
        }
        else
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
        }
    }

    private static void LeaveGathering(SimWorld world, long id)
    {
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long nextState = carryAmount > 0 ? GatherStateReturning : GatherStateIdle;
        world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, nextState);
    }

    private static bool IsResourceNode(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.ResourceNode);

    private static bool IsDropSite(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.DropSite) && IsUsableDropSite(world, id);

    /// <summary>建筑投放点须已完工；独立投放点没有 Building 组件，恒可用。</summary>
    private static bool IsUsableDropSite(SimWorld world, long id) =>
        !world.HasComponent(id, SimComponents.Building)
        || world.GetValue(id, SimComponents.Building, SimComponents.State) == BuildingSystem.StateComplete;

    /// <summary>投放点失效后按携带资源改投最近可用建筑，找不到则返回 -1。</summary>
    private static long ReselectDropSite(SimWorld world, long id)
    {
        long resourceKind = world.GetValue(id, SimComponents.Gather, SimComponents.CarryType);
        long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
        long x = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        long y = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
        return NearestDropSite(world, team, resourceKind, x, y);
    }

    /// <summary>
    /// 在己方已完工、且接收该资源的建筑中就近选点：按 footprint 最近格曼哈顿距离，
    /// 平手取实体 id 较小者，保证确定性与跨端一致。
    /// </summary>
    public static long NearestDropSite(SimWorld world, long team, long resourceKind, long fromX, long fromY)
    {
        long bit = 1L << (int)resourceKind;
        long bestId = -1;
        long bestDistance = long.MaxValue;
        ComponentStore drops = world.Store(SimComponents.DropSite);
        foreach (long id in drops.ActiveIds())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            if (!IsUsableDropSite(world, id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if ((world.GetValue(id, SimComponents.DropSite, SimComponents.AcceptMask) & bit) == 0)
            {
                continue;
            }
            long distance = FootprintDistance(world, id, fromX, fromY);
            if (distance < bestDistance || (distance == bestDistance && (bestId < 0 || id < bestId)))
            {
                bestDistance = distance;
                bestId = id;
            }
        }
        return bestId;
    }

    private static long FootprintDistance(SimWorld world, long building, long fromX, long fromY)
    {
        long originX = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.X));
        long originY = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.Y));
        long sizeWidth = world.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        long sizeHeight = world.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        long distanceX = System.Math.Max(0, System.Math.Max(originX - fromX, fromX - (originX + sizeWidth - 1)));
        long distanceY = System.Math.Max(0, System.Math.Max(originY - fromY, fromY - (originY + sizeHeight - 1)));
        return distanceX + distanceY;
    }

    private static bool InRange(SimWorld world, long first, long second)
    {
        long firstX = world.GetValue(first, SimComponents.Position, SimComponents.X);
        long firstY = world.GetValue(first, SimComponents.Position, SimComponents.Y);
        if (world.HasComponent(second, SimComponents.Building))
        {
            return InRangeOfFootprint(world, firstX, firstY, second);
        }
        long secondX = world.GetValue(second, SimComponents.Position, SimComponents.X);
        long secondY = world.GetValue(second, SimComponents.Position, SimComponents.Y);
        long deltaX = Fixed.Sub(secondX, firstX);
        long deltaY = Fixed.Sub(secondY, firstY);
        long distanceSq = Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY));
        return distanceSq <= GatherRangeSq;
    }

    /// <summary>农田等建筑节点：以 footprint 边缘判定，村民站在相邻格即可采集。</summary>
    private static bool InRangeOfFootprint(SimWorld world, long firstX, long firstY, long building)
    {
        long originX = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.X));
        long originY = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.Y));
        long sizeWidth = world.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        long sizeHeight = world.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        long cellX = Fixed.ToInt(firstX);
        long cellY = Fixed.ToInt(firstY);
        long distanceX = System.Math.Max(0, System.Math.Max(originX - cellX, cellX - (originX + sizeWidth - 1)));
        long distanceY = System.Math.Max(0, System.Math.Max(originY - cellY, cellY - (originY + sizeHeight - 1)));
        return distanceX <= 1 && distanceY <= 1;
    }

    private static void MoveToCell(SimWorld world, long id, long target, NavGrid grid, TerrainGrid? terrain, long domain)
    {
        long targetX;
        long targetY;
        if (world.HasComponent(target, SimComponents.Building))
        {
            long workerX = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
            long workerY = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
            (long approachX, long approachY) = BuildingApproachCell(world, grid, terrain, domain, target, workerX, workerY);
            if (approachX < 0)
            {
                return;
            }
            targetX = approachX;
            targetY = approachY;
        }
        else
        {
            targetX = Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.X));
            targetY = Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.Y));
        }
        world.SetValue(id, SimComponents.Move, SimComponents.TargetX, targetX);
        world.SetValue(id, SimComponents.Move, SimComponents.TargetY, targetY);
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    private static (long, long) BuildingApproachCell(
        SimWorld world, NavGrid grid, TerrainGrid? terrain, long domain, long building, long workerX, long workerY)
    {
        long originX = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.X));
        long originY = Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.Y));
        long sizeWidth = world.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        long sizeHeight = world.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        long bestX = -1;
        long bestY = -1;
        long bestDistance = 0;
        for (long cellY = originY - 1; cellY <= originY + sizeHeight; cellY++)
        {
            for (long cellX = originX - 1; cellX <= originX + sizeWidth; cellX++)
            {
                bool inside = cellX >= originX && cellX < originX + sizeWidth
                    && cellY >= originY && cellY < originY + sizeHeight;
                if (inside)
                {
                    continue;
                }
                if (!Traversal.CellPassable(grid, terrain, (int)cellX, (int)cellY, domain))
                {
                    continue;
                }
                long distance = System.Math.Abs(cellX - workerX) + System.Math.Abs(cellY - workerY);
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

    private static void Stop(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }
}
