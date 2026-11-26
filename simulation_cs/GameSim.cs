using System.Collections.Generic;

namespace AoeSim;

public sealed class GameSim
{
    public const long ResourceWood = 0;
    public const long ResourceFood = 1;
    public const long ResourceGold = 2;
    public const long ResourceStone = 3;

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
        int fieldCacheCapacity = DefaultFieldCacheCapacity)
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
        for (int player = 0; player < playerCount; player++)
        {
            Vision.Add(new VisionGrid(grid.Width, grid.Height));
        }
        if (matchConfig != null)
        {
            Match = MatchState.Create(World, Lockstep.PlayerCount, matchConfig);
        }
    }

    /// <summary>
    /// 按兵种生成单位：HP/攻击/射程/护甲/克制全部取自 <see cref="UnitDefs"/>，
    /// 写进 Combat 列随校验和同步。旧签名继续可用，内部转调同一套底层生成逻辑。
    /// </summary>
    public long SpawnUnit(int cellX, int cellY, long team, long unitType, long stance = CombatStance.Aggressive, long popCost = 0)
    {
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
        long effectiveMaxHp = TechCatalog.EffectiveStat(age, research, TechCatalog.StatMaxHp, maxHp);
        long effectiveDamage = TechCatalog.EffectiveStat(age, research, TechCatalog.StatDamage, damage);
        long rangeMin = ToRangeCells(rangeMinTiles);
        long rangeMax = TechCatalog.EffectiveStat(
            age, research, TechCatalog.StatRangeMax, ToRangeCells(rangeMaxTiles));
        if (rangeMax < rangeMin)
        {
            rangeMax = rangeMin;
        }
        long cooldown = TechCatalog.EffectiveStat(age, research, TechCatalog.StatCooldown, 6);
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
        World.SetValue(id, SimComponents.Move, SimComponents.TargetX, cellX);
        World.SetValue(id, SimComponents.Move, SimComponents.TargetY, cellY);
        World.SetValue(id, SimComponents.Move, SimComponents.Active, 1);
    }

    public long IssuePlaceBuilding(long type, int cellX, int cellY, long player)
    {
        long id = BuildingSystem.Place(World, Grid, Terrain, type, cellX, cellY, player);
        if (id >= 0)
        {
            InvalidateFields();
        }
        return id;
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
        || kind == LockstepCommand.KindPlaceBuilding;

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
        || kind == LockstepCommand.KindConstruct;

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
            case LockstepCommand.KindConstruct:
                IssueConstruct(command.Entity, command.TargetEntity);
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
        GatherSystem.Step(World, InventoryForTeam);
        MonkSystem.Step(World);
        OrderSystem.Step(World, Spatial);
        BuildingSystem.Step(World, Grid, Terrain);
        MovementSystem.Step(World, Grid, Occupancy, FieldProvider, Terrain);
        Spatial.Rebuild(World);
        List<long> dead = ProjectileSystem.Step(World);
        dead.AddRange(CombatSystem.Step(World, Spatial));
        ResolveDeaths(dead);
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
            if (World.HasComponent(id, SimComponents.Building))
            {
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
    private FlowField FieldProvider(int targetX, int targetY)
    {
        long key = ((long)targetX << 32) ^ (targetY & 0xFFFF_FFFFL);
        if (_fields.TryGetValue(key, out FlowField? field))
        {
            LinkedListNode<long> node = _fieldNodes[key];
            _fieldRecency.Remove(node);
            _fieldRecency.AddLast(node);
            return field;
        }
        field = new FlowField(Grid, Terrain);
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
