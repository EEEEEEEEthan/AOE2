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

    public static long FindEntity(SimWorld world, long team)
    {
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
                return id;
            }
        }
        return -1;
    }
}
