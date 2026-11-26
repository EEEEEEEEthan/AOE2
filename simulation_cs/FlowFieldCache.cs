using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 按目标格共享的有界 LRU 流场缓存。流场是派生数据、不进校验和，
/// 淘汰后重建结果完全一致；本类只负责缓存与淘汰，不参与模拟编排。
/// </summary>
internal sealed class FlowFieldCache
{
    private readonly NavGrid _grid;
    private readonly TerrainGrid _terrain;
    private readonly Dictionary<long, FlowField> _fields = new();
    private readonly Dictionary<long, LinkedListNode<long>> _fieldNodes = new();
    private readonly LinkedList<long> _fieldRecency = new();

    public FlowFieldCache(NavGrid grid, TerrainGrid terrain, int capacity)
    {
        if (capacity < 1)
        {
            throw new System.ArgumentOutOfRangeException(nameof(capacity));
        }
        _grid = grid;
        _terrain = terrain;
        Capacity = capacity;
    }

    public int Count => _fields.Count;

    public int Capacity { get; }

    /// <summary>本 tick 新建的流场数；每 tick 开始时由 <see cref="ResetTick"/> 清零，不进校验和。</summary>
    public int CreatedThisTick { get; private set; }

    public long CreatedTotal { get; private set; }

    public void ResetTick() => CreatedThisTick = 0;

    public void Invalidate()
    {
        _fields.Clear();
        _fieldNodes.Clear();
        _fieldRecency.Clear();
    }

    /// <summary>
    /// 按目标格共享有界 LRU 流场：编队或大规模移动必须让单位共享同一目标格的流场，
    /// 单位各自按所在格取方向，不能为分摊落点生成多张流场，否则一次移动会触发多张全图 BFS。
    /// </summary>
    public FlowField Get(int targetX, int targetY, long domain)
    {
        long key = ((long)targetX << 40) ^ ((long)targetY << 16) ^ (domain & 0xFFFFL);
        if (_fields.TryGetValue(key, out FlowField? field))
        {
            LinkedListNode<long> node = _fieldNodes[key];
            _fieldRecency.Remove(node);
            _fieldRecency.AddLast(node);
            return field;
        }
        field = new FlowField(_grid, _terrain, domain);
        field.Compute(_grid, targetX, targetY);
        if (_fields.Count >= Capacity)
        {
            LinkedListNode<long> oldest = _fieldRecency.First!;
            _fieldRecency.RemoveFirst();
            _fieldNodes.Remove(oldest.Value);
            _fields.Remove(oldest.Value);
        }
        _fields[key] = field;
        _fieldNodes[key] = _fieldRecency.AddLast(key);
        CreatedThisTick += 1;
        CreatedTotal += 1;
        return field;
    }
}
