using System;

namespace AoeSim;

/// <summary>
/// 带高度层的地形数据，刻意与 NavGrid 解耦：NavGrid 只描述建筑/障碍占位，
/// TerrainGrid 只描述每格的类型与高度，两者由 MapData 组合。
/// 高度以 1/8 格为单位的体素层数表示（1 格 = 8 层），整数存储，确定性。
/// </summary>
public sealed class TerrainGrid
{
    public const byte TypeGrass = 0;
    public const byte TypeDirt = 1;
    public const byte TypeWater = 2;
    public const byte TypeCliff = 3;

    public const int MaxStep = 1;

    public int Width { get; }
    public int Height { get; }
    public byte[] Types { get; private set; }
    public byte[] Heights { get; private set; }

    public TerrainGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Types = new byte[width * height];
        Heights = new byte[width * height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Index(int x, int y) => y * Width + x;

    public byte TypeAt(int x, int y) => InBounds(x, y) ? Types[Index(x, y)] : TypeCliff;

    public byte HeightAt(int x, int y) => InBounds(x, y) ? Heights[Index(x, y)] : (byte)0;

    public void Set(int x, int y, byte type, byte height)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        int index = Index(x, y);
        Types[index] = type;
        Heights[index] = height;
    }

    public void SetHeight(int x, int y, byte height)
    {
        if (InBounds(x, y))
        {
            Heights[Index(x, y)] = height;
        }
    }

    public void SetType(int x, int y, byte type)
    {
        if (InBounds(x, y))
        {
            Types[Index(x, y)] = type;
        }
    }

    public bool IsWater(int x, int y) => TypeAt(x, y) == TypeWater;

    public bool IsCliff(int x, int y) => TypeAt(x, y) == TypeCliff;

    /// <summary>能否从一格走到相邻格：终点非水/崖，且高度差不超过一层。</summary>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY)
    {
        if (!InBounds(fromX, fromY) || !InBounds(toX, toY))
        {
            return false;
        }
        byte type = Types[Index(toX, toY)];
        if (type == TypeWater || type == TypeCliff)
        {
            return false;
        }
        int delta = Math.Abs(Heights[Index(toX, toY)] - Heights[Index(fromX, fromY)]);
        return delta <= MaxStep;
    }

    public TerrainGrid Clone()
    {
        var other = new TerrainGrid(Width, Height)
        {
            Types = (byte[])Types.Clone(),
            Heights = (byte[])Heights.Clone(),
        };
        return other;
    }

    public static TerrainGrid Flat(int width, int height, byte type = TypeGrass)
    {
        var grid = new TerrainGrid(width, height);
        Array.Fill(grid.Types, type);
        return grid;
    }

    public long Checksum()
    {
        long hash = SimWorld.FnvOffset;
        hash = SimWorld.HashInt(hash, Width);
        hash = SimWorld.HashInt(hash, Height);
        foreach (byte value in Types)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        foreach (byte value in Heights)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        return hash;
    }
}
