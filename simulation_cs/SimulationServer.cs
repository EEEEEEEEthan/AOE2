using System.Collections.Generic;

namespace AoeSim;

/// <summary>录像录制挂钩：服务器在指令通过校验并进入回合队列后回调，越权/被拒指令不会写入。</summary>
public interface ICommandRecorder
{
    void Record(long turn, LockstepCommand command);
}

/// <summary>
/// 权威服务器：每 tick 推进模拟，按 <see cref="CommandFrame.Turn"/> 收集各玩家输入，在回合边界统一应用。
/// 玩家身份由连接层绑定，<see cref="CommandFrame.Player"/> 与指令自报的 Player 都不被信任；
/// 越权/过期/非法指令直接丢弃并计数，不进入模拟。
/// </summary>
public sealed class SimulationServer
{
    /// <summary>允许客户端提前提交的回合数上限，避免伪造超远回合占满内部队列。</summary>
    private const int TurnLookahead = 8;

    private readonly Dictionary<long, List<LockstepCommand>[]> _turns = new();
    private readonly List<ComputerPlayer> _computerPlayers = new();
    private readonly object _gate = new();
    private long _rejectedCommands;
    private long _acceptedCommands;
    private int _turnTicks;

    public GameSim Sim { get; }
    public long ViewTeam { get; set; }

    /// <summary>可选录像录制器：只接收通过归属/回合校验的指令。</summary>
    public ICommandRecorder? Recorder { get; set; }

    public long Turn
    {
        get
        {
            lock (_gate)
            {
                return Sim.Lockstep.Turn;
            }
        }
    }

    /// <summary>运行期间被服务器拒绝的指令数（越权、过期、非法或执行时实体已失效）。</summary>
    public long RejectedCommands
    {
        get
        {
            lock (_gate)
            {
                return _rejectedCommands;
            }
        }
    }

    /// <summary>已通过归属校验并进入回合队列的指令数。</summary>
    public long AcceptedCommands
    {
        get
        {
            lock (_gate)
            {
                return _acceptedCommands;
            }
        }
    }

    public SimulationServer(GameSim sim)
    {
        Sim = sim;
    }

    /// <summary>挂载电脑玩家；同队已存在则忽略，按队伍升序在回合边界依次决策。</summary>
    public void AddComputerPlayer(ComputerPlayer player)
    {
        lock (_gate)
        {
            if (player.Team < 0 || player.Team >= Sim.Lockstep.PlayerCount)
            {
                return;
            }
            foreach (ComputerPlayer existing in _computerPlayers)
            {
                if (existing.Team == player.Team)
                {
                    return;
                }
            }
            _computerPlayers.Add(player);
            _computerPlayers.Sort((left, right) => left.Team.CompareTo(right.Team));
        }
    }

    public int ComputerPlayerCount
    {
        get
        {
            lock (_gate)
            {
                return _computerPlayers.Count;
            }
        }
    }

    /// <summary>本地/可信调用：以帧里声明的 Player 作为来源玩家。</summary>
    public void Enqueue(CommandFrame frame) => Enqueue(frame, frame.Player);

    /// <summary>
    /// 连接层入口：来源玩家由服务器按连接分配，帧与指令自报的 Player 一律被覆写。
    /// 当前回合已开始过的帧顺延到下一回合；早于当前回合的帧整帧丢弃。
    /// </summary>
    public void Enqueue(CommandFrame frame, long sourcePlayer)
    {
        lock (_gate)
        {
            if (sourcePlayer < 0 || sourcePlayer >= Sim.Lockstep.PlayerCount)
            {
                _rejectedCommands += frame.Commands.Length;
                return;
            }
            long current = Sim.Lockstep.Turn;
            if (frame.Turn < current || frame.Turn > current + TurnLookahead)
            {
                _rejectedCommands += frame.Commands.Length;
                return;
            }
            long target = frame.Turn == current && _turnTicks > 0 ? current + 1 : frame.Turn;
            List<LockstepCommand>[] slots = SlotsFor(target);
            foreach (LockstepCommand command in frame.Commands)
            {
                command.Player = sourcePlayer;
                if (Sim.IsCommandAllowed(command))
                {
                    slots[sourcePlayer].Add(command);
                    _acceptedCommands += 1;
                    Recorder?.Record(target, command);
                }
                else
                {
                    _rejectedCommands += 1;
                }
            }
        }
    }

    /// <summary>可信的本地批量入口，玩家取指令自身的 Player。</summary>
    public void Enqueue(IEnumerable<LockstepCommand> commands)
    {
        lock (_gate)
        {
            long current = Sim.Lockstep.Turn;
            long target = _turnTicks > 0 ? current + 1 : current;
            foreach (LockstepCommand command in commands)
            {
                if (command.Player < 0 || command.Player >= Sim.Lockstep.PlayerCount
                    || !Sim.IsCommandAllowed(command))
                {
                    _rejectedCommands += 1;
                    continue;
                }
                SlotsFor(target)[command.Player].Add(command);
                _acceptedCommands += 1;
                Recorder?.Record(target, command);
            }
        }
    }

    /// <summary>回放专用：把录像里已通过校验的指令直接放进指定回合，不再重复校验。</summary>
    public void EnqueueReplay(long turn, LockstepCommand command)
    {
        lock (_gate)
        {
            if (command.Player < 0 || command.Player >= Sim.Lockstep.PlayerCount)
            {
                return;
            }
            SlotsFor(turn)[command.Player].Add(command);
        }
    }

    /// <summary>推进一个模拟 tick；到回合边界时应用该回合已收到的输入并递增回合号。</summary>
    public long Step()
    {
        lock (_gate)
        {
            if (_turnTicks == 0)
            {
                RunComputerPlayers(Sim.Lockstep.Turn);
                ApplyTurnCommands(Sim.Lockstep.Turn);
            }
            Sim.SimulateTick();
            _turnTicks += 1;
            if (_turnTicks >= Sim.Lockstep.TicksPerTurn)
            {
                _turnTicks = 0;
                Sim.Lockstep.AdvanceTurn();
                PrunePastTurns(Sim.Lockstep.Turn);
            }
            return Sim.World.Checksum();
        }
    }

    public SnapshotFrame Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshot(ViewTeam);
        }
    }

    /// <summary>按指定观察方裁剪快照：网络连接各自传自己的 team，互不依赖共享可变状态。</summary>
    public SnapshotFrame Snapshot(long viewerTeam)
    {
        lock (_gate)
        {
            return BuildSnapshot(viewerTeam);
        }
    }

    /// <summary>同一 tick 的全量快照（不裁剪视野），供多观察者共享构建后按队伍过滤。</summary>
    public SnapshotFrame SnapshotFull()
    {
        lock (_gate)
        {
            return SnapshotFrame.Build(Sim.World, Sim.Lockstep.Turn, Sim.Match);
        }
    }

    /// <summary>复用全量快照，仅按观察方裁剪实体并附上其视野位图。</summary>
    public SnapshotFrame SnapshotFor(SnapshotFrame full, long viewerTeam)
    {
        lock (_gate)
        {
            VisionGrid? vision = Sim.VisionFor(viewerTeam);
            return vision == null
                ? full
                : SnapshotFrame.Filter(Sim.World, full, vision, viewerTeam);
        }
    }

    private SnapshotFrame BuildSnapshot(long viewerTeam) =>
        SnapshotFrame.Build(Sim.World, Sim.Lockstep.Turn, Sim.Match, Sim.VisionFor(viewerTeam), viewerTeam);

    public long Checksum()
    {
        lock (_gate)
        {
            return Sim.World.Checksum();
        }
    }

    /// <summary>回合开始时让各电脑玩家决策，指令仍走归属校验与录像录制路径。</summary>
    private void RunComputerPlayers(long turn)
    {
        if (_computerPlayers.Count == 0 || (Sim.Match != null && Sim.Match.Ended))
        {
            return;
        }
        foreach (ComputerPlayer player in _computerPlayers)
        {
            List<LockstepCommand> commands = player.Think(turn);
            if (commands.Count == 0)
            {
                continue;
            }
            Enqueue(
                new CommandFrame { Player = player.Team, Turn = turn, Commands = commands.ToArray() },
                player.Team);
        }
    }

    /// <summary>按玩家升序应用本回合输入，保证同一回合内的指令顺序确定。</summary>
    private void ApplyTurnCommands(long turn)
    {
        if (!_turns.Remove(turn, out List<LockstepCommand>[]? slots))
        {
            return;
        }
        foreach (List<LockstepCommand> commands in slots)
        {
            foreach (LockstepCommand command in commands)
            {
                if (!Sim.ApplyCommand(command))
                {
                    _rejectedCommands += 1;
                }
            }
        }
    }

    private List<LockstepCommand>[] SlotsFor(long turn)
    {
        if (!_turns.TryGetValue(turn, out List<LockstepCommand>[]? slots))
        {
            slots = new List<LockstepCommand>[Sim.Lockstep.PlayerCount];
            for (int player = 0; player < slots.Length; player++)
            {
                slots[player] = new List<LockstepCommand>();
            }
            _turns[turn] = slots;
        }
        return slots;
    }

    /// <summary>丢弃在回合号已推进后仍残留的过期队列（正常路径下已由回合边界消费）。</summary>
    private void PrunePastTurns(long currentTurn)
    {
        if (_turns.Count == 0)
        {
            return;
        }
        List<long> stale = new();
        foreach (long turn in _turns.Keys)
        {
            if (turn < currentTurn)
            {
                stale.Add(turn);
            }
        }
        foreach (long turn in stale)
        {
            _turns.Remove(turn);
        }
    }
}
