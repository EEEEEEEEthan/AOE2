namespace AoeSim;

public sealed class SimulationClient
{
    public SnapshotFrame? Latest { get; private set; }

    /// <summary>连接建立时下发的静态地图；快照不再携带地形，渲染地形时读这里。</summary>
    public StaticMapFrame? Map { get; private set; }

    /// <summary>解码并按需把增量帧合并到上一帧：关键帧直接替换，增量帧在既有完整状态上叠加。</summary>
    public void ApplySnapshot(byte[] encodedSnapshot)
    {
        SnapshotFrame? previous = Latest;
        SnapshotFrame frame = SnapshotFrame.Decode(encodedSnapshot, previous);
        Latest = frame.Delta && previous != null ? frame.Merge(previous) : frame;
    }

    public void ApplySnapshot(SnapshotFrame snapshot) => Latest = snapshot;

    public void ApplyStaticMap(byte[] encodedMap) => Map = StaticMapFrame.Decode(encodedMap);

    public void ApplyStaticMap(StaticMapFrame map) => Map = map;

    public long Tick => Latest?.Tick ?? -1;

    public int EntityCount => Latest?.Entities.Length ?? 0;
}
