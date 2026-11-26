using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>采集经济语义。</summary>
public static partial class KernelSelfTest
{
    private static void CheckGather(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 42);
        long node = sim.SpawnResourceNode(4, 5, GameSim.ResourceWood, 100);
        long drop = sim.SpawnDropSite(10, 5, 0b1111);
        long villager = sim.SpawnVillager(4, 5, 0, 0.2, 4, 1.0);
        sim.IssueGather(villager, node, drop);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        long inventory = sim.Resource(0, GameSim.ResourceWood);
        long carry = sim.World.GetValue(villager, SimComponents.Gather, SimComponents.CarryAmount);
        long remaining = sim.World.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining);
        Expect(failures, inventory > 0, "gather delivers to inventory");
        Expect(failures, inventory + carry + remaining == 100, "gather conserves resources");
    }
}
