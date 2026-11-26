using System.Collections.Generic;

namespace AoeSim;

public sealed class ComponentStore
{
    private readonly Dictionary<string, long[]> _columns = new();
    private List<bool> _active = new();

    public int Count { get; private set; }

    public bool Has(long id) => id >= 0 && id < _active.Count && _active[(int)id];

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
        }
    }

    public void Remove(long id)
    {
        if (Has(id))
        {
            _active[(int)id] = false;
            Count -= 1;
        }
    }

    public void SetValue(long id, string column, long value)
    {
        EnsureCapacity(id);
        if (id < 0)
        {
            return;
        }
        if (!_columns.TryGetValue(column, out long[]? values))
        {
            values = new long[_active.Count];
            _columns[column] = values;
        }
        if (values.Length <= id)
        {
            System.Array.Resize(ref values, (int)id + 1);
            _columns[column] = values;
        }
        values[(int)id] = value;
    }

    public long GetValue(long id, string column)
    {
        if (!_columns.TryGetValue(column, out long[]? values))
        {
            return 0;
        }
        if (id < 0 || id >= values.Length)
        {
            return 0;
        }
        return values[(int)id];
    }

    public List<string> ColumnsSorted()
    {
        var names = new List<string>(_columns.Keys);
        names.Sort(System.StringComparer.Ordinal);
        return names;
    }

    public long[] ColumnValues(string column) =>
        _columns.TryGetValue(column, out long[]? values) ? values : System.Array.Empty<long>();

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
            foreach (string column in new List<string>(_columns.Keys))
            {
                long[] values = _columns[column];
                System.Array.Resize(ref values, newCount);
                _columns[column] = values;
            }
        }
    }
}
