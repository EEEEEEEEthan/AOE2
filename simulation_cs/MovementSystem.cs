using System;

namespace AoeSim;

public static class MovementSystem
{
    public const long DiagonalScale = 46341;

    private static readonly int[,] Neighbors =
    {
        { 1, 0 },
        { -1, 0 },
        { 0, 1 },
        { 0, -1 },
        { 1, 1 },
        { 1, -1 },
        { -1, 1 },
        { -1, -1 },
    };

    public static void Step(
        SimWorld world,
        NavGrid grid,
        OccupancyMap occupancy,
        Func<int, int, FlowField> fieldProvider,
        TerrainGrid? terrain = null)
    {
        ComponentStore moveStore = world.Store(SimComponents.Move);
        if (moveStore.Count == 0)
        {
            return;
        }
        occupancy.Rebuild(world);
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
            StepEntity(world, id, grid, occupancy, fieldProvider, terrain);
        }
    }

    private static void StepEntity(
        SimWorld world,
        long id,
        NavGrid grid,
        OccupancyMap occupancy,
        Func<int, int, FlowField> fieldProvider,
        TerrainGrid? terrain)
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
            SnapToCenter(world, id, targetX, targetY);
            Stop(world, id);
            return;
        }
        FlowField? field = fieldProvider(targetX, targetY);
        if (field == null || !field.Reachable(cellX, cellY))
        {
            Stop(world, id);
            return;
        }
        if (!ChooseNextCell(id, grid, occupancy, field, cellX, cellY, terrain, out int nextX, out int nextY))
        {
            world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
            world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
            return;
        }
        occupancy.Stamp(id, nextX, nextY);
        long centerX = Fixed.Add(Fixed.FromInt(nextX), Fixed.Half);
        long centerY = Fixed.Add(Fixed.FromInt(nextY), Fixed.Half);
        long deltaX = Fixed.Sub(centerX, x);
        long deltaY = Fixed.Sub(centerY, y);
        long stepX;
        long stepY;
        if (deltaX != 0 && deltaY != 0)
        {
            long diagonalSpeed = Fixed.Mul(speed, DiagonalScale);
            stepX = StepAxis(deltaX, diagonalSpeed);
            stepY = StepAxis(deltaY, diagonalSpeed);
        }
        else
        {
            stepX = StepAxis(deltaX, speed);
            stepY = StepAxis(deltaY, speed);
        }
        long newX = Fixed.Add(x, stepX);
        long newY = Fixed.Add(y, stepY);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, stepX);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, stepY);
        world.SetValue(id, SimComponents.Position, SimComponents.X, newX);
        world.SetValue(id, SimComponents.Position, SimComponents.Y, newY);
        if (nextX == targetX && nextY == targetY && newX == centerX && newY == centerY)
        {
            Stop(world, id);
        }
    }

    /// <summary>按流场方向优先、次优代价排序，挑一个未被静态障碍或其它单位占用的下一格。</summary>
    private static bool ChooseNextCell(
        long id,
        NavGrid grid,
        OccupancyMap occupancy,
        FlowField field,
        int cellX,
        int cellY,
        TerrainGrid? terrain,
        out int nextX,
        out int nextY)
    {
        nextX = cellX;
        nextY = cellY;
        int currentCost = field.CostAt(cellX, cellY);
        if (currentCost <= 0)
        {
            return false;
        }
        GridDirection primary = field.DirectionAt(cellX, cellY);
        int bestCost = int.MaxValue;
        int bestRank = int.MaxValue;
        for (int index = 0; index < Neighbors.GetLength(0); index++)
        {
            int offsetX = Neighbors[index, 0];
            int offsetY = Neighbors[index, 1];
            int candidateX = cellX + offsetX;
            int candidateY = cellY + offsetY;
            if (!grid.InBounds(candidateX, candidateY) || grid.IsBlocked(candidateX, candidateY))
            {
                continue;
            }
            if (offsetX != 0 && offsetY != 0
                && (grid.IsBlocked(cellX + offsetX, cellY) || grid.IsBlocked(cellX, cellY + offsetY)))
            {
                continue;
            }
            if (terrain != null && !terrain.CanTraverse(cellX, cellY, candidateX, candidateY))
            {
                continue;
            }
            int candidateCost = field.CostAt(candidateX, candidateY);
            if (candidateCost < 0 || candidateCost >= currentCost)
            {
                continue;
            }
            long owner = occupancy.OwnerAt(candidateX, candidateY);
            if (owner != -1 && owner != id)
            {
                continue;
            }
            int rank = offsetX == primary.X && offsetY == primary.Y ? -1 : index;
            if (candidateCost < bestCost || (candidateCost == bestCost && rank < bestRank))
            {
                bestCost = candidateCost;
                bestRank = rank;
                nextX = candidateX;
                nextY = candidateY;
            }
        }
        return bestCost != int.MaxValue;
    }

    private static long StepAxis(long delta, long axisSpeed)
    {
        if (delta == 0)
        {
            return 0;
        }
        if (Fixed.Abs(delta) <= axisSpeed)
        {
            return delta;
        }
        return delta > 0 ? axisSpeed : -axisSpeed;
    }

    private static void SnapToCenter(SimWorld world, long id, int targetX, int targetY)
    {
        world.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(targetX), Fixed.Half));
        world.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(targetY), Fixed.Half));
    }

    private static void Stop(SimWorld world, long id)
    {
        world.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        world.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }
}
