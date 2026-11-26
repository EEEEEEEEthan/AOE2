using System;

namespace AoeSim;

public static class MovementSystem
{
    public const long DiagonalScale = 46341;

    public static void Step(SimWorld world, NavGrid grid, Func<int, int, FlowField> fieldProvider)
    {
        ComponentStore moveStore = world.Store(SimComponents.Move);
        if (moveStore.Count == 0)
        {
            return;
        }
        foreach (long id in world.Entities())
        {
            if (!moveStore.Has(id))
            {
                continue;
            }
            if (moveStore.GetValue(id, SimComponents.Active) == 0)
            {
                continue;
            }
            StepEntity(world, id, grid, fieldProvider);
        }
    }

    private static void StepEntity(SimWorld world, long id, NavGrid grid, Func<int, int, FlowField> fieldProvider)
    {
        long x = world.GetValue(id, SimComponents.Position, SimComponents.X);
        long y = world.GetValue(id, SimComponents.Position, SimComponents.Y);
        int targetX = (int)world.GetValue(id, SimComponents.Move, SimComponents.TargetX);
        int targetY = (int)world.GetValue(id, SimComponents.Move, SimComponents.TargetY);
        long speed = world.GetValue(id, SimComponents.Move, SimComponents.Speed);
        int cellX = (int)Fixed.ToInt(x);
        int cellY = (int)Fixed.ToInt(y);
        if (cellX == targetX && cellY == targetY)
        {
            Stop(world, id);
            return;
        }
        FlowField field = fieldProvider(targetX, targetY);
        if (field == null || !field.Reachable(cellX, cellY))
        {
            Stop(world, id);
            return;
        }
        GridDirection direction = field.DirectionAt(cellX, cellY);
        if (direction.IsZero)
        {
            Stop(world, id);
            return;
        }
        long targetCenterX = Fixed.Add(Fixed.FromInt(targetX), Fixed.Half);
        long targetCenterY = Fixed.Add(Fixed.FromInt(targetY), Fixed.Half);
        long deltaX = Fixed.Sub(targetCenterX, x);
        long deltaY = Fixed.Sub(targetCenterY, y);
        if (Fixed.Abs(deltaX) <= speed && Fixed.Abs(deltaY) <= speed)
        {
            world.SetValue(id, SimComponents.Position, SimComponents.X, targetCenterX);
            world.SetValue(id, SimComponents.Position, SimComponents.Y, targetCenterY);
            world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
            world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
            Stop(world, id);
            return;
        }
        long velocityX;
        long velocityY;
        if (direction.X != 0 && direction.Y != 0)
        {
            long diagonalSpeed = Fixed.Mul(speed, DiagonalScale);
            velocityX = direction.X * diagonalSpeed;
            velocityY = direction.Y * diagonalSpeed;
        }
        else
        {
            velocityX = direction.X * speed;
            velocityY = direction.Y * speed;
        }
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, velocityX);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, velocityY);
        world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(x, velocityX));
        world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(y, velocityY));
    }

    private static void Stop(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }
}
