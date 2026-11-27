using System;
using System.Collections.Generic;

namespace AoeSim;

/// <summary>电脑难度：只由整数参数区分，不引入浮点。</summary>
public enum AiDifficulty
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
}

/// <summary>电脑玩家可落盘状态：随机流、决策回合与进攻目标等 ECS 之外的控制器字段。</summary>
public sealed class ComputerPlayerState
{
    public long Team;
    public int Difficulty;
    public long RngState;
    public long LastDecisionTurn;
    public int MilitaryTrained;
    public bool Attacking;
    public int TargetX = -1;
    public int TargetY = -1;
}

/// <summary>
/// 服务器权威的电脑玩家：每个决策回合读取模拟状态（敌方目标须本队视野可见），
/// 产出交给 <see cref="SimulationServer"/> 做归属校验后进入锁步队列的指令。
/// 随机流由种子与队伍派生、决策间隔按整数回合，保证同种子决策确定。
/// </summary>
public sealed class ComputerPlayer
{
    private sealed class Profile
    {
        public int DecisionTurns;
        public int TargetVillagers;
        public int AttackThreshold;
        public int FirstAttackTurn;
        public int MaxMilitary;
        public int HouseGap;
        public int MilitaryQueueRoom;
    }

    private const int DefenseRadiusCells = 12;
    private const int MaxHouses = 14;

    private static readonly Profile[] Profiles =
    {
        new()
        {
            DecisionTurns = 12, TargetVillagers = 7, AttackThreshold = 6,
            FirstAttackTurn = 60, MaxMilitary = 14, HouseGap = 2, MilitaryQueueRoom = 1,
        },
        new()
        {
            DecisionTurns = 8, TargetVillagers = 12, AttackThreshold = 4,
            FirstAttackTurn = 30, MaxMilitary = 30, HouseGap = 2, MilitaryQueueRoom = 2,
        },
        new()
        {
            DecisionTurns = 5, TargetVillagers = 18, AttackThreshold = 3,
            FirstAttackTurn = 18, MaxMilitary = 50, HouseGap = 3, MilitaryQueueRoom = 2,
        },
    };

    private static readonly long[] GatherMix =
    {
        GameSim.ResourceWood, GameSim.ResourceWood, GameSim.ResourceFood,
        GameSim.ResourceWood, GameSim.ResourceGold, GameSim.ResourceFood,
        GameSim.ResourceWood, GameSim.ResourceStone,
    };

    private static readonly long[] MilitaryMix =
    {
        UnitDefs.Swordsman, UnitDefs.Archer, UnitDefs.Spearman,
    };

    private sealed class WorldScan
    {
        public readonly List<long> Villagers = new();
        public readonly List<long> Military = new();
        public readonly List<long> Barracks = new();
        public readonly List<long> Foundations = new();
        public long TownCenter = -1;
        public int TownCenterX;
        public int TownCenterY;
        public int HouseCount;
        public int HouseFoundationCount;
    }

    public GameSim Sim { get; }
    public long Team { get; }
    public AiDifficulty Difficulty { get; }

    private readonly Profile _profile;
    private readonly SimRng _rng;
    private readonly int[] _enemyStartX;
    private readonly int[] _enemyStartY;
    private long _lastDecisionTurn = long.MinValue / 2;
    private int _militaryTrained;
    private bool _attacking;
    private int _targetX = -1;
    private int _targetY = -1;

    public ComputerPlayer(
        GameSim sim,
        long team,
        AiDifficulty difficulty = AiDifficulty.Normal,
        IReadOnlyList<PlayerStart>? starts = null,
        long seed = 1)
    {
        Sim = sim;
        Team = team;
        int difficultyIndex = (int)difficulty;
        Difficulty = difficultyIndex >= 0 && difficultyIndex < Profiles.Length
            ? difficulty
            : AiDifficulty.Normal;
        _profile = Profiles[(int)Difficulty];
        _rng = new SimRng(seed ^ ((team + 1) * 0x9E37_79B9L));

        var enemyStartX = new List<int>();
        var enemyStartY = new List<int>();
        if (starts != null)
        {
            int centerOffsetX = BuildingTypes.SizeWidth(BuildingTypes.TownCenter) / 2;
            int centerOffsetY = BuildingTypes.SizeHeight(BuildingTypes.TownCenter) / 2;
            foreach (PlayerStart start in starts)
            {
                if (start.Team == team)
                {
                    continue;
                }
                enemyStartX.Add(start.X + centerOffsetX);
                enemyStartY.Add(start.Y + centerOffsetY);
            }
        }
        _enemyStartX = enemyStartX.ToArray();
        _enemyStartY = enemyStartY.ToArray();
    }

    /// <summary>导出控制器状态，供对局存档保存随机流与决策进度。</summary>
    public ComputerPlayerState CaptureState() => new()
    {
        Team = Team,
        Difficulty = (int)Difficulty,
        RngState = _rng.State,
        LastDecisionTurn = _lastDecisionTurn,
        MilitaryTrained = _militaryTrained,
        Attacking = _attacking,
        TargetX = _targetX,
        TargetY = _targetY,
    };

    /// <summary>读档时把控制器状态复位到存档时点，避免 AI 重放已做过的决策。</summary>
    public void RestoreState(ComputerPlayerState state)
    {
        if (state.Team != Team || state.Difficulty != (int)Difficulty)
        {
            return;
        }
        _rng.State = state.RngState;
        _lastDecisionTurn = state.LastDecisionTurn;
        _militaryTrained = state.MilitaryTrained;
        _attacking = state.Attacking;
        _targetX = state.TargetX;
        _targetY = state.TargetY;
    }

    /// <summary>本决策回合要提交的指令；空列表表示本回合不行动。</summary>
    public List<LockstepCommand> Think(long turn)
    {
        var commands = new List<LockstepCommand>();
        if (_lastDecisionTurn + _profile.DecisionTurns > turn)
        {
            return commands;
        }
        _lastDecisionTurn = turn;
        WorldScan scan = ScanWorld();
        PlanEconomy(commands, scan);
        PlanConstruction(commands, scan);
        PlanGather(commands, scan);
        PlanMilitaryProduction(commands, scan);
        PlanRally(commands, scan);
        PlanAttack(commands, scan, turn);
        return commands;
    }

    private WorldScan ScanWorld()
    {
        var scan = new WorldScan();
        SimWorld world = Sim.World;
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Team))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.Team, SimComponents.TeamId) != Team)
            {
                continue;
            }
            if (world.HasComponent(id, SimComponents.Building))
            {
                ScanBuilding(scan, id);
                continue;
            }
            if (world.HasComponent(id, SimComponents.Gather))
            {
                scan.Villagers.Add(id);
            }
            else if (world.HasComponent(id, SimComponents.Combat))
            {
                scan.Military.Add(id);
            }
        }
        if (scan.TownCenter < 0)
        {
            UseFirstFoundationAsAnchor(scan);
        }
        return scan;
    }

    private void ScanBuilding(WorldScan scan, long id)
    {
        SimWorld world = Sim.World;
        long state = world.GetValue(id, SimComponents.Building, SimComponents.State);
        if (state == BuildingSystem.StateRuins)
        {
            return;
        }
        long type = world.GetValue(id, SimComponents.Building, SimComponents.BuildingType);
        if (type == BuildingTypes.TownCenter && state == BuildingSystem.StateComplete
            && scan.TownCenter < 0)
        {
            scan.TownCenter = id;
            CellOf(id, out scan.TownCenterX, out scan.TownCenterY);
        }
        if (type == BuildingTypes.Barracks)
        {
            scan.Barracks.Add(id);
        }
        if (type == BuildingTypes.House)
        {
            scan.HouseCount += 1;
            if (state == BuildingSystem.StateFoundation)
            {
                scan.HouseFoundationCount += 1;
            }
        }
        if (state == BuildingSystem.StateFoundation)
        {
            scan.Foundations.Add(id);
        }
    }

    private void UseFirstFoundationAsAnchor(WorldScan scan)
    {
        foreach (long foundation in scan.Foundations)
        {
            long type = Sim.World.GetValue(
                foundation, SimComponents.Building, SimComponents.BuildingType);
            if (type != BuildingTypes.TownCenter)
            {
                continue;
            }
            scan.TownCenter = foundation;
            CellOf(foundation, out scan.TownCenterX, out scan.TownCenterY);
            return;
        }
    }

    private void PlanEconomy(List<LockstepCommand> commands, WorldScan scan)
    {
        if (scan.TownCenter < 0)
        {
            return;
        }
        SimWorld world = Sim.World;
        bool canTrainVillager = scan.Villagers.Count < _profile.TargetVillagers
            && Sim.Resource(Team, GameSim.ResourceFood)
                >= UnitDefs.Cost(UnitDefs.Villager).AmountOf(GameSim.ResourceFood)
            && Sim.CanSpawnPopulation(Team, 1)
            && QueuedTotal(world, scan.TownCenter) < 1;
        if (canTrainVillager)
        {
            commands.Add(TrainCommand(scan.TownCenter, UnitDefs.Villager));
        }

        long wood = Sim.Resource(Team, GameSim.ResourceWood);
        if (scan.Barracks.Count == 0)
        {
            if (wood >= BuildingTypes.Cost(BuildingTypes.Barracks).Wood
                && TryPlace(commands, BuildingTypes.Barracks, scan))
            {
                return;
            }
        }
        bool needsHouse = scan.HouseFoundationCount == 0
            && scan.HouseCount < MaxHouses
            && Sim.PopulationCap(Team) - Sim.PopulationUsed(Team) <= _profile.HouseGap;
        if (needsHouse && wood >= BuildingTypes.Cost(BuildingTypes.House).Wood)
        {
            TryPlace(commands, BuildingTypes.House, scan);
        }
    }

    private bool TryPlace(List<LockstepCommand> commands, long type, WorldScan scan)
    {
        if (!TryFindPlacement(type, scan.TownCenterX, scan.TownCenterY, out int cellX, out int cellY))
        {
            return false;
        }
        commands.Add(new LockstepCommand(
            Team, LockstepCommand.KindPlaceBuilding, cellX: cellX, cellY: cellY, aux: type));
        return true;
    }

    private bool TryFindPlacement(long type, int centerX, int centerY, out int cellX, out int cellY)
    {
        for (int radius = 2; radius <= 16; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) != radius)
                    {
                        continue;
                    }
                    int candidateX = centerX + offsetX;
                    int candidateY = centerY + offsetY;
                    if (BuildingSystem.CanPlace(Sim.Grid, Sim.Terrain, type, candidateX, candidateY)
                        && !FootprintHasUnit(type, candidateX, candidateY))
                    {
                        cellX = candidateX;
                        cellY = candidateY;
                        return true;
                    }
                }
            }
        }
        cellX = 0;
        cellY = 0;
        return false;
    }

    private bool FootprintHasUnit(long type, int originX, int originY)
    {
        int sizeWidth = BuildingTypes.SizeWidth(type);
        int sizeHeight = BuildingTypes.SizeHeight(type);
        SimWorld world = Sim.World;
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || world.HasComponent(id, SimComponents.Building)
                || !world.HasComponent(id, SimComponents.Position))
            {
                continue;
            }
            CellOf(id, out int cellX, out int cellY);
            if (cellX >= originX && cellX < originX + sizeWidth
                && cellY >= originY && cellY < originY + sizeHeight)
            {
                return true;
            }
        }
        return false;
    }

    private void PlanConstruction(List<LockstepCommand> commands, WorldScan scan)
    {
        if (scan.Foundations.Count == 0)
        {
            return;
        }
        SimWorld world = Sim.World;
        var busyBuilders = new HashSet<long>();
        foreach (long villager in scan.Villagers)
        {
            if (!world.HasComponent(villager, SimComponents.Construct))
            {
                continue;
            }
            busyBuilders.Add(world.GetValue(
                villager, SimComponents.Construct, SimComponents.TargetBase));
        }
        var assignedVillagers = new HashSet<long>();
        foreach (long foundation in scan.Foundations)
        {
            if (busyBuilders.Contains(foundation))
            {
                continue;
            }
            long builder = NearestAvailableVillager(scan.Villagers, assignedVillagers, foundation);
            if (builder < 0)
            {
                continue;
            }
            commands.Add(new LockstepCommand(
                Team, LockstepCommand.KindConstruct, entity: builder, targetEntity: foundation));
            assignedVillagers.Add(builder);
            busyBuilders.Add(foundation);
        }
    }

    private long NearestAvailableVillager(
        List<long> villagers, HashSet<long> assignedVillagers, long foundation)
    {
        SimWorld world = Sim.World;
        CellOf(foundation, out int targetX, out int targetY);
        long bestVillager = -1;
        long bestDistance = long.MaxValue;
        foreach (long villager in villagers)
        {
            if (assignedVillagers.Contains(villager)
                || world.HasComponent(villager, SimComponents.Construct))
            {
                continue;
            }
            CellOf(villager, out int villagerX, out int villagerY);
            long distance = DistanceSquared(villagerX, villagerY, targetX, targetY);
            if (distance < bestDistance || (distance == bestDistance
                && (bestVillager < 0 || villager < bestVillager)))
            {
                bestDistance = distance;
                bestVillager = villager;
            }
        }
        return bestVillager;
    }

    private void PlanGather(List<LockstepCommand> commands, WorldScan scan)
    {
        SimWorld world = Sim.World;
        foreach (long villager in scan.Villagers)
        {
            if (world.HasComponent(villager, SimComponents.Construct))
            {
                continue;
            }
            if (world.GetValue(villager, SimComponents.Gather, SimComponents.GatherState)
                != GatherSystem.GatherStateIdle)
            {
                continue;
            }
            long desired = GatherMix[_rng.NextIntRange(0, GatherMix.Length - 1)];
            long node = FindNearestNode(villager, desired);
            if (node < 0)
            {
                node = FindNearestNode(villager, -1);
            }
            if (node >= 0)
            {
                commands.Add(new LockstepCommand(
                    Team, LockstepCommand.KindGather, entity: villager, targetEntity: node,
                    dropEntity: -1));
            }
        }
    }

    private long FindNearestNode(long villager, long resourceType)
    {
        SimWorld world = Sim.World;
        CellOf(villager, out int fromX, out int fromY);
        long bestNode = -1;
        long bestDistance = long.MaxValue;
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.ResourceNode))
            {
                continue;
            }
            if (world.GetValue(id, SimComponents.ResourceNode, SimComponents.Remaining) <= 0)
            {
                continue;
            }
            if (resourceType >= 0
                && world.GetValue(id, SimComponents.ResourceNode, SimComponents.NodeType)
                    != resourceType)
            {
                continue;
            }
            CellOf(id, out int nodeX, out int nodeY);
            if (Sim.Terrain.IsWater(nodeX, nodeY))
            {
                continue;
            }
            long distance = DistanceSquared(fromX, fromY, nodeX, nodeY);
            if (distance < bestDistance || (distance == bestDistance
                && (bestNode < 0 || id < bestNode)))
            {
                bestDistance = distance;
                bestNode = id;
            }
        }
        return bestNode;
    }

    private void PlanMilitaryProduction(List<LockstepCommand> commands, WorldScan scan)
    {
        if (scan.Military.Count >= _profile.MaxMilitary)
        {
            return;
        }
        SimWorld world = Sim.World;
        long barracks = -1;
        foreach (long candidate in scan.Barracks)
        {
            long state = world.GetValue(candidate, SimComponents.Building, SimComponents.State);
            if (state == BuildingSystem.StateComplete
                && QueuedTotal(world, candidate) < _profile.MilitaryQueueRoom)
            {
                barracks = candidate;
                break;
            }
        }
        if (barracks < 0 || !Sim.CanSpawnPopulation(Team, 1))
        {
            return;
        }
        long civ = Sim.Civ(Team);
        for (int offset = 0; offset < MilitaryMix.Length; offset++)
        {
            long unitType = MilitaryMix[(_militaryTrained + offset) % MilitaryMix.Length];
            if (!UnitDefs.IsUnitAvailable(civ, unitType) || !CanAfford(unitType))
            {
                continue;
            }
            commands.Add(TrainCommand(barracks, unitType));
            _militaryTrained += 1;
            return;
        }
    }

    private void PlanRally(List<LockstepCommand> commands, WorldScan scan)
    {
        if (TryFindEnemyAnchor(scan, out int anchorX, out int anchorY) == false)
        {
            return;
        }
        int rallyX = scan.TownCenterX + (anchorX - scan.TownCenterX) / 4;
        int rallyY = scan.TownCenterY + (anchorY - scan.TownCenterY) / 4;
        SimWorld world = Sim.World;
        foreach (long barracks in scan.Barracks)
        {
            long state = world.GetValue(barracks, SimComponents.Building, SimComponents.State);
            if (state != BuildingSystem.StateComplete)
            {
                continue;
            }
            commands.Add(new LockstepCommand(
                Team, LockstepCommand.KindSetRally, entity: barracks,
                cellX: rallyX, cellY: rallyY, targetEntity: -1));
        }
    }

    private void PlanAttack(List<LockstepCommand> commands, WorldScan scan, long turn)
    {
        if (scan.Military.Count == 0)
        {
            return;
        }
        if (!_attacking)
        {
            if (scan.Military.Count < _profile.AttackThreshold
                || turn < _profile.FirstAttackTurn)
            {
                return;
            }
            _attacking = true;
            _targetX = -1;
            _targetY = -1;
        }
        if (!TryFindAttackTarget(scan, out int targetX, out int targetY))
        {
            return;
        }
        bool targetChanged = targetX != _targetX || targetY != _targetY;
        foreach (long unit in scan.Military)
        {
            if (Sim.World.HasComponent(unit, SimComponents.AttackTarget))
            {
                continue;
            }
            if (!targetChanged && IsMarchingTo(unit, targetX, targetY))
            {
                continue;
            }
            commands.Add(new LockstepCommand(
                Team, LockstepCommand.KindAttackMove, entity: unit,
                cellX: targetX, cellY: targetY));
        }
        _targetX = targetX;
        _targetY = targetY;
    }

    private bool TryFindAttackTarget(WorldScan scan, out int targetX, out int targetY)
    {
        SimWorld world = Sim.World;
        VisionGrid? vision = Sim.VisionFor(Team);
        int anchorX = scan.TownCenterX;
        int anchorY = scan.TownCenterY;
        long defendUnit = -1;
        long defendDistance = long.MaxValue;
        int defendX = 0;
        int defendY = 0;
        long enemyDistance = long.MaxValue;
        int enemyX = 0;
        int enemyY = 0;
        long closestEnemy = -1;
        foreach (long id in world.Entities())
        {
            if (!world.IsAlive(id) || !world.HasComponent(id, SimComponents.Team)
                || !world.HasComponent(id, SimComponents.Position)
                || !world.HasComponent(id, SimComponents.Health))
            {
                continue;
            }
            long owner = world.GetValue(id, SimComponents.Team, SimComponents.TeamId);
            if (owner == Team || owner == GameSim.GaiaTeam)
            {
                continue;
            }
            CellOf(id, out int cellX, out int cellY);
            if (vision != null && !vision.VisibleAt(cellX, cellY))
            {
                continue;
            }
            long distance = DistanceSquared(cellX, cellY, anchorX, anchorY);
            bool isBuilding = world.HasComponent(id, SimComponents.Building);
            if (!isBuilding && world.HasComponent(id, SimComponents.Combat)
                && distance <= DefenseRadiusCells * DefenseRadiusCells)
            {
                if (distance < defendDistance || (distance == defendDistance
                    && (defendUnit < 0 || id < defendUnit)))
                {
                    defendDistance = distance;
                    defendUnit = id;
                    defendX = cellX;
                    defendY = cellY;
                }
            }
            if (distance < enemyDistance || (distance == enemyDistance
                && (closestEnemy < 0 || id < closestEnemy)))
            {
                enemyDistance = distance;
                closestEnemy = id;
                enemyX = cellX;
                enemyY = cellY;
            }
        }
        if (defendUnit >= 0)
        {
            return TryApproachCell(defendX, defendY, out targetX, out targetY);
        }
        if (closestEnemy >= 0)
        {
            return TryApproachCell(enemyX, enemyY, out targetX, out targetY);
        }
        if (!TryFindEnemyAnchor(scan, out int fallbackX, out int fallbackY))
        {
            targetX = 0;
            targetY = 0;
            return false;
        }
        return TryApproachCell(fallbackX, fallbackY, out targetX, out targetY);
    }

    /// <summary>目标格可能落在建筑占地内而不可达，改为取最近的可站立邻格。</summary>
    private bool TryApproachCell(int targetX, int targetY, out int cellX, out int cellY)
    {
        if (Traversal.CellPassable(Sim.Grid, Sim.Terrain, targetX, targetY, MovementDomain.Land))
        {
            cellX = targetX;
            cellY = targetY;
            return true;
        }
        for (int radius = 1; radius <= 10; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) != radius)
                    {
                        continue;
                    }
                    int candidateX = targetX + offsetX;
                    int candidateY = targetY + offsetY;
                    if (Traversal.CellPassable(
                        Sim.Grid, Sim.Terrain, candidateX, candidateY, MovementDomain.Land))
                    {
                        cellX = candidateX;
                        cellY = candidateY;
                        return true;
                    }
                }
            }
        }
        cellX = targetX;
        cellY = targetY;
        return false;
    }

    private bool TryFindEnemyAnchor(WorldScan scan, out int anchorX, out int anchorY)
    {
        anchorX = 0;
        anchorY = 0;
        if (_enemyStartX.Length == 0)
        {
            anchorX = Sim.Grid.Width / 2;
            anchorY = Sim.Grid.Height / 2;
            return true;
        }
        long bestDistance = long.MaxValue;
        int bestIndex = -1;
        for (int index = 0; index < _enemyStartX.Length; index++)
        {
            long distance = DistanceSquared(
                scan.TownCenterX, scan.TownCenterY, _enemyStartX[index], _enemyStartY[index]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }
        anchorX = _enemyStartX[bestIndex];
        anchorY = _enemyStartY[bestIndex];
        return true;
    }

    private bool IsMarchingTo(long unit, int cellX, int cellY)
    {
        SimWorld world = Sim.World;
        if (!world.HasComponent(unit, SimComponents.Order))
        {
            return false;
        }
        if (world.GetValue(unit, SimComponents.Order, SimComponents.OrderType)
            != OrderSystem.TypeAttackMove)
        {
            return false;
        }
        return world.GetValue(unit, SimComponents.Order, SimComponents.TargetX) == cellX
            && world.GetValue(unit, SimComponents.Order, SimComponents.TargetY) == cellY;
    }

    private bool CanAfford(long unitType)
    {
        for (long kind = 0; kind < 4; kind++)
        {
            if (Sim.Resource(Team, kind) < UnitDefs.Cost(unitType).AmountOf(kind))
            {
                return false;
            }
        }
        return true;
    }

    private static long QueuedTotal(SimWorld world, long building)
    {
        if (!world.HasComponent(building, SimComponents.Production))
        {
            return 0;
        }
        long current = world.GetValue(building, SimComponents.Production, SimComponents.ProdCurrent);
        long count = world.GetValue(building, SimComponents.Production, SimComponents.ProdQueueCount);
        return count + (current != ProductionSystem.None ? 1 : 0);
    }

    private LockstepCommand TrainCommand(long building, long unitType) =>
        new(Team, LockstepCommand.KindTrain, entity: building, targetEntity: -1, aux: unitType);

    private void CellOf(long entity, out int cellX, out int cellY)
    {
        SimWorld world = Sim.World;
        cellX = (int)Fixed.ToInt(world.GetValue(entity, SimComponents.Position, SimComponents.X));
        cellY = (int)Fixed.ToInt(world.GetValue(entity, SimComponents.Position, SimComponents.Y));
    }

    private static long DistanceSquared(int fromX, int fromY, int toX, int toY)
    {
        long deltaX = toX - fromX;
        long deltaY = toY - fromY;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
