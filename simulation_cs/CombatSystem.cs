using System;
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
            long explicitTarget = LockedTarget(world, id);
            long cooldownLeft = world.GetValue(id, SimComponents.Combat, SimComponents.CooldownLeft);
            if (cooldownLeft > 0)
            {
                world.SetValue(id, SimComponents.Combat, SimComponents.CooldownLeft, cooldownLeft - 1);
                continue;
            }
            if (explicitTarget >= 0)
            {
                if (InRange(world, id, explicitTarget))
                {
                    Attack(world, id, explicitTarget, dead);
                }
                continue;
            }
            long target = NearestEnemy(world, spatial, id);
            if (target < 0)
            {
                continue;
            }
            Attack(world, id, target, dead);
        }
        foreach (long id in dead)
        {
            world.Despawn(id);
        }
        return dead.Count;
    }

    /// <summary>
    /// 读取显式攻击目标并推进追击；目标非法或已死亡时清除锁定并停止移动，回落自动索敌。
    /// 返回有效的锁定目标 id，无锁定时返回 -1。
    /// </summary>
    private static long LockedTarget(SimWorld world, long attacker)
    {
        if (!world.HasComponent(attacker, SimComponents.AttackTarget))
        {
            return -1;
        }
        long target = world.GetValue(attacker, SimComponents.AttackTarget, SimComponents.TargetEntity);
        if (!IsValidTarget(world, attacker, target))
        {
            world.Detach(attacker, SimComponents.AttackTarget);
            StopMoving(world, attacker);
            return -1;
        }
        if (InRange(world, attacker, target))
        {
            StopMoving(world, attacker);
        }
        else
        {
            Chase(world, attacker, target);
        }
        return target;
    }

    private static bool IsValidTarget(SimWorld world, long attacker, long target)
    {
        if (target == attacker || !world.IsAlive(target))
        {
            return false;
        }
        if (!world.HasComponent(target, SimComponents.Health)
            || !world.HasComponent(target, SimComponents.Position))
        {
            return false;
        }
        return world.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId);
    }

    private static bool InRange(SimWorld world, long attacker, long target)
    {
        long rangeMin = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMin);
        long rangeMax = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMax);
        long distance = CellDistance(world, attacker, target);
        return distance >= rangeMin && distance <= rangeMax;
    }

    private static long CellDistance(SimWorld world, long first, long second)
    {
        int firstX = (int)Fixed.ToInt(world.GetValue(first, SimComponents.Position, SimComponents.X));
        int firstY = (int)Fixed.ToInt(world.GetValue(first, SimComponents.Position, SimComponents.Y));
        int secondX = (int)Fixed.ToInt(world.GetValue(second, SimComponents.Position, SimComponents.X));
        int secondY = (int)Fixed.ToInt(world.GetValue(second, SimComponents.Position, SimComponents.Y));
        return Math.Max(Math.Abs(secondX - firstX), Math.Abs(secondY - firstY));
    }

    private static void Chase(SimWorld world, long attacker, long target)
    {
        if (!world.HasComponent(attacker, SimComponents.Move))
        {
            return;
        }
        world.SetValue(
            attacker,
            SimComponents.Move,
            SimComponents.TargetX,
            Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.X)));
        world.SetValue(
            attacker,
            SimComponents.Move,
            SimComponents.TargetY,
            Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.Y)));
        world.SetValue(attacker, SimComponents.Move, SimComponents.Active, 1);
    }

    private static void StopMoving(SimWorld world, long id)
    {
        if (!world.HasComponent(id, SimComponents.Move))
        {
            return;
        }
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }

    /// <summary>格子制射程：切比雪夫距离落在 [RANGE_MIN, RANGE_MAX] 内才算可攻击。</summary>
    private static long NearestEnemy(SimWorld world, SpatialHash spatial, long attacker)
    {
        long team = world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId);
        long rangeMin = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMin);
        long rangeMax = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMax);
        if (rangeMax <= 0)
        {
            return -1;
        }
        long x = world.GetValue(attacker, SimComponents.Position, SimComponents.X);
        long y = world.GetValue(attacker, SimComponents.Position, SimComponents.Y);
        long queryRadius = Fixed.Add(Fixed.FromInt(rangeMax * 2), Fixed.Half);
        List<long> candidates = spatial.QueryRadius(x, y, queryRadius);
        int attackerCellX = (int)Fixed.ToInt(x);
        int attackerCellY = (int)Fixed.ToInt(y);
        long best = -1;
        long bestCellDistance = 0;
        long bestDistanceSq = 0;
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
            int cellDistance = Math.Max(
                Math.Abs((int)Fixed.ToInt(candidateX) - attackerCellX),
                Math.Abs((int)Fixed.ToInt(candidateY) - attackerCellY));
            if (cellDistance < rangeMin || cellDistance > rangeMax)
            {
                continue;
            }
            long deltaX = Fixed.Sub(candidateX, x);
            long deltaY = Fixed.Sub(candidateY, y);
            long distanceSq = Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY));
            if (best < 0
                || cellDistance < bestCellDistance
                || (cellDistance == bestCellDistance && (distanceSq < bestDistanceSq || (distanceSq == bestDistanceSq && candidate < best))))
            {
                best = candidate;
                bestCellDistance = cellDistance;
                bestDistanceSq = distanceSq;
            }
        }
        return best;
    }

    private static void Attack(SimWorld world, long attacker, long target, List<long> dead)
    {
        long damage = world.GetValue(attacker, SimComponents.Combat, SimComponents.Damage);
        long cooldown = world.GetValue(attacker, SimComponents.Combat, SimComponents.Cooldown);
        world.SetValue(attacker, SimComponents.Combat, SimComponents.CooldownLeft, Math.Max(1, cooldown));
        long rangeMax = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMax);
        if (rangeMax <= 1)
        {
            ApplyDamage(world, target, damage, dead);
        }
        else
        {
            ProjectileSystem.Launch(world, attacker, target, damage);
        }
    }

    private static void ApplyDamage(SimWorld world, long target, long damage, List<long> dead)
    {
        long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp) - damage;
        world.SetValue(target, SimComponents.Health, SimComponents.Hp, hp);
        if (hp <= 0 && !dead.Contains(target))
        {
            dead.Add(target);
        }
    }
}
