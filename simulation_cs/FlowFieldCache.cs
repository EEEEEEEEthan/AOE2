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
    private readonly Stack<FlowField> _fieldPool = new();

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

    /// <summary>全表失效：用于「解除阻挡」等可能在任何位置开新路的变化，以及外部显式调用。</summary>
    public void Invalidate()
    {
        foreach (FlowField field in _fields.Values)
        {
            _fieldPool.Push(field);
        }
        _fields.Clear();
        _fieldNodes.Clear();
        _fieldRecency.Clear();
    }

    /// <summary>
    /// 新增阻挡（建筑落位/完工、城门关闭）时只作废受影响流场：
    /// 仅当矩形内存在对该目标格「可达」（旧代价 ≥ 0）的格子时该流场才可能变化；
    /// 原本不可达的格被封不会改变任何代价，可直接保留，避免建造高峰整表重建。
    /// </summary>
    public void InvalidateForBlockedRect(int originX, int originY, int width, int height)
    {
        if (_fields.Count == 0)
        {
            return;
        }
        List<long>? dirty = null;
        foreach (KeyValuePair<long, FlowField> entry in _fields)
        {
            if (FieldTouchesRect(entry.Value, originX, originY, width, height))
            {
                (dirty ??= new List<long>()).Add(entry.Key);
            }
        }
        if (dirty == null)
        {
            return;
        }
        foreach (long key in dirty)
        {
            RemoveField(key);
        }
    }

    private static bool FieldTouchesRect(FlowField field, int originX, int originY, int width, int height)
    {
        int minX = System.Math.Max(originX, 0);
        int minY = System.Math.Max(originY, 0);
        int maxX = System.Math.Min(originX + width, field.Width);
        int maxY = System.Math.Min(originY + height, field.Height);
        for (int y = minY; y < maxY; y++)
        {
            for (int x = minX; x < maxX; x++)
            {
                if (field.CostAt(x, y) >= 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void RemoveField(long key)
    {
        if (!_fields.Remove(key, out FlowField? field))
        {
            return;
        }
        if (_fieldNodes.Remove(key, out LinkedListNode<long>? node))
        {
            _fieldRecency.Remove(node);
        }
        _fieldPool.Push(field);
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
        if (_fields.Count >= Capacity)
        {
            RemoveField(_fieldRecency.First!.Value);
        }
        field = _fieldPool.Count > 0 ? _fieldPool.Pop() : new FlowField(_grid, _terrain, domain);
        field.Reconfigure(_terrain, domain);
        field.Compute(_grid, targetX, targetY);
        _fields[key] = field;
        _fieldNodes[key] = _fieldRecency.AddLast(key);
        CreatedThisTick += 1;
        CreatedTotal += 1;
        return field;
    }
}
