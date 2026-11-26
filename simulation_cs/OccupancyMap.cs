namespace AoeSim;

/// <summary>动态占位层：按实体 footprint 记录格主，供移动结算做确定性排队与避让。</summary>
public sealed class OccupancyMap
{
    private readonly int[] _owner;

    public int Width { get; }
    public int Height { get; }

    public OccupancyMap(int gridWidth, int gridHeight)
    {
        Width = gridWidth;
        Height = gridHeight;
        _owner = new int[Width * Height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    private int Index(int x, int y) => y * Width + x;

    /// <summary>空或越界返回 -1，其余返回格主实体 id。</summary>
    public long OwnerAt(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return -1;
        }
        return _owner[Index(x, y)] - 1;
    }

    public bool IsFree(int x, int y) => InBounds(x, y) && _owner[Index(x, y)] == 0;

    public void Stamp(long id, int x, int y)
    {
        if (InBounds(x, y))
        {
            _owner[Index(x, y)] = (int)id + 1;
        }
    }

    public void StampRect(long id, int x, int y, int rectWidth, int rectHeight)
    {
        for (int cellY = y; cellY < y + rectHeight; cellY++)
        {
            for (int cellX = x; cellX < x + rectWidth; cellX++)
            {
                Stamp(id, cellX, cellY);
            }
        }
    }

    public void Rebuild(SimWorld world)
    {
        System.Array.Clear(_owner, 0, _owner.Length);
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            int cellX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
            int cellY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
            if (world.HasComponent(id, SimComponents.Footprint))
            {
                int width = (int)world.GetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth);
                int height = (int)world.GetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight);
                StampRect(id, cellX, cellY, System.Math.Max(1, width), System.Math.Max(1, height));
            }
        }
    }
}
