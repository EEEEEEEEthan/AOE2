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
    public const long KindStance = 9;
    public const long KindAttackMove = 10;
    public const long KindPatrol = 11;
    public const long KindGuard = 12;
    public const long KindFollow = 13;
    public const long KindHold = 14;
    public const long KindSetRally = 15;
    public const long KindAgeUp = 16;
    public const long KindResearch = 17;
    public const long KindPlaceBuilding = 18;
    public const long KindConstruct = 19;

    public const int FieldCount = 12;

    public long Player { get; set; }
    public long Kind { get; set; }
    public long Entity { get; set; }
    public long CellX { get; set; }
    public long CellY { get; set; }
    public long TargetEntity { get; set; }
    public long DropEntity { get; set; }
    public long Stance { get; set; }
    public long Cell2X { get; set; }
    public long Cell2Y { get; set; }
    public long Aux { get; set; }
    public long Queue { get; set; }

    public LockstepCommand(
        long player = 0,
        long kind = KindMove,
        long entity = -1,
        long cellX = 0,
        long cellY = 0,
        long targetEntity = -1,
        long dropEntity = -1,
        long stance = -1,
        long cell2X = 0,
        long cell2Y = 0,
        long aux = 0,
        long queue = 0)
    {
        Player = player;
        Kind = kind;
        Entity = entity;
        CellX = cellX;
        CellY = cellY;
        TargetEntity = targetEntity;
        DropEntity = dropEntity;
        Stance = stance;
        Cell2X = cell2X;
        Cell2Y = cell2Y;
        Aux = aux;
        Queue = queue;
    }

    public long[] ToArray() =>
        new[]
        {
            Player, Kind, Entity, CellX, CellY, TargetEntity, DropEntity, Stance,
            Cell2X, Cell2Y, Aux, Queue,
        };

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
        if (values.Length >= 8)
        {
            command.Stance = values[7];
        }
        if (values.Length >= 9)
        {
            command.Cell2X = values[8];
        }
        if (values.Length >= 10)
        {
            command.Cell2Y = values[9];
        }
        if (values.Length >= 11)
        {
            command.Aux = values[10];
        }
        if (values.Length >= 12)
        {
            command.Queue = values[11];
        }
        return command;
    }
}
