using System.Collections.Generic;

namespace AoeSim;

public readonly struct GridDirection
{
    public readonly int X;
    public readonly int Y;

    public GridDirection(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static GridDirection Zero => new(0, 0);

    public bool IsZero => X == 0 && Y == 0;
}

public sealed class FlowField
{
    private static readonly int[,] NeighborsOffsets =
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

    public int Width { get; }
    public int Height { get; }
    public int GoalX { get; private set; }
    public int GoalY { get; private set; }
    public int[] Cost { get; }
    public int[] DirX { get; }
    public int[] DirY { get; }

    public FlowField(NavGrid grid)
    {
        Width = grid.Width;
        Height = grid.Height;
        Cost = new int[Width * Height];
        DirX = new int[Width * Height];
        DirY = new int[Width * Height];
        System.Array.Fill(Cost, -1);
    }

    public bool Compute(NavGrid grid, int targetX, int targetY)
    {
        GoalX = targetX;
        GoalY = targetY;
        System.Array.Fill(Cost, -1);
        System.Array.Fill(DirX, 0);
        System.Array.Fill(DirY, 0);
        if (!grid.InBounds(targetX, targetY) || grid.IsBlocked(targetX, targetY))
        {
            return false;
        }
        int start = grid.Index(targetX, targetY);
        Cost[start] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int currentX = current % Width;
            int currentY = current / Width;
            int nextCost = Cost[current] + 1;
            for (int neighborIndex = 0; neighborIndex < NeighborsOffsets.GetLength(0); neighborIndex++)
            {
                int offsetX = NeighborsOffsets[neighborIndex, 0];
                int offsetY = NeighborsOffsets[neighborIndex, 1];
                int neighborX = currentX + offsetX;
                int neighborY = currentY + offsetY;
                if (!grid.InBounds(neighborX, neighborY) || grid.IsBlocked(neighborX, neighborY))
                {
                    continue;
                }
                if (offsetX != 0 && offsetY != 0)
                {
                    if (grid.IsBlocked(currentX + offsetX, currentY) || grid.IsBlocked(currentX, currentY + offsetY))
                    {
                        continue;
                    }
                }
                int neighbor = grid.Index(neighborX, neighborY);
                if (Cost[neighbor] != -1)
                {
                    continue;
                }
                Cost[neighbor] = nextCost;
                queue.Enqueue(neighbor);
            }
        }
        BuildDirections(grid);
        return true;
    }

    public GridDirection DirectionAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return GridDirection.Zero;
        }
        int index = y * Width + x;
        return new GridDirection(DirX[index], DirY[index]);
    }

    public int CostAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return -1;
        }
        return Cost[y * Width + x];
    }

    public bool Reachable(int x, int y) => CostAt(x, y) >= 0;

    private void BuildDirections(NavGrid grid)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = y * Width + x;
                int currentCost = Cost[index];
                if (currentCost <= 0)
                {
                    continue;
                }
                int bestOffsetX = 0;
                int bestOffsetY = 0;
                int bestCost = currentCost;
                for (int neighborIndex = 0; neighborIndex < NeighborsOffsets.GetLength(0); neighborIndex++)
                {
                    int offsetX = NeighborsOffsets[neighborIndex, 0];
                    int offsetY = NeighborsOffsets[neighborIndex, 1];
                    int neighborX = x + offsetX;
                    int neighborY = y + offsetY;
                    if (neighborX < 0 || neighborY < 0 || neighborX >= Width || neighborY >= Height)
                    {
                        continue;
                    }
                    if (offsetX != 0 && offsetY != 0)
                    {
                        if (grid.IsBlocked(x + offsetX, y) || grid.IsBlocked(x, y + offsetY))
                        {
                            continue;
                        }
                    }
                    int neighborCost = Cost[neighborY * Width + neighborX];
                    if (neighborCost < 0 || neighborCost >= bestCost)
                    {
                        continue;
                    }
                    bestCost = neighborCost;
                    bestOffsetX = offsetX;
                    bestOffsetY = offsetY;
                }
                DirX[index] = bestOffsetX;
                DirY[index] = bestOffsetY;
            }
        }
    }
}
