namespace AoeSim;

public sealed partial class GameSim
{
    public void CompleteWonder(long id)
    {
        if (World.IsAlive(id) && World.HasComponent(id, SimComponents.Wonder))
        {
            World.SetValue(id, SimComponents.Wonder, SimComponents.Complete, 1);
        }
    }

    public void SetRelicHolder(long id, long team)
    {
        if (World.IsAlive(id) && World.HasComponent(id, SimComponents.Relic))
        {
            World.SetValue(id, SimComponents.Relic, SimComponents.HolderTeam, team);
        }
    }

    /// <summary>某方当前持有的圣物数量（地面未归属的圣物不计）。供圣物胜利条件调用。</summary>
    public long RelicCountOf(long team)
    {
        ComponentStore relicStore = World.Store(SimComponents.Relic);
        long count = 0;
        foreach (long id in World.Entities())
        {
            if (!relicStore.Has(id) || !World.IsAlive(id))
            {
                continue;
            }
            if (World.GetValue(id, SimComponents.Relic, SimComponents.HolderTeam) == team)
            {
                count += 1;
            }
        }
        return count;
    }
}
