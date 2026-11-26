namespace AoeSim;

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

    public SimWorld World { get; }
    public long Entity { get; }
    public MatchConfig Config { get; }

    private readonly long[] _playerEntities;

    private MatchState(SimWorld world, long entity, long[] playerEntities, MatchConfig config)
    {
        World = world;
        Entity = entity;
        _playerEntities = playerEntities;
        Config = config;
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

    /// <summary>
    /// 读档专用：不新建实体，只在已恢复的 SimWorld 里找回对局实体与各玩家状态实体，
    /// 重建 MatchState 包装。找不到对局实体时返回 null。
    /// </summary>
    public static MatchState? Rebind(SimWorld world, long playerCount, MatchConfig config)
    {
        long matchEntity = -1;
        ComponentStore matchStore = world.Store(SimComponents.Match);
        ComponentStore playerStore = world.Store(SimComponents.PlayerState);
        var playerEntities = new long[playerCount];
        for (long team = 0; team < playerCount; team++)
        {
            playerEntities[team] = -1;
        }
        foreach (long id in world.Entities())
        {
            if (matchEntity < 0 && matchStore.Has(id))
            {
                matchEntity = id;
            }
            if (playerStore.Has(id))
            {
                long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
                if (team >= 0 && team < playerCount)
                {
                    playerEntities[team] = id;
                }
            }
        }
        if (matchEntity < 0)
        {
            return null;
        }
        return new MatchState(world, matchEntity, playerEntities, config);
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
