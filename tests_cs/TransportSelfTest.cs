using System;
using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>
/// 运输船自测：装船→航行→卸船后位置与校验和一致、未靠岸卸载被拒、超载/非法乘员被拒、
/// 运输船被击沉时乘员一并清除，以及整套流程的确定性对拍。
/// </summary>
public static class TransportSelfTest
{
    private const long Seed = 20261128;

    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckLoadSailUnload(failures);
        CheckRejections(failures);
        CheckSinkDropsPassengers(failures);
        CheckDeterminism(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static TerrainGrid ShoreTerrain(int width, int height, int waterFromX)
    {
        TerrainGrid terrain = TerrainGrid.Flat(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = waterFromX; x < width; x++)
            {
                terrain.Set(x, y, TerrainGrid.TypeWater, 0);
            }
        }
        return terrain;
    }

    private static NavGrid ShoreNav(int width, int height, int waterFromX)
    {
        var nav = new NavGrid(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = waterFromX; x < width; x++)
            {
                nav.SetBlocked(x, y, true);
            }
        }
        return nav;
    }

    private static GameSim NewShoreSim(long seed)
    {
        TerrainGrid terrain = ShoreTerrain(16, 8, 8);
        NavGrid nav = ShoreNav(16, 8, 8);
        return new GameSim(nav, seed, 2, 6, null, terrain);
    }

    private static int CellX(SimWorld world, long id) =>
        (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.X));

    private static int CellY(SimWorld world, long id) =>
        (int)Fixed.ToInt(world.GetValue(id, SimComponents.Position, SimComponents.Y));

    private static void CheckLoadSailUnload(List<string> failures)
    {
        GameSim sim = NewShoreSim(Seed);
        long ship = sim.SpawnTransportShip(8, 4, 0);
        long unit = sim.SpawnUnit(7, 4, 0, UnitDefs.Swordsman);
        Expect(failures, ship >= 0 && unit >= 0, "transport and passenger spawn");

        bool loaded = sim.ApplyCommand(new LockstepCommand(
            player: 0,
            kind: LockstepCommand.KindTransportLoad,
            entity: unit,
            targetEntity: ship));
        Expect(failures, loaded, "load command accepted at shore");
        Expect(failures, !sim.World.HasComponent(unit, SimComponents.Position),
            "boarded unit leaves world position");
        Expect(failures, sim.World.HasComponent(unit, SimComponents.Aboard),
            "boarded unit carries aboard marker");
        Expect(failures, TransportSystem.Count(sim.World, ship) == 1, "ship counts one passenger");

        sim.IssueMove(ship, 12, 4);
        for (int tick = 0; tick < 120; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, CellX(sim.World, ship) >= 11, "transport sails across water");
        Expect(failures, sim.IssueTransportUnload(ship) == 0, "unload rejected away from shore");
        Expect(failures, TransportSystem.Count(sim.World, ship) == 1,
            "passenger stays aboard after rejected unload");

        sim.IssueMove(ship, 8, 4);
        for (int tick = 0; tick < 160; tick++)
        {
            sim.SimulateTick();
        }
        int unloaded = sim.ApplyCommand(new LockstepCommand(
            player: 0,
            kind: LockstepCommand.KindTransportUnload,
            entity: ship)) ? 0 : -1;
        Expect(failures, unloaded == 0 && TransportSystem.Count(sim.World, ship) == 0,
            "unload command clears ship count");
        Expect(failures, sim.World.HasComponent(unit, SimComponents.Position),
            "unloaded unit regains world position");
        Expect(failures, !sim.World.HasComponent(unit, SimComponents.Aboard),
            "unloaded unit drops aboard marker");
        Expect(failures, CellX(sim.World, unit) < 8 && CellY(sim.World, unit) >= 0,
            "unloaded unit lands on shore");
    }

    private static void CheckRejections(List<string> failures)
    {
        GameSim sim = NewShoreSim(Seed + 1);
        long ship = sim.SpawnTransportShip(8, 4, 0, capacity: 2);
        long first = sim.SpawnUnit(7, 4, 0, UnitDefs.Swordsman);
        long second = sim.SpawnUnit(7, 4, 0, UnitDefs.Spearman);
        long third = sim.SpawnUnit(7, 4, 0, UnitDefs.Archer);
        Expect(failures, sim.IssueTransportLoad(first, ship), "first passenger boards");
        Expect(failures, sim.IssueTransportLoad(second, ship), "second passenger boards");
        Expect(failures, !sim.IssueTransportLoad(third, ship), "over capacity passenger rejected");
        Expect(failures, TransportSystem.Count(sim.World, ship) == 2, "ship count capped at capacity");

        long enemy = sim.SpawnUnit(7, 5, 1, UnitDefs.Swordsman, CombatStance.NoAttack);
        Expect(failures, !sim.IssueTransportLoad(enemy, ship), "enemy unit cannot board");
        Expect(failures,
            !sim.World.HasComponent(enemy, SimComponents.Aboard),
            "enemy unit stays off ship");

        long boat = sim.SpawnFishingShip(10, 4, 0);
        Expect(failures, boat >= 0, "fishing ship spawns for illegal-load check");
        Expect(failures, !sim.IssueTransportLoad(boat, ship), "water unit cannot board transport");

        long farUnit = sim.SpawnUnit(2, 2, 0, UnitDefs.Swordsman);
        Expect(failures, !sim.IssueTransportLoad(farUnit, ship), "distant unit cannot board");

        TerrainGrid openWater = TerrainGrid.Flat(16, 8, TerrainGrid.TypeWater);
        var openNav = new NavGrid(16, 8);
        var offshoreSim = new GameSim(openNav, Seed + 1, 2, 6, null, openWater);
        long offShore = offshoreSim.SpawnTransportShip(8, 4, 0);
        long midWaterUnit = offshoreSim.SpawnUnit(8, 4, 0, UnitDefs.Swordsman);
        Expect(failures, offShore >= 0, "offshore transport spawns");
        Expect(failures, !offshoreSim.IssueTransportLoad(midWaterUnit, offShore),
            "loading rejected when ship is not at shore or dock");
    }

    private static void CheckSinkDropsPassengers(List<string> failures)
    {
        GameSim sim = NewShoreSim(Seed + 2);
        long ship = sim.SpawnTransportShip(8, 4, 0, maxHp: 20);
        long first = sim.SpawnUnit(7, 4, 0, UnitDefs.Swordsman);
        long second = sim.SpawnUnit(7, 4, 0, UnitDefs.Archer);
        Expect(failures, sim.IssueTransportLoad(first, ship), "first passenger boards doomed ship");
        Expect(failures, sim.IssueTransportLoad(second, ship), "second passenger boards doomed ship");

        long raider = sim.SpawnWarship(13, 4, 1);
        sim.IssueAttack(raider, ship);
        for (int tick = 0; tick < 300 && sim.World.IsAlive(ship); tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures, !sim.World.IsAlive(ship), "transport is sunk by warship fire");
        Expect(failures, !sim.World.IsAlive(first) && !sim.World.IsAlive(second),
            "passengers are lost with the sunk transport");
    }

    private static void CheckDeterminism(List<string> failures)
    {
        long first = RunSequence(Seed + 3);
        long second = RunSequence(Seed + 3);
        Expect(failures, first != 0 && first == second,
            "transport load/sail/unload checksum is deterministic");
    }

    private static long RunSequence(long seed)
    {
        GameSim sim = NewShoreSim(seed);
        long ship = sim.SpawnTransportShip(8, 4, 0);
        long unit = sim.SpawnUnit(7, 4, 0, UnitDefs.Swordsman);
        sim.IssueTransportLoad(unit, ship);
        sim.IssueMove(ship, 12, 4);
        for (int tick = 0; tick < 80; tick++)
        {
            sim.SimulateTick();
        }
        sim.IssueTransportUnload(ship);
        for (int tick = 0; tick < 40; tick++)
        {
            sim.SimulateTick();
        }
        return sim.Checksum();
    }
}
