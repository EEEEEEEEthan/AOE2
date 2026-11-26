using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AoeSim;

/// <summary>字符串名到整数 id 的单槽直查缓存。命中依赖引用相等，未命中回落到字典。</summary>
internal sealed class NameIdCache
{
    private readonly int _mask;
    private readonly string?[] _keys;
    private readonly int[] _ids;

    public NameIdCache(int capacity)
    {
        _mask = capacity - 1;
        _keys = new string?[capacity];
        _ids = new int[capacity];
    }

    public bool TryGet(string name, out int id)
    {
        int slot = RuntimeHelpers.GetHashCode(name) & _mask;
        if (ReferenceEquals(_keys[slot], name))
        {
            id = _ids[slot];
            return true;
        }
        id = -1;
        return false;
    }

    public void Set(string name, int id)
    {
        int slot = RuntimeHelpers.GetHashCode(name) & _mask;
        _keys[slot] = name;
        _ids[slot] = id;
    }
}

public sealed class ComponentStore
{
    private readonly List<long[]> _columns = new();
    private readonly Dictionary<string, int> _columnIds = new(StringComparer.Ordinal);
    private readonly NameIdCache _columnCache = new(256);
    private List<bool> _active = new();
    private List<string>? _sortedColumnNames;
    private int[] _sortedColumnIds = Array.Empty<int>();
    private long[] _activeIds = Array.Empty<long>();
    private bool _activeIdsDirty = true;

    public int Count { get; private set; }

    public bool Has(long id) => id >= 0 && id < _active.Count && _active[(int)id];

    /// <summary>
    /// 该组件当前挂载的实体 id 升序快照。Add/Remove 置脏，下次访问重建一次；
    /// 系统遍历候选实体用它代替「全实体扫描 + Has(id)」。
    /// </summary>
    public ReadOnlySpan<long> ActiveIds()
    {
        if (_activeIdsDirty)
        {
            var ids = new List<long>(Count);
            for (int id = 0; id < _active.Count; id++)
            {
                if (_active[id])
                {
                    ids.Add(id);
                }
            }
            _activeIds = ids.ToArray();
            _activeIdsDirty = false;
        }
        return _activeIds;
    }

    public void Add(long id)
    {
        EnsureCapacity(id);
        if (id < 0)
        {
            return;
        }
        if (!_active[(int)id])
        {
            _active[(int)id] = true;
            Count += 1;
            _activeIdsDirty = true;
        }
    }

    public void Remove(long id)
    {
        if (Has(id))
        {
            _active[(int)id] = false;
            Count -= 1;
            _activeIdsDirty = true;
        }
    }

    public void SetValue(long id, string column, long value) =>
        SetValue(id, ColumnId(column, create: true), value);

    public void SetValue(long id, int columnId, long value)
    {
        EnsureCapacity(id);
        if (id < 0)
        {
            return;
        }
        long[] values = _columns[columnId];
        if (values.Length <= id)
        {
            Array.Resize(ref values, (int)id + 1);
            _columns[columnId] = values;
        }
        values[(int)id] = value;
    }

    public long GetValue(long id, string column)
    {
        int columnId = ColumnId(column, create: false);
        return columnId < 0 ? 0 : GetValue(id, columnId);
    }

    public long GetValue(long id, int columnId)
    {
        long[] values = _columns[columnId];
        if (id < 0 || id >= values.Length)
        {
            return 0;
        }
        return values[(int)id];
    }

    public int ColumnId(string column) => ColumnId(column, create: false);

    /// <summary>解析列 id，缺失时创建空列；用于系统入口为将要写入的列建立句柄。</summary>
    public int CreateColumn(string column) => ColumnId(column, create: true);

    public List<string> ColumnsSorted()
    {
        EnsureSortedColumns();
        return _sortedColumnNames!;
    }

    public long[] ColumnValues(string column)
    {
        int columnId = ColumnId(column, create: false);
        return columnId < 0 ? Array.Empty<long>() : _columns[columnId];
    }

    /// <summary>按缓存好的列排序把单个实体的全部列值混入校验和，循环内不做查找与分配。</summary>
    public long HashEntity(long hash, long id)
    {
        EnsureSortedColumns();
        for (int index = 0; index < _sortedColumnNames!.Count; index++)
        {
            hash = SimWorld.HashText(hash, _sortedColumnNames[index]);
            hash = SimWorld.HashInt(hash, GetValue(id, _sortedColumnIds[index]));
        }
        return hash;
    }

    private int ColumnId(string column, bool create)
    {
        if (_columnCache.TryGet(column, out int cached))
        {
            return cached;
        }
        if (_columnIds.TryGetValue(column, out int columnId))
        {
            _columnCache.Set(column, columnId);
            return columnId;
        }
        if (!create)
        {
#if DEBUG
            if (!SimComponents.KnownNames.Contains(column))
            {
                throw new InvalidOperationException($"读取了未定义的列名 '{column}'。");
            }
#endif
            return -1;
        }
        columnId = _columns.Count;
        _columns.Add(Array.Empty<long>());
        _columnIds.Add(column, columnId);
        _columnCache.Set(column, columnId);
        _sortedColumnNames = null;
        return columnId;
    }

    private void EnsureSortedColumns()
    {
        if (_sortedColumnNames != null)
        {
            return;
        }
        var names = new List<string>(_columnIds.Keys);
        names.Sort(StringComparer.Ordinal);
        _sortedColumnNames = names;
        _sortedColumnIds = new int[names.Count];
        for (int index = 0; index < names.Count; index++)
        {
            _sortedColumnIds[index] = _columnIds[names[index]];
        }
    }

    private void EnsureCapacity(long id)
    {
        if (id < 0)
        {
            return;
        }
        if (_active.Count <= id)
        {
            int newCount = (int)id + 1;
            while (_active.Count < newCount)
            {
                _active.Add(false);
            }
            for (int index = 0; index < _columns.Count; index++)
            {
                long[] values = _columns[index];
                Array.Resize(ref values, newCount);
                _columns[index] = values;
            }
        }
    }
}
