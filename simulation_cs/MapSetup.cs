using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 把一份 <see cref="MapData"/> 落进模拟层：静态阻塞已随地图进入 NavGrid，
/// 这里生成资源实体，并用建筑/经济系统摆出每位玩家的城镇中心、投放点与初始村民。
/// </summary>
public static class MapSetup
{
    public const int StartingVillagers = 3;
    public const long StartingWood = 500;
    public const long StartingFood = 200;
    public const long StartingGold = 100;
    public const long StartingStone = 200;
    public const double VillagerSpeed = 0.09;
    public const long VillagerCarryCapacity = 20;
    public const double VillagerGatherRate = 0.5;

    public static GameSim BuildGeneratedMatch(MapType type, long seed, int playerCount) =>
        BuildGeneratedMatch(MapGenerator.Generate(type, seed, playerCount), seed, playerCount);

    public static GameSim BuildGeneratedMatch(MapData map, long seed, int playerCount)
    {
        var sim = new GameSim(map.Nav, seed, playerCount, 6, MatchConfig.Standard, map.Terrain);
        Apply(sim, map);
        return sim;
    }

    public static void Apply(GameSim sim, MapData map)
    {
        var drops = new Dictionary<int, long>();
        foreach (PlayerStart start in map.Starts)
        {
            sim.AddResource(start.Team, GameSim.ResourceWood, StartingWood);
            sim.AddResource(start.Team, GameSim.ResourceFood, StartingFood);
            sim.AddResource(start.Team, GameSim.ResourceGold, StartingGold);
            sim.AddResource(start.Team, GameSim.ResourceStone, StartingStone);
            long townCenter = sim.IssuePlaceBuilding(
                BuildingTypes.TownCenter, start.X, start.Y, start.Team);
            if (townCenter >= 0)
            {
                BuildingSystem.Complete(sim.World, townCenter);
            }
            int centerX = start.X + BuildingTypes.SizeWidth(BuildingTypes.TownCenter) / 2;
            int centerY = start.Y + BuildingTypes.SizeHeight(BuildingTypes.TownCenter) / 2;
            drops[start.Team] = sim.SpawnDropSite(centerX, centerY, 0b1111);
        }

        foreach (ResourcePoint point in map.Resources)
        {
            sim.SpawnResourceNode(point.X, point.Y, point.Type, point.Amount);
        }

        foreach (PlayerStart start in map.Starts)
        {
            SpawnVillagers(sim, map, start, drops[start.Team]);
        }
    }

    private static void SpawnVillagers(GameSim sim, MapData map, PlayerStart start, long drop)
    {
        int centerX = start.X + BuildingTypes.SizeWidth(BuildingTypes.TownCenter) / 2;
        int centerY = start.Y + BuildingTypes.SizeHeight(BuildingTypes.TownCenter) / 2;
        ResourcePoint? wood = NearestResource(map, GameSim.ResourceWood, centerX, centerY);
        ResourcePoint? food = NearestResource(map, GameSim.ResourceFood, centerX, centerY);
        for (int index = 0; index < StartingVillagers; index++)
        {
            if (!TryFindSpawnCell(sim, centerX, centerY, 3, out int x, out int y))
            {
                continue;
            }
            long villager = sim.SpawnVillager(
                x, y, start.Team, VillagerSpeed, VillagerCarryCapacity, VillagerGatherRate, 1);
            if (villager < 0)
            {
                continue;
            }
            ResourcePoint? target = index == StartingVillagers - 1 ? food : wood;
            if (target != null)
            {
                long node = FindNodeAt(sim, target.X, target.Y);
                if (node >= 0)
                {
                    sim.IssueGather(villager, node, drop);
                }
            }
        }
    }

    private static ResourcePoint? NearestResource(MapData map, long type, int x, int y)
    {
        ResourcePoint? nearest = null;
        long best = long.MaxValue;
        foreach (ResourcePoint point in map.Resources)
        {
            if (point.Type != type)
            {
                continue;
            }
            long dx = point.X - x;
            long dy = point.Y - y;
            long distance = dx * dx + dy * dy;
            if (distance < best)
            {
                best = distance;
                nearest = point;
            }
        }
        return nearest;
    }

    private static long FindNodeAt(GameSim sim, int x, int y)
    {
        long targetX = Fixed.Add(Fixed.FromInt(x), Fixed.Half);
        long targetY = Fixed.Add(Fixed.FromInt(y), Fixed.Half);
        ComponentStore nodes = sim.World.Store(SimComponents.ResourceNode);
        foreach (long id in sim.World.Entities())
        {
            if (!nodes.Has(id) || !sim.World.IsAlive(id))
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Position, SimComponents.X) == targetX
                && sim.World.GetValue(id, SimComponents.Position, SimComponents.Y) == targetY)
            {
                return id;
            }
        }
        return -1;
    }

    private static bool TryFindSpawnCell(GameSim sim, int centerX, int centerY, int minR, out int x, out int y)
    {
        for (int radius = minR; radius <= minR + 3; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != radius)
                    {
                        continue;
                    }
                    int candidateX = centerX + dx;
                    int candidateY = centerY + dy;
                    if (!sim.Terrain.InBounds(candidateX, candidateY))
                    {
                        continue;
                    }
                    byte type = sim.Terrain.TypeAt(candidateX, candidateY);
                    if (type == TerrainGrid.TypeWater || type == TerrainGrid.TypeCliff)
                    {
                        continue;
                    }
                    if (sim.Grid.IsBlocked(candidateX, candidateY))
                    {
                        continue;
                    }
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }
        }
        x = centerX;
        y = centerY;
        return false;
    }
}
