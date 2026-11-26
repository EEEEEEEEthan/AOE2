namespace AoeSim;

/// <summary>
/// 村民修理：贴近受损友方建筑后按 tick 扣木材回血，满血或木材不足即停手。
/// 与施工共用邻接寻路与贴近判定，遍历按实体 id 升序，保证确定性。
/// </summary>
public static class RepairSystem
{
    public const long StateActive = 0;
    public const long HpPerTick = 10;
    public const long WoodPerTick = 1;

    public static void Step(SimWorld world, NavGrid grid, TerrainGrid terrain, GameSim sim)
    {
        ComponentStore repairStore = world.Store(SimComponents.Repair);
        if (repairStore.Count == 0)
        {
            return;
        }
        foreach (long id in repairStore.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            StepWorker(world, grid, terrain, sim, id);
        }
    }

    /// <summary>目标是活着的、已完工的、受损的友方建筑；废墟、地基与满血建筑都不可修。</summary>
    public static bool CanRepair(SimWorld world, long villager, long building)
    {
        if (!world.IsAlive(building) || !world.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        if (!world.HasComponent(building, SimComponents.Health)
            || !world.HasComponent(building, SimComponents.Position))
        {
            return false;
        }
        if (world.GetValue(building, SimComponents.Building, SimComponents.State)
            != BuildingSystem.StateComplete)
        {
            return false;
        }
        if (world.GetValue(building, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(villager, SimComponents.Team, SimComponents.TeamId))
        {
            return false;
        }
        long hp = world.GetValue(building, SimComponents.Health, SimComponents.Hp);
        long maxHp = world.GetValue(building, SimComponents.Health, SimComponents.MaxHp);
        return hp < maxHp;
    }

    private static void StepWorker(
        SimWorld world, NavGrid grid, TerrainGrid terrain, GameSim sim, long id)
    {
        long buildingId = world.GetValue(id, SimComponents.Repair, SimComponents.TargetBase);
        if (!CanRepair(world, id, buildingId))
        {
            world.Detach(id, SimComponents.Repair);
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
        if (BuildingSystem.IsAdjacent(workerX, workerY, originX, originY, sizeWidth, sizeHeight))
        {
            BuildingSystem.StopWorker(world, id);
            Advance(world, sim, id, buildingId);
            return;
        }
        BuildingSystem.Approach(world, grid, terrain, id, workerX, workerY, originX, originY, sizeWidth, sizeHeight);
    }

    private static void Advance(SimWorld world, GameSim sim, long id, long buildingId)
    {
        long hp = world.GetValue(buildingId, SimComponents.Health, SimComponents.Hp);
        long maxHp = world.GetValue(buildingId, SimComponents.Health, SimComponents.MaxHp);
        long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
        if (sim.Resource(team, GameSim.ResourceWood) < WoodPerTick)
        {
            return;
        }
        sim.AddResource(team, GameSim.ResourceWood, -WoodPerTick);
        long healed = System.Math.Min(HpPerTick, maxHp - hp);
        world.SetValue(buildingId, SimComponents.Health, SimComponents.Hp, hp + healed);
        if (hp + healed >= maxHp)
        {
            world.Detach(id, SimComponents.Repair);
        }
    }
}
