using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>地图类型脚本枚举：先落地一张阿拉伯，后续黑森林/岛屿按同一接口扩展。</summary>
public enum MapType
{
    Arabia = 0,
}

public interface IMapGenerator
{
    MapType Type { get; }

    MapData Generate(long seed, int playerCount);
}

public static class MapGenerator
{
    public static IMapGenerator For(
        MapType type,
        int width = ArabiaMapGenerator.DefaultWidth,
        int height = ArabiaMapGenerator.DefaultHeight) => type switch
    {
        MapType.Arabia => new ArabiaMapGenerator(width, height),
        _ => throw new ArgumentException($"unknown map type {type}"),
    };

    public static MapData Generate(MapType type, long seed, int playerCount) =>
        For(type).Generate(seed, playerCount);
}

/// <summary>
/// 阿拉伯（Arabia）：平整草地打底、四周悬崖封边，双方以地图中轴镜像对称。
/// 每位玩家在城镇中心周围得到木材、浆果/羊、金、石资源簇；同种子逐格一致。
/// 只用整数与 <see cref="SimRng"/>，不引入任何表现层依赖。
/// </summary>
public sealed class ArabiaMapGenerator : IMapGenerator
{
    public const int DefaultWidth = 64;
    public const int DefaultHeight = 64;

    private const int MinSize = 24;
    private const int TownCenterSize = 4;
    private const int StartPadRadius = 3;
    private const int PlacementAttempts = 320;

    private static readonly (long Type, int Count, long Amount, int MinR, int MaxR)[] Clusters =
    {
        (GameSim.ResourceWood, 12, 100, 6, 14),
        (GameSim.ResourceFood, 4, 125, 4, 10),
        (GameSim.ResourceFood, 4, 100, 5, 12),
        (GameSim.ResourceGold, 3, 800, 6, 13),
        (GameSim.ResourceStone, 2, 350, 6, 13),
    };

    private readonly int _width;
    private readonly int _height;

    public ArabiaMapGenerator(int width = DefaultWidth, int height = DefaultHeight)
    {
        if (width < MinSize || height < MinSize)
        {
            throw new ArgumentException($"map size must be at least {MinSize}x{MinSize}");
        }
        _width = width;
        _height = height;
    }

    public MapType Type => MapType.Arabia;

    public MapData Generate(long seed, int playerCount)
    {
        if (playerCount < 1 || playerCount > 4)
        {
            throw new ArgumentException("playerCount must be between 1 and 4");
        }
        var rng = new SimRng(MixSeed(seed));
        var terrain = new TerrainGrid(_width, _height);
        var nav = new NavGrid(_width, _height);
        PaintBorder(terrain, nav);

        List<PlayerStart> starts = BuildStarts(playerCount);
        var occupied = new HashSet<long>();
        foreach (PlayerStart start in starts)
        {
            FlattenStartPad(terrain, nav, start);
            MarkTownCenter(occupied, start);
        }

        var resources = new List<ResourcePoint>();
        for (int index = 0; index < starts.Count; index++)
        {
            if (IsMirrorTarget(index))
            {
                continue;
            }
            int before = resources.Count;
            AddStartResources(resources, occupied, terrain, nav, rng, starts[index]);
            int mirror = MirrorOf(index);
            if (mirror >= 0)
            {
                MirrorRange(resources, before, occupied, terrain, nav, starts[mirror]);
            }
        }

        SyncNavWithTerrain(terrain, nav);
        return new MapData("arabia", seed, terrain, nav, resources: resources, starts: starts);
    }

    private static long MixSeed(long seed) => seed ^ 0x5DEECE66DL;

    private List<PlayerStart> BuildStarts(int playerCount)
    {
        int half = TownCenterSize / 2;
        int leftX = Math.Max(3, _width / 4) - half;
        int rightX = (_width - 1 - (_width / 4)) - half;
        int midX = (_width / 2) - half;
        int middleY = (_height / 2) - half;
        var starts = new List<PlayerStart>(playerCount)
        {
            new(0, leftX, middleY),
        };
        if (playerCount >= 2)
        {
            starts.Add(new PlayerStart(1, rightX, middleY));
        }
        if (playerCount >= 3)
        {
            int topY = Math.Max(3, _height / 4) - half;
            starts.Add(new PlayerStart(2, midX, topY));
        }
        if (playerCount >= 4)
        {
            int bottomY = (_height - 1 - (_height / 4)) - half;
            starts.Add(new PlayerStart(3, midX, bottomY));
        }
        return starts;
    }

    private static bool IsMirrorTarget(int index) => index == 1;

    private static int MirrorOf(int index) => index == 0 ? 1 : -1;

    private void PaintBorder(TerrainGrid terrain, NavGrid nav)
    {
        for (int x = 0; x < _width; x++)
        {
            terrain.Set(x, 0, TerrainGrid.TypeCliff, 2);
            terrain.Set(x, _height - 1, TerrainGrid.TypeCliff, 2);
            nav.SetBlocked(x, 0, true);
            nav.SetBlocked(x, _height - 1, true);
        }
        for (int y = 0; y < _height; y++)
        {
            terrain.Set(0, y, TerrainGrid.TypeCliff, 2);
            terrain.Set(_width - 1, y, TerrainGrid.TypeCliff, 2);
            nav.SetBlocked(0, y, true);
            nav.SetBlocked(_width - 1, y, true);
        }
    }

    private void FlattenStartPad(TerrainGrid terrain, NavGrid nav, PlayerStart start)
    {
        int centerX = start.X + TownCenterSize / 2;
        int centerY = start.Y + TownCenterSize / 2;
        for (int y = centerY - StartPadRadius; y <= centerY + StartPadRadius; y++)
        {
            for (int x = centerX - StartPadRadius; x <= centerX + StartPadRadius; x++)
            {
                if (!terrain.InBounds(x, y))
                {
                    continue;
                }
                terrain.Set(x, y, TerrainGrid.TypeGrass, 0);
                nav.SetBlocked(x, y, false);
            }
        }
    }

    private static void MarkTownCenter(HashSet<long> occupied, PlayerStart start)
    {
        for (int y = start.Y; y < start.Y + TownCenterSize; y++)
        {
            for (int x = start.X; x < start.X + TownCenterSize; x++)
            {
                occupied.Add(CellKey(x, y));
            }
        }
    }

    private void AddStartResources(
        List<ResourcePoint> resources,
        HashSet<long> occupied,
        TerrainGrid terrain,
        NavGrid nav,
        SimRng rng,
        PlayerStart start)
    {
        int centerX = start.X + TownCenterSize / 2;
        int centerY = start.Y + TownCenterSize / 2;
        foreach ((long type, int count, long amount, int minR, int maxR) in Clusters)
        {
            for (int index = 0; index < count; index++)
            {
                if (TryPlaceSpot(
                    rng, occupied, terrain, nav, centerX, centerY, minR, maxR,
                    out int x, out int y))
                {
                    resources.Add(new ResourcePoint(type, x, y, amount));
                }
            }
        }
    }

    private bool TryPlaceSpot(
        SimRng rng,
        HashSet<long> occupied,
        TerrainGrid terrain,
        NavGrid nav,
        int centerX,
        int centerY,
        int minR,
        int maxR,
        out int x,
        out int y)
    {
        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            int dx = (int)rng.NextIntRange(-maxR, maxR);
            int dy = (int)rng.NextIntRange(-maxR, maxR);
            int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (distance < minR)
            {
                continue;
            }
            int candidateX = centerX + dx;
            int candidateY = centerY + dy;
            if (IsFreeSpot(occupied, terrain, nav, candidateX, candidateY))
            {
                occupied.Add(CellKey(candidateX, candidateY));
                x = candidateX;
                y = candidateY;
                return true;
            }
        }
        return TryFallbackSpot(rng, occupied, terrain, nav, centerX, centerY, minR, maxR, out x, out y);
    }

    private bool TryFallbackSpot(
        SimRng rng,
        HashSet<long> occupied,
        TerrainGrid terrain,
        NavGrid nav,
        int centerX,
        int centerY,
        int minR,
        int maxR,
        out int x,
        out int y)
    {
        int offset = (int)rng.NextIntRange(0, _width * _height - 1);
        for (int step = 0; step < _width * _height; step++)
        {
            int cell = (offset + step) % (_width * _height);
            int candidateX = cell % _width;
            int candidateY = cell / _width;
            int distance = Math.Max(Math.Abs(candidateX - centerX), Math.Abs(candidateY - centerY));
            if (distance < minR || distance > maxR)
            {
                continue;
            }
            if (IsFreeSpot(occupied, terrain, nav, candidateX, candidateY))
            {
                occupied.Add(CellKey(candidateX, candidateY));
                x = candidateX;
                y = candidateY;
                return true;
            }
        }
        x = 0;
        y = 0;
        return false;
    }

    private bool IsFreeSpot(
        HashSet<long> occupied, TerrainGrid terrain, NavGrid nav, int x, int y)
    {
        if (x < 1 || y < 1 || x >= _width - 1 || y >= _height - 1)
        {
            return false;
        }
        byte type = terrain.TypeAt(x, y);
        if (type == TerrainGrid.TypeWater || type == TerrainGrid.TypeCliff)
        {
            return false;
        }
        if (nav.IsTerrainBlocked(x, y) || nav.IsBuildingBlocked(x, y))
        {
            return false;
        }
        return !occupied.Contains(CellKey(x, y));
    }

    private void MirrorRange(
        List<ResourcePoint> resources,
        int fromIndex,
        HashSet<long> occupied,
        TerrainGrid terrain,
        NavGrid nav,
        PlayerStart mirrorStart)
    {
        int count = resources.Count;
        for (int index = fromIndex; index < count; index++)
        {
            ResourcePoint source = resources[index];
            int mirroredX = _width - 1 - source.X;
            if (IsFreeSpot(occupied, terrain, nav, mirroredX, source.Y))
            {
                occupied.Add(CellKey(mirroredX, source.Y));
                resources.Add(new ResourcePoint(source.Type, mirroredX, source.Y, source.Amount));
                continue;
            }
            int centerX = mirrorStart.X + TownCenterSize / 2;
            int centerY = mirrorStart.Y + TownCenterSize / 2;
            if (TryFallbackSpot(
                new SimRng(source.Amount + source.Type * 131 + source.Y),
                occupied, terrain, nav, centerX, centerY, 4, 14,
                out int fallbackX, out int fallbackY))
            {
                resources.Add(new ResourcePoint(source.Type, fallbackX, fallbackY, source.Amount));
            }
        }
    }

    private void SyncNavWithTerrain(TerrainGrid terrain, NavGrid nav)
    {
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                byte type = terrain.TypeAt(x, y);
                if (type == TerrainGrid.TypeWater || type == TerrainGrid.TypeCliff)
                {
                    nav.SetBlocked(x, y, true);
                }
            }
        }
    }

    private static long CellKey(int x, int y) => ((long)x << 32) ^ (y & 0xFFFF_FFFFL);
}
