using System.Collections.Generic;
using System.IO;
using AoeSim;

using static AOE2Framework.SelfTestBase;

namespace AOE2Framework;

/// <summary>录像录制与回放的语义断言：往返校验和一致、损坏文件被拒、被拒指令不入录像。</summary>
public static class ReplaySelfTest
{
    private const long Seed = 424242;
    private const int PlayerCount = 2;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckRecordReplayRoundTrip(failures);
        CheckCivRoundTrip(failures);
        CheckCorruptedReplayRejected(failures);
        return failures;
    }

    /// <summary>选文明录制后回放：文明随录像往返，独特单位仍可用，最终校验和一致。</summary>
    private static void CheckCivRoundTrip(List<string> failures)
    {
        long[] civs = { CivilizationCatalog.Longbow, CivilizationCatalog.Generic };
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed + 1, PlayerCount);
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed + 1, PlayerCount, civs);
        var session = new LocalSession(sim);
        var recorder = new ReplayRecorder(
            map, Seed + 1, PlayerCount, sim.Lockstep.TicksPerTurn, sim.Match!.Config,
            new[] { (int)CivilizationCatalog.Longbow, (int)CivilizationCatalog.Generic });
        session.Server.Recorder = recorder;
        for (int tick = 0; tick < 20; tick++)
        {
            long checksum = session.Step();
            recorder.NoteTick(sim.World.Tick, checksum);
        }

        ReplayData data = recorder.Build();
        Expect(failures, data.CivIds.Length == PlayerCount, "recording stores per-player civs");

        string path = Path.Combine(Path.GetTempPath(), $"aoe2_replay_civ_{System.Guid.NewGuid():N}.aoe2rep");
        try
        {
            data.Save(path);
            ReplayData loaded = ReplayData.Load(path);
            Expect(failures,
                loaded.CivIds.Length == PlayerCount
                    && loaded.CivIds[0] == CivilizationCatalog.Longbow
                    && loaded.CivIds[1] == CivilizationCatalog.Generic,
                "loaded replay keeps per-player civs");

            var replay = new ReplaySession(loaded);
            Expect(failures,
                replay.Server.Sim.Civ(0) == CivilizationCatalog.Longbow,
                "replay restores player 0 civ");
            Expect(failures,
                replay.Server.Sim.Civ(1) == CivilizationCatalog.Generic,
                "replay restores player 1 civ");
            int guard = (int)loaded.FinalTick + 30;
            while (!replay.Finished && guard-- > 0)
            {
                replay.Step();
            }
            Expect(failures, !replay.ChecksumMismatch, "civ replay checksum matches the recording");
            Expect(failures,
                replay.Server.Checksum() == loaded.FinalChecksum,
                "civ replay final checksum equals the recorded checksum");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void CheckRecordReplayRoundTrip(List<string> failures)
    {
        MapData map = MapGenerator.Generate(MapType.Arabia, Seed, PlayerCount);
        GameSim sim = MapSetup.BuildGeneratedMatch(map, Seed, PlayerCount);
        var session = new LocalSession(sim);
        var recorder = new ReplayRecorder(map, Seed, PlayerCount, sim.Lockstep.TicksPerTurn, sim.Match!.Config);
        session.Server.Recorder = recorder;

        long ownVillager = FindEntity(sim, 0, SimComponents.Gather);
        Expect(failures, ownVillager >= 0, "generated match spawns an own villager");
        long otherVillager = FindEntity(sim, 1, SimComponents.Gather);
        Expect(failures, otherVillager >= 0, "generated match spawns an enemy villager");

        if (ownVillager >= 0)
        {
            session.Submit(new CommandFrame
            {
                Player = 0,
                Turn = 0,
                Commands = new[]
                {
                    new LockstepCommand(0, LockstepCommand.KindMove, ownVillager, 6, 6),
                },
            });
        }
        if (otherVillager >= 0)
        {
            // 冒充敌方单位：服务器会把 Player 覆盖为 0，归属校验应拒绝且不写入录像。
            session.Submit(new CommandFrame
            {
                Player = 0,
                Turn = 0,
                Commands = new[]
                {
                    new LockstepCommand(0, LockstepCommand.KindMove, otherVillager, 1, 1),
                },
            });
        }

        for (int tick = 0; tick < 30; tick++)
        {
            long checksum = session.Step();
            recorder.NoteTick(sim.World.Tick, checksum);
        }

        ReplayData data = recorder.Build();
        Expect(failures, data.TotalCommands() == 1, "only accepted commands are recorded");
        Expect(failures, data.FinalTick == sim.World.Tick, "recording captures the final tick");

        string path = Path.Combine(Path.GetTempPath(), $"aoe2_replay_{System.Guid.NewGuid():N}.aoe2rep");
        try
        {
            data.Save(path);
            ReplayData loaded = ReplayData.Load(path);
            Expect(failures, loaded.TotalCommands() == 1, "loaded replay keeps accepted commands");
            Expect(failures, loaded.FinalChecksum == data.FinalChecksum, "loaded replay keeps final checksum");

            var replay = new ReplaySession(loaded);
            int guard = (int)loaded.FinalTick + 30;
            while (!replay.Finished && guard-- > 0)
            {
                replay.Step();
            }
            Expect(failures, replay.Finished, "replay reaches the recorded final tick");
            Expect(failures, !replay.ChecksumMismatch, "replay checksum matches the recording");
            Expect(failures,
                replay.Server.Checksum() == loaded.FinalChecksum,
                "replay final checksum equals the recorded checksum");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void CheckCorruptedReplayRejected(List<string> failures)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aoe2_replay_bad_{System.Guid.NewGuid():N}.aoe2rep");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            bool threw = false;
            try
            {
                ReplayData.Load(path);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Expect(failures, threw, "corrupted replay is rejected with a clear error");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static long FindEntity(GameSim sim, long team, string component)
    {
        ComponentStore store = sim.World.Store(component);
        foreach (long id in sim.World.Entities())
        {
            if (!store.Has(id) || !sim.World.IsAlive(id))
            {
                continue;
            }
            if (!sim.World.HasComponent(id, SimComponents.Team))
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
}
