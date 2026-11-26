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
    private readonly Dictionary<string, Dictionary<long, long>> _playerEntities = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (string Component, long Team)> _playerEntityOwner = new();
    private ComponentStore? _positionStore;
    private ComponentStore? _footprintStore;

    /// <summary>
    /// 位置或 footprint 变化的实体会同时记入占位层与空间哈希各自的脏集合：两者在不同的时点同步
    /// （占位层在移动结算前、空间哈希在移动结算后），各自处理完再清空，避免一方的清除让另一方漏掉变更。
    /// 静止实体不会进入集合，因此不产生每 tick 的全量重建开销。
    /// </summary>
    private readonly HashSet<long> _occupancyDirty = new();
    private readonly HashSet<long> _spatialDirty = new();

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
        _occupancyDirty.Add(id);
        _spatialDirty.Add(id);
        for (int index = 0; index < _stores.Count; index++)
        {
            _stores[index].Remove(id);
        }
        ForgetPlayerEntity(id);
    }

    /// <summary>
    /// 登记「每玩家一个实体」的派生索引（人口账本、玩家进度等）。索引不进组件存储，
    /// 也不参与校验和，只把按玩家查找从全表扫描降到 O(1)。
    /// </summary>
    public void RegisterPlayerEntity(string component, long team, long id)
    {
        if (!_playerEntities.TryGetValue(component, out Dictionary<long, long>? byTeam))
        {
            byTeam = new Dictionary<long, long>();
            _playerEntities.Add(component, byTeam);
        }
        byTeam[team] = id;
        _playerEntityOwner[id] = (component, team);
    }

    public bool TryGetPlayerEntity(string component, long team, out long id)
    {
        if (_playerEntities.TryGetValue(component, out Dictionary<long, long>? byTeam))
        {
            return byTeam.TryGetValue(team, out id);
        }
        id = -1;
        return false;
    }

    private void ForgetPlayerEntity(long id)
    {
        if (!_playerEntityOwner.TryGetValue(id, out (string Component, long Team) owner))
        {
            return;
        }
        _playerEntityOwner.Remove(id);
        if (_playerEntities.TryGetValue(owner.Component, out Dictionary<long, long>? byTeam)
            && byTeam.TryGetValue(owner.Team, out long registered)
            && registered == id)
        {
            byTeam.Remove(owner.Team);
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
        var store = new ComponentStore();
        _stores.Add(store);
        _storeIds.Add(component, componentId);
        _storeCache.Set(component, componentId);
        _sortedComponentNames = null;
        if (component == SimComponents.Position)
        {
            _positionStore = store;
        }
        else if (component == SimComponents.Footprint)
        {
            _footprintStore = store;
        }
        return componentId;
    }

    public void Attach(long id, string component)
    {
        ComponentStore store = Store(component);
        store.Add(id);
        MarkSpatialStructuresDirty(id, store);
    }

    public void Detach(long id, string component)
    {
        ComponentStore store = Store(component);
        store.Remove(id);
        MarkSpatialStructuresDirty(id, store);
    }

    public void SetValue(long id, string component, string column, long value)
    {
        ComponentStore store = Store(component);
        store.SetValue(id, column, value);
        MarkSpatialStructuresDirty(id, store);
    }

    public void SetValue(long id, int componentId, int columnId, long value)
    {
        ComponentStore store = Store(componentId);
        store.SetValue(id, columnId, value);
        MarkSpatialStructuresDirty(id, store);
    }

    private void MarkSpatialStructuresDirty(long id, ComponentStore store)
    {
        if (ReferenceEquals(store, _positionStore) || ReferenceEquals(store, _footprintStore))
        {
            _occupancyDirty.Add(id);
            _spatialDirty.Add(id);
        }
    }

    /// <summary>占位层待同步实体，升序保证增量落章顺序与全量重建一致。</summary>
    public long[] OccupancyDirtySorted()
    {
        long[] ids = SortedSnapshot(_occupancyDirty);
        _occupancyDirty.Clear();
        return ids;
    }

    /// <summary>空间哈希待同步实体，升序保证确定性。</summary>
    public long[] SpatialDirtySorted()
    {
        long[] ids = SortedSnapshot(_spatialDirty);
        _spatialDirty.Clear();
        return ids;
    }

    private static long[] SortedSnapshot(HashSet<long> source)
    {
        if (source.Count == 0)
        {
            return Array.Empty<long>();
        }
        var ids = new long[source.Count];
        source.CopyTo(ids);
        Array.Sort(ids);
        return ids;
    }

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
