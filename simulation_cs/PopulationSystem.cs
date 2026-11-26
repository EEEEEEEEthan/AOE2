namespace AoeSim;

/// <summary>
/// 按玩家的人口账本。每个已配置上限的玩家拥有一条不带 POSITION 的 POPULATION 实体，
/// 所以寻路、战斗、视野与快照都会自然跳过它；单位用 POP 组件记下自身占用。
/// 没有账本的玩家视为无上限，这样旧场景与旧测试直接生成单位时行为不变。
/// </summary>
public static class PopulationSystem
{
    public const long DefaultCapMax = 200;

    public static long EnsurePlayer(SimWorld world, long team)
    {
        long existing = FindLedger(world, team);
        if (existing >= 0)
        {
            return existing;
        }
        long id = world.Spawn();
        world.Attach(id, SimComponents.Population);
        world.Attach(id, SimComponents.Team);
        world.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        world.SetValue(id, SimComponents.Population, SimComponents.Used, 0);
        world.SetValue(id, SimComponents.Population, SimComponents.Cap, 0);
        world.SetValue(id, SimComponents.Population, SimComponents.CapMax, DefaultCapMax);
        return id;
    }

    public static long FindLedger(SimWorld world, long team)
    {
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Population))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                return id;
            }
        }
        return -1;
    }

    public static bool HasLedger(SimWorld world, long team) => FindLedger(world, team) >= 0;

    public static long Used(SimWorld world, long team)
    {
        long id = FindLedger(world, team);
        return id < 0 ? 0 : world.GetValue(id, SimComponents.Population, SimComponents.Used);
    }

    public static long Cap(SimWorld world, long team)
    {
        long id = FindLedger(world, team);
        return id < 0 ? 0 : world.GetValue(id, SimComponents.Population, SimComponents.Cap);
    }

    public static long CapMax(SimWorld world, long team)
    {
        long id = FindLedger(world, team);
        return id < 0 ? 0 : world.GetValue(id, SimComponents.Population, SimComponents.CapMax);
    }

    /// <summary>累加人口上限并 clamp 到 CAP_MAX（原作封顶 200）；返回新的上限。</summary>
    public static long AddCap(SimWorld world, long team, long amount)
    {
        long id = EnsurePlayer(world, team);
        long cap = world.GetValue(id, SimComponents.Population, SimComponents.Cap) + amount;
        long capMax = world.GetValue(id, SimComponents.Population, SimComponents.CapMax);
        if (cap < 0)
        {
            cap = 0;
        }
        if (cap > capMax)
        {
            cap = capMax;
        }
        world.SetValue(id, SimComponents.Population, SimComponents.Cap, cap);
        return cap;
    }

    /// <summary>人口余量是否够生成：无账本的玩家不受限；cost &lt;= 0 恒可生成。</summary>
    public static bool CanSpawn(SimWorld world, long team, long cost)
    {
        if (cost <= 0)
        {
            return true;
        }
        long id = FindLedger(world, team);
        if (id < 0)
        {
            return true;
        }
        long used = world.GetValue(id, SimComponents.Population, SimComponents.Used);
        long cap = world.GetValue(id, SimComponents.Population, SimComponents.Cap);
        return used + cost <= cap;
    }

    public static void Reserve(SimWorld world, long team, long cost)
    {
        if (cost <= 0)
        {
            return;
        }
        long id = FindLedger(world, team);
        if (id < 0)
        {
            return;
        }
        long used = world.GetValue(id, SimComponents.Population, SimComponents.Used) + cost;
        world.SetValue(id, SimComponents.Population, SimComponents.Used, used);
    }

    /// <summary>在实体被 world.Despawn 之前调用，按 POP.COST 归还其占用。</summary>
    public static void Release(SimWorld world, long entity)
    {
        if (!world.HasComponent(entity, SimComponents.Pop))
        {
            return;
        }
        long cost = world.GetValue(entity, SimComponents.Pop, SimComponents.PopCost);
        long team = world.GetValue(entity, SimComponents.Team, SimComponents.TeamId);
        long id = FindLedger(world, team);
        if (id < 0)
        {
            return;
        }
        long used = world.GetValue(id, SimComponents.Population, SimComponents.Used) - cost;
        if (used < 0)
        {
            used = 0;
        }
        world.SetValue(id, SimComponents.Population, SimComponents.Used, used);
    }
}
