using System.Collections.Generic;

namespace AoeSim;

public sealed class SpatialHash
{
    public long CellSize { get; }
    private readonly Dictionary<long, List<long>> _buckets = new();

    public SpatialHash(int cellTiles = 4)
    {
        CellSize = cellTiles * Fixed.Scale;
    }

    public void Clear() => _buckets.Clear();

    public void Insert(long id, long xFixed, long yFixed)
    {
        long key = Key(Coordinate(xFixed), Coordinate(yFixed));
        if (!_buckets.TryGetValue(key, out List<long>? bucket))
        {
            bucket = new List<long>();
            _buckets[key] = bucket;
        }
        bucket.Add(id);
    }

    public void Rebuild(SimWorld world)
    {
        Clear();
        ComponentStore positionStore = world.Store(SimComponents.Position);
        foreach (long id in positionStore.ActiveIds())
        {
            Insert(
                id,
                world.GetValue(id, SimComponents.Position, SimComponents.X),
                world.GetValue(id, SimComponents.Position, SimComponents.Y));
        }
    }

    public List<long> QueryRadius(long xFixed, long yFixed, long radiusFixed)
    {
        var result = new List<long>();
        QueryRadius(xFixed, yFixed, radiusFixed, result);
        return result;
    }

    /// <summary>把半径内候选写入调用方提供的复用缓冲（先清空），热路径用它消除每次分配。</summary>
    public void QueryRadius(long xFixed, long yFixed, long radiusFixed, List<long> result)
    {
        result.Clear();
        if (radiusFixed < 0)
        {
            return;
        }
        long minX = Coordinate(xFixed - radiusFixed);
        long maxX = Coordinate(xFixed + radiusFixed);
        long minY = Coordinate(yFixed - radiusFixed);
        long maxY = Coordinate(yFixed + radiusFixed);
        for (long bucketY = minY; bucketY <= maxY; bucketY++)
        {
            for (long bucketX = minX; bucketX <= maxX; bucketX++)
            {
                if (!_buckets.TryGetValue(Key(bucketX, bucketY), out List<long>? bucket))
                {
                    continue;
                }
                result.AddRange(bucket);
            }
        }
    }

    private long Coordinate(long value)
    {
        long result = value / CellSize;
        if (value < 0 && value % CellSize != 0)
        {
            result -= 1;
        }
        return result;
    }

    private static long Key(long cellX, long cellY) => (cellX << 32) ^ (cellY & 0xFFFF_FFFFL);
}
