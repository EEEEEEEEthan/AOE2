using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AoeSim;

/// <summary>
/// 地图数据：把 TerrainGrid（高度/类型）与 NavGrid（建筑与障碍占位）打包成可存读的格式。
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

    public int Width => Terrain.Width;
    public int Height => Terrain.Height;

    private readonly List<MapPlacement> _placements;

    public MapData(
        string name,
        long seed,
        TerrainGrid terrain,
        NavGrid nav,
        IEnumerable<MapPlacement>? placements = null)
    {
        if (terrain.Width != nav.Width || terrain.Height != nav.Height)
        {
            throw new ArgumentException("terrain and nav dimensions must match");
        }
        Name = name;
        Seed = seed;
        Terrain = terrain;
        Nav = nav;
        _placements = placements == null ? new List<MapPlacement>() : new List<MapPlacement>(placements);
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
        return new MapData(dto.Name, dto.Seed, terrain, nav, placements);
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
    }
}
