namespace AoeSim;

public sealed class LockstepCommand
{
    public const long KindMove = 0;
    public const long KindAttack = 1;
    public const long KindStop = 2;
    public const long KindGather = 3;
    public const long KindSurrender = 4;
    public const long KindHeal = 5;
    public const long KindConvert = 6;
    public const long KindPickupRelic = 7;
    public const long KindDepositRelic = 8;

    public long Player { get; set; }
    public long Kind { get; set; }
    public long Entity { get; set; }
    public long CellX { get; set; }
    public long CellY { get; set; }
    public long TargetEntity { get; set; }
    public long DropEntity { get; set; }

    public LockstepCommand(
        long player = 0,
        long kind = KindMove,
        long entity = -1,
        long cellX = 0,
        long cellY = 0,
        long targetEntity = -1,
        long dropEntity = -1)
    {
        Player = player;
        Kind = kind;
        Entity = entity;
        CellX = cellX;
        CellY = cellY;
        TargetEntity = targetEntity;
        DropEntity = dropEntity;
    }

    public long[] ToArray() =>
        new[] { Player, Kind, Entity, CellX, CellY, TargetEntity, DropEntity };

    public static LockstepCommand FromArray(long[] values)
    {
        var command = new LockstepCommand();
        if (values.Length >= 1)
        {
            command.Player = values[0];
        }
        if (values.Length >= 2)
        {
            command.Kind = values[1];
        }
        if (values.Length >= 3)
        {
            command.Entity = values[2];
        }
        if (values.Length >= 4)
        {
            command.CellX = values[3];
        }
        if (values.Length >= 5)
        {
            command.CellY = values[4];
        }
        if (values.Length >= 6)
        {
            command.TargetEntity = values[5];
        }
        if (values.Length >= 7)
        {
            command.DropEntity = values[6];
        }
        return command;
    }
}
