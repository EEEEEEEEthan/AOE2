using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 建筑驻军与城镇警钟：单位进入城镇中心/塔楼后移出世界占位（摘掉 Position 等运行时组件），
/// 建筑按驻军数量与构成提升攻击；城镇警钟一键召集本玩家村民进最近的城镇中心，解除时按原工作复原。
/// 全部结算只用整数与实体 id 升序，随世界校验和同步。
/// </summary>
public static class GarrisonSystem
{
    public const long TownBellOff = 0;
    public const long TownBellSummoned = 1;

    /// <summary>完工时为可驻军建筑挂上攻击所需的 Combat 与驻军账本；重复调用只补齐缺失列。</summary>
    public static void EnsureBuildingCombat(SimWorld world, long building)
    {
        if (!world.IsAlive(building) || !world.HasComponent(building, SimComponents.Building))
        {
            return;
        }
        long type = world.GetValue(building, SimComponents.Building, SimComponents.BuildingType);
        if (!BuildingTypes.IsGarrisonable(type))
        {
            return;
        }
        if (!world.HasComponent(building, SimComponents.Combat))
        {
            world.Attach(building, SimComponents.Combat);
            world.SetValue(building, SimComponents.Combat, SimComponents.Damage, BuildingTypes.AttackDamage(type));
            world.SetValue(building, SimComponents.Combat, SimComponents.RangeMin, 0);
            world.SetValue(building, SimComponents.Combat, SimComponents.RangeMax, BuildingTypes.AttackRange(type));
            world.SetValue(building, SimComponents.Combat, SimComponents.Cooldown, BuildingTypes.AttackCooldown(type));
            world.SetValue(building, SimComponents.Combat, SimComponents.CooldownLeft, 0);
            world.SetValue(building, SimComponents.Combat, SimComponents.Stance, CombatStance.StandGround);
            world.SetValue(building, SimComponents.Combat, SimComponents.UnitType, UnitDefs.Swordsman);
            world.SetValue(building, SimComponents.Combat, SimComponents.AttackClass, UnitAttackClass.Pierce);
            world.SetValue(building, SimComponents.Combat, SimComponents.ArmorClass, UnitArmorClass.Building);
            world.SetValue(building, SimComponents.Combat, SimComponents.IsRanged, 1);
        }
        if (!world.HasComponent(building, SimComponents.Garrison))
        {
            world.Attach(building, SimComponents.Garrison);
            world.SetValue(building, SimComponents.Garrison, SimComponents.Count, 0);
            world.SetValue(building, SimComponents.Garrison, SimComponents.Capacity, BuildingTypes.GarrisonCapacity(type));
            world.SetValue(building, SimComponents.Garrison, SimComponents.BaseDamage, BuildingTypes.AttackDamage(type));
            world.SetValue(building, SimComponents.Garrison, SimComponents.BaseCooldown, BuildingTypes.AttackCooldown(type));
            world.SetValue(building, SimComponents.Garrison, SimComponents.BonusDamage, 0);
        }
    }

    public static long GarrisonCount(SimWorld world, long building) =>
        world.HasComponent(building, SimComponents.Garrison)
            ? world.GetValue(building, SimComponents.Garrison, SimComponents.Count)
            : 0;

    public static bool CanGarrison(SimWorld world, long unit, long building)
    {
        if (!world.IsAlive(unit) || !world.HasComponent(unit, SimComponents.Position)
            || !world.HasComponent(unit, SimComponents.Move)
            || world.HasComponent(unit, SimComponents.Garrisoned))
        {
            return false;
        }
        if (!world.IsAlive(building) || !world.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        long type = world.GetValue(building, SimComponents.Building, SimComponents.BuildingType);
        if (!BuildingTypes.IsGarrisonable(type))
        {
            return false;
        }
        if (world.GetValue(building, SimComponents.Building, SimComponents.State) != BuildingSystem.StateComplete)
        {
            return false;
        }
        if (world.HasComponent(unit, SimComponents.Building)
            || world.HasComponent(unit, SimComponents.ResourceNode)
            || world.HasComponent(unit, SimComponents.Monk))
        {
            return false;
        }
        if (world.GetValue(unit, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(building, SimComponents.Team, SimComponents.TeamId))
        {
            return false;
        }
        EnsureBuildingCombat(world, building);
        long count = world.GetValue(building, SimComponents.Garrison, SimComponents.Count);
        long capacity = world.GetValue(building, SimComponents.Garrison, SimComponents.Capacity);
        return count < capacity;
    }

    /// <summary>把单位收入建筑：记录其贡献与是否在采集，随后摘掉占位/移动/指令/采集组件，从世界与选择集中隐藏。</summary>
    public static bool TryGarrison(SimWorld world, long unit, long building, bool townBell)
    {
        if (!CanGarrison(world, unit, building))
        {
            return false;
        }
        long contribution = ContributionOf(world, unit);
        world.Attach(unit, SimComponents.Garrisoned);
        world.SetValue(unit, SimComponents.Garrisoned, SimComponents.TargetEntity, building);
        world.SetValue(unit, SimComponents.Garrisoned, SimComponents.TownBell,
            townBell ? TownBellSummoned : TownBellOff);
        world.SetValue(unit, SimComponents.Garrisoned, SimComponents.Contribution, contribution);
        world.SetValue(unit, SimComponents.Garrisoned, SimComponents.HadGather,
            world.HasComponent(unit, SimComponents.Gather) ? 1 : 0);

        long count = world.GetValue(building, SimComponents.Garrison, SimComponents.Count) + 1;
        world.SetValue(building, SimComponents.Garrison, SimComponents.Count, count);
        long bonus = world.GetValue(building, SimComponents.Garrison, SimComponents.BonusDamage) + contribution;
        world.SetValue(building, SimComponents.Garrison, SimComponents.BonusDamage, bonus);

        if (world.HasComponent(unit, SimComponents.Move))
        {
            world.SetValue(unit, SimComponents.Move, SimComponents.Active, 0);
        }
        world.Detach(unit, SimComponents.Position);
        world.Detach(unit, SimComponents.Move);
        world.Detach(unit, SimComponents.Velocity);
        world.Detach(unit, SimComponents.Footprint);
        world.Detach(unit, SimComponents.Order);
        world.Detach(unit, SimComponents.Gather);
        world.Detach(unit, SimComponents.AttackTarget);
        world.Detach(unit, SimComponents.Engage);
        return true;
    }

    /// <summary>解除单个单位驻军，在建筑周边就近落位；采集组件按进入前的记录决定是否复原。</summary>
    public static bool Release(SimWorld world, NavGrid grid, TerrainGrid terrain, long unit)
    {
        if (!world.IsAlive(unit) || !world.HasComponent(unit, SimComponents.Garrisoned))
        {
            return false;
        }
        long building = world.GetValue(unit, SimComponents.Garrisoned, SimComponents.TargetEntity);
        long contribution = world.GetValue(unit, SimComponents.Garrisoned, SimComponents.Contribution);
        bool hadGather = world.GetValue(unit, SimComponents.Garrisoned, SimComponents.HadGather) != 0;

        if (world.IsAlive(building) && world.HasComponent(building, SimComponents.Garrison))
        {
            long count = world.GetValue(building, SimComponents.Garrison, SimComponents.Count);
            long bonus = world.GetValue(building, SimComponents.Garrison, SimComponents.BonusDamage);
            world.SetValue(building, SimComponents.Garrison, SimComponents.Count, count > 0 ? count - 1 : 0);
            long next = bonus - contribution;
            world.SetValue(building, SimComponents.Garrison, SimComponents.BonusDamage, next > 0 ? next : 0);
        }

        int cellX = 0;
        int cellY = 0;
        if (world.IsAlive(building) && world.HasComponent(building, SimComponents.Position))
        {
            (cellX, cellY) = FindReleaseCell(world, grid, terrain, building);
        }
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
        world.Detach(unit, SimComponents.Garrisoned);
        return true;
    }

    /// <summary>建筑被摧毁时把其中的驻军全部放出，避免单位被锁在废墟里。</summary>
    public static int ReleaseAll(SimWorld world, NavGrid grid, TerrainGrid terrain, long building)
    {
        var units = new List<long>();
        ComponentStore store = world.Store(SimComponents.Garrisoned);
        foreach (long id in store.ActiveIds())
        {
            if (store.GetValue(id, SimComponents.TargetEntity) == building)
            {
                units.Add(id);
            }
        }
        int released = 0;
        foreach (long id in units)
        {
            if (Release(world, grid, terrain, id))
            {
                released += 1;
            }
        }
        return released;
    }

    /// <summary>城镇警钟召集：本玩家所有在野外采集的村民就近进入有容量的己方城镇中心，容量满者留在外面。</summary>
    public static int SummonTownBell(SimWorld world, NavGrid grid, TerrainGrid terrain, long team)
    {
        var centers = new List<long>();
        var counts = new List<long>();
        var capacities = new List<long>();
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.TownCenter)
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
            EnsureBuildingCombat(world, id);
            centers.Add(id);
            counts.Add(world.GetValue(id, SimComponents.Garrison, SimComponents.Count));
            capacities.Add(world.GetValue(id, SimComponents.Garrison, SimComponents.Capacity));
        }
        if (centers.Count == 0)
        {
            return 0;
        }

        var villagers = new List<long>();
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id)
                || !world.HasComponent(id, SimComponents.Position)
                || !world.HasComponent(id, SimComponents.Gather)
                || world.HasComponent(id, SimComponents.Garrisoned))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            villagers.Add(id);
        }

        int summoned = 0;
        foreach (long villager in villagers)
        {
            int best = -1;
            long bestDistance = 0;
            int villagerX = (int)Fixed.ToInt(world.GetValue(villager, SimComponents.Position, SimComponents.X));
            int villagerY = (int)Fixed.ToInt(world.GetValue(villager, SimComponents.Position, SimComponents.Y));
            for (int index = 0; index < centers.Count; index++)
            {
                if (counts[index] >= capacities[index])
                {
                    continue;
                }
                int centerX = (int)Fixed.ToInt(world.GetValue(centers[index], SimComponents.Position, SimComponents.X));
                int centerY = (int)Fixed.ToInt(world.GetValue(centers[index], SimComponents.Position, SimComponents.Y));
                long distance = System.Math.Abs(villagerX - centerX) + System.Math.Abs(villagerY - centerY);
                if (best < 0 || distance < bestDistance
                    || (distance == bestDistance && centers[index] < centers[best]))
                {
                    best = index;
                    bestDistance = distance;
                }
            }
            if (best < 0)
            {
                continue;
            }
            if (TryGarrison(world, villager, centers[best], true))
            {
                counts[best] += 1;
                summoned += 1;
            }
        }
        return summoned;
    }

    /// <summary>城镇警钟解除：只放出由警钟召集的村民，手动驻军的单位不受影响。</summary>
    public static int DismissTownBell(SimWorld world, NavGrid grid, TerrainGrid terrain, long team)
    {
        var units = new List<long>();
        ComponentStore store = world.Store(SimComponents.Garrisoned);
        foreach (long id in store.ActiveIds())
        {
            if (store.GetValue(id, SimComponents.TownBell) != TownBellSummoned)
            {
                continue;
            }
            if (!world.IsAlive(id)
                || world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            units.Add(id);
        }
        int dismissed = 0;
        foreach (long id in units)
        {
            if (Release(world, grid, terrain, id))
            {
                dismissed += 1;
            }
        }
        return dismissed;
    }

    /// <summary>每 tick 按驻军数量刷新建筑伤害与冷却；弓兵（远程）计入双倍伤害，人数越多射速越快。</summary>
    public static void Step(SimWorld world)
    {
        ComponentStore store = world.Store(SimComponents.Garrison);
        if (store.Count == 0)
        {
            return;
        }
        foreach (long id in store.ActiveIds())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Combat))
            {
                continue;
            }
            long baseDamage = store.GetValue(id, SimComponents.BaseDamage);
            long bonus = store.GetValue(id, SimComponents.BonusDamage);
            long baseCooldown = store.GetValue(id, SimComponents.BaseCooldown);
            long count = store.GetValue(id, SimComponents.Count);
            world.SetValue(id, SimComponents.Combat, SimComponents.Damage, baseDamage + bonus);
            long cooldown = baseCooldown - count / 5;
            if (cooldown < 2)
            {
                cooldown = 2;
            }
            world.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, cooldown);
        }
    }

    private static long ContributionOf(SimWorld world, long unit)
    {
        if (!world.HasComponent(unit, SimComponents.Combat))
        {
            return 1;
        }
        long ranged = world.GetValue(unit, SimComponents.Combat, SimComponents.IsRanged);
        long unitType = world.GetValue(unit, SimComponents.Combat, SimComponents.UnitType);
        return ranged != 0 || unitType == UnitDefs.Archer ? 2 : 1;
    }

    private static (int, int) FindReleaseCell(
        SimWorld world, NavGrid grid, TerrainGrid terrain, long building)
    {
        int originX = (int)Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.X));
        int originY = (int)Fixed.ToInt(world.GetValue(building, SimComponents.Position, SimComponents.Y));
        int sizeWidth = (int)world.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        int sizeHeight = (int)world.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        if (sizeWidth < 1)
        {
            sizeWidth = 1;
        }
        if (sizeHeight < 1)
        {
            sizeHeight = 1;
        }
        int centerX = originX + sizeWidth / 2;
        int centerY = originY + sizeHeight / 2;
        int bestX = -1;
        int bestY = -1;
        long bestDistance = 0;
        for (int cellY = originY - 1; cellY <= originY + sizeHeight; cellY++)
        {
            for (int cellX = originX - 1; cellX <= originX + sizeWidth; cellX++)
            {
                if (cellX >= originX && cellX < originX + sizeWidth
                    && cellY >= originY && cellY < originY + sizeHeight)
                {
                    continue;
                }
                if (!grid.InBounds(cellX, cellY) || grid.IsBlocked(cellX, cellY))
                {
                    continue;
                }
                byte terrainType = terrain.TypeAt(cellX, cellY);
                if (terrainType == TerrainGrid.TypeWater || terrainType == TerrainGrid.TypeCliff)
                {
                    continue;
                }
                long distance = System.Math.Abs(cellX - centerX) + System.Math.Abs(cellY - centerY);
                if (bestX < 0 || distance < bestDistance
                    || (distance == bestDistance
                        && (cellX < bestX || (cellX == bestX && cellY < bestY))))
                {
                    bestX = cellX;
                    bestY = cellY;
                    bestDistance = distance;
                }
            }
        }
        if (bestX < 0)
        {
            return (originX, originY);
        }
        return (bestX, bestY);
    }
}
