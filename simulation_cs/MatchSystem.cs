namespace AoeSim;

/// <summary>每 tick 末尾做一次确定性胜负结算：统计存活、判定淘汰、检查各胜利条件。</summary>
public static class MatchSystem
{
    public static void Step(SimWorld world, MatchState match)
    {
        if (match.Ended)
        {
            return;
        }
        if (match.Phase == MatchState.PhaseSetup)
        {
            match.Phase = MatchState.PhaseRunning;
        }

        int playerCount = match.PlayerCount;
        var participants = new long[playerCount];
        var resources = new long[playerCount];
        var wondersComplete = new bool[playerCount];
        var kingsAlive = new bool[playerCount];
        long relicTotal = 0;
        var relicsHeld = new long[playerCount];

        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            long team = TeamOf(world, id);
            bool hasTeam = team >= 0 && team < playerCount;
            if (hasTeam)
            {
                if (IsParticipant(world, id))
                {
                    participants[team] += 1;
                }
                if (world.HasComponent(id, SimComponents.King))
                {
                    kingsAlive[team] = true;
                }
                if (world.HasComponent(id, SimComponents.Wonder)
                    && world.GetValue(id, SimComponents.Wonder, SimComponents.Complete) != 0)
                {
                    wondersComplete[team] = true;
                }
                if (world.HasComponent(id, SimComponents.Resource))
                {
                    foreach (string column in SimComponents.ResourceColumns)
                    {
                        resources[team] += world.GetValue(id, SimComponents.Resource, column);
                    }
                }
            }
            if (world.HasComponent(id, SimComponents.Relic))
            {
                relicTotal += 1;
                long holder = world.GetValue(id, SimComponents.Relic, SimComponents.HolderTeam);
                if (holder >= 0 && holder < playerCount)
                {
                    relicsHeld[holder] += 1;
                }
            }
        }

        for (long team = 0; team < playerCount; team++)
        {
            PlayerState player = match.Player(team);
            player.Participants = participants[team];
            player.Score = participants[team] * 10 + resources[team];
            if (kingsAlive[team])
            {
                player.HadKing = true;
            }
            if (!player.Defeated)
            {
                bool eliminated = participants[team] == 0;
                bool kingLost = match.Config.Rules.HasFlag(VictoryCondition.Regicide)
                    && player.HadKing
                    && !kingsAlive[team];
                if (eliminated || kingLost)
                {
                    player.Defeated = true;
                }
            }
            player.WonderHold = !player.Defeated
                && wondersComplete[team]
                && match.Config.Rules.HasFlag(VictoryCondition.Wonder)
                    ? player.WonderHold + 1
                    : 0;
            bool holdsAllRelics = relicTotal > 0 && relicsHeld[team] == relicTotal;
            player.RelicHold = !player.Defeated
                && holdsAllRelics
                && match.Config.Rules.HasFlag(VictoryCondition.Relic)
                    ? player.RelicHold + 1
                    : 0;
        }

        ResolveDirectVictory(match);
        if (!match.Ended)
        {
            ResolveLastStanding(match);
        }
        if (!match.Ended)
        {
            ResolveScoreVictory(match);
        }
    }

    private static void ResolveDirectVictory(MatchState match)
    {
        for (long team = 0; team < match.PlayerCount; team++)
        {
            PlayerState player = match.Player(team);
            if (player.Defeated)
            {
                continue;
            }
            if (match.Config.Rules.HasFlag(VictoryCondition.Wonder)
                && match.Config.WonderHoldTicks > 0
                && player.WonderHold >= match.Config.WonderHoldTicks)
            {
                match.End(team, VictoryReason.Wonder);
                return;
            }
            if (match.Config.Rules.HasFlag(VictoryCondition.Relic)
                && match.Config.RelicHoldTicks > 0
                && player.RelicHold >= match.Config.RelicHoldTicks)
            {
                match.End(team, VictoryReason.Relic);
                return;
            }
        }
    }

    private static void ResolveScoreVictory(MatchState match)
    {
        if (!match.Config.Rules.HasFlag(VictoryCondition.Score)
            || match.Config.ScoreTickLimit <= 0
            || match.World.Tick < match.Config.ScoreTickLimit)
        {
            return;
        }
        long bestTeam = -1;
        long bestScore = -1;
        bool tied = false;
        for (long team = 0; team < match.PlayerCount; team++)
        {
            PlayerState player = match.Player(team);
            if (player.Defeated)
            {
                continue;
            }
            if (player.Score > bestScore)
            {
                bestScore = player.Score;
                bestTeam = team;
                tied = false;
            }
            else if (player.Score == bestScore)
            {
                tied = true;
            }
        }
        if (bestTeam < 0)
        {
            match.End(-1, VictoryReason.Draw);
        }
        else if (tied)
        {
            match.End(-1, VictoryReason.Draw);
        }
        else
        {
            match.End(bestTeam, VictoryReason.Score);
        }
    }

    private static void ResolveLastStanding(MatchState match)
    {
        long lastTeam = -1;
        long aliveTeams = 0;
        for (long team = 0; team < match.PlayerCount; team++)
        {
            if (!match.Player(team).Defeated)
            {
                aliveTeams += 1;
                lastTeam = team;
            }
        }
        if (aliveTeams == 0)
        {
            match.End(-1, VictoryReason.Draw);
            return;
        }
        if (aliveTeams != 1)
        {
            return;
        }
        long reason = VictoryReason.None;
        if (match.AnyPlayerSurrendered())
        {
            reason = VictoryReason.Surrender;
        }
        else if (match.Config.Rules.HasFlag(VictoryCondition.Regicide) && match.AnyPlayerHadKing())
        {
            reason = VictoryReason.Regicide;
        }
        else if (match.Config.Rules.HasFlag(VictoryCondition.Conquest))
        {
            reason = VictoryReason.Conquest;
        }
        if (reason != VictoryReason.None)
        {
            match.End(lastTeam, reason);
        }
    }

    private static long TeamOf(SimWorld world, long id) =>
        world.HasComponent(id, SimComponents.Team)
            ? world.GetValue(id, SimComponents.Team, SimComponents.TeamId)
            : -1;

    private static bool IsParticipant(SimWorld world, long id)
    {
        if (world.HasComponent(id, SimComponents.Building))
        {
            return world.GetValue(id, SimComponents.Building, SimComponents.State)
                != BuildingSystem.StateRuins;
        }
        return world.HasComponent(id, SimComponents.Combat)
            || world.HasComponent(id, SimComponents.Gather)
            || world.HasComponent(id, SimComponents.King);
    }
}
