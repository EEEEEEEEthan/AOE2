using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>驻守语义：城镇中心/箭塔容量、驻军加成与城镇警钟。</summary>
public static partial class KernelSelfTest
{
    private static void CheckGarrison(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 601, 2, 6);
        sim.AddResource(0, GameSim.ResourceWood, 2000);
        sim.AddResource(0, GameSim.ResourceStone, 2000);
        long tc = sim.IssuePlaceBuilding(BuildingTypes.TownCenter, 2, 2, 0);
        BuildingSystem.Complete(sim.World, tc);
        Expect(failures,
            sim.World.HasComponent(tc, SimComponents.Combat),
            "completed town center gains combat");
        Expect(failures,
            sim.World.HasComponent(tc, SimComponents.Garrison),
            "completed town center gains garrison ledger");
        Expect(failures,
            sim.World.GetValue(tc, SimComponents.Garrison, SimComponents.Capacity)
                == BuildingTypes.GarrisonCapacity(BuildingTypes.TownCenter),
            "town center garrison capacity by type");

        long archer = sim.SpawnUnit(8, 3, 0, UnitDefs.Archer);
        Expect(failures, sim.IssueGarrison(archer, tc), "own unit can garrison");
        Expect(failures,
            !sim.World.HasComponent(archer, SimComponents.Position),
            "garrisoned unit leaves world position");
        Expect(failures,
            sim.World.HasComponent(archer, SimComponents.Garrisoned),
            "garrisoned marker attached");
        Expect(failures,
            sim.World.GetValue(tc, SimComponents.Garrison, SimComponents.Count) == 1,
            "garrison count increments");
        Expect(failures,
            sim.World.GetValue(tc, SimComponents.Garrison, SimComponents.BonusDamage) == 2,
            "ranged garrison adds double damage bonus");

        long enemy = sim.SpawnUnit(7, 2, 1, UnitDefs.Archer, CombatStance.NoAttack);
        long hpBefore = sim.World.GetValue(enemy, SimComponents.Health, SimComponents.Hp);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            !sim.World.IsAlive(enemy)
                || sim.World.GetValue(enemy, SimComponents.Health, SimComponents.Hp) < hpBefore,
            "garrisoned town center fires on nearby enemy");

        Expect(failures, sim.IssueUngarrison(archer), "ungarrison succeeds");
        Expect(failures,
            sim.World.HasComponent(archer, SimComponents.Position),
            "ungarrison restores world position");
        Expect(failures,
            sim.World.GetValue(tc, SimComponents.Garrison, SimComponents.Count) == 0,
            "garrison count decrements on release");
        Expect(failures,
            !sim.World.HasComponent(archer, SimComponents.Garrisoned),
            "garrisoned marker clears on release");

        long tower = sim.IssuePlaceBuilding(BuildingTypes.Tower, 15, 2, 0);
        BuildingSystem.Complete(sim.World, tower);
        long capacity = BuildingTypes.GarrisonCapacity(BuildingTypes.Tower);
        int garrisoned = 0;
        for (int index = 0; index < capacity + 1; index++)
        {
            long unit = sim.SpawnUnit(14, 8 + index, 0, UnitDefs.Swordsman);
            if (sim.IssueGarrison(unit, tower))
            {
                garrisoned += 1;
            }
        }
        Expect(failures, garrisoned == capacity, "tower rejects garrison beyond capacity");
        Expect(failures,
            sim.World.GetValue(tower, SimComponents.Garrison, SimComponents.Count) == capacity,
            "tower garrison count capped at capacity");

        var bell = new GameSim(new NavGrid(24, 24), 602, 2, 6);
        bell.AddResource(0, GameSim.ResourceWood, 2000);
        bell.AddResource(0, GameSim.ResourceStone, 2000);
        long bellTc = bell.IssuePlaceBuilding(BuildingTypes.TownCenter, 2, 2, 0);
        BuildingSystem.Complete(bell.World, bellTc);
        long node = bell.SpawnResourceNode(12, 12, GameSim.ResourceWood, 500);
        long drop = bell.SpawnDropSite(10, 10, 0b1111);
        var villagers = new long[3];
        for (int index = 0; index < villagers.Length; index++)
        {
            villagers[index] = bell.SpawnVillager(9 + index, 9, 0, 0.2, 10, 1.0);
            bell.IssueGather(villagers[index], node, drop);
        }
        Expect(failures, bell.IssueTownBell(0, false) == villagers.Length, "town bell summons all villagers");
        Expect(failures,
            bell.World.GetValue(bellTc, SimComponents.Garrison, SimComponents.Count) == villagers.Length,
            "town bell fills the town center");
        bool hidden = true;
        foreach (long villager in villagers)
        {
            hidden &= !bell.World.HasComponent(villager, SimComponents.Position);
        }
        Expect(failures, hidden, "town bell villagers leave the world");
        Expect(failures, bell.IssueTownBell(0, true) == villagers.Length, "town bell dismiss releases villagers");
        bool restored = true;
        foreach (long villager in villagers)
        {
            restored &= bell.World.HasComponent(villager, SimComponents.Position)
                && bell.World.HasComponent(villager, SimComponents.Gather);
        }
        Expect(failures, restored, "town bell dismiss restores position and gather work");
        Expect(failures,
            bell.World.GetValue(bellTc, SimComponents.Garrison, SimComponents.Count) == 0,
            "town bell dismiss empties the town center");
    }
}
