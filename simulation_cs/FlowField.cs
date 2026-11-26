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

    private const int OrthogonalCost = 1000;
    private const int DiagonalCost = 1414;

    /// <summary>
    /// 桶队列的桶数取「最大单步代价 + 1」：边权只有 1000/1414，环形桶保证按距离单调出队，
    /// 出队时父格已定值，方向可在同一次遍历里结算，省掉第二遍全图扫描。
    /// </summary>
    private const int BucketCount = DiagonalCost + 1;

    public int Width { get; }
    public int Height { get; }
    public int GoalX { get; private set; }
    public int GoalY { get; private set; }
    public int[] Cost { get; }
    public int[] DirX { get; }
    public int[] DirY { get; }

    private TerrainGrid? _terrain;
    private long _domain;

    // 桶队列的暂存区按线程复用（仿真单线程、Compute 不可重入），
    // 避免每张流场再额外分配三张 W×H 数组；线程隔离保证多仿真线程互不干扰。
    // ThreadStatic 的字段初始值只在首个线程生效，其它线程为 null，由 EnsureScratch 兜底分配。
    [System.ThreadStatic] private static int[] _bucketHeads = System.Array.Empty<int>();
    [System.ThreadStatic] private static int[] _bucketPrev = System.Array.Empty<int>();
    [System.ThreadStatic] private static int[] _bucketNext = System.Array.Empty<int>();
    [System.ThreadStatic] private static int[] _bucketOf = System.Array.Empty<int>();

    public FlowField(NavGrid grid, TerrainGrid? terrain = null, long domain = MovementDomain.Land)
    {
        Width = grid.Width;
        Height = grid.Height;
        _terrain = terrain;
        _domain = domain;
        Cost = new int[Width * Height];
        DirX = new int[Width * Height];
        DirY = new int[Width * Height];
        System.Array.Fill(Cost, -1);
    }

    /// <summary>从对象池取出后重设通行域，复用已分配的数组。</summary>
    internal void Reconfigure(TerrainGrid? terrain, long domain)
    {
        _terrain = terrain;
        _domain = domain;
    }

    private bool Passable(NavGrid grid, int x, int y) =>
        Traversal.CellPassable(grid, _terrain, x, y, _domain);

    public bool Compute(NavGrid grid, int targetX, int targetY)
    {
        GoalX = targetX;
        GoalY = targetY;
        System.Array.Fill(Cost, -1);
        System.Array.Fill(DirX, 0);
        System.Array.Fill(DirY, 0);
        if (!Passable(grid, targetX, targetY))
        {
            return false;
        }
        EnsureScratch();
        int cellCount = Width * Height;
        System.Array.Fill(_bucketOf, -1, 0, cellCount);
        System.Array.Fill(_bucketHeads, -1, 0, BucketCount);

        int start = grid.Index(targetX, targetY);
        Cost[start] = 0;
        EnqueueCell(start, 0);
        int queued = 1;
        int currentDist = 0;
        while (queued > 0)
        {
            int cell = _bucketHeads[currentDist % BucketCount];
            if (cell == -1)
            {
                currentDist += 1;
                continue;
            }
            RemoveCell(cell);
            queued -= 1;
            if (currentDist > Cost[cell])
            {
                continue;
            }
            // 当前格代价已定；父格代价严格更小、必然已出队，方向在此一次算完。
            if (currentDist > 0)
            {
                ResolveDirection(grid, cell);
            }
            int currentX = cell % Width;
            int currentY = cell / Width;
            for (int neighborIndex = 0; neighborIndex < NeighborsOffsets.GetLength(0); neighborIndex++)
            {
                int offsetX = NeighborsOffsets[neighborIndex, 0];
                int offsetY = NeighborsOffsets[neighborIndex, 1];
                int neighborX = currentX + offsetX;
                int neighborY = currentY + offsetY;
                if (!Passable(grid, neighborX, neighborY))
                {
                    continue;
                }
                bool diagonal = offsetX != 0 && offsetY != 0;
                if (diagonal)
                {
                    if (!Passable(grid, currentX + offsetX, currentY)
                        || !Passable(grid, currentX, currentY + offsetY))
                    {
                        continue;
                    }
                }
                if (_terrain != null && !_terrain.CanTraverse(currentX, currentY, neighborX, neighborY, _domain))
                {
                    continue;
                }
                int neighbor = grid.Index(neighborX, neighborY);
                int nextCost = currentDist + (diagonal ? DiagonalCost : OrthogonalCost);
                if (Cost[neighbor] != -1 && nextCost >= Cost[neighbor])
                {
                    continue;
                }
                Cost[neighbor] = nextCost;
                if (EnqueueCell(neighbor, nextCost))
                {
                    queued += 1;
                }
            }
        }
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

    /// <summary>
    /// 与旧 BuildDirections 完全一致的父格选择：按固定邻居顺序取代价严格更小的最小者。
    /// 被选父格代价小于当前格，故调用时其 Cost 已是最终值。
    /// </summary>
    private void ResolveDirection(NavGrid grid, int index)
    {
        int x = index % Width;
        int y = index / Width;
        int bestCost = Cost[index];
        int bestOffsetX = 0;
        int bestOffsetY = 0;
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
                if (!Passable(grid, x + offsetX, y) || !Passable(grid, x, y + offsetY))
                {
                    continue;
                }
            }
            if (_terrain != null && !_terrain.CanTraverse(x, y, neighborX, neighborY, _domain))
            {
                continue;
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

    private void EnsureScratch()
    {
        int cellCount = Width * Height;
        if (_bucketHeads == null || _bucketHeads.Length < BucketCount)
        {
            _bucketHeads = new int[BucketCount];
        }
        if (_bucketPrev == null || _bucketPrev.Length < cellCount
            || _bucketNext == null || _bucketNext.Length < cellCount
            || _bucketOf == null || _bucketOf.Length < cellCount)
        {
            _bucketPrev = new int[cellCount];
            _bucketNext = new int[cellCount];
            _bucketOf = new int[cellCount];
        }
    }

    /// <summary>入桶；若该格已在桶中（距离被改进）先摘除再按新距离重插。返回是否为新入队。</summary>
    private static bool EnqueueCell(int cell, int distance)
    {
        bool alreadyQueued = _bucketOf[cell] != -1;
        if (alreadyQueued)
        {
            RemoveCell(cell);
        }
        int bucket = distance % BucketCount;
        int head = _bucketHeads[bucket];
        _bucketPrev[cell] = -1;
        _bucketNext[cell] = head;
        if (head != -1)
        {
            _bucketPrev[head] = cell;
        }
        _bucketHeads[bucket] = cell;
        _bucketOf[cell] = bucket;
        return !alreadyQueued;
    }

    private static void RemoveCell(int cell)
    {
        int bucket = _bucketOf[cell];
        int prev = _bucketPrev[cell];
        int next = _bucketNext[cell];
        if (prev != -1)
        {
            _bucketNext[prev] = next;
        }
        else
        {
            _bucketHeads[bucket] = next;
        }
        if (next != -1)
        {
            _bucketPrev[next] = prev;
        }
        _bucketOf[cell] = -1;
    }
}
