using System.Collections.Generic;

namespace AoeSim;

public sealed class SpatialHash
{
    public long CellSize { get; }
    private readonly Dictionary<long, List<long>> _buckets = new();
    private readonly Dictionary<long, long> _entityBucket = new();

    /// <summary>最近一次 <see cref="Sync"/> 实际处理的脏实体数，供增量维护的回归测试断言。</summary>
    public int LastSyncedEntities { get; private set; }

    public SpatialHash(int cellTiles = 4)
    {
        CellSize = cellTiles * Fixed.Scale;
    }

    public void Clear()
    {
        _buckets.Clear();
        _entityBucket.Clear();
    }

    public void Insert(long id, long xFixed, long yFixed)
    {
        long key = Key(Coordinate(xFixed), Coordinate(yFixed));
        if (!_buckets.TryGetValue(key, out List<long>? bucket))
        {
            bucket = new List<long>();
            _buckets[key] = bucket;
        }
        bucket.Add(id);
        _entityBucket[id] = key;
    }

    /// <summary>增量同步：只对位置变化的实体做删旧桶/插新桶，静止实体不参与。</summary>
    public void Sync(SimWorld world)
    {
        long[] dirty = world.SpatialDirtySorted();
        LastSyncedEntities = 0;
        for (int index = 0; index < dirty.Length; index++)
        {
            long id = dirty[index];
            Remove(id);
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            Insert(
                id,
                world.GetValue(id, SimComponents.Position, SimComponents.X),
                world.GetValue(id, SimComponents.Position, SimComponents.Y));
            LastSyncedEntities += 1;
        }
    }

    private void Remove(long id)
    {
        if (!_entityBucket.TryGetValue(id, out long key))
        {
            return;
        }
        _entityBucket.Remove(id);
        if (_buckets.TryGetValue(key, out List<long>? bucket))
        {
            bucket.Remove(id);
            if (bucket.Count == 0)
            {
                _buckets.Remove(key);
            }
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
