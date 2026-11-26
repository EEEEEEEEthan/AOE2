namespace AoeSim;

/// <summary>
/// 陆路贸易车：在市场之间往返，抵达下一座市场时按两座市场的曼哈顿距离与双方食物价差结算黄金。
/// 行驶复用建筑的邻接接近逻辑，遍历按实体 id 升序，保证确定性。
/// </summary>
public static class TradeSystem
{
    public const long GoldPerDistanceCell = 2;
    public const long PriceDivisor = 10;
    public const double CartSpeedTilesPerTick = 0.2;

    public static void Step(SimWorld world, NavGrid grid, TerrainGrid terrain, GameSim sim)
    {
        ComponentStore tradeStore = world.Store(SimComponents.Trade);
        if (tradeStore.Count == 0)
        {
            return;
        }
        foreach (long id in tradeStore.ActiveIds())
        {
            if (world.IsAlive(id))
            {
                StepCart(world, grid, terrain, sim, id);
            }
        }
    }

    public static bool IsCompletedMarket(SimWorld world, long id)
    {
        if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Building))
        {
            return false;
        }
        if (world.GetValue(id, SimComponents.Building, SimComponents.BuildingType) != BuildingTypes.Market)
        {
            return false;
        }
        return world.GetValue(id, SimComponents.Building, SimComponents.State) == BuildingSystem.StateComplete;
    }

    private static void StepCart(SimWorld world, NavGrid grid, TerrainGrid terrain, GameSim sim, long id)
    {
        long home = world.GetValue(id, SimComponents.Trade, SimComponents.TradeHome);
        long target = world.GetValue(id, SimComponents.Trade, SimComponents.TradeTarget);
        if (home < 0 || target < 0)
        {
            return;
        }
        if (!IsCompletedMarket(world, home) || !IsCompletedMarket(world, target) || home == target)
        {
            world.Detach(id, SimComponents.Trade);
            BuildingSystem.StopWorker(world, id);
            return;
        }
        if (!world.HasComponent(id, SimComponents.Position))
        {
            return;
        }
        int targetX = (int)Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.X));
        int targetY = (int)Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.Y));
        int sizeWidth = (int)world.GetValue(target, SimComponents.Building, SimComponents.SizeWidth);
        int sizeHeight = (int)world.GetValue(target, SimComponents.Building, SimComponents.SizeHeight);
        int cartX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        int cartY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
        if (BuildingSystem.IsAdjacent(cartX, cartY, targetX, targetY, sizeWidth, sizeHeight))
        {
            BuildingSystem.StopWorker(world, id);
            long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            long gold = TripGold(world, home, target);
            if (gold > 0)
            {
                sim.AddResource(team, GameSim.ResourceGold, gold);
            }
            world.SetValue(id, SimComponents.Trade, SimComponents.TradeHome, target);
            world.SetValue(id, SimComponents.Trade, SimComponents.TradeTarget, home);
            return;
        }
        BuildingSystem.Approach(world, grid, terrain, id, cartX, cartY, targetX, targetY, sizeWidth, sizeHeight);
    }

    private static long TripGold(SimWorld world, long first, long second)
    {
        int firstX = (int)Fixed.ToInt(world.GetValue(first, SimComponents.Position, SimComponents.X));
        int firstY = (int)Fixed.ToInt(world.GetValue(first, SimComponents.Position, SimComponents.Y));
        int secondX = (int)Fixed.ToInt(world.GetValue(second, SimComponents.Position, SimComponents.X));
        int secondY = (int)Fixed.ToInt(world.GetValue(second, SimComponents.Position, SimComponents.Y));
        long distance = System.Math.Abs(firstX - secondX) + System.Math.Abs(firstY - secondY);
        long firstTeam = world.GetValue(first, SimComponents.Team, SimComponents.TeamId);
        long secondTeam = world.GetValue(second, SimComponents.Team, SimComponents.TeamId);
        long priceGap = System.Math.Abs(
            MarketSystem.Rate(world, firstTeam, GameSim.ResourceFood)
            - MarketSystem.Rate(world, secondTeam, GameSim.ResourceFood));
        return distance * GoldPerDistanceCell + priceGap / PriceDivisor;
    }
}
