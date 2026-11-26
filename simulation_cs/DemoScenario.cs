namespace AoeSim;

/// <summary>与旧 GDScript 演示一致的确定性场景，供本地局与 dedicated server 共用。</summary>
public static class DemoScenario
{
    public const int GridWidth = 32;
    public const int GridHeight = 32;
    public const long Seed = 20261126;

    public static NavGrid BuildGrid()
    {
        var grid = new NavGrid(GridWidth, GridHeight);
        for (int y = 0; y < GridHeight; y++)
        {
            grid.SetBlocked(0, y, true);
            grid.SetBlocked(GridWidth - 1, y, true);
        }
        for (int x = 0; x < GridWidth; x++)
        {
            grid.SetBlocked(x, 0, true);
            grid.SetBlocked(x, GridHeight - 1, true);
        }
        for (int y = 3; y < 13; y++)
        {
            grid.SetBlocked(16, y, true);
        }
        for (int y = 19; y < 29; y++)
        {
            grid.SetBlocked(16, y, true);
        }
        grid.SetBlockedRect(22, 6, 3, 2);
        grid.SetBlockedRect(8, 24, 2, 4);
        return grid;
    }

    public static GameSim BuildEmpty(long seed = Seed)
    {
        return new GameSim(BuildGrid(), seed, 2, 6, MatchConfig.Standard);
    }

    public static GameSim Build(long seed = Seed)
    {
        var sim = BuildEmpty(seed);
        SpawnForces(sim);
        IssueOrders(sim);
        SpawnEconomy(sim);
        return sim;
    }

    private static void SpawnForces(GameSim sim)
    {
        for (int row = 0; row < 9; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                int cellY = 7 + row * 2;
                int teamBlueX = 3 + column;
                int teamRedX = 28 - column;
                sim.SpawnUnit(teamBlueX, cellY, 0, 60, 8, 1.0, 1.0, 0.09);
                sim.SpawnUnit(teamRedX, cellY, 1, 60, 8, 1.0, 1.0, 0.09);
            }
        }
    }

    private static void IssueOrders(GameSim sim)
    {
        foreach (long id in sim.World.Entities())
        {
            long team = sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            if (team == 0)
            {
                sim.IssueMove(id, 27, 16);
            }
            else
            {
                sim.IssueMove(id, 4, 16);
            }
        }
    }

    private static void SpawnEconomy(GameSim sim)
    {
        long forest = sim.SpawnResourceNode(6, 6, GameSim.ResourceWood, 300);
        long townCenter = sim.SpawnDropSite(10, 10, 0b1111);
        for (int index = 0; index < 3; index++)
        {
            long villager = sim.SpawnVillager(6 + index, 8, 0, 0.09, 20, 0.5);
            sim.IssueGather(villager, forest, townCenter);
        }
    }
}
