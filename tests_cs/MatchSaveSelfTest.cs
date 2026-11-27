using System.Collections.Generic;
using System.IO;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>对局存档语义断言：往返校验和一致、继续推演仍一致、元信息可读、损坏文件被拒。</summary>
public static class MatchSaveSelfTest
{
    private const long Seed = 20261127;
    private const int PlayerCount = 2;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckSaveRoundTrip(failures);
        CheckServerRoundTrip(failures);
        CheckMarketRoundTrip(failures);
        CheckTamperedBodyChecksumRejected(failures);
        CheckCorruptedSaveRejected(failures);
        return failures;
    }

    /// <summary>存档中间数据被改动时，恢复后的校验和与头部记录不符，必须拒绝载入。</summary>
    private static void CheckTamperedBodyChecksumRejected(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed, PlayerCount);
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed, PlayerCount);
        AdvanceTicks(sim, 20);
        MatchSaveData data = MatchSaveData.Build(sim, map, "篡改测试", 1);
        MatchSaveData.ComponentRecord? record = null;
        foreach (MatchSaveData.ComponentRecord candidate in data.Components)
        {
            if (candidate.Values.Count > 0 && candidate.Columns.Count > 0)
            {
                record = candidate;
                break;
            }
        }
        Expect(failures, record != null, "save has component values to tamper with");
        if (record == null)
        {
            return;
        }
        record.Values[0][0] += 1;
        string path = Path.Combine(Path.GetTempPath(), $"aoe2_match_tamper_{System.Guid.NewGuid():N}.aoe2match");
        try
        {
            data.Save(path);
            bool rejected = false;
            try
            {
                MatchSaveData.Load(path).Restore();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Expect(failures, rejected, "tampered save is rejected by checksum verification");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>带电脑玩家与服务器回合进度的完整对局链路：读档后 AI 与锁步队列必须从同一时点续推。</summary>
    private static void CheckServerRoundTrip(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed, PlayerCount);
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed, PlayerCount);
        var server = new SimulationServer(sim);
        server.AddComputerPlayer(new ComputerPlayer(sim, 1, AiDifficulty.Normal, map.Starts, Seed));
        for (int tick = 0; tick < 240; tick++)
        {
            server.Step();
        }
        long villager = FindEntity(sim, 0, SimComponents.Gather);
        if (villager >= 0)
        {
            server.Enqueue(new CommandFrame
            {
                Player = 0,
                Turn = server.Turn,
                Commands = new[]
                {
                    new LockstepCommand(0, LockstepCommand.KindMove, villager, 6, 6),
                },
            });
        }

        long savedChecksum = sim.World.Checksum();
        MatchSaveData data = MatchSaveData.Build(
            sim, map, "AI 存档", (int)AiDifficulty.Normal, server.CaptureState());
        Expect(failures, data.ServerState != null, "save carries the server state");
        Expect(failures, data.ServerState!.ComputerPlayers.Count == 1, "save carries the AI controller");

        GameSim restoredSim = data.Restore();
        Expect(failures, restoredSim.World.Checksum() == savedChecksum,
            "AI match restores to the saved checksum");
        var restoredServer = new SimulationServer(restoredSim);
        restoredServer.RestoreState(data.ServerState, map.Starts, data.Seed);
        Expect(failures, restoredServer.ComputerPlayerCount == 1,
            "restored server rebuilds the AI controller");

        for (int tick = 0; tick < 180; tick++)
        {
            long expected = server.Step();
            long actual = restoredServer.Step();
            if (expected != actual)
            {
                failures.Add($"AI match diverged at continued tick {tick}: {expected} vs {actual}; "
                    + FirstDifference(sim, restoredSim));
                break;
            }
        }
    }

    /// <summary>市场价差与市场实体索引都要随存档恢复：读档后成交必须延续原价，而不是新建实体回落到基准价。</summary>
    private static void CheckMarketRoundTrip(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed, PlayerCount);
        GameSim original = MapSetup.BuildGeneratedMatch(map, Seed, PlayerCount);
        int marketX = -1;
        int marketY = -1;
        int sizeWidth = BuildingTypes.SizeWidth(BuildingTypes.Market);
        int sizeHeight = BuildingTypes.SizeHeight(BuildingTypes.Market);
        for (int y = 0; y + sizeHeight <= map.Height && marketX < 0; y++)
        {
            for (int x = 0; x + sizeWidth <= map.Width; x++)
            {
                if (IsAreaFree(map.Nav, x, y, sizeWidth, sizeHeight))
                {
                    marketX = x;
                    marketY = y;
                    break;
                }
            }
        }
        Expect(failures, marketX >= 0, "found a free spot for the test market");
        if (marketX < 0)
        {
            return;
        }
        original.AddResource(0, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.Market).Wood);
        long market = original.IssuePlaceBuilding(BuildingTypes.Market, marketX, marketY, 0);
        Expect(failures, market >= 0, "test market is placed");
        if (market < 0)
        {
            return;
        }
        BuildingSystem.Complete(original.World, market);
        original.AddResource(0, GameSim.ResourceGold, 5000);
        original.AddResource(0, GameSim.ResourceWood, 1000);
        MarketSystem.Buy(original, 0, GameSim.ResourceWood, 200);
        MarketSystem.Sell(original, 0, GameSim.ResourceWood, 50);
        long savedRate = MarketSystem.Rate(original.World, 0, GameSim.ResourceWood);
        Expect(failures, savedRate != MarketSystem.BaseRate, "trades shift the market rate before saving");

        MatchSaveData data = MatchSaveData.Build(original, map, "市场存档", 1);
        GameSim restored = data.Restore();
        Expect(failures,
            restored.World.TryGetPlayerEntity(SimComponents.MarketState, 0, out _),
            "restored match re-indexes the market entity");
        Expect(failures,
            MarketSystem.Rate(restored.World, 0, GameSim.ResourceWood) == savedRate,
            "restored market keeps the shifted rate");
        Expect(failures, restored.World.NextEntity == original.World.NextEntity,
            "restored market does not add entities");

        for (int tick = 0; tick < 30; tick++)
        {
            MarketSystem.Buy(original, 0, GameSim.ResourceWood, 20);
            MarketSystem.Buy(restored, 0, GameSim.ResourceWood, 20);
            MarketSystem.Sell(original, 0, GameSim.ResourceWood, 10);
            MarketSystem.Sell(restored, 0, GameSim.ResourceWood, 10);
            if (MarketSystem.Rate(original.World, 0, GameSim.ResourceWood)
                != MarketSystem.Rate(restored.World, 0, GameSim.ResourceWood))
            {
                failures.Add($"market rate diverged at continued tick {tick}");
                break;
            }
            original.SimulateTick();
            restored.SimulateTick();
            if (original.World.Checksum() != restored.World.Checksum())
            {
                failures.Add($"market match diverged at continued tick {tick}: "
                    + FirstDifference(original, restored));
                break;
            }
        }
    }

    private static bool IsAreaFree(NavGrid nav, int x, int y, int width, int height)
    {
        for (int cellY = y; cellY < y + height; cellY++)
        {
            for (int cellX = x; cellX < x + width; cellX++)
            {
                if (!nav.InBounds(cellX, cellY) || nav.IsBlocked(cellX, cellY))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static long FindEntity(GameSim sim, long team, string component)
    {
        ComponentStore store = sim.World.Store(component);
        foreach (long id in sim.World.Entities())
        {
            if (!store.Has(id) || !sim.World.IsAlive(id)
                || !sim.World.HasComponent(id, SimComponents.Team))
            {
                continue;
            }
            if (sim.World.GetValue(id, SimComponents.Team, SimComponents.TeamId) == team)
            {
                return id;
            }
        }
        return -1;
    }

    private static void CheckSaveRoundTrip(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed, PlayerCount);
        GameSim original = MapSetup.BuildGeneratedMatch(map, Seed, PlayerCount);
        AdvanceTicks(original, 40);
        long savedChecksum = original.World.Checksum();

        MatchSaveData data = MatchSaveData.Build(original, map, "自测存档", 1);
        Expect(failures, data.SavedChecksum == savedChecksum, "save records the world checksum");
        Expect(failures, data.AliveIds.Count > 0, "save records alive entities");
        Expect(failures, data.Components.Count > 0, "save records component stores");

        string path = Path.Combine(Path.GetTempPath(), $"aoe2_match_{System.Guid.NewGuid():N}.aoe2match");
        try
        {
            data.Save(path);
            MatchSaveInfo info = MatchSaveData.LoadInfo(path);
            Expect(failures, info.Ok, "save metadata loads without error");
            Expect(failures, info.Name == "自测存档", "save metadata keeps the name");
            Expect(failures, info.MapName == map.Name, "save metadata keeps the map name");
            Expect(failures, info.Tick == original.World.Tick, "save metadata keeps the game time");

            MatchSaveData loaded = MatchSaveData.Load(path);
            GameSim restored = loaded.Restore();
            Expect(failures, restored.World.Tick == original.World.Tick, "restored tick matches");
            Expect(failures, restored.World.Checksum() == savedChecksum,
                "restored world checksum equals the save");
            Expect(failures, restored.Match != null, "restored match state is rebound");
            Expect(failures, restored.Lockstep.PlayerCount == original.Lockstep.PlayerCount,
                "restored player count matches");
            Expect(failures, restored.Lockstep.Turn == original.Lockstep.Turn,
                "restored lockstep turn matches");

            for (int tick = 0; tick < 60; tick++)
            {
                original.SimulateTick();
                restored.SimulateTick();
                if (original.World.Checksum() != restored.World.Checksum())
                {
                    failures.Add($"restored simulation diverged at continued tick {tick}: "
                        + FirstDifference(original, restored));
                    break;
                }
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string FirstDifference(GameSim left, GameSim right)
    {
        if (left.World.Tick != right.World.Tick)
        {
            return $"tick {left.World.Tick} vs {right.World.Tick}";
        }
        if (left.World.NextEntity != right.World.NextEntity)
        {
            return $"nextEntity {left.World.NextEntity} vs {right.World.NextEntity}";
        }
        if (left.World.Rng.State != right.World.Rng.State)
        {
            return $"rng {left.World.Rng.State} vs {right.World.Rng.State}";
        }
        foreach (long id in left.World.Entities())
        {
            if (!right.World.IsAlive(id))
            {
                return $"entity {id} alive only in original";
            }
            foreach (string component in left.World.ComponentNamesSorted())
            {
                bool hasLeft = left.World.HasComponent(id, component);
                bool hasRight = right.World.HasComponent(id, component);
                if (hasLeft != hasRight)
                {
                    return $"entity {id} component {component} presence {hasLeft} vs {hasRight}";
                }
                if (!hasLeft)
                {
                    continue;
                }
                ComponentStore leftStore = left.World.Store(component);
                foreach (string column in leftStore.ColumnsSorted())
                {
                    long lv = left.World.GetValue(id, component, column);
                    long rv = right.World.GetValue(id, component, column);
                    if (lv != rv)
                    {
                        return $"entity {id} {component}.{column} {lv} vs {rv}";
                    }
                }
            }
        }
        foreach (long id in right.World.Entities())
        {
            if (!left.World.IsAlive(id))
            {
                return $"entity {id} alive only in restored";
            }
        }
        return "world values equal (derived state differs)";
    }

    private static void CheckCorruptedSaveRejected(List<string> failures)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aoe2_match_bad_{System.Guid.NewGuid():N}.aoe2match");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            bool threw = false;
            try
            {
                MatchSaveData.Load(path);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Expect(failures, threw, "corrupted save is rejected with a clear error");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
