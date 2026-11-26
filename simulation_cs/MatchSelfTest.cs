using System.Collections.Generic;
using AoeSim;
using Godot;

namespace AOE2Framework;

/// <summary>胜负条件与对局流程的语义断言；返回空数组表示全过。</summary>
public static class MatchSelfTest
{
    public static Godot.Collections.Array<string> Run()
    {
        var failures = new List<string>();
        CheckPhases(failures);
        CheckConquest(failures);
        CheckRegicide(failures);
        CheckWonder(failures);
        CheckRelic(failures);
        CheckScore(failures);
        CheckSurrender(failures);
        CheckDraw(failures);
        CheckDeterminism(failures);
        var result = new Godot.Collections.Array<string>();
        foreach (string failure in failures)
        {
            result.Add(failure);
        }
        return result;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static MatchConfig RulesOnly(VictoryCondition condition, long holdTicks = 5, long scoreLimit = 27000) => new()
    {
        Rules = condition,
        WonderHoldTicks = holdTicks,
        RelicHoldTicks = holdTicks,
        ScoreTickLimit = scoreLimit,
    };

    private static void CheckPhases(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 1, 2, 6, RulesOnly(VictoryCondition.Conquest));
        sim.SpawnUnit(1, 1, 0, 50, 0, 0.0, 0.0);
        sim.SpawnUnit(14, 14, 1, 50, 0, 0.0, 0.0);
        Expect(failures, sim.Match != null && sim.Match.Phase == MatchState.PhaseSetup, "match starts in setup phase");
        sim.SimulateTick();
        Expect(failures, sim.Match != null && sim.Match.Phase == MatchState.PhaseRunning, "match enters running on first tick");
        Expect(failures, sim.Match != null && !sim.Match.Ended, "live match not ended");
    }

    private static void CheckConquest(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 2, 2, 6, RulesOnly(VictoryCondition.Conquest));
        sim.SpawnUnit(5, 5, 0, 100, 20, 1.5, 0.0);
        sim.SpawnUnit(6, 5, 1, 10, 0, 1.5, 0.0);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Match != null && sim.Match.Ended, "conquest ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "conquest winner is last standing");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Conquest, "conquest reason recorded");
        Expect(failures, sim.Match != null && sim.Match.Player(1).Defeated, "eliminated player defeated");
        Expect(failures, sim.Match != null && sim.Match.Player(1).Participants == 0, "eliminated player has no participants");
    }

    private static void CheckRegicide(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 3, 2, 6, RulesOnly(VictoryCondition.Regicide));
        sim.SpawnUnit(4, 5, 0, 100, 20, 2.0, 0.0);
        sim.SpawnKing(5, 5, 1, 10, 0, 0.0, 0.0);
        sim.SpawnUnit(12, 12, 1, 100, 0, 0.0, 0.0);
        for (int tick = 0; tick < 10; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Match != null && sim.Match.Ended, "regicide ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "regicide winner is surviving king side");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Regicide, "regicide reason recorded");
        Expect(failures,
            sim.Match != null && sim.Match.Player(1).Defeated && sim.Match.Player(1).Participants > 0,
            "regicide defeat triggers with surviving units");
    }

    private static void CheckWonder(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 4, 2, 6, RulesOnly(VictoryCondition.Wonder, holdTicks: 5));
        long wonder = sim.SpawnWonder(3, 3, 0, 100);
        sim.CompleteWonder(wonder);
        sim.SpawnUnit(12, 12, 1, 100, 0, 0.0, 0.0);
        for (int tick = 0; tick < 8; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Match != null && sim.Match.Ended, "wonder ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "wonder winner is builder");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Wonder, "wonder reason recorded");
    }

    private static void CheckRelic(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 5, 2, 6, RulesOnly(VictoryCondition.Relic, holdTicks: 5));
        sim.SpawnUnit(1, 1, 0, 100, 0, 0.0, 0.0);
        sim.SpawnUnit(12, 12, 1, 100, 0, 0.0, 0.0);
        long firstRelic = sim.SpawnRelic(7, 7);
        long secondRelic = sim.SpawnRelic(8, 8);
        sim.SetRelicHolder(firstRelic, 0);
        sim.SetRelicHolder(secondRelic, 0);
        for (int tick = 0; tick < 8; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Match != null && sim.Match.Ended, "relic ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "relic winner holds all relics");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Relic, "relic reason recorded");
    }

    private static void CheckScore(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 6, 2, 6, RulesOnly(VictoryCondition.Score, scoreLimit: 5));
        sim.SpawnUnit(1, 1, 0, 100, 0, 0.0, 0.0);
        sim.SpawnUnit(12, 12, 1, 100, 0, 0.0, 0.0);
        sim.AddResource(0, GameSim.ResourceWood, 100);
        for (int tick = 0; tick < 8; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, sim.Match != null && sim.Match.Ended, "score limit ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "score winner has higher score");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Score, "score reason recorded");
    }

    private static void CheckSurrender(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 7, 2, 6, RulesOnly(VictoryCondition.Conquest));
        sim.SpawnUnit(1, 1, 0, 100, 0, 0.0, 0.0);
        sim.SpawnUnit(12, 12, 1, 100, 0, 0.0, 0.0);
        sim.Surrender(1);
        sim.SimulateTick();
        Expect(failures, sim.Match != null && sim.Match.Ended, "surrender ends match");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == 0, "surrender hands win to opponent");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Surrender, "surrender reason recorded");
        Expect(failures, sim.Match != null && sim.Match.Player(1).Surrendered, "surrendered flag recorded");
    }

    private static void CheckDraw(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 8, 2, 6, RulesOnly(VictoryCondition.Conquest));
        sim.SimulateTick();
        Expect(failures, sim.Match != null && sim.Match.Ended, "empty match ends");
        Expect(failures, sim.Match != null && sim.Match.WinnerTeam == -1, "empty match has no winner");
        Expect(failures, sim.Match != null && sim.Match.Reason == VictoryReason.Draw, "empty match recorded as draw");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        var first = BuildDeterminismScenario();
        var second = BuildDeterminismScenario();
        for (int tick = 0; tick < 40; tick++)
        {
            first.SimulateTick();
            second.SimulateTick();
        }
        Expect(failures, first.World.Checksum() == second.World.Checksum(), "match state reproducible checksum");
        Expect(failures, first.Match != null && second.Match != null, "match state present");
        Expect(failures,
            first.Match != null && second.Match != null
                && first.Match.Phase == second.Match.Phase
                && first.Match.WinnerTeam == second.Match.WinnerTeam
                && first.Match.Reason == second.Match.Reason,
            "match outcome reproducible");
    }

    private static GameSim BuildDeterminismScenario()
    {
        var sim = new GameSim(
            new NavGrid(16, 16),
            9,
            2,
            6,
            new MatchConfig { Rules = VictoryCondition.Conquest | VictoryCondition.Regicide });
        sim.SpawnUnit(4, 5, 0, 50, 7, 1.5, 0.1);
        sim.SpawnKing(5, 5, 1, 10, 7, 1.5, 0.1);
        sim.SpawnUnit(12, 12, 1, 50, 7, 1.5, 0.1);
        return sim;
    }
}
