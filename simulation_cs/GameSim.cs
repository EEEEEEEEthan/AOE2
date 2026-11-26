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
    public OccupancyMap Occupancy { get; }
    public Lockstep Lockstep { get; }
    public MatchState? Match { get; }

    private readonly Dictionary<long, FlowField> _fields = new();
    private readonly Dictionary<long, long> _inventoryEntities = new();

    public GameSim(NavGrid grid, long seed = 1, int playerCount = 2, int ticksPerTurn = 6, MatchConfig? matchConfig = null)
    {
        Grid = grid;
        World = new SimWorld(seed);
        Spatial = new SpatialHash(4);
        Occupancy = new OccupancyMap(grid.Width, grid.Height);
        Lockstep = new Lockstep(playerCount, ticksPerTurn);
        if (matchConfig != null)
        {
            Match = MatchState.Create(World, Lockstep.PlayerCount, matchConfig);
        }
    }

    /// <summary>射程按格子配置：rangeMinTiles/rangeMaxTiles 只在生成边界转成格数，相邻格即为近战。</summary>
    public long SpawnUnit(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        long damage,
        double rangeMinTiles,
        double rangeMaxTiles,
        double speedTilesPerTick,
        long stance = CombatStance.Aggressive)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Velocity);
        World.Attach(id, SimComponents.Move);
        World.Attach(id, SimComponents.Health);
        World.Attach(id, SimComponents.Combat);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Footprint);
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
        long rangeMin = ToRangeCells(rangeMinTiles);
        long rangeMax = ToRangeCells(rangeMaxTiles);
        if (rangeMax < rangeMin)
        {
            rangeMax = rangeMin;
        }
        World.SetValue(id, SimComponents.Combat, SimComponents.Damage, damage);
        World.SetValue(id, SimComponents.Combat, SimComponents.RangeMin, rangeMin);
        World.SetValue(id, SimComponents.Combat, SimComponents.RangeMax, rangeMax);
        World.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, 6);
        World.SetValue(id, SimComponents.Combat, SimComponents.CooldownLeft, 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.Stance, stance);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        return id;
    }

    private static long ToRangeCells(double tiles) =>
        (long)System.Math.Round(tiles, System.MidpointRounding.AwayFromZero);

    public void SetStance(long id, long stance)
    {
        if (!World.IsAlive(id) || !World.HasComponent(id, SimComponents.Combat))
        {
            return;
        }
        if (!CombatStance.IsValid(stance))
        {
            return;
        }
        World.SetValue(id, SimComponents.Combat, SimComponents.Stance, stance);
        World.Detach(id, SimComponents.Order);
        if (World.HasComponent(id, SimComponents.Engage))
        {
            World.Detach(id, SimComponents.Engage);
            World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
            World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
            World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
        }
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
        World.Attach(id, SimComponents.Footprint);
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
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        return id;
    }

    public long SpawnKing(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        long damage,
        double rangeMinTiles,
        double rangeMaxTiles,
        double speedTilesPerTick)
    {
        long id = SpawnUnit(cellX, cellY, team, maxHp, damage, rangeMinTiles, rangeMaxTiles, speedTilesPerTick);
        World.Attach(id, SimComponents.King);
        if (Match != null && team >= 0 && team < Match.PlayerCount)
        {
            Match.Player(team).HadKing = true;
        }
        return id;
    }

    public long SpawnBuilding(int cellX, int cellY, long team, long maxHp)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Health);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Building);
        World.Attach(id, SimComponents.Footprint);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Health, SimComponents.Hp, maxHp);
        World.SetValue(id, SimComponents.Health, SimComponents.MaxHp, maxHp);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        return id;
    }

    public long SpawnWonder(int cellX, int cellY, long team, long maxHp)
    {
        long id = SpawnBuilding(cellX, cellY, team, maxHp);
        World.Attach(id, SimComponents.Wonder);
        World.SetValue(id, SimComponents.Wonder, SimComponents.Complete, 0);
        return id;
    }

    public void CompleteWonder(long id)
    {
        if (World.IsAlive(id) && World.HasComponent(id, SimComponents.Wonder))
        {
            World.SetValue(id, SimComponents.Wonder, SimComponents.Complete, 1);
        }
    }

    public long SpawnRelic(int cellX, int cellY)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Relic);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Relic, SimComponents.HolderTeam, -1);
        World.SetValue(id, SimComponents.Relic, SimComponents.Carrier, -1);
        World.SetValue(id, SimComponents.Relic, SimComponents.StoredIn, -1);
        World.SetValue(id, SimComponents.Relic, SimComponents.SpawnX, Fixed.FromInt(cellX));
        World.SetValue(id, SimComponents.Relic, SimComponents.SpawnY, Fixed.FromInt(cellY));
        return id;
    }

    public long SpawnMonk(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        double speedTilesPerTick,
        long healRate,
        long convertRate,
        long convertRequired,
        double rangeTiles,
        long conversionResistance)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Velocity);
        World.Attach(id, SimComponents.Move);
        World.Attach(id, SimComponents.Health);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Monk);
        World.Attach(id, SimComponents.Footprint);
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
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        long rangeFixed = Fixed.FromFloat(rangeTiles);
        World.SetValue(id, SimComponents.Monk, SimComponents.State, MonkSystem.StateIdle);
        World.SetValue(id, SimComponents.Monk, SimComponents.TargetEntity, -1);
        World.SetValue(id, SimComponents.Monk, SimComponents.ConvertProgress, 0);
        World.SetValue(id, SimComponents.Monk, SimComponents.ConvertRate, convertRate);
        World.SetValue(id, SimComponents.Monk, SimComponents.ConvertRequired, convertRequired);
        World.SetValue(id, SimComponents.Monk, SimComponents.HealRate, healRate);
        World.SetValue(id, SimComponents.Monk, SimComponents.ConversionResistance, conversionResistance);
        World.SetValue(id, SimComponents.Monk, SimComponents.RangeSq, Fixed.Mul(rangeFixed, rangeFixed));
        World.SetValue(id, SimComponents.Monk, SimComponents.LastHp, maxHp);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        return id;
    }

    public long SpawnRelicSlot(int cellX, int cellY, long team, long goldRate, long goldPeriod)
    {
        EnsureInventoryInitialized();
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.RelicSlot);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.RelicSlot, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.RelicSlot, SimComponents.GoldRate, goldRate);
        World.SetValue(id, SimComponents.RelicSlot, SimComponents.GoldPeriod, goldPeriod);
        World.SetValue(id, SimComponents.RelicSlot, SimComponents.GoldProgress, 0);
        return id;
    }

    public void SetRelicHolder(long id, long team)
    {
        if (World.IsAlive(id) && World.HasComponent(id, SimComponents.Relic))
        {
            World.SetValue(id, SimComponents.Relic, SimComponents.HolderTeam, team);
        }
    }

    /// <summary>某方当前持有的圣物数量（地面未归属的圣物不计）。供圣物胜利条件调用。</summary>
    public long RelicCountOf(long team)
    {
        ComponentStore relicStore = World.Store(SimComponents.Relic);
        long count = 0;
        foreach (long id in World.Entities())
        {
            if (!relicStore.Has(id) || !World.IsAlive(id))
            {
                continue;
            }
            if (World.GetValue(id, SimComponents.Relic, SimComponents.HolderTeam) == team)
            {
                count += 1;
            }
        }
        return count;
    }

    public void Surrender(long team)
    {
        if (Match == null || Match.Ended || team < 0 || team >= Match.PlayerCount)
        {
            return;
        }
        PlayerState player = Match.Player(team);
        if (player.Defeated)
        {
            return;
        }
        player.Defeated = true;
        player.Surrendered = true;
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
        World.Detach(id, SimComponents.AttackTarget);
        World.Detach(id, SimComponents.Engage);
        World.Detach(id, SimComponents.Order);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    public void IssueAttack(long attacker, long target)
    {
        if (!World.IsAlive(attacker) || !World.HasComponent(attacker, SimComponents.Combat))
        {
            return;
        }
        if (!World.IsAlive(target) || !World.HasComponent(target, SimComponents.Health))
        {
            return;
        }
        if (World.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            == World.GetValue(attacker, SimComponents.Team, SimComponents.TeamId))
        {
            return;
        }
        if (World.HasComponent(attacker, SimComponents.Move))
        {
            World.SetValue(attacker, SimComponents.Move, SimComponents.Active, 0);
            World.SetValue(attacker, SimComponents.Velocity, SimComponents.X, 0);
            World.SetValue(attacker, SimComponents.Velocity, SimComponents.Y, 0);
        }
        World.Detach(attacker, SimComponents.Engage);
        World.Detach(attacker, SimComponents.Order);
        World.Attach(attacker, SimComponents.AttackTarget);
        World.SetValue(attacker, SimComponents.AttackTarget, SimComponents.TargetEntity, target);
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

    public void IssueHeal(long monk, long target)
    {
        if (!World.IsAlive(monk) || !World.HasComponent(monk, SimComponents.Monk))
        {
            return;
        }
        if (monk == target || !World.IsAlive(target) || !World.HasComponent(target, SimComponents.Health))
        {
            return;
        }
        if (World.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            != World.GetValue(monk, SimComponents.Team, SimComponents.TeamId))
        {
            return;
        }
        SetMonkTarget(monk, MonkSystem.StateHealing, target);
    }

    public void IssueConvert(long monk, long target)
    {
        if (!World.IsAlive(monk) || !World.HasComponent(monk, SimComponents.Monk))
        {
            return;
        }
        if (monk == target || !World.IsAlive(target) || !World.HasComponent(target, SimComponents.Health))
        {
            return;
        }
        if (World.GetValue(target, SimComponents.Team, SimComponents.TeamId)
            == World.GetValue(monk, SimComponents.Team, SimComponents.TeamId))
        {
            return;
        }
        SetMonkTarget(monk, MonkSystem.StateConverting, target);
    }

    public void IssuePickupRelic(long monk, long relic)
    {
        if (!World.IsAlive(monk) || !World.HasComponent(monk, SimComponents.Monk))
        {
            return;
        }
        if (!World.IsAlive(relic) || !World.HasComponent(relic, SimComponents.Relic))
        {
            return;
        }
        if (World.GetValue(relic, SimComponents.Relic, SimComponents.Carrier) >= 0
            || World.GetValue(relic, SimComponents.Relic, SimComponents.StoredIn) >= 0)
        {
            return;
        }
        SetMonkTarget(monk, MonkSystem.StateSeekRelic, relic);
    }

    public void IssueDepositRelic(long monk, long slot)
    {
        if (!World.IsAlive(monk) || !World.HasComponent(monk, SimComponents.Monk))
        {
            return;
        }
        if (!World.IsAlive(slot) || !World.HasComponent(slot, SimComponents.RelicSlot))
        {
            return;
        }
        if (MonkSystem.CarriedRelic(World, monk) < 0)
        {
            return;
        }
        SetMonkTarget(monk, MonkSystem.StateSeekDeposit, slot);
    }

    private void SetMonkTarget(long monk, long state, long target)
    {
        if (World.HasComponent(monk, SimComponents.Move))
        {
            World.SetValue(monk, SimComponents.Move, SimComponents.Active, 0);
            World.SetValue(monk, SimComponents.Velocity, SimComponents.X, 0);
            World.SetValue(monk, SimComponents.Velocity, SimComponents.Y, 0);
        }
        World.SetValue(monk, SimComponents.Monk, SimComponents.State, state);
        World.SetValue(monk, SimComponents.Monk, SimComponents.TargetEntity, target);
        World.SetValue(monk, SimComponents.Monk, SimComponents.ConvertProgress, 0);
    }

    public void Stop(long id)
    {
        if (!World.IsAlive(id))
        {
            return;
        }
        World.Detach(id, SimComponents.AttackTarget);
        World.Detach(id, SimComponents.Engage);
        World.Detach(id, SimComponents.Order);
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
    }

    public void IssueAttackMove(long id, long cellX, long cellY) =>
        OrderSystem.IssueAttackMove(World, id, cellX, cellY, 0, false);

    public void IssuePatrol(long id, long firstX, long firstY, long secondX, long secondY) =>
        OrderSystem.IssuePatrol(World, id, firstX, firstY, secondX, secondY, false);

    public void IssueGuard(long id, long target) =>
        OrderSystem.IssueGuard(World, id, target, 0, false);

    public void IssueFollow(long id, long target) =>
        OrderSystem.IssueFollow(World, id, target, 0, false);

    public void IssueHold(long id) => OrderSystem.IssueHold(World, id, false);

    public void IssueSetRally(long building, long cellX, long cellY, long target) =>
        OrderSystem.SetRally(World, building, cellX, cellY, target);

    public bool ApplyRallyTo(long building, long unit) =>
        OrderSystem.ApplyRally(World, building, unit);

    public void ApplyCommand(LockstepCommand command)
    {
        switch (command.Kind)
        {
            case LockstepCommand.KindMove:
                IssueMove(command.Entity, command.CellX, command.CellY);
                break;
            case LockstepCommand.KindAttack:
                IssueAttack(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindStop:
                Stop(command.Entity);
                break;
            case LockstepCommand.KindGather:
                IssueGather(command.Entity, command.TargetEntity, command.DropEntity);
                break;
            case LockstepCommand.KindSurrender:
                Surrender(command.Player);
                break;
            case LockstepCommand.KindHeal:
                IssueHeal(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindConvert:
                IssueConvert(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindPickupRelic:
                IssuePickupRelic(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindDepositRelic:
                IssueDepositRelic(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindStance:
                SetStance(command.Entity, command.Stance);
                break;
            case LockstepCommand.KindAttackMove:
                OrderSystem.IssueAttackMove(
                    World, command.Entity, command.CellX, command.CellY, command.Aux, command.Queue != 0);
                break;
            case LockstepCommand.KindPatrol:
                OrderSystem.IssuePatrol(
                    World, command.Entity, command.CellX, command.CellY,
                    command.Cell2X, command.Cell2Y, command.Queue != 0);
                break;
            case LockstepCommand.KindGuard:
                OrderSystem.IssueGuard(
                    World, command.Entity, command.TargetEntity, command.Aux, command.Queue != 0);
                break;
            case LockstepCommand.KindFollow:
                OrderSystem.IssueFollow(
                    World, command.Entity, command.TargetEntity, command.Aux, command.Queue != 0);
                break;
            case LockstepCommand.KindHold:
                OrderSystem.IssueHold(World, command.Entity, command.Queue != 0);
                break;
            case LockstepCommand.KindSetRally:
                OrderSystem.SetRally(World, command.Entity, command.CellX, command.CellY, command.TargetEntity);
                break;
        }
    }

    public void SimulateTick()
    {
        if (Match != null && Match.Ended)
        {
            return;
        }
        GatherSystem.Step(World, InventoryForTeam);
        MonkSystem.Step(World);
        OrderSystem.Step(World, Spatial);
        MovementSystem.Step(World, Grid, Occupancy, FieldProvider);
        ProjectileSystem.Step(World);
        CombatSystem.Step(World, Spatial);
        RelicSystem.Step(World, InventoryForTeam);
        if (Match != null)
        {
            MatchSystem.Step(World, Match);
        }
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

    private long InventoryForTeam(long team) =>
        _inventoryEntities.TryGetValue(team, out long inventory) ? inventory : -1;

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
