namespace AoeSim;

/// <summary>
/// 锁步指令路由：public 指令方法保持既有签名，只做转发；真正的校验与状态改写落在对应 System。
/// 放置/城墙/城门 → <see cref="BuildingSystem"/>，贸易路线 → <see cref="TradeSystem"/>，
/// 僧侣 → <see cref="MonkSystem"/>，采集 → <see cref="GatherSystem"/>，
/// 移动/攻击/姿态/停止 → <see cref="OrderSystem"/>，训练 → <see cref="ProductionSystem"/>。
/// </summary>
public sealed partial class GameSim
{
    public void SetStance(long id, long stance) => OrderSystem.SetStance(World, id, stance);

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

    public void IssueMove(long id, long cellX, long cellY) =>
        OrderSystem.IssueMove(World, id, cellX, cellY);

    public long IssuePlaceBuilding(long type, int cellX, int cellY, long player) =>
        BuildingSystem.IssuePlace(this, type, cellX, cellY, player);

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
        return UnitCatalog.IsProducible(this, player, buildingType, unitType);
    }

    /// <summary>训练指令入口：改为入队，由 <see cref="ProductionSystem"/> 计时后出生。</summary>
    public bool TrainUnit(long building, long player, long unitType) =>
        CanTrain(building, player, unitType) && ProductionSystem.Train(this, building, unitType);

    /// <summary>在建筑生产队列里排入一个单位；成功后立即扣费。</summary>
    public bool Train(long building, long unitType) => ProductionSystem.Train(this, building, unitType);

    /// <summary>取消训练：slot 0 为进行中项，1..n 为排队位置（1 最前），按原价退款。</summary>
    public bool CancelTraining(long building, long slot) => ProductionSystem.Cancel(this, building, slot);

    public bool SetRepeatProduction(long building, bool repeat) =>
        ProductionSystem.SetRepeat(World, building, repeat);

    public long[] IssuePlaceWallSegment(
        long type, int startX, int startY, int endX, int endY, long player) =>
        BuildingSystem.IssuePlaceWallSegment(this, type, startX, startY, endX, endY, player);

    public bool IssueToggleGate(long gate, long player, long targetState) =>
        BuildingSystem.IssueToggleGate(this, gate, player, targetState);

    public bool IssueReseedFarm(long building, long player) =>
        BuildingSystem.IssueReseedFarm(this, building, player);

    public void IssueConstruct(long villager, long building) =>
        BuildingSystem.IssueConstruct(World, villager, building);

    public bool IssueRepair(long villager, long building, long player) =>
        BuildingSystem.IssueRepair(World, villager, building, player);

    public void IssueAttack(long attacker, long target) =>
        OrderSystem.IssueAttack(World, attacker, target);

    public void IssueGather(long villager, long node, long drop) =>
        GatherSystem.IssueGather(World, villager, node, drop);

    public void IssueHeal(long monk, long target) =>
        MonkSystem.IssueHeal(World, monk, target);

    public void IssueConvert(long monk, long target) =>
        MonkSystem.IssueConvert(World, monk, target);

    public void IssuePickupRelic(long monk, long relic) =>
        MonkSystem.IssuePickupRelic(World, monk, relic);

    public void IssueDepositRelic(long monk, long slot) =>
        MonkSystem.IssueDepositRelic(World, monk, slot);

    public void Stop(long id) => OrderSystem.IssueStop(World, id);

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

    /// <summary>设定贸易路线，随后由 <see cref="TradeSystem"/> 自动往返产金。</summary>
    public bool IssueTradeRoute(long cart, long homeMarket, long targetMarket) =>
        TradeSystem.IssueRoute(World, cart, homeMarket, targetMarket);

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
        || kind == LockstepCommand.KindTrain
        || kind == LockstepCommand.KindCancelTrain;

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
            case LockstepCommand.KindCancelTrain:
                CancelTraining(command.Entity, command.CellX);
                break;
        }
        return true;
    }
}
