using System.Collections.Generic;

namespace AoeSim;

public sealed class GameSim
{
    public const long ResourceWood = 0;
    public const long ResourceFood = 1;
    public const long ResourceGold = 2;
    public const long ResourceStone = 3;

    /// <summary>中立野生动物队伍：不与任何玩家开战，也不会被自动索敌。</summary>
    public const long GaiaTeam = -1;

    public const double DefaultSightTiles = 4.0;
    public const int DefaultFieldCacheCapacity = 128;

    public SimWorld World { get; }
    public NavGrid Grid { get; }
    public TerrainGrid Terrain { get; }
    public SpatialHash Spatial { get; }
    public OccupancyMap Occupancy { get; }
    public Lockstep Lockstep { get; }
    public MatchState? Match { get; }
    public List<VisionGrid> Vision { get; } = new();

    private readonly long _sightRadius = Fixed.FromFloat(DefaultSightTiles);
    private readonly Dictionary<long, FlowField> _fields = new();
    private readonly Dictionary<long, LinkedListNode<long>> _fieldNodes = new();
    private readonly LinkedList<long> _fieldRecency = new();
    private readonly Dictionary<long, long> _inventoryEntities = new();
    private readonly Dictionary<long, long> _progressEntities = new();
    private readonly long[] _civIds;
    private bool _spatialReady;

    /// <summary>当前流场缓存中的条目数，供性能测试与监控断言。</summary>
    public int FieldCacheCount => _fields.Count;

    public int FieldCacheCapacity { get; }

    /// <summary>本 tick 新建的流场数；每次 SimulateTick 开始时清零，不进校验和。</summary>
    public int FieldsCreatedThisTick { get; private set; }

    public long FieldsCreatedTotal { get; private set; }

    public GameSim(
        NavGrid grid,
        long seed = 1,
        int playerCount = 2,
        int ticksPerTurn = 6,
        MatchConfig? matchConfig = null,
        TerrainGrid? terrain = null,
        int fieldCacheCapacity = DefaultFieldCacheCapacity,
        long[]? civIds = null)
    {
        if (fieldCacheCapacity < 1)
        {
            throw new System.ArgumentOutOfRangeException(nameof(fieldCacheCapacity));
        }
        FieldCacheCapacity = fieldCacheCapacity;
        Grid = grid;
        Terrain = terrain ?? TerrainGrid.Flat(grid.Width, grid.Height);
        if (Terrain.Width != grid.Width || Terrain.Height != grid.Height)
        {
            throw new System.ArgumentException("terrain dimensions must match nav grid");
        }
        World = new SimWorld(seed);
        Spatial = new SpatialHash(4);
        Occupancy = new OccupancyMap(grid.Width, grid.Height);
        Lockstep = new Lockstep(playerCount, ticksPerTurn);
        _civIds = new long[playerCount];
        for (int player = 0; player < playerCount; player++)
        {
            Vision.Add(new VisionGrid(grid.Width, grid.Height));
            long civ = civIds != null && player < civIds.Length
                ? civIds[player]
                : CivilizationCatalog.Generic;
            _civIds[player] = CivilizationCatalog.IsValid(civ) ? civ : CivilizationCatalog.Generic;
        }
        if (matchConfig != null)
        {
            Match = MatchState.Create(World, Lockstep.PlayerCount, matchConfig);
        }
        if (civIds != null)
        {
            for (int player = 0; player < Lockstep.PlayerCount; player++)
            {
                Progress(player);
            }
        }
    }

    /// <summary>某玩家本局所选文明；越界返回通用文明。</summary>
    public long Civ(long team) =>
        team >= 0 && team < _civIds.Length ? _civIds[team] : CivilizationCatalog.Generic;

    /// <summary>
    /// 按兵种生成单位：HP/攻击/射程/护甲/克制全部取自 <see cref="UnitDefs"/>，
    /// 写进 Combat 列随校验和同步。旧签名继续可用，内部转调同一套底层生成逻辑。
    /// </summary>
    public long SpawnUnit(int cellX, int cellY, long team, long unitType, long stance = CombatStance.Aggressive, long popCost = 0)
    {
        if (!UnitDefs.IsUnitAvailable(Civ(team), unitType))
        {
            return -1;
        }
        UnitDef def = UnitDefs.Get(unitType);
        long id = SpawnUnitCore(
            cellX,
            cellY,
            team,
            def.MaxHp,
            def.AttackDamage,
            def.MinRangeTiles,
            def.MaxRangeTiles,
            def.SpeedTilesPerTick,
            stance,
            popCost);
        if (id < 0)
        {
            return -1;
        }
        World.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, def.Cooldown);
        World.SetValue(id, SimComponents.Combat, SimComponents.UnitType, unitType);
        World.SetValue(id, SimComponents.Combat, SimComponents.AttackClass, def.AttackClass);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorClass, def.ArmorClass);
        World.SetValue(id, SimComponents.Combat, SimComponents.IsRanged, def.IsRanged ? 1 : 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorMelee, def.ArmorMelee);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorPierce, def.ArmorPierce);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorSiege, def.ArmorSiege);
        return id;
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
        long stance = CombatStance.Aggressive,
        long popCost = 0)
    {
        long id = SpawnUnitCore(
            cellX,
            cellY,
            team,
            maxHp,
            damage,
            rangeMinTiles,
            rangeMaxTiles,
            speedTilesPerTick,
            stance,
            popCost);
        if (id < 0)
        {
            return -1;
        }
        bool ranged = World.GetValue(id, SimComponents.Combat, SimComponents.RangeMax) > 1;
        World.SetValue(id, SimComponents.Combat, SimComponents.UnitType, UnitDefs.Swordsman);
        World.SetValue(
            id,
            SimComponents.Combat,
            SimComponents.AttackClass,
            ranged ? UnitAttackClass.Pierce : UnitAttackClass.Melee);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorClass, UnitArmorClass.Infantry);
        World.SetValue(id, SimComponents.Combat, SimComponents.IsRanged, ranged ? 1 : 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorMelee, 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorPierce, 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.ArmorSiege, 0);
        return id;
    }

    private long SpawnUnitCore(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        long damage,
        double rangeMinTiles,
        double rangeMaxTiles,
        double speedTilesPerTick,
        long stance,
        long popCost)
    {
        if (!PopulationSystem.CanSpawn(World, team, popCost))
        {
            return -1;
        }
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
        long age = PlayerAge(team);
        long research = PlayerResearch(team);
        long civ = Civ(team);
        long effectiveMaxHp = TechCatalog.EffectiveStat(civ, age, research, TechCatalog.StatMaxHp, maxHp);
        long effectiveDamage = TechCatalog.EffectiveStat(civ, age, research, TechCatalog.StatDamage, damage);
        long rangeMin = ToRangeCells(rangeMinTiles);
        long rangeMax = TechCatalog.EffectiveStat(
            civ, age, research, TechCatalog.StatRangeMax, ToRangeCells(rangeMaxTiles));
        if (rangeMax < rangeMin)
        {
            rangeMax = rangeMin;
        }
        long cooldown = TechCatalog.EffectiveStat(civ, age, research, TechCatalog.StatCooldown, 6);
        World.SetValue(id, SimComponents.Health, SimComponents.Hp, effectiveMaxHp);
        World.SetValue(id, SimComponents.Health, SimComponents.MaxHp, effectiveMaxHp);
        World.SetValue(id, SimComponents.Combat, SimComponents.Damage, effectiveDamage);
        World.SetValue(id, SimComponents.Combat, SimComponents.RangeMin, rangeMin);
        World.SetValue(id, SimComponents.Combat, SimComponents.RangeMax, rangeMax);
        World.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, cooldown);
        World.SetValue(id, SimComponents.Combat, SimComponents.CooldownLeft, 0);
        World.SetValue(id, SimComponents.Combat, SimComponents.Stance, stance);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        AccountPopulation(id, team, popCost);
        return id;
    }

    /// <summary>把单位的人口占用记入自身 POP 组件并累加玩家账本。</summary>
    private void AccountPopulation(long id, long team, long popCost)
    {
        if (popCost <= 0)
        {
            return;
        }
        World.Attach(id, SimComponents.Pop);
        World.SetValue(id, SimComponents.Pop, SimComponents.PopCost, popCost);
        PopulationSystem.Reserve(World, team, popCost);
    }

    private static long ToRangeCells(double tiles) =>
        (long)System.Math.Round(tiles, System.MidpointRounding.AwayFromZero);

    public PlayerProgress Progress(long team)
    {
        if (!_progressEntities.TryGetValue(team, out long entity))
        {
            if (!World.TryGetPlayerEntity(SimComponents.PlayerProgress, team, out entity))
            {
                entity = World.Spawn();
                World.Attach(entity, SimComponents.PlayerProgress);
                World.Attach(entity, SimComponents.Team);
                World.SetValue(entity, SimComponents.Team, SimComponents.TeamId, team);
                World.SetValue(entity, SimComponents.PlayerProgress, SimComponents.Age, TechCatalog.AgeDark);
                World.SetValue(entity, SimComponents.PlayerProgress, SimComponents.Research, 0);
                long civ = Civ(team);
                if (civ != CivilizationCatalog.Generic)
                {
                    World.SetValue(entity, SimComponents.PlayerProgress, SimComponents.Civ, civ);
                }
                World.RegisterPlayerEntity(SimComponents.PlayerProgress, team, entity);
            }
            _progressEntities[team] = entity;
        }
        return new PlayerProgress(World, entity, team);
    }

    public long PlayerAge(long team) =>
        _progressEntities.TryGetValue(team, out long entity)
        || World.TryGetPlayerEntity(SimComponents.PlayerProgress, team, out entity)
            ? World.GetValue(entity, SimComponents.PlayerProgress, SimComponents.Age)
            : TechCatalog.AgeDark;

    public long PlayerResearch(long team) =>
        _progressEntities.TryGetValue(team, out long entity)
        || World.TryGetPlayerEntity(SimComponents.PlayerProgress, team, out entity)
            ? World.GetValue(entity, SimComponents.PlayerProgress, SimComponents.Research)
            : 0;

    public bool IsResearched(long team, long techId) =>
        TechCatalog.IsResearched(PlayerResearch(team), techId);

    public bool AgeUp(long team, long targetAge)
    {
        if (team < 0 || team >= Lockstep.PlayerCount || !TechCatalog.IsValidAge(targetAge))
        {
            return false;
        }
        if (targetAge != PlayerAge(team) + 1)
        {
            return false;
        }
        return ResearchSystem.TryStartAge(this, team, targetAge);
    }

    public bool Research(long team, long techId)
    {
        if (team < 0 || team >= Lockstep.PlayerCount || !TechCatalog.IsValidTech(techId))
        {
            return false;
        }
        return ResearchSystem.TryStartTech(this, team, techId);
    }

    public bool CancelResearch(long team)
    {
        if (team < 0 || team >= Lockstep.PlayerCount)
        {
            return false;
        }
        return ResearchSystem.Cancel(this, team);
    }

    internal void ApplyProgressDelta(
        long team, long oldAge, long oldResearch, long newAge, long newResearch)
    {
        ApplyStatDelta(
            team,
            ProgressDelta(oldAge, oldResearch, newAge, newResearch, TechCatalog.StatMaxHp),
            ProgressDelta(oldAge, oldResearch, newAge, newResearch, TechCatalog.StatDamage),
            ProgressDelta(oldAge, oldResearch, newAge, newResearch, TechCatalog.StatRangeMax),
            ProgressDelta(oldAge, oldResearch, newAge, newResearch, TechCatalog.StatCooldown));
    }

    private static long ProgressDelta(
        long oldAge, long oldResearch, long newAge, long newResearch, long stat) =>
        TechCatalog.AgeBonus(newAge, stat) + TechCatalog.TechBonus(newResearch, stat)
        - TechCatalog.AgeBonus(oldAge, stat) - TechCatalog.TechBonus(oldResearch, stat);

    private void ApplyStatDelta(
        long team, long maxHpDelta, long damageDelta, long rangeMaxDelta, long cooldownDelta)
    {
        ComponentStore combatStore = World.Store(SimComponents.Combat);
        if (combatStore.Count == 0)
        {
            return;
        }
        foreach (long id in World.Entities())
        {
            if (!World.IsAlive(id) || !combatStore.Has(id))
            {
                continue;
            }
            if (World.GetValue(id, SimComponents.Team, SimComponents.TeamId) != team)
            {
                continue;
            }
            if (maxHpDelta != 0)
            {
                long maxHp = World.GetValue(id, SimComponents.Health, SimComponents.MaxHp) + maxHpDelta;
                long hp = World.GetValue(id, SimComponents.Health, SimComponents.Hp) + maxHpDelta;
                World.SetValue(id, SimComponents.Health, SimComponents.MaxHp, maxHp);
                World.SetValue(id, SimComponents.Health, SimComponents.Hp, System.Math.Max(0, hp));
            }
            if (damageDelta != 0)
            {
                long damage = World.GetValue(id, SimComponents.Combat, SimComponents.Damage) + damageDelta;
                World.SetValue(id, SimComponents.Combat, SimComponents.Damage, System.Math.Max(0, damage));
            }
            if (rangeMaxDelta != 0)
            {
                long rangeMin = World.GetValue(id, SimComponents.Combat, SimComponents.RangeMin);
                long rangeMax = World.GetValue(id, SimComponents.Combat, SimComponents.RangeMax) + rangeMaxDelta;
                World.SetValue(
                    id, SimComponents.Combat, SimComponents.RangeMax, System.Math.Max(rangeMin, rangeMax));
            }
            if (cooldownDelta != 0)
            {
                long cooldown = World.GetValue(id, SimComponents.Combat, SimComponents.Cooldown) + cooldownDelta;
                World.SetValue(id, SimComponents.Combat, SimComponents.Cooldown, System.Math.Max(1, cooldown));
            }
        }
    }

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

    public long SpawnResourceNode(int cellX, int cellY, long nodeType, long amount, long subtype = ResourceSubtype.None)
    {
        EnsureInventoryInitialized();
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.ResourceNode);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.ResourceNode, SimComponents.NodeType, nodeType);
        World.SetValue(id, SimComponents.ResourceNode, SimComponents.Subtype, subtype);
        World.SetValue(id, SimComponents.ResourceNode, SimComponents.Remaining, amount);
        return id;
    }

    /// <summary>
    /// 生成一只中立（gaia）野生动物：带血量与位置，可被玩家显式攻击；不挂 Combat，因此不会反击，
    /// 也不会进入自动索敌。死亡后由 <see cref="HuntingSystem"/> 转为食物尸体。
    /// </summary>
    public long SpawnHuntingAnimal(int cellX, int cellY, long species)
    {
        if (!AnimalSpecies.IsValid(species))
        {
            return -1;
        }
        long hp = AnimalSpecies.MaxHp(species);
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Health);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.HuntingAnimal);
        World.Attach(id, SimComponents.Footprint);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Health, SimComponents.Hp, hp);
        World.SetValue(id, SimComponents.Health, SimComponents.MaxHp, hp);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, GaiaTeam);
        World.SetValue(id, SimComponents.HuntingAnimal, SimComponents.Species, species);
        World.SetValue(id, SimComponents.HuntingAnimal, SimComponents.Meat, AnimalSpecies.Meat(species));
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
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
        double gatherRatePerTick,
        long popCost = 0)
    {
        if (!PopulationSystem.CanSpawn(World, team, popCost))
        {
            return -1;
        }
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
        AccountPopulation(id, team, popCost);
        return id;
    }

    /// <summary>陆地/水面通行域标记；未挂域组件的实体一律按陆地处理。</summary>
    private void AttachDomain(long id, long domain)
    {
        World.Attach(id, SimComponents.Domain);
        World.SetValue(id, SimComponents.Domain, SimComponents.DomainId, domain);
    }

    /// <summary>渔船：水面移动，对鱼群资源节点采集食物并返回码头投放。</summary>
    public long SpawnFishingShip(
        int cellX,
        int cellY,
        long team,
        double speedTilesPerTick = 0.10,
        long carryCapacity = 10,
        double gatherRatePerTick = 0.45,
        long popCost = 1)
    {
        long id = SpawnVillager(cellX, cellY, team, speedTilesPerTick, carryCapacity, gatherRatePerTick, popCost);
        if (id >= 0)
        {
            AttachDomain(id, MovementDomain.Water);
        }
        return id;
    }

    /// <summary>战船：水面作战单位，纳入现有战斗/护甲/弹道体系，水战即单位对射。</summary>
    public long SpawnWarship(
        int cellX,
        int cellY,
        long team,
        long maxHp = 140,
        long damage = 12,
        double rangeMaxTiles = 6.0,
        double speedTilesPerTick = 0.11,
        long popCost = 1)
    {
        long id = SpawnUnit(
            cellX, cellY, team, maxHp, damage, 1.0, rangeMaxTiles, speedTilesPerTick,
            CombatStance.Aggressive, popCost);
        if (id >= 0)
        {
            AttachDomain(id, MovementDomain.Water);
        }
        return id;
    }

    /// <summary>放置并完工一座码头（临水校验在 <see cref="BuildingSystem.CanPlace"/>）。</summary>
    public long SpawnDock(int cellX, int cellY, long team)
    {
        long id = BuildingSystem.Place(World, Grid, Terrain, BuildingTypes.Dock, cellX, cellY, team);
        if (id >= 0)
        {
            BuildingSystem.Complete(World, id);
        }
        return id;
    }

    /// <summary>陆路贸易车：无战斗能力，靠市场路线指令往返产金，生产入口待单位训练合入后接入。</summary>
    public long SpawnTradeCart(int cellX, int cellY, long team)
    {
        long id = World.Spawn();
        World.Attach(id, SimComponents.Position);
        World.Attach(id, SimComponents.Velocity);
        World.Attach(id, SimComponents.Move);
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Footprint);
        World.Attach(id, SimComponents.Trade);
        World.SetValue(id, SimComponents.Position, SimComponents.X, Fixed.Add(Fixed.FromInt(cellX), Fixed.Half));
        World.SetValue(id, SimComponents.Position, SimComponents.Y, Fixed.Add(Fixed.FromInt(cellY), Fixed.Half));
        World.SetValue(id, SimComponents.Velocity, SimComponents.X, 0);
        World.SetValue(id, SimComponents.Velocity, SimComponents.Y, 0);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Speed, Fixed.FromFloat(TradeSystem.CartSpeedTilesPerTick));
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 0);
        World.SetValue(id, SimComponents.Team, SimComponents.TeamId, team);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        World.SetValue(id, SimComponents.Trade, SimComponents.TradeHome, -1);
        World.SetValue(id, SimComponents.Trade, SimComponents.TradeTarget, -1);
        return id;
    }

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

    public long SpawnKing(
        int cellX,
        int cellY,
        long team,
        long maxHp,
        long damage,
        double rangeMinTiles,
        double rangeMaxTiles,
        double speedTilesPerTick,
        long popCost = 0)
    {
        long id = SpawnUnit(cellX, cellY, team, maxHp, damage, rangeMinTiles, rangeMaxTiles, speedTilesPerTick, CombatStance.Aggressive, popCost);
        if (id < 0)
        {
            return -1;
        }
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
        World.SetValue(id, SimComponents.Building, SimComponents.BuildingType, BuildingTypes.None);
        World.SetValue(id, SimComponents.Building, SimComponents.SizeWidth, 1);
        World.SetValue(id, SimComponents.Building, SimComponents.SizeHeight, 1);
        World.SetValue(id, SimComponents.Building, SimComponents.Progress, 1);
        World.SetValue(id, SimComponents.Building, SimComponents.MaxProgress, 1);
        World.SetValue(id, SimComponents.Building, SimComponents.State, BuildingSystem.StateComplete);
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

    /// <summary>累加某玩家的初始人口上限（房屋/城镇中心等上限来源在 AOE-5 接入时调用同一接口）。</summary>
    public long AddPopulationCap(long player, long amount) => PopulationSystem.AddCap(World, player, amount);

    public long PopulationUsed(long player) => PopulationSystem.Used(World, player);

    public long PopulationCap(long player) => PopulationSystem.Cap(World, player);

    public bool CanSpawnPopulation(long player, long cost) => PopulationSystem.CanSpawn(World, player, cost);

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
        if (!_inventoryEntities.ContainsKey(player))
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
        InvalidateFields();
        return id;
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
        if (!_inventoryEntities.ContainsKey(player))
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
        InvalidateFields();
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
        InvalidateFields();
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
        || kind == LockstepCommand.KindToggleGate;

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
        }
        return true;
    }

    public void SimulateTick()
    {
        FieldsCreatedThisTick = 0;
        if (Match != null && Match.Ended)
        {
            return;
        }
        if (!_spatialReady)
        {
            Spatial.Rebuild(World);
            _spatialReady = true;
        }
        GatherSystem.Step(World, InventoryForTeam, Grid, Terrain);
        MonkSystem.Step(World);
        OrderSystem.Step(World, Spatial);
        BuildingSystem.Step(World, Grid, Terrain);
        GarrisonSystem.Step(World);
        RepairSystem.Step(World, Grid, Terrain, this);
        TradeSystem.Step(World, Grid, Terrain, this);
        MovementSystem.Step(World, Grid, Occupancy, FieldProvider, Terrain);
        Spatial.Rebuild(World);
        List<long> dead = ProjectileSystem.Step(World);
        dead.AddRange(CombatSystem.Step(World, Spatial));
        ResolveDeaths(dead);
        HuntingSystem.Step(World);
        ResearchSystem.Step(this);
        RelicSystem.Step(World, InventoryForTeam);
        if (Match != null)
        {
            MatchSystem.Step(World, Match);
        }
        VisionSystem.Step(World, Vision, _sightRadius);
        World.Tick += 1;
    }

    private void ResolveDeaths(List<long> dead)
    {
        bool fieldsChanged = false;
        foreach (long id in dead)
        {
            if (!World.IsAlive(id))
            {
                continue;
            }
            if (World.HasComponent(id, SimComponents.HuntingAnimal))
            {
                HuntingSystem.TurnIntoCorpse(World, id);
                continue;
            }
            if (World.HasComponent(id, SimComponents.Building))
            {
                GarrisonSystem.ReleaseAll(World, Grid, Terrain, id);
                BuildingSystem.Ruin(World, Grid, id);
                fieldsChanged = true;
                continue;
            }
            PopulationSystem.Release(World, id);
            World.Despawn(id);
        }
        if (fieldsChanged)
        {
            InvalidateFields();
        }
    }

    public VisionGrid? VisionFor(long team) =>
        team >= 0 && team < Vision.Count ? Vision[(int)team] : null;

    /// <summary>世界状态 + 各玩家可视性位图的混合校验和，用于视野确定性对拍。</summary>
    public long Checksum()
    {
        long hash = World.Checksum();
        hash = (hash ^ Terrain.Checksum()) & SimWorld.Mask32;
        hash = (hash * SimWorld.FnvPrime) & SimWorld.Mask32;
        foreach (VisionGrid grid in Vision)
        {
            hash = (hash ^ grid.Checksum()) & SimWorld.Mask32;
            hash = (hash * SimWorld.FnvPrime) & SimWorld.Mask32;
        }
        return hash;
    }

    public FlowField FieldFor(int targetX, int targetY) =>
        FieldProvider(targetX, targetY, MovementDomain.Land);

    public FlowField FieldFor(int targetX, int targetY, long domain) =>
        FieldProvider(targetX, targetY, domain);

    public void InvalidateFields()
    {
        _fields.Clear();
        _fieldNodes.Clear();
        _fieldRecency.Clear();
    }

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

    /// <summary>
    /// 按目标格共享有界 LRU 流场：编队或大规模移动必须让单位共享同一目标格的流场，
    /// 单位各自按所在格取方向，不能为分摊落点生成多张流场，否则一次移动会触发多张全图 BFS。
    /// 流场是派生数据、不进校验和，淘汰后重建结果完全一致。
    /// </summary>
    private FlowField FieldProvider(int targetX, int targetY, long domain)
    {
        long key = ((long)targetX << 40) ^ ((long)targetY << 16) ^ (domain & 0xFFFFL);
        if (_fields.TryGetValue(key, out FlowField? field))
        {
            LinkedListNode<long> node = _fieldNodes[key];
            _fieldRecency.Remove(node);
            _fieldRecency.AddLast(node);
            return field;
        }
        field = new FlowField(Grid, Terrain, domain);
        field.Compute(Grid, targetX, targetY);
        if (_fields.Count >= FieldCacheCapacity)
        {
            LinkedListNode<long> oldest = _fieldRecency.First!;
            _fieldRecency.RemoveFirst();
            _fieldNodes.Remove(oldest.Value);
            _fields.Remove(oldest.Value);
        }
        _fields[key] = field;
        _fieldNodes[key] = _fieldRecency.AddLast(key);
        FieldsCreatedThisTick += 1;
        FieldsCreatedTotal += 1;
        return field;
    }
}
