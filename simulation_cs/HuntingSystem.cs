namespace AoeSim;

/// <summary>
/// 狩猎：动物死亡后转为可采集尸体，尸体随时间腐烂或采空后消失。
/// 死亡判定在 <see cref="GameSim.ResolveDeaths"/> 里切进来，这里只负责腐烂计时。
/// </summary>
public static class HuntingSystem
{
    /// <summary>把刚被击杀的动物实体原地转成食物尸体资源点，供普通采集系统复用。</summary>
    public static void TurnIntoCorpse(SimWorld world, long id)
    {
        if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.HuntingAnimal))
        {
            return;
        }
        long species = world.GetValue(id, SimComponents.HuntingAnimal, SimComponents.Species);
        long meat = world.GetValue(id, SimComponents.HuntingAnimal, SimComponents.Meat);
        if (meat <= 0)
        {
            meat = AnimalSpecies.Meat(species);
        }
        world.Detach(id, SimComponents.HuntingAnimal);
        if (world.HasComponent(id, SimComponents.Combat))
        {
            world.Detach(id, SimComponents.Combat);
        }
        if (world.HasComponent(id, SimComponents.Health))
        {
            world.Detach(id, SimComponents.Health);
        }
        if (world.HasComponent(id, SimComponents.Team))
        {
            world.Detach(id, SimComponents.Team);
        }
        world.Attach(id, SimComponents.ResourceNode);
        world.SetValue(id, SimComponents.ResourceNode, SimComponents.NodeType, GameSim.ResourceFood);
        world.SetValue(id, SimComponents.ResourceNode, SimComponents.Subtype, ResourceSubtype.Corpse);
        world.SetValue(id, SimComponents.ResourceNode, SimComponents.Remaining, meat);
        world.Attach(id, SimComponents.Corpse);
        world.SetValue(id, SimComponents.Corpse, SimComponents.RotTicks, AnimalSpecies.RotTicks(species));
    }

    /// <summary>每 tick 推进尸体腐烂；采空或腐烂归零就移除。</summary>
    public static void Step(SimWorld world)
    {
        ComponentStore corpses = world.Store(SimComponents.Corpse);
        if (corpses.Count == 0)
        {
            return;
        }
        var expired = new System.Collections.Generic.List<long>();
        foreach (long id in corpses.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            long remaining = world.HasComponent(id, SimComponents.ResourceNode)
                ? world.GetValue(id, SimComponents.ResourceNode, SimComponents.Remaining)
                : 0;
            long rot = corpses.GetValue(id, SimComponents.RotTicks) - 1;
            if (rot <= 0 || remaining <= 0)
            {
                expired.Add(id);
                continue;
            }
            corpses.SetValue(id, SimComponents.RotTicks, rot);
        }
        foreach (long id in expired)
        {
            world.Despawn(id);
        }
    }
}
