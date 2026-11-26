using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AoeSim;

/// <summary>存档列表项：只解头部元信息，不解世界状态。</summary>
public sealed class MatchSaveInfo
{
    public string Path = "";
    public string Name = "";
    public string MapName = "";
    public string CivNames = "";
    public long Tick;
    public long CreatedUnix;
    public bool Ok = true;
    public string Error = "";
}

/// <summary>
/// 对局存档格式：与地图存档、录像都不同，扩展名 .aoe2match，独立目录。
/// 保存的是「可确定性继续推演」的完整模拟状态：<see cref="SimWorld"/> 的 tick、实体分配计数、
/// RNG 状态、所有存活实体与各组件列值，另含地形/导航（随 MapJson）、锁步回合、对局配置、视野位图。
/// 空间哈希、占用图、流场、玩家索引等派生结构不落盘，读档后重建，末尾用世界校验和对拍。
/// </summary>
public sealed class MatchSaveData
{
    public const string Magic = "AOE2MATCH";
    public const int FormatVersion = 1;

    public string Name = "对局存档";
    public long CreatedUnix;
    public string MapName = "";
    public string CivNames = "";
    public string MapJson = "";
    public long Seed;
    public int PlayerCount = 2;
    public int TicksPerTurn = 6;
    public MatchConfig Config = MatchConfig.Standard;
    public int AiDifficulty = 1;
    public long SavedTick;
    public long SavedChecksum;

    public long Tick;
    public long NextEntity;
    public long RngState;
    public long LockstepTurn;
    public bool HasMatch;
    public byte[] NavBlocked = System.Array.Empty<byte>();
    public byte[] NavBuildingBlocked = System.Array.Empty<byte>();
    public byte[] NavStructurePassable = System.Array.Empty<byte>();
    public List<byte[]> VisionStates = new();
    public List<long> AliveIds = new();
    public List<ComponentRecord> Components = new();

    /// <summary>读档重建出来的地图；由 <see cref="Restore"/> 填写，供桥层取起始位置与名称。</summary>
    public MapData? Map { get; private set; }

    public sealed class ComponentRecord
    {
        public string Name = "";
        public List<string> Columns = new();
        public List<long> EntityIds = new();
        public List<long[]> Values = new();
    }

    public void Save(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using FileStream file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.UTF8);
        WriteHeader(writer);
        writer.Write(Tick);
        writer.Write(NextEntity);
        writer.Write(RngState);
        writer.Write(LockstepTurn);
        writer.Write(HasMatch);
        WriteBytes(writer, NavBlocked);
        WriteBytes(writer, NavBuildingBlocked);
        WriteBytes(writer, NavStructurePassable);
        writer.Write(VisionStates.Count);
        foreach (byte[] states in VisionStates)
        {
            writer.Write(states.Length);
            writer.Write(states);
        }
        writer.Write(AliveIds.Count);
        foreach (long id in AliveIds)
        {
            writer.Write(id);
        }
        writer.Write(Components.Count);
        foreach (ComponentRecord component in Components)
        {
            writer.Write(component.Name);
            writer.Write(component.Columns.Count);
            foreach (string column in component.Columns)
            {
                writer.Write(column);
            }
            writer.Write(component.EntityIds.Count);
            for (int index = 0; index < component.EntityIds.Count; index++)
            {
                writer.Write(component.EntityIds[index]);
                long[] values = component.Values[index];
                for (int column = 0; column < component.Columns.Count; column++)
                {
                    writer.Write(column < values.Length ? values[column] : 0L);
                }
            }
        }
    }

    public static MatchSaveData Load(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Encoding.UTF8);
        MatchSaveData data = ReadHeader(reader);
        data.Tick = reader.ReadInt64();
        data.NextEntity = reader.ReadInt64();
        data.RngState = reader.ReadInt64();
        data.LockstepTurn = reader.ReadInt64();
        data.HasMatch = reader.ReadBoolean();
        data.NavBlocked = ReadBytes(reader);
        data.NavBuildingBlocked = ReadBytes(reader);
        data.NavStructurePassable = ReadBytes(reader);
        int visionCount = reader.ReadInt32();
        if (visionCount < 0 || visionCount > 64)
        {
            throw new InvalidDataException("存档视野数量非法");
        }
        for (int index = 0; index < visionCount; index++)
        {
            int length = reader.ReadInt32();
            if (length < 0)
            {
                throw new InvalidDataException("存档视野数据非法");
            }
            data.VisionStates.Add(reader.ReadBytes(length));
        }
        int aliveCount = reader.ReadInt32();
        if (aliveCount < 0)
        {
            throw new InvalidDataException("存档实体数量非法");
        }
        for (int index = 0; index < aliveCount; index++)
        {
            data.AliveIds.Add(reader.ReadInt64());
        }
        int componentCount = reader.ReadInt32();
        if (componentCount < 0)
        {
            throw new InvalidDataException("存档组件数量非法");
        }
        for (int index = 0; index < componentCount; index++)
        {
            var component = new ComponentRecord { Name = reader.ReadString() };
            int columnCount = reader.ReadInt32();
            if (columnCount < 0)
            {
                throw new InvalidDataException("存档组件列数量非法");
            }
            for (int column = 0; column < columnCount; column++)
            {
                component.Columns.Add(reader.ReadString());
            }
            int entityCount = reader.ReadInt32();
            if (entityCount < 0)
            {
                throw new InvalidDataException("存档组件实体数量非法");
            }
            for (int entity = 0; entity < entityCount; entity++)
            {
                component.EntityIds.Add(reader.ReadInt64());
                var values = new long[columnCount];
                for (int column = 0; column < columnCount; column++)
                {
                    values[column] = reader.ReadInt64();
                }
                component.Values.Add(values);
            }
            data.Components.Add(component);
        }
        return data;
    }

    /// <summary>只解头部元信息，供读档列表快速展示与损坏检测。</summary>
    public static MatchSaveInfo LoadInfo(string path)
    {
        var info = new MatchSaveInfo { Path = path };
        try
        {
            using FileStream file = File.OpenRead(path);
            using var reader = new BinaryReader(file, Encoding.UTF8);
            MatchSaveData data = ReadHeader(reader);
            info.Name = data.Name;
            info.MapName = data.MapName;
            info.CivNames = data.CivNames;
            info.Tick = data.SavedTick;
            info.CreatedUnix = data.CreatedUnix;
        }
        catch (Exception error)
        {
            info.Ok = false;
            info.Error = error.Message;
            info.Name = System.IO.Path.GetFileName(path);
        }
        return info;
    }

    private void WriteHeader(BinaryWriter writer)
    {
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(NetProtocol.Version);
        writer.Write(Name);
        writer.Write(CreatedUnix);
        writer.Write(MapName);
        writer.Write(CivNames);
        writer.Write(Seed);
        writer.Write(PlayerCount);
        writer.Write(TicksPerTurn);
        writer.Write((int)Config.Rules);
        writer.Write(Config.WonderHoldTicks);
        writer.Write(Config.RelicHoldTicks);
        writer.Write(Config.ScoreTickLimit);
        writer.Write(AiDifficulty);
        writer.Write(SavedTick);
        writer.Write(SavedChecksum);
        writer.Write(MapJson);
    }

    private static void WriteBytes(BinaryWriter writer, byte[] values)
    {
        writer.Write(values.Length);
        writer.Write(values);
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException("存档数组长度非法");
        }
        return reader.ReadBytes(length);
    }

    private static MatchSaveData ReadHeader(BinaryReader reader)
    {
        string magic = reader.ReadString();
        if (magic != Magic)
        {
            throw new InvalidDataException("不是有效的对局存档文件");
        }
        int format = reader.ReadInt32();
        if (format != FormatVersion)
        {
            throw new InvalidDataException($"对局存档格式版本不兼容：文件 {format}，当前 {FormatVersion}");
        }
        int protocol = reader.ReadInt32();
        if (protocol != NetProtocol.Version)
        {
            throw new InvalidDataException($"对局存档协议版本不兼容：文件 {protocol}，当前 {NetProtocol.Version}");
        }
        var data = new MatchSaveData
        {
            Name = reader.ReadString(),
            CreatedUnix = reader.ReadInt64(),
            MapName = reader.ReadString(),
            CivNames = reader.ReadString(),
            Seed = reader.ReadInt64(),
            PlayerCount = reader.ReadInt32(),
            TicksPerTurn = reader.ReadInt32(),
        };
        if (data.PlayerCount <= 0 || data.TicksPerTurn <= 0)
        {
            throw new InvalidDataException("对局存档玩家数或回合长度非法");
        }
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
        data.AiDifficulty = reader.ReadInt32();
        data.SavedTick = reader.ReadInt64();
        data.SavedChecksum = reader.ReadInt64();
        data.MapJson = reader.ReadString();
        if (string.IsNullOrEmpty(data.MapJson))
        {
            throw new InvalidDataException("对局存档缺少地图数据");
        }
        return data;
    }

    /// <summary>把当前模拟打包成存档结构（不落盘）。</summary>
    public static MatchSaveData Build(GameSim sim, MapData map, string name, int aiDifficulty)
    {
        SimWorld world = sim.World;
        var data = new MatchSaveData
        {
            Name = name,
            CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            MapName = map.Name,
            CivNames = CivNamesOf(sim),
            MapJson = map.ToJson(),
            Seed = map.Seed,
            PlayerCount = sim.Lockstep.PlayerCount,
            TicksPerTurn = sim.Lockstep.TicksPerTurn,
            Config = sim.Match?.Config ?? MatchConfig.Standard,
            AiDifficulty = aiDifficulty,
            SavedTick = world.Tick,
            SavedChecksum = world.Checksum(),
            Tick = world.Tick,
            NextEntity = world.NextEntity,
            RngState = world.Rng.State,
            LockstepTurn = sim.Lockstep.Turn,
            HasMatch = sim.Match != null,
        };
        NavGrid nav = sim.Grid;
        data.NavBlocked = (byte[])nav.Blocked.Clone();
        data.NavBuildingBlocked = (byte[])nav.BuildingBlocked.Clone();
        data.NavStructurePassable = (byte[])nav.StructurePassable.Clone();
        for (int player = 0; player < sim.Vision.Count; player++)
        {
            data.VisionStates.Add(sim.Vision[player].States());
        }
        foreach (long id in world.Entities())
        {
            data.AliveIds.Add(id);
        }
        foreach (string componentName in world.ComponentNamesSorted())
        {
            int componentId = world.ComponentId(componentName);
            if (componentId < 0)
            {
                continue;
            }
            ComponentStore store = world.Store(componentId);
            if (store.Count == 0)
            {
                continue;
            }
            var record = new ComponentRecord { Name = componentName };
            List<string> columns = store.ColumnsSorted();
            record.Columns.AddRange(columns);
            foreach (long id in store.ActiveIds())
            {
                if (!world.IsAlive(id))
                {
                    continue;
                }
                var values = new long[columns.Count];
                for (int index = 0; index < columns.Count; index++)
                {
                    values[index] = store.GetValue(id, columns[index]);
                }
                record.EntityIds.Add(id);
                record.Values.Add(values);
            }
            if (record.EntityIds.Count > 0)
            {
                data.Components.Add(record);
            }
        }
        return data;
    }

    private static string CivNamesOf(GameSim sim)
    {
        var names = new List<string>(sim.Lockstep.PlayerCount);
        for (long team = 0; team < sim.Lockstep.PlayerCount; team++)
        {
            names.Add(CivilizationCatalog.Name(sim.Civ(team)));
        }
        return string.Join("、", names);
    }

    /// <summary>按存档重建一份可继续推进的 GameSim，并还原派生索引。</summary>
    public GameSim Restore()
    {
        MapData map = MapData.FromJson(MapJson);
        map.Nav.LoadLayers(NavBlocked, NavBuildingBlocked, NavStructurePassable);
        Map = map;
        var sim = new GameSim(
            map.Nav, Seed, PlayerCount, TicksPerTurn, null, map.Terrain,
            GameSim.DefaultFieldCacheCapacity, null);
        SimWorld world = sim.World;
        world.Tick = Tick;
        world.NextEntity = NextEntity;
        world.Rng.State = RngState;
        sim.Lockstep.RestoreTurn(LockstepTurn);
        foreach (long id in AliveIds)
        {
            world.RestoreAlive(id);
        }
        foreach (ComponentRecord record in Components)
        {
            int componentId = world.ComponentId(record.Name, create: true);
            ComponentStore store = world.Store(componentId);
            var columnIds = new int[record.Columns.Count];
            for (int column = 0; column < record.Columns.Count; column++)
            {
                columnIds[column] = store.CreateColumn(record.Columns[column]);
            }
            for (int entity = 0; entity < record.EntityIds.Count; entity++)
            {
                long id = record.EntityIds[entity];
                world.Attach(id, componentId);
                long[] values = record.Values[entity];
                for (int column = 0; column < columnIds.Length; column++)
                {
                    long value = column < values.Length ? values[column] : 0L;
                    world.SetValue(id, componentId, columnIds[column], value);
                }
            }
        }
        if (HasMatch)
        {
            sim.Match = MatchState.Rebind(world, PlayerCount, Config);
        }
        for (int player = 0; player < sim.Vision.Count && player < VisionStates.Count; player++)
        {
            sim.Vision[player].LoadStates(VisionStates[player]);
        }
        sim.RestoreDerivedIndexes();
        return sim;
    }
}
