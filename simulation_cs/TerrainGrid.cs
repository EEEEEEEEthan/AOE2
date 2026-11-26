using System;

namespace AoeSim;

/// <summary>通行域：陆地单位只能在陆地走，船只只能在水面走，两域互不越界。</summary>
public static class MovementDomain
{
    public const long Land = 0;
    public const long Water = 1;
}

/// <summary>
/// 带四顶点高度的地形：每格 4 个角各有一个高度，相邻格共享边上的顶点，
/// 整体是一张 (宽 + 1) × (高 + 1) 的顶点高度场，格内高度由四角派生。
/// 悬崖/陡坡由顶点高差形成；高度以 1/8 格为单位的体素层数表示，整数存储，确定性。
/// 与 NavGrid 解耦：NavGrid 只描述建筑/障碍占位。
/// </summary>
public sealed class TerrainGrid
{
    public const byte TypeGrass = 0;
    public const byte TypeDirt = 1;
    public const byte TypeWater = 2;
    public const byte TypeCliff = 3;

    /// <summary>可通行/可建造的最大坡度，按 1/8 格高度层计；超过即视为悬崖或陡坡。</summary>
    public const int MaxWalkableSlope = 2;

    public int Width { get; }
    public int Height { get; }
    public int VertexWidth => Width + 1;
    public int VertexHeight => Height + 1;
    public byte[] Types { get; private set; }
    public byte[] VertexHeights { get; private set; }

    public TerrainGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Types = new byte[width * height];
        VertexHeights = new byte[(width + 1) * (height + 1)];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool InVertexBounds(int x, int y) =>
        x >= 0 && y >= 0 && x <= Width && y <= Height;

    public int Index(int x, int y) => y * Width + x;

    public int VertexIndex(int x, int y) => y * VertexWidth + x;

    public byte TypeAt(int x, int y) => InBounds(x, y) ? Types[Index(x, y)] : TypeCliff;

    public byte VertexHeightAt(int x, int y) =>
        InVertexBounds(x, y) ? VertexHeights[VertexIndex(x, y)] : (byte)0;

    public void SetVertexHeight(int x, int y, byte height)
    {
        if (InVertexBounds(x, y))
        {
            VertexHeights[VertexIndex(x, y)] = height;
        }
    }

    public void SetCellHeight(int x, int y, byte height)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        SetVertexHeight(x, y, height);
        SetVertexHeight(x + 1, y, height);
        SetVertexHeight(x, y + 1, height);
        SetVertexHeight(x + 1, y + 1, height);
    }

    public int CellMinHeight(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return 0;
        }
        int minimum = byte.MaxValue;
        minimum = Math.Min(minimum, VertexHeightAt(x, y));
        minimum = Math.Min(minimum, VertexHeightAt(x + 1, y));
        minimum = Math.Min(minimum, VertexHeightAt(x, y + 1));
        minimum = Math.Min(minimum, VertexHeightAt(x + 1, y + 1));
        return minimum;
    }

    public int CellMaxHeight(int x, int y)
    {
        if (!InBounds(x, y))
        {
            return 0;
        }
        int maximum = 0;
        maximum = Math.Max(maximum, VertexHeightAt(x, y));
        maximum = Math.Max(maximum, VertexHeightAt(x + 1, y));
        maximum = Math.Max(maximum, VertexHeightAt(x, y + 1));
        maximum = Math.Max(maximum, VertexHeightAt(x + 1, y + 1));
        return maximum;
    }

    /// <summary>格代表高度：四角最小与最大的中点，用于相邻格高差判定。</summary>
    public int CellLevel(int x, int y) => (CellMinHeight(x, y) + CellMaxHeight(x, y)) / 2;

    /// <summary>单格四角高差超过可通行坡度即为陡坡/悬崖面。</summary>
    public bool IsSteep(int x, int y) =>
        CellMaxHeight(x, y) - CellMinHeight(x, y) > MaxWalkableSlope;

    /// <summary>兼容旧接口：返回该格最高角的高度。</summary>
    public byte HeightAt(int x, int y) => (byte)CellMaxHeight(x, y);

    public void Set(int x, int y, byte type, byte height)
    {
        if (!InBounds(x, y))
        {
            return;
        }
        Types[Index(x, y)] = type;
        SetCellHeight(x, y, height);
    }

    public void SetHeight(int x, int y, byte height) => SetCellHeight(x, y, height);

    public void SetType(int x, int y, byte type)
    {
        if (InBounds(x, y))
        {
            Types[Index(x, y)] = type;
        }
    }

    public bool IsWater(int x, int y) => TypeAt(x, y) == TypeWater;

    public bool IsCliff(int x, int y) => TypeAt(x, y) == TypeCliff;

    private static bool IsWalkableType(byte type) =>
        type != TypeWater && type != TypeCliff;

    /// <summary>该格在陆地域可站立：非水非崖且不是陡坡。</summary>
    public bool IsLandPassable(int x, int y) =>
        InBounds(x, y) && IsWalkableType(TypeAt(x, y)) && !IsSteep(x, y);

    /// <summary>该格在水域可航行：水且不是陡坡（水面保持平坦）。</summary>
    public bool IsWaterPassable(int x, int y) =>
        InBounds(x, y) && IsWater(x, y) && !IsSteep(x, y);

    /// <summary>该格在给定通行域内可站立。</summary>
    public bool IsPassableInDomain(int x, int y, long domain) =>
        domain == MovementDomain.Water ? IsWaterPassable(x, y) : IsLandPassable(x, y);

    /// <summary>
    /// 能否从一格走到相邻格：起点与终点都必须落在同一通行域内，且高差在可通行坡度内。
    /// 默认陆地判定保持旧语义。
    /// </summary>
    public bool CanTraverse(int fromX, int fromY, int toX, int toY) =>
        CanTraverse(fromX, fromY, toX, toY, MovementDomain.Land);

    public bool CanTraverse(int fromX, int fromY, int toX, int toY, long domain)
    {
        if (!InBounds(fromX, fromY) || !InBounds(toX, toY))
        {
            return false;
        }
        if (!IsPassableInDomain(fromX, fromY, domain) || !IsPassableInDomain(toX, toY, domain))
        {
            return false;
        }
        if (IsSteep(fromX, fromY) || IsSteep(toX, toY))
        {
            return false;
        }
        return Math.Abs(CellLevel(toX, toY) - CellLevel(fromX, fromY)) <= MaxWalkableSlope;
    }

    /// <summary>旧地图兼容：把逐格高度展开到四角，后写覆盖共享顶点。</summary>
    public void ExpandCellHeights(int[] cellHeights)
    {
        int cells = Width * Height;
        for (int index = 0; index < cells; index++)
        {
            SetCellHeight(index % Width, index / Width, (byte)cellHeights[index]);
        }
    }

    public TerrainGrid Clone()
    {
        var other = new TerrainGrid(Width, Height)
        {
            Types = (byte[])Types.Clone(),
            VertexHeights = (byte[])VertexHeights.Clone(),
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
        foreach (byte value in VertexHeights)
        {
            hash = ((hash ^ value) * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        return hash;
    }
}
