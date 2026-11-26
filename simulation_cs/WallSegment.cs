using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 城墙按段拖拽放置的栅格化与连接判定。纯几何、全整数，保证同起止点在各端得到同样的格序列。
/// 封闭圈不额外维护状态：每格写入 NavGrid 阻断层，配合 <see cref="FlowField"/> 禁止斜切墙角自然成型。
/// </summary>
public static class WallSegment
{
    /// <summary>偏移键，用于判断某格是否属于本段，避免把段内相邻格误当作外部连接。</summary>
    public const int NoKey = int.MinValue;

    /// <summary>
    /// 把起止两格栅格化为一段墙格序列（含两端）。同行/同列走直线；否则走 L 形正交拐角，
    /// 先沿位移较大的轴推进（相等时先走 X），因此同一对起止点永远得到同一条路径。
    /// </summary>
    public static List<(int X, int Y)> Rasterize(int startX, int startY, int endX, int endY)
    {
        var cells = new List<(int X, int Y)>();
        int deltaX = endX - startX;
        int deltaY = endY - startY;
        int stepX = System.Math.Sign(deltaX);
        int stepY = System.Math.Sign(deltaY);
        int absX = System.Math.Abs(deltaX);
        int absY = System.Math.Abs(deltaY);
        int x = startX;
        int y = startY;
        int first = absX >= absY ? absX : absY;
        int second = absX >= absY ? absY : absX;
        bool firstAlongX = absX >= absY;
        for (int index = 0; index < first; index++)
        {
            cells.Add((x, y));
            if (firstAlongX)
            {
                x += stepX;
            }
            else
            {
                y += stepY;
            }
        }
        for (int index = 0; index < second; index++)
        {
            cells.Add((x, y));
            if (firstAlongX)
            {
                y += stepY;
            }
            else
            {
                x += stepX;
            }
        }
        cells.Add((endX, endY));
        return cells;
    }

    /// <summary>
    /// 段首尾是否都与外部建筑 footprint 正交相接（墙/门/城镇中心/塔等都算）。
    /// 斜向相邻不算连接；只有正交连续才允许围出封闭圈。
    /// </summary>
    public static bool EndpointsConnected(NavGrid grid, IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells.Count == 0)
        {
            return false;
        }
        var own = new HashSet<int>();
        foreach ((int cellX, int cellY) in cells)
        {
            own.Add(Key(grid, cellX, cellY));
        }
        return ConnectsAt(grid, cells[0].X, cells[0].Y, own)
            && ConnectsAt(grid, cells[cells.Count - 1].X, cells[cells.Count - 1].Y, own);
    }

    /// <summary>该格四邻中是否有不属于本段的外部建筑 footprint。</summary>
    public static bool ConnectsAt(NavGrid grid, int cellX, int cellY, HashSet<int>? exclude = null)
    {
        return IsExternalBuilding(grid, cellX + 1, cellY, exclude)
            || IsExternalBuilding(grid, cellX - 1, cellY, exclude)
            || IsExternalBuilding(grid, cellX, cellY + 1, exclude)
            || IsExternalBuilding(grid, cellX, cellY - 1, exclude);
    }

    /// <summary>格在网格内的整数键，供连接判定排除本段自身格。</summary>
    public static int Key(NavGrid grid, int cellX, int cellY) =>
        grid.InBounds(cellX, cellY) ? grid.Index(cellX, cellY) : NoKey;

    private static bool IsExternalBuilding(NavGrid grid, int cellX, int cellY, HashSet<int>? exclude)
    {
        if (!grid.IsBuildingBlocked(cellX, cellY))
        {
            return false;
        }
        return exclude == null || !exclude.Contains(Key(grid, cellX, cellY));
    }
}
