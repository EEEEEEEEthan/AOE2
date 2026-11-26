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
}

public readonly struct EntitySnapshot
{
    public readonly long Id;
    public readonly long X;
    public readonly long Y;
    public readonly long Team;
    public readonly long Kind;
    public readonly long Hp;

    public EntitySnapshot(long id, long x, long y, long team, long kind, long hp)
    {
        Id = id;
        X = x;
        Y = y;
        Team = team;
        Kind = kind;
        Hp = hp;
    }
}

public readonly struct PlayerSnapshot
{
    public readonly long Team;
    public readonly long Defeated;
    public readonly long Winner;
    public readonly long Score;
    public readonly long Participants;

    public PlayerSnapshot(long team, long defeated, long winner, long score, long participants)
    {
        Team = team;
        Defeated = defeated;
        Winner = winner;
        Score = score;
        Participants = participants;
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
    public PlayerSnapshot[] Players = System.Array.Empty<PlayerSnapshot>();
    public EntitySnapshot[] Entities = System.Array.Empty<EntitySnapshot>();

    public static SnapshotFrame Build(SimWorld world, long turn, MatchState? match = null)
    {
        var entities = new List<EntitySnapshot>();
        foreach (long id in world.Entities())
        {
            if (!world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            long kind = ClassifyEntity(world, id);
            long team = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            long hp = world.GetValue(id, SimComponents.Health, SimComponents.Hp);
            entities.Add(new EntitySnapshot(
                id,
                world.GetValue(id, SimComponents.Position, SimComponents.X),
                world.GetValue(id, SimComponents.Position, SimComponents.Y),
                team,
                kind,
                hp));
        }
        var frame = new SnapshotFrame { Tick = world.Tick, Turn = turn, Entities = entities.ToArray() };
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
                players[team] = new PlayerSnapshot(
                    team,
                    player.Defeated ? 1 : 0,
                    player.Winner ? 1 : 0,
                    player.Score,
                    player.Participants);
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
        if (world.HasComponent(id, SimComponents.Combat))
        {
            return EntityKind.Unit;
        }
        return EntityKind.Inventory;
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
            }
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
                reader.ReadInt64());
        }
        frame.Entities = entities;
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
                reader.ReadInt64());
        }
        frame.Commands = commands;
        return frame;
    }
}
