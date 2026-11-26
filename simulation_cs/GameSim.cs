using System.Collections.Generic;

namespace AoeSim;

public sealed class GameSim
{
    public const long ResourceWood = 0;
    public const long ResourceFood = 1;
    public const long ResourceGold = 2;
    public const long ResourceStone = 3;

    public SimWorld World { get; }
    public NavGrid Grid { get; }
    public SpatialHash Spatial { get; }
    public Lockstep Lockstep { get; }

    private readonly Dictionary<long, FlowField> _fields = new();
    private readonly Dictionary<long, long> _inventoryEntities = new();

    public GameSim(NavGrid grid, long seed = 1, int playerCount = 2, int ticksPerTurn = 6)
    {
        Grid = grid;
        World = new SimWorld(seed);
        Spatial = new SpatialHash(4);
        Lockstep = new Lockstep(playerCount, ticksPerTurn);
    }

    public long SpawnUnit(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        long damage,
        double rangeTiles,
        double speedTilesPerTick)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Velocity);
        World.Attach(id, SimComponents.Move);
        World.Attach(id, SimComponents.Health);
        World.Attach(id, SimComponents.Combat);
        World.Attach(id, SimComponents.Team);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Speed, Fixed.FromFloat(speedTilesPerTick));
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        World.SetValue(id, SimComponents.Health, SimComponents.Hp, maxHp);
        World.SetValue(id, SimComponents.Health, SimComponents.MaxHp, maxHp);
        long rangeFixed = Fixed.FromFloat(rangeTiles);
        World.SetValue(id, SimComponents.Combat, SimComponents.Damage, damage);
        World.SetValue(id, SimComponents.Combat, SimComponents.RangeSq, Fixed.Mul(rangeFixed, rangeFixed));
        World.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, 6);
        World.SetValue(id, SimComponents.Combat, SimComponents.CooldownLeft, 0);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        return id;
    }

    public long SpawnResourceNode(int cellX, int cellY, long nodeType, long amount)
    {
        EnsureInventoryInitialized();
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.ResourceNode);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.ResourceNode, SimComponents.NodeType, nodeType);
        World.SetValue(id, SimComponents.ResourceNode, SimComponents.Remaining, amount);
        return id;
    }

    public long SpawnDropSite(int cellX, int cellY, long acceptMask)
    {
        EnsureInventoryInitialized();
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.DropSite);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.DropSite, SimComponents.AcceptMask, acceptMask);
        return id;
    }

    public long SpawnVillager(
        int cellX,
        int cellY,
        long team,
        double speedTilesPerTick,
        long carryCapacity,
        double gatherRatePerTick)
    {
        EnsureInventoryInitialized();
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Velocity);
        World.Attach(id, SimComponents.Move);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Gather);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Speed, Fixed.FromFloat(speedTilesPerTick));
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateIdle);
        World.SetValue(id, SimComponents.Gather, SimComponents.NodeEntity, -1);
        World.SetValue(id, SimComponents.Gather, SimComponents.DropEntity, -1);
        World.SetValue(id, SimComponents.Gather, SimComponents.Rate, Fixed.FromFloat(gatherRatePerTick));
        World.SetValue(id, SimComponents.Gather, SimComponents.CarryType, 0);
        World.SetValue(id, SimComponents.Gather, SimComponents.CarryAmount, 0);
        World.SetValue(id, SimComponents.Gather, SimComponents.CarryCapacity, carryCapacity);
        World.SetValue(id, SimComponents.Gather, SimComponents.Progress, 0);
        return id;
    }

    public long Resource(long player, long resourceKind)
    {
        EnsureInventoryInitialized();
        return World.GetValue(
            _inventoryEntities[player],
            SimComponents.Resource,
            SimComponents.ResourceColumns[(int)resourceKind]);
    }

    public void AddResource(long player, long resourceKind, long amount)
    {
        EnsureInventoryInitialized();
        long inventory = _inventoryEntities[player];
        string column = SimComponents.ResourceColumns[(int)resourceKind];
        World.SetValue(
            inventory,
            SimComponents.Resource,
            column,
            World.GetValue(inventory, SimComponents.Resource, column) + amount);
    }

    public void IssueMove(long id, long cellX, long cellY)
    {
        if (!World.IsAlive(id) || !World.HasComponent(id, SimComponents.Move))
        {
            return;
        }
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    public void IssueGather(long villager, long node, long drop)
    {
        if (!World.IsAlive(villager) || !World.HasComponent(villager, SimComponents.Gather))
        {
            return;
        }
        World.SetValue(villager, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateGathering);
        World.SetValue(villager, SimComponents.Gather, SimComponents.NodeEntity, node);
        World.SetValue(villager, SimComponents.Gather, SimComponents.DropEntity, drop);
        World.SetValue(villager, SimComponents.Gather, SimComponents.CarryAmount, 0);
        World.SetValue(villager, SimComponents.Gather, SimComponents.Progress, 0);
    }

    public void Stop(long id)
    {
        if (!World.IsAlive(id))
        {
            return;
        }
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }

    public void ApplyCommand(LockstepCommand command)
    {
        switch (command.Kind)
        {
            case LockstepCommand.KindMove:
                IssueMove(command.Entity, command.CellX, command.CellY);
                break;
            case LockstepCommand.KindStop:
                Stop(command.Entity);
                break;
            case LockstepCommand.KindGather:
                IssueGather(command.Entity, command.TargetEntity, command.DropEntity);
                break;
        }
    }

    public void SimulateTick()
    {
        GatherSystem.Step(World, InventoryForTeam);
        MovementSystem.Step(World, Grid, FieldProvider);
        CombatSystem.Step(World, Spatial);
        World.Tick += 1;
    }

    public long SimulateTurn()
    {
        if (Lockstep.CanAdvance())
        {
            foreach (LockstepCommand command in Lockstep.TakeCommands())
            {
                ApplyCommand(command);
            }
        }
        for (int step = 0; step < Lockstep.TicksPerTurn; step++)
        {
            SimulateTick();
        }
        return World.Checksum();
    }

    public bool SubmitTurn(long player, List<LockstepCommand> commands) =>
        Lockstep.SubmitTurn(Lockstep.Turn, player, commands);

    public FlowField FieldFor(int targetX, int targetY) => FieldProvider(targetX, targetY);

    public void InvalidateFields() => _fields.Clear();

    private void EnsureInventoryInitialized()
    {
        if (_inventoryEntities.Count > 0)
        {
            return;
        }
        for (int player = 0; player < Lockstep.PlayerCount; player++)
        {
            long id = World.Spawn();
            World.Attach(id, SimComponents.Resource);
            World.Attach(id, SimComponents.Team);
            World.SetValue(id, SimComponents.Team, SimComponents.TeamId, player);
            foreach (string column in SimComponents.ResourceColumns)
            {
                World.SetValue(id, SimComponents.Resource, column, 0);
            }
            _inventoryEntities[player] = id;
        }
    }

    private long InventoryForTeam(long team) => _inventoryEntities[team];

    private FlowField FieldProvider(int targetX, int targetY)
    {
        long key = ((long)targetX << 32) ^ (targetY & 0xFFFF_FFFFL);
        if (_fields.TryGetValue(key, out FlowField? field))
        {
            return field;
        }
        field = new FlowField(Grid);
        field.Compute(Grid, targetX, targetY);
        _fields[key] = field;
        return field;
    }
}
