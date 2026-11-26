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

    /// <summary>
    /// 演示地图的高度层：与 NavGrid 完全独立。中央屏障、边界与障碍块抬升为崖壁，
    /// 右上角一块高原和几处土丘只影响渲染高度，不改变任何单位的可走路径。
    /// </summary>
    public static TerrainGrid BuildTerrain()
    {
        var terrain = new TerrainGrid(GridWidth, GridHeight);
        for (int y = 0; y < GridHeight; y++)
        {
            terrain.Set(0, y, TerrainGrid.TypeCliff, 1);
            terrain.Set(GridWidth - 1, y, TerrainGrid.TypeCliff, 1);
        }
        for (int x = 0; x < GridWidth; x++)
        {
            terrain.Set(x, 0, TerrainGrid.TypeCliff, 1);
            terrain.Set(x, GridHeight - 1, TerrainGrid.TypeCliff, 1);
        }
        for (int y = 3; y < 13; y++)
        {
            terrain.Set(16, y, TerrainGrid.TypeCliff, 2);
        }
        for (int y = 19; y < 29; y++)
        {
            terrain.Set(16, y, TerrainGrid.TypeCliff, 2);
        }
        SetRect(terrain, 22, 6, 3, 2, TerrainGrid.TypeCliff, 2);
        SetRect(terrain, 8, 24, 2, 4, TerrainGrid.TypeCliff, 2);
        SetRect(terrain, 20, 2, 7, 4, TerrainGrid.TypeGrass, 4);
        SetRect(terrain, 8, 2, 5, 4, TerrainGrid.TypeDirt, 1);
        SetRect(terrain, 27, 27, 3, 3, TerrainGrid.TypeWater, 0);
        return terrain;
    }

    private static void SetRect(
        TerrainGrid terrain, int x, int y, int width, int height, byte type, byte level)
    {
        for (int cellY = y; cellY < y + height; cellY++)
        {
            for (int cellX = x; cellX < x + width; cellX++)
            {
                terrain.Set(cellX, cellY, type, level);
            }
        }
    }

    public static MapData BuildMap() =>
        new("voxel_demo", Seed, BuildTerrain(), BuildGrid());

    public static GameSim Build(long seed = Seed)
    {
        var sim = new GameSim(BuildGrid(), seed, 2, 6, MatchConfig.Standard, BuildTerrain());
        SpawnForces(sim);
        IssueOrders(sim);
        SpawnEconomy(sim);
        SpawnBuildingDemo(sim);
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
                sim.SpawnUnit(teamBlueX, cellY, 0, UnitTypeFor(row, column));
                sim.SpawnUnit(teamRedX, cellY, 1, UnitTypeFor(row, column + 2));
            }
        }
    }

    /// <summary>四兵种轮转混编，偏好长枪兵/弓兵对位，便于目视验证克制。</summary>
    private static long UnitTypeFor(int row, int column) => (row + column) % 4 switch
    {
        0 => UnitDefs.Swordsman,
        1 => UnitDefs.Spearman,
        2 => UnitDefs.Archer,
        _ => UnitDefs.Knight,
    };

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

    /// <summary>一期建筑演示：兵营地基 + 村民施工，完整走放置→施工→完工闭环。</summary>
    private static void SpawnBuildingDemo(GameSim sim)
    {
        long building = sim.IssuePlaceBuilding(BuildingTypes.Barracks, 3, 3, 0);
        long builder = sim.SpawnVillager(8, 4, 0, 0.09, 20, 0.5);
        sim.IssueConstruct(builder, building);
    }
}
