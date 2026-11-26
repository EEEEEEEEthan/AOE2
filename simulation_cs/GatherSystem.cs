using System;

namespace AoeSim;

public static class GatherSystem
{
    public const long GatherStateIdle = 0;
    public const long GatherStateGathering = 1;
    public const long GatherStateReturning = 2;

    public const long GatherRangeSq = 147456;

    public static void Step(SimWorld world, Func<long, long> inventoryProvider)
    {
        ComponentStore gatherStore = world.Store(SimComponents.Gather);
        if (gatherStore.Count == 0)
        {
            return;
        }
        foreach (long id in gatherStore.ActiveIds())
        {
            long state = gatherStore.GetValue(id, SimComponents.GatherState);
            if (state == GatherStateGathering)
            {
                StepGathering(world, id);
            }
            else if (state == GatherStateReturning)
            {
                StepReturning(world, id, inventoryProvider);
            }
        }
    }

    private static void StepGathering(SimWorld world, long id)
    {
        long node = world.GetValue(id, SimComponents.Gather, SimComponents.NodeEntity);
        long remaining = 0;
        if (IsResourceNode(world, node))
        {
            remaining = world.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining);
        }
        if (remaining <= 0)
        {
            LeaveGathering(world, id);
            return;
        }
        if (!InRange(world, id, node))
        {
            MoveToCell(world, id, node);
            return;
        }
        Stop(world, id);
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long carryCapacity = world.GetValue(id, SimComponents.Gather, SimComponents.CarryCapacity);
        if (carryAmount == 0)
        {
            world.SetValue(
                id,
                SimComponents.Gather,
                SimComponents.CarryType,
                world.GetValue(node, SimComponents.ResourceNode, SimComponents.NodeType));
        }
        long progress = world.GetValue(id, SimComponents.Gather, SimComponents.Progress)
            + world.GetValue(id, SimComponents.Gather, SimComponents.Rate);
        while (progress >= Fixed.Scale && carryAmount < carryCapacity && remaining > 0)
        {
            progress -= Fixed.Scale;
            carryAmount += 1;
            remaining -= 1;
        }
        world.SetValue(id, SimComponents.Gather, SimComponents.Progress, progress);
        world.SetValue(id, SimComponents.Gather, SimComponents.CarryAmount, carryAmount);
        world.SetValue(node, SimComponents.ResourceNode, SimComponents.Remaining, remaining);
        if (carryAmount >= carryCapacity || remaining <= 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateReturning);
        }
    }

    private static void StepReturning(SimWorld world, long id, Func<long, long> inventoryProvider)
    {
        long drop = world.GetValue(id, SimComponents.Gather, SimComponents.DropEntity);
        if (!IsDropSite(world, drop))
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
            return;
        }
        if (!InRange(world, id, drop))
        {
            MoveToCell(world, id, drop);
            return;
        }
        Stop(world, id);
        long carryType = world.GetValue(id, SimComponents.Gather, SimComponents.CarryType);
        long acceptMask = world.GetValue(drop, SimComponents.DropSite, SimComponents.AcceptMask);
        if ((acceptMask & (1L << (int)carryType)) == 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
            return;
        }
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long inventory = inventoryProvider(world.GetValue(id, SimComponents.Team, SimComponents.TeamId));
        string column = SimComponents.ResourceColumns[(int)carryType];
        long stored = world.GetValue(inventory, SimComponents.Resource, column);
        world.SetValue(inventory, SimComponents.Resource, column, stored + carryAmount);
        world.SetValue(id, SimComponents.Gather, SimComponents.CarryAmount, 0);
        world.SetValue(id, SimComponents.Gather, SimComponents.Progress, 0);
        long node = world.GetValue(id, SimComponents.Gather, SimComponents.NodeEntity);
        if (IsResourceNode(world, node)
            && world.GetValue(node, SimComponents.ResourceNode, SimComponents.Remaining) > 0)
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateGathering);
        }
        else
        {
            world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherStateIdle);
        }
    }

    private static void LeaveGathering(SimWorld world, long id)
    {
        long carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
        long nextState = carryAmount > 0 ? GatherStateReturning : GatherStateIdle;
        world.SetValue(id, SimComponents.Gather, SimComponents.GatherState, nextState);
    }

    private static bool IsResourceNode(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.ResourceNode);

    private static bool IsDropSite(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.DropSite);

    private static bool InRange(SimWorld world, long first, long second)
    {
        long firstX = world.GetValue(first, SimComponents.Position, SimComponents.X);
        long firstY = world.GetValue(first, SimComponents.Position, SimComponents.Y);
        long secondX = world.GetValue(second, SimComponents.Position, SimComponents.X);
        long secondY = world.GetValue(second, SimComponents.Position, SimComponents.Y);
        long deltaX = Fixed.Sub(secondX, firstX);
        long deltaY = Fixed.Sub(secondY, firstY);
        long distanceSq = Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY));
        return distanceSq <= GatherRangeSq;
    }

    private static void MoveToCell(SimWorld world, long id, long target)
    {
        world.SetValue(
            id,
            SimComponents.Move,
            SimComponents.TargetX,
            Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.X)));
        world.SetValue(
            id,
            SimComponents.Move,
            SimComponents.TargetY,
            Fixed.ToInt(world.GetValue(target, SimComponents.Position, SimComponents.Y)));
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    private static void Stop(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }
}
