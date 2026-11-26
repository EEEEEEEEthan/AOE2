namespace AoeSim;

public sealed class NavGrid
{
    public const byte CostBlocked = 0xFF;

    public int Width { get; }
    public int Height { get; }
    public byte[] Blocked { get; private set; }
    public byte[] BuildingBlocked { get; private set; }

    /// <summary>建筑占地中可通行的一层：开启的城门写入此层，占地仍算被建筑占用。</summary>
    public byte[] StructurePassable { get; private set; }

    public NavGrid(int gridWidth, int gridHeight)
    {
        Width = gridWidth;
        Height = gridHeight;
        Blocked = new byte[Width * Height];
        BuildingBlocked = new byte[Width * Height];
        StructurePassable = new byte[Width * Height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public bool IsBlocked(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return true;
        }
        return Blocked[Index(x, y)] == CostBlocked
            || IsStructureBlocked(x, y);
    }

    public bool IsTerrainBlocked(int x, int y) =>
        InBounds(x, y) && Blocked[Index(x, y)] == CostBlocked;

    public bool IsBuildingBlocked(int x, int y) =>
        InBounds(x, y) && BuildingBlocked[Index(x, y)] == CostBlocked;

    /// <summary>建筑占地是否阻挡通行：占用且未被开启的城门标记为可通行时放行。</summary>
    public bool IsStructureBlocked(int x, int y) =>
        InBounds(x, y)
        && BuildingBlocked[Index(x, y)] == CostBlocked
        && StructurePassable[Index(x, y)] != CostBlocked;

    public void SetBlocked(int x, int y, bool value)
    {
        if (InBounds(x, y))
        {
            Blocked[Index(x, y)] = value ? CostBlocked : (byte)0;
        }
    }

    public void SetBlockedRect(int x, int y, int rectWidth, int rectHeight)
    {
        for (int cellY = y; cellY < y + rectHeight; cellY++)
        {
            for (int cellX = x; cellX < x + rectWidth; cellX++)
            {
                SetBlocked(cellX, cellY, true);
            }
        }
    }

    public void SetBuildingBlockedRect(int x, int y, int rectWidth, int rectHeight, bool value)
    {
        for (int cellY = y; cellY < y + rectHeight; cellY++)
        {
            for (int cellX = x; cellX < x + rectWidth; cellX++)
            {
                if (InBounds(cellX, cellY))
                {
                    BuildingBlocked[Index(cellX, cellY)] = value ? CostBlocked : (byte)0;
                }
            }
        }
    }

    public void SetStructurePassableRect(int x, int y, int rectWidth, int rectHeight, bool value)
    {
        for (int cellY = y; cellY < y + rectHeight; cellY++)
        {
            for (int cellX = x; cellX < x + rectWidth; cellX++)
            {
                if (InBounds(cellX, cellY))
                {
                    StructurePassable[Index(cellX, cellY)] = value ? CostBlocked : (byte)0;
                }
            }
        }
    }

    public void ClearBlocked() => System.Array.Fill(Blocked, (byte)0);

    public NavGrid Clone()
    {
        var other = new NavGrid(Width, Height);
        other.Blocked = (byte[])Blocked.Clone();
        other.BuildingBlocked = (byte[])BuildingBlocked.Clone();
        other.StructurePassable = (byte[])StructurePassable.Clone();
        return other;
    }
}
