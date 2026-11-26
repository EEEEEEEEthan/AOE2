namespace AoeSim;

public sealed class SimulationClient
{
    public SnapshotFrame? Latest { get; private set; }

    public void ApplySnapshot(byte[] encodedSnapshot) =>
        Latest = SnapshotFrame.Decode(encodedSnapshot);

    public void ApplySnapshot(SnapshotFrame snapshot) => Latest = snapshot;

    public long Tick => Latest?.Tick ?? -1;

    public int EntityCount => Latest?.Entities.Length ?? 0;
}
