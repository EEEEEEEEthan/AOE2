namespace AoeSim;

public sealed partial class GameSim
{
    public PlayerProgress Progress(long team)
    {
        if (!_progressIndex.TryGet(team, out long entity))
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
            _progressIndex.Set(team, entity);
        }
        return new PlayerProgress(World, entity, team);
    }

    public long PlayerAge(long team) =>
        _progressIndex.TryGet(team, out long entity)
        || World.TryGetPlayerEntity(SimComponents.PlayerProgress, team, out entity)
            ? World.GetValue(entity, SimComponents.PlayerProgress, SimComponents.Age)
            : TechCatalog.AgeDark;

    public long PlayerResearch(long team) =>
        _progressIndex.TryGet(team, out long entity)
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

    /// <summary>累加某玩家的初始人口上限（房屋/城镇中心等上限来源在 AOE-5 接入时调用同一接口）。</summary>
    public long AddPopulationCap(long player, long amount) => PopulationSystem.AddCap(World, player, amount);

    public long PopulationUsed(long player) => PopulationSystem.Used(World, player);

    public long PopulationCap(long player) => PopulationSystem.Cap(World, player);

    public bool CanSpawnPopulation(long player, long cost) => PopulationSystem.CanSpawn(World, player, cost);

    public long Resource(long player, long resourceKind)
    {
        EnsureInventoryInitialized();
        return World.GetValue(
            _inventoryIndex.Get(player),
            SimComponents.Resource,
            SimComponents.ResourceColumns[(int)resourceKind]);
    }

    public void AddResource(long player, long resourceKind, long amount)
    {
        EnsureInventoryInitialized();
        long inventory = _inventoryIndex.Get(player);
        string column = SimComponents.ResourceColumns[(int)resourceKind];
        World.SetValue(
            inventory,
            SimComponents.Resource,
            column,
            World.GetValue(inventory, SimComponents.Resource, column) + amount);
    }

    /// <summary>该玩家是否已有库存实体；指令落地在改世界前据此校验玩家范围。</summary>
    internal bool HasInventory(long player)
    {
        EnsureInventoryInitialized();
        return _inventoryIndex.Contains(player);
    }

    private void EnsureInventoryInitialized()
    {
        if (_inventoryIndex.Count > 0)
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
            _inventoryIndex.Set(player, id);
        }
    }

    private long InventoryForTeam(long team) =>
        _inventoryIndex.TryGet(team, out long inventory) ? inventory : -1;
}
