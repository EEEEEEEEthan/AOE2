using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>
/// 电脑玩家自测：经济采集、房屋/兵营建造、出兵、进攻、两档难度可区分，
/// 以及同种子同指令下校验和一致。
/// </summary>
public static class ComputerPlayerSelfTest
{
    private const int MapWidth = 40;
    private const int MapHeight = 40;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckEconomyAndProduction(failures);
        CheckAttack(failures);
        CheckDeterminism(failures);
        CheckDifficultyDiffers(failures);
        return failures;
    }

    private static SimulationServer Build(long seed, AiDifficulty difficulty, bool rich, out GameSim sim)
    {
        MapData map = MapGenerator.For(MapType.Arabia, MapWidth, MapHeight).Generate(seed, 2);
        sim = MapSetup.BuildGeneratedMatch(map, seed, 2);
        if (rich)
        {
            for (long kind = 0; kind < 4; kind++)
            {
                sim.AddResource(1, kind, 5000);
            }
        }
        var server = new SimulationServer(sim);
        server.AddComputerPlayer(new ComputerPlayer(sim, 1, difficulty, map.Starts, seed));
        return server;
    }

    private static long Advance(SimulationServer server, int ticks)
    {
        long checksum = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            checksum = server.Step();
        }
        return checksum;
    }

    private static int CountVillagers(GameSim sim, long team) =>
        CountTeamEntities(sim, team, SimComponents.Gather, excludeBuildings: true);

    private static int CountMilitary(GameSim sim, long team) =>
        CountTeamEntities(sim, team, SimComponents.Combat, excludeBuildings: true);

    private static int CountTeamEntities(GameSim sim, long team, string component, bool excludeBuildings)
    {
        int count = 0;
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id) || !sim.World.HasComponent(id, component))
            {
                continue;
            }
            if (!sim.World.HasComponent(id, SimComponents.Team)
                || sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (excludeBuildings && sim.World.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            count += 1;
        }
        return count;
    }

    private static long FindBuilding(GameSim sim, long team, long type)
    {
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id) || !sim.World.HasComponent(id, SimComponents.Building))
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Building, SimComponents.BuildingType) == type)
            {
                return id;
            }
        }
        return -1;
    }

    private static bool HasGatheringVillager(GameSim sim, long team)
    {
        foreach (long id in sim.World.Entities())
        {
            if (!sim.World.IsAlive(id) || !sim.World.HasComponent(id, SimComponents.Gather))
            {
                continue;
            }
            if (!sim.World.HasComponent(id, SimComponents.Team)
                || sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Gather, SimComponents.NodeEntity) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void CheckEconomyAndProduction(List<string> failures)
    {
        SimulationServer server = Build(701, AiDifficulty.Normal, rich: false, out GameSim sim);
        Advance(server, 900);

        int villagers = CountVillagers(sim, 1);
        int military = CountMilitary(sim, 1);
        long barracks = FindBuilding(sim, 1, BuildingTypes.Barracks);
        long barracksState = barracks >= 0
            ? sim.World.GetValue(barracks, SimComponents.Building, SimComponents.State)
            : -1;
        Expect(failures,
            villagers > MapSetup.StartingVillagers,
            $"ai trains additional villagers (villagers={villagers}, food={sim.Resource(1, GameSim.ResourceFood)}, wood={sim.Resource(1, GameSim.ResourceWood)}, pop={sim.PopulationUsed(1)}/{sim.PopulationCap(1)})");
        Expect(failures,
            barracks >= 0,
            $"ai builds a barracks (state={barracksState})");
        Expect(failures,
            military > 0,
            $"ai produces military units (military={military})");
        Expect(failures,
            HasGatheringVillager(sim, 1),
            "ai keeps villagers gathering");
    }

    private static void CheckAttack(List<string> failures)
    {
        SimulationServer server = Build(702, AiDifficulty.Normal, rich: true, out GameSim sim);
        Advance(server, 1500);

        long enemyTownCenter = FindBuilding(sim, 0, BuildingTypes.TownCenter);
        Expect(failures, enemyTownCenter >= 0, "enemy town center still present");
        long remainingHp = sim.World.GetValue(
            enemyTownCenter, SimComponents.Health, SimComponents.Hp);
        Expect(failures,
            remainingHp < BuildingTypes.MaxHp(BuildingTypes.TownCenter),
            $"ai attacks the enemy base (enemy hp={remainingHp})");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        long first = DeterminismRun(703, AiDifficulty.Normal, 600, out int firstVillagers);
        long second = DeterminismRun(703, AiDifficulty.Normal, 600, out int secondVillagers);
        Expect(failures, first == second, "same seed and ai decisions share checksum");
        Expect(failures, firstVillagers == secondVillagers, "same seed reaches same villager count");
    }

    private static long DeterminismRun(long seed, AiDifficulty difficulty, int ticks, out int villagers)
    {
        SimulationServer server = Build(seed, difficulty, rich: true, out GameSim sim);
        long checksum = Advance(server, ticks);
        villagers = CountVillagers(sim, 1);
        return checksum;
    }

    private static void CheckDifficultyDiffers(List<string> failures)
    {
        long easy = DeterminismRun(704, AiDifficulty.Easy, 700, out int easyVillagers);
        long hard = DeterminismRun(704, AiDifficulty.Hard, 700, out int hardVillagers);
        Expect(failures, easy != hard, "difficulty changes the match outcome");
        Expect(failures,
            hardVillagers > easyVillagers,
            "hard difficulty fields a larger economy");
    }
}
