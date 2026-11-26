namespace AoeSim;

/// <summary>
/// 市场玩法：每玩家维护四类资源的整数基准价，买入价与卖出价按价差展开；
/// 每次成交按数量推动基准价，供需上下限封顶，全部为整数运算并随校验和同步。
/// </summary>
public static class MarketSystem
{
    public const long BaseRate = 100;
    public const long BuySpread = 25;
    public const long SellSpread = 25;
    public const long MinRate = 40;
    public const long MaxRate = 200;
    public const long ImpactPerHundred = 10;
    public const long RateScale = 100;
    public const long MaxTradeAmount = 10000;

    private static bool IsValidResource(long resourceKind) =>
        resourceKind >= 0 && resourceKind < SimComponents.ResourceColumns.Length;

    /// <summary>单次买卖/进贡的数量上限，挡住来自 LockstepCommand 的超大数量与溢出玩法。</summary>
    private static bool IsValidAmount(long amount) =>
        amount > 0 && amount <= MaxTradeAmount;

    /// <summary>先除后乘，避免 amount × rate 在边界改动后溢出；结果与原式整数截断一致。</summary>
    private static long ScaledAmount(long amount, long rate)
    {
        long quotient = amount / RateScale;
        long remainder = amount % RateScale;
        return quotient * rate + remainder * rate / RateScale;
    }

    public static bool HasCompletedMarket(SimWorld world, long team)
    {
        ComponentStore store = world.Store(SimComponents.Building);
        if (store.Count == 0)
        {
            return false;
        }
        foreach (long id in store.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.Market)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Building, SimComponents.State) != BuildingSystem.StateComplete)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                return true;
            }
        }
        return false;
    }

    public static long Rate(SimWorld world, long team, long resourceKind)
    {
        if (!IsValidResource(resourceKind)
            || !world.TryGetPlayerEntity(SimComponents.MarketState, team, out long entity))
        {
            return BaseRate;
        }
        return world.GetValue(entity, SimComponents.MarketState, SimComponents.RateColumns[(int)resourceKind]);
    }

    public static long BuyRate(SimWorld world, long team, long resourceKind) =>
        Rate(world, team, resourceKind) + BuySpread;

    public static long SellRate(SimWorld world, long team, long resourceKind) =>
        Rate(world, team, resourceKind) - SellSpread;

    public static long BuyCost(SimWorld world, long team, long resourceKind, long amount) =>
        ScaledAmount(amount, BuyRate(world, team, resourceKind));

    public static long SellRevenue(SimWorld world, long team, long resourceKind, long amount) =>
        ScaledAmount(amount, SellRate(world, team, resourceKind));

    /// <summary>买入：仅拥有完工市场的玩家可买，先扣金再入资源，成交后抬升基准价。</summary>
    public static bool Buy(GameSim sim, long team, long resourceKind, long amount)
    {
        if (!IsValidResource(resourceKind) || !IsValidAmount(amount))
        {
            return false;
        }
        if (team < 0 || team >= sim.Lockstep.PlayerCount)
        {
            return false;
        }
        if (!HasCompletedMarket(sim.World, team))
        {
            return false;
        }
        long cost = System.Math.Max(1, BuyCost(sim.World, team, resourceKind, amount));
        if (sim.Resource(team, GameSim.ResourceGold) < cost)
        {
            return false;
        }
        sim.AddResource(team, GameSim.ResourceGold, -cost);
        sim.AddResource(team, resourceKind, amount);
        ShiftRate(sim.World, team, resourceKind, amount);
        return true;
    }

    /// <summary>卖出：资源不足或数量折算不出金币时拒绝，避免白扣资源；成交后压低基准价。</summary>
    public static bool Sell(GameSim sim, long team, long resourceKind, long amount)
    {
        if (!IsValidResource(resourceKind) || !IsValidAmount(amount))
        {
            return false;
        }
        if (team < 0 || team >= sim.Lockstep.PlayerCount)
        {
            return false;
        }
        if (!HasCompletedMarket(sim.World, team))
        {
            return false;
        }
        if (sim.Resource(team, resourceKind) < amount)
        {
            return false;
        }
        long revenue = SellRevenue(sim.World, team, resourceKind, amount);
        if (revenue <= 0)
        {
            return false;
        }
        sim.AddResource(team, resourceKind, -amount);
        sim.AddResource(team, GameSim.ResourceGold, revenue);
        ShiftRate(sim.World, team, resourceKind, -amount);
        return true;
    }

    /// <summary>进贡：从发起方库存原子转入目标玩家，可向队友或对手输送四类资源。</summary>
    public static bool Tribute(GameSim sim, long from, long to, long resourceKind, long amount)
    {
        if (!IsValidResource(resourceKind) || !IsValidAmount(amount) || from == to)
        {
            return false;
        }
        if (from < 0 || from >= sim.Lockstep.PlayerCount || to < 0 || to >= sim.Lockstep.PlayerCount)
        {
            return false;
        }
        if (sim.Resource(from, resourceKind) < amount)
        {
            return false;
        }
        sim.AddResource(from, resourceKind, -amount);
        sim.AddResource(to, resourceKind, amount);
        return true;
    }

    private static void ShiftRate(SimWorld world, long team, long resourceKind, long signedAmount)
    {
        long delta = ScaledAmount(signedAmount, ImpactPerHundred);
        if (delta == 0)
        {
            return;
        }
        long entity = EnsureState(world, team);
        string column = SimComponents.RateColumns[(int)resourceKind];
        long rate = world.GetValue(entity, SimComponents.MarketState, column) + delta;
        rate = System.Math.Clamp(rate, MinRate, MaxRate);
        world.SetValue(entity, SimComponents.MarketState, column, rate);
    }

    private static long EnsureState(SimWorld world, long team)
    {
        if (world.TryGetPlayerEntity(SimComponents.MarketState, team, out long entity))
        {
            return entity;
        }
        entity = world.Spawn();
        world.Attach(entity, SimComponents.MarketState);
        world.Attach(entity, SimComponents.Team);
        world.SetValue(entity, SimComponents.Team, SimComponents.TeamId, team);
        foreach (string column in SimComponents.RateColumns)
        {
            world.SetValue(entity, SimComponents.MarketState, column, BaseRate);
        }
        world.RegisterPlayerEntity(SimComponents.MarketState, team, entity);
        return entity;
    }
}
