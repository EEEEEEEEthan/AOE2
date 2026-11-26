using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>远程弹道：按格子距离与弹速算出命中所需 tick，到达时才结算伤害。</summary>
public static class ProjectileSystem
{
    public const long SpeedCellsPerTick = 2;
    private static readonly long SpeedFixed = Fixed.FromInt(SpeedCellsPerTick);

    public static long Launch(SimWorld world, long attacker, long target, long damage)
    {
        long id = world.Spawn();
        world.Attach(id, SimComponents.Projectile);
        world.SetValue(id, SimComponents.Projectile, SimComponents.ProjectileTarget, target);
        world.SetValue(id, SimComponents.Projectile, SimComponents.ProjectileDamage, damage);
        world.SetValue(id, SimComponents.Projectile, SimComponents.ProjectileTicksLeft, TravelTicks(world, attacker, target));
        // 记录发射方阵营，命中时用于击杀归属；发射者可能在弹道飞行中阵亡。
        if (world.HasComponent(attacker, SimComponents.Team))
        {
            world.Attach(id, SimComponents.Team);
            world.SetValue(
                id,
                SimComponents.Team,
                SimComponents.TeamId,
                world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId));
        }
        return id;
    }

    public static List<long> Step(SimWorld world, List<KillEvent>? kills = null)
    {
        var dead = new List<long>();
        ComponentStore store = world.Store(SimComponents.Projectile);
        if (store.Count == 0)
        {
            return dead;
        }
        var spent = new List<long>();
        var deadSet = new HashSet<long>();
        foreach (long id in store.ActiveIds())
        {
            long ticksLeft = store.GetValue(id, SimComponents.ProjectileTicksLeft) - 1;
            if (ticksLeft > 0)
            {
                store.SetValue(id, SimComponents.ProjectileTicksLeft, ticksLeft);
                continue;
            }
            long target = store.GetValue(id, SimComponents.ProjectileTarget);
            long damage = store.GetValue(id, SimComponents.ProjectileDamage);
            if (world.IsAlive(target) && world.HasComponent(target, SimComponents.Health))
            {
                long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp) - damage;
                world.SetValue(target, SimComponents.Health, SimComponents.Hp, hp);
                if (hp <= 0 && deadSet.Add(target))
                {
                    dead.Add(target);
                    if (kills != null
                        && world.HasComponent(id, SimComponents.Team)
                        && world.HasComponent(target, SimComponents.Team))
                    {
                        kills.Add(new KillEvent(
                            target,
                            world.GetValue(id, SimComponents.Team, SimComponents.TeamId),
                            world.GetValue(target, SimComponents.Team, SimComponents.TeamId)));
                    }
                }
            }
            spent.Add(id);
        }
        foreach (long id in spent)
        {
            world.Despawn(id);
        }
        return dead;
    }

    private static long TravelTicks(SimWorld world, long attacker, long target)
    {
        int attackerX = (int)Fixed.ToInt(world.GetValue(attacker, SimComponents.Position, SimComponents.X));
        int attackerY = (int)Fixed.ToInt(world.GetValue(attacker, SimComponents.Position, SimComponents.Y));
        int targetX = (int)Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.X));
        int targetY = (int)Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.Y));
        int distance = Math.Max(Math.Abs(targetX - attackerX), Math.Abs(targetY - attackerY));
        long ticks = (Fixed.FromInt(distance) + SpeedFixed - 1) / SpeedFixed;
        return Math.Max(1, ticks);
    }
}
