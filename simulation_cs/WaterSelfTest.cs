using System;
using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>
/// 水上玩法竖切自测：群岛地图与鱼群、陆/水通行域、码头临水放置、
/// 渔船往返采集与战船水面作战。
/// </summary>
public static class WaterSelfTest
{
    private const long Seed = 20261127;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckIslandsMap(failures);
        CheckDomainTraversal(failures);
        CheckDockPlacement(failures);
        CheckFishingShip(failures);
        CheckWarshipCombat(failures);
        CheckGeneratedIslandsMatch(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static TerrainGrid ShoreTerrain(int width, int height, int waterFromX)
    {
        TerrainGrid terrain = TerrainGrid.Flat(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = waterFromX; x < width; x++)
            {
                terrain.Set(x, y, TerrainGrid.TypeWater, 0);
            }
        }
        return terrain;
    }

    private static NavGrid ShoreNav(int width, int height, int waterFromX)
    {
        var nav = new NavGrid(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = waterFromX; x < width; x++)
            {
                nav.SetBlocked(x, y, true);
            }
        }
        return nav;
    }

    private static int CellX(SimWorld world, long id) =>
        (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));

    private static void CheckIslandsMap(List<string> failures)
    {
        MapData first = MapGenerator.Generate(MapType.Islands, Seed, 2);
        MapData second = MapGenerator.Generate(MapType.Islands, Seed, 2);
        Expect(failures, first.Checksum() == second.Checksum(), "islands same seed same checksum");
        Expect(failures, first.ToJson() == second.ToJson(), "islands same seed same json");

        int waterCells = 0;
        int landCells = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                if (first.Terrain.IsWater(x, y))
                {
                    waterCells += 1;
                }
                else
                {
                    landCells += 1;
                }
                if (first.Terrain.IsWater(x, y))
                {
                    Expect(failures, first.Nav.IsTerrainBlocked(x, y), "islands water is nav blocked for land");
                }
            }
        }
        Expect(failures, waterCells > landCells, "islands map is mostly water");
        Expect(failures, landCells > 0, "islands map carves land");

        int fish = 0;
        foreach (ResourcePoint point in first.Resources)
        {
            if (point.Type == GameSim.ResourceFood && first.Terrain.IsWater(point.X, point.Y))
            {
                fish += 1;
            }
        }
        Expect(failures, fish > 0, "islands map places fish on water");

        foreach (PlayerStart start in first.Starts)
        {
            Expect(
                failures,
                BuildingSystem.CanPlace(first.Nav, first.Terrain, BuildingTypes.TownCenter, start.X, start.Y),
                $"islands start {start.Team} can place town center");
        }
    }

    private static void CheckDomainTraversal(List<string> failures)
    {
        TerrainGrid terrain = ShoreTerrain(16, 8, 8);
        Expect(failures, terrain.CanTraverse(6, 4, 7, 4, MovementDomain.Land), "land step on land");
        Expect(failures, !terrain.CanTraverse(7, 4, 8, 4, MovementDomain.Land), "land cannot enter water");
        Expect(failures, terrain.CanTraverse(9, 4, 10, 4, MovementDomain.Water), "water step on water");
        Expect(failures, !terrain.CanTraverse(8, 4, 7, 4, MovementDomain.Water), "water cannot enter land");

        NavGrid nav = ShoreNav(16, 8, 8);
        var landField = new FlowField(nav, terrain, MovementDomain.Land);
        Expect(failures, landField.Compute(nav, 15, 4) == false, "land field rejects water goal");
        var waterField = new FlowField(nav, terrain, MovementDomain.Water);
        Expect(failures, waterField.Compute(nav, 15, 4) && waterField.Reachable(9, 4),
            "water field reaches across water");
        Expect(failures, !waterField.Reachable(2, 4), "water field cannot reach land");
    }

    private static void CheckDockPlacement(List<string> failures)
    {
        TerrainGrid terrain = ShoreTerrain(16, 8, 8);
        NavGrid nav = ShoreNav(16, 8, 8);
        Expect(failures,
            BuildingSystem.CanPlace(nav, terrain, BuildingTypes.Dock, 5, 3),
            "dock placed on shore next to water");
        Expect(failures,
            !BuildingSystem.CanPlace(nav, terrain, BuildingTypes.Dock, 1, 3),
            "dock rejected away from water");
        Expect(failures,
            !BuildingSystem.CanPlace(nav, terrain, BuildingTypes.Dock, 9, 3),
            "dock rejected on water");
    }

    private static void CheckFishingShip(List<string> failures)
    {
        TerrainGrid terrain = ShoreTerrain(16, 8, 8);
        NavGrid nav = ShoreNav(16, 8, 8);
        var sim = new GameSim(nav, Seed, 2, 6, null, terrain);
        sim.AddResource(0, GameSim.ResourceFood, 0);
        long dock = sim.SpawnDock(5, 3, 0);
        Expect(failures, dock >= 0, "dock spawned on shore");
        long fish = sim.SpawnResourceNode(10, 4, GameSim.ResourceFood, 300);
        long ship = sim.SpawnFishingShip(9, 4, 0, 0.12, 10, 1.0);
        Expect(failures, ship >= 0, "fishing ship spawned");
        sim.IssueGather(ship, fish, dock);
        for (int tick = 0; tick < 260; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) > 0, "fishing ship delivers food to dock");
        Expect(failures, CellX(sim.World, ship) >= 8, "fishing ship stays on water");
    }

    private static void CheckWarshipCombat(List<string> failures)
    {
        TerrainGrid terrain = TerrainGrid.Flat(16, 8, TerrainGrid.TypeWater);
        var nav = new NavGrid(16, 8);
        var sim = new GameSim(nav, Seed + 5, 2, 6, null, terrain);
        long attacker = sim.SpawnWarship(4, 4, 0);
        long defender = sim.SpawnWarship(9, 4, 1);
        Expect(failures, attacker >= 0 && defender >= 0, "warships spawned on water");
        long maxHp = sim.World.GetValue(defender, SimComponents.Health, SimComponents.MaxHp);
        sim.IssueAttack(attacker, defender);
        for (int tick = 0; tick < 30; tick++)
        {
            sim.SimulateTick();
        }
        long hp = sim.World.IsAlive(defender)
            ? sim.World.GetValue(defender, SimComponents.Health, SimComponents.Hp)
            : 0;
        Expect(failures, hp < maxHp, "warship damages enemy ship on water");
    }

    private static void CheckGeneratedIslandsMatch(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Islands, Seed, 2);
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed, 2);
        int townCenters = 0;
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id) || !sim.World.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Building, SimComponents.BuildingType)
                == BuildingTypes.TownCenter)
            {
                townCenters += 1;
            }
        }
        Expect(failures, townCenters == 2, "generated islands match places town centers");
    }
}
