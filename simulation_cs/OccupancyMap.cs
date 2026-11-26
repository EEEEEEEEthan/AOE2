using System.Collections.Generic;

namespace AoeSim;

/// <summary>动态占位层：按实体 footprint 记录格主，供移动结算做确定性排队与避让。</summary>
public sealed class OccupancyMap
{
    private readonly int[] _owner;
    private readonly int[] _reserved;
    private readonly Dictionary<int, List<long>> _cellMembers = new();
    private readonly Dictionary<long, Rect> _rects = new();
    private readonly Dictionary<long, int> _reservations = new();

    public int Width { get; }
    public int Height { get; }

    /// <summary>最近一次 <see cref="Sync"/> 实际处理的脏实体数，供增量维护的回归测试断言。</summary>
    public int LastSyncedEntities { get; private set; }

    public OccupancyMap(int gridWidth, int gridHeight)
    {
        Width = gridWidth;
        Height = gridHeight;
        _owner = new int[Width * Height];
        _reserved = new int[Width * Height];
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
        int index = Index(x, y);
        if (_reserved[index] != 0)
        {
            return _reserved[index] - 1;
        }
        return _owner[index] - 1;
    }

    public bool IsFree(int x, int y) =>
        InBounds(x, y) && _owner[Index(x, y)] == 0 && _reserved[Index(x, y)] == 0;

    /// <summary>移动结算临时预占下一格；下次 <see cref="Sync"/> 时清除，等价于旧版每 tick 全量重建。</summary>
    public void Stamp(long id, int x, int y)
    {
        if (InBounds(x, y))
        {
            int index = Index(x, y);
            _reserved[index] = (int)id + 1;
            _reservations[id] = index;
        }
    }

    /// <summary>
    /// 增量同步：只处理自上次同步后位置/footprint 变化的实体。格子格主按「覆盖该格的最大实体 id」维护，
    /// 与旧版全量重建按升序落章的结果一致；静止实体不参与本 tick 的任何工作。
    /// </summary>
    public void Sync(SimWorld world)
    {
        long[] dirty = world.OccupancyDirtySorted();
        LastSyncedEntities = 0;
        for (int index = 0; index < dirty.Length; index++)
        {
            long id = dirty[index];
            ClearEntity(id);
            if (!world.IsAlive(id)
                || !world.HasComponent(id, SimComponents.Position)
                || !world.HasComponent(id, SimComponents.Footprint))
            {
                continue;
            }
            int originX = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
            int originY = (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
            int footprintWidth = System.Math.Max(
                1, (int)world.GetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth));
            int footprintHeight = System.Math.Max(
                1, (int)world.GetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight));
            _rects[id] = new Rect(originX, originY, footprintWidth, footprintHeight);
            for (int cellY = originY; cellY < originY + footprintHeight; cellY++)
            {
                for (int cellX = originX; cellX < originX + footprintWidth; cellX++)
                {
                    AddMember(cellX, cellY, id);
                }
            }
            LastSyncedEntities += 1;
        }
    }

    private void AddMember(int cellX, int cellY, long id)
    {
        if (!InBounds(cellX, cellY))
        {
            return;
        }
        int index = Index(cellX, cellY);
        if (!_cellMembers.TryGetValue(index, out List<long>? members))
        {
            members = new List<long>(1);
            _cellMembers[index] = members;
        }
        members.Add(id);
        if ((int)id + 1 > _owner[index])
        {
            _owner[index] = (int)id + 1;
        }
    }

    private void ClearEntity(long id)
    {
        if (_rects.TryGetValue(id, out Rect rect))
        {
            for (int cellY = rect.OriginY; cellY < rect.OriginY + rect.Height; cellY++)
            {
                for (int cellX = rect.OriginX; cellX < rect.OriginX + rect.Width; cellX++)
                {
                    RemoveMember(cellX, cellY, id);
                }
            }
            _rects.Remove(id);
        }
        if (_reservations.TryGetValue(id, out int reserved))
        {
            if (_reserved[reserved] == (int)id + 1)
            {
                _reserved[reserved] = 0;
            }
            _reservations.Remove(id);
        }
    }

    private void RemoveMember(int cellX, int cellY, long id)
    {
        if (!InBounds(cellX, cellY))
        {
            return;
        }
        int index = Index(cellX, cellY);
        if (!_cellMembers.TryGetValue(index, out List<long>? members))
        {
            return;
        }
        members.Remove(id);
        if ((int)id + 1 == _owner[index])
        {
            _owner[index] = MaxMember(members);
        }
    }

    private static int MaxMember(List<long> members)
    {
        int max = 0;
        for (int index = 0; index < members.Count; index++)
        {
            int tag = (int)members[index] + 1;
            if (tag > max)
            {
                max = tag;
            }
        }
        return max;
    }

    private readonly struct Rect
    {
        public Rect(int originX, int originY, int width, int height)
        {
            OriginX = originX;
            OriginY = originY;
            Width = width;
            Height = height;
        }

        public int OriginX { get; }
        public int OriginY { get; }
        public int Width { get; }
        public int Height { get; }
    }
}
