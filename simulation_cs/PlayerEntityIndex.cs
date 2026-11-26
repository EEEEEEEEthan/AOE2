using System.Collections.Generic;

namespace AoeSim;

/// <summary>玩家 → 单一实体的稀疏索引（库存、进度等），避免在 GameSim 上堆杂项字典。</summary>
internal sealed class PlayerEntityIndex
{
    private readonly Dictionary<long, long> _entities = new();

    public int Count => _entities.Count;

    public bool Contains(long team) => _entities.ContainsKey(team);

    public bool TryGet(long team, out long entity) => _entities.TryGetValue(team, out entity);

    public long Get(long team) => _entities[team];

    public void Set(long team, long entity) => _entities[team] = entity;
}
