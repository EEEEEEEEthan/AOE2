namespace AoeSim;

/// <summary>
/// 通行域相关的格子判定：把地形类型、坡度与建筑占位统一成一个按域区分的可站立判断，
/// 供流场与移动选择下一格时共享，保证陆地/水面两域互不越界。
/// 地图的 NavGrid 仍把水面当作静态阻塞（供陆地单位使用），水面域改为看地形水格。
/// </summary>
public static class Traversal
{
    public static bool CellPassable(NavGrid grid, TerrainGrid? terrain, int x, int y, long domain)
    {
        if (!grid.InBounds(x, y) || grid.IsBuildingBlocked(x, y))
        {
            return false;
        }
        if (terrain == null)
        {
            return !grid.IsBlocked(x, y);
        }
        if (domain == MovementDomain.Water)
        {
            return terrain.IsWaterPassable(x, y);
        }
        return !grid.IsTerrainBlocked(x, y) && terrain.IsLandPassable(x, y);
    }

    /// <summary>从某实体读取通行域；未挂域组件的单位一律视为陆地。</summary>
    public static long DomainOf(SimWorld world, long id)
    {
        if (!world.HasComponent(id, SimComponents.Domain))
        {
            return MovementDomain.Land;
        }
        return world.GetValue(id, SimComponents.Domain, SimComponents.DomainId);
    }
}
