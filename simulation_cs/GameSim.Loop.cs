using System.Collections.Generic;

namespace AoeSim;

/// <summary>对局循环：逐 tick 编排各 System、结算死亡、产出校验和与流场查询。</summary>
public sealed partial class GameSim
{
    public void SimulateTick()
    {
        _fieldCache.ResetTick();
        if (Match != null && Match.Ended)
        {
            return;
        }
        if (!_spatialReady)
        {
            Spatial.Sync(World);
            _spatialReady = true;
        }
        GatherSystem.Step(World, InventoryForTeam, Grid, Terrain, Match);
        MonkSystem.Step(World);
        OrderSystem.Step(World, Spatial);
        BuildingSystem.Step(World, Grid, Terrain);
        GarrisonSystem.Step(World);
        RepairSystem.Step(World, Grid, Terrain, this);
        TradeSystem.Step(World, Grid, Terrain, this);
        MovementSystem.Step(World, Grid, Occupancy, FieldProvider, Terrain);
        Spatial.Sync(World);
        var kills = new List<KillEvent>();
        List<long> dead = ProjectileSystem.Step(World, kills);
        dead.AddRange(CombatSystem.Step(World, Spatial, kills));
        ResolveDeaths(dead);
        RecordKills(kills);
        ProductionSystem.Step(World, this);
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
            if (World.HasComponent(id, SimComponents.Transport))
            {
                TransportSystem.SinkPassengers(World, id);
            }
            PopulationSystem.Release(World, id);
            World.Despawn(id);
        }
        if (fieldsChanged)
        {
            InvalidateFields();
        }
    }

    /// <summary>把本 tick 的击杀归属并入对局统计；同一目标被多次致死结算只记一次。</summary>
    private void RecordKills(List<KillEvent> kills)
    {
        if (Match == null || kills.Count == 0)
        {
            return;
        }
        var counted = new HashSet<long>();
        foreach (KillEvent kill in kills)
        {
            if (!counted.Add(kill.Victim))
            {
                continue;
            }
            if (kill.KillerTeam < 0 || kill.VictimTeam < 0)
            {
                continue;
            }
            Match.RecordKill(kill.KillerTeam, kill.VictimTeam);
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

    public void InvalidateFields() => _fieldCache.Invalidate();

    internal void InvalidateFieldsForBlockedRect(int originX, int originY, int width, int height) =>
        _fieldCache.InvalidateForBlockedRect(originX, originY, width, height);

    private FlowField FieldProvider(int targetX, int targetY, long domain) =>
        _fieldCache.Get(targetX, targetY, domain);
}
