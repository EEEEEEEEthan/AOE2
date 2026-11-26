using System;

namespace AoeSim;

/// <summary>按玩家的可视性网格：未探索 / 已探索 / 当前可见。全程定点，确定性。</summary>
public sealed class VisionGrid
{
    public const byte StateUnexplored = 0;
    public const byte StateExplored = 1;
    public const byte StateVisible = 2;

    public int Width { get; }
    public int Height { get; }

    private readonly byte[] _visible;
    private readonly byte[] _explored;

    public VisionGrid(int width, int height)
    {
        Width = width;
        Height = height;
        _visible = new byte[width * height];
        _explored = new byte[width * height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public void ClearVisible() => Array.Clear(_visible, 0, _visible.Length);

    public bool IsExplored(int x, int y) => InBounds(x, y) && _explored[Index(x, y)] == 1;

    public bool VisibleAt(int x, int y) => InBounds(x, y) && _visible[Index(x, y)] == 1;

    public byte StateAt(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return StateUnexplored;
        }
        int cell = Index(x, y);
        if (_visible[cell] == 1)
        {
            return StateVisible;
        }
        if (_explored[cell] == 1)
        {
            return StateExplored;
        }
        return StateUnexplored;
    }

    /// <summary>以定点坐标为中心揭示圆；欧氏距离用 Q16.16 计算，保证跨端一致。</summary>
    public void RevealCircle(long centerX, long centerY, long radius)
    {
        if (radius <= 0 || Width == 0 || Height == 0)
        {
            return;
        }
        long radiusSq = Fixed.Mul(radius, radius);
        int minX = (int)Fixed.Clamp(Fixed.ToInt(Fixed.Sub(centerX, radius)), 0, Width - 1);
        int maxX = (int)Fixed.Clamp(Fixed.ToInt(Fixed.Add(centerX, radius)), 0, Width - 1);
        int minY = (int)Fixed.Clamp(Fixed.ToInt(Fixed.Sub(centerY, radius)), 0, Height - 1);
        int maxY = (int)Fixed.Clamp(Fixed.ToInt(Fixed.Add(centerY, radius)), 0, Height - 1);
        for (int tileY = minY; tileY <= maxY; tileY++)
        {
            for (int tileX = minX; tileX <= maxX; tileX++)
            {
                long dx = Fixed.Sub(Fixed.Add(Fixed.FromInt(tileX), Fixed.Half), centerX);
                long dy = Fixed.Sub(Fixed.Add(Fixed.FromInt(tileY), Fixed.Half), centerY);
                long distance = Fixed.Add(Fixed.Mul(dx, dx), Fixed.Mul(dy, dy));
                if (distance > radiusSq)
                {
                    continue;
                }
                int cell = Index(tileX, tileY);
                _visible[cell] = 1;
                _explored[cell] = 1;
            }
        }
    }

    public byte[] States()
    {
        var states = new byte[Width * Height];
        for (int cell = 0; cell < states.Length; cell++)
        {
            if (_visible[cell] == 1)
            {
                states[cell] = StateVisible;
            }
            else if (_explored[cell] == 1)
            {
                states[cell] = StateExplored;
            }
        }
        return states;
    }

    public long Checksum()
    {
        long hash = SimWorld.FnvOffset;
        foreach (byte value in _visible)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        foreach (byte value in _explored)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        return hash;
    }
}
