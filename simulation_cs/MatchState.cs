namespace AoeSim;

/// <summary>一次击杀归属：在伤害结算时捕获双方队伍，避免停用时实体解散后丢失阵营信息。</summary>
public readonly struct KillEvent
{
    public readonly long Victim;
    public readonly long KillerTeam;
    public readonly long VictimTeam;

    public KillEvent(long victim, long killerTeam, long victimTeam)
    {
        Victim = victim;
        KillerTeam = killerTeam;
        VictimTeam = victimTeam;
    }
}

[System.Flags]
public enum VictoryCondition
{
    None = 0,
    Conquest = 1,
    Regicide = 2,
    Wonder = 4,
    Relic = 8,
    Score = 16,
}

public static class VictoryReason
{
    public const long None = -1;
    public const long Conquest = 0;
    public const long Regicide = 1;
    public const long Wonder = 2;
    public const long Relic = 3;
    public const long Score = 4;
    public const long Surrender = 5;
    public const long Draw = 6;
}

public sealed class MatchConfig
{
    public VictoryCondition Rules { get; init; } = VictoryCondition.Conquest;
    public long WonderHoldTicks { get; init; } = 900;
    public long RelicHoldTicks { get; init; } = 900;
    public long ScoreTickLimit { get; init; } = 27000;

    public static MatchConfig Standard => new()
    {
        Rules = VictoryCondition.Conquest
            | VictoryCondition.Regicide
            | VictoryCondition.Wonder
            | VictoryCondition.Relic
            | VictoryCondition.Score,
    };
}

/// <summary>对局状态存放在 ECS 世界内，随全局校验和与存档一起确定性推进。</summary>
public sealed class MatchState
{
    public const long PhaseSetup = 0;
    public const long PhaseRunning = 1;
    public const long PhaseEnded = 2;

    /// <summary>分数曲线采样间隔（tick）与容量上限；超限时按整倍下采样，保持曲线平滑。</summary>
    public const long HistoryInitialInterval = 300;
    public const int HistoryMaxSamples = 120;

    public SimWorld World { get; }
    public long Entity { get; }
    public MatchConfig Config { get; }

    /// <summary>结算统计：逐队累计击杀、损失与资源采集量，以及四项分数分项。</summary>
    public long[] Kills { get; }
    public long[] Losses { get; }
    public long[] Gathered { get; }
    public long[] SocietyScore { get; }
    public long[] EconomyScore { get; }
    public long[] MilitaryScore { get; }
    public long[] TechScore { get; }

    /// <summary>分数曲线：按固定间隔采样总分，随快照下发供结算界面绘制。</summary>
    public long HistoryInterval { get; internal set; } = HistoryInitialInterval;
    public System.Collections.Generic.List<long> HistoryTicks { get; } = new();
    public System.Collections.Generic.List<long[]> HistoryScores { get; } = new();

    private readonly long[] _playerEntities;

    private MatchState(SimWorld world, long entity, long[] playerEntities, MatchConfig config)
    {
        World = world;
        Entity = entity;
        _playerEntities = playerEntities;
        Config = config;
        int count = playerEntities.Length;
        Kills = new long[count];
        Losses = new long[count];
        Gathered = new long[count];
        SocietyScore = new long[count];
        EconomyScore = new long[count];
        MilitaryScore = new long[count];
        TechScore = new long[count];
    }

    public static MatchState Create(SimWorld world, long playerCount, MatchConfig config)
    {
        long entity = world.Spawn();
        world.Attach(entity, SimComponents.Match);
        world.SetValue(entity, SimComponents.Match, SimComponents.Phase, PhaseSetup);
        world.SetValue(entity, SimComponents.Match, SimComponents.WinnerTeam, -1);
        world.SetValue(entity, SimComponents.Match, SimComponents.VictoryReason, VictoryReason.None);
        world.SetValue(entity, SimComponents.Match, SimComponents.EndTick, -1);
        world.SetValue(entity, SimComponents.Match, SimComponents.RuleMask, (long)config.Rules);
        world.SetValue(entity, SimComponents.Match, SimComponents.WonderHoldTicks, config.WonderHoldTicks);
        world.SetValue(entity, SimComponents.Match, SimComponents.RelicHoldTicks, config.RelicHoldTicks);
        world.SetValue(entity, SimComponents.Match, SimComponents.ScoreTickLimit, config.ScoreTickLimit);
        var playerEntities = new long[playerCount];
        for (long team = 0; team < playerCount; team++)
        {
            long playerEntity = world.Spawn();
            world.Attach(playerEntity, SimComponents.PlayerState);
            world.Attach(playerEntity, SimComponents.Team);
            world.SetValue(playerEntity, SimComponents.Team, SimComponents.TeamId, team);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.Defeated, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.Winner, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.Score, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.Participants, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.WonderHold, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.RelicHold, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.HadKing, 0);
            world.SetValue(playerEntity, SimComponents.PlayerState, SimComponents.Surrendered, 0);
            playerEntities[team] = playerEntity;
        }
        return new MatchState(world, entity, playerEntities, config);
    }

    public int PlayerCount => _playerEntities.Length;

    public long Phase
    {
        get => World.GetValue(Entity, SimComponents.Match, SimComponents.Phase);
        internal set => World.SetValue(Entity, SimComponents.Match, SimComponents.Phase, value);
    }

    public long WinnerTeam
    {
        get => World.GetValue(Entity, SimComponents.Match, SimComponents.WinnerTeam);
        private set => World.SetValue(Entity, SimComponents.Match, SimComponents.WinnerTeam, value);
    }

    public long Reason
    {
        get => World.GetValue(Entity, SimComponents.Match, SimComponents.VictoryReason);
        private set => World.SetValue(Entity, SimComponents.Match, SimComponents.VictoryReason, value);
    }

    public long EndTick
    {
        get => World.GetValue(Entity, SimComponents.Match, SimComponents.EndTick);
        private set => World.SetValue(Entity, SimComponents.Match, SimComponents.EndTick, value);
    }

    public bool Ended => Phase == PhaseEnded;

    public PlayerState Player(long team) => new(World, _playerEntities[team], team);

    public bool AnyPlayerHadKing()
    {
        for (long team = 0; team < _playerEntities.Length; team++)
        {
            if (Player(team).HadKing)
            {
                return true;
            }
        }
        return false;
    }

    public bool AnyPlayerSurrendered()
    {
        for (long team = 0; team < _playerEntities.Length; team++)
        {
            if (Player(team).Surrendered)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>记一次击杀胜负：击杀方 +1 击杀，被击杀方 +1 损失；无关实体的队伍已在上游过滤。</summary>
    public void RecordKill(long killerTeam, long victimTeam)
    {
        if (killerTeam >= 0 && killerTeam < Kills.Length)
        {
            Kills[killerTeam] += 1;
        }
        if (victimTeam >= 0 && victimTeam < Losses.Length)
        {
            Losses[victimTeam] += 1;
        }
    }

    public void RecordGathered(long team, long amount)
    {
        if (team >= 0 && team < Gathered.Length && amount > 0)
        {
            Gathered[team] += amount;
        }
    }

    /// <summary>
    /// 按固定间隔把各方当前总分压入曲线。非强制的非采样 tick 直接返回；
    /// 样本超容量时移除奇数下标并让采样间隔翻倍，曲线仍覆盖全局时间且长度有界。
    /// </summary>
    public void SampleScoreHistory(long tick, bool force)
    {
        if (!force && (HistoryInterval <= 0 || tick % HistoryInterval != 0))
        {
            return;
        }
        if (HistoryTicks.Count > 0 && HistoryTicks[HistoryTicks.Count - 1] == tick)
        {
            return;
        }
        var scores = new long[_playerEntities.Length];
        for (long team = 0; team < _playerEntities.Length; team++)
        {
            scores[team] = Player(team).Score;
        }
        HistoryTicks.Add(tick);
        HistoryScores.Add(scores);
        while (HistoryTicks.Count > HistoryMaxSamples)
        {
            DownsampleHistory();
        }
    }

    private void DownsampleHistory()
    {
        int kept = (HistoryTicks.Count + 1) / 2;
        var ticks = new System.Collections.Generic.List<long>(kept);
        var scores = new System.Collections.Generic.List<long[]>(kept);
        for (int index = 0; index < HistoryTicks.Count; index += 2)
        {
            ticks.Add(HistoryTicks[index]);
            scores.Add(HistoryScores[index]);
        }
        if (HistoryTicks.Count > 0 && (HistoryTicks.Count - 1) % 2 != 0)
        {
            ticks[ticks.Count - 1] = HistoryTicks[HistoryTicks.Count - 1];
            scores[scores.Count - 1] = HistoryScores[HistoryScores.Count - 1];
        }
        HistoryTicks.Clear();
        HistoryTicks.AddRange(ticks);
        HistoryScores.Clear();
        HistoryScores.AddRange(scores);
        HistoryInterval *= 2;
    }

    internal void End(long winnerTeam, long reason)
    {
        Phase = PhaseEnded;
        WinnerTeam = winnerTeam;
        Reason = reason;
        EndTick = World.Tick;
        for (long team = 0; team < _playerEntities.Length; team++)
        {
            Player(team).Winner = winnerTeam >= 0 && team == winnerTeam;
        }
    }
}

public sealed class PlayerState
{
    private readonly SimWorld _world;

    public PlayerState(SimWorld world, long entity, long team)
    {
        _world = world;
        Entity = entity;
        Team = team;
    }

    public long Entity { get; }
    public long Team { get; }

    private long Read(string column) => _world.GetValue(Entity, SimComponents.PlayerState, column);

    private void Write(string column, long value) =>
        _world.SetValue(Entity, SimComponents.PlayerState, column, value);

    public bool Defeated
    {
        get => Read(SimComponents.Defeated) != 0;
        set => Write(SimComponents.Defeated, value ? 1 : 0);
    }

    public bool Winner
    {
        get => Read(SimComponents.Winner) != 0;
        set => Write(SimComponents.Winner, value ? 1 : 0);
    }

    public long Score
    {
        get => Read(SimComponents.Score);
        set => Write(SimComponents.Score, value);
    }

    public long Participants
    {
        get => Read(SimComponents.Participants);
        set => Write(SimComponents.Participants, value);
    }

    public long WonderHold
    {
        get => Read(SimComponents.WonderHold);
        set => Write(SimComponents.WonderHold, value);
    }

    public long RelicHold
    {
        get => Read(SimComponents.RelicHold);
        set => Write(SimComponents.RelicHold, value);
    }

    public bool HadKing
    {
        get => Read(SimComponents.HadKing) != 0;
        set => Write(SimComponents.HadKing, value ? 1 : 0);
    }

    public bool Surrendered
    {
        get => Read(SimComponents.Surrendered) != 0;
        set => Write(SimComponents.Surrendered, value ? 1 : 0);
    }
}
