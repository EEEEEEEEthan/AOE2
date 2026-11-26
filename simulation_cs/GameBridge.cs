using Godot;

namespace AOE2Framework;

/// <summary>本地局入口：内嵌本机服务器跑 C# 模拟，客户端只消费快照；表现层经此读状态。</summary>
[GlobalClass]
public partial class GameBridge : Node
{
    private const double TickSeconds = 1.0 / 30.0;
    private const int MaxStepsPerFrame = 6;

    private AoeSim.LocalSession? _session;
    private AoeSim.DedicatedServer? _dedicated;
    private AoeSim.NavGrid? _grid;
    private AoeSim.TerrainGrid? _terrain;
    private double _accumulator;
    private Godot.Collections.Dictionary? _batch;
    private long _batchTick = long.MinValue;

    public bool HasSession => _session != null;

    public override void _ExitTree()
    {
        _dedicated?.Stop();
        _dedicated = null;
    }

    public void StartLocalMatch()
    {
        _grid = AoeSim.DemoScenario.BuildGrid();
        _terrain = AoeSim.DemoScenario.BuildTerrain();
    }

    public void StartLocalDemo()
    {
        AoeSim.GameSim sim = AoeSim.DemoScenario.Build();
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        ResetBatchCache();
        // 先推一帧快照，保证渲染首帧就有数据。
        _session.Step();
    }

    public void StartGeneratedMatch(
        long seed = 20261127, int playerCount = 2, AoeSim.MapType mapType = AoeSim.MapType.Arabia)
    {
        AoeSim.GameSim sim = AoeSim.MapSetup.BuildGeneratedMatch(mapType, seed, playerCount);
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        ResetBatchCache();
        _session.Step();
    }

    /// <summary>预览/截图专用：把所有建筑类型按双方阵营摆成两排，验收建筑外观。</summary>
    public void StartBuildingShowcase()
    {
        var grid = new AoeSim.NavGrid(28, 12);
        var terrain = AoeSim.TerrainGrid.Flat(28, 12);
        var sim = new AoeSim.GameSim(grid, 7, 2, 6, AoeSim.MatchConfig.Standard, terrain);
        _grid = grid;
        _terrain = terrain;
        long[] types =
        {
            AoeSim.BuildingTypes.TownCenter,
            AoeSim.BuildingTypes.House,
            AoeSim.BuildingTypes.Barracks,
            AoeSim.BuildingTypes.Wall,
            AoeSim.BuildingTypes.Tower,
            AoeSim.BuildingTypes.Market,
            AoeSim.BuildingTypes.Farm,
            AoeSim.BuildingTypes.FortifiedWall,
            AoeSim.BuildingTypes.Gate,
            AoeSim.BuildingTypes.PalisadeWall,
        };
        int[] originsX = { 0, 5, 8, 12, 14, 16, 20, 23, 24, 25 };
        for (int team = 0; team < 2; team++)
        {
            int originY = team == 0 ? 2 : 8;
            sim.AddResource(team, AoeSim.GameSim.ResourceWood, 1000);
            sim.AddResource(team, AoeSim.GameSim.ResourceStone, 1000);
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

    public void Step(double delta)
    {
        if (_session == null)
        {
            return;
        }
        _accumulator += delta;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < MaxStepsPerFrame)
        {
            _session.Step();
            _accumulator -= TickSeconds;
            steps += 1;
        }
    }

    public long ClientTick() => _session?.Client.Tick ?? -1;

    public long MatchPhase() => _session?.Client.Latest?.Phase ?? -1;

    public long MatchWinnerTeam() => _session?.Client.Latest?.WinnerTeam ?? -1;

    public long MatchReason() => _session?.Client.Latest?.Reason ?? AoeSim.VictoryReason.None;

    public long MatchEndTick() => _session?.Client.Latest?.EndTick ?? -1;

    public bool MatchEnded() => MatchPhase() == AoeSim.MatchState.PhaseEnded;

    public int CivilizationCount => (int)AoeSim.CivilizationCatalog.Count;

    public int TechCount => (int)AoeSim.TechCatalog.TechCount;

    public string CivilizationName(long civ) => AoeSim.CivilizationCatalog.Name(civ);

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
        AoeSim.SnapshotFrame? frame = _session?.Client.Latest;
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
            });
        }
        return result;
    }

    public void Surrender()
    {
        if (_session == null)
        {
            return;
        }
        var frame = new AoeSim.CommandFrame
        {
            Player = 0,
            Turn = _session.Server.Turn,
            Commands = new[] { new AoeSim.LockstepCommand(0, AoeSim.LockstepCommand.KindSurrender) },
        };
        _session.Submit(frame);
    }

    public void FastForward(int maxTicks)
    {
        if (_session == null)
        {
            return;
        }
        for (int tick = 0; tick < maxTicks && !MatchEnded(); tick++)
        {
            _session.Step();
        }
    }

    public long ServerChecksum() => _session?.Server.Checksum() ?? -1;

    public int GridWidth => _grid?.Width ?? 0;

    public int GridHeight => _grid?.Height ?? 0;

    public int VisionWidth => (int)(_session?.Client.Latest?.VisionWidth ?? 0);

    public int VisionHeight => (int)(_session?.Client.Latest?.VisionHeight ?? 0);

    public byte[] VisionStates()
    {
        AoeSim.SnapshotFrame? frame = _session?.Client.Latest;
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
        if (_session == null || _grid == null)
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
        AoeSim.SnapshotFrame? frame = _session?.Client.Latest;
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
        AoeSim.SnapshotFrame? frame = _session?.Client.Latest;
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
        for (int index = 0; index < count; index++)
        {
            AoeSim.EntitySnapshot entity = entities[index];
            ids[index] = entity.Id;
            positionsX[index] = (float)AoeSim.Fixed.ToFloat(entity.X);
            positionsY[index] = (float)AoeSim.Fixed.ToFloat(entity.Y);
            teams[index] = (int)entity.Team;
            kinds[index] = (int)entity.Kind;
            hitPoints[index] = (int)entity.Hp;
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
        };
        _batchTick = frame.Tick;
        return _batch;
    }
}

