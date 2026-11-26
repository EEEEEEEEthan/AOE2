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

    /// <summary>系统入口一次性解析的组件与列句柄，内层循环只做整型访问。</summary>
    private readonly struct MovementLayout
    {
        public readonly ComponentAccess Position;
        public readonly ComponentAccess Move;
        public readonly ComponentAccess Velocity;
        public readonly ComponentAccess Domain;
        public readonly int PositionX;
        public readonly int PositionY;
        public readonly int VelocityX;
        public readonly int VelocityY;
        public readonly int MoveTargetX;
        public readonly int MoveTargetY;
        public readonly int MoveSpeed;
        public readonly int MoveActive;
        public readonly int DomainId;

        public MovementLayout(SimWorld world)
        {
            Position = world.Access(SimComponents.Position);
            Move = world.Access(SimComponents.Move);
            Velocity = world.Access(SimComponents.Velocity);
            Domain = world.Access(SimComponents.Domain);
            PositionX = Position.Column(SimComponents.X);
            PositionY = Position.Column(SimComponents.Y);
            VelocityX = Velocity.Column(SimComponents.X);
            VelocityY = Velocity.Column(SimComponents.Y);
            MoveTargetX = Move.Column(SimComponents.TargetX);
            MoveTargetY = Move.Column(SimComponents.TargetY);
            MoveSpeed = Move.Column(SimComponents.Speed);
            MoveActive = Move.Column(SimComponents.Active);
            DomainId = Domain.Column(SimComponents.DomainId);
        }
    }

    /// <summary>
    /// 单位通过 <paramref name="fieldProvider"/> 共享按目标格缓存的有界流场，各自按所在格取方向；
    /// 不为单个单位或分摊落点新建流场。
    /// </summary>
    public static void Step(
        SimWorld world,
        NavGrid grid,
        OccupancyMap occupancy,
        Func<int, int, long, FlowField> fieldProvider,
        TerrainGrid? terrain = null)
    {
        var layout = new MovementLayout(world);
        if (layout.Move.Store.Count == 0)
        {
            return;
        }
        occupancy.Sync(world);
        foreach (long id in layout.Move.Store.ActiveIds())
        {
            if (layout.Move.Get(id, layout.MoveActive) == 0)
            {
                continue;
            }
            StepEntity(layout, id, grid, occupancy, fieldProvider, terrain);
        }
    }

    private static void StepEntity(
        in MovementLayout layout,
        long id,
        NavGrid grid,
        OccupancyMap occupancy,
        Func<int, int, long, FlowField> fieldProvider,
        TerrainGrid? terrain)
    {
        long x = layout.Position.Get(id, layout.PositionX);
        long y = layout.Position.Get(id, layout.PositionY);
        int targetX = (int)layout.Move.Get(id, layout.MoveTargetX);
        int targetY = (int)layout.Move.Get(id, layout.MoveTargetY);
        long speed = layout.Move.Get(id, layout.MoveSpeed);
        int cellX = (int)Fixed.ToInt(x);
        int cellY = (int)Fixed.ToInt(y);
        if (cellX == targetX && cellY == targetY)
        {
            SnapToCenter(layout, id, targetX, targetY);
            Stop(layout, id);
            return;
        }
        long domain = layout.Domain.Has(id) ? layout.Domain.Get(id, layout.DomainId) : MovementDomain.Land;
        FlowField? field = fieldProvider(targetX, targetY, domain);
        if (field == null || !field.Reachable(cellX, cellY))
        {
            Stop(layout, id);
            return;
        }
        if (!ChooseNextCell(id, grid, occupancy, field, cellX, cellY, terrain, domain, out int nextX, out int nextY))
        {
            layout.Velocity.Set(id, layout.VelocityX, 0);
            layout.Velocity.Set(id, layout.VelocityY, 0);
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
        layout.Velocity.Set(id, layout.VelocityX, stepX);
        layout.Velocity.Set(id, layout.VelocityY, stepY);
        layout.Position.Set(id, layout.PositionX, newX);
        layout.Position.Set(id, layout.PositionY, newY);
        if (nextX == targetX && nextY == targetY && newX == centerX && newY == centerY)
        {
            Stop(layout, id);
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
        long domain,
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
            if (!Traversal.CellPassable(grid, terrain, candidateX, candidateY, domain))
            {
                continue;
            }
            if (offsetX != 0 && offsetY != 0
                && (!Traversal.CellPassable(grid, terrain, cellX + offsetX, cellY, domain)
                    || !Traversal.CellPassable(grid, terrain, cellX, cellY + offsetY, domain)))
            {
                continue;
            }
            if (terrain != null && !terrain.CanTraverse(cellX, cellY, candidateX, candidateY, domain))
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

    private static void SnapToCenter(in MovementLayout layout, long id, int targetX, int targetY)
    {
        layout.Position.Set(id, layout.PositionX, Fixed.Add(Fixed.FromInt(targetX), Fixed.Half));
        layout.Position.Set(id, layout.PositionY, Fixed.Add(Fixed.FromInt(targetY), Fixed.Half));
    }

    private static void Stop(in MovementLayout layout, long id)
    {
        layout.Move.Set(id, layout.MoveActive, 0);
        layout.Velocity.Set(id, layout.VelocityX, 0);
        layout.Velocity.Set(id, layout.VelocityY, 0);
    }
}

