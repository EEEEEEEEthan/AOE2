using System.Collections.Generic;
using AoeSim;
using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>移动与寻路语义：流场、缓存、占位、射程与格距。</summary>
public static partial class KernelSelfTest
{
    private static void CheckPathfinding(List<string> failures)
    {
        var grid = new NavGrid(16, 16);
        var field = new FlowField(grid);
        Expect(failures, field.Compute(grid, 10, 10) && field.Reachable(2, 2), "flow field reachable goal");
        int x = 2;
        int y = 2;
        int steps = 0;
        while (field.CostAt(x, y) > 0 && steps < 200)
        {
            GridDirection direction = field.DirectionAt(x, y);
            x += direction.X;
            y += direction.Y;
            steps += 1;
        }
        Expect(failures, x == 10 && y == 10 && field.CostAt(x, y) == 0, "flow field greedy descent");

        var walled = new NavGrid(16, 16);
        for (int wallY = 0; wallY < 16; wallY++)
        {
            if (wallY != 3)
            {
                walled.SetBlocked(8, wallY, true);
            }
        }
        var sim = new GameSim(walled, 12);
        long id = sim.SpawnUnit(2, 8, 0, 50, 0, 0.0, 0.0, 0.6);
        sim.IssueMove(id, 13, 8);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.X)) == 13
            && Fixed.ToInt(sim.World.GetValue(id, SimComponents.Position, SimComponents.Y)) == 8,
            "unit routes around wall");

        var gapField = new FlowField(walled);
        Expect(failures, gapField.Compute(walled, 13, 8) && gapField.Reachable(2, 8),
            "flow field crosses single-cell gap");

        var cornerGrid = new NavGrid(8, 8);
        cornerGrid.SetBlocked(6, 5, true);
        var cornerField = new FlowField(cornerGrid);
        cornerField.Compute(cornerGrid, 3, 3);
        GridDirection cornerDirection = cornerField.DirectionAt(5, 5);
        Expect(failures, !(cornerDirection.X == 1 && cornerDirection.Y == 1),
            "flow field does not cut blocked corner");

        var openSim = new GameSim(new NavGrid(16, 16), 17);
        long walker = openSim.SpawnUnit(2, 2, 0, 50, 0, 0.0, 0.0, 0.5);
        openSim.IssueMove(walker, 12, 12);
        int previousCost = int.MaxValue;
        bool costMonotonic = true;
        for (int tick = 0; tick < 400; tick++)
        {
            openSim.SimulateTick();
            int walkerX = (int)Fixed.ToInt(
                openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X));
            int walkerY = (int)Fixed.ToInt(
                openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y));
            int walkerCost = openSim.FieldFor(12, 12).CostAt(walkerX, walkerY);
            if (walkerCost < 0 || walkerCost > previousCost)
            {
                costMonotonic = false;
            }
            previousCost = walkerCost;
        }
        long settledX = openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X);
        long settledY = openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y);
        for (int tick = 0; tick < 10; tick++)
        {
            openSim.SimulateTick();
        }
        Expect(failures,
            Fixed.ToInt(settledX) == 12 && Fixed.ToInt(settledY) == 12
            && openSim.World.GetValue(walker, SimComponents.Position, SimComponents.X) == settledX
            && openSim.World.GetValue(walker, SimComponents.Position, SimComponents.Y) == settledY,
            "unit reaches open-field goal without jitter");
        Expect(failures, costMonotonic, "unit flow-field cost never increases");
    }

    private static void CheckFlowFieldCache(List<string> failures)
    {
        var shared = new GameSim(new NavGrid(32, 32), 901, 1, 6);
        const int goalX = 16;
        const int goalY = 16;
        for (int index = 0; index < 200; index++)
        {
            int spawnX = index % 32;
            int spawnY = index / 32 + 1;
            long id = shared.SpawnUnit(spawnX, spawnY, 0, 10, 0, 0.0, 0.0, 0.5);
            shared.IssueMove(id, goalX, goalY);
        }
        shared.SimulateTick();
        Expect(failures, shared.FieldsCreatedThisTick == 1, "same goal shares one flow field per tick");
        Expect(failures, shared.FieldCacheCount == 1, "same goal keeps a single cache entry");

        var bounded = new GameSim(new NavGrid(24, 24), 902, 1, 6, null, null, 4);
        for (int index = 0; index < 12; index++)
        {
            bounded.FieldFor(2 + index, 12);
        }
        Expect(failures, bounded.FieldCacheCount == 4, "flow field cache respects capacity");
        Expect(failures, bounded.FieldsCreatedTotal == 12, "distinct goals each created once");
        Expect(failures, bounded.FieldFor(2, 12).Reachable(0, 0), "evicted goal rebuilds reachable field");

        // 命中：同目标格重复取用返回同一实例，不新建。
        var hit = new GameSim(new NavGrid(16, 16), 903, 1, 6);
        FlowField firstHit = hit.FieldFor(8, 8);
        long createdAfterHit = hit.FieldsCreatedTotal;
        Expect(failures, ReferenceEquals(firstHit, hit.FieldFor(8, 8)),
            "flow field cache hit returns the same instance");
        Expect(failures, hit.FieldsCreatedTotal == createdAfterHit,
            "flow field cache hit creates nothing");

        // 区域化失效：被墙隔开的对侧目标流场，不因另一侧新增阻挡而作废。
        var split = new NavGrid(16, 16);
        for (int wallY = 0; wallY < 16; wallY++)
        {
            split.SetBlocked(8, wallY, true);
        }
        var regional = new GameSim(split, 904, 1, 6);
        regional.AddResource(0, GameSim.ResourceWood, 200);
        FlowField leftField = regional.FieldFor(2, 2);
        regional.FieldFor(14, 14);
        Expect(failures, regional.FieldCacheCount == 2, "two goals cached before blocking");
        long createdBeforeBlock = regional.FieldsCreatedTotal;
        Expect(failures, regional.IssuePlaceBuilding(BuildingTypes.House, 10, 10, 0) >= 0,
            "house placed on the far side");
        Expect(failures, regional.FieldCacheCount == 1,
            "blocking only drops the flow field on the affected side");
        Expect(failures, ReferenceEquals(leftField, regional.FieldFor(2, 2)),
            "disconnected goal keeps its cached flow field");
        Expect(failures, regional.FieldsCreatedTotal == createdBeforeBlock,
            "kept flow field is not recreated");
        Expect(failures, regional.FieldFor(14, 14).CostAt(10, 10) < 0,
            "rebuilt flow field treats the new building as blocked");

        // 全表失效：无界变化（如解除阻挡）后整表清空，可按需重建。
        regional.InvalidateFields();
        Expect(failures, regional.FieldCacheCount == 0, "full invalidation clears the cache");
        Expect(failures, regional.FieldFor(2, 2).Reachable(2, 3),
            "invalidated goal rebuilds on demand");
    }

    private static void CheckOccupancy(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(12, 12), 61);
        long follower = sim.SpawnUnit(2, 5, 0, 40, 0, 0.0, 0.0, 0.4);
        long leader = sim.SpawnUnit(3, 5, 0, 40, 0, 0.0, 0.0, 0.4);
        sim.IssueMove(follower, 8, 5);
        sim.IssueMove(leader, 8, 5);
        for (int tick = 0; tick < 200; tick++)
        {
            sim.SimulateTick();
        }
        long leaderX = Fixed.ToInt(sim.World.GetValue(leader, SimComponents.Position, SimComponents.X));
        long leaderY = Fixed.ToInt(sim.World.GetValue(leader, SimComponents.Position, SimComponents.Y));
        long followerX = Fixed.ToInt(sim.World.GetValue(follower, SimComponents.Position, SimComponents.X));
        long followerY = Fixed.ToInt(sim.World.GetValue(follower, SimComponents.Position, SimComponents.Y));
        Expect(failures, leaderX != followerX || leaderY != followerY, "occupancy units never overlap");
        Expect(failures, leaderX == 8 && leaderY == 5, "occupancy leading unit reaches goal");
    }

    /// <summary>大量静止实体时，占位层与空间哈希每 tick 处理量为 0，不再全量重建。</summary>
    private static void CheckIncrementalSpatial(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(32, 32), 7031);
        for (int index = 0; index < 300; index++)
        {
            int cellX = index % 32;
            int cellY = index / 32;
            sim.SpawnUnit(cellX, cellY, 0, 40, 0, 0.0, 0.0, 0.0);
        }
        sim.SimulateTick();
        sim.SimulateTick();
        int occupancyWork = 0;
        int spatialWork = 0;
        for (int tick = 0; tick < 5; tick++)
        {
            sim.SimulateTick();
            occupancyWork += sim.Occupancy.LastSyncedEntities;
            spatialWork += sim.Spatial.LastSyncedEntities;
        }
        Expect(failures, occupancyWork == 0, "静止实体占位层每 tick 无重建");
        Expect(failures, spatialWork == 0, "静止实体空间哈希每 tick 无重建");
    }

    private static void CheckGridRange(List<string> failures)
    {
        var meleeFar = new GameSim(new NavGrid(16, 16), 62);
        long meleeAttacker = meleeFar.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long meleeTarget = meleeFar.SpawnUnit(7, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 60; tick++)
        {
            meleeFar.SimulateTick();
        }
        Expect(failures, meleeFar.World.IsAlive(meleeAttacker) && meleeFar.World.IsAlive(meleeTarget), "melee misses two cells away");

        var meleeNear = new GameSim(new NavGrid(16, 16), 63);
        meleeNear.SpawnUnit(5, 5, 0, 100, 10, 1.0, 1.0, 0.0, CombatStance.StandGround);
        long adjacent = meleeNear.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 20; tick++)
        {
            meleeNear.SimulateTick();
        }
        Expect(failures, !meleeNear.World.IsAlive(adjacent), "melee hits adjacent cell");

        var ranged = new GameSim(new NavGrid(16, 16), 64);
        ranged.SpawnUnit(5, 5, 0, 100, 10, 2.0, 2.0, 0.0, CombatStance.StandGround);
        long tooClose = ranged.SpawnUnit(6, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        long inBand = ranged.SpawnUnit(7, 5, 1, 10, 0, 0.0, 0.0, 0.0);
        for (int tick = 0; tick < 30; tick++)
        {
            ranged.SimulateTick();
        }
        Expect(failures, ranged.World.IsAlive(tooClose), "ranged respects minimum range");
        Expect(failures, !ranged.World.IsAlive(inBand), "ranged hits within max range");
    }
}
