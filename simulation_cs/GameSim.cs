using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 模拟门面：只保留核心状态、构造与本局玩家文明等编排入口。
/// 生成、经济、指令、循环与流场分别落在同名 partial 文件，新功能应落到对应职责文件或既有 System。
/// </summary>
public sealed partial class GameSim
{
    public const long ResourceWood = 0;
    public const long ResourceFood = 1;
    public const long ResourceGold = 2;
    public const long ResourceStone = 3;

    /// <summary>中立野生动物队伍：不与任何玩家开战，也不会被自动索敌。</summary>
    public const long GaiaTeam = -1;

    public const double DefaultSightTiles = 4.0;
    public const int DefaultFieldCacheCapacity = 128;

    public SimWorld World { get; }
    public NavGrid Grid { get; }
    public TerrainGrid Terrain { get; }
    public SpatialHash Spatial { get; }
    public OccupancyMap Occupancy { get; }
    public Lockstep Lockstep { get; }
    public MatchState? Match { get; }
    public List<VisionGrid> Vision { get; } = new();

    private readonly long _sightRadius = Fixed.FromFloat(DefaultSightTiles);
    private readonly FlowFieldCache _fieldCache;
    private readonly PlayerEntityIndex _inventoryIndex = new();
    private readonly PlayerEntityIndex _progressIndex = new();
    private readonly long[] _civIds;
    private bool _spatialReady;

    /// <summary>当前流场缓存中的条目数，供性能测试与监控断言。</summary>
    public int FieldCacheCount => _fieldCache.Count;

    public int FieldCacheCapacity => _fieldCache.Capacity;

    /// <summary>本 tick 新建的流场数；每次 SimulateTick 开始时清零，不进校验和。</summary>
    public int FieldsCreatedThisTick => _fieldCache.CreatedThisTick;

    public long FieldsCreatedTotal => _fieldCache.CreatedTotal;

    public GameSim(
        NavGrid grid,
        long seed = 1,
        int playerCount = 2,
        int ticksPerTurn = 6,
        MatchConfig? matchConfig = null,
        TerrainGrid? terrain = null,
        int fieldCacheCapacity = DefaultFieldCacheCapacity,
        long[]? civIds = null)
    {
        Grid = grid;
        Terrain = terrain ?? TerrainGrid.Flat(grid.Width, grid.Height);
        if (Terrain.Width != grid.Width || Terrain.Height != grid.Height)
        {
            throw new System.ArgumentException("terrain dimensions must match nav grid");
        }
        _fieldCache = new FlowFieldCache(Grid, Terrain, fieldCacheCapacity);
        World = new SimWorld(seed);
        Spatial = new SpatialHash(4);
        Occupancy = new OccupancyMap(grid.Width, grid.Height);
        Lockstep = new Lockstep(playerCount, ticksPerTurn);
        _civIds = new long[playerCount];
        for (int player = 0; player < playerCount; player++)
        {
            Vision.Add(new VisionGrid(grid.Width, grid.Height));
            long civ = civIds != null && player < civIds.Length
                ? civIds[player]
                : CivilizationCatalog.Generic;
            _civIds[player] = CivilizationCatalog.IsValid(civ) ? civ : CivilizationCatalog.Generic;
        }
        if (matchConfig != null)
        {
            Match = MatchState.Create(World, Lockstep.PlayerCount, matchConfig);
        }
        if (civIds != null)
        {
            for (int player = 0; player < Lockstep.PlayerCount; player++)
            {
                Progress(player);
            }
        }
    }

    /// <summary>某玩家本局所选文明；越界返回通用文明。</summary>
    public long Civ(long team) =>
        team >= 0 && team < _civIds.Length ? _civIds[team] : CivilizationCatalog.Generic;
}
