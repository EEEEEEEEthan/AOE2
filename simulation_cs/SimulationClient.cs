namespace AoeSim;

public sealed class SimulationClient
{
    public SnapshotFrame? Latest { get; private set; }

    /// <summary>连接建立时下发的静态地图；快照不再携带地形，渲染地形时读这里。</summary>
    public StaticMapFrame? Map { get; private set; }

    public void ApplySnapshot(byte[] encodedSnapshot) =>
        Latest = SnapshotFrame.Decode(encodedSnapshot);

    public void ApplySnapshot(SnapshotFrame snapshot) => Latest = snapshot;

    public void ApplyStaticMap(byte[] encodedMap) => Map = StaticMapFrame.Decode(encodedMap);

    public void ApplyStaticMap(StaticMapFrame map) => Map = map;

    public long Tick => Latest?.Tick ?? -1;

    public int EntityCount => Latest?.Entities.Length ?? 0;
}
