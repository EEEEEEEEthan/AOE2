using System.Collections.Generic;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>
/// 陆地自然资源与狩猎竖切自测：地图子类型与动物成簇、击杀转尸体、
/// 尸体可采集并腐烂消失、gaia 不参与自动索敌、全流程确定性。
/// </summary>
public static class HuntingSelfTest
{
    private const long Seed = 20261127;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckMapSubtypes(failures);
        CheckKillBecomesCorpse(failures);
        CheckCorpseGather(failures);
        CheckCorpseRot(failures);
        CheckGaiaNotAutoTargeted(failures);
        CheckDeterminism(failures);
        return failures;
    }

    private static void CheckMapSubtypes(List<string> failures)
    {
        MapData map = new ArabiaMapGenerator(32, 32).Generate(Seed, 2);
        var subtypes = new HashSet<long>();
        foreach (ResourcePoint point in map.Resources)
        {
            subtypes.Add(point.Subtype);
        }
        Expect(failures, subtypes.Contains(ResourceSubtype.Tree), "map has tree nodes");
        Expect(failures, subtypes.Contains(ResourceSubtype.BerryBush), "map has berry bushes");
        Expect(failures, subtypes.Contains(ResourceSubtype.GoldMine), "map has gold mines");
        Expect(failures, subtypes.Contains(ResourceSubtype.StoneMine), "map has stone mines");

        var species = new HashSet<long>();
        foreach (AnimalSpawn animal in map.Animals)
        {
            species.Add(animal.Species);
        }
        Expect(failures, species.Contains(AnimalSpecies.Sheep), "map has sheep herds");
        Expect(failures, map.Animals.Count > 0, "map places hunting animals");
    }

    private static void CheckKillBecomesCorpse(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), Seed, 2);
        long animal = sim.SpawnHuntingAnimal(5, 5, AnimalSpecies.Sheep);
        Expect(failures, animal >= 0, "sheep spawned");
        long hunter = sim.SpawnUnit(4, 5, 0, 100, 20, 1.0, 1.0, 0.2);
        sim.IssueAttack(hunter, animal);
        Run(sim, 12);

        Expect(failures, sim.World.IsAlive(animal), "corpse keeps the animal entity id");
        Expect(failures, !sim.World.HasComponent(animal, SimComponents.HuntingAnimal), "animal component removed");
        Expect(failures, sim.World.HasComponent(animal, SimComponents.ResourceNode), "animal becomes resource node");
        Expect(failures, !sim.World.HasComponent(animal, SimComponents.Health), "corpse is no longer attackable");
        Expect(
            failures,
            sim.World.GetValue(animal, SimComponents.ResourceNode, SimComponents.Subtype) == ResourceSubtype.Corpse,
            "corpse carries corpse subtype");
        Expect(
            failures,
            sim.World.GetValue(animal, SimComponents.ResourceNode, SimComponents.NodeType) == GameSim.ResourceFood,
            "corpse yields food");
        Expect(
            failures,
            sim.World.GetValue(animal, SimComponents.ResourceNode, SimComponents.Remaining) == AnimalSpecies.Meat(AnimalSpecies.Sheep),
            "corpse keeps species meat amount");
    }

    private static void CheckCorpseGather(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), Seed, 2);
        long animal = sim.SpawnHuntingAnimal(6, 5, AnimalSpecies.Deer);
        long hunter = sim.SpawnUnit(5, 5, 0, 100, 40, 1.0, 1.0, 0.2);
        sim.IssueAttack(hunter, animal);
        Run(sim, 12);
        long drop = sim.SpawnDropSite(5, 8, 0b1111);
        long villager = sim.SpawnVillager(5, 6, 0, 0.3, 10, 1.0);
        Run(sim, 2);
        sim.IssueGather(villager, animal, drop);
        Run(sim, 120);
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) > 0, "villager gathers food from corpse");
    }

    private static void CheckCorpseRot(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), Seed, 2);
        long animal = sim.SpawnHuntingAnimal(5, 5, AnimalSpecies.Boar);
        long killer = sim.SpawnUnit(4, 5, 0, 100, 100, 1.0, 1.0, 0.2);
        sim.IssueAttack(killer, animal);
        Run(sim, 12);
        Expect(failures, sim.World.HasComponent(animal, SimComponents.Corpse), "boar leaves a corpse");
        if (sim.World.IsAlive(animal))
        {
            sim.World.SetValue(animal, SimComponents.Corpse, SimComponents.RotTicks, 3);
        }
        Run(sim, 6);
        Expect(failures, !sim.World.IsAlive(animal), "rotted corpse disappears");
    }

    private static void CheckGaiaNotAutoTargeted(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), Seed, 2);
        long animal = sim.SpawnHuntingAnimal(6, 5, AnimalSpecies.Sheep);
        sim.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.2);
        Run(sim, 40);
        long hp = sim.World.GetValue(animal, SimComponents.Health, SimComponents.Hp);
        long maxHp = sim.World.GetValue(animal, SimComponents.Health, SimComponents.MaxHp);
        Expect(failures, hp == maxHp, "gaia animals are not auto-acquired");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        long first = RunScenario();
        long second = RunScenario();
        Expect(failures, first == second, "hunting scenario is deterministic");

        MapData mapFirst = new ArabiaMapGenerator(32, 32).Generate(Seed, 2);
        MapData mapSecond = new ArabiaMapGenerator(32, 32).Generate(Seed, 2);
        Expect(failures, mapFirst.Checksum() == mapSecond.Checksum(), "animal map checksum is deterministic");
        Expect(failures, mapFirst.ToJson() == mapSecond.ToJson(), "animal map json is deterministic");

        MapData restored = MapData.FromJson(mapFirst.ToJson());
        Expect(failures, restored.Checksum() == mapFirst.Checksum(), "animal map json roundtrip keeps checksum");
        Expect(failures, restored.Animals.Count == mapFirst.Animals.Count, "animal map json keeps animals");
    }

    private static long RunScenario()
    {
        var sim = new GameSim(new NavGrid(24, 24), Seed, 2);
        long sheep = sim.SpawnHuntingAnimal(8, 6, AnimalSpecies.Sheep);
        long deer = sim.SpawnHuntingAnimal(9, 9, AnimalSpecies.Deer);
        long hunter = sim.SpawnUnit(7, 6, 0, 100, 15, 1.0, 1.2, 0.3);
        sim.IssueAttack(hunter, sheep);
        Run(sim, 10);
        long drop = sim.SpawnDropSite(5, 10, 0b1111);
        long villager = sim.SpawnVillager(5, 8, 0, 0.25, 8, 1.0);
        sim.IssueGather(villager, sheep, drop);
        Run(sim, 60);
        sim.IssueAttack(hunter, deer);
        Run(sim, 60);
        return sim.World.Checksum();
    }

    private static void Run(GameSim sim, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            sim.SimulateTick();
        }
    }
}
