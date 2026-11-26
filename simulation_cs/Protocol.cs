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

    public EntitySnapshot(
        long id,
        long x,
        long y,
        long team,
        long kind,
        long hp,
        long nodeType,
        long nodeRemaining,
        long gatherState,
        long carryType,
        long carryAmount,
        long buildingType,
        long width,
        long height,
        long buildingState,
        long buildingProgress,
        long buildingMaxProgress)
    {
        Id = id;
        X = x;
        Y = y;
        Team = team;
        Kind = kind;
        Hp = hp;
        NodeType = nodeType;
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
    }
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
    public readonly long Wood;
    public readonly long Food;
    public readonly long Gold;
    public readonly long Stone;

    public PlayerSnapshot(
        long team,
        long defeated,
        long winner,
        long score,
        long participants,
        long age,
        long research,
        long wood,
        long food,
        long gold,
        long stone)
    {
        Team = team;
        Defeated = defeated;
        Winner = winner;
        Score = score;
        Participants = participants;
        Age = age;
        Research = research;
        Wood = wood;
        Food = food;
        Gold = gold;
        Stone = stone;
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
    public byte[] VisionStates = System.Array.Empty<byte>();
    public long TerrainWidth;
    public long TerrainHeight;
    public byte[] TerrainTypes = System.Array.Empty<byte>();
    public byte[] TerrainHeights = System.Array.Empty<byte>();
    public PlayerSnapshot[] Players = System.Array.Empty<PlayerSnapshot>();
    public EntitySnapshot[] Entities = System.Array.Empty<EntitySnapshot>();

    public static SnapshotFrame Build(
        SimWorld world,
        long turn,
        MatchState? match = null,
        VisionGrid? vision = null,
        long viewerTeam = -1,
        TerrainGrid? terrain = null)
    {
        var entities = new List<EntitySnapshot>();
        var inventories = new Dictionary<long, long>();
        foreach (long id in world.Entities())
        {
            if (world.HasComponent(id, SimComponents.Resource) && world.HasComponent(id, SimComponents.Team))
            {
                inventories[world.GetValue(id, SimComponents.Team, SimComponents.TeamId)] = id;
            }
            if (!world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            if (vision != null && viewerTeam >= 0 && !ShouldInclude(world, id, vision, viewerTeam))
            {
                continue;
            }
            long kind = ClassifyEntity(world, id);
            long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            long hp = world.GetValue(id, SimComponents.Health, SimComponents.Hp);
            long nodeType = 0;
            long nodeRemaining = 0;
            if (kind == EntityKind.ResourceNode)
            {
                nodeType = world.GetValue(id, SimComponents.ResourceNode, SimComponents.NodeType);
                nodeRemaining = world.GetValue(id, SimComponents.ResourceNode, SimComponents.Remaining);
            }
            long gatherState = 0;
            long carryType = 0;
            long carryAmount = 0;
            if (kind == EntityKind.Villager)
            {
                gatherState = world.GetValue(id, SimComponents.Gather, SimComponents.GatherState);
                carryType = world.GetValue(id, SimComponents.Gather, SimComponents.CarryType);
                carryAmount = world.GetValue(id, SimComponents.Gather, SimComponents.CarryAmount);
            }
            long buildingType = 0;
            long width = 0;
            long height = 0;
            long buildingState = 0;
            long buildingProgress = 0;
            long buildingMaxProgress = 0;
            if (kind == EntityKind.Building)
            {
                buildingType = world.GetValue(id, SimComponents.Building, SimComponents.BuildingType);
                width = world.GetValue(id, SimComponents.Building, SimComponents.SizeWidth);
                height = world.GetValue(id, SimComponents.Building, SimComponents.SizeHeight);
                buildingState = world.GetValue(id, SimComponents.Building, SimComponents.State);
                buildingProgress = world.GetValue(id, SimComponents.Building, SimComponents.Progress);
                buildingMaxProgress = world.GetValue(id, SimComponents.Building, SimComponents.MaxProgress);
            }
            entities.Add(new EntitySnapshot(
                id,
                world.GetValue(id, SimComponents.Position, SimComponents.X),
                world.GetValue(id, SimComponents.Position, SimComponents.Y),
                team,
                kind,
                hp,
                nodeType,
                nodeRemaining,
                gatherState,
                carryType,
                carryAmount,
                buildingType,
                width,
                height,
                buildingState,
                buildingProgress,
                buildingMaxProgress));
        }
        var frame = new SnapshotFrame { Tick = world.Tick, Turn = turn, Entities = entities.ToArray() };
        if (vision != null)
        {
            frame.VisionWidth = vision.Width;
            frame.VisionHeight = vision.Height;
            frame.VisionStates = vision.States();
        }
        if (terrain != null)
        {
            frame.TerrainWidth = terrain.Width;
            frame.TerrainHeight = terrain.Height;
            frame.TerrainTypes = (byte[])terrain.Types.Clone();
            frame.TerrainHeights = (byte[])terrain.Heights.Clone();
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
                    ? world.GetValue(progressEntity, SimComponents.PlayerProgress, SimComponents.Age)
                    : TechCatalog.AgeDark;
                long research = progressEntity >= 0
                    ? world.GetValue(progressEntity, SimComponents.PlayerProgress, SimComponents.Research)
                    : 0;
                long wood = 0;
                long food = 0;
                long gold = 0;
                long stone = 0;
                if (inventories.TryGetValue(team, out long inventory))
                {
                    wood = world.GetValue(inventory, SimComponents.Resource, SimComponents.Wood);
                    food = world.GetValue(inventory, SimComponents.Resource, SimComponents.Food);
                    gold = world.GetValue(inventory, SimComponents.Resource, SimComponents.Gold);
                    stone = world.GetValue(inventory, SimComponents.Resource, SimComponents.Stone);
                }
                players[team] = new PlayerSnapshot(
                    team,
                    player.Defeated ? 1 : 0,
                    player.Winner ? 1 : 0,
                    player.Score,
                    player.Participants,
                    age,
                    research,
                    wood,
                    food,
                    gold,
                    stone);
            }
            frame.Players = players;
        }
        return frame;
    }

    private static long ClassifyEntity(SimWorld world, long id)
    {
        if (world.HasComponent(id, SimComponents.ResourceNode))
        {
            return EntityKind.ResourceNode;
        }
        if (world.HasComponent(id, SimComponents.DropSite))
        {
            return EntityKind.DropSite;
        }
        if (world.HasComponent(id, SimComponents.Gather))
        {
            return EntityKind.Villager;
        }
        if (world.HasComponent(id, SimComponents.Monk))
        {
            return EntityKind.Monk;
        }
        if (world.HasComponent(id, SimComponents.Relic))
        {
            return EntityKind.Relic;
        }
        if (world.HasComponent(id, SimComponents.RelicSlot))
        {
            return EntityKind.RelicSlot;
        }
        if (world.HasComponent(id, SimComponents.Building))
        {
            return EntityKind.Building;
        }
        if (world.HasComponent(id, SimComponents.Combat))
        {
            return EntityKind.Unit;
        }
        return EntityKind.Inventory;
    }

    /// <summary>按观察者玩家裁剪快照：己方恒可见，中立与静态建筑按已探索，敌方单位仅当前可见。</summary>
    public static bool ShouldInclude(SimWorld world, long id, VisionGrid vision, long viewerTeam)
    {
        long x = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));
        long y = Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));
        int tileX = (int)x;
        int tileY = (int)y;
        bool hasTeam = world.HasComponent(id, SimComponents.Team);
        if (hasTeam && world.GetValue(id, SimComponents.Team, SimComponents.TeamId) == viewerTeam)
        {
            return true;
        }
        if (!hasTeam)
        {
            return vision.IsExplored(tileX, tileY);
        }
        bool structure = world.HasComponent(id, SimComponents.Building)
            || world.HasComponent(id, SimComponents.Wonder)
            || world.HasComponent(id, SimComponents.RelicSlot);
        if (structure)
        {
            return vision.IsExplored(tileX, tileY);
        }
        return vision.VisibleAt(tileX, tileY);
    }

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Tick);
            writer.Write(Turn);
            writer.Write(Phase);
            writer.Write(WinnerTeam);
            writer.Write(Reason);
            writer.Write(EndTick);
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
                writer.Write(player.Wood);
                writer.Write(player.Food);
                writer.Write(player.Gold);
                writer.Write(player.Stone);
            }
            writer.Write(Entities.Length);
            foreach (EntitySnapshot entity in Entities)
            {
                writer.Write(entity.Id);
                writer.Write(entity.X);
                writer.Write(entity.Y);
                writer.Write(entity.Team);
                writer.Write(entity.Kind);
                writer.Write(entity.Hp);
                writer.Write(entity.NodeType);
                writer.Write(entity.NodeRemaining);
                writer.Write(entity.GatherState);
                writer.Write(entity.CarryType);
                writer.Write(entity.CarryAmount);
                writer.Write(entity.BuildingType);
                writer.Write(entity.Width);
                writer.Write(entity.Height);
                writer.Write(entity.BuildingState);
                writer.Write(entity.BuildingProgress);
                writer.Write(entity.BuildingMaxProgress);
            }
            writer.Write(VisionWidth);
            writer.Write(VisionHeight);
            writer.Write(VisionStates.Length);
            writer.Write(VisionStates);
            writer.Write(TerrainWidth);
            writer.Write(TerrainHeight);
            writer.Write(TerrainTypes.Length);
            writer.Write(TerrainTypes);
            writer.Write(TerrainHeights.Length);
            writer.Write(TerrainHeights);
        }
        return stream.ToArray();
    }

    public static SnapshotFrame Decode(byte[] data)
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
        };
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
                reader.ReadInt64());
        }
        frame.Players = players;
        int count = reader.ReadInt32();
        var entities = new EntitySnapshot[count];
        for (int index = 0; index < count; index++)
        {
            entities[index] = new EntitySnapshot(
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
        frame.Entities = entities;
        frame.VisionWidth = reader.ReadInt64();
        frame.VisionHeight = reader.ReadInt64();
        int visionCount = reader.ReadInt32();
        frame.VisionStates = reader.ReadBytes(visionCount);
        frame.TerrainWidth = reader.ReadInt64();
        frame.TerrainHeight = reader.ReadInt64();
        int terrainTypeCount = reader.ReadInt32();
        frame.TerrainTypes = reader.ReadBytes(terrainTypeCount);
        int terrainHeightCount = reader.ReadInt32();
        frame.TerrainHeights = reader.ReadBytes(terrainHeightCount);
        return frame;
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
