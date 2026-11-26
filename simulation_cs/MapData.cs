using System;
using System.IO;
using System.Text.Json;

namespace AoeSim;

/// <summary>
/// 地图数据：把 TerrainGrid（高度/类型）与 NavGrid（建筑与障碍占位）打包成可存读的格式。
/// JSON 里 terrain.types / terrain.heights / blocked 都是按行优先展开的扁平数组，
/// 一份地图既有渲染用的高度，也有模拟用的可走性，但两个网格仍然互相独立。
/// </summary>
public sealed class MapData
{
    public string Name { get; private set; }
    public long Seed { get; private set; }
    public TerrainGrid Terrain { get; }
    public NavGrid Nav { get; }

    public int Width => Terrain.Width;
    public int Height => Terrain.Height;

    public MapData(string name, long seed, TerrainGrid terrain, NavGrid nav)
    {
        if (terrain.Width != nav.Width || terrain.Height != nav.Height)
        {
            throw new ArgumentException("terrain and nav dimensions must match");
        }
        Name = name;
        Seed = seed;
        Terrain = terrain;
        Nav = nav;
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
        return new MapData(dto.Name, dto.Seed, terrain, nav);
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
    }
}
