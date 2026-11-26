namespace AoeSim;

/// <summary>
/// 僧侣行为：治疗友军、招降敌军、拾取/携带/存放圣物。
/// 全部按实体 id 升序推进，招降进度为整数累加，确保确定性。
/// </summary>
public static class MonkSystem
{
    public const long StateIdle = 0;
    public const long StateHealing = 1;
    public const long StateConverting = 2;
    public const long StateCarrying = 3;
    public const long StateSeekRelic = 4;
    public const long StateSeekDeposit = 5;

    public static void Step(SimWorld world)
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
            StepMonk(world, id);
        }
    }

    private static void StepMonk(SimWorld world, long id)
    {
        long hp = world.GetValue(id, SimComponents.Health, SimComponents.Hp);
        long lastHp = world.GetValue(id, SimComponents.Monk, SimComponents.LastHp);
        long state = world.GetValue(id, SimComponents.Monk, SimComponents.State);
        if (hp < lastHp && state == StateConverting)
        {
            world.SetValue(id, SimComponents.Monk, SimComponents.ConvertProgress, 0);
        }
        world.SetValue(id, SimComponents.Monk, SimComponents.LastHp, hp);

        switch (state)
        {
            case StateHealing:
                StepHealing(world, id);
                break;
            case StateConverting:
                StepConverting(world, id);
                break;
            case StateSeekRelic:
                StepSeekRelic(world, id);
                break;
            case StateSeekDeposit:
                StepSeekDeposit(world, id);
                break;
            case StateCarrying:
                if (CarriedRelic(world, id) < 0)
                {
                    BecomeIdle(world, id);
                }
                break;
        }
    }

    private static void StepHealing(SimWorld world, long id)
    {
        long target = world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity);
        if (!IsFriendlyPatient(world, id, target))
        {
            BecomeIdle(world, id);
            return;
        }
        if (!InRange(world, id, target))
        {
            Chase(world, id, target);
            return;
        }
        Stop(world, id);
        long healRate = world.GetValue(id, SimComponents.Monk, SimComponents.HealRate);
        long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp);
        long maxHp = world.GetValue(target, SimComponents.Health, SimComponents.MaxHp);
        world.SetValue(target, SimComponents.Health, SimComponents.Hp, Fixed.Min(maxHp, hp + healRate));
    }

    private static void StepConverting(SimWorld world, long id)
    {
        long target = world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity);
        if (!IsValidEnemy(world, id, target))
        {
            BecomeIdle(world, id);
            return;
        }
        if (!InRange(world, id, target))
        {
            world.SetValue(id, SimComponents.Monk, SimComponents.ConvertProgress, 0);
            Chase(world, id, target);
            return;
        }
        Stop(world, id);
        long rate = world.GetValue(id, SimComponents.Monk, SimComponents.ConvertRate);
        long resistance = world.HasComponent(target, SimComponents.Monk)
            ? world.GetValue(target, SimComponents.Monk, SimComponents.ConversionResistance)
            : 0;
        long progress = world.GetValue(id, SimComponents.Monk, SimComponents.ConvertProgress)
            + Fixed.Max(1, rate - resistance);
        long required = world.GetValue(id, SimComponents.Monk, SimComponents.ConvertRequired);
        if (required <= 0)
        {
            required = 1;
        }
        if (progress < required)
        {
            world.SetValue(id, SimComponents.Monk, SimComponents.ConvertProgress, progress);
            return;
        }
        world.SetValue(
            target,
            SimComponents.Team,
            SimComponents.TeamId,
            world.GetValue(id, SimComponents.Team, SimComponents.TeamId));
        if (world.HasComponent(target, SimComponents.Monk))
        {
            BecomeIdle(world, target);
        }
        BecomeIdle(world, id);
    }

    private static void StepSeekRelic(SimWorld world, long id)
    {
        long relic = world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity);
        if (!IsGroundRelic(world, relic))
        {
            BecomeIdle(world, id);
            return;
        }
        if (!InRange(world, id, relic))
        {
            Chase(world, id, relic);
            return;
        }
        Stop(world, id);
    }

    private static void StepSeekDeposit(SimWorld world, long id)
    {
        if (CarriedRelic(world, id) < 0)
        {
            BecomeIdle(world, id);
            return;
        }
        long slot = world.GetValue(id, SimComponents.Monk, SimComponents.TargetEntity);
        if (!IsSlot(world, slot))
        {
            world.SetValue(id, SimComponents.Monk, SimComponents.State, StateCarrying);
            world.SetValue(id, SimComponents.Monk, SimComponents.TargetEntity, -1);
            return;
        }
        if (!InRange(world, id, slot))
        {
            Chase(world, id, slot);
            return;
        }
        Stop(world, id);
    }

    public static long CarriedRelic(SimWorld world, long monk)
    {
        ComponentStore relicStore = world.Store(SimComponents.Relic);
        if (relicStore.Count == 0)
        {
            return -1;
        }
        foreach (long id in world.Entities())
        {
            if (!relicStore.Has(id) || !world.IsAlive(id))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Relic, SimComponents.Carrier) == monk)
            {
                return id;
            }
        }
        return -1;
    }

    private static bool IsGroundRelic(SimWorld world, long relic)
    {
        if (!world.IsAlive(relic) || !world.HasComponent(relic, SimComponents.Relic))
        {
            return false;
        }
        return world.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) < 0
            && world.GetValue(relic, SimComponents.Relic, SimComponents.StoredIn) < 0;
    }

    private static bool IsSlot(SimWorld world, long slot) =>
        world.IsAlive(slot) && world.HasComponent(slot, SimComponents.RelicSlot);

    private static bool IsFriendlyPatient(SimWorld world, long monk, long target)
    {
        if (target == monk || !world.IsAlive(target))
        {
            return false;
        }
        if (!world.HasComponent(target, SimComponents.Health)
            || !world.HasComponent(target, SimComponents.Position))
        {
            return false;
        }
        if (world.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(monk, SimComponents.Team, SimComponents.TeamId))
        {
            return false;
        }
        long hp = world.GetValue(target, SimComponents.Health, SimComponents.Hp);
        long maxHp = world.GetValue(target, SimComponents.Health, SimComponents.MaxHp);
        return hp < maxHp;
    }

    private static bool IsValidEnemy(SimWorld world, long monk, long target)
    {
        if (target == monk || !world.IsAlive(target))
        {
            return false;
        }
        if (!world.HasComponent(target, SimComponents.Health)
            || !world.HasComponent(target, SimComponents.Position))
        {
            return false;
        }
        return world.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            != world.GetValue(monk, SimComponents.Team, SimComponents.TeamId);
    }

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

    private static void Chase(SimWorld world, long id, long target)
    {
        if (!world.HasComponent(id, SimComponents.Move))
        {
            return;
        }
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
        if (!world.HasComponent(id, SimComponents.Move))
        {
            return;
        }
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }

    private static void BecomeIdle(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Monk, SimComponents.State, StateIdle);
        world.SetValue(id, SimComponents.Monk, SimComponents.TargetEntity, -1);
        world.SetValue(id, SimComponents.Monk, SimComponents.ConvertProgress, 0);
        Stop(world, id);
    }
}
