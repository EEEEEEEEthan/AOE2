using System.Collections.Generic;

namespace AoeSim;

public static class CombatSystem
{
    public static int Step(SimWorld world, SpatialHash spatial)
    {
        spatial.Rebuild(world);
        ComponentStore combatStore = world.Store(SimComponents.Combat);
        if (combatStore.Count == 0)
        {
            return 0;
        }
        var dead = new List<long>();
        foreach (long id in world.Entities())
        {
            if (!combatStore.Has(id))
            {
                continue;
            }
            if (!world.IsAlive(id))
            {
                continue;
            }
            long cooldownLeft = world.GetValue(id, SimComponents.Combat, SimComponents.CooldownLeft);
            if (cooldownLeft > 0)
            {
                world.SetValue(id, SimComponents.Combat, SimComponents.CooldownLeft, cooldownLeft - 1);
                continue;
            }
            long target = NearestEnemy(world, spatial, id);
            if (target < 0)
            {
                continue;
            }
            ApplyDamage(world, id, target, dead);
        }
        foreach (long id in dead)
        {
            world.Despawn(id);
        }
        return dead.Count;
    }

    private static long NearestEnemy(SimWorld world, SpatialHash spatial, long attacker)
    {
        long team = world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId);
        long rangeSq = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeSq);
        long x = world.GetValue(attacker, SimComponents.Position, SimComponents.X);
        long y = world.GetValue(attacker, SimComponents.Position, SimComponents.Y);
        long rangeFixed = Fixed.Sqrt(rangeSq);
        List<long> candidates = spatial.QueryRadius(x, y, rangeFixed);
        long best = -1;
        long bestDistance = 0;
        foreach (long candidate in candidates)
        {
            if (candidate == attacker)
            {
                continue;
            }
            if (!world.IsAlive(candidate))
            {
                continue;
            }
            if (!world.HasComponent(candidate, SimComponents.Health))
            {
                continue;
            }
            if (world.GetValue(candidate, SimComponents.Team, SimComponents.TeamId) == team)
            {
                continue;
            }
            long candidateX = world.GetValue(candidate, SimComponents.Position, SimComponents.X);
            long candidateY = world.GetValue(candidate, SimComponents.Position, SimComponents.Y);
            long deltaX = Fixed.Sub(candidateX, x);
            long deltaY = Fixed.Sub(candidateY, y);
            long distance = Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY));
            if (distance > rangeSq)
            {
                continue;
            }
            if (best < 0 || distance < bestDistance || (distance == bestDistance && candidate < best))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static void ApplyDamage(SimWorld world, long attacker, long target, List<long> dead)
    {
        long damage = world.GetValue(attacker, SimComponents.Combat, SimComponents.Damage);
        long cooldown = world.GetValue(attacker, SimComponents.Combat, SimComponents.Cooldown);
        world.SetValue(attacker, SimComponents.Combat, SimComponents.CooldownLeft, System.Math.Max(1, cooldown));
        long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp) - damage;
        world.SetValue(target, SimComponents.Health, SimComponents.Hp, hp);
        if (hp <= 0 && !dead.Contains(target))
        {
            dead.Add(target);
        }
    }
}
