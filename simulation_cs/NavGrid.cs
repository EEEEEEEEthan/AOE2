namespace AoeSim;

public sealed class NavGrid
{
    public const byte CostBlocked = 0xFF;

    public int Width { get; }
    public int Height { get; }
    public byte[] Blocked { get; private set; }

    public NavGrid(int gridWidth, int gridHeight)
    {
        Width = gridWidth;
        Height = gridHeight;
        Blocked = new byte[Width * Height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public bool IsBlocked(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return true;
        }
        return Blocked[Index(x, y)] == CostBlocked;
    }

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

    public void ClearBlocked() => System.Array.Fill(Blocked, (byte)0);

    public NavGrid Clone()
    {
        var other = new NavGrid(Width, Height);
        other.Blocked = (byte[])Blocked.Clone();
        return other;
    }
}
