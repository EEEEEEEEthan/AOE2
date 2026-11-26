using System;
using System.Collections.Generic;

namespace AoeSim;

public static class CombatSystem
{
    [ThreadStatic]
    private static List<long>? _candidates;

    public static List<long> Step(SimWorld world, SpatialHash spatial)
    {
        var dead = new List<long>();
        var deadSet = new HashSet<long>();
        ComponentStore combatStore = world.Store(SimComponents.Combat);
        if (combatStore.Count == 0)
        {
            return dead;
        }
        foreach (long id in combatStore.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            // 驻军中的单位已移出世界占位，不再参与索敌与射击。
            if (world.HasComponent(id, SimComponents.Garrisoned))
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
                    Attack(world, id, explicitTarget, dead, deadSet);
                }
                continue;
            }
            AutoEngage(world, spatial, id, dead, deadSet);
        }
        return dead;
    }

    /// <summary>
    /// 读取显式攻击目标并推进追击；目标非法或已死亡时清除锁定并停止移动。
    /// 返回有效的锁定目标 id，无锁定时返回 -1。显式指令优先级高于姿态自动索敌。
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

    /// <summary>
    /// 按姿态决定是否自主索敌与追击：禁止还击不索敌；驻守只打射程内；防御短距离追击；
    /// 进攻在较大仇恨半径内追击。玩家移动中的单位不主动接战。
    /// </summary>
    private static void AutoEngage(
        SimWorld world,
        SpatialHash spatial,
        long attacker,
        List<long> dead,
        HashSet<long> deadSet)
    {
        if (world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMax) <= 0)
        {
            return;
        }
        long stance = world.GetValue(attacker, SimComponents.Combat, SimComponents.Stance);
        if (!CombatStance.AutoAcquires(stance))
        {
            ClearEngagement(world, attacker);
            return;
        }
        long target = EngagedTarget(world, attacker);
        if (target >= 0 && (!IsValidTarget(world, attacker, target) || !WithinLeash(world, attacker, stance)))
        {
            ClearEngagement(world, attacker);
            target = -1;
        }
        if (target < 0)
        {
            if (IsMoving(world, attacker))
            {
                return;
            }
            target = AcquireTarget(world, spatial, attacker, stance);
            if (target < 0)
            {
                return;
            }
            BeginEngagement(world, attacker, target);
        }
        if (InRange(world, attacker, target))
        {
            StopMoving(world, attacker);
            Attack(world, attacker, target, dead, deadSet);
            return;
        }
        if (CombatStance.LeashTiles(stance) == 0)
        {
            ClearEngagement(world, attacker);
            return;
        }
        Chase(world, attacker, target);
    }

    private static long EngagedTarget(SimWorld world, long attacker)
    {
        if (!world.HasComponent(attacker, SimComponents.Engage))
        {
            return -1;
        }
        return world.GetValue(attacker, SimComponents.Engage, SimComponents.AutoTarget);
    }

    private static void BeginEngagement(SimWorld world, long attacker, long target)
    {
        world.Attach(attacker, SimComponents.Engage);
        world.SetValue(attacker, SimComponents.Engage, SimComponents.AutoTarget, target);
        world.SetValue(
            attacker,
            SimComponents.Engage,
            SimComponents.OriginX,
            world.GetValue(attacker, SimComponents.Position, SimComponents.X));
        world.SetValue(
            attacker,
            SimComponents.Engage,
            SimComponents.OriginY,
            world.GetValue(attacker, SimComponents.Position, SimComponents.Y));
    }

    private static void ClearEngagement(SimWorld world, long attacker)
    {
        if (!world.HasComponent(attacker, SimComponents.Engage))
        {
            return;
        }
        world.Detach(attacker, SimComponents.Engage);
        StopMoving(world, attacker);
    }

    /// <summary>以接战起点衡量是否仍在 leash 内；超出即放弃追击。</summary>
    private static bool WithinLeash(SimWorld world, long attacker, long stance)
    {
        long leashTiles = CombatStance.LeashTiles(stance);
        if (leashTiles == 0)
        {
            return true;
        }
        long distance = CellDistanceBetween(
            world.GetValue(attacker, SimComponents.Position, SimComponents.X),
            world.GetValue(attacker, SimComponents.Position, SimComponents.Y),
            world.GetValue(attacker, SimComponents.Engage, SimComponents.OriginX),
            world.GetValue(attacker, SimComponents.Engage, SimComponents.OriginY));
        return distance <= leashTiles;
    }

    private static bool IsMoving(SimWorld world, long id)
    {
        if (!world.HasComponent(id, SimComponents.Move))
        {
            return false;
        }
        return world.GetValue(id, SimComponents.Move, SimComponents.Active) != 0;
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

    private static long CellDistance(SimWorld world, long first, long second) =>
        CellDistanceBetween(
            world.GetValue(first, SimComponents.Position, SimComponents.X),
            world.GetValue(first, SimComponents.Position, SimComponents.Y),
            world.GetValue(second, SimComponents.Position, SimComponents.X),
            world.GetValue(second, SimComponents.Position, SimComponents.Y));

    private static long CellDistanceBetween(long firstX, long firstY, long secondX, long secondY) =>
        Math.Max(
            Math.Abs((int)Fixed.ToInt(secondX) - (int)Fixed.ToInt(firstX)),
            Math.Abs((int)Fixed.ToInt(secondY) - (int)Fixed.ToInt(firstY)));

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

    /// <summary>按姿态索敌：驻守只看射程内，防御/进攻在射程外再放宽若干格。</summary>
    private static long AcquireTarget(SimWorld world, SpatialHash spatial, long attacker, long stance)
    {
        long team = world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId);
        long rangeMin = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMin);
        long rangeMax = world.GetValue(attacker, SimComponents.Combat, SimComponents.RangeMax);
        long acquireMax = rangeMax + CombatStance.AcquireTiles(stance);
        long x = world.GetValue(attacker, SimComponents.Position, SimComponents.X);
        long y = world.GetValue(attacker, SimComponents.Position, SimComponents.Y);
        long queryRadius = Fixed.Add(Fixed.FromInt(acquireMax * 2), Fixed.Half);
        List<long> candidates = _candidates ??= new List<long>();
        spatial.QueryRadius(x, y, queryRadius, candidates);
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
            long candidateTeam = world.GetValue(candidate, SimComponents.Team, SimComponents.TeamId);
            // Gaia 野生动物只吃玩家显式攻击指令，不参与自动索敌，避免单位乱追猎物。
            if (candidateTeam == team || candidateTeam == GameSim.GaiaTeam)
            {
                continue;
            }
            long candidateX = world.GetValue(candidate, SimComponents.Position, SimComponents.X);
            long candidateY = world.GetValue(candidate, SimComponents.Position, SimComponents.Y);
            long cellDistance = CellDistanceBetween(x, y, candidateX, candidateY);
            if (cellDistance < rangeMin || cellDistance > acquireMax)
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

    private static void Attack(
        SimWorld world,
        long attacker,
        long target,
        List<long> dead,
        HashSet<long> deadSet)
    {
        long damage = ResolveDamage(world, attacker, target);
        long cooldown = world.GetValue(attacker, SimComponents.Combat, SimComponents.Cooldown);
        world.SetValue(attacker, SimComponents.Combat, SimComponents.CooldownLeft, Math.Max(1, cooldown));
        if (world.GetValue(attacker, SimComponents.Combat, SimComponents.IsRanged) == 0)
        {
            ApplyDamage(world, target, damage, dead, deadSet);
        }
        else
        {
            ProjectileSystem.Launch(world, attacker, target, damage);
        }
    }

    /// <summary>
    /// 原作式伤害：按攻击类型扣目标对应护甲，最少 1，再叠加兵种克制附加伤害。
    /// 全程整数，保证跨端确定性。
    /// </summary>
    private static long ResolveDamage(SimWorld world, long attacker, long target)
    {
        long attackClass = world.GetValue(attacker, SimComponents.Combat, SimComponents.AttackClass);
        long baseDamage = world.GetValue(attacker, SimComponents.Combat, SimComponents.Damage);
        long damage = Math.Max(baseDamage - ArmorValue(world, target, attackClass), 1);
        long attackerType = world.GetValue(attacker, SimComponents.Combat, SimComponents.UnitType);
        damage += UnitDefs.BonusDamage(attackerType, TargetArmorClass(world, target));
        return Math.Max(damage, 1);
    }

    private static long ArmorValue(SimWorld world, long target, long attackClass)
    {
        string column = attackClass switch
        {
            UnitAttackClass.Pierce => SimComponents.ArmorPierce,
            UnitAttackClass.Siege => SimComponents.ArmorSiege,
            _ => SimComponents.ArmorMelee,
        };
        return world.GetValue(target, SimComponents.Combat, column);
    }

    /// <summary>建筑没有 Combat 组件，按建筑护甲类型结算，才能吃到攻城单位的克制加成。</summary>
    private static long TargetArmorClass(SimWorld world, long target) =>
        world.HasComponent(target, SimComponents.Building)
            ? UnitArmorClass.Building
            : world.GetValue(target, SimComponents.Combat, SimComponents.ArmorClass);

    private static void ApplyDamage(
        SimWorld world,
        long target,
        long damage,
        List<long> dead,
        HashSet<long> deadSet)
    {
        long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp) - damage;
        world.SetValue(target, SimComponents.Health, SimComponents.Hp, hp);
        if (hp <= 0 && deadSet.Add(target))
        {
            dead.Add(target);
        }
    }
}
