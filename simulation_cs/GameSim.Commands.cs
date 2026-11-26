using System.Collections.Generic;

namespace AoeSim;

/// <summary>
/// 指令落地：合法性校验与状态改写。真正的玩法逻辑尽量下沉到既有 System（放置→BuildingSystem、
/// 市场→MarketSystem、训练→生成入口），GameSim 只做锁步入口与转发。
/// </summary>
public sealed partial class GameSim
{
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

    public void IssueMove(long id, long cellX, long cellY)
    {
        if (!World.IsAlive(id) || !World.HasComponent(id, SimComponents.Move))
        {
            return;
        }
        World.Detach(id, SimComponents.AttackTarget);
        World.Detach(id, SimComponents.Engage);
        World.Detach(id, SimComponents.Order);
        World.Detach(id, SimComponents.Repair);
        World.Detach(id, SimComponents.Trade);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    public long IssuePlaceBuilding(long type, int cellX, int cellY, long player)
    {
        if (!BuildingTypes.IsKnown(type))
        {
            return -1;
        }
        EnsureInventoryInitialized();
        if (!_inventoryIndex.Contains(player))
        {
            return -1;
        }
        ResourceCost cost = BuildingTypes.Cost(type);
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            if (Resource(player, resourceKind) < cost.AmountOf(resourceKind))
            {
                return -1;
            }
        }
        long id = BuildingSystem.Place(World, Grid, Terrain, type, cellX, cellY, player);
        if (id < 0)
        {
            return -1;
        }
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            AddResource(player, resourceKind, -cost.AmountOf(resourceKind));
        }
        InvalidateFieldsForBlockedRect(
            cellX, cellY, BuildingTypes.SizeWidth(type), BuildingTypes.SizeHeight(type));
        return id;
    }

    public bool CanTrain(long building, long player, long unitType)
    {
        if (!World.IsAlive(building) || !World.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.Team, SimComponents.TeamId) != player)
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.Building, SimComponents.State) != BuildingSystem.StateComplete)
        {
            return false;
        }
        long buildingType = World.GetValue(building, SimComponents.Building, SimComponents.BuildingType);
        if (unitType == UnitDefs.Villager)
        {
            return buildingType == BuildingTypes.TownCenter;
        }
        if (buildingType != BuildingTypes.Barracks)
        {
            return false;
        }
        return UnitDefs.IsUnitAvailable(Civ(player), unitType);
    }

    public bool TrainUnit(long building, long player, long unitType)
    {
        if (!CanTrain(building, player, unitType))
        {
            return false;
        }
        long popCost = UnitDefs.PopulationCost(unitType);
        if (!PopulationSystem.CanSpawn(World, player, popCost))
        {
            return false;
        }
        ResourceCost cost = UnitDefs.Cost(unitType);
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            if (Resource(player, resourceKind) < cost.AmountOf(resourceKind))
            {
                return false;
            }
        }
        if (!TryFindTrainCell(building, out int cellX, out int cellY))
        {
            return false;
        }
        long unit = unitType == UnitDefs.Villager
            ? SpawnVillager(
                cellX, cellY, player, MapSetup.VillagerSpeed,
                MapSetup.VillagerCarryCapacity, MapSetup.VillagerGatherRate, popCost)
            : SpawnUnit(cellX, cellY, player, unitType, CombatStance.Aggressive, popCost);
        if (unit < 0)
        {
            return false;
        }
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            AddResource(player, resourceKind, -cost.AmountOf(resourceKind));
        }
        ApplyRallyTo(building, unit);
        return true;
    }

    private bool TryFindTrainCell(long building, out int cellX, out int cellY)
    {
        long originX = Fixed.ToInt(World.GetValue(building, SimComponents.Position, SimComponents.X));
        long originY = Fixed.ToInt(World.GetValue(building, SimComponents.Position, SimComponents.Y));
        long width = World.GetValue(building, SimComponents.Building, SimComponents.SizeWidth);
        long height = World.GetValue(building, SimComponents.Building, SimComponents.SizeHeight);
        for (long y = originY - 1; y <= originY + height; y++)
        {
            for (long x = originX - 1; x <= originX + width; x++)
            {
                bool onRing = x < originX || x >= originX + width || y < originY || y >= originY + height;
                if (!onRing)
                {
                    continue;
                }
                int candidateX = (int)x;
                int candidateY = (int)y;
                if (!Terrain.IsLandPassable(candidateX, candidateY) || Grid.IsBlocked(candidateX, candidateY))
                {
                    continue;
                }
                cellX = candidateX;
                cellY = candidateY;
                return true;
            }
        }
        cellX = 0;
        cellY = 0;
        return false;
    }

    /// <summary>
    /// 按段拖拽放置城墙体系：起止两格经 <see cref="WallSegment.Rasterize"/> 展开为整段，
    /// 单回合内原子落位——任一格非法或总造价不足则整段拒绝、不扣费、不落地。
    /// 走现有锁步指令通道（复用 CellX/CellY 存起点、Cell2X/Cell2Y 存终点），保证确定性同步。
    /// </summary>
    public long[] IssuePlaceWallSegment(
        long type, int startX, int startY, int endX, int endY, long player)
    {
        if (!BuildingTypes.IsWallLike(type))
        {
            return System.Array.Empty<long>();
        }
        EnsureInventoryInitialized();
        if (!_inventoryIndex.Contains(player))
        {
            return System.Array.Empty<long>();
        }
        List<(int X, int Y)> cells = WallSegment.Rasterize(startX, startY, endX, endY);
        foreach ((int cellX, int cellY) in cells)
        {
            if (!BuildingSystem.CanPlace(Grid, Terrain, type, cellX, cellY))
            {
                return System.Array.Empty<long>();
            }
        }
        ResourceCost unitCost = BuildingTypes.Cost(type);
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            if (Resource(player, resourceKind) < unitCost.AmountOf(resourceKind) * cells.Count)
            {
                return System.Array.Empty<long>();
            }
        }
        var ids = new long[cells.Count];
        for (int index = 0; index < cells.Count; index++)
        {
            ids[index] = BuildingSystem.Place(
                World, Grid, Terrain, type, cells[index].X, cells[index].Y, player);
        }
        for (long resourceKind = ResourceWood; resourceKind <= ResourceStone; resourceKind++)
        {
            AddResource(player, resourceKind, -unitCost.AmountOf(resourceKind) * cells.Count);
        }
        foreach ((int cellX, int cellY) in cells)
        {
            InvalidateFieldsForBlockedRect(cellX, cellY, 1, 1);
        }
        return ids;
    }

    /// <summary>
    /// 城门开合/锁定：只允许本方已完工城门，targetState 取 0 关 / 1 开 / 2 锁定；
    /// 成功后重建流场，让寻路立刻按新通行性生效。
    /// </summary>
    public bool IssueToggleGate(long gate, long player, long targetState)
    {
        if (!World.IsAlive(gate) || !World.HasComponent(gate, SimComponents.Building))
        {
            return false;
        }
        if (World.GetValue(gate, SimComponents.Team, SimComponents.TeamId) != player)
        {
            return false;
        }
        if (!BuildingSystem.SetGateState(World, Grid, gate, targetState))
        {
            return false;
        }
        if (targetState == BuildingSystem.GateOpen)
        {
            // 开城门是解除阻挡，任何地方都可能因此出现新捷径，只能全表失效。
            InvalidateFields();
        }
        else
        {
            int gateX = (int)Fixed.ToInt(World.GetValue(gate, SimComponents.Position, SimComponents.X));
            int gateY = (int)Fixed.ToInt(World.GetValue(gate, SimComponents.Position, SimComponents.Y));
            int gateWidth = (int)World.GetValue(gate, SimComponents.Building, SimComponents.SizeWidth);
            int gateHeight = (int)World.GetValue(gate, SimComponents.Building, SimComponents.SizeHeight);
            InvalidateFieldsForBlockedRect(gateX, gateY, gateWidth, gateHeight);
        }
        return true;
    }

    /// <summary>空田手动重播：仅本方已完工且耗尽的农田可用，扣木材后重填食物节点。</summary>
    public bool IssueReseedFarm(long building, long player)
    {
        if (!World.IsAlive(building) || !World.HasComponent(building, SimComponents.Building))
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.Building, SimComponents.BuildingType)
            != BuildingTypes.Farm)
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.Building, SimComponents.State)
            != BuildingSystem.StateComplete)
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.Team, SimComponents.TeamId) != player)
        {
            return false;
        }
        if (!World.HasComponent(building, SimComponents.ResourceNode))
        {
            return false;
        }
        if (World.GetValue(building, SimComponents.ResourceNode, SimComponents.Remaining) > 0)
        {
            return false;
        }
        if (Resource(player, ResourceWood) < BuildingTypes.FarmReseedCost)
        {
            return false;
        }
        if (!BuildingSystem.ReseedFarm(World, building))
        {
            return false;
        }
        AddResource(player, ResourceWood, -BuildingTypes.FarmReseedCost);
        return true;
    }

    public void IssueConstruct(long villager, long building)
    {
        if (!World.IsAlive(villager) || !World.HasComponent(villager, SimComponents.Move))
        {
            return;
        }
        if (!World.IsAlive(building) || !World.HasComponent(building, SimComponents.Building))
        {
            return;
        }
        if (World.GetValue(building, SimComponents.Building, SimComponents.State) != BuildingSystem.StateFoundation)
        {
            return;
        }
        if (World.HasComponent(villager, SimComponents.Gather))
        {
            World.SetValue(villager, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateIdle);
            World.SetValue(villager, SimComponents.Gather, SimComponents.NodeEntity, -1);
            World.SetValue(villager, SimComponents.Gather, SimComponents.Progress, 0);
        }
        World.Attach(villager, SimComponents.Construct);
        World.SetValue(villager, SimComponents.Construct, SimComponents.TargetBase, building);
        World.SetValue(villager, SimComponents.Construct, SimComponents.State, BuildingSystem.ConstructActive);
        World.Detach(villager, SimComponents.Repair);
    }

    /// <summary>村民修理受损友方建筑：校验归属与目标状态后挂载 Repair 组件。</summary>
    public bool IssueRepair(long villager, long building, long player)
    {
        if (!World.IsAlive(villager) || !World.HasComponent(villager, SimComponents.Gather))
        {
            return false;
        }
        if (World.GetValue(villager, SimComponents.Team, SimComponents.TeamId) != player)
        {
            return false;
        }
        if (!RepairSystem.CanRepair(World, villager, building))
        {
            return false;
        }
        World.SetValue(villager, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateIdle);
        World.SetValue(villager, SimComponents.Gather, SimComponents.NodeEntity, -1);
        World.SetValue(villager, SimComponents.Gather, SimComponents.Progress, 0);
        World.Detach(villager, SimComponents.Construct);
        World.Attach(villager, SimComponents.Repair);
        World.SetValue(villager, SimComponents.Repair, SimComponents.TargetBase, building);
        World.SetValue(villager, SimComponents.Repair, SimComponents.State, RepairSystem.StateActive);
        return true;
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
        if (drop < 0)
        {
            drop = ResolveNearestDropSite(villager, node);
        }
        World.Detach(villager, SimComponents.Repair);
        World.SetValue(villager, SimComponents.Gather, SimComponents.GatherState, GatherSystem.GatherStateGathering);
        World.SetValue(villager, SimComponents.Gather, SimComponents.NodeEntity, node);
        World.SetValue(villager, SimComponents.Gather, SimComponents.DropEntity, drop);
        World.SetValue(villager, SimComponents.Gather, SimComponents.CarryAmount, 0);
        World.SetValue(villager, SimComponents.Gather, SimComponents.Progress, 0);
    }

    /// <summary>未指定投放点时，按目标资源种类在己方已完工投放建筑中就近选取。</summary>
    private long ResolveNearestDropSite(long villager, long node)
    {
        if (!World.HasComponent(node, SimComponents.ResourceNode))
        {
            return -1;
        }
        long resourceKind = World.GetValue(node, SimComponents.ResourceNode, SimComponents.NodeType);
        long team = World.GetValue(villager, SimComponents.Team, SimComponents.TeamId);
        long x = Fixed.ToInt(World.GetValue(villager, SimComponents.Position, SimComponents.X));
        long y = Fixed.ToInt(World.GetValue(villager, SimComponents.Position, SimComponents.Y));
        return GatherSystem.NearestDropSite(World, team, resourceKind, x, y);
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
        World.Detach(id, SimComponents.Repair);
        World.Detach(id, SimComponents.Trade);
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

    /// <summary>把己方单位收入城镇中心或塔楼，驻军提升建筑攻击。</summary>
    public bool IssueGarrison(long unit, long building) =>
        GarrisonSystem.TryGarrison(World, unit, building, false);

    /// <summary>解除单个单位的驻军，在建筑周边就近落位。</summary>
    public bool IssueUngarrison(long unit) => GarrisonSystem.Release(World, Grid, Terrain, unit);

    /// <summary>把本队陆地单位装上运输船；要求船靠岸/临码头、单位在船边且船未满载。</summary>
    public bool IssueTransportLoad(long unit, long ship) =>
        TransportSystem.TryLoad(World, Grid, Terrain, unit, ship);

    /// <summary>把船上乘员卸载到船体周边的合法陆地格，装不下的留在船上；船未靠岸时拒绝。</summary>
    public int IssueTransportUnload(long ship) =>
        TransportSystem.TryUnload(World, Grid, Terrain, ship);

    /// <summary>城镇警钟：召集本玩家村民进最近的城镇中心；dismiss 为真时解除并恢复其原工作。</summary>
    public int IssueTownBell(long player, bool dismiss)
    {
        if (player < 0 || player >= Lockstep.PlayerCount)
        {
            return 0;
        }
        return dismiss
            ? GarrisonSystem.DismissTownBell(World, Grid, Terrain, player)
            : GarrisonSystem.SummonTownBell(World, Grid, Terrain, player);
    }

    public void IssueSetRally(long building, long cellX, long cellY, long target) =>
        OrderSystem.SetRally(World, building, cellX, cellY, target);

    public bool ApplyRallyTo(long building, long unit) =>
        OrderSystem.ApplyRally(World, building, unit);

    /// <summary>
    /// 设定贸易路线：起点必须是本玩家完工的市场，终点是任意完工市场，两端不可相同。
    /// 车随后由 <see cref="TradeSystem"/> 自动往返，每次抵达按距离与价差产金。
    /// </summary>
    public bool IssueTradeRoute(long cart, long homeMarket, long targetMarket)
    {
        if (!World.IsAlive(cart) || !World.HasComponent(cart, SimComponents.Trade))
        {
            return false;
        }
        if (!TradeSystem.IsCompletedMarket(World, homeMarket)
            || !TradeSystem.IsCompletedMarket(World, targetMarket))
        {
            return false;
        }
        if (homeMarket == targetMarket)
        {
            return false;
        }
        long team = World.GetValue(cart, SimComponents.Team, SimComponents.TeamId);
        if (World.GetValue(homeMarket, SimComponents.Team, SimComponents.TeamId) != team)
        {
            return false;
        }
        World.SetValue(cart, SimComponents.Trade, SimComponents.TradeHome, homeMarket);
        World.SetValue(cart, SimComponents.Trade, SimComponents.TradeTarget, targetMarket);
        return true;
    }

    /// <summary>
    /// 服务器权威校验：指令玩家必须落在本局玩家范围内；作用于实体的指令还要求该实体属于该玩家。
    /// 作用于玩家的指令（投降/时代/科技/放置建筑）只校验玩家本身。
    /// </summary>
    public bool IsCommandAllowed(LockstepCommand command)
    {
        if (command.Player < 0 || command.Player >= Lockstep.PlayerCount)
        {
            return false;
        }
        if (IsPlayerCommand(command.Kind))
        {
            return true;
        }
        if (!IsEntityCommand(command.Kind) || !World.IsAlive(command.Entity))
        {
            return false;
        }
        return World.HasComponent(command.Entity, SimComponents.Team)
            && World.GetValue(command.Entity, SimComponents.Team, SimComponents.TeamId) == command.Player;
    }

    private static bool IsPlayerCommand(long kind) =>
        kind == LockstepCommand.KindSurrender
        || kind == LockstepCommand.KindAgeUp
        || kind == LockstepCommand.KindResearch
        || kind == LockstepCommand.KindCancelResearch
        || kind == LockstepCommand.KindPlaceBuilding
        || kind == LockstepCommand.KindPlaceWallSegment
        || kind == LockstepCommand.KindTownBell
        || kind == LockstepCommand.KindMarketBuy
        || kind == LockstepCommand.KindMarketSell
        || kind == LockstepCommand.KindTribute;

    private static bool IsEntityCommand(long kind) =>
        kind == LockstepCommand.KindMove
        || kind == LockstepCommand.KindAttack
        || kind == LockstepCommand.KindStop
        || kind == LockstepCommand.KindGather
        || kind == LockstepCommand.KindHeal
        || kind == LockstepCommand.KindConvert
        || kind == LockstepCommand.KindPickupRelic
        || kind == LockstepCommand.KindDepositRelic
        || kind == LockstepCommand.KindStance
        || kind == LockstepCommand.KindAttackMove
        || kind == LockstepCommand.KindPatrol
        || kind == LockstepCommand.KindGuard
        || kind == LockstepCommand.KindFollow
        || kind == LockstepCommand.KindHold
        || kind == LockstepCommand.KindSetRally
        || kind == LockstepCommand.KindConstruct
        || kind == LockstepCommand.KindReseedFarm
        || kind == LockstepCommand.KindGarrison
        || kind == LockstepCommand.KindUngarrison
        || kind == LockstepCommand.KindRepair
        || kind == LockstepCommand.KindTradeRoute
        || kind == LockstepCommand.KindToggleGate
        || kind == LockstepCommand.KindTransportLoad
        || kind == LockstepCommand.KindTransportUnload
        || kind == LockstepCommand.KindTrain;

    public bool ApplyCommand(LockstepCommand command)
    {
        if (!IsCommandAllowed(command))
        {
            return false;
        }
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
            case LockstepCommand.KindAgeUp:
                AgeUp(command.Player, command.Aux);
                break;
            case LockstepCommand.KindResearch:
                Research(command.Player, command.Aux);
                break;
            case LockstepCommand.KindCancelResearch:
                CancelResearch(command.Player);
                break;
            case LockstepCommand.KindPlaceBuilding:
                IssuePlaceBuilding(command.Aux, (int)command.CellX, (int)command.CellY, command.Player);
                break;
            case LockstepCommand.KindPlaceWallSegment:
                IssuePlaceWallSegment(
                    command.Aux,
                    (int)command.CellX,
                    (int)command.CellY,
                    (int)command.Cell2X,
                    (int)command.Cell2Y,
                    command.Player);
                break;
            case LockstepCommand.KindConstruct:
                IssueConstruct(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindReseedFarm:
                IssueReseedFarm(command.Entity, command.Player);
                break;
            case LockstepCommand.KindGarrison:
                IssueGarrison(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindUngarrison:
                IssueUngarrison(command.Entity);
                break;
            case LockstepCommand.KindTownBell:
                IssueTownBell(command.Player, command.Aux != 0);
                break;
            case LockstepCommand.KindRepair:
                IssueRepair(command.Entity, command.TargetEntity, command.Player);
                break;
            case LockstepCommand.KindMarketBuy:
                return MarketSystem.Buy(this, command.Player, command.Aux, command.CellX);
            case LockstepCommand.KindMarketSell:
                return MarketSystem.Sell(this, command.Player, command.Aux, command.CellX);
            case LockstepCommand.KindTribute:
                return MarketSystem.Tribute(
                    this, command.Player, command.Aux, command.CellX, command.CellY);
            case LockstepCommand.KindTradeRoute:
                IssueTradeRoute(command.Entity, command.TargetEntity, command.DropEntity);
                break;
            case LockstepCommand.KindToggleGate:
                IssueToggleGate(command.Entity, command.Player, command.Aux);
                break;
            case LockstepCommand.KindTransportLoad:
                IssueTransportLoad(command.Entity, command.TargetEntity);
                break;
            case LockstepCommand.KindTransportUnload:
                IssueTransportUnload(command.Entity);
                break;
            case LockstepCommand.KindTrain:
                TrainUnit(command.Entity, command.Player, command.Aux);
                break;
        }
        return true;
    }
}
