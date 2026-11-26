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
        // 先推一帧快照，保证渲染首帧就有数据。
        _session.Step();
    }

    public void StartGeneratedMatch(long seed = 20261127, int playerCount = 2)
    {
        AoeSim.GameSim sim = AoeSim.MapSetup.BuildGeneratedMatch(
            AoeSim.MapType.Arabia, seed, playerCount);
        _grid = sim.Grid;
        _terrain = sim.Terrain;
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
        _session.Step();
    }

    /// <summary>预览/截图专用：把所有建筑类型按双方阵营摆成两排，验收建筑外观。</summary>
    public void StartBuildingShowcase()
    {
        var grid = new AoeSim.NavGrid(20, 12);
        var terrain = AoeSim.TerrainGrid.Flat(20, 12);
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
        };
        int[] originsX = { 0, 5, 8, 12, 14, 16 };
        for (int team = 0; team < 2; team++)
        {
            int originY = team == 0 ? 2 : 8;
            for (int index = 0; index < types.Length; index++)
            {
                long id = sim.IssuePlaceBuilding(types[index], originsX[index], originY, team);
                if (id >= 0)
                {
                    CompleteBuilding(sim, id);
                }
            }
        }
        _session = new AoeSim.LocalSession(sim);
        _accumulator = 0.0;
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
        var heights = new int[_terrain.Heights.Length];
        for (int index = 0; index < heights.Length; index++)
        {
            heights[index] = _terrain.Heights[index];
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

    public string GeneratedMapJson(long seed = 20261127, int playerCount = 2) =>
        AoeSim.MapGenerator.Generate(AoeSim.MapType.Arabia, seed, playerCount).ToJson();

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
            });
        }
        return result;
    }
}
