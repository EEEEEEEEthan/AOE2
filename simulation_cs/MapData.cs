using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AoeSim;

/// <summary>地图上的一处资源点：类型沿用 GameSim 的资源常量，坐标是格子中心所在格。</summary>
public sealed class ResourcePoint
{
    public ResourcePoint(long type, int x, int y, long amount, long subtype = 0)
    {
        Type = type;
        X = x;
        Y = y;
        Amount = amount;
        Subtype = subtype;
    }

    public long Type { get; }
    public int X { get; }
    public int Y { get; }
    public long Amount { get; }
    public long Subtype { get; }
}

/// <summary>地图上的一处野生动物出生点：Subtype 是 <see cref="AnimalSpecies"/> 物种。</summary>
public sealed class AnimalSpawn
{
    public AnimalSpawn(long species, int x, int y)
    {
        Species = species;
        X = x;
        Y = y;
    }

    public long Species { get; }
    public int X { get; }
    public int Y { get; }
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
/// JSON 里 types / blocked 是按行优先的逐格数组，vertexHeights 是 (宽 + 1) × (高 + 1)
/// 的共享顶点高度场；heights 为逐格最高角高度，用于兼容旧地图与外部读取。
/// 一份地图既有渲染用的顶点高度，也有模拟用的可走性，但两个网格仍然互相独立。
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
    public IReadOnlyList<AnimalSpawn> Animals { get; private set; }
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
        IReadOnlyList<PlayerStart>? starts = null,
        IReadOnlyList<AnimalSpawn>? animals = null)
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
        Animals = animals ?? Array.Empty<AnimalSpawn>();
        Starts = starts ?? Array.Empty<PlayerStart>();
    }

    public string ToJson()
    {
        int cells = Width * Height;
        int vertexCells = Terrain.VertexWidth * Terrain.VertexHeight;
        var types = new int[cells];
        var heights = new int[cells];
        var vertexHeights = new int[vertexCells];
        var blocked = new int[cells];
        for (int index = 0; index < cells; index++)
        {
            types[index] = Terrain.Types[index];
            heights[index] = Terrain.HeightAt(index % Width, index / Width);
            blocked[index] = Nav.Blocked[index] == NavGrid.CostBlocked ? 1 : 0;
        }
        for (int index = 0; index < vertexCells; index++)
        {
            vertexHeights[index] = Terrain.VertexHeights[index];
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
                Subtype = point.Subtype,
            };
        }
        var animals = new AnimalDto[Animals.Count];
        for (int index = 0; index < Animals.Count; index++)
        {
            AnimalSpawn animal = Animals[index];
            animals[index] = new AnimalDto
            {
                Species = animal.Species,
                X = animal.X,
                Y = animal.Y,
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
            VertexHeights = vertexHeights,
            Blocked = blocked,
            Placements = _placements.ConvertAll(placement => placement.Clone()).ToArray(),
            Resources = resources,
            Animals = animals,
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
        int vertexCells = (dto.Width + 1) * (dto.Height + 1);
        if (dto.Types.Length != cells)
        {
            throw new ArgumentException("terrain array size mismatch");
        }
        var terrain = new TerrainGrid(dto.Width, dto.Height);
        var nav = new NavGrid(dto.Width, dto.Height);
        if (dto.VertexHeights.Length == vertexCells)
        {
            for (int index = 0; index < vertexCells; index++)
            {
                terrain.VertexHeights[index] = (byte)dto.VertexHeights[index];
            }
        }
        else if (dto.Heights.Length == cells)
        {
            terrain.ExpandCellHeights(dto.Heights);
        }
        else
        {
            throw new ArgumentException("terrain height array size mismatch");
        }
        for (int index = 0; index < cells; index++)
        {
            terrain.Types[index] = (byte)dto.Types[index];
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
                resources.Add(new ResourcePoint(point.Type, point.X, point.Y, point.Amount, point.Subtype));
            }
        }
        var animals = new List<AnimalSpawn>();
        if (dto.Animals != null)
        {
            foreach (AnimalDto animal in dto.Animals)
            {
                animals.Add(new AnimalSpawn(animal.Species, animal.X, animal.Y));
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
        else if (dto.Resources == null && placements.Count > 0)
        {
            MapScenario scenario = MapScenarioBuilder.FromPlacements(placements, terrain);
            resources = scenario.Resources;
            starts = scenario.Starts;
            animals = scenario.Animals;
        }
        return new MapData(dto.Name, dto.Seed, terrain, nav, placements, resources, starts, animals);
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
            hash = SimWorld.HashInt(hash, placement.Subtype);
            hash = SimWorld.HashInt(hash, placement.Amount);
            hash = SimWorld.HashInt(hash, placement.BuildingType);
            hash = SimWorld.HashInt(hash, placement.UnitType);
        }
        foreach (ResourcePoint point in Resources)
        {
            hash = SimWorld.HashInt(hash, point.Type);
            hash = SimWorld.HashInt(hash, point.X);
            hash = SimWorld.HashInt(hash, point.Y);
            hash = SimWorld.HashInt(hash, point.Amount);
            hash = SimWorld.HashInt(hash, point.Subtype);
        }
        foreach (AnimalSpawn animal in Animals)
        {
            hash = SimWorld.HashInt(hash, animal.Species);
            hash = SimWorld.HashInt(hash, animal.X);
            hash = SimWorld.HashInt(hash, animal.Y);
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
        public int[] VertexHeights { get; set; } = Array.Empty<int>();
        public int[] Blocked { get; set; } = Array.Empty<int>();
        public MapPlacement[]? Placements { get; set; } = Array.Empty<MapPlacement>();
        public ResourceDto[]? Resources { get; set; }
        public AnimalDto[]? Animals { get; set; }
        public StartDto[]? Starts { get; set; }
    }

    private sealed class ResourceDto
    {
        public long Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public long Amount { get; set; }
        public long Subtype { get; set; }
    }

    private sealed class AnimalDto
    {
        public long Species { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class StartDto
    {
        public int Team { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
}
