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
        CheckCorruptedSaveRejected(failures);
        return failures;
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
