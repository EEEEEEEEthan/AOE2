using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AoeSim;

/// <summary>地图上的一处资源点：类型沿用 GameSim 的资源常量，坐标是格子中心所在格。</summary>
public sealed class ResourcePoint
{
    public ResourcePoint(long type, int x, int y, long amount)
    {
        Type = type;
        X = x;
        Y = y;
        Amount = amount;
    }

    public long Type { get; }
    public int X { get; }
    public int Y { get; }
    public long Amount { get; }
}

/// <summary>一个玩家的起始位置：X/Y 是城镇中心 4×4 占地的左上角格。</summary>
public sealed class PlayerStart
{
    public PlayerStart(int team, int x, int y)
    {
        Team = team;
        X = x;
        Y = y;
    }

    public int Team { get; }
    public int X { get; }
    public int Y { get; }
}

/// <summary>
/// 地图数据：把 TerrainGrid（高度/类型）与 NavGrid（建筑与障碍占位）打包成可存读的格式，
/// 并附带生成器产出、由起始布置系统消费的资源点与玩家起始位置。
/// JSON 里 terrain 的 types / heights / blocked 都是按行优先展开的扁平数组，
/// 一份地图既有渲染用的高度，也有模拟用的可走性，但两个网格仍然互相独立。
/// placements 是出生点/资源点/建筑等编辑器放置元数据，缺省为空且向后兼容。
/// </summary>
public sealed class MapData
{
    public string Name { get; private set; }
    public long Seed { get; private set; }
    public TerrainGrid Terrain { get; }
    public NavGrid Nav { get; }
    public IReadOnlyList<MapPlacement> Placements => _placements;
    public IReadOnlyList<ResourcePoint> Resources { get; private set; }
    public IReadOnlyList<PlayerStart> Starts { get; private set; }

    public int Width => Terrain.Width;
    public int Height => Terrain.Height;

    private readonly List<MapPlacement> _placements;

    public MapData(
        string name,
        long seed,
        TerrainGrid terrain,
        NavGrid nav,
        IEnumerable<MapPlacement>? placements = null,
        IReadOnlyList<ResourcePoint>? resources = null,
        IReadOnlyList<PlayerStart>? starts = null)
    {
        if (terrain.Width != nav.Width || terrain.Height != nav.Height)
        {
            throw new ArgumentException("terrain and nav dimensions must match");
        }
        Name = name;
        Seed = seed;
        Terrain = terrain;
        Nav = nav;
        _placements = placements == null
            ? new List<MapPlacement>()
            : new List<MapPlacement>(placements);
        Resources = resources ?? Array.Empty<ResourcePoint>();
        Starts = starts ?? Array.Empty<PlayerStart>();
    }

    public string ToJson()
    {
        int cells = Width * Height;
        var types = new int[cells];
        var heights = new int[cells];
        var blocked = new int[cells];
        for (int index = 0; index < cells; index++)
        {
            types[index] = Terrain.Types[index];
            heights[index] = Terrain.Heights[index];
            blocked[index] = Nav.Blocked[index] == NavGrid.CostBlocked ? 1 : 0;
        }
        var resources = new ResourceDto[Resources.Count];
        for (int index = 0; index < Resources.Count; index++)
        {
            ResourcePoint point = Resources[index];
            resources[index] = new ResourceDto
            {
                Type = point.Type,
                X = point.X,
                Y = point.Y,
                Amount = point.Amount,
            };
        }
        var starts = new StartDto[Starts.Count];
        for (int index = 0; index < Starts.Count; index++)
        {
            PlayerStart start = Starts[index];
            starts[index] = new StartDto { Team = start.Team, X = start.X, Y = start.Y };
        }
        var dto = new MapDto
        {
            Name = Name,
            Seed = Seed,
            Width = Width,
            Height = Height,
            Types = types,
            Heights = heights,
            Blocked = blocked,
            Placements = _placements.ConvertAll(placement => placement.Clone()).ToArray(),
            Resources = resources,
            Starts = starts,
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static MapData FromJson(string json)
    {
        MapDto? dto = JsonSerializer.Deserialize<MapDto>(json, JsonOptions);
        if (dto == null || dto.Width <= 0 || dto.Height <= 0)
        {
            throw new ArgumentException("invalid map json");
        }
        int cells = dto.Width * dto.Height;
        if (dto.Types.Length != cells || dto.Heights.Length != cells)
        {
            throw new ArgumentException("terrain array size mismatch");
        }
        var terrain = new TerrainGrid(dto.Width, dto.Height);
        var nav = new NavGrid(dto.Width, dto.Height);
        for (int index = 0; index < cells; index++)
        {
            terrain.Types[index] = (byte)dto.Types[index];
            terrain.Heights[index] = (byte)dto.Heights[index];
            if (dto.Blocked.Length == cells)
            {
                nav.Blocked[index] = dto.Blocked[index] != 0 ? NavGrid.CostBlocked : (byte)0;
            }
        }
        var placements = new List<MapPlacement>();
        foreach (MapPlacement placement in dto.Placements ?? Array.Empty<MapPlacement>())
        {
            placements.Add(placement.Clone());
        }
        var resources = new List<ResourcePoint>();
        if (dto.Resources != null)
        {
            foreach (ResourceDto point in dto.Resources)
            {
                resources.Add(new ResourcePoint(point.Type, point.X, point.Y, point.Amount));
            }
        }
        var starts = new List<PlayerStart>();
        if (dto.Starts != null)
        {
            foreach (StartDto start in dto.Starts)
            {
                starts.Add(new PlayerStart(start.Team, start.X, start.Y));
            }
        }
        return new MapData(dto.Name, dto.Seed, terrain, nav, placements, resources, starts);
    }

    /// <summary>种子与最终布局的确定性摘要：同种子同地图必须逐项一致。</summary>
    public long Checksum()
    {
        long hash = SimWorld.FnvOffset;
        hash = SimWorld.HashText(hash, Name);
        hash = SimWorld.HashInt(hash, Seed);
        hash = SimWorld.HashInt(hash, Width);
        hash = SimWorld.HashInt(hash, Height);
        hash = SimWorld.HashInt(hash, Terrain.Checksum());
        foreach (byte value in Nav.Blocked)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        foreach (MapPlacement placement in _placements)
        {
            hash = SimWorld.HashText(hash, placement.Kind);
            hash = SimWorld.HashInt(hash, placement.X);
            hash = SimWorld.HashInt(hash, placement.Y);
            hash = SimWorld.HashInt(hash, placement.Team);
            hash = SimWorld.HashInt(hash, placement.NodeType);
            hash = SimWorld.HashInt(hash, placement.Amount);
        }
        foreach (ResourcePoint point in Resources)
        {
            hash = SimWorld.HashInt(hash, point.Type);
            hash = SimWorld.HashInt(hash, point.X);
            hash = SimWorld.HashInt(hash, point.Y);
            hash = SimWorld.HashInt(hash, point.Amount);
        }
        foreach (PlayerStart start in Starts)
        {
            hash = SimWorld.HashInt(hash, start.Team);
            hash = SimWorld.HashInt(hash, start.X);
            hash = SimWorld.HashInt(hash, start.Y);
        }
        return hash;
    }

    public static MapData Load(string path) => FromJson(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToJson());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class MapDto
    {
        public string Name { get; set; } = "";
        public long Seed { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int[] Types { get; set; } = Array.Empty<int>();
        public int[] Heights { get; set; } = Array.Empty<int>();
        public int[] Blocked { get; set; } = Array.Empty<int>();
        public MapPlacement[]? Placements { get; set; } = Array.Empty<MapPlacement>();
        public ResourceDto[]? Resources { get; set; }
        public StartDto[]? Starts { get; set; }
    }

    private sealed class ResourceDto
    {
        public long Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public long Amount { get; set; }
    }

    private sealed class StartDto
    {
        public int Team { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
