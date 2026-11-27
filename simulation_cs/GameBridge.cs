using Godot;

namespace AOE2Framework;

/// <summary>本地局入口：内嵌本机服务器跑 C# 模拟，客户端只消费快照；表现层经此读状态。</summary>
[GlobalClass]
public partial class GameBridge : Node
{
    private const double TickSeconds = 1.0 / 30.0;
    private const int MaxStepsPerFrame = 6;

    private AoeSim.ILocalSession? _session;
    private AoeSim.ReplayRecorder? _recorder;
    private AoeSim.ReplaySession? _replay;
    private AoeSim.ReplayData? _replayData;
    private AoeSim.DedicatedServer? _dedicated;
    private MultiplayerSession? _multiplayer;
    private AoeSim.NavGrid? _grid;
    private AoeSim.TerrainGrid? _terrain;
    private double _accumulator;
    private Godot.Collections.Dictionary? _batch;
    private long _batchTick = long.MinValue;
    private bool _isReplay;
    private bool _replayPaused = true;
    private double _replaySpeed = 1.0;
    private string _savedReplayPath = "";

    public bool HasSession => _session != null || _multiplayer != null;

    public bool IsReplay => _isReplay;

    public bool IsRemote => _multiplayer != null;

    public string LastReplayError { get; private set; } = "";

    public string LastMapError { get; private set; } = "";

    public int MyTeam => _multiplayer != null
        ? (int)_multiplayer.MySeat
        : (int)(_session?.Server.ViewTeam ?? 0);

    private AoeSim.SimulationClient? ClientState => _multiplayer?.Client?.State ?? _session?.Client;

    public override void _ExitTree()
    {
        _dedicated?.Stop();
        _dedicated = null;
    }

    public void StartLocalMatch() => StartGeneratedMatch();

    /// <summary>不带文明选择的入口：保持旧调用端的默认文明路径（civIds 为 null，不写入文明列）。</summary>
    public void StartGeneratedMatch(long seed, int playerCount, AoeSim.MapType mapType, int aiDifficulty) =>
        StartGeneratedMatch(seed, playerCount, mapType, aiDifficulty, null);

    public void StartLocalDemo()
    {
        StopPlayback();
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        ResetBatchCache();
        // 先推一帧快照，保证渲染首帧就有数据。
        _session.Step();
    }

    /// <summary>
    /// 生成并开始一场对局。玩家 0 始终为本地人类；aiDifficulty 为负时关闭电脑对手，
    /// 否则玩家 1 及之后由电脑控制器接管。
    /// </summary>
    public void StartGeneratedMatch(
        long seed = 20261127, int playerCount = 2, AoeSim.MapType mapType = AoeSim.MapType.Arabia,
        int aiDifficulty = 1, int[]? civIds = null)
    {
        StopPlayback();
        AoeSim.MapData map = AoeSim.MapGenerator.Generate(mapType, seed, playerCount);
        long[]? matchCivs = null;
        if (civIds != null)
        {
            matchCivs = new long[civIds.Length];
            for (int index = 0; index < civIds.Length; index++)
            {
                matchCivs[index] = civIds[index];
            }
        }
        AoeSim.GameSim sim = AoeSim.MapSetup.BuildGeneratedMatch(map, seed, playerCount, matchCivs);
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        var session = new AoeSim.LocalSession(sim);
        _session = session;
        _accumulator = 0.0;
        ResetBatchCache();
        _recorder = new AoeSim.ReplayRecorder(
            map, seed, playerCount, sim.Lockstep.TicksPerTurn, sim.Match!.Config, civIds);
        session.Server.Recorder = _recorder;
        if (aiDifficulty >= 0)
        {
            var difficulty = (AoeSim.AiDifficulty)aiDifficulty;
            for (long team = 1; team < playerCount; team++)
            {
                session.Server.AddComputerPlayer(new AoeSim.ComputerPlayer(
                    sim, team, difficulty, map.Starts, seed));
            }
        }
        _savedReplayPath = "";
        _session.Step();
        NoteRecordedTick(sim);
    }

    /// <summary>
    /// 从编辑器保存的地图文件开局：地图自带起始位置、资源点、建筑与单位放置，
    /// 玩家数由起始位置数量决定，玩家 0 为本地人类，其余交给电脑控制器。
    /// 成功返回 true；文件损坏或没有会话时把原因写进 <see cref="LastMapError"/>。
    /// </summary>
    public bool StartFromMap(string path, int aiDifficulty = 1, int[]? civIds = null)
    {
        StopPlayback();
        AoeSim.MapData map;
        try
        {
            map = AoeSim.MapData.Load(ResolveMapPath(path));
        }
        catch (System.Exception error)
        {
            LastMapError = error.Message;
            return false;
        }
        int playerCount = System.Math.Max(1, map.Starts.Count);
        long[]? matchCivs = null;
        if (map.HasPlayerSettings)
        {
            matchCivs = map.CivIdsForMatch(playerCount);
        }
        else if (civIds != null)
        {
            matchCivs = new long[civIds.Length];
            for (int index = 0; index < civIds.Length; index++)
            {
                matchCivs[index] = civIds[index];
            }
        }
        AoeSim.GameSim sim = AoeSim.MapSetup.BuildGeneratedMatch(
            map, map.Seed, playerCount, matchCivs);
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        var session = new AoeSim.LocalSession(sim);
        _session = session;
        _accumulator = 0.0;
        ResetBatchCache();
        int[]? replayCivs = matchCivs == null ? null : System.Array.ConvertAll(
            matchCivs, civ => (int)civ);
        _recorder = new AoeSim.ReplayRecorder(
            map, map.Seed, playerCount, sim.Lockstep.TicksPerTurn, sim.Match!.Config, replayCivs);
        session.Server.Recorder = _recorder;
        if (aiDifficulty >= 0)
        {
            var difficulty = (AoeSim.AiDifficulty)aiDifficulty;
            for (long team = 1; team < playerCount; team++)
            {
                session.Server.AddComputerPlayer(new AoeSim.ComputerPlayer(
                    sim, team, difficulty, map.Starts, map.Seed));
            }
        }
        _savedReplayPath = "";
        LastMapError = "";
        _session.Step();
        NoteRecordedTick(sim);
        return true;
    }

    private static string ResolveMapPath(string path)
    {
        if (path.StartsWith("res://", System.StringComparison.Ordinal)
            || path.StartsWith("user://", System.StringComparison.Ordinal))
        {
            return Godot.ProjectSettings.GlobalizePath(path);
        }
        return path;
    }

    /// <summary>预览/截图专用：把所有建筑类型按双方阵营摆成两排，验收建筑外观。</summary>
    public void StartBuildingShowcase()
    {
        StopPlayback();
        var grid = new AoeSim.NavGrid(72, 16);
        var terrain = AoeSim.TerrainGrid.Flat(72, 16);
        var sim = new AoeSim.GameSim(grid, 7, 2, 6, AoeSim.MatchConfig.Standard, terrain);
        _grid = grid;
        _terrain = terrain;
        long[] types =
        {
            AoeSim.BuildingTypes.TownCenter,
            AoeSim.BuildingTypes.House,
            AoeSim.BuildingTypes.Barracks,
            AoeSim.BuildingTypes.ArcheryRange,
            AoeSim.BuildingTypes.Stable,
            AoeSim.BuildingTypes.Blacksmith,
            AoeSim.BuildingTypes.Monastery,
            AoeSim.BuildingTypes.University,
            AoeSim.BuildingTypes.Castle,
            AoeSim.BuildingTypes.SiegeWorkshop,
            AoeSim.BuildingTypes.Wall,
            AoeSim.BuildingTypes.Tower,
            AoeSim.BuildingTypes.Market,
            AoeSim.BuildingTypes.Farm,
            AoeSim.BuildingTypes.FortifiedWall,
            AoeSim.BuildingTypes.Gate,
            AoeSim.BuildingTypes.PalisadeWall,
        };
        int[] originsX = new int[types.Length];
        int cursorX = 0;
        for (int index = 0; index < types.Length; index++)
        {
            originsX[index] = cursorX;
            cursorX += AoeSim.BuildingTypes.SizeWidth(types[index]) + 1;
        }
        for (int team = 0; team < 2; team++)
        {
            int originY = team == 0 ? 2 : 10;
            sim.AddResource(team, AoeSim.GameSim.ResourceWood, 5000);
            sim.AddResource(team, AoeSim.GameSim.ResourceStone, 3000);
            for (int index = 0; index < types.Length; index++)
            {
                long id = sim.IssuePlaceBuilding(types[index], originsX[index], originY, team);
                if (id >= 0)
                {
                    CompleteBuilding(sim, id);
                    if (team == 1 && types[index] == AoeSim.BuildingTypes.Gate)
                    {
                        AoeSim.BuildingSystem.SetGateState(
                            sim.World, grid, id, AoeSim.BuildingSystem.GateOpen);
                    }
                }
            }
        }
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        ResetBatchCache();
        _session.Step();
    }

    /// <summary>生产队列演示局：一座完工兵营带排队单位，供截图与目视验证。</summary>
    public void StartProductionDemo()
    {
        StopPlayback();
        AoeSim.GameSim sim = new AoeSim.GameSim(
            AoeSim.DemoScenario.BuildGrid(),
            20261127,
            2,
            6,
            null,
            AoeSim.DemoScenario.BuildTerrain());
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        for (long kind = 0; kind < 4; kind++)
        {
            sim.AddResource(0, kind, 2000);
        }
        sim.AddPopulationCap(0, 40);
        long barracks = sim.SpawnCompletedBuilding(
            12, 12, 0, AoeSim.BuildingTypes.Barracks);
        long archeryRange = sim.SpawnCompletedBuilding(
            16, 12, 0, AoeSim.BuildingTypes.ArcheryRange);
        long stable = sim.SpawnCompletedBuilding(
            20, 12, 0, AoeSim.BuildingTypes.Stable);
        sim.IssueSetRally(barracks, 18, 16, -1);
        sim.Train(barracks, AoeSim.UnitDefs.Spearman);
        sim.Train(barracks, AoeSim.UnitDefs.Swordsman);
        sim.Train(archeryRange, AoeSim.UnitDefs.Archer);
        sim.Train(stable, AoeSim.UnitDefs.Knight);
        sim.SetRepeatProduction(barracks, true);
        for (int tick = 0; tick < 8; tick++)
        {
            sim.SimulateTick();
        }
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        ResetBatchCache();
        _session.Step();
    }

    private static void CompleteBuilding(AoeSim.GameSim sim, long id)
    {
        long type = sim.World.GetValue(
            id, AoeSim.SimComponents.Building, AoeSim.SimComponents.BuildingType);
        sim.World.SetValue(
            id, AoeSim.SimComponents.Building, AoeSim.SimComponents.Progress,
            AoeSim.BuildingTypes.BuildTicks(type));
        sim.World.SetValue(
            id, AoeSim.SimComponents.Building, AoeSim.SimComponents.State,
            AoeSim.BuildingSystem.StateComplete);
    }

    public void StartDedicatedServer(int port)
    {
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        _dedicated = new AoeSim.DedicatedServer(port, sim);
        _dedicated.Start();
        GD.Print($"dedicated server listening on {port}");
    }

    /// <summary>接管大厅里已经连上的客户端：对局状态只来自服务器快照，本机不再跑模拟。</summary>
    public void AdoptMultiplayer(MultiplayerSession session)
    {
        StopPlayback();
        _multiplayer = session;
        AddChild(session);
        _session = null;
        _accumulator = 0.0;
        ResetBatchCache();
        PrepareRemoteWorld();
    }

    /// <summary>对局开始后静态地图才下发，地形与空导航网格在此补齐，供渲染与放置预览使用。</summary>
    private void PrepareRemoteWorld()
    {
        AoeSim.StaticMapFrame? map = _multiplayer?.Client?.State.Map;
        if (map == null || _terrain != null)
        {
            return;
        }
        _terrain = AoeSim.TerrainGrid.FromArrays(
            (int)map.Width, (int)map.Height, map.TerrainTypes, map.TerrainVertexHeights);
        _grid = new AoeSim.NavGrid((int)map.Width, (int)map.Height);
    }

    public void Step(double delta)
    {
        if (_multiplayer != null)
        {
            PrepareRemoteWorld();
            return;
        }
        if (_session == null)
        {
            return;
        }
        if (_isReplay)
        {
            if (_replayPaused || _replay == null || _replay.Finished)
            {
                return;
            }
            _accumulator += delta * _replaySpeed;
            int replaySteps = 0;
            while (_accumulator >= TickSeconds && replaySteps < MaxStepsPerFrame && !_replay.Finished)
            {
                _replay.Step();
                _accumulator -= TickSeconds;
                replaySteps += 1;
            }
            return;
        }
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < MaxStepsPerFrame)
        {
            long checksum = _session.Step();
            _accumulator -= TickSeconds;
            steps += 1;
            if (_recorder != null)
            {
                NoteRecordedTick(_session.Server.Sim, checksum);
            }
        }
    }

    private void NoteRecordedTick(AoeSim.GameSim sim) => NoteRecordedTick(sim, sim.World.Checksum());

    private void NoteRecordedTick(AoeSim.GameSim sim, long checksum) =>
        _recorder?.NoteTick(sim.World.Tick, checksum);

    private void StopPlayback()
    {
        _isReplay = false;
        _replay = null;
        _replayData = null;
        _replayPaused = true;
        _replaySpeed = 1.0;
        _recorder = null;
        _savedReplayPath = "";
    }

    private static string ReplayDirectory()
    {
        string path = Godot.ProjectSettings.GlobalizePath("user://replays");
        Godot.DirAccess.MakeDirRecursiveAbsolute(path);
        return path;
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> ListReplays()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        string directory = ReplayDirectory();
        if (!Godot.DirAccess.DirExistsAbsolute(directory))
        {
            return result;
        }
        var entries = new System.Collections.Generic.List<(long created, Godot.Collections.Dictionary item)>();
        foreach (string file in Godot.DirAccess.GetFilesAt(directory))
        {
            if (!file.EndsWith(".aoe2rep", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string fullPath = directory.PathJoin(file);
            try
            {
                AoeSim.ReplayData data = AoeSim.ReplayData.Load(fullPath);
                entries.Add((data.CreatedUnix, new Godot.Collections.Dictionary
                {
                    { "path", fullPath },
                    { "name", data.Name },
                    { "ticks", data.FinalTick },
                    { "commands", data.TotalCommands() },
                    { "created", data.CreatedUnix },
                    { "ok", true },
                }));
            }
            catch (System.Exception error)
            {
                entries.Add((0, new Godot.Collections.Dictionary
                {
                    { "path", fullPath },
                    { "name", file },
                    { "ticks", 0L },
                    { "commands", 0L },
                    { "created", 0L },
                    { "ok", false },
                    { "error", error.Message },
                }));
            }
        }
        entries.Sort((left, right) => right.created.CompareTo(left.created));
        foreach (var entry in entries)
        {
            result.Add(entry.item);
        }
        return result;
    }

    public bool HasRecording => _recorder != null && _recorder.HasData;

    public bool HasSavedReplay => _savedReplayPath.Length > 0;

    /// <summary>把当前对局（到此为止）保存为录像，返回可读路径；失败时 LastReplayError 给出原因。</summary>
    public string SaveReplay() => SaveReplay("");

    public string SaveReplay(string name)
    {
        if (_recorder == null || !_recorder.HasData)
        {
            LastReplayError = "当前对局没有可保存的录像数据";
            return "";
        }
        if (_savedReplayPath.Length > 0 && name.Length == 0)
        {
            return _savedReplayPath;
        }
        try
        {
            AoeSim.ReplayData data = _recorder.Build();
            if (name.Length > 0)
            {
                data.Name = name;
            }
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string path = ReplayDirectory().PathJoin($"{stamp}.aoe2rep");
            data.Save(path);
            _savedReplayPath = path;
            LastReplayError = "";
            return path;
        }
        catch (System.Exception error)
        {
            LastReplayError = error.Message;
            return "";
        }
    }

    /// <summary>结算时自动落盘一次；已保存过则直接复用同一路径。</summary>
    public string SaveReplayIfNeeded()
    {
        if (_savedReplayPath.Length > 0)
        {
            return _savedReplayPath;
        }
        return SaveReplay();
    }

    public bool StartReplay(string path)
    {
        StopPlayback();
        try
        {
            AoeSim.ReplayData data = AoeSim.ReplayData.Load(path);
            var replay = new AoeSim.ReplaySession(data);
            _replayData = data;
            _replay = replay;
            _session = replay;
            _isReplay = true;
            _replayPaused = true;
            _replaySpeed = 1.0;
            _grid = replay.Server.Sim.Grid;
            _terrain = replay.Server.Sim.Terrain;
            replay.Server.ViewTeam = -1;
            _accumulator = 0.0;
            ResetBatchCache();
            LastReplayError = "";
            return true;
        }
        catch (System.Exception error)
        {
            LastReplayError = error.Message;
            return false;
        }
    }

    /// <summary>按目标 tick 重建回放并快进：确定性保证寻址后状态一致。</summary>
    public bool ReplaySeek(long tick)
    {
        if (_replayData == null)
        {
            return false;
        }
        try
        {
            var replay = new AoeSim.ReplaySession(_replayData, tick);
            _replay = replay;
            _session = replay;
            replay.Server.ViewTeam = -1;
            _accumulator = 0.0;
            ResetBatchCache();
            return true;
        }
        catch (System.Exception error)
        {
            LastReplayError = error.Message;
            return false;
        }
    }

    public long ReplayTotalTicks => _replayData?.FinalTick ?? 0;

    public long ReplayTick => _replay?.Server.Sim.World.Tick ?? 0;

    public bool ReplayFinished => _replay == null || _replay.Finished;

    public void SetReplayPaused(bool paused) => _replayPaused = paused;

    public void SetReplaySpeed(double speed) => _replaySpeed = speed <= 0 ? 1.0 : speed;

    public string ReplayChecksumStatus()
    {
        if (_replay == null || _replayData == null)
        {
            return "无回放";
        }
        if (!_replay.Finished)
        {
            return "回放中";
        }
        return _replay.ChecksumMismatch ? "校验和不一致" : "校验和一致";
    }

    public long ClientTick() => ClientState?.Tick ?? -1;    public long MatchPhase() => ClientState?.Latest?.Phase ?? -1;

    public long MatchWinnerTeam() => ClientState?.Latest?.WinnerTeam ?? -1;

    public long MatchReason() => ClientState?.Latest?.Reason ?? AoeSim.VictoryReason.None;

    public long MatchEndTick() => ClientState?.Latest?.EndTick ?? -1;

    public bool MatchEnded() => MatchPhase() == AoeSim.MatchState.PhaseEnded;

    public int CivilizationCount => (int)AoeSim.CivilizationCatalog.Count;

    public int TechCount => (int)AoeSim.TechCatalog.TechCount;

    public string CivilizationName(long civ) => AoeSim.CivilizationCatalog.Name(civ);

    /// <summary>当前本地对局中某玩家实际生效的文明 id；无会话时返回 -1。</summary>
    public long PlayerCivilization(long team) => _session?.Server.Sim.Civ(team) ?? -1;

    /// <summary>
    /// 只读科技树数据：按科技 id 顺序给出名称、时代、前置建筑、造价、研发 tick
    /// 与本文明是否可用，供表现层渲染，不触及对局状态。
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> TechTree(long civ)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        for (long techId = 0; techId < AoeSim.TechCatalog.TechCount; techId++)
        {
            long requiredAge = AoeSim.TechCatalog.RequiredAge(techId);
            long requiredBuilding = AoeSim.TechCatalog.RequiredBuilding(techId);
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", techId },
                { "name", AoeSim.TechCatalog.TechName(techId) },
                { "description", AoeSim.TechCatalog.TechDescription(techId) },
                { "required_age", requiredAge },
                { "required_age_name", AoeSim.TechCatalog.AgeName(requiredAge) },
                { "required_building", requiredBuilding },
                { "required_building_name", AoeSim.BuildingTypes.DisplayName(requiredBuilding) },
                { "food", AoeSim.TechCatalog.TechFoodCostOf(techId) },
                { "gold", AoeSim.TechCatalog.TechGoldCostOf(techId) },
                { "research_ticks", AoeSim.TechCatalog.TechResearchTicksFor(techId) },
                { "available", AoeSim.TechCatalog.IsAvailable(civ, techId) },
            });
        }
        return result;
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> PlayerStates()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null)
        {
            return result;
        }
        foreach (AoeSim.PlayerSnapshot player in frame.Players)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "team", player.Team },
                { "defeated", player.Defeated },
                { "winner", player.Winner },
                { "score", player.Score },
                { "participants", player.Participants },
                { "age", player.Age },
                { "research", player.Research },
                { "research_kind", player.ResearchKind },
                { "research_target", player.ResearchTarget },
                { "research_progress", player.ResearchProgress },
                { "research_max_progress", player.ResearchMaxProgress },
                { "wood", player.Wood },
                { "food", player.Food },
                { "gold", player.Gold },
                { "stone", player.Stone },
                { "market_wood", player.MarketWood },
                { "market_food", player.MarketFood },
                { "market_gold", player.MarketGold },
                { "market_stone", player.MarketStone },
                { "population_used", player.PopulationUsed },
                { "population_cap", player.PopulationCap },
                { "kills", player.Kills },
                { "losses", player.Losses },
                { "gathered", player.Gathered },
                { "score_military", player.ScoreMilitary },
                { "score_economy", player.ScoreEconomy },
                { "score_tech", player.ScoreTech },
            });
        }
        return result;
    }

    /// <summary>分数曲线：由服务器按固定间隔采样，随快照下发；每项为 {tick, scores:[逐队总分]}。</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> ScoreTimeline()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null || !frame.HasHistory)
        {
            return result;
        }
        for (int index = 0; index < frame.HistoryTicks.Length; index++)
        {
            var scores = new Godot.Collections.Array<long>();
            foreach (long score in frame.HistoryScores[index])
            {
                scores.Add(score);
            }
            result.Add(new Godot.Collections.Dictionary
            {
                { "tick", frame.HistoryTicks[index] },
                { "scores", scores },
            });
        }
        return result;
    }

    private void SubmitCommands(AoeSim.LockstepCommand[] commands)
    {
        if (commands.Length == 0)
        {
            return;
        }
        if (_multiplayer != null)
        {
            _multiplayer.Client?.SubmitCommands(commands);
            return;
        }
        if (_session == null)
        {
            return;
        }
        var frame = new AoeSim.CommandFrame
        {
            Player = MyTeam,
            Turn = _session.Server.Turn,
            Commands = commands,
        };
        _session.Submit(frame);
    }

    public void MoveUnits(long[] ids, long cellX, long cellY)
    {
        var commands = new AoeSim.LockstepCommand[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            commands[index] = new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindMove, ids[index], cellX, cellY);
        }
        SubmitCommands(commands);
    }

    public void AttackUnits(long[] ids, long target)
    {
        var commands = new AoeSim.LockstepCommand[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            commands[index] = new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindAttack, ids[index], 0, 0, target);
        }
        SubmitCommands(commands);
    }

    public void GatherUnits(long[] ids, long node)
    {
        var commands = new AoeSim.LockstepCommand[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            commands[index] = new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindGather, ids[index], 0, 0, node, -1);
        }
        SubmitCommands(commands);
    }

    public void StopUnits(long[] ids)
    {
        var commands = new AoeSim.LockstepCommand[ids.Length];
        for (int index = 0; index < ids.Length; index++)
        {
            commands[index] = new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindStop, ids[index]);
        }
        SubmitCommands(commands);
    }

    public void SetRally(long building, long cellX, long cellY) =>
        SetRally(building, cellX, cellY, -1);

    public void SetRally(long building, long cellX, long cellY, long target)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindSetRally, building, cellX, cellY, target),
        });
    }

    public void PlaceBuilding(long type, long cellX, long cellY)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindPlaceBuilding, -1, cellX, cellY, -1, -1, -1, 0, 0, type),
        });
    }

    public void ResearchTech(long techId)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindResearch, -1, 0, 0, -1, -1, -1, 0, 0, techId),
        });
    }

    public void AgeUp(long targetAge)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindAgeUp, -1, 0, 0, -1, -1, -1, 0, 0, targetAge),
        });
    }

    public void Train(long building, long unitType)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindTrain, building, 0, 0, -1, -1, -1, 0, 0, unitType),
        });
    }

    /// <summary>把选中的陆地单位装上运输船，一单位一条命令。</summary>
    public void LoadTransport(long[] unitIds, long ship)
    {
        var commands = new AoeSim.LockstepCommand[unitIds.Length];
        for (int index = 0; index < unitIds.Length; index++)
        {
            commands[index] = new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindTransportLoad, unitIds[index], 0, 0, ship);
        }
        SubmitCommands(commands);
    }

    public void UnloadTransport(long ship)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindTransportUnload, ship),
        });
    }

    public bool CanTransportUnload(long ship)
    {
        if (_multiplayer != null)
        {
            return true;
        }
        if (_session == null || _grid == null || _terrain == null)
        {
            return false;
        }
        return AoeSim.TransportSystem.CanUnload(
            _session.Server.Sim.World, _grid, _terrain, ship);
    }

    public bool CanTransportLoad(long unit, long ship)
    {
        if (_multiplayer != null)
        {
            return true;
        }
        if (_session == null || _grid == null || _terrain == null)
        {
            return false;
        }
        return AoeSim.TransportSystem.CanLoad(
            _session.Server.Sim.World, _grid, _terrain, unit, ship);
    }

    public bool HasCompletedMarket
    {
        get
        {
            if (_multiplayer != null)
            {
                AoeSim.SnapshotFrame? frame = ClientState?.Latest;
                if (frame == null)
                {
                    return false;
                }
                foreach (AoeSim.EntitySnapshot entity in frame.Entities)
                {
                    if (entity.Team == MyTeam
                        && entity.Kind == AoeSim.EntityKind.Building
                        && entity.BuildingType == AoeSim.BuildingTypes.Market
                        && entity.BuildingState == AoeSim.BuildingSystem.StateComplete)
                    {
                        return true;
                    }
                }
                return false;
            }
            return _session != null
                && AoeSim.MarketSystem.HasCompletedMarket(_session.Server.Sim.World, MyTeam);
        }
    }

    public long MarketBaseRate(long resource)
    {
        if (_multiplayer != null)
        {
            AoeSim.PlayerSnapshot? player = MyPlayerSnapshot();
            if (player == null)
            {
                return AoeSim.MarketSystem.BaseRate;
            }
            return resource switch
            {
                AoeSim.GameSim.ResourceWood => player.Value.MarketWood,
                AoeSim.GameSim.ResourceFood => player.Value.MarketFood,
                AoeSim.GameSim.ResourceGold => player.Value.MarketGold,
                _ => player.Value.MarketStone,
            };
        }
        return _session == null
            ? AoeSim.MarketSystem.BaseRate
            : AoeSim.MarketSystem.Rate(_session.Server.Sim.World, MyTeam, resource);
    }

    public long MarketBuyRate(long resource) => MarketBaseRate(resource) + AoeSim.MarketSystem.BuySpread;

    public long MarketSellRate(long resource) => MarketBaseRate(resource) - AoeSim.MarketSystem.SellSpread;

    public bool CanMarketBuy(long resource, long amount)
    {
        if (amount <= 0 || amount > AoeSim.MarketSystem.MaxTradeAmount)
        {
            return false;
        }
        if (_multiplayer != null)
        {
            AoeSim.PlayerSnapshot? player = MyPlayerSnapshot();
            if (player == null || !HasCompletedMarket)
            {
                return false;
            }
            long remoteCost = System.Math.Max(
                1, MarketBuyRate(resource) * amount / 100);
            return player.Value.Gold >= remoteCost;
        }
        if (_session == null)
        {
            return false;
        }
        AoeSim.GameSim sim = _session.Server.Sim;
        if (!AoeSim.MarketSystem.HasCompletedMarket(sim.World, MyTeam))
        {
            return false;
        }
        long cost = System.Math.Max(1, AoeSim.MarketSystem.BuyCost(sim.World, MyTeam, resource, amount));
        return sim.Resource(MyTeam, AoeSim.GameSim.ResourceGold) >= cost;
    }

    public bool CanMarketSell(long resource, long amount)
    {
        if (amount <= 0 || amount > AoeSim.MarketSystem.MaxTradeAmount)
        {
            return false;
        }
        if (_multiplayer != null)
        {
            AoeSim.PlayerSnapshot? player = MyPlayerSnapshot();
            return player != null && HasCompletedMarket
                && MyResource(player.Value, resource) >= amount;
        }
        if (_session == null)
        {
            return false;
        }
        AoeSim.GameSim sim = _session.Server.Sim;
        if (!AoeSim.MarketSystem.HasCompletedMarket(sim.World, MyTeam))
        {
            return false;
        }
        if (sim.Resource(MyTeam, resource) < amount)
        {
            return false;
        }
        return AoeSim.MarketSystem.SellRevenue(sim.World, MyTeam, resource, amount) > 0;
    }

    public bool CanTribute(long target, long resource, long amount)
    {
        if (amount <= 0 || amount > AoeSim.MarketSystem.MaxTradeAmount)
        {
            return false;
        }
        if (_multiplayer != null)
        {
            AoeSim.PlayerSnapshot? player = MyPlayerSnapshot();
            return player != null && target != MyTeam
                && MyResource(player.Value, resource) >= amount;
        }
        if (_session == null)
        {
            return false;
        }
        AoeSim.GameSim sim = _session.Server.Sim;
        if (target < 0 || target >= sim.Lockstep.PlayerCount || target == MyTeam)
        {
            return false;
        }
        return sim.Resource(MyTeam, resource) >= amount;
    }

    private AoeSim.PlayerSnapshot? MyPlayerSnapshot()
    {
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null || MyTeam < 0 || MyTeam >= frame.Players.Length)
        {
            return null;
        }
        return frame.Players[MyTeam];
    }

    private static long MyResource(in AoeSim.PlayerSnapshot player, long resource) => resource switch
    {
        AoeSim.GameSim.ResourceWood => player.Wood,
        AoeSim.GameSim.ResourceFood => player.Food,
        AoeSim.GameSim.ResourceGold => player.Gold,
        _ => player.Stone,
    };

    public void MarketBuy(long resource, long amount)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindMarketBuy, -1, amount, 0, -1, -1, -1, 0, 0, resource),
        });
    }

    public void MarketSell(long resource, long amount)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindMarketSell, -1, amount, 0, -1, -1, -1, 0, 0, resource),
        });
    }

    public void Tribute(long target, long resource, long amount)
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(
                MyTeam, AoeSim.LockstepCommand.KindTribute, -1, resource, amount, -1, -1, -1, 0, 0, target),
        });
    }

    public bool CanPlaceBuilding(long type, long cellX, long cellY) =>
        _grid != null
        && _terrain != null
        && AoeSim.BuildingSystem.CanPlace(_grid, _terrain, type, (int)cellX, (int)cellY);

    public bool CanTrainBuilding(long building, long unitType) =>
        _multiplayer != null || (_session != null && _session.Server.Sim.CanTrain(building, MyTeam, unitType));

    public Godot.Collections.Array<Godot.Collections.Dictionary> BuildingOptions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        for (long type = AoeSim.BuildingTypes.TownCenter; type <= AoeSim.BuildingTypes.MaxKnown; type++)
        {
            AoeSim.ResourceCost cost = AoeSim.BuildingTypes.Cost(type);
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", type },
                { "name", AoeSim.BuildingTypes.DisplayName(type) },
                { "width", AoeSim.BuildingTypes.SizeWidth(type) },
                { "height", AoeSim.BuildingTypes.SizeHeight(type) },
                { "wood", cost.Wood },
                { "food", cost.Food },
                { "gold", cost.Gold },
                { "stone", cost.Stone },
            });
        }
        return result;
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> UnitOptions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        long[] types =
        {
            AoeSim.UnitDefs.Villager,
            AoeSim.UnitDefs.Swordsman,
            AoeSim.UnitDefs.Spearman,
            AoeSim.UnitDefs.Archer,
            AoeSim.UnitDefs.Skirmisher,
            AoeSim.UnitDefs.Scout,
            AoeSim.UnitDefs.Knight,
            AoeSim.UnitDefs.Camel,
            AoeSim.UnitDefs.TransportShip,
            AoeSim.UnitDefs.BatteringRam,
            AoeSim.UnitDefs.Mangonel,
        };
        foreach (long type in types)
        {
            AoeSim.ResourceCost cost = AoeSim.UnitDefs.Cost(type);
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", type },
                { "name", AoeSim.UnitDefs.DisplayName(type) },
                { "wood", cost.Wood },
                { "food", cost.Food },
                { "gold", cost.Gold },
                { "stone", cost.Stone },
            });
        }
        return result;
    }

    public string UnitName(long unitType) => AoeSim.UnitDefs.DisplayName(unitType);

    public string BuildingName(long buildingType) => AoeSim.BuildingTypes.DisplayName(buildingType);

    public int BuildingWidth(long buildingType) => AoeSim.BuildingTypes.SizeWidth(buildingType);

    public int BuildingHeight(long buildingType) => AoeSim.BuildingTypes.SizeHeight(buildingType);

    public void Surrender()
    {
        SubmitCommands(new[]
        {
            new AoeSim.LockstepCommand(MyTeam, AoeSim.LockstepCommand.KindSurrender),
        });
    }

    public void FastForward(int maxTicks)
    {
        if (_multiplayer != null || _session == null)
        {
            return;
        }
        for (int tick = 0; tick < maxTicks && !MatchEnded(); tick++)
        {
            long checksum = _session.Step();
            if (_recorder != null)
            {
                NoteRecordedTick(_session.Server.Sim, checksum);
            }
        }
    }

    public long ServerChecksum() => _session?.Server.Checksum() ?? -1;

    public long AcceptedCommands => _session?.Server.AcceptedCommands ?? 0;

    public long RejectedCommands => _session?.Server.RejectedCommands ?? 0;

    public int GridWidth => _grid?.Width ?? 0;

    public int GridHeight => _grid?.Height ?? 0;

    public int VisionWidth => (int)(ClientState?.Latest?.VisionWidth ?? 0);

    public int VisionHeight => (int)(ClientState?.Latest?.VisionHeight ?? 0);

    public byte[] VisionStates()
    {
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null)
        {
            return System.Array.Empty<byte>();
        }
        return frame.VisionStates;
    }

    public int[] GridBlocked()
    {
        if (_grid == null)
        {
            return System.Array.Empty<int>();
        }
        var blocked = new int[_grid.Blocked.Length];
        for (int index = 0; index < _grid.Blocked.Length; index++)
        {
            blocked[index] = _grid.Blocked[index];
        }
        return blocked;
    }

    public int TerrainWidth => _terrain?.Width ?? 0;

    public int TerrainHeight => _terrain?.Height ?? 0;

    public int[] TerrainTypes()
    {
        if (_terrain == null)
        {
            return System.Array.Empty<int>();
        }
        var types = new int[_terrain.Types.Length];
        for (int index = 0; index < types.Length; index++)
        {
            types[index] = _terrain.Types[index];
        }
        return types;
    }

    public int[] TerrainHeights()
    {
        if (_terrain == null)
        {
            return System.Array.Empty<int>();
        }
        var heights = new int[_terrain.Types.Length];
        for (int index = 0; index < heights.Length; index++)
        {
            heights[index] = _terrain.HeightAt(index % _terrain.Width, index / _terrain.Width);
        }
        return heights;
    }

    public int[] TerrainVertexHeights()
    {
        if (_terrain == null)
        {
            return System.Array.Empty<int>();
        }
        var heights = new int[_terrain.VertexHeights.Length];
        for (int index = 0; index < heights.Length; index++)
        {
            heights[index] = _terrain.VertexHeights[index];
        }
        return heights;
    }

    public int TerrainLayerAt(int tileX, int tileY)
    {
        if (_terrain == null)
        {
            return 0;
        }
        return _terrain.HeightAt(tileX, tileY);
    }

    public string DemoMapJson() => AoeSim.DemoScenario.BuildMap().ToJson();

    public string GeneratedMapJson(
        long seed = 20261127, int playerCount = 2, AoeSim.MapType mapType = AoeSim.MapType.Arabia) =>
        AoeSim.MapGenerator.Generate(mapType, seed, playerCount).ToJson();

    /// <summary>预览/截图专用：把观察方视野整张揭示后推一帧快照，便于一次看到完整地图。</summary>
    public void RevealAllVision()
    {
        if (_multiplayer != null || _session == null || _grid == null)
        {
            return;
        }
        AoeSim.VisionGrid? vision = _session.Server.Sim.VisionFor(_session.Server.ViewTeam);
        if (vision == null)
        {
            return;
        }
        vision.RevealCircle(
            AoeSim.Fixed.FromInt(_grid.Width / 2),
            AoeSim.Fixed.FromInt(_grid.Height / 2),
            AoeSim.Fixed.FromInt(System.Math.Max(_grid.Width, _grid.Height) * 2));
        _session.Step();
    }

    public Godot.Collections.Array<Godot.Collections.Dictionary> Snapshot()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null)
        {
            return result;
        }
        foreach (AoeSim.EntitySnapshot entity in frame.Entities)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                { "id", entity.Id },
                { "x", AoeSim.Fixed.ToFloat(entity.X) },
                { "y", AoeSim.Fixed.ToFloat(entity.Y) },
                { "team", entity.Team },
                { "kind", entity.Kind },
                { "hp", entity.Hp },
                { "max_hp", entity.MaxHp },
                { "unit_type", entity.UnitType },
                { "node_type", entity.NodeType },
                { "node_subtype", entity.NodeSubtype },
                { "node_remaining", entity.NodeRemaining },
                { "gather_state", entity.GatherState },
                { "carry_type", entity.CarryType },
                { "carry_amount", entity.CarryAmount },
                { "building_type", entity.BuildingType },
                { "width", entity.Width },
                { "height", entity.Height },
                { "building_state", entity.BuildingState },
                { "building_progress", entity.BuildingProgress },
                { "building_max_progress", entity.BuildingMaxProgress },
                { "garrison_count", entity.GarrisonCount },
                { "garrison_capacity", entity.GarrisonCapacity },
                { "repair_state", entity.RepairState },
                { "repair_target", entity.RepairTarget },
                { "gate_state", entity.GateState },
                { "prod_unit", entity.ProdUnitType },
                { "prod_ticks_left", entity.ProdTicksLeft },
                { "prod_ticks_total", entity.ProdTicksTotal },
                { "prod_queue", entity.ProdQueueCount },
                { "prod_repeat", entity.ProdRepeat },
                { "transport_count", entity.TransportCount },
                { "transport_capacity", entity.TransportCapacity },
            });
        }
        return result;
    }

    private void ResetBatchCache()
    {
        _batch = null;
        _batchTick = long.MinValue;
    }

    /// <summary>
    /// 批量快照：按列打包实体，前端按下标读取，避免每实体一个 Dictionary。
    /// 同一 tick 复用上次结果，模拟未推进时不重复封装。
    /// </summary>
    public Godot.Collections.Dictionary SnapshotBatch()
    {
        AoeSim.SnapshotFrame? frame = ClientState?.Latest;
        if (frame == null)
        {
            return new Godot.Collections.Dictionary();
        }
        if (_batch != null && _batchTick == frame.Tick)
        {
            return _batch;
        }
        AoeSim.EntitySnapshot[] entities = frame.Entities;
        int count = entities.Length;
        var ids = new long[count];
        var positionsX = new float[count];
        var positionsY = new float[count];
        var teams = new int[count];
        var kinds = new int[count];
        var hitPoints = new int[count];
        var maxHitPoints = new int[count];
        var unitTypes = new int[count];
        var nodeTypes = new int[count];
        var nodeSubtypes = new int[count];
        var nodeRemainings = new int[count];
        var buildingTypes = new int[count];
        var widths = new int[count];
        var heights = new int[count];
        var buildingStates = new int[count];
        var buildingProgresses = new float[count];
        var buildingMaxProgresses = new float[count];
        var garrisonCounts = new int[count];
        var garrisonCapacities = new int[count];
        var repairStates = new int[count];
        var repairTargets = new int[count];
        var gateStates = new int[count];
        var prodUnits = new long[count];
        var prodTicksLefts = new long[count];
        var prodTicksTotals = new long[count];
        var prodQueues = new long[count];
        var prodRepeats = new long[count];
        var transportCounts = new int[count];
        var transportCapacities = new int[count];
        for (int index = 0; index < count; index++)
        {
            AoeSim.EntitySnapshot entity = entities[index];
            ids[index] = entity.Id;
            positionsX[index] = (float)AoeSim.Fixed.ToFloat(entity.X);
            positionsY[index] = (float)AoeSim.Fixed.ToFloat(entity.Y);
            teams[index] = (int)entity.Team;
            kinds[index] = (int)entity.Kind;
            hitPoints[index] = (int)entity.Hp;
            maxHitPoints[index] = (int)entity.MaxHp;
            unitTypes[index] = (int)entity.UnitType;
            nodeTypes[index] = (int)entity.NodeType;
            nodeSubtypes[index] = (int)entity.NodeSubtype;
            nodeRemainings[index] = (int)entity.NodeRemaining;
            buildingTypes[index] = (int)entity.BuildingType;
            widths[index] = (int)entity.Width;
            heights[index] = (int)entity.Height;
            buildingStates[index] = (int)entity.BuildingState;
            buildingProgresses[index] = (float)AoeSim.Fixed.ToFloat(entity.BuildingProgress);
            buildingMaxProgresses[index] = (float)AoeSim.Fixed.ToFloat(entity.BuildingMaxProgress);
            garrisonCounts[index] = (int)entity.GarrisonCount;
            garrisonCapacities[index] = (int)entity.GarrisonCapacity;
            repairStates[index] = (int)entity.RepairState;
            repairTargets[index] = (int)entity.RepairTarget;
            gateStates[index] = (int)entity.GateState;
            prodUnits[index] = entity.ProdUnitType;
            prodTicksLefts[index] = entity.ProdTicksLeft;
            prodTicksTotals[index] = entity.ProdTicksTotal;
            prodQueues[index] = entity.ProdQueueCount;
            prodRepeats[index] = entity.ProdRepeat;
            transportCounts[index] = (int)entity.TransportCount;
            transportCapacities[index] = (int)entity.TransportCapacity;
        }
        _batch = new Godot.Collections.Dictionary
        {
            { "tick", frame.Tick },
            { "count", count },
            { "ids", ids },
            { "x", positionsX },
            { "y", positionsY },
            { "team", teams },
            { "kind", kinds },
            { "hp", hitPoints },
            { "max_hp", maxHitPoints },
            { "unit_type", unitTypes },
            { "node_type", nodeTypes },
            { "node_subtype", nodeSubtypes },
            { "node_remaining", nodeRemainings },
            { "building_type", buildingTypes },
            { "width", widths },
            { "height", heights },
            { "building_state", buildingStates },
            { "building_progress", buildingProgresses },
            { "building_max_progress", buildingMaxProgresses },
            { "garrison_count", garrisonCounts },
            { "garrison_capacity", garrisonCapacities },
            { "repair_state", repairStates },
            { "repair_target", repairTargets },
            { "gate_state", gateStates },
            { "prod_unit", prodUnits },
            { "prod_ticks_left", prodTicksLefts },
            { "prod_ticks_total", prodTicksTotals },
            { "prod_queue", prodQueues },
            { "prod_repeat", prodRepeats },
            { "transport_count", transportCounts },
            { "transport_capacity", transportCapacities },
        };
        _batchTick = frame.Tick;
        return _batch;
    }
}
