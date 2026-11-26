namespace AoeSim;

/// <summary>按玩家承载时代与已研究科技的进度实体；研究位图以整数 bitset 存储。</summary>
public sealed class PlayerProgress
{
    private readonly SimWorld _world;

    public PlayerProgress(SimWorld world, long entity, long team)
    {
        _world = world;
        Entity = entity;
        Team = team;
    }

    public long Entity { get; }
    public long Team { get; }

    public long Age
    {
        get => _world.GetValue(Entity, SimComponents.PlayerProgress, SimComponents.Age);
        set => _world.SetValue(Entity, SimComponents.PlayerProgress, SimComponents.Age, value);
    }

    public long Research
    {
        get => _world.GetValue(Entity, SimComponents.PlayerProgress, SimComponents.Research);
        set => _world.SetValue(Entity, SimComponents.PlayerProgress, SimComponents.Research, value);
    }

    /// <summary>所属文明 id；缺省为通用文明。写入后随校验和同步。</summary>
    public long Civ
    {
        get => _world.GetValue(Entity, SimComponents.PlayerProgress, SimComponents.Civ);
        set => _world.SetValue(Entity, SimComponents.PlayerProgress, SimComponents.Civ, value);
    }

    public static long FindEntity(SimWorld world, long team)
    {
        if (world.TryGetPlayerEntity(SimComponents.PlayerProgress, team, out long indexed))
        {
            return indexed;
        }
        ComponentStore store = world.Store(SimComponents.PlayerProgress);
        if (store.Count == 0)
        {
            return -1;
        }
        foreach (long id in world.Entities())
        {
            if (!store.Has(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                world.RegisterPlayerEntity(SimComponents.PlayerProgress, team, id);
                return id;
            }
        }
        return -1;
    }
}
