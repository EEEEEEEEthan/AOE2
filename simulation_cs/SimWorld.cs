using System.Collections.Generic;

namespace AoeSim;

public sealed class SimWorld
{
    public const long FnvOffset = 0x811C_9DC5L;
    public const long FnvPrime = 0x0100_0193L;
    public const long Mask32 = 0xFFFF_FFFFL;

    public long Tick { get; set; }
    public long NextEntity { get; set; }
    public SimRng Rng { get; }

    private readonly List<bool> _alive = new();
    private readonly Dictionary<string, ComponentStore> _stores = new();
    private long[] _aliveCache = System.Array.Empty<long>();
    private bool _cacheDirty = true;

    public SimWorld(long seed = 1)
    {
        Rng = new SimRng(seed);
    }

    public long Spawn()
    {
        long id = NextEntity;
        NextEntity += 1;
        while (_alive.Count <= id)
        {
            _alive.Add(false);
        }
        _alive[(int)id] = true;
        _cacheDirty = true;
        return id;
    }

    public void Despawn(long id)
    {
        if (!IsAlive(id))
        {
            return;
        }
        _alive[(int)id] = false;
        _cacheDirty = true;
        foreach (ComponentStore store in _stores.Values)
        {
            store.Remove(id);
        }
    }

    public bool IsAlive(long id) => id >= 0 && id < _alive.Count && _alive[(int)id];

    public long[] Entities()
    {
        if (_cacheDirty)
        {
            var result = new List<long>();
            for (int id = 0; id < _alive.Count; id++)
            {
                if (_alive[id])
                {
                    result.Add(id);
                }
            }
            _aliveCache = result.ToArray();
            _cacheDirty = false;
        }
        return _aliveCache;
    }

    public ComponentStore Store(string name)
    {
        if (!_stores.TryGetValue(name, out ComponentStore? store))
        {
            store = new ComponentStore();
            _stores[name] = store;
        }
        return store;
    }

    public void Attach(long id, string component) => Store(component).Add(id);

    public void Detach(long id, string component) => Store(component).Remove(id);

    public void SetValue(long id, string component, string column, long value) =>
        Store(component).SetValue(id, column, value);

    public long GetValue(long id, string component, string column) =>
        Store(component).GetValue(id, column);

    public bool HasComponent(long id, string component) =>
        _stores.TryGetValue(component, out ComponentStore? store) && store.Has(id);

    public List<string> ComponentNamesSorted()
    {
        var names = new List<string>(_stores.Keys);
        names.Sort(System.StringComparer.Ordinal);
        return names;
    }

    public long Checksum()
    {
        long hash = FnvOffset;
        hash = HashInt(hash, Tick);
        hash = HashInt(hash, NextEntity);
        hash = HashInt(hash, Rng.State);
        foreach (long id in Entities())
        {
            hash = HashInt(hash, id);
            foreach (string componentText in ComponentNamesSorted())
            {
                ComponentStore store = _stores[componentText];
                if (!store.Has(id))
                {
                    continue;
                }
                hash = HashText(hash, componentText);
                foreach (string columnText in store.ColumnsSorted())
                {
                    hash = HashText(hash, columnText);
                    hash = HashInt(hash, store.GetValue(id, columnText));
                }
            }
        }
        return hash;
    }

    public static long HashText(long hash, string text)
    {
        foreach (char character in text)
        {
            hash = (hash ^ character) & Mask32;
            hash = (hash * FnvPrime) & Mask32;
        }
        return hash;
    }

    public static long HashInt(long hash, long value)
    {
        long bytes = value & Mask32;
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash = (hash ^ ((bytes >> shift) & 0xFF)) & Mask32;
            hash = (hash * FnvPrime) & Mask32;
        }
        return hash;
    }
}
