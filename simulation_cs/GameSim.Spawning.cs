namespace AoeSim;

public sealed partial class GameSim
{
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

    /// <summary>
    /// 运输船：水面移动、无攻击能力，带乘员容量。陆地单位经装载指令上船后随船移动，
    /// 靠岸或临码头时再卸载；被击沉时船上乘员一并损失。
    /// </summary>
    public long SpawnTransportShip(
        int cellX,
        int cellY,
        long team,
        long capacity = TransportSystem.DefaultCapacity,
        long maxHp = 120,
        double speedTilesPerTick = 0.12,
        long popCost = 1)
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
        World.Attach(id, SimComponents.Team);
        World.Attach(id, SimComponents.Footprint);
        World.Attach(id, SimComponents.Transport);
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
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintWidth, 1);
        World.SetValue(id, SimComponents.Footprint, SimComponents.FootprintHeight, 1);
        World.SetValue(id, SimComponents.Transport, SimComponents.Count, 0);
        World.SetValue(id, SimComponents.Transport, SimComponents.Capacity, capacity);
        AttachDomain(id, MovementDomain.Water);
        AccountPopulation(id, team, popCost);
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
}
