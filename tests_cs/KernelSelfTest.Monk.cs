using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>僧侣治疗/转化与圣物语义。</summary>
public static partial class KernelSelfTest
{
    private static void CheckMonk(List<string> failures)
    {
        var heal = new GameSim(new NavGrid(16, 16), 101);
        long healer = heal.SpawnMonk(5, 5, 0, 60, 0.0, 10, 10, 30, 1.5, 0);
        long ally = heal.SpawnUnit(5, 6, 0, 100, 0, 0.0, 0.0, 0.0);
        heal.World.SetValue(ally, SimComponents.Health, SimComponents.Hp, 40);
        heal.IssueHeal(healer, ally);
        for (int tick = 0; tick < 3; tick++)
        {
            heal.SimulateTick();
        }
        Expect(failures,
            heal.World.GetValue(ally, SimComponents.Health, SimComponents.Hp) == 70,
            "monk heals friendly up to max");
        for (int tick = 0; tick < 20; tick++)
        {
            heal.SimulateTick();
        }
        Expect(failures,
            heal.World.GetValue(ally, SimComponents.Health, SimComponents.Hp) == 100,
            "monk heal capped at max hp");

        long enemy = heal.SpawnUnit(6, 5, 1, 100, 0, 0.0, 0.0, 0.0);
        heal.World.SetValue(enemy, SimComponents.Health, SimComponents.Hp, 40);
        heal.IssueHeal(healer, enemy);
        Expect(failures,
            heal.World.GetValue(healer, SimComponents.Monk, SimComponents.State) == MonkSystem.StateIdle,
            "heal ignores enemy target");

        var convert = new GameSim(new NavGrid(16, 16), 102);
        long monk = convert.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long victim = convert.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0, 0.0);
        convert.IssueConvert(monk, victim);
        for (int tick = 0; tick < 3; tick++)
        {
            convert.SimulateTick();
        }
        Expect(failures,
            convert.World.GetValue(victim, SimComponents.Team, SimComponents.TeamId) == 0,
            "convert flips target team at threshold");
        Expect(failures,
            convert.World.GetValue(monk, SimComponents.Monk, SimComponents.State) == MonkSystem.StateIdle,
            "convert resets monk after success");

        var resist = new GameSim(new NavGrid(16, 16), 103);
        long converter = resist.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long resistant = resist.SpawnMonk(5, 6, 1, 60, 0.0, 0, 10, 30, 1.5, 25);
        resist.IssueConvert(converter, resistant);
        for (int tick = 0; tick < 5; tick++)
        {
            resist.SimulateTick();
        }
        Expect(failures,
            resist.World.GetValue(resistant, SimComponents.Team, SimComponents.TeamId) == 1,
            "conversion resistance slows conversion");
        for (int tick = 0; tick < 40; tick++)
        {
            resist.SimulateTick();
        }
        Expect(failures,
            resist.World.GetValue(resistant, SimComponents.Team, SimComponents.TeamId) == 0,
            "resistant target still converts eventually");

        var interrupt = new GameSim(new NavGrid(16, 16), 104);
        long damaged = interrupt.SpawnMonk(5, 5, 0, 60, 0.0, 0, 10, 30, 1.5, 0);
        long prey = interrupt.SpawnUnit(5, 6, 1, 100, 0, 0.0, 0.0, 0.0);
        interrupt.IssueConvert(damaged, prey);
        interrupt.SimulateTick();
        long progressBefore = interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress);
        interrupt.World.SetValue(damaged, SimComponents.Health, SimComponents.Hp, 50);
        interrupt.SimulateTick();
        long progressAfterHit = interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress);
        Expect(failures, progressBefore == 10, "convert progress accumulates");
        Expect(failures, progressAfterHit == 10, "monk damage resets convert progress");

        interrupt.World.SetValue(
            prey,
            SimComponents.Position,
            SimComponents.X,
            AoeSim.Fixed.FromInt(14));
        interrupt.SimulateTick();
        Expect(failures,
            interrupt.World.GetValue(damaged, SimComponents.Monk, SimComponents.ConvertProgress) == 0,
            "target leaving range resets convert progress");

        Expect(failures, MonkDeterminismChecksum() == MonkDeterminismChecksum(), "monk behavior deterministic");
    }

    private static long MonkDeterminismChecksum()
    {
        var sim = new GameSim(new NavGrid(20, 20), 111, 2, 6);
        long first = sim.SpawnMonk(4, 5, 0, 60, 0.1, 5, 6, 24, 2.0, 0);
        long second = sim.SpawnMonk(4, 8, 0, 60, 0.1, 5, 6, 24, 2.0, 0);
        long patient = sim.SpawnUnit(5, 5, 0, 100, 0, 0.0, 0.0, 0.0);
        sim.World.SetValue(patient, SimComponents.Health, SimComponents.Hp, 30);
        long foe = sim.SpawnUnit(10, 8, 1, 100, 0, 0.0, 0.0, 0.0);
        sim.IssueHeal(first, patient);
        sim.IssueConvert(second, foe);
        long checksum = 0;
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
            checksum = sim.World.Checksum();
        }
        return checksum;
    }

    private static void CheckRelic(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 201);
        long slot = sim.SpawnRelicSlot(7, 5, 0, 1, 5);
        long relic = sim.SpawnRelic(5, 6);
        long monk = sim.SpawnMonk(5, 5, 0, 60, 0.5, 0, 0, 0, 2.0, 0);
        sim.IssuePickupRelic(monk, relic);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) == monk,
            "monk picks up relic");
        Expect(failures, sim.RelicCountOf(0) == 1, "carried relic counts for team");
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Position, SimComponents.X)
                == sim.World.GetValue(monk, SimComponents.Position, SimComponents.X),
            "carried relic follows monk position");

        sim.IssueDepositRelic(monk, slot);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.StoredIn) == slot,
            "monk deposits relic into slot");
        Expect(failures,
            sim.World.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) == -1,
            "deposited relic no longer carried");
        Expect(failures, sim.RelicCountOf(0) == 1, "stored relic counts for slot team");

        long goldBefore = sim.Resource(0, GameSim.ResourceGold);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        long goldAfter = sim.Resource(0, GameSim.ResourceGold);
        Expect(failures, goldAfter - goldBefore == 2, "stored relic generates gold per period");

        long second = sim.SpawnRelic(6, 6);
        sim.World.SetValue(second, SimComponents.Relic, SimComponents.Carrier, -1);
        sim.World.SetValue(second, SimComponents.Relic, SimComponents.StoredIn, slot);
        sim.SimulateTick();
        Expect(failures, sim.RelicCountOf(0) == 2, "multiple relics counted");
        long goldTwoBefore = sim.Resource(0, GameSim.ResourceGold);
        for (int tick = 0; tick < 5; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.Resource(0, GameSim.ResourceGold) - goldTwoBefore == 2,
            "gold generation stacks linearly with relics");
    }
}
