using System.Collections.Generic;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>地图生成器的确定性、对称性、可建性与落地驱动自测。</summary>
public static class MapSelfTest
{
    private const int Size = 32;

    private const long Seed = 20261127;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckDeterminism(failures);
        CheckSymmetry(failures);
        CheckTerrainMatchesNav(failures);
        CheckStartsAndResources(failures);
        CheckJsonRoundtrip(failures);
        CheckSpawnedMatch(failures);
        return failures;
    }

    private static MapData Generate(long seed = Seed, int players = 2) =>
        new ArabiaMapGenerator(Size, Size).Generate(seed, players);

    private static void CheckDeterminism(List<string> failures)
    {
        MapData first = Generate();
        MapData second = Generate();
        Expect(failures, first.Checksum() == second.Checksum(), "same seed same map checksum");
        Expect(failures, first.ToJson() == second.ToJson(), "same seed same map json");

        MapData other = Generate(Seed + 1);
        Expect(failures, first.Checksum() != other.Checksum(), "different seed different map");
    }

    private static void CheckSymmetry(List<string> failures)
    {
        MapData map = Generate();
        var cells = new HashSet<(long, int, int, long)>();
        foreach (ResourcePoint point in map.Resources)
        {
            cells.Add((point.Type, point.X, point.Y, point.Amount));
        }
        bool mirrored = map.Resources.Count > 0;
        foreach (ResourcePoint point in map.Resources)
        {
            int mirrorX = map.Width - 1 - point.X;
            mirrored &= cells.Contains((point.Type, mirrorX, point.Y, point.Amount));
        }
        Expect(failures, mirrored, "two player resources mirror across the map axis");

        var animalCells = new HashSet<(long, int, int)>();
        foreach (AnimalSpawn animal in map.Animals)
        {
            animalCells.Add((animal.Species, animal.X, animal.Y));
        }
        bool animalsMirrored = map.Animals.Count > 0;
        foreach (AnimalSpawn animal in map.Animals)
        {
            animalsMirrored &= animalCells.Contains((animal.Species, map.Width - 1 - animal.X, animal.Y));
        }
        Expect(failures, animalsMirrored, "two player animals mirror across the map axis");
        Expect(failures, map.Animals.Count > 0, "map generator places hunting animals");

        MapData four = Generate(Seed, 4);
        Expect(failures, four.Starts.Count == 4, "four player map has four starts");
    }

    private static void CheckTerrainMatchesNav(List<string> failures)
    {
        MapData map = Generate();
        bool matches = true;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                byte type = map.Terrain.TypeAt(x, y);
                bool impassable = type == TerrainGrid.TypeWater || type == TerrainGrid.TypeCliff;
                matches &= !impassable || map.Nav.IsTerrainBlocked(x, y);
            }
        }
        Expect(failures, matches, "water and cliff cells are blocked in nav grid");
        Expect(failures, map.Nav.IsTerrainBlocked(0, 0), "map border is blocked");
    }

    private static void CheckStartsAndResources(List<string> failures)
    {
        MapData map = Generate();
        long townCenter = BuildingTypes.TownCenter;
        foreach (PlayerStart start in map.Starts)
        {
            Expect(
                failures,
                BuildingSystem.CanPlace(map.Nav, map.Terrain, townCenter, start.X, start.Y),
                $"start {start.Team} town center can be placed");
        }

        bool resourcesValid = true;
        foreach (ResourcePoint point in map.Resources)
        {
            byte type = map.Terrain.TypeAt(point.X, point.Y);
            bool onGround = type != TerrainGrid.TypeWater && type != TerrainGrid.TypeCliff;
            resourcesValid &= onGround;
            resourcesValid &= !map.Nav.IsTerrainBlocked(point.X, point.Y);
            resourcesValid &= !InsideTownCenter(map, point);
        }
        Expect(failures, resourcesValid, "all resource points sit on free buildable ground");
        Expect(failures, map.Resources.Count > 0, "map generator places resources");
    }

    private static bool InsideTownCenter(MapData map, ResourcePoint point)
    {
        foreach (PlayerStart start in map.Starts)
        {
            if (point.X >= start.X && point.X < start.X + BuildingTypes.SizeWidth(BuildingTypes.TownCenter)
                && point.Y >= start.Y && point.Y < start.Y + BuildingTypes.SizeHeight(BuildingTypes.TownCenter))
            {
                return true;
            }
        }
        return false;
    }

    private static void CheckJsonRoundtrip(List<string> failures)
    {
        MapData original = Generate();
        MapData restored = MapData.FromJson(original.ToJson());
        Expect(failures, restored.Checksum() == original.Checksum(), "map json roundtrip keeps checksum");
        Expect(
            failures,
            restored.Resources.Count == original.Resources.Count,
            "map json roundtrip keeps resource points");
        Expect(
            failures,
            restored.Starts.Count == original.Starts.Count,
            "map json roundtrip keeps player starts");
    }

    private static void CheckSpawnedMatch(List<string> failures)
    {
        MapData map = Generate();
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed, 2);

        int townCenters = 0;
        int villagers = 0;
        int resourceNodes = 0;
        int animals = 0;
        bool allComplete = true;
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id))
            {
                continue;
            }
            if (sim.World.HasComponent(id, SimComponents.Building))
            {
                if (sim.World.GetValue(id, SimComponents.Building, SimComponents.BuildingType)
                    == BuildingTypes.TownCenter)
                {
                    townCenters += 1;
                    allComplete &= sim.World.GetValue(
                        id, SimComponents.Building, SimComponents.State) == BuildingSystem.StateComplete;
                }
            }
            if (sim.World.HasComponent(id, SimComponents.Gather))
            {
                villagers += 1;
            }
            if (sim.World.HasComponent(id, SimComponents.ResourceNode))
            {
                resourceNodes += 1;
            }
            if (sim.World.HasComponent(id, SimComponents.HuntingAnimal))
            {
                animals += 1;
            }
        }
        Expect(failures, townCenters == 2, "generated match places one town center per player");
        Expect(failures, allComplete, "starting town centers are complete");
        Expect(
            failures,
            villagers == 2 * MapSetup.StartingVillagers,
            "generated match places starting villagers");
        Expect(failures, resourceNodes == map.Resources.Count, "generated match spawns every resource point");
        Expect(failures, animals == map.Animals.Count, "generated match spawns every hunting animal");
        Expect(failures, sim.Grid.Width == Size && sim.Grid.Height == Size, "generated match uses map grid");
    }
}
