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
            result["starts"] = ToGodotStarts(map.Starts);
            result["resources"] = map.Resources.Count;
            result["player_count"] = map.Players.Count;
            result["players"] = ToGodotPlayers(map.Players);
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
        Godot.Collections.Array placements,
        Godot.Collections.Array players)
    {
        var result = new Godot.Collections.Dictionary();
        try
        {
            AoeSim.MapData map = BuildMap(
                name, seed, width, height, types, vertexHeights, blocked, placements, players);
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
        Godot.Collections.Array placements,
        Godot.Collections.Array players)
    {
        AoeSim.MapData map = BuildMap(
            "validation", 0, width, height, types, vertexHeights, blocked, placements, players);
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

        bool playersConfigured = true;
        var spawnTeams = new HashSet<int>();
        foreach (AoeSim.MapPlacement spawn in spawns)
        {
            spawnTeams.Add(spawn.Team);
        }
        var settingTeams = new HashSet<int>();
        foreach (AoeSim.PlayerSetting player in map.Players)
        {
            settingTeams.Add(player.Team);
            if (!spawnTeams.Contains(player.Team))
            {
                playersConfigured = false;
                messages.Add($"玩家 {player.Team + 1} 没有对应出生点");
            }
        }
        foreach (int team in spawnTeams)
        {
            if (!settingTeams.Contains(team))
            {
                playersConfigured = false;
                messages.Add($"出生点阵营 {team + 1} 没有玩家设置");
            }
        }
        if (spawns.Count != map.Players.Count)
        {
            messages.Add($"出生点 {spawns.Count} 个，玩家设置 {map.Players.Count} 名");
        }

        var result = new Godot.Collections.Dictionary
        {
            { "ok", spawnConnected && resourcesReachable && playersConfigured },
            { "spawn_connected", spawnConnected },
            { "resources_reachable", resourcesReachable },
            { "players_configured", playersConfigured },
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
        Godot.Collections.Array placements,
        Godot.Collections.Array players)
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
        List<AoeSim.MapPlacement> placementList = FromGodotPlacements(placements);
        AoeSim.MapScenario scenario =
            AoeSim.MapScenarioBuilder.FromPlacements(placementList, terrain);
        return new AoeSim.MapData(
            name, seed, terrain, nav, placementList,
            scenario.Resources, scenario.Starts, scenario.Animals,
            FromGodotPlayers(players));
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
                Subtype = IntField(dict, "subtype"),
                Amount = IntField(dict, "amount"),
                BuildingType = IntField(dict, "building_type"),
                UnitType = IntField(dict, "unit_type"),
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
                { "subtype", placement.Subtype },
                { "amount", placement.Amount },
                { "building_type", placement.BuildingType },
                { "unit_type", placement.UnitType },
            });
        }
        return result;
    }

    private static List<AoeSim.PlayerSetting> FromGodotPlayers(Godot.Collections.Array players)
    {
        var result = new List<AoeSim.PlayerSetting>();
        foreach (Variant item in players)
        {
            var dict = item.AsGodotDictionary();
            int team = IntField(dict, "team");
            result.Add(new AoeSim.PlayerSetting(
                team,
                IntField(dict, "civ"),
                TeamLongField(dict, "wood", AoeSim.MapSetup.StartingWood),
                TeamLongField(dict, "food", AoeSim.MapSetup.StartingFood),
                TeamLongField(dict, "gold", AoeSim.MapSetup.StartingGold),
                TeamLongField(dict, "stone", AoeSim.MapSetup.StartingStone)));
        }
        return result;
    }

    private static Godot.Collections.Array ToGodotPlayers(IReadOnlyList<AoeSim.PlayerSetting> players)
    {
        var result = new Godot.Collections.Array();
        foreach (AoeSim.PlayerSetting player in players)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "team", player.Team },
                { "civ", player.Civ },
                { "wood", player.Wood },
                { "food", player.Food },
                { "gold", player.Gold },
                { "stone", player.Stone },
            });
        }
        return result;
    }

    private static long TeamLongField(Godot.Collections.Dictionary dict, string key, long fallback)
    {
        return dict.TryGetValue(key, out Variant value) ? value.AsInt64() : fallback;
    }

    private static Godot.Collections.Array ToGodotStarts(IReadOnlyList<AoeSim.PlayerStart> starts)
    {
        var result = new Godot.Collections.Array();
        foreach (AoeSim.PlayerStart start in starts)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "team", start.Team },
                { "x", start.X },
                { "y", start.Y },
            });
        }
        return result;
    }

    /// <summary>编辑器建筑调色板：按时代分类，供 GDScript 分页展示。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> BuildingOptions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        for (long type = AoeSim.BuildingTypes.TownCenter;
             type <= AoeSim.BuildingTypes.MaxKnown;
             type++)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", type },
                { "name", AoeSim.BuildingTypes.DisplayName(type) },
                { "category", AoeSim.TechCatalog.AgeName(AoeSim.BuildingTypes.AgeOf(type)) },
                { "width", AoeSim.BuildingTypes.SizeWidth(type) },
                { "height", AoeSim.BuildingTypes.SizeHeight(type) },
            });
        }
        return result;
    }

    /// <summary>编辑器单位调色板：村民单列，其余按兵种家族分类。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> UnitOptions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>
        {
            new()
            {
                { "id", AoeSim.UnitDefs.Villager },
                { "name", AoeSim.UnitDefs.DisplayName(AoeSim.UnitDefs.Villager) },
                { "category", "经济" },
            },
        };
        for (long type = 0; type < AoeSim.UnitDefs.Count; type++)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", type },
                { "name", AoeSim.UnitDefs.DisplayName(type) },
                { "category", UnitCategory(type) },
            });
        }
        return result;
    }

    private static string UnitCategory(long unitType)
    {
        long family = AoeSim.UnitFamilies.FamilyOf(unitType);
        return family switch
        {
            AoeSim.UnitFamilies.Infantry => "步兵",
            AoeSim.UnitFamilies.Spear => "长枪兵",
            AoeSim.UnitFamilies.Archer => "弓箭手",
            AoeSim.UnitFamilies.Skirmisher => "散兵",
            AoeSim.UnitFamilies.Scout => "侦察骑兵",
            AoeSim.UnitFamilies.Cavalry => "骑兵",
            AoeSim.UnitFamilies.Camel => "骆驼",
            AoeSim.UnitFamilies.Unique => "独特单位",
            _ => "攻城武器",
        };
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
