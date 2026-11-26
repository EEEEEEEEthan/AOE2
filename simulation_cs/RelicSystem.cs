using System;

namespace AoeSim;

/// <summary>
/// 圣物行为：拾取/携带同步、存放入带 RELIC_SLOT 的实体、存放后按周期产金。
/// 圣物的归属（HolderTeam）用于圣物胜利计数；金矿产量为整数计时，纳入校验和。
/// </summary>
public static class RelicSystem
{
    public static void Step(SimWorld world, Func<long, long> inventoryProvider)
    {
        ComponentStore relicStore = world.Store(SimComponents.Relic);
        if (relicStore.Count == 0)
        {
            return;
        }
        foreach (long id in relicStore.ActiveIds())
        {
            if (!world.IsAlive(id))
            {
                continue;
            }
            StepRelic(world, id);
        }
        StepDeposits(world);
        StepGold(world, inventoryProvider);
    }

    private static void StepRelic(SimWorld world, long id)
    {
        long carrier = world.GetValue(id, SimComponents.Relic, SimComponents.Carrier);
        long stored = world.GetValue(id, SimComponents.Relic, SimComponents.StoredIn);
        if (stored >= 0)
        {
            if (!IsSlot(world, stored))
            {
                world.SetValue(id, SimComponents.Relic, SimComponents.StoredIn, -1);
                world.SetValue(id, SimComponents.Relic, SimComponents.Carrier, -1);
                world.SetValue(id, SimComponents.Relic, SimComponents.HolderTeam, -1);
                return;
            }
            world.SetValue(
                id,
                SimComponents.Relic,
                SimComponents.HolderTeam,
                world.GetValue(stored, SimComponents.RelicSlot, SimComponents.TeamId));
            return;
        }
        if (carrier >= 0)
        {
            if (!world.IsAlive(carrier) || !world.HasComponent(carrier, SimComponents.Monk))
            {
                world.SetValue(id, SimComponents.Relic, SimComponents.Carrier, -1);
                world.SetValue(id, SimComponents.Relic, SimComponents.HolderTeam, -1);
                return;
            }
            world.SetValue(
                id,
                SimComponents.Position,
                SimComponents.X,
                world.GetValue(carrier, SimComponents.Position, SimComponents.X));
            world.SetValue(
                id,
                SimComponents.Position,
                SimComponents.Y,
                world.GetValue(carrier, SimComponents.Position, SimComponents.Y));
            world.SetValue(
                id,
                SimComponents.Relic,
                SimComponents.HolderTeam,
                world.GetValue(carrier, SimComponents.Team, SimComponents.TeamId));
            return;
        }
        long monk = FindPickupMonk(world, id);
        if (monk >= 0)
        {
            world.SetValue(id, SimComponents.Relic, SimComponents.Carrier, monk);
            world.SetValue(id, SimComponents.Relic, SimComponents.StoredIn, -1);
            world.SetValue(
                id,
                SimComponents.Relic,
                SimComponents.HolderTeam,
                world.GetValue(monk, SimComponents.Team, SimComponents.TeamId));
            world.SetValue(
                id,
                SimComponents.Position,
                SimComponents.X,
                world.GetValue(monk, SimComponents.Position, SimComponents.X));
            world.SetValue(
                id,
                SimComponents.Position,
                SimComponents.Y,
                world.GetValue(monk, SimComponents.Position, SimComponents.Y));
            world.SetValue(monk, SimComponents.Monk, SimComponents.State, MonkSystem.StateCarrying);
            world.SetValue(monk, SimComponents.Monk, SimComponents.TargetEntity, -1);
        }
    }

    private static long FindPickupMonk(SimWorld world, long relic)
    {
        ComponentStore monkStore = world.Store(SimComponents.Monk);
        if (monkStore.Count == 0)
        {
            return -1;
        }
        foreach (long id in world.Entities())
        {
            if (!monkStore.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Monk, SimComponents.State) != MonkSystem.StateSeekRelic)
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity) != relic)
            {
                continue;
            }
            if (InRange(world, id, relic))
            {
                return id;
            }
        }
        return -1;
    }

    private static void StepDeposits(SimWorld world)
    {
        ComponentStore monkStore = world.Store(SimComponents.Monk);
        if (monkStore.Count == 0)
        {
            return;
        }
        foreach (long id in world.Entities())
        {
            if (!monkStore.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Monk, SimComponents.State) != MonkSystem.StateSeekDeposit)
            {
                continue;
            }
            long slot = world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity);
            if (!IsSlot(world, slot))
            {
                continue;
            }
            long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            if (world.GetValue(slot, SimComponents.RelicSlot, SimComponents.TeamId) != team)
            {
                world.SetValue(id, SimComponents.Monk, SimComponents.State, MonkSystem.StateCarrying);
                world.SetValue(id, SimComponents.Monk, SimComponents.TargetEntity, -1);
                continue;
            }
            long relic = MonkSystem.CarriedRelic(world, id);
            if (relic < 0 || !InRange(world, id, slot))
            {
                continue;
            }
            world.SetValue(relic, SimComponents.Relic, SimComponents.Carrier, -1);
            world.SetValue(relic, SimComponents.Relic, SimComponents.StoredIn, slot);
            world.SetValue(relic, SimComponents.Relic, SimComponents.HolderTeam, team);
            world.SetValue(id, SimComponents.Monk, SimComponents.State, MonkSystem.StateIdle);
            world.SetValue(id, SimComponents.Monk, SimComponents.TargetEntity, -1);
        }
    }

    private static void StepGold(SimWorld world, Func<long, long> inventoryProvider)
    {
        ComponentStore slotStore = world.Store(SimComponents.RelicSlot);
        if (slotStore.Count == 0)
        {
            return;
        }
        foreach (long id in world.Entities())
        {
            if (!slotStore.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            long storedCount = StoredCount(world, id);
            if (storedCount <= 0)
            {
                world.SetValue(id, SimComponents.RelicSlot, SimComponents.GoldProgress, 0);
                continue;
            }
            long period = world.GetValue(id, SimComponents.RelicSlot, SimComponents.GoldPeriod);
            if (period <= 0)
            {
                period = 1;
            }
            long rate = world.GetValue(id, SimComponents.RelicSlot, SimComponents.GoldRate);
            long progress = world.GetValue(id, SimComponents.RelicSlot, SimComponents.GoldProgress) + storedCount;
            long gold = 0;
            while (progress >= period)
            {
                progress -= period;
                gold += rate;
            }
            world.SetValue(id, SimComponents.RelicSlot, SimComponents.GoldProgress, progress);
            if (gold <= 0)
            {
                continue;
            }
            long team = world.GetValue(id, SimComponents.RelicSlot, SimComponents.TeamId);
            long inventory = inventoryProvider(team);
            if (inventory < 0)
            {
                continue;
            }
            string column = SimComponents.ResourceColumns[(int)GameSim.ResourceGold];
            world.SetValue(
                inventory,
                SimComponents.Resource,
                column,
                world.GetValue(inventory, SimComponents.Resource, column) + gold);
        }
    }

    private static long StoredCount(SimWorld world, long slot)
    {
        ComponentStore relicStore = world.Store(SimComponents.Relic);
        if (relicStore.Count == 0)
        {
            return 0;
        }
        long count = 0;
        foreach (long id in world.Entities())
        {
            if (!relicStore.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Relic, SimComponents.StoredIn) == slot)
            {
                count += 1;
            }
        }
        return count;
    }

    private static bool IsSlot(SimWorld world, long id) =>
        world.IsAlive(id) && world.HasComponent(id, SimComponents.RelicSlot);

    private static bool InRange(SimWorld world, long first, long second)
    {
        long rangeSq = world.GetValue(first, SimComponents.Monk, SimComponents.RangeSq);
        long deltaX = Fixed.Sub(
            world.GetValue(second, SimComponents.Position, SimComponents.X),
            world.GetValue(first, SimComponents.Position, SimComponents.X));
        long deltaY = Fixed.Sub(
            world.GetValue(second, SimComponents.Position, SimComponents.Y),
            world.GetValue(first, SimComponents.Position, SimComponents.Y));
        return Fixed.Add(Fixed.Mul(deltaX, deltaX), Fixed.Mul(deltaY, deltaY)) <= rangeSq;
    }
}
