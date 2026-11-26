using System.Collections.Generic;
using System.IO;

namespace AoeSim;

public static class EntityKind
{
    public const long Unit = 0;
    public const long Villager = 1;
    public const long ResourceNode = 2;
    public const long DropSite = 3;
    public const long Inventory = 4;
    public const long Monk = 5;
    public const long Relic = 6;
    public const long RelicSlot = 7;
    public const long Building = 8;
    public const long TradeCart = 9;
    public const long HuntingAnimal = 10;
}

/// <summary>网络协议版本：静态地图帧携带，握手时校验两端一致。</summary>
public static class NetProtocol
{
    public const int Version = 9;
}

/// <summary>
/// 对局内不变的静态地图数据，连接建立时下发一次；之后每帧快照不再重复携带地形。
/// </summary>
public sealed class StaticMapFrame
{
    public int Version = NetProtocol.Version;
    public long Width;
    public long Height;
    public byte[] TerrainTypes = System.Array.Empty<byte>();
    public byte[] TerrainVertexHeights = System.Array.Empty<byte>();

    public static StaticMapFrame FromTerrain(TerrainGrid terrain) => new()
    {
        Width = terrain.Width,
        Height = terrain.Height,
        TerrainTypes = (byte[])terrain.Types.Clone(),
        TerrainVertexHeights = (byte[])terrain.VertexHeights.Clone(),
    };

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(TerrainTypes.Length);
            writer.Write(TerrainTypes);
            writer.Write(TerrainVertexHeights.Length);
            writer.Write(TerrainVertexHeights);
        }
        return stream.ToArray();
    }

    public static StaticMapFrame Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version != NetProtocol.Version)
        {
            throw new InvalidDataException($"protocol version mismatch: {version}");
        }
        return new StaticMapFrame
        {
            Version = version,
            Width = reader.ReadInt64(),
            Height = reader.ReadInt64(),
            TerrainTypes = reader.ReadBytes(reader.ReadInt32()),
            TerrainVertexHeights = reader.ReadBytes(reader.ReadInt32()),
        };
    }
}

/// <summary>握手帧：服务器按接入顺序给该连接分配观察方 team，客户端据此渲染自己的视野。</summary>
public sealed class WelcomeFrame
{
    public long ViewerTeam;

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ViewerTeam);
        }
        return stream.ToArray();
    }

    public static WelcomeFrame Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return new WelcomeFrame { ViewerTeam = reader.ReadInt64() };
    }
}

public readonly struct EntitySnapshot
{
    public readonly long Id;
    public readonly long X;
    public readonly long Y;
    public readonly long Team;
    public readonly long Kind;
    public readonly long Hp;
    public readonly long NodeType;
    public readonly long NodeSubtype;
    public readonly long NodeRemaining;
    public readonly long GatherState;
    public readonly long CarryType;
    public readonly long CarryAmount;
    public readonly long BuildingType;
    public readonly long Width;
    public readonly long Height;
    public readonly long BuildingState;
    public readonly long BuildingProgress;
    public readonly long BuildingMaxProgress;
    public readonly long GarrisonCount;
    public readonly long GarrisonCapacity;
    public readonly long RepairState;
    public readonly long RepairTarget;
    public readonly long GateState;
    public readonly long UnitType;
    public readonly long MaxHp;

    public EntitySnapshot(
        long id,
        long x,
        long y,
        long team,
        long kind,
        long hp,
        long nodeType,
        long nodeSubtype,
        long nodeRemaining,
        long gatherState,
        long carryType,
        long carryAmount,
        long buildingType,
        long width,
        long height,
        long buildingState,
        long buildingProgress,
        long buildingMaxProgress,
        long garrisonCount,
        long garrisonCapacity,
        long repairState,
        long repairTarget,
        long gateState,
        long unitType,
        long maxHp)
    {
        Id = id;
        X = x;
        Y = y;
        Team = team;
        Kind = kind;
        Hp = hp;
        NodeType = nodeType;
        NodeSubtype = nodeSubtype;
        NodeRemaining = nodeRemaining;
        GatherState = gatherState;
        CarryType = carryType;
        CarryAmount = carryAmount;
        BuildingType = buildingType;
        Width = width;
        Height = height;
        BuildingState = buildingState;
        BuildingProgress = buildingProgress;
        BuildingMaxProgress = buildingMaxProgress;
        GarrisonCount = garrisonCount;
        GarrisonCapacity = garrisonCapacity;
        RepairState = repairState;
        RepairTarget = repairTarget;
        GateState = gateState;
        UnitType = unitType;
        MaxHp = maxHp;
    }

    /// <summary>逐字段比较：增量编码以它为「本实体是否需要下发」的判据。</summary>
    public bool SameAs(in EntitySnapshot other) =>
        Id == other.Id
        && X == other.X
        && Y == other.Y
        && Team == other.Team
        && Kind == other.Kind
        && Hp == other.Hp
        && NodeType == other.NodeType
        && NodeSubtype == other.NodeSubtype
        && NodeRemaining == other.NodeRemaining
        && GatherState == other.GatherState
        && CarryType == other.CarryType
        && CarryAmount == other.CarryAmount
        && BuildingType == other.BuildingType
        && Width == other.Width
        && Height == other.Height
        && BuildingState == other.BuildingState
        && BuildingProgress == other.BuildingProgress
        && BuildingMaxProgress == other.BuildingMaxProgress
        && GarrisonCount == other.GarrisonCount
        && GarrisonCapacity == other.GarrisonCapacity
        && RepairState == other.RepairState
        && RepairTarget == other.RepairTarget
        && GateState == other.GateState
        && UnitType == other.UnitType
        && MaxHp == other.MaxHp;
}

public readonly struct PlayerSnapshot
{
    public readonly long Team;
    public readonly long Defeated;
    public readonly long Winner;
    public readonly long Score;
    public readonly long Participants;
    public readonly long Age;
    public readonly long Research;
    public readonly long ResearchKind;
    public readonly long ResearchTarget;
    public readonly long ResearchProgress;
    public readonly long ResearchMaxProgress;
    public readonly long Wood;
    public readonly long Food;
    public readonly long Gold;
    public readonly long Stone;
    public readonly long MarketWood;
    public readonly long MarketFood;
    public readonly long MarketGold;
    public readonly long MarketStone;
    public readonly long PopulationUsed;
    public readonly long PopulationCap;

    public PlayerSnapshot(
        long team,
        long defeated,
        long winner,
        long score,
        long participants,
        long age,
        long research,
        long researchKind,
        long researchTarget,
        long researchProgress,
        long researchMaxProgress,
        long wood,
        long food,
        long gold,
        long stone,
        long marketWood,
        long marketFood,
        long marketGold,
        long marketStone,
        long populationUsed,
        long populationCap)
    {
        Team = team;
        Defeated = defeated;
        Winner = winner;
        Score = score;
        Participants = participants;
        Age = age;
        Research = research;
        ResearchKind = researchKind;
        ResearchTarget = researchTarget;
        ResearchProgress = researchProgress;
        ResearchMaxProgress = researchMaxProgress;
        Wood = wood;
        Food = food;
        Gold = gold;
        Stone = stone;
        MarketWood = marketWood;
        MarketFood = marketFood;
        MarketGold = marketGold;
        MarketStone = marketStone;
        PopulationUsed = populationUsed;
        PopulationCap = populationCap;
    }
}

public sealed class SnapshotFrame
{
    public long Tick;
    public long Turn;
    public long Phase = -1;
    public long WinnerTeam = -1;
    public long Reason = AoeSim.VictoryReason.None;
    public long EndTick = -1;
    public long VisionWidth;
    public long VisionHeight;
    public bool HasVision;
    public byte[] VisionStates = System.Array.Empty<byte>();
    public PlayerSnapshot[] Players = System.Array.Empty<PlayerSnapshot>();
    public EntitySnapshot[] Entities = System.Array.Empty<EntitySnapshot>();

    /// <summary>为 true 时 Entities 只含新增/变化实体，RemovedEntities 含本帧消失的实体 id。</summary>
    public bool Delta;
    /// <summary>与 <see cref="Entities"/> 一一对应的字段掩码：仅这些字段被编码，其余沿用客户端上一帧的值。</summary>
    public uint[] EntityMasks = System.Array.Empty<uint>();
    public long[] RemovedEntities = System.Array.Empty<long>();

    [System.ThreadStatic] private static List<EntitySnapshot>? _entityBuffer;
    [System.ThreadStatic] private static Dictionary<long, long>? _inventoryBuffer;
    [System.ThreadStatic] private static MemoryStream? _encodeStream;

    /// <summary>快照构建入口一次性解析的组件与列句柄，实体循环内只做整型访问。</summary>
    private readonly struct SnapshotLayout
    {
        public readonly ComponentAccess Resource;
        public readonly ComponentAccess Team;
        public readonly ComponentAccess Position;
        public readonly ComponentAccess Health;
        public readonly ComponentAccess Combat;
        public readonly ComponentAccess ResourceNode;
        public readonly ComponentAccess HuntingAnimal;
        public readonly ComponentAccess Gather;
        public readonly ComponentAccess Repair;
        public readonly ComponentAccess Building;
        public readonly ComponentAccess Garrison;
        public readonly ComponentAccess PlayerProgress;
        public readonly ComponentAccess Wonder;
        public readonly ComponentAccess RelicSlot;
        public readonly ComponentAccess DropSite;
        public readonly ComponentAccess Monk;
        public readonly ComponentAccess Relic;
        public readonly ComponentAccess Trade;
        public readonly ComponentAccess Transport;
        public readonly int ResourceWood;
        public readonly int ResourceFood;
        public readonly int ResourceGold;
        public readonly int ResourceStone;
        public readonly int TeamId;
        public readonly int PositionX;
        public readonly int PositionY;
        public readonly int HealthHp;
        public readonly int HealthMaxHp;
        public readonly int CombatUnitType;
        public readonly int NodeType;
        public readonly int NodeSubtype;
        public readonly int NodeRemaining;
        public readonly int Species;
        public readonly int GatherState;
        public readonly int CarryType;
        public readonly int CarryAmount;
        public readonly int RepairState;
        public readonly int RepairTarget;
        public readonly int BuildingType;
        public readonly int SizeWidth;
        public readonly int SizeHeight;
        public readonly int BuildingState;
        public readonly int BuildingProgress;
        public readonly int BuildingMaxProgress;
        public readonly int GateState;
        public readonly int GarrisonCount;
        public readonly int GarrisonCapacity;
        public readonly int ProgressAge;
        public readonly int ProgressResearch;
        public readonly int ProgressResearchKind;
        public readonly int ProgressResearchTarget;
        public readonly int ProgressResearchProgress;
        public readonly int ProgressResearchMaxProgress;

        public SnapshotLayout(SimWorld world)
        {
            Resource = world.Access(SimComponents.Resource);
            Team = world.Access(SimComponents.Team);
            Position = world.Access(SimComponents.Position);
            Health = world.Access(SimComponents.Health);
            Combat = world.Access(SimComponents.Combat);
            ResourceNode = world.Access(SimComponents.ResourceNode);
            HuntingAnimal = world.Access(SimComponents.HuntingAnimal);
            Gather = world.Access(SimComponents.Gather);
            Repair = world.Access(SimComponents.Repair);
            Building = world.Access(SimComponents.Building);
            Garrison = world.Access(SimComponents.Garrison);
            PlayerProgress = world.Access(SimComponents.PlayerProgress);
            Wonder = world.Access(SimComponents.Wonder);
            RelicSlot = world.Access(SimComponents.RelicSlot);
            DropSite = world.Access(SimComponents.DropSite);
            Monk = world.Access(SimComponents.Monk);
            Relic = world.Access(SimComponents.Relic);
            Trade = world.Access(SimComponents.Trade);
            Transport = world.Access(SimComponents.Transport);
            ResourceWood = Resource.Column(SimComponents.Wood);
            ResourceFood = Resource.Column(SimComponents.Food);
            ResourceGold = Resource.Column(SimComponents.Gold);
            ResourceStone = Resource.Column(SimComponents.Stone);
            TeamId = Team.Column(SimComponents.TeamId);
            PositionX = Position.Column(SimComponents.X);
            PositionY = Position.Column(SimComponents.Y);
            HealthHp = Health.Column(SimComponents.Hp);
            HealthMaxHp = Health.Column(SimComponents.MaxHp);
            CombatUnitType = Combat.Column(SimComponents.UnitType);
            NodeType = ResourceNode.Column(SimComponents.NodeType);
            NodeSubtype = ResourceNode.Column(SimComponents.Subtype);
            NodeRemaining = ResourceNode.Column(SimComponents.Remaining);
            Species = HuntingAnimal.Column(SimComponents.Species);
            GatherState = Gather.Column(SimComponents.GatherState);
            CarryType = Gather.Column(SimComponents.CarryType);
            CarryAmount = Gather.Column(SimComponents.CarryAmount);
            RepairState = Repair.Column(SimComponents.State);
            RepairTarget = Repair.Column(SimComponents.TargetBase);
            BuildingType = Building.Column(SimComponents.BuildingType);
            SizeWidth = Building.Column(SimComponents.SizeWidth);
            SizeHeight = Building.Column(SimComponents.SizeHeight);
            BuildingState = Building.Column(SimComponents.State);
            BuildingProgress = Building.Column(SimComponents.Progress);
            BuildingMaxProgress = Building.Column(SimComponents.MaxProgress);
            GateState = Building.Column(SimComponents.GateState);
            GarrisonCount = Garrison.Column(SimComponents.Count);
            GarrisonCapacity = Garrison.Column(SimComponents.Capacity);
            ProgressAge = PlayerProgress.Column(SimComponents.Age);
            ProgressResearch = PlayerProgress.Column(SimComponents.Research);
            ProgressResearchKind = PlayerProgress.Column(SimComponents.ResearchKind);
            ProgressResearchTarget = PlayerProgress.Column(SimComponents.ResearchTarget);
            ProgressResearchProgress = PlayerProgress.Column(SimComponents.ResearchProgress);
            ProgressResearchMaxProgress = PlayerProgress.Column(SimComponents.ResearchMaxProgress);
        }
    }

    public static SnapshotFrame Build(
        SimWorld world,
        long turn,
        MatchState? match = null,
        VisionGrid? vision = null,
        long viewerTeam = -1)
    {
        var layout = new SnapshotLayout(world);
        List<EntitySnapshot> entities = _entityBuffer ??= new List<EntitySnapshot>();
        entities.Clear();
        Dictionary<long, long> inventories = _inventoryBuffer ??= new Dictionary<long, long>();
        inventories.Clear();
        foreach (long id in world.Entities())
        {
            if (layout.Resource.Has(id) && layout.Team.Has(id))
            {
                inventories[layout.Team.Get(id, layout.TeamId)] = id;
            }
            if (!layout.Position.Has(id))
            {
                continue;
            }
            if (vision != null && viewerTeam >= 0 && !ShouldInclude(id, vision, viewerTeam, layout))
            {
                continue;
            }
            long kind = ClassifyEntity(id, layout);
            long team = layout.Team.Get(id, layout.TeamId);
            long hp = layout.Health.Get(id, layout.HealthHp);
            long maxHp = layout.Health.Has(id)
                ? layout.Health.Get(id, layout.HealthMaxHp)
                : hp;
            long unitType = layout.Combat.Has(id)
                ? layout.Combat.Get(id, layout.CombatUnitType)
                : -1;
            long nodeType = 0;
            long nodeSubtype = 0;
            long nodeRemaining = 0;
            if (layout.ResourceNode.Has(id))
            {
                nodeType = layout.ResourceNode.Get(id, layout.NodeType);
                nodeSubtype = layout.ResourceNode.Get(id, layout.NodeSubtype);
                nodeRemaining = layout.ResourceNode.Get(id, layout.NodeRemaining);
            }
            else if (layout.HuntingAnimal.Has(id))
            {
                nodeSubtype = layout.HuntingAnimal.Get(id, layout.Species);
            }
            long gatherState = 0;
            long carryType = 0;
            long carryAmount = 0;
            if (kind == EntityKind.Villager)
            {
                gatherState = layout.Gather.Get(id, layout.GatherState);
                carryType = layout.Gather.Get(id, layout.CarryType);
                carryAmount = layout.Gather.Get(id, layout.CarryAmount);
            }
            long buildingType = 0;
            long width = 0;
            long height = 0;
            long buildingState = 0;
            long buildingProgress = 0;
            long buildingMaxProgress = 0;
            long garrisonCount = 0;
            long garrisonCapacity = 0;
            long repairState = 0;
            long repairTarget = -1;
            long gateState = BuildingSystem.GateClosed;
            if (kind == EntityKind.Villager && layout.Repair.Has(id))
            {
                repairState = layout.Repair.Get(id, layout.RepairState);
                repairTarget = layout.Repair.Get(id, layout.RepairTarget);
            }
            if (kind == EntityKind.Building)
            {
                buildingType = layout.Building.Get(id, layout.BuildingType);
                width = layout.Building.Get(id, layout.SizeWidth);
                height = layout.Building.Get(id, layout.SizeHeight);
                buildingState = layout.Building.Get(id, layout.BuildingState);
                buildingProgress = layout.Building.Get(id, layout.BuildingProgress);
                buildingMaxProgress = layout.Building.Get(id, layout.BuildingMaxProgress);
                if (BuildingTypes.IsGate(buildingType))
                {
                    gateState = layout.Building.Get(id, layout.GateState);
                }
                if (layout.Garrison.Has(id))
                {
                    garrisonCount = layout.Garrison.Get(id, layout.GarrisonCount);
                    garrisonCapacity = layout.Garrison.Get(id, layout.GarrisonCapacity);
                }
            }
            entities.Add(new EntitySnapshot(
                id,
                layout.Position.Get(id, layout.PositionX),
                layout.Position.Get(id, layout.PositionY),
                team,
                kind,
                hp,
                nodeType,
                nodeSubtype,
                nodeRemaining,
                gatherState,
                carryType,
                carryAmount,
                buildingType,
                width,
                height,
                buildingState,
                buildingProgress,
                buildingMaxProgress,
                garrisonCount,
                garrisonCapacity,
                repairState,
                repairTarget,
                gateState,
                unitType,
                maxHp));
        }
        var frame = new SnapshotFrame { Tick = world.Tick, Turn = turn, Entities = entities.ToArray() };
        if (vision != null)
        {
            frame.VisionWidth = vision.Width;
            frame.VisionHeight = vision.Height;
            frame.VisionStates = vision.States();
            frame.HasVision = true;
        }
        if (match != null)
        {
            frame.Phase = match.Phase;
            frame.WinnerTeam = match.WinnerTeam;
            frame.Reason = match.Reason;
            frame.EndTick = match.EndTick;
            var players = new PlayerSnapshot[match.PlayerCount];
            for (long team = 0; team < match.PlayerCount; team++)
            {
                PlayerState player = match.Player(team);
                long progressEntity = PlayerProgress.FindEntity(world, team);
                long age = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressAge)
                    : TechCatalog.AgeDark;
                long research = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressResearch)
                    : 0;
                long researchKind = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressResearchKind)
                    : ResearchSystem.KindNone;
                long researchTarget = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressResearchTarget)
                    : 0;
                long researchProgress = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressResearchProgress)
                    : 0;
                long researchMaxProgress = progressEntity >= 0
                    ? layout.PlayerProgress.Get(progressEntity, layout.ProgressResearchMaxProgress)
                    : 0;
                long wood = 0;
                long food = 0;
                long gold = 0;
                long stone = 0;
                if (inventories.TryGetValue(team, out long inventory))
                {
                    wood = layout.Resource.Get(inventory, layout.ResourceWood);
                    food = layout.Resource.Get(inventory, layout.ResourceFood);
                    gold = layout.Resource.Get(inventory, layout.ResourceGold);
                    stone = layout.Resource.Get(inventory, layout.ResourceStone);
                }
                players[team] = new PlayerSnapshot(
                    team,
                    player.Defeated ? 1 : 0,
                    player.Winner ? 1 : 0,
                    player.Score,
                    player.Participants,
                    age,
                    research,
                    researchKind,
                    researchTarget,
                    researchProgress,
                    researchMaxProgress,
                    wood,
                    food,
                    gold,
                    stone,
                    MarketSystem.Rate(world, team, GameSim.ResourceWood),
                    MarketSystem.Rate(world, team, GameSim.ResourceFood),
                    MarketSystem.Rate(world, team, GameSim.ResourceGold),
                    MarketSystem.Rate(world, team, GameSim.ResourceStone),
                    PopulationSystem.Used(world, team),
                    PopulationSystem.Cap(world, team));
            }
            frame.Players = players;
        }
        return frame;
    }

    /// <summary>
    /// 从同 tick 的全量快照按观察方视野裁剪。全量快照的实体、玩家与市场价格只构建一次，
    /// 各连接只复用它并按队伍裁剪实体，避免每个观察者都重新遍历世界、重算玩家派生数据。
    /// </summary>
    public static SnapshotFrame Filter(SimWorld world, SnapshotFrame full, VisionGrid vision, long viewerTeam)
    {
        var layout = new SnapshotLayout(world);
        List<EntitySnapshot> kept = _entityBuffer ??= new List<EntitySnapshot>();
        kept.Clear();
        foreach (EntitySnapshot entity in full.Entities)
        {
            if (ShouldInclude(entity.Id, vision, viewerTeam, layout))
            {
                kept.Add(entity);
            }
        }
        return new SnapshotFrame
        {
            Tick = full.Tick,
            Turn = full.Turn,
            Phase = full.Phase,
            WinnerTeam = full.WinnerTeam,
            Reason = full.Reason,
            EndTick = full.EndTick,
            Players = full.Players,
            Entities = kept.ToArray(),
            VisionWidth = vision.Width,
            VisionHeight = vision.Height,
            HasVision = true,
            VisionStates = vision.States(),
        };
    }

    private static long ClassifyEntity(long id, in SnapshotLayout layout)
    {
        // 农田同时持有 Building 与 ResourceNode：先判建筑，否则会被当成普通资源点丢掉建筑字段。
        if (layout.Building.Has(id))
        {
            return EntityKind.Building;
        }
        if (layout.ResourceNode.Has(id))
        {
            return EntityKind.ResourceNode;
        }
        if (layout.HuntingAnimal.Has(id))
        {
            return EntityKind.HuntingAnimal;
        }
        if (layout.DropSite.Has(id))
        {
            return EntityKind.DropSite;
        }
        if (layout.Gather.Has(id))
        {
            return EntityKind.Villager;
        }
        if (layout.Monk.Has(id))
        {
            return EntityKind.Monk;
        }
        if (layout.Relic.Has(id))
        {
            return EntityKind.Relic;
        }
        if (layout.RelicSlot.Has(id))
        {
            return EntityKind.RelicSlot;
        }
        if (layout.Trade.Has(id))
        {
            return EntityKind.TradeCart;
        }
        if (layout.Transport.Has(id) || layout.Combat.Has(id))
        {
            return EntityKind.Unit;
        }
        return EntityKind.Inventory;
    }

    /// <summary>按观察者玩家裁剪快照：己方恒可见，中立与静态建筑按已探索，敌方单位仅当前可见。</summary>
    public static bool ShouldInclude(SimWorld world, long id, VisionGrid vision, long viewerTeam) =>
        ShouldInclude(id, vision, viewerTeam, new SnapshotLayout(world));

    private static bool ShouldInclude(long id, VisionGrid vision, long viewerTeam, in SnapshotLayout layout)
    {
        int tileX = (int)Fixed.ToInt(layout.Position.Get(id, layout.PositionX));
        int tileY = (int)Fixed.ToInt(layout.Position.Get(id, layout.PositionY));
        bool hasTeam = layout.Team.Has(id);
        if (hasTeam && layout.Team.Get(id, layout.TeamId) == viewerTeam)
        {
            return true;
        }
        if (!hasTeam)
        {
            return vision.IsExplored(tileX, tileY);
        }
        bool structure = layout.Building.Has(id)
            || layout.Wonder.Has(id)
            || layout.RelicSlot.Has(id);
        if (structure)
        {
            return vision.IsExplored(tileX, tileY);
        }
        return vision.VisibleAt(tileX, tileY);
    }

    public byte[] Encode()
    {
        byte[] buffer = EncodeToBuffer(out int length);
        var result = new byte[length];
        System.Buffer.BlockCopy(buffer, 0, result, 0, length);
        return result;
    }

    /// <summary>写入可复用的内部缓冲，返回起始缓冲与有效长度；调用方须在下一次 EncodeToBuffer 前用完。</summary>
    public byte[] EncodeToBuffer(out int length)
    {
        MemoryStream stream = _encodeStream ??= new MemoryStream(64 * 1024);
        stream.SetLength(0);
        stream.Position = 0;
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Tick);
            writer.Write(Turn);
            writer.Write(Phase);
            writer.Write(WinnerTeam);
            writer.Write(Reason);
            writer.Write(EndTick);
            writer.Write(Delta);
            writer.Write(Players.Length);
            foreach (PlayerSnapshot player in Players)
            {
                writer.Write(player.Team);
                writer.Write(player.Defeated);
                writer.Write(player.Winner);
                writer.Write(player.Score);
                writer.Write(player.Participants);
                writer.Write(player.Age);
                writer.Write(player.Research);
                writer.Write(player.ResearchKind);
                writer.Write(player.ResearchTarget);
                writer.Write(player.ResearchProgress);
                writer.Write(player.ResearchMaxProgress);
                writer.Write(player.Wood);
                writer.Write(player.Food);
                writer.Write(player.Gold);
                writer.Write(player.Stone);
                writer.Write(player.MarketWood);
                writer.Write(player.MarketFood);
                writer.Write(player.MarketGold);
                writer.Write(player.MarketStone);
                writer.Write(player.PopulationUsed);
                writer.Write(player.PopulationCap);
            }
            writer.Write(Entities.Length);
            for (int index = 0; index < Entities.Length; index++)
            {
                WriteEntity(writer, Entities[index], EntityMasks.Length == Entities.Length ? EntityMasks[index] : SnapshotDelta.AllFields);
            }
            writer.Write(RemovedEntities.Length);
            foreach (long removed in RemovedEntities)
            {
                writer.Write(removed);
            }
            writer.Write(VisionWidth);
            writer.Write(VisionHeight);
            writer.Write(HasVision);
            if (HasVision)
            {
                WriteVisionStates(writer, VisionStates);
            }
        }
        length = (int)stream.Length;
        return stream.GetBuffer();
    }

    /// <summary>按掩码写实体：Id 恒写，其余字段仅写掩码位为 1 的部分，掩码为全量时等价于完整字段。</summary>
    private static void WriteEntity(BinaryWriter writer, in EntitySnapshot entity, uint mask)
    {
        writer.Write(entity.Id);
        writer.Write(mask);
        if ((mask & 1u) != 0) writer.Write(entity.X);
        if ((mask & (1u << 1)) != 0) writer.Write(entity.Y);
        if ((mask & (1u << 2)) != 0) writer.Write(entity.Team);
        if ((mask & (1u << 3)) != 0) writer.Write(entity.Kind);
        if ((mask & (1u << 4)) != 0) writer.Write(entity.Hp);
        if ((mask & (1u << 5)) != 0) writer.Write(entity.NodeType);
        if ((mask & (1u << 6)) != 0) writer.Write(entity.NodeSubtype);
        if ((mask & (1u << 7)) != 0) writer.Write(entity.NodeRemaining);
        if ((mask & (1u << 8)) != 0) writer.Write(entity.GatherState);
        if ((mask & (1u << 9)) != 0) writer.Write(entity.CarryType);
        if ((mask & (1u << 10)) != 0) writer.Write(entity.CarryAmount);
        if ((mask & (1u << 11)) != 0) writer.Write(entity.BuildingType);
        if ((mask & (1u << 12)) != 0) writer.Write(entity.Width);
        if ((mask & (1u << 13)) != 0) writer.Write(entity.Height);
        if ((mask & (1u << 14)) != 0) writer.Write(entity.BuildingState);
        if ((mask & (1u << 15)) != 0) writer.Write(entity.BuildingProgress);
        if ((mask & (1u << 16)) != 0) writer.Write(entity.BuildingMaxProgress);
        if ((mask & (1u << 17)) != 0) writer.Write(entity.GarrisonCount);
        if ((mask & (1u << 18)) != 0) writer.Write(entity.GarrisonCapacity);
        if ((mask & (1u << 19)) != 0) writer.Write(entity.RepairState);
        if ((mask & (1u << 20)) != 0) writer.Write(entity.RepairTarget);
        if ((mask & (1u << 21)) != 0) writer.Write(entity.GateState);
        if ((mask & (1u << 22)) != 0) writer.Write(entity.UnitType);
        if ((mask & (1u << 23)) != 0) writer.Write(entity.MaxHp);
    }

    private static EntitySnapshot ReadEntity(BinaryReader reader, long id, in EntitySnapshot baseline)
    {
        uint mask = reader.ReadUInt32();
        long x = baseline.X;
        long y = baseline.Y;
        long team = baseline.Team;
        long kind = baseline.Kind;
        long hp = baseline.Hp;
        long nodeType = baseline.NodeType;
        long nodeSubtype = baseline.NodeSubtype;
        long nodeRemaining = baseline.NodeRemaining;
        long gatherState = baseline.GatherState;
        long carryType = baseline.CarryType;
        long carryAmount = baseline.CarryAmount;
        long buildingType = baseline.BuildingType;
        long width = baseline.Width;
        long height = baseline.Height;
        long buildingState = baseline.BuildingState;
        long buildingProgress = baseline.BuildingProgress;
        long buildingMaxProgress = baseline.BuildingMaxProgress;
        long garrisonCount = baseline.GarrisonCount;
        long garrisonCapacity = baseline.GarrisonCapacity;
        long repairState = baseline.RepairState;
        long repairTarget = baseline.RepairTarget;
        long gateState = baseline.GateState;
        long unitType = baseline.UnitType;
        long maxHp = baseline.MaxHp;
        if ((mask & 1u) != 0) x = reader.ReadInt64();
        if ((mask & (1u << 1)) != 0) y = reader.ReadInt64();
        if ((mask & (1u << 2)) != 0) team = reader.ReadInt64();
        if ((mask & (1u << 3)) != 0) kind = reader.ReadInt64();
        if ((mask & (1u << 4)) != 0) hp = reader.ReadInt64();
        if ((mask & (1u << 5)) != 0) nodeType = reader.ReadInt64();
        if ((mask & (1u << 6)) != 0) nodeSubtype = reader.ReadInt64();
        if ((mask & (1u << 7)) != 0) nodeRemaining = reader.ReadInt64();
        if ((mask & (1u << 8)) != 0) gatherState = reader.ReadInt64();
        if ((mask & (1u << 9)) != 0) carryType = reader.ReadInt64();
        if ((mask & (1u << 10)) != 0) carryAmount = reader.ReadInt64();
        if ((mask & (1u << 11)) != 0) buildingType = reader.ReadInt64();
        if ((mask & (1u << 12)) != 0) width = reader.ReadInt64();
        if ((mask & (1u << 13)) != 0) height = reader.ReadInt64();
        if ((mask & (1u << 14)) != 0) buildingState = reader.ReadInt64();
        if ((mask & (1u << 15)) != 0) buildingProgress = reader.ReadInt64();
        if ((mask & (1u << 16)) != 0) buildingMaxProgress = reader.ReadInt64();
        if ((mask & (1u << 17)) != 0) garrisonCount = reader.ReadInt64();
        if ((mask & (1u << 18)) != 0) garrisonCapacity = reader.ReadInt64();
        if ((mask & (1u << 19)) != 0) repairState = reader.ReadInt64();
        if ((mask & (1u << 20)) != 0) repairTarget = reader.ReadInt64();
        if ((mask & (1u << 21)) != 0) gateState = reader.ReadInt64();
        if ((mask & (1u << 22)) != 0) unitType = reader.ReadInt64();
        if ((mask & (1u << 23)) != 0) maxHp = reader.ReadInt64();
        return new EntitySnapshot(
            id, x, y, team, kind, hp, nodeType, nodeSubtype, nodeRemaining,
            gatherState, carryType, carryAmount, buildingType, width, height,
            buildingState, buildingProgress, buildingMaxProgress, garrisonCount,
            garrisonCapacity, repairState, repairTarget, gateState, unitType, maxHp);
    }

    private static void WriteVisionStates(BinaryWriter writer, byte[] states)
    {
        writer.Write(states.Length);
        if (states.Length == 0)
        {
            return;
        }
        int runCount = 0;
        for (int index = 0; index < states.Length;)
        {
            byte value = states[index];
            while (index < states.Length && states[index] == value)
            {
                index++;
            }
            runCount++;
        }
        bool useRle = runCount * 5 < states.Length;
        writer.Write(useRle);
        if (!useRle)
        {
            writer.Write(states);
            return;
        }
        writer.Write(runCount);
        for (int index = 0; index < states.Length;)
        {
            byte value = states[index];
            int runStart = index;
            while (index < states.Length && states[index] == value)
            {
                index++;
            }
            writer.Write(value);
            writer.Write(index - runStart);
        }
    }

    private static byte[] ReadVisionStates(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == 0)
        {
            return System.Array.Empty<byte>();
        }
        if (!reader.ReadBoolean())
        {
            return reader.ReadBytes(length);
        }
        var states = new byte[length];
        int runCount = reader.ReadInt32();
        int offset = 0;
        for (int run = 0; run < runCount; run++)
        {
            byte value = reader.ReadByte();
            int count = reader.ReadInt32();
            System.Array.Fill(states, value, offset, count);
            offset += count;
        }
        return states;
    }

    /// <summary>
    /// 解码快照。增量帧的字段掩码需要基线恢复：<paramref name="previous"/> 必须是客户端上一帧合并后的
    /// 完整快照；关键帧（Delta=false）可传 null。
    /// </summary>
    public static SnapshotFrame Decode(byte[] data, SnapshotFrame? previous = null)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var frame = new SnapshotFrame
        {
            Tick = reader.ReadInt64(),
            Turn = reader.ReadInt64(),
            Phase = reader.ReadInt64(),
            WinnerTeam = reader.ReadInt64(),
            Reason = reader.ReadInt64(),
            EndTick = reader.ReadInt64(),
            Delta = reader.ReadBoolean(),
        };
        Dictionary<long, EntitySnapshot>? baselines = null;
        if (frame.Delta && previous != null)
        {
            baselines = new Dictionary<long, EntitySnapshot>(previous.Entities.Length);
            foreach (EntitySnapshot entity in previous.Entities)
            {
                baselines[entity.Id] = entity;
            }
        }
        int playerCount = reader.ReadInt32();
        var players = new PlayerSnapshot[playerCount];
        for (int index = 0; index < playerCount; index++)
        {
            players[index] = new PlayerSnapshot(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64());
        }
        frame.Players = players;
        int count = reader.ReadInt32();
        var entities = new EntitySnapshot[count];
        for (int index = 0; index < count; index++)
        {
            long id = reader.ReadInt64();
            EntitySnapshot baseline = default;
            if (baselines != null)
            {
                baselines.TryGetValue(id, out baseline);
            }
            entities[index] = ReadEntity(reader, id, baseline);
        }
        frame.Entities = entities;
        int removedCount = reader.ReadInt32();
        var removed = new long[removedCount];
        for (int index = 0; index < removedCount; index++)
        {
            removed[index] = reader.ReadInt64();
        }
        frame.RemovedEntities = removed;
        frame.VisionWidth = reader.ReadInt64();
        frame.VisionHeight = reader.ReadInt64();
        frame.HasVision = reader.ReadBoolean();
        frame.VisionStates = frame.HasVision ? ReadVisionStates(reader) : System.Array.Empty<byte>();
        return frame;
    }

    /// <summary>把增量帧合并到上一帧完整快照：替换变化实体、删除消失实体；视野未下发时沿用上一帧。</summary>
    public SnapshotFrame Merge(SnapshotFrame previous)
    {
        if (!Delta)
        {
            return this;
        }
        var removed = new HashSet<long>(RemovedEntities);
        var changed = new Dictionary<long, EntitySnapshot>(Entities.Length);
        foreach (EntitySnapshot entity in Entities)
        {
            changed[entity.Id] = entity;
        }
        var merged = new EntitySnapshot[previous.Entities.Length + Entities.Length];
        int count = 0;
        foreach (EntitySnapshot entity in previous.Entities)
        {
            if (removed.Contains(entity.Id))
            {
                continue;
            }
            merged[count++] = changed.TryGetValue(entity.Id, out EntitySnapshot updated) ? updated : entity;
            changed.Remove(entity.Id);
        }
        foreach (EntitySnapshot entity in changed.Values)
        {
            merged[count++] = entity;
        }
        System.Array.Resize(ref merged, count);
        return new SnapshotFrame
        {
            Tick = Tick,
            Turn = Turn,
            Phase = Phase,
            WinnerTeam = WinnerTeam,
            Reason = Reason,
            EndTick = EndTick,
            Players = Players,
            Entities = merged,
            VisionWidth = HasVision ? VisionWidth : previous.VisionWidth,
            VisionHeight = HasVision ? VisionHeight : previous.VisionHeight,
            HasVision = HasVision || previous.HasVision,
            VisionStates = HasVision ? VisionStates : previous.VisionStates,
        };
    }
}

/// <summary>
/// 每条连接一份的增量编码器：首次下发关键帧，其后只下发新增/变化实体（按字段掩码）、消失实体 id，
/// 视野位图仅在内容变化时附带。变化检测只做整数比较，不改变模拟状态。
/// </summary>
public sealed class SnapshotDelta
{
    /// <summary>实体全部 24 个可变字段的掩码（Id 恒写，不占位）。</summary>
    public const uint AllFields = 0x00FFFFFFu;

    private readonly Dictionary<long, EntitySnapshot> _previous = new();
    private readonly HashSet<long> _currentIds = new();
    private readonly List<EntitySnapshot> _changed = new();
    private readonly List<uint> _masks = new();
    private readonly List<long> _removed = new();
    private byte[]? _vision;

    /// <summary>丢弃已发送状态：下一次 <see cref="Build"/> 强制关键帧（慢客户端丢帧或重连时调用）。</summary>
    public void Reset()
    {
        _previous.Clear();
        _vision = null;
    }

    public SnapshotFrame Build(SnapshotFrame current)
    {
        bool keyframe = _previous.Count == 0;
        _currentIds.Clear();
        _changed.Clear();
        _masks.Clear();
        _removed.Clear();
        foreach (EntitySnapshot entity in current.Entities)
        {
            _currentIds.Add(entity.Id);
            if (keyframe)
            {
                _changed.Add(entity);
                _masks.Add(AllFields);
                continue;
            }
            if (!_previous.TryGetValue(entity.Id, out EntitySnapshot previous))
            {
                _changed.Add(entity);
                _masks.Add(AllFields);
            }
            else if (!previous.SameAs(entity))
            {
                _changed.Add(entity);
                _masks.Add(DiffMask(previous, entity));
            }
        }
        if (!keyframe)
        {
            foreach (long id in _previous.Keys)
            {
                if (!_currentIds.Contains(id))
                {
                    _removed.Add(id);
                }
            }
        }
        foreach (EntitySnapshot entity in _changed)
        {
            _previous[entity.Id] = entity;
        }
        foreach (long id in _removed)
        {
            _previous.Remove(id);
        }
        bool visionChanged = current.HasVision
            && (keyframe || _vision == null || !SameBytes(_vision, current.VisionStates));
        if (visionChanged)
        {
            _vision = current.VisionStates;
        }
        return new SnapshotFrame
        {
            Tick = current.Tick,
            Turn = current.Turn,
            Phase = current.Phase,
            WinnerTeam = current.WinnerTeam,
            Reason = current.Reason,
            EndTick = current.EndTick,
            Players = current.Players,
            Delta = !keyframe,
            Entities = _changed.ToArray(),
            EntityMasks = _masks.ToArray(),
            RemovedEntities = _removed.ToArray(),
            VisionWidth = current.VisionWidth,
            VisionHeight = current.VisionHeight,
            HasVision = visionChanged,
            VisionStates = visionChanged ? _vision! : System.Array.Empty<byte>(),
        };
    }

    private static uint DiffMask(in EntitySnapshot previous, in EntitySnapshot current)
    {
        uint mask = 0;
        if (current.X != previous.X) mask |= 1u;
        if (current.Y != previous.Y) mask |= 1u << 1;
        if (current.Team != previous.Team) mask |= 1u << 2;
        if (current.Kind != previous.Kind) mask |= 1u << 3;
        if (current.Hp != previous.Hp) mask |= 1u << 4;
        if (current.NodeType != previous.NodeType) mask |= 1u << 5;
        if (current.NodeSubtype != previous.NodeSubtype) mask |= 1u << 6;
        if (current.NodeRemaining != previous.NodeRemaining) mask |= 1u << 7;
        if (current.GatherState != previous.GatherState) mask |= 1u << 8;
        if (current.CarryType != previous.CarryType) mask |= 1u << 9;
        if (current.CarryAmount != previous.CarryAmount) mask |= 1u << 10;
        if (current.BuildingType != previous.BuildingType) mask |= 1u << 11;
        if (current.Width != previous.Width) mask |= 1u << 12;
        if (current.Height != previous.Height) mask |= 1u << 13;
        if (current.BuildingState != previous.BuildingState) mask |= 1u << 14;
        if (current.BuildingProgress != previous.BuildingProgress) mask |= 1u << 15;
        if (current.BuildingMaxProgress != previous.BuildingMaxProgress) mask |= 1u << 16;
        if (current.GarrisonCount != previous.GarrisonCount) mask |= 1u << 17;
        if (current.GarrisonCapacity != previous.GarrisonCapacity) mask |= 1u << 18;
        if (current.RepairState != previous.RepairState) mask |= 1u << 19;
        if (current.RepairTarget != previous.RepairTarget) mask |= 1u << 20;
        if (current.GateState != previous.GateState) mask |= 1u << 21;
        if (current.UnitType != previous.UnitType) mask |= 1u << 22;
        if (current.MaxHp != previous.MaxHp) mask |= 1u << 23;
        return mask;
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }
}

public sealed class CommandFrame
{
    public long Player;
    public long Turn;
    public LockstepCommand[] Commands = System.Array.Empty<LockstepCommand>();

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Player);
            writer.Write(Turn);
            writer.Write(Commands.Length);
            foreach (LockstepCommand command in Commands)
            {
                writer.Write(command.Player);
                writer.Write(command.Kind);
                writer.Write(command.Entity);
                writer.Write(command.CellX);
                writer.Write(command.CellY);
                writer.Write(command.TargetEntity);
                writer.Write(command.DropEntity);
                writer.Write(command.Stance);
                writer.Write(command.Cell2X);
                writer.Write(command.Cell2Y);
                writer.Write(command.Aux);
                writer.Write(command.Queue);
            }
        }
        return stream.ToArray();
    }

    public static CommandFrame Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var frame = new CommandFrame { Player = reader.ReadInt64(), Turn = reader.ReadInt64() };
        int count = reader.ReadInt32();
        var commands = new LockstepCommand[count];
        for (int index = 0; index < count; index++)
        {
            commands[index] = new LockstepCommand(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64());
        }
        frame.Commands = commands;
        return frame;
    }
}
