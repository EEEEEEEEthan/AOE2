using System;
using System.Collections.Generic;
using System.IO;

namespace AoeSim;

/// <summary>
/// 录像记录格式：头部（magic + 格式版本 + 协议版本）+ 初始地图与对局配置 + 按回合的已接受指令序列。
/// 回放时用同一份 <see cref="MapData"/> 与种子重建 <see cref="GameSim"/>，再按回合喂入指令，
/// 确定性保证每一步状态与录制时一致；文件尾附带最后 tick 与校验和用于对拍。
/// </summary>
public sealed class ReplayData
{
    public const string Magic = "AOE2REPLAY";
    public const int FormatVersion = 1;

    public string Name = "对局录像";
    public long CreatedUnix;
    public long Seed;
    public int PlayerCount;
    public int TicksPerTurn = 6;
    public MatchConfig Config = MatchConfig.Standard;
    public string MapJson = "";
    public Dictionary<long, List<LockstepCommand>> Commands = new();
    public long FinalTick = -1;
    public long FinalChecksum;

    public long TotalCommands()
    {
        long total = 0;
        foreach (List<LockstepCommand> commands in Commands.Values)
        {
            total += commands.Count;
        }
        return total;
    }

    public void Save(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using FileStream file = File.Create(path);
        using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(NetProtocol.Version);
        writer.Write(Name);
        writer.Write(CreatedUnix);
        writer.Write(Seed);
        writer.Write(PlayerCount);
        writer.Write(TicksPerTurn);
        writer.Write((int)Config.Rules);
        writer.Write(Config.WonderHoldTicks);
        writer.Write(Config.RelicHoldTicks);
        writer.Write(Config.ScoreTickLimit);
        writer.Write(MapJson);
        var turns = new List<long>(Commands.Keys);
        turns.Sort();
        writer.Write(turns.Count);
        foreach (long turn in turns)
        {
            List<LockstepCommand> commands = Commands[turn];
            writer.Write(turn);
            writer.Write(commands.Count);
            foreach (LockstepCommand command in commands)
            {
                foreach (long field in command.ToArray())
                {
                    writer.Write(field);
                }
            }
        }
        writer.Write(FinalTick);
        writer.Write(FinalChecksum);
    }

    public static ReplayData Load(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var reader = new BinaryReader(file, System.Text.Encoding.UTF8);
        string magic = reader.ReadString();
        if (magic != Magic)
        {
            throw new InvalidDataException("不是有效的录像文件");
        }
        int format = reader.ReadInt32();
        if (format != FormatVersion)
        {
            throw new InvalidDataException($"录像格式版本不兼容：文件 {format}，当前 {FormatVersion}");
        }
        int protocol = reader.ReadInt32();
        if (protocol != NetProtocol.Version)
        {
            throw new InvalidDataException($"录像协议版本不兼容：文件 {protocol}，当前 {NetProtocol.Version}");
        }
        var data = new ReplayData
        {
            Name = reader.ReadString(),
            CreatedUnix = reader.ReadInt64(),
            Seed = reader.ReadInt64(),
            PlayerCount = reader.ReadInt32(),
            TicksPerTurn = reader.ReadInt32(),
        };
        var rules = (VictoryCondition)reader.ReadInt32();
        long wonder = reader.ReadInt64();
        long relic = reader.ReadInt64();
        long score = reader.ReadInt64();
        data.Config = new MatchConfig
        {
            Rules = rules,
            WonderHoldTicks = wonder,
            RelicHoldTicks = relic,
            ScoreTickLimit = score,
        };
        data.MapJson = reader.ReadString();
        if (string.IsNullOrEmpty(data.MapJson))
        {
            throw new InvalidDataException("录像缺少初始地图数据");
        }
        if (data.PlayerCount <= 0)
        {
            throw new InvalidDataException("录像玩家数量非法");
        }
        int turnCount = reader.ReadInt32();
        if (turnCount < 0)
        {
            throw new InvalidDataException("录像回合数量非法");
        }
        for (int turn = 0; turn < turnCount; turn++)
        {
            long turnNumber = reader.ReadInt64();
            int commandCount = reader.ReadInt32();
            if (commandCount < 0)
            {
                throw new InvalidDataException("录像指令数量非法");
            }
            var commands = new List<LockstepCommand>(commandCount);
            for (int index = 0; index < commandCount; index++)
            {
                if (!data.Commands.TryGetValue(turnNumber, out List<LockstepCommand>? bucket))
                {
                    bucket = new List<LockstepCommand>();
                    data.Commands[turnNumber] = bucket;
                }
                bucket.Add(LockstepCommand.FromArray(ReadFields(reader)));
            }
        }
        data.FinalTick = reader.ReadInt64();
        data.FinalChecksum = reader.ReadInt64();
        return data;
    }

    private static long[] ReadFields(BinaryReader reader)
    {
        var fields = new long[LockstepCommand.FieldCount];
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index] = reader.ReadInt64();
        }
        return fields;
    }
}

/// <summary>录制器：挂在服务器上只接收通过校验的指令，并记录每步落定后的 tick 与校验和。</summary>
public sealed class ReplayRecorder : ICommandRecorder
{
    private readonly MapData _map;
    private readonly long _seed;
    private readonly int _playerCount;
    private readonly int _ticksPerTurn;
    private readonly MatchConfig _config;
    private readonly Dictionary<long, List<LockstepCommand>> _commands = new();

    public long LastTick { get; private set; } = -1;
    public long LastChecksum { get; private set; }

    public ReplayRecorder(MapData map, long seed, int playerCount, int ticksPerTurn, MatchConfig config)
    {
        _map = map;
        _seed = seed;
        _playerCount = playerCount;
        _ticksPerTurn = ticksPerTurn;
        _config = config;
    }

    public void Record(long turn, LockstepCommand command)
    {
        if (!_commands.TryGetValue(turn, out List<LockstepCommand>? bucket))
        {
            bucket = new List<LockstepCommand>();
            _commands[turn] = bucket;
        }
        bucket.Add(command);
    }

    public void NoteTick(long tick, long checksum)
    {
        if (tick >= LastTick)
        {
            LastTick = tick;
            LastChecksum = checksum;
        }
    }

    public bool HasData => LastTick >= 0;

    public ReplayData Build() => new()
    {
        Name = _map.Name,
        CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Seed = _seed,
        PlayerCount = _playerCount,
        TicksPerTurn = _ticksPerTurn,
        Config = _config,
        MapJson = _map.ToJson(),
        Commands = new Dictionary<long, List<LockstepCommand>>(_commands),
        FinalTick = LastTick,
        FinalChecksum = LastChecksum,
    };
}

/// <summary>
/// 回放会话：用录像里的初始地图与配置重建对局，预先按回合填好已接受指令，
/// 之后每步只推进服务器，与本地局的服务器→快照链路完全一致。
/// </summary>
public sealed class ReplaySession : ILocalSession
{
    public ReplayData Data { get; }
    public SimulationServer Server { get; }
    public SimulationClient Client { get; }

    public long TotalTicks => Data.FinalTick;
    public bool ChecksumMismatch { get; private set; }
    public bool Finished => Data.FinalTick < 0 || Server.Sim.World.Tick >= Data.FinalTick;

    public ReplaySession(ReplayData data, long seekTick = 0)
    {
        Data = data;
        MapData map = MapData.FromJson(data.MapJson);
        GameSim sim = MapSetup.BuildGeneratedMatch(
            map, data.Seed, data.PlayerCount, data.TicksPerTurn, data.Config);
        Server = new SimulationServer(sim);
        Client = new SimulationClient();
        Client.ApplyStaticMap(StaticMapFrame.FromTerrain(sim.Terrain));
        // 回放是观战视角：不裁剪视野，渲染端在没有迷雾数据时直接隐藏迷雾。
        Server.ViewTeam = -1;
        foreach (KeyValuePair<long, List<LockstepCommand>> pair in data.Commands)
        {
            foreach (LockstepCommand command in pair.Value)
            {
                Server.EnqueueReplay(pair.Key, command);
            }
        }
        Client.ApplySnapshot(Server.Snapshot());
        // 起点至少推进到第 1 tick：第 0 tick 还没有视野，直接展示会是一片黑。
        long target = seekTick <= 0 ? 1 : Math.Min(seekTick, Math.Max(0, data.FinalTick));
        while (!Finished && sim.World.Tick < target)
        {
            Step();
        }
    }

    /// <summary>回放是只读的，不接受任何新的玩家输入。</summary>
    public void Submit(CommandFrame frame)
    {
    }

    public long Step()
    {
        if (Finished)
        {
            return Server.Checksum();
        }
        long checksum = Server.Step();
        Client.ApplySnapshot(Server.Snapshot());
        if (Data.FinalTick >= 0 && Server.Sim.World.Tick >= Data.FinalTick
            && checksum != Data.FinalChecksum)
        {
            ChecksumMismatch = true;
        }
        return checksum;
    }
}
