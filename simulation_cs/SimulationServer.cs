using System.Collections.Generic;

namespace AoeSim;

public sealed class SimulationServer
{
    private readonly List<LockstepCommand> _pending = new();

    public GameSim Sim { get; }
    public long Turn => Sim.Lockstep.Turn;

    public SimulationServer(GameSim sim)
    {
        Sim = sim;
    }

    public void Enqueue(CommandFrame frame)
    {
        foreach (LockstepCommand command in frame.Commands)
        {
            _pending.Add(command);
        }
    }

    public void Enqueue(IEnumerable<LockstepCommand> commands) => _pending.AddRange(commands);

    public long Step()
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

    public SnapshotFrame Snapshot() => SnapshotFrame.Build(Sim.World, Turn, Sim.Match);

    public long Checksum() => Sim.World.Checksum();
}
