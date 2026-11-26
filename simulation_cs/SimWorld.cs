using System;
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
    private readonly List<ComponentStore> _stores = new();
    private readonly Dictionary<string, int> _storeIds = new(StringComparer.Ordinal);
    private readonly NameIdCache _storeCache = new(256);
    private List<string>? _sortedComponentNames;
    private int[] _sortedComponentIds = Array.Empty<int>();
    private long[] _aliveCache = Array.Empty<long>();
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
        for (int index = 0; index < _stores.Count; index++)
        {
            _stores[index].Remove(id);
        }
    }

    public bool IsAlive(long id) => id >= 0 && id < _alive.Count && _alive[(int)id];

    public System.ReadOnlySpan<long> Entities()
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
        int componentId = ComponentId(name, create: true);
        return _stores[componentId];
    }

    public ComponentStore Store(int componentId) => _stores[componentId];

    public int ComponentId(string component) => ComponentId(component, create: false);

    public int ComponentId(string component, bool create)
    {
        if (_storeCache.TryGet(component, out int cached))
        {
            return cached;
        }
        if (_storeIds.TryGetValue(component, out int componentId))
        {
            _storeCache.Set(component, componentId);
            return componentId;
        }
        if (!create)
        {
            return -1;
        }
        componentId = _stores.Count;
        _stores.Add(new ComponentStore());
        _storeIds.Add(component, componentId);
        _storeCache.Set(component, componentId);
        _sortedComponentNames = null;
        return componentId;
    }

    public void Attach(long id, string component) => Store(component).Add(id);

    public void Detach(long id, string component) => Store(component).Remove(id);

    public void SetValue(long id, string component, string column, long value) =>
        Store(component).SetValue(id, column, value);

    public void SetValue(long id, int componentId, int columnId, long value) =>
        Store(componentId).SetValue(id, columnId, value);

    public long GetValue(long id, string component, string column) =>
        Store(component).GetValue(id, column);

    public long GetValue(long id, int componentId, int columnId) =>
        Store(componentId).GetValue(id, columnId);

    public bool HasComponent(long id, string component)
    {
        int componentId = ComponentId(component, create: false);
        return componentId >= 0 && _stores[componentId].Has(id);
    }

    public List<string> ComponentNamesSorted()
    {
        if (_sortedComponentNames == null)
        {
            var names = new List<string>(_storeIds.Keys);
            names.Sort(StringComparer.Ordinal);
            _sortedComponentNames = names;
            _sortedComponentIds = new int[names.Count];
            for (int index = 0; index < names.Count; index++)
            {
                _sortedComponentIds[index] = _storeIds[names[index]];
            }
        }
        return _sortedComponentNames;
    }

    public long Checksum()
    {
        long hash = FnvOffset;
        hash = HashInt(hash, Tick);
        hash = HashInt(hash, NextEntity);
        hash = HashInt(hash, Rng.State);
        List<string> componentNames = ComponentNamesSorted();
        foreach (long id in Entities())
        {
            hash = HashInt(hash, id);
            for (int componentIndex = 0; componentIndex < componentNames.Count; componentIndex++)
            {
                ComponentStore store = _stores[_sortedComponentIds[componentIndex]];
                if (!store.Has(id))
                {
                    continue;
                }
                hash = HashText(hash, componentNames[componentIndex]);
                hash = store.HashEntity(hash, id);
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
