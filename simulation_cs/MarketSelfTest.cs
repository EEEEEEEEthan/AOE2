using System.Collections.Generic;
using AoeSim;

namespace AOE2Framework;

/// <summary>市场买卖、进贡与陆路贸易车的语义、校验和、快照与确定性自测；返回空列表表示全过。</summary>
public static class MarketSelfTest
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        CheckBuy(failures);
        CheckSell(failures);
        CheckBuySellRejections(failures);
        CheckTribute(failures);
        CheckTradeRoute(failures);
        CheckTradeRouteRejections(failures);
        CheckCommandDispatch(failures);
        CheckSnapshotExposesRates(failures);
        CheckDeterministic(failures);
        return failures;
    }

    private static void Expect(List<string> failures, bool condition, string message)
    {
        if (!condition)
        {
            failures.Add(message);
        }
    }

    private static long BuildMarket(GameSim sim, int cellX, int cellY, long team)
    {
        sim.AddResource(team, GameSim.ResourceWood, BuildingTypes.Cost(BuildingTypes.Market));
        long id = sim.IssuePlaceBuilding(BuildingTypes.Market, cellX, cellY, team);
        BuildingSystem.Complete(sim.World, id);
        return id;
    }

    private static void CheckBuy(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 1201, 2, 6);
        BuildMarket(sim, 4, 4, 0);
        sim.AddResource(0, GameSim.ResourceGold, 1000);
        long cost = MarketSystem.BuyCost(sim.World, 0, GameSim.ResourceWood, 100);
        long rateBefore = MarketSystem.Rate(sim.World, 0, GameSim.ResourceWood);
        Expect(failures, MarketSystem.Buy(sim, 0, GameSim.ResourceWood, 100), "buy succeeds with market and gold");
        Expect(failures, sim.Resource(0, GameSim.ResourceGold) == 1000 - cost, "buy deducts gold cost");
        Expect(failures, sim.Resource(0, GameSim.ResourceWood) == 100, "buy delivers resource");
        Expect(failures,
            MarketSystem.Rate(sim.World, 0, GameSim.ResourceWood) > rateBefore,
            "buy raises market rate");
    }

    private static void CheckSell(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 1202, 2, 6);
        BuildMarket(sim, 4, 4, 0);
        sim.AddResource(0, GameSim.ResourceWood, 300);
        long revenue = MarketSystem.SellRevenue(sim.World, 0, GameSim.ResourceWood, 100);
        long rateBefore = MarketSystem.Rate(sim.World, 0, GameSim.ResourceWood);
        long goldBefore = sim.Resource(0, GameSim.ResourceGold);
        Expect(failures, MarketSystem.Sell(sim, 0, GameSim.ResourceWood, 100), "sell succeeds with stock");
        Expect(failures, sim.Resource(0, GameSim.ResourceWood) == 200, "sell removes resource");
        Expect(failures, sim.Resource(0, GameSim.ResourceGold) == goldBefore + revenue, "sell pays gold");
        Expect(failures,
            MarketSystem.Rate(sim.World, 0, GameSim.ResourceWood) < rateBefore,
            "sell lowers market rate");
    }

    private static void CheckBuySellRejections(List<string> failures)
    {
        var withoutMarket = new GameSim(new NavGrid(16, 16), 1203, 2, 6);
        withoutMarket.AddResource(0, GameSim.ResourceGold, 500);
        Expect(failures,
            !MarketSystem.Buy(withoutMarket, 0, GameSim.ResourceWood, 100),
            "buy without a completed market is rejected");
        Expect(failures,
            !MarketSystem.Sell(withoutMarket, 0, GameSim.ResourceWood, 100),
            "sell without a completed market is rejected");

        var sim = new GameSim(new NavGrid(20, 20), 1204, 2, 6);
        BuildMarket(sim, 4, 4, 0);
        long checksum = sim.World.Checksum();
        Expect(failures, !MarketSystem.Buy(sim, 0, GameSim.ResourceWood, 100), "buy without gold is rejected");
        Expect(failures, !MarketSystem.Sell(sim, 0, GameSim.ResourceWood, 100), "sell without stock is rejected");
        Expect(failures, !MarketSystem.Buy(sim, 0, GameSim.ResourceWood, 0), "zero amount buy is rejected");
        Expect(failures, !MarketSystem.Buy(sim, 0, 9, 100), "unknown resource buy is rejected");
        Expect(failures, sim.World.Checksum() == checksum, "rejected trades leave state unchanged");
    }

    private static void CheckTribute(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(16, 16), 1205, 3, 6);
        sim.AddResource(0, GameSim.ResourceFood, 500);
        Expect(failures, MarketSystem.Tribute(sim, 0, 1, GameSim.ResourceFood, 200), "tribute transfers to target");
        Expect(failures, sim.Resource(0, GameSim.ResourceFood) == 300, "tribute deducts source stock");
        Expect(failures, sim.Resource(1, GameSim.ResourceFood) == 200, "tribute credits target stock");
        long checksum = sim.World.Checksum();
        Expect(failures, !MarketSystem.Tribute(sim, 0, 1, GameSim.ResourceFood, 1000), "over-stock tribute rejected");
        Expect(failures, !MarketSystem.Tribute(sim, 0, 5, GameSim.ResourceFood, 10), "out-of-range target rejected");
        Expect(failures, !MarketSystem.Tribute(sim, 0, 0, GameSim.ResourceFood, 10), "self tribute rejected");
        Expect(failures, sim.World.Checksum() == checksum, "rejected tributes leave state unchanged");
    }

    private static void CheckTradeRoute(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 1206, 2, 6);
        long firstMarket = BuildMarket(sim, 2, 2, 0);
        long secondMarket = BuildMarket(sim, 14, 14, 1);
        long cart = sim.SpawnTradeCart(7, 3, 0);
        Expect(failures, sim.IssueTradeRoute(cart, firstMarket, secondMarket), "trade route accepts two markets");
        long goldBefore = sim.Resource(0, GameSim.ResourceGold);
        for (int tick = 0; tick < 600; tick++)
        {
            sim.SimulateTick();
        }
        Expect(failures,
            sim.Resource(0, GameSim.ResourceGold) > goldBefore,
            "trade cart earns gold after travelling between markets");
        Expect(failures,
            sim.World.HasComponent(cart, SimComponents.Trade),
            "trade cart keeps its route while running");
    }

    private static void CheckTradeRouteRejections(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(24, 24), 1207, 2, 6);
        long ownMarket = BuildMarket(sim, 2, 2, 0);
        long foreignMarket = BuildMarket(sim, 14, 14, 1);
        long cart = sim.SpawnTradeCart(7, 3, 0);
        Expect(failures,
            !sim.IssueTradeRoute(cart, foreignMarket, ownMarket),
            "trade route rejects a foreign home market");
        Expect(failures, !sim.IssueTradeRoute(cart, ownMarket, ownMarket), "trade route rejects identical markets");
        Expect(failures,
            sim.World.GetValue(cart, SimComponents.Trade, SimComponents.TradeTarget) == -1,
            "rejected route leaves the cart unassigned");
    }

    private static void CheckCommandDispatch(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 1208, 2, 6);
        BuildMarket(sim, 4, 4, 0);
        sim.AddResource(0, GameSim.ResourceGold, 500);
        var buy = new LockstepCommand(
            player: 0, kind: LockstepCommand.KindMarketBuy, cellX: 100, aux: GameSim.ResourceWood);
        Expect(failures, sim.ApplyCommand(buy), "market buy command is accepted");
        Expect(failures, sim.Resource(0, GameSim.ResourceWood) == 100, "market buy command moves resources");
        long checksum = sim.World.Checksum();
        var outOfRange = new LockstepCommand(
            player: 7, kind: LockstepCommand.KindMarketBuy, cellX: 100, aux: GameSim.ResourceWood);
        Expect(failures, !sim.ApplyCommand(outOfRange), "market buy command from unknown player is rejected");
        Expect(failures, sim.World.Checksum() == checksum, "rejected market command leaves state unchanged");
    }

    private static void CheckSnapshotExposesRates(List<string> failures)
    {
        var sim = new GameSim(new NavGrid(20, 20), 1209, 2, 6, MatchConfig.Standard);
        BuildMarket(sim, 4, 4, 0);
        sim.AddResource(0, GameSim.ResourceGold, 500);
        MarketSystem.Buy(sim, 0, GameSim.ResourceWood, 100);
        SnapshotFrame frame = SnapshotFrame.Build(sim.World, sim.Lockstep.Turn, sim.Match);
        SnapshotFrame decoded = SnapshotFrame.Decode(frame.Encode());
        Expect(failures,
            frame.Players[0].MarketWood == MarketSystem.Rate(sim.World, 0, GameSim.ResourceWood),
            "snapshot exposes traded market rate");
        Expect(failures,
            frame.Players[1].MarketWood == MarketSystem.BaseRate,
            "snapshot defaults untouched market rate");
        Expect(failures,
            decoded.Players[0].MarketWood == frame.Players[0].MarketWood
            && decoded.Players[1].MarketWood == frame.Players[1].MarketWood,
            "snapshot roundtrip keeps market rates");
    }

    private static void CheckDeterministic(List<string> failures)
    {
        Expect(failures, MarketRunChecksum() == MarketRunChecksum(), "market run is deterministic");
        Expect(failures, MarketRunChecksum() != 0, "market checksum is populated");
    }

    private static long MarketRunChecksum()
    {
        var sim = new GameSim(new NavGrid(24, 24), 1210, 2, 6);
        long firstMarket = BuildMarket(sim, 2, 2, 0);
        long secondMarket = BuildMarket(sim, 14, 14, 1);
        sim.AddResource(0, GameSim.ResourceGold, 2000);
        sim.AddResource(0, GameSim.ResourceWood, 500);
        MarketSystem.Buy(sim, 0, GameSim.ResourceWood, 300);
        MarketSystem.Sell(sim, 0, GameSim.ResourceWood, 100);
        MarketSystem.Tribute(sim, 0, 1, GameSim.ResourceGold, 50);
        long cart = sim.SpawnTradeCart(7, 3, 0);
        sim.IssueTradeRoute(cart, firstMarket, secondMarket);
        for (int tick = 0; tick < 300; tick++)
        {
            sim.SimulateTick();
        }
        return sim.World.Checksum();
    }
}
