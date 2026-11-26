using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 指令状态机：攻击移动 / 巡逻 / 守卫 / 跟随 / 原地待命 / 集结点。
/// 在 <see cref="MovementSystem"/> 之前执行，所有结算只用整数与实体 id 升序，纳入校验和。
/// </summary>
public static class OrderSystem
{
    public const long TypeNone = 0;
    public const long TypeAttackMove = 1;
    public const long TypePatrol = 2;
    public const long TypeGuard = 3;
    public const long TypeFollow = 4;
    public const long TypeHold = 5;

    public const long StateIdle = 0;
    public const long StateMoving = 1;
    public const long StateEngaging = 2;
    public const long StateReturning = 3;

    public const long DefaultAggroCells = 5;
    public const long DefaultLeashCells = 6;
    public const long DefaultFollowCells = 2;

    [ThreadStatic]
    private static List<long>? _candidates;

    public static void Step(SimWorld world, SpatialHash spatial)
    {
        ComponentStore orderStore = world.Store(SimComponents.Order);
        if (orderStore.Count == 0)
        {
            return;
        }
        foreach (long id in orderStore.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            StepEntity(world, spatial, id);
        }
    }

    public static void IssueAttackMove(SimWorld world, long id, long cellX, long cellY, long aux, bool queue)
    {
        if (!CanMoveOrder(world, id))
        {
            return;
        }
        long radius = aux > 0 ? aux : DefaultAggroCells;
        Start(world, id, TypeAttackMove, -1, cellX, cellY, 0, 0, radius, queue);
    }

    public static void IssuePatrol(SimWorld world, long id, long firstX, long firstY, long secondX, long secondY, bool queue)
    {
        if (!CanMoveOrder(world, id))
        {
            return;
        }
        Start(world, id, TypePatrol, -1, secondX, secondY, firstX, firstY, 0, queue);
    }

    public static void IssueGuard(SimWorld world, long id, long target, long leash, bool queue)
    {
        if (!CanOrder(world, id))
        {
            return;
        }
        long anchorX = 0;
        long anchorY = 0;
        if (TryPosition(world, id, out long selfX, out long selfY))
        {
            anchorX = selfX;
            anchorY = selfY;
        }
        long radius = leash > 0 ? leash : DefaultLeashCells;
        Start(world, id, TypeGuard, target, anchorX, anchorY, 0, 0, radius, queue);
    }

    public static void IssueFollow(SimWorld world, long id, long target, long distance, bool queue)
    {
        if (!CanMoveOrder(world, id) || !IsValidEntity(world, target))
        {
            return;
        }
        long gap = distance > 0 ? distance : DefaultFollowCells;
        Start(world, id, TypeFollow, target, 0, 0, 0, 0, gap, queue);
    }

    public static void IssueHold(SimWorld world, long id, bool queue)
    {
        if (!CanOrder(world, id))
        {
            return;
        }
        Begin(world, id, TypeHold, -1, 0, 0, 0, 0, 0, queue);
        StopMoving(world, id);
        world.Detach(id, SimComponents.AttackTarget);
        world.Detach(id, SimComponents.Engage);
        if (world.HasComponent(id, SimComponents.Combat))
        {
            world.SetValue(id, SimComponents.Combat, SimComponents.Stance, CombatStance.StandGround);
        }
    }

    public static void SetRally(SimWorld world, long id, long cellX, long cellY, long target)
    {
        if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Position))
        {
            return;
        }
        world.Attach(id, SimComponents.Rally);
        world.SetValue(id, SimComponents.Rally, SimComponents.RallyX, cellX);
        world.SetValue(id, SimComponents.Rally, SimComponents.RallyY, cellY);
        world.SetValue(id, SimComponents.Rally, SimComponents.RallyTarget, target);
    }

    /// <summary>产出单位时按建筑的集结点下发一次指令；无集结点返回 false。</summary>
    public static bool ApplyRally(SimWorld world, long building, long unit)
    {
        if (!world.HasComponent(building, SimComponents.Rally) || !world.IsAlive(unit))
        {
            return false;
        }
        long target = world.GetValue(building, SimComponents.Rally, SimComponents.RallyTarget);
        long cellX = world.GetValue(building, SimComponents.Rally, SimComponents.RallyX);
        long cellY = world.GetValue(building, SimComponents.Rally, SimComponents.RallyY);
        if (target >= 0 && IsValidEntity(world, target))
        {
            if (TryPosition(world, target, out long targetX, out long targetY))
            {
                cellX = targetX;
                cellY = targetY;
            }
        }
        IssueAttackMove(world, unit, cellX, cellY, 0, false);
        return true;
    }

    private static bool CanOrder(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.Position);

    private static bool CanMoveOrder(SimWorld world, long id) =>
        CanOrder(world, id) && world.HasComponent(id, SimComponents.Move);

    private static void Start(
        SimWorld world,
        long id,
        long type,
        long target,
        long cellX,
        long cellY,
        long auxX,
        long auxY,
        long aux,
        bool queue) =>
        Begin(world, id, type, target, cellX, cellY, auxX, auxY, aux, queue);

    private static void Begin(
        SimWorld world,
        long id,
        long type,
        long target,
        long cellX,
        long cellY,
        long auxX,
        long auxY,
        long aux,
        bool queue)
    {
        world.Attach(id, SimComponents.Order);
        if (queue && HasPendingOrder(world, id))
        {
            Enqueue(world, id, type, target, cellX, cellY, auxX, auxY, aux);
            return;
        }
        world.SetValue(id, SimComponents.Order, SimComponents.OrderType, type);
        world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
        world.SetValue(id, SimComponents.Order, SimComponents.TargetEntity, target);
        world.SetValue(id, SimComponents.Order, SimComponents.TargetX, cellX);
        world.SetValue(id, SimComponents.Order, SimComponents.TargetY, cellY);
        world.SetValue(id, SimComponents.Order, SimComponents.AuxX, auxX);
        world.SetValue(id, SimComponents.Order, SimComponents.AuxY, auxY);
        world.SetValue(id, SimComponents.Order, SimComponents.Aux, aux);
        if (!queue)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.QueueCount, 0);
        }
    }

    private static bool HasPendingOrder(SimWorld world, long id) =>
        world.HasComponent(id, SimComponents.Order)
        && world.GetValue(id, SimComponents.Order, SimComponents.OrderType) != TypeNone;

    private static void Enqueue(
        SimWorld world,
        long id,
        long type,
        long target,
        long cellX,
        long cellY,
        long auxX,
        long auxY,
        long aux)
    {
        long count = world.GetValue(id, SimComponents.Order, SimComponents.QueueCount);
        if (count < 0 || count >= SimComponents.QueueDepth)
        {
            return;
        }
        int slot = (int)count;
        world.SetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[slot], type);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueTargetColumns[slot], target);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueXColumns[slot], cellX);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueYColumns[slot], cellY);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxXColumns[slot], auxX);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxYColumns[slot], auxY);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxColumns[slot], aux);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueCount, count + 1);
    }

    private static void Complete(SimWorld world, long id)
    {
        StopMoving(world, id);
        long count = world.GetValue(id, SimComponents.Order, SimComponents.QueueCount);
        if (count <= 0)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.OrderType, TypeNone);
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
            world.Detach(id, SimComponents.AttackTarget);
            return;
        }
        world.SetValue(id, SimComponents.Order, SimComponents.OrderType,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.TargetEntity,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueTargetColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.TargetX,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueXColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.TargetY,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueYColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.AuxX,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxXColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.AuxY,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxYColumns[0]));
        world.SetValue(id, SimComponents.Order, SimComponents.Aux,
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxColumns[0]));
        for (int slot = 0; slot < count - 1; slot++)
        {
            CopySlot(world, id, slot + 1, slot);
        }
        int last = (int)(count - 1);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[last], TypeNone);
        world.SetValue(id, SimComponents.Order, SimComponents.QueueCount, count - 1);
        world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
    }

    private static void CopySlot(SimWorld world, long id, int from, int to)
    {
        world.SetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueTypeColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueTargetColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueTargetColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueXColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueXColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueYColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueYColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxXColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxXColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxYColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxYColumns[from]));
        world.SetValue(id, SimComponents.Order, SimComponents.QueueAuxColumns[to],
            world.GetValue(id, SimComponents.Order, SimComponents.QueueAuxColumns[from]));
    }

    private static void StepEntity(SimWorld world, SpatialHash spatial, long id)
    {
        long type = world.GetValue(id, SimComponents.Order, SimComponents.OrderType);
        switch (type)
        {
            case TypeAttackMove:
                StepAttackMove(world, spatial, id);
                break;
            case TypePatrol:
                StepPatrol(world, id);
                break;
            case TypeGuard:
                StepGuard(world, id);
                break;
            case TypeFollow:
                StepFollow(world, id);
                break;
            case TypeHold:
                StepHold(world, id);
                break;
        }
    }

    private static void StepAttackMove(SimWorld world, SpatialHash spatial, long id)
    {
        long state = world.GetValue(id, SimComponents.Order, SimComponents.OrderState);
        if (state == StateEngaging)
        {
            if (HasExplicitTarget(world, id))
            {
                return;
            }
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateMoving);
            state = StateMoving;
        }
        if (state == StateIdle)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateMoving);
        }
        long targetX = world.GetValue(id, SimComponents.Order, SimComponents.TargetX);
        long targetY = world.GetValue(id, SimComponents.Order, SimComponents.TargetY);
        CellOf(world, id, out int cellX, out int cellY);
        if (cellX == targetX && cellY == targetY)
        {
            Complete(world, id);
            return;
        }
        long radius = world.GetValue(id, SimComponents.Order, SimComponents.Aux);
        long enemy = NearestEnemy(world, spatial, id, radius);
        if (enemy >= 0)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateEngaging);
            StopMoving(world, id);
            world.Attach(id, SimComponents.AttackTarget);
            world.SetValue(id, SimComponents.AttackTarget, SimComponents.TargetEntity, enemy);
            return;
        }
        MoveTowards(world, id, targetX, targetY);
    }

    private static void StepPatrol(SimWorld world, long id)
    {
        long state = world.GetValue(id, SimComponents.Order, SimComponents.OrderState);
        if (state == StateIdle)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateMoving);
        }
        long targetX = world.GetValue(id, SimComponents.Order, SimComponents.TargetX);
        long targetY = world.GetValue(id, SimComponents.Order, SimComponents.TargetY);
        CellOf(world, id, out int cellX, out int cellY);
        if (cellX == targetX && cellY == targetY)
        {
            long otherX = world.GetValue(id, SimComponents.Order, SimComponents.AuxX);
            long otherY = world.GetValue(id, SimComponents.Order, SimComponents.AuxY);
            world.SetValue(id, SimComponents.Order, SimComponents.AuxX, targetX);
            world.SetValue(id, SimComponents.Order, SimComponents.AuxY, targetY);
            world.SetValue(id, SimComponents.Order, SimComponents.TargetX, otherX);
            world.SetValue(id, SimComponents.Order, SimComponents.TargetY, otherY);
            targetX = otherX;
            targetY = otherY;
        }
        MoveTowards(world, id, targetX, targetY);
    }

    private static void StepGuard(SimWorld world, long id)
    {
        long target = world.GetValue(id, SimComponents.Order, SimComponents.TargetEntity);
        long anchorX;
        long anchorY;
        if (target >= 0 && IsValidEntity(world, target) && TryPosition(world, target, out anchorX, out anchorY))
        {
            world.SetValue(id, SimComponents.Order, SimComponents.AuxX, anchorX);
            world.SetValue(id, SimComponents.Order, SimComponents.AuxY, anchorY);
        }
        else
        {
            anchorX = world.GetValue(id, SimComponents.Order, SimComponents.AuxX);
            anchorY = world.GetValue(id, SimComponents.Order, SimComponents.AuxY);
        }
        long leash = world.GetValue(id, SimComponents.Order, SimComponents.Aux);
        CellOf(world, id, out int cellX, out int cellY);
        long distance = Math.Max(Math.Abs(cellX - anchorX), Math.Abs(cellY - anchorY));
        if (distance > leash)
        {
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateReturning);
            MoveTowards(world, id, anchorX, anchorY);
            return;
        }
        long state = world.GetValue(id, SimComponents.Order, SimComponents.OrderState);
        if (state == StateReturning || state == StateMoving)
        {
            StopMoving(world, id);
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
        }
    }

    private static void StepFollow(SimWorld world, long id)
    {
        long target = world.GetValue(id, SimComponents.Order, SimComponents.TargetEntity);
        if (target < 0 || !IsValidEntity(world, target) || !TryPosition(world, target, out long anchorX, out long anchorY))
        {
            Complete(world, id);
            return;
        }
        long gap = world.GetValue(id, SimComponents.Order, SimComponents.Aux);
        CellOf(world, id, out int cellX, out int cellY);
        long distance = Math.Max(Math.Abs(cellX - anchorX), Math.Abs(cellY - anchorY));
        if (distance > gap)
        {
            MoveTowards(world, id, anchorX, anchorY);
            world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateMoving);
            return;
        }
        StopMoving(world, id);
        world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
    }

    private static void StepHold(SimWorld world, long id)
    {
        StopMoving(world, id);
        world.Detach(id, SimComponents.Engage);
        world.SetValue(id, SimComponents.Order, SimComponents.OrderState, StateIdle);
    }

    private static void MoveTowards(SimWorld world, long id, long cellX, long cellY)
    {
        if (!world.HasComponent(id, SimComponents.Move))
        {
            return;
        }
        world.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        world.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    private static void StopMoving(SimWorld world, long id)
    {
        if (world.HasComponent(id, SimComponents.Move))
        {
            world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
            world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
            world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
        }
    }

    private static bool HasExplicitTarget(SimWorld world, long id)
    {
        if (!world.HasComponent(id, SimComponents.AttackTarget))
        {
            return false;
        }
        long target = world.GetValue(id, SimComponents.AttackTarget, SimComponents.TargetEntity);
        return target >= 0 && IsValidEntity(world, target)
            && world.GetValue(target, SimComponents.Team, SimComponents.TeamId)
                != world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
    }

    private static bool IsValidEntity(SimWorld world, long id) =>
        id >= 0 && world.IsAlive(id) && world.HasComponent(id, SimComponents.Position);

    private static bool TryPosition(SimWorld world, long id, out long cellX, out long cellY)
    {
        if (!IsValidEntity(world, id))
        {
            cellX = 0;
            cellY = 0;
            return false;
        }
        CellOf(world, id, out int x, out int y);
        cellX = x;
        cellY = y;
        return true;
    }

    private static void CellOf(SimWorld world, long id, out int cellX, out int cellY)
    {
        cellX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        cellY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
    }

    private static long NearestEnemy(SimWorld world, SpatialHash spatial, long attacker, long radiusCells)
    {
        if (radiusCells <= 0)
        {
            return -1;
        }
        long team = world.GetValue(attacker, SimComponents.Team, SimComponents.TeamId);
        long x = world.GetValue(attacker, SimComponents.Position, SimComponents.X);
        long y = world.GetValue(attacker, SimComponents.Position, SimComponents.Y);
        List<long> candidates = _candidates ??= new List<long>();
        spatial.QueryRadius(x, y, Fixed.FromInt(radiusCells + 1), candidates);
        int attackerX = (int)Fixed.ToInt(x);
        int attackerY = (int)Fixed.ToInt(y);
        long best = -1;
        long bestCellDistance = 0;
        long bestDistanceSq = 0;
        foreach (long candidate in candidates)
        {
            if (candidate == attacker || !world.IsAlive(candidate))
            {
                continue;
            }
            if (!world.HasComponent(candidate, SimComponents.Health)
                || !world.HasComponent(candidate, SimComponents.Position))
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
                Math.Abs((int)Fixed.ToInt(candidateX) - attackerX),
                Math.Abs((int)Fixed.ToInt(candidateY) - attackerY));
            if (cellDistance > radiusCells)
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
}
