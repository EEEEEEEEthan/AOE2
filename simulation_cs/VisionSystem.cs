using System.Collections.Generic;

namespace AoeSim;

/// <summary>每 tick 清可见层，再按实体 id 升序用各玩家的视距半径揭示圆形范围。</summary>
public static class VisionSystem
{
    public static void Step(SimWorld world, IReadOnlyList<VisionGrid> grids, long radius)
    {
        foreach (VisionGrid grid in grids)
        {
            grid.ClearVisible();
        }
        foreach (long id in world.Entities())
        {
            if (!world.HasComponent(id, SimComponents.Team))
            {
                continue;
            }
            if (!world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            if (team < 0 || team >= grids.Count)
            {
                continue;
            }
            long x = world.GetValue(id, SimComponents.Position, SimComponents.X);
            long y = world.GetValue(id, SimComponents.Position, SimComponents.Y);
            grids[(int)team].RevealCircle(x, y, radius);
        }
    }
}
