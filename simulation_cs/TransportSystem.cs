using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 运输船装载/卸载：复用驻军的「移出世界占位」思路，乘员上船后摘掉位置/移动/指令等运行时组件，
/// 只在船上保留血量为击沉时一并清除。载入/放出都要求船体靠岸或紧邻本方已完工码头，
/// 卸载在船体周边按距离与坐标确定性选取陆地落点，落满为止、装不下的乘员留在船上。
/// 全部结算只用整数与实体 id 升序，随世界校验和同步。
/// </summary>
public static class TransportSystem
{
    /// <summary>原作默认运输船容量 5。</summary>
    public const long DefaultCapacity = 5;

    /// <summary>登船时单位与船体允许的最大曼哈顿格距。</summary>
    public const long LoadRange = 2;

    /// <summary>卸载时在船体周围搜寻落点的切比雪夫半径。</summary>
    private const int UnloadRadius = 2;

    public static long Count(SimWorld world, long ship) =>
        world.HasComponent(ship, SimComponents.Transport)
            ? world.GetValue(ship, SimComponents.Transport, SimComponents.Count)
            : 0;

    public static long Capacity(SimWorld world, long ship) =>
        world.HasComponent(ship, SimComponents.Transport)
            ? world.GetValue(ship, SimComponents.Transport, SimComponents.Capacity)
            : 0;

    public static bool CanLoad(
        SimWorld world, NavGrid grid, TerrainGrid terrain, long unit, long ship)
    {
        if (!world.IsAlive(ship) || !world.HasComponent(ship, SimComponents.Transport))
        {
            return false;
        }
        if (!world.IsAlive(unit)
            || !world.HasComponent(unit, SimComponents.Position)
            || !world.HasComponent(unit, SimComponents.Move))
        {
            return false;
        }
        if (world.HasComponent(unit, SimComponents.Aboard)
            || world.HasComponent(unit, SimComponents.Garrisoned))
        {
            return false;
        }
        if (world.HasComponent(unit, SimComponents.Building)
            || world.HasComponent(unit, SimComponents.ResourceNode)
            || world.HasComponent(unit, SimComponents.Monk))
        {
            return false;
        }
        if (Traversal.DomainOf(world, unit) != MovementDomain.Land)
        {
            return false;
        }
        if (world.GetValue(unit, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(ship, SimComponents.Team, SimComponents.TeamId))
        {
            return false;
        }
        if (Count(world, ship) >= Capacity(world, ship))
        {
            return false;
        }
        if (!IsAtShoreOrDock(world, grid, terrain, ship))
        {
            return false;
        }
        int shipX = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.X));
        int shipY = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.Y));
        int unitX = (int)Fixed.ToInt(world.GetValue(unit, SimComponents.Position, SimComponents.X));
        int unitY = (int)Fixed.ToInt(world.GetValue(unit, SimComponents.Position, SimComponents.Y));
        return Math.Abs(unitX - shipX) + Math.Abs(unitY - shipY) <= LoadRange;
    }

    /// <summary>把单位装进运输船：记录是否在采集，随后摘掉占位/移动/指令/工作组件，从世界与选择集中隐藏。</summary>
    public static bool TryLoad(SimWorld world, NavGrid grid, TerrainGrid terrain, long unit, long ship)
    {
        if (!CanLoad(world, grid, terrain, unit, ship))
        {
            return false;
        }
        world.Attach(unit, SimComponents.Aboard);
        world.SetValue(unit, SimComponents.Aboard, SimComponents.TargetEntity, ship);
        world.SetValue(unit, SimComponents.Aboard, SimComponents.HadGather,
            world.HasComponent(unit, SimComponents.Gather) ? 1 : 0);

        long count = Count(world, ship) + 1;
        world.SetValue(ship, SimComponents.Transport, SimComponents.Count, count);

        world.Detach(unit, SimComponents.Position);
        world.Detach(unit, SimComponents.Move);
        world.Detach(unit, SimComponents.Velocity);
        world.Detach(unit, SimComponents.Footprint);
        world.Detach(unit, SimComponents.Order);
        world.Detach(unit, SimComponents.Gather);
        world.Detach(unit, SimComponents.AttackTarget);
        world.Detach(unit, SimComponents.Engage);
        world.Detach(unit, SimComponents.Construct);
        world.Detach(unit, SimComponents.Repair);
        world.Detach(unit, SimComponents.Trade);
        return true;
    }

    /// <summary>船上有人且已靠岸或临本方码头时才允许卸载，供 UI 判断按钮可用性。</summary>
    public static bool CanUnload(SimWorld world, NavGrid grid, TerrainGrid terrain, long ship) =>
        world.IsAlive(ship)
        && world.HasComponent(ship, SimComponents.Transport)
        && Count(world, ship) > 0
        && IsAtShoreOrDock(world, grid, terrain, ship);

    /// <summary>在船体周边寻找合法陆地落点，按实体 id 升序放出乘员；落点用完或船未靠岸即停止。</summary>
    public static int TryUnload(SimWorld world, NavGrid grid, TerrainGrid terrain, long ship)
    {
        if (!CanUnload(world, grid, terrain, ship))
        {
            return 0;
        }
        var passengers = new List<long>();
        ComponentStore aboardStore = world.Store(SimComponents.Aboard);
        foreach (long id in aboardStore.ActiveIds())
        {
            if (aboardStore.GetValue(id, SimComponents.TargetEntity) == ship)
            {
                passengers.Add(id);
            }
        }
        if (passengers.Count == 0)
        {
            return 0;
        }
        List<(int X, int Y)> cells = FindDropCells(world, grid, terrain, ship, passengers.Count);
        long remaining = Count(world, ship);
        int unloaded = 0;
        foreach (long unit in passengers)
        {
            if (unloaded >= cells.Count)
            {
                break;
            }
            (int cellX, int cellY) = cells[unloaded];
            bool hadGather = world.GetValue(unit, SimComponents.Aboard, SimComponents.HadGather) != 0;
            PlaceUnit(world, unit, cellX, cellY, hadGather);
            unloaded += 1;
        }
        remaining -= unloaded;
        world.SetValue(
            ship, SimComponents.Transport, SimComponents.Count, remaining > 0 ? remaining : 0);
        return unloaded;
    }

    /// <summary>运输船被击沉：船上乘员一并随船损失，先归还人口再销毁实体。</summary>
    public static int SinkPassengers(SimWorld world, long ship)
    {
        int componentId = world.ComponentId(SimComponents.Aboard);
        if (componentId < 0)
        {
            return 0;
        }
        ComponentStore store = world.Store(componentId);
        var doomed = new List<long>();
        foreach (long id in store.ActiveIds())
        {
            if (store.GetValue(id, SimComponents.TargetEntity) == ship)
            {
                doomed.Add(id);
            }
        }
        foreach (long id in doomed)
        {
            PopulationSystem.Release(world, id);
            world.Despawn(id);
        }
        return doomed.Count;
    }

    private static void PlaceUnit(SimWorld world, long unit, int cellX, int cellY, bool hadGather)
    {
        world.Attach(unit, SimComponents.Position);
        world.SetValue(unit, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        world.SetValue(unit, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        world.Attach(unit, SimComponents.Move);
        world.SetValue(unit, SimComponents.Move, SimComponents.TargetX, cellX);
        world.SetValue(unit, SimComponents.Move, SimComponents.TargetY, cellY);
        world.SetValue(unit, SimComponents.Move, SimComponents.Active, 0);
        world.Attach(unit, SimComponents.Velocity);
        world.SetValue(unit, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(unit, SimComponents.Velocity, SimComponents.Y, 0);
        world.Attach(unit, SimComponents.Footprint);
        world.SetValue(unit, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        world.SetValue(unit, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        if (hadGather)
        {
            world.Attach(unit, SimComponents.Gather);
        }
        world.Detach(unit, SimComponents.Aboard);
    }

    /// <summary>船临岸（相邻格为合法陆地）或紧邻本方已完工码头的任一占地面积。</summary>
    private static bool IsAtShoreOrDock(
        SimWorld world, NavGrid grid, TerrainGrid terrain, long ship)
    {
        if (!world.HasComponent(ship, SimComponents.Position))
        {
            return false;
        }
        int shipX = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.X));
        int shipY = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.Y));
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }
                if (Traversal.CellPassable(
                    grid, terrain, shipX + offsetX, shipY + offsetY, MovementDomain.Land))
                {
                    return true;
                }
            }
        }
        long team = world.GetValue(ship, SimComponents.Team, SimComponents.TeamId);
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.Dock)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.State) != BuildingSystem.StateComplete)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            int originX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
            int originY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
            int width = (int)world.GetValue(id, SimComponents.Building, SimComponents.SizeWidth);
            int height = (int)world.GetValue(id, SimComponents.Building, SimComponents.SizeHeight);
            if (shipX >= originX - 1 && shipX <= originX + width
                && shipY >= originY - 1 && shipY <= originY + height)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>在船体半径内挑合法陆地格：排除船格、建筑占位与已有单位格，按距离、x、y 升序取前若干个。</summary>
    private static List<(int X, int Y)> FindDropCells(
        SimWorld world, NavGrid grid, TerrainGrid terrain, long ship, int limit)
    {
        int shipX = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.X));
        int shipY = (int)Fixed.ToInt(world.GetValue(ship, SimComponents.Position, SimComponents.Y));
        var occupied = new HashSet<long>();
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            if (world.HasComponent(id, SimComponents.Aboard)
                || world.HasComponent(id, SimComponents.Garrisoned))
            {
                continue;
            }
            int x = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
            int y = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
            occupied.Add(CellKey(x, y));
        }
        var candidates = new List<(int X, int Y, int Distance)>();
        for (int offsetY = -UnloadRadius; offsetY <= UnloadRadius; offsetY++)
        {
            for (int offsetX = -UnloadRadius; offsetX <= UnloadRadius; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }
                int cellX = shipX + offsetX;
                int cellY = shipY + offsetY;
                if (!Traversal.CellPassable(grid, terrain, cellX, cellY, MovementDomain.Land))
                {
                    continue;
                }
                if (occupied.Contains(CellKey(cellX, cellY)))
                {
                    continue;
                }
                candidates.Add((cellX, cellY, Math.Abs(offsetX) + Math.Abs(offsetY)));
            }
        }
        candidates.Sort((first, second) =>
        {
            int byDistance = first.Distance.CompareTo(second.Distance);
            if (byDistance != 0)
            {
                return byDistance;
            }
            int byX = first.X.CompareTo(second.X);
            return byX != 0 ? byX : first.Y.CompareTo(second.Y);
        });
        var cells = new List<(int X, int Y)>(Math.Min(limit, candidates.Count));
        for (int index = 0; index < candidates.Count && index < limit; index++)
        {
            cells.Add((candidates[index].X, candidates[index].Y));
        }
        return cells;
    }

    private static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;
}
