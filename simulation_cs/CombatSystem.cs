using System;
using System.Collections.Generic;

namespace AoeSim;

public static class CombatSystem
{
    [ThreadStatic]
    private static List<long>? _candidates;

    /// <summary>系统入口一次性解析的组件与列句柄，内层循环只做整型访问。</summary>
    private readonly struct CombatLayout
    {
        public readonly ComponentAccess Combat;
        public readonly ComponentAccess Position;
        public readonly ComponentAccess Velocity;
        public readonly ComponentAccess Move;
        public readonly ComponentAccess Team;
        public readonly ComponentAccess Health;
        public readonly ComponentAccess AttackTarget;
        public readonly ComponentAccess Engage;
        public readonly ComponentAccess Garrisoned;
        public readonly ComponentAccess Aboard;
        public readonly ComponentAccess Building;
        public readonly int CombatCooldownLeft;
        public readonly int CombatRangeMax;
        public readonly int CombatRangeMin;
        public readonly int CombatStanceColumn;
        public readonly int CombatCooldown;
        public readonly int CombatIsRanged;
        public readonly int CombatAttackClass;
        public readonly int CombatDamage;
        public readonly int CombatUnitType;
        public readonly int CombatArmorClass;
        public readonly int CombatArmorMelee;
        public readonly int CombatArmorPierce;
        public readonly int CombatArmorSiege;
        public readonly int PositionX;
        public readonly int PositionY;
        public readonly int VelocityX;
        public readonly int VelocityY;
        public readonly int MoveActive;
        public readonly int MoveTargetX;
        public readonly int MoveTargetY;
        public readonly int TeamId;
        public readonly int HealthHp;
        public readonly int AttackTargetEntity;
        public readonly int EngageAutoTarget;
        public readonly int EngageOriginX;
        public readonly int EngageOriginY;

        public CombatLayout(SimWorld world)
        {
            Combat = world.Access(SimComponents.Combat);
            Position = world.Access(SimComponents.Position);
            Velocity = world.Access(SimComponents.Velocity);
            Move = world.Access(SimComponents.Move);
            Team = world.Access(SimComponents.Team);
            Health = world.Access(SimComponents.Health);
            AttackTarget = world.Access(SimComponents.AttackTarget);
            Engage = world.Access(SimComponents.Engage);
            Garrisoned = world.Access(SimComponents.Garrisoned);
            Aboard = world.Access(SimComponents.Aboard);
            Building = world.Access(SimComponents.Building);
            CombatCooldownLeft = Combat.ColumnForWrite(SimComponents.CooldownLeft);
            CombatRangeMax = Combat.Column(SimComponents.RangeMax);
            CombatRangeMin = Combat.Column(SimComponents.RangeMin);
            CombatStanceColumn = Combat.Column(SimComponents.Stance);
            CombatCooldown = Combat.Column(SimComponents.Cooldown);
            CombatIsRanged = Combat.Column(SimComponents.IsRanged);
            CombatAttackClass = Combat.Column(SimComponents.AttackClass);
            CombatDamage = Combat.Column(SimComponents.Damage);
            CombatUnitType = Combat.Column(SimComponents.UnitType);
            CombatArmorClass = Combat.Column(SimComponents.ArmorClass);
            CombatArmorMelee = Combat.Column(SimComponents.ArmorMelee);
            CombatArmorPierce = Combat.Column(SimComponents.ArmorPierce);
            CombatArmorSiege = Combat.Column(SimComponents.ArmorSiege);
            PositionX = Position.Column(SimComponents.X);
            PositionY = Position.Column(SimComponents.Y);
            VelocityX = Velocity.ColumnForWrite(SimComponents.X);
            VelocityY = Velocity.ColumnForWrite(SimComponents.Y);
            MoveActive = Move.ColumnForWrite(SimComponents.Active);
            MoveTargetX = Move.ColumnForWrite(SimComponents.TargetX);
            MoveTargetY = Move.ColumnForWrite(SimComponents.TargetY);
            TeamId = Team.Column(SimComponents.TeamId);
            HealthHp = Health.ColumnForWrite(SimComponents.Hp);
            AttackTargetEntity = AttackTarget.Column(SimComponents.TargetEntity);
            EngageAutoTarget = Engage.ColumnForWrite(SimComponents.AutoTarget);
            EngageOriginX = Engage.ColumnForWrite(SimComponents.OriginX);
            EngageOriginY = Engage.ColumnForWrite(SimComponents.OriginY);
        }
    }

    public static List<long> Step(SimWorld world, SpatialHash spatial)
    {
        var dead = new List<long>();
        var deadSet = new HashSet<long>();
        var layout = new CombatLayout(world);
        if (layout.Combat.Store.Count == 0)
        {
            return dead;
        }
        foreach (long id in layout.Combat.Store.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            // 驻军中的单位已移出世界占位，不再参与索敌与射击。
            if (layout.Garrisoned.Has(id))
            {
                continue;
            }
            // 运输船上的乘员同样不参战，也不把船的结算卷入乘员。
            if (layout.Aboard.Has(id))
            {
                continue;
            }
            long explicitTarget = LockedTarget(world, layout, id);
            long cooldownLeft = layout.Combat.Get(id, layout.CombatCooldownLeft);
            if (cooldownLeft > 0)
            {
                layout.Combat.Set(id, layout.CombatCooldownLeft, cooldownLeft - 1);
                continue;
            }
            if (explicitTarget >= 0)
            {
                if (InRange(layout, id, explicitTarget))
                {
                    Attack(world, layout, id, explicitTarget, dead, deadSet);
                }
                continue;
            }
            AutoEngage(world, layout, spatial, id, dead, deadSet);
        }
        return dead;
    }

    /// <summary>
    /// 读取显式攻击目标并推进追击；目标非法或已死亡时清除锁定并停止移动。
    /// 返回有效的锁定目标 id，无锁定时返回 -1。显式指令优先级高于姿态自动索敌。
    /// </summary>
    private static long LockedTarget(SimWorld world, in CombatLayout layout, long attacker)
    {
        if (!layout.AttackTarget.Has(attacker))
        {
            return -1;
        }
        long target = layout.AttackTarget.Get(attacker, layout.AttackTargetEntity);
        if (!IsValidTarget(world, layout, attacker, target))
        {
            layout.AttackTarget.Detach(attacker);
            StopMoving(layout, attacker);
            return -1;
        }
        if (InRange(layout, attacker, target))
        {
            StopMoving(layout, attacker);
        }
        else
        {
            Chase(layout, attacker, target);
        }
        return target;
    }

    /// <summary>
    /// 按姿态决定是否自主索敌与追击：禁止还击不索敌；驻守只打射程内；防御短距离追击；
    /// 进攻在较大仇恨半径内追击。玩家移动中的单位不主动接战。
    /// </summary>
    private static void AutoEngage(
        SimWorld world,
        in CombatLayout layout,
        SpatialHash spatial,
        long attacker,
        List<long> dead,
        HashSet<long> deadSet)
    {
        if (layout.Combat.Get(attacker, layout.CombatRangeMax) <= 0)
        {
            return;
        }
        long stance = layout.Combat.Get(attacker, layout.CombatStanceColumn);
        if (!CombatStance.AutoAcquires(stance))
        {
            ClearEngagement(layout, attacker);
            return;
        }
        long target = EngagedTarget(layout, attacker);
        if (target >= 0 && (!IsValidTarget(world, layout, attacker, target) || !WithinLeash(layout, attacker, stance)))
        {
            ClearEngagement(layout, attacker);
            target = -1;
        }
        if (target < 0)
        {
            if (IsMoving(layout, attacker))
            {
                return;
            }
            target = AcquireTarget(world, layout, spatial, attacker, stance);
            if (target < 0)
            {
                return;
            }
            BeginEngagement(layout, attacker, target);
        }
        if (InRange(layout, attacker, target))
        {
            StopMoving(layout, attacker);
            Attack(world, layout, attacker, target, dead, deadSet);
            return;
        }
        if (CombatStance.LeashTiles(stance) == 0)
        {
            ClearEngagement(layout, attacker);
            return;
        }
        Chase(layout, attacker, target);
    }

    private static long EngagedTarget(in CombatLayout layout, long attacker)
    {
        if (!layout.Engage.Has(attacker))
        {
            return -1;
        }
        return layout.Engage.Get(attacker, layout.EngageAutoTarget);
    }

    private static void BeginEngagement(in CombatLayout layout, long attacker, long target)
    {
        layout.Engage.Attach(attacker);
        layout.Engage.Set(attacker, layout.EngageAutoTarget, target);
        layout.Engage.Set(attacker, layout.EngageOriginX, layout.Position.Get(attacker, layout.PositionX));
        layout.Engage.Set(attacker, layout.EngageOriginY, layout.Position.Get(attacker, layout.PositionY));
    }

    private static void ClearEngagement(in CombatLayout layout, long attacker)
    {
        if (!layout.Engage.Has(attacker))
        {
            return;
        }
        layout.Engage.Detach(attacker);
        StopMoving(layout, attacker);
    }

    /// <summary>以接战起点衡量是否仍在 leash 内；超出即放弃追击。</summary>
    private static bool WithinLeash(in CombatLayout layout, long attacker, long stance)
    {
        long leashTiles = CombatStance.LeashTiles(stance);
        if (leashTiles == 0)
        {
            return true;
        }
        long distance = CellDistanceBetween(
            layout.Position.Get(attacker, layout.PositionX),
            layout.Position.Get(attacker, layout.PositionY),
            layout.Engage.Get(attacker, layout.EngageOriginX),
            layout.Engage.Get(attacker, layout.EngageOriginY));
        return distance <= leashTiles;
    }

    private static bool IsMoving(in CombatLayout layout, long id)
    {
        if (!layout.Move.Has(id))
        {
            return false;
        }
        return layout.Move.Get(id, layout.MoveActive) != 0;
    }

    private static bool IsValidTarget(SimWorld world, in CombatLayout layout, long attacker, long target)
    {
        if (target == attacker || !world.IsAlive(target))
        {
            return false;
        }
        if (!layout.Health.Has(target) || !layout.Position.Has(target))
        {
            return false;
        }
        return layout.Team.Get(target, layout.TeamId) != layout.Team.Get(attacker, layout.TeamId);
    }

    private static bool InRange(in CombatLayout layout, long attacker, long target)
    {
        long rangeMin = layout.Combat.Get(attacker, layout.CombatRangeMin);
        long rangeMax = layout.Combat.Get(attacker, layout.CombatRangeMax);
        long distance = CellDistance(layout, attacker, target);
        return distance >= rangeMin && distance <= rangeMax;
    }

    private static long CellDistance(in CombatLayout layout, long first, long second) =>
        CellDistanceBetween(
            layout.Position.Get(first, layout.PositionX),
            layout.Position.Get(first, layout.PositionY),
            layout.Position.Get(second, layout.PositionX),
            layout.Position.Get(second, layout.PositionY));

    private static long CellDistanceBetween(long firstX, long firstY, long secondX, long secondY) =>
        Math.Max(
            Math.Abs((int)Fixed.ToInt(secondX) - (int)Fixed.ToInt(firstX)),
            Math.Abs((int)Fixed.ToInt(secondY) - (int)Fixed.ToInt(firstY)));

    private static void Chase(in CombatLayout layout, long attacker, long target)
    {
        if (!layout.Move.Has(attacker))
        {
            return;
        }
        layout.Move.Set(attacker, layout.MoveTargetX, Fixed.ToInt(layout.Position.Get(target, layout.PositionX)));
        layout.Move.Set(attacker, layout.MoveTargetY, Fixed.ToInt(layout.Position.Get(target, layout.PositionY)));
        layout.Move.Set(attacker, layout.MoveActive, 1);
    }

    private static void StopMoving(in CombatLayout layout, long id)
    {
        if (!layout.Move.Has(id))
        {
            return;
        }
        layout.Move.Set(id, layout.MoveActive, 0);
        layout.Velocity.Set(id, layout.VelocityX, 0);
        layout.Velocity.Set(id, layout.VelocityY, 0);
    }

    /// <summary>按姿态索敌：驻守只看射程内，防御/进攻在射程外再放宽若干格。</summary>
    private static long AcquireTarget(
        SimWorld world,
        in CombatLayout layout,
        SpatialHash spatial,
        long attacker,
        long stance)
    {
        long team = layout.Team.Get(attacker, layout.TeamId);
        long rangeMin = layout.Combat.Get(attacker, layout.CombatRangeMin);
        long rangeMax = layout.Combat.Get(attacker, layout.CombatRangeMax);
        long acquireMax = rangeMax + CombatStance.AcquireTiles(stance);
        long x = layout.Position.Get(attacker, layout.PositionX);
        long y = layout.Position.Get(attacker, layout.PositionY);
        long queryRadius = Fixed.Add(Fixed.FromInt(acquireMax * 2), Fixed.Half);
        List<long> candidates = _candidates ??= new List<long>();
        spatial.QueryRadius(x, y, queryRadius, candidates);
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
            if (!layout.Health.Has(candidate))
            {
                continue;
            }
            long candidateTeam = layout.Team.Get(candidate, layout.TeamId);
            // Gaia 野生动物只吃玩家显式攻击指令，不参与自动索敌，避免单位乱追猎物。
            if (candidateTeam == team || candidateTeam == GameSim.GaiaTeam)
            {
                continue;
            }
            long candidateX = layout.Position.Get(candidate, layout.PositionX);
            long candidateY = layout.Position.Get(candidate, layout.PositionY);
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
        in CombatLayout layout,
        long attacker,
        long target,
        List<long> dead,
        HashSet<long> deadSet)
    {
        long damage = ResolveDamage(layout, attacker, target);
        long cooldown = layout.Combat.Get(attacker, layout.CombatCooldown);
        layout.Combat.Set(attacker, layout.CombatCooldownLeft, Math.Max(1, cooldown));
        if (layout.Combat.Get(attacker, layout.CombatIsRanged) == 0)
        {
            ApplyDamage(layout, target, damage, dead, deadSet);
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
    private static long ResolveDamage(in CombatLayout layout, long attacker, long target)
    {
        long attackClass = layout.Combat.Get(attacker, layout.CombatAttackClass);
        long baseDamage = layout.Combat.Get(attacker, layout.CombatDamage);
        long damage = Math.Max(baseDamage - ArmorValue(layout, target, attackClass), 1);
        long attackerType = layout.Combat.Get(attacker, layout.CombatUnitType);
        damage += UnitDefs.BonusDamage(attackerType, TargetArmorClass(layout, target));
        return Math.Max(damage, 1);
    }

    private static long ArmorValue(in CombatLayout layout, long target, long attackClass)
    {
        int column = attackClass switch
        {
            UnitAttackClass.Pierce => layout.CombatArmorPierce,
            UnitAttackClass.Siege => layout.CombatArmorSiege,
            _ => layout.CombatArmorMelee,
        };
        return layout.Combat.Get(target, column);
    }

    /// <summary>建筑没有 Combat 组件，按建筑护甲类型结算，才能吃到攻城单位的克制加成。</summary>
    private static long TargetArmorClass(in CombatLayout layout, long target) =>
        layout.Building.Has(target)
            ? UnitArmorClass.Building
            : layout.Combat.Get(target, layout.CombatArmorClass);

    private static void ApplyDamage(
        in CombatLayout layout,
        long target,
        long damage,
        List<long> dead,
        HashSet<long> deadSet)
    {
        long hp = layout.Health.Get(target, layout.HealthHp) - damage;
        layout.Health.Set(target, layout.HealthHp, hp);
        if (hp <= 0 && deadSet.Add(target))
        {
            dead.Add(target);
        }
    }
}
