using System.Collections.Generic;

namespace AoeSim;

public sealed class SimulationServer
{
    private readonly List<LockstepCommand> _pending = new();
    private readonly object _gate = new();

    public GameSim Sim { get; }
    public long ViewTeam { get; set; }

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

    public SimulationServer(GameSim sim)
    {
        Sim = sim;
    }

    public void Enqueue(CommandFrame frame)
    {
        lock (_gate)
        {
            foreach (LockstepCommand command in frame.Commands)
            {
                _pending.Add(command);
            }
        }
    }

    public void Enqueue(IEnumerable<LockstepCommand> commands)
    {
        lock (_gate)
        {
            _pending.AddRange(commands);
        }
    }

    public long Step()
    {
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                foreach (LockstepCommand command in _pending)
                {
                    Sim.ApplyCommand(command);
                }
                _pending.Clear();
            }
            Sim.SimulateTick();
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

    private SnapshotFrame BuildSnapshot(long viewerTeam) =>
        SnapshotFrame.Build(Sim.World, Sim.Lockstep.Turn, Sim.Match, Sim.VisionFor(viewerTeam), viewerTeam);

    public long Checksum()
    {
        lock (_gate)
        {
            return Sim.World.Checksum();
        }
    }
}
