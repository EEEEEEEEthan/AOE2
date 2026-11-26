namespace AoeSim;

/// <summary>
/// 时代/科技研究：开始时校验前置与资源并扣费，随后按 tick 推进，完成时才写入年龄或研究位；
/// 取消或承载建筑被毁/转废墟时返还已付资源。同刻每玩家只允许一项研究。
/// </summary>
public static class ResearchSystem
{
    public const long KindNone = 0;
    public const long KindAge = 1;
    public const long KindTech = 2;

    public static long ProgressEntity(SimWorld world, long team) =>
        PlayerProgress.FindEntity(world, team);

    public static long ActiveKind(SimWorld world, long team)
    {
        long entity = ProgressEntity(world, team);
        return entity < 0 ? KindNone : Read(world, entity, SimComponents.ResearchKind);
    }

    public static long ActiveTarget(SimWorld world, long team)
    {
        long entity = ProgressEntity(world, team);
        return entity < 0 ? -1 : Read(world, entity, SimComponents.ResearchTarget);
    }

    public static bool TryStartAge(GameSim sim, long team, long targetAge)
    {
        long entity = sim.Progress(team).Entity;
        SimWorld world = sim.World;
        if (Read(world, entity, SimComponents.ResearchKind) != KindNone)
        {
            return false;
        }
        long requiredBuildings = TechCatalog.RequiredBuildings(targetAge);
        if (requiredBuildings > 0
            && CompletedBuildingsOfAge(sim, team, targetAge - 1) < requiredBuildings)
        {
            return false;
        }
        long food = TechCatalog.AgeFoodCostOf(targetAge);
        long gold = TechCatalog.AgeGoldCostOf(targetAge);
        if (!Pay(sim, team, food, gold))
        {
            return false;
        }
        Begin(
            world,
            entity,
            KindAge,
            targetAge,
            TechCatalog.AgeResearchTicksFor(targetAge),
            food,
            gold,
            FindCompletedBuilding(sim, team, BuildingTypes.TownCenter));
        return true;
    }

    public static bool TryStartTech(GameSim sim, long team, long techId)
    {
        long entity = sim.Progress(team).Entity;
        SimWorld world = sim.World;
        if (Read(world, entity, SimComponents.ResearchKind) != KindNone)
        {
            return false;
        }
        if (TechCatalog.IsResearched(sim.PlayerResearch(team), techId)
            || !TechCatalog.IsAvailable(sim.Civ(team), techId)
            || sim.PlayerAge(team) < TechCatalog.RequiredAge(techId))
        {
            return false;
        }
        long building = FindCompletedBuilding(sim, team, TechCatalog.RequiredBuilding(techId));
        if (building < 0)
        {
            return false;
        }
        long food = TechCatalog.TechFoodCostOf(techId);
        long gold = TechCatalog.TechGoldCostOf(techId);
        if (!Pay(sim, team, food, gold))
        {
            return false;
        }
        Begin(
            world,
            entity,
            KindTech,
            techId,
            TechCatalog.TechResearchTicksFor(techId),
            food,
            gold,
            building);
        return true;
    }

    public static bool Cancel(GameSim sim, long team)
    {
        SimWorld world = sim.World;
        long entity = ProgressEntity(world, team);
        if (entity < 0 || Read(world, entity, SimComponents.ResearchKind) == KindNone)
        {
            return false;
        }
        Refund(sim, entity);
        Clear(world, entity);
        return true;
    }

    public static void Step(GameSim sim)
    {
        SimWorld world = sim.World;
        ComponentStore store = world.Store(SimComponents.PlayerProgress);
        if (store.Count == 0)
        {
            return;
        }
        foreach (long entity in store.ActiveIds())
        {
            if (!world.IsAlive(entity))
            {
                continue;
            }
            long kind = Read(world, entity, SimComponents.ResearchKind);
            if (kind == KindNone)
            {
                continue;
            }
            long building = Read(world, entity, SimComponents.ResearchBuilding);
            if (building >= 0 && !IsCompleteBuilding(world, building))
            {
                Refund(sim, entity);
                Clear(world, entity);
                continue;
            }
            long progress = Read(world, entity, SimComponents.ResearchProgress) + 1;
            long maxProgress = Read(world, entity, SimComponents.ResearchMaxProgress);
            if (progress < maxProgress)
            {
                world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchProgress, progress);
                continue;
            }
            Complete(sim, entity, kind);
        }
    }

    private static void Complete(GameSim sim, long entity, long kind)
    {
        SimWorld world = sim.World;
        long team = world.GetValue(entity, SimComponents.Team, SimComponents.TeamId);
        long target = Read(world, entity, SimComponents.ResearchTarget);
        long oldAge = world.GetValue(entity, SimComponents.PlayerProgress, SimComponents.Age);
        long oldResearch = world.GetValue(entity, SimComponents.PlayerProgress, SimComponents.Research);
        long newAge = oldAge;
        long newResearch = oldResearch;
        if (kind == KindAge)
        {
            newAge = target;
            world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.Age, newAge);
        }
        else
        {
            newResearch = oldResearch | TechCatalog.ResearchBit(target);
            world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.Research, newResearch);
        }
        Clear(world, entity);
        sim.ApplyProgressDelta(team, oldAge, oldResearch, newAge, newResearch);
    }

    private static void Begin(
        SimWorld world,
        long entity,
        long kind,
        long target,
        long maxProgress,
        long paidFood,
        long paidGold,
        long building)
    {
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchKind, kind);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchTarget, target);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchProgress, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchMaxProgress, maxProgress);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchPaidFood, paidFood);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchPaidGold, paidGold);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchBuilding, building);
    }

    private static void Clear(SimWorld world, long entity)
    {
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchKind, KindNone);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchTarget, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchProgress, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchMaxProgress, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchPaidFood, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchPaidGold, 0);
        world.SetValue(entity, SimComponents.PlayerProgress, SimComponents.ResearchBuilding, -1);
    }

    private static bool Pay(GameSim sim, long team, long food, long gold)
    {
        if (sim.Resource(team, GameSim.ResourceFood) < food
            || sim.Resource(team, GameSim.ResourceGold) < gold)
        {
            return false;
        }
        sim.AddResource(team, GameSim.ResourceFood, -food);
        sim.AddResource(team, GameSim.ResourceGold, -gold);
        return true;
    }

    private static void Refund(GameSim sim, long entity)
    {
        SimWorld world = sim.World;
        long team = world.GetValue(entity, SimComponents.Team, SimComponents.TeamId);
        long paidFood = Read(world, entity, SimComponents.ResearchPaidFood);
        long paidGold = Read(world, entity, SimComponents.ResearchPaidGold);
        sim.AddResource(team, GameSim.ResourceFood, paidFood);
        sim.AddResource(team, GameSim.ResourceGold, paidGold);
    }

    private static long CompletedBuildingsOfAge(GameSim sim, long team, long age)
    {
        SimWorld world = sim.World;
        ComponentStore store = world.Store(SimComponents.Building);
        long count = 0;
        foreach (long id in store.ActiveIds())
        {
            if (!IsCompleteBuilding(world, id)
                || world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (BuildingTypes.AgeOf(world.GetValue(id, SimComponents.Building, SimComponents.BuildingType)) == age)
            {
                count += 1;
            }
        }
        return count;
    }

    private static long FindCompletedBuilding(GameSim sim, long team, long type)
    {
        SimWorld world = sim.World;
        ComponentStore store = world.Store(SimComponents.Building);
        foreach (long id in store.ActiveIds())
        {
            if (!IsCompleteBuilding(world, id)
                || world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) == type)
            {
                return id;
            }
        }
        return -1;
    }

    private static bool IsCompleteBuilding(SimWorld world, long id) =>
        world.IsAlive(id)
        && world.HasComponent(id, SimComponents.Building)
        && world.GetValue(id, SimComponents.Building, SimComponents.State) == BuildingSystem.StateComplete;

    private static long Read(SimWorld world, long entity, string column) =>
        world.GetValue(entity, SimComponents.PlayerProgress, column);
}
