using System;
using System.Collections.Generic;
using Godot;

namespace AOE2Framework;

/// <summary>
/// 地图编辑器与 C# MapData 的桥：编解码与运行期完全同一份 MapData，
/// 并用 TerrainGrid / NavGrid / FlowField 校验出生点连通与资源可达。
/// </summary>
[GlobalClass]
public partial class MapEditorBridge : Node
{
    public Godot.Collections.Dictionary LoadMap(string path)
    {
        var result = new Godot.Collections.Dictionary();
        try
        {
            AoeSim.MapData map = AoeSim.MapData.Load(ResolvePath(path));
            result["name"] = map.Name;
            result["seed"] = map.Seed;
            result["width"] = map.Width;
            result["height"] = map.Height;
            result["types"] = ToInts(map.Terrain.Types);
            result["vertex_heights"] = ToInts(map.Terrain.VertexHeights);
            result["blocked"] = ToBlockedFlags(map.Nav.Blocked);
            result["placements"] = ToGodotPlacements(map.Placements);
        }
        catch (Exception exception)
        {
            result["error"] = exception.Message;
        }
        return result;
    }

    public Godot.Collections.Dictionary SaveMap(
        string path,
        string name,
        long seed,
        int width,
        int height,
        int[] types,
        int[] vertexHeights,
        int[] blocked,
        Godot.Collections.Array placements)
    {
        var result = new Godot.Collections.Dictionary();
        try
        {
            AoeSim.MapData map = BuildMap(
                name, seed, width, height, types, vertexHeights, blocked, placements);
            string resolved = ResolvePath(path);
            string? directory = System.IO.Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            map.Save(resolved);
            result["ok"] = true;
        }
        catch (Exception exception)
        {
            result["ok"] = false;
            result["error"] = exception.Message;
        }
        return result;
    }

    public Godot.Collections.Dictionary Validate(
        int width,
        int height,
        int[] types,
        int[] vertexHeights,
        int[] blocked,
        Godot.Collections.Array placements)
    {
        AoeSim.MapData map = BuildMap(
            "validation", 0, width, height, types, vertexHeights, blocked, placements);
        var spawns = new List<AoeSim.MapPlacement>();
        var resources = new List<AoeSim.MapPlacement>();
        foreach (AoeSim.MapPlacement placement in map.Placements)
        {
            if (placement.Kind == AoeSim.MapPlacement.KindSpawn)
            {
                spawns.Add(placement);
            }
            else if (placement.Kind == AoeSim.MapPlacement.KindResource)
            {
                resources.Add(placement);
            }
        }

        var messages = new List<string>();
        bool spawnConnected = false;
        AoeSim.FlowField? field = null;
        if (spawns.Count == 0)
        {
            messages.Add("缺少出生点，无法校验连通");
        }
        else
        {
            AoeSim.MapPlacement first = spawns[0];
            if (map.Nav.IsBlocked(first.X, first.Y))
            {
                messages.Add("首个出生点被障碍占据");
            }
            field = new AoeSim.FlowField(map.Nav, map.Terrain);
            spawnConnected = field.Compute(map.Nav, first.X, first.Y);
            if (!spawnConnected)
            {
                messages.Add("首个出生点不在地图可走区域");
            }
            for (int index = 1; spawnConnected && index < spawns.Count; index++)
            {
                AoeSim.MapPlacement spawn = spawns[index];
                if (!field.Reachable(spawn.X, spawn.Y))
                {
                    spawnConnected = false;
                    messages.Add($"出生点 ({spawn.X}, {spawn.Y}) 与首个出生点不连通");
                }
            }
        }

        bool resourcesReachable = true;
        if (resources.Count == 0)
        {
            messages.Add("没有资源点，跳过资源可达校验");
        }
        else if (field != null)
        {
            foreach (AoeSim.MapPlacement resource in resources)
            {
                if (HasReachableNeighbor(field, map, resource.X, resource.Y))
                {
                    continue;
                }
                resourcesReachable = false;
                messages.Add($"资源点 ({resource.X}, {resource.Y}) 无法从出生点到达");
            }
        }

        var result = new Godot.Collections.Dictionary
        {
            { "ok", spawnConnected && resourcesReachable },
            { "spawn_connected", spawnConnected },
            { "resources_reachable", resourcesReachable },
            { "spawn_count", spawns.Count },
            { "resource_count", resources.Count },
            { "messages", messages.ToArray() },
        };
        return result;
    }

    private static AoeSim.MapData BuildMap(
        string name,
        long seed,
        int width,
        int height,
        int[] types,
        int[] vertexHeights,
        int[] blocked,
        Godot.Collections.Array placements)
    {
        int cells = width * height;
        int vertexCells = (width + 1) * (height + 1);
        if (types.Length != cells || vertexHeights.Length != vertexCells)
        {
            throw new ArgumentException("terrain array size mismatch");
        }
        var terrain = new AoeSim.TerrainGrid(width, height);
        var nav = new AoeSim.NavGrid(width, height);
        for (int index = 0; index < vertexCells; index++)
        {
            terrain.VertexHeights[index] = (byte)vertexHeights[index];
        }
        for (int index = 0; index < cells; index++)
        {
            terrain.Types[index] = (byte)types[index];
            if (index < blocked.Length)
            {
                nav.Blocked[index] = blocked[index] != 0 ? AoeSim.NavGrid.CostBlocked : (byte)0;
            }
        }
        return new AoeSim.MapData(name, seed, terrain, nav, FromGodotPlacements(placements));
    }

    private static List<AoeSim.MapPlacement> FromGodotPlacements(Godot.Collections.Array placements)
    {
        var result = new List<AoeSim.MapPlacement>();
        foreach (Variant item in placements)
        {
            var dict = item.AsGodotDictionary();
            result.Add(new AoeSim.MapPlacement
            {
                Kind = StringField(dict, "kind", AoeSim.MapPlacement.KindSpawn),
                X = IntField(dict, "x"),
                Y = IntField(dict, "y"),
                Team = IntField(dict, "team"),
                NodeType = IntField(dict, "node_type"),
                Amount = IntField(dict, "amount"),
            });
        }
        return result;
    }

    private static Godot.Collections.Array ToGodotPlacements(IReadOnlyList<AoeSim.MapPlacement> placements)
    {
        var result = new Godot.Collections.Array();
        foreach (AoeSim.MapPlacement placement in placements)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "kind", placement.Kind },
                { "x", placement.X },
                { "y", placement.Y },
                { "team", placement.Team },
                { "node_type", placement.NodeType },
                { "amount", placement.Amount },
            });
        }
        return result;
    }

    private static bool HasReachableNeighbor(
        AoeSim.FlowField field, AoeSim.MapData map, int cellX, int cellY)
    {
        if (IsPassable(map, cellX, cellY) && field.Reachable(cellX, cellY))
        {
            return true;
        }
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }
                int neighborX = cellX + offsetX;
                int neighborY = cellY + offsetY;
                if (IsPassable(map, neighborX, neighborY) && field.Reachable(neighborX, neighborY))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsPassable(AoeSim.MapData map, int cellX, int cellY)
    {
        if (!map.Terrain.InBounds(cellX, cellY) || map.Nav.IsBlocked(cellX, cellY))
        {
            return false;
        }
        byte type = map.Terrain.TypeAt(cellX, cellY);
        return type != AoeSim.TerrainGrid.TypeWater && type != AoeSim.TerrainGrid.TypeCliff;
    }

    private static int IntField(Godot.Collections.Dictionary dict, string key)
    {
        return dict.TryGetValue(key, out Variant value) ? value.AsInt32() : 0;
    }

    private static string StringField(Godot.Collections.Dictionary dict, string key, string fallback)
    {
        return dict.TryGetValue(key, out Variant value) ? value.AsString() : fallback;
    }

    private static int[] ToInts(byte[] values)
    {
        var result = new int[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = values[index];
        }
        return result;
    }

    private static int[] ToBlockedFlags(byte[] values)
    {
        var result = new int[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = values[index] == AoeSim.NavGrid.CostBlocked ? 1 : 0;
        }
        return result;
    }

    private static string ResolvePath(string path)
    {
        if (path.StartsWith("res://", StringComparison.Ordinal)
            || path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ProjectSettings.GlobalizePath(path);
        }
        return path;
    }
}
