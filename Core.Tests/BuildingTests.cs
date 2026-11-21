using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Temples, strongholds and sanctuaries, all built by upgrading.
public class BuildingTests
{
    // From TradingPostTests.Setup: the Giants upgrade 43 (next to the Witches' 31) to a trading post;
    // the Witches take the 1 power offered (free, so automatically). So does each upgrade of 43 below.
    // The Witches have passed, so it is the Giants' turn again, with 12 coins and 4 workers.
    // Giants' bank slots now empty: dwelling slot 0, nothing else.
    public static readonly Move[] TradingPostBuilt = Concat(TradingPostTests.Setup, new[]
    {
        UpgradeTo(G, PieceKind.TradingPost, 0, 43), End(G),
    });

    // Then a temple on 43, with favour 1 (Fire +2: Fire 1 to 3, 1 power). Giants to act, with 7 coins and 2 workers.
    public static readonly Move[] TempleBuilt = Concat(TradingPostBuilt, new Move[]
    {
        UpgradeTo(G, PieceKind.Temple, 0, 43), new TakeFavour(G, 1), End(G),
    });

    [Fact]
    public void A_trading_post_upgrades_to_a_temple()
    {
        var result = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Temple, 0, 43));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal((Piece?)new Piece(G, PieceKind.Temple, 0), s.Map[43]);
        Assert.Null(s[G].TempleBank[0]);
        // The trading post goes back to the only empty slot of its bank.
        Assert.Equal((Piece?)new Piece(G, PieceKind.TradingPost, 0), s[G].TradingPostBank[0]);
        Assert.Contains<GameEvent>(new BuildingReturned(new Piece(G, PieceKind.TradingPost, 0), 43, 0), result.Events);
    }

    [Fact]
    public void A_temple_costs_five_coins_and_two_workers_anywhere()
    {
        var before = Play(TradingPostBuilt);
        Assert.Equal(new Cost(5, 2), Rules.UpgradeCost(before, PieceKind.Temple, G, 43));
        Assert.Equal(new Cost(5, 2), Rules.UpgradeCost(before, PieceKind.Temple, G, 5)); // no opponent nearby: no surcharge

        var s = Rules.Apply(before, UpgradeTo(G, PieceKind.Temple, 0, 43)).State;
        Assert.Equal(7, s[G].Coins);
        Assert.Equal(2, s[G].Workers);
    }

    [Fact]
    public void A_trading_post_upgrades_to_a_stronghold_for_six_coins_and_four_workers()
    {
        var s = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Stronghold, 0, 43)).State;

        Assert.Equal((Piece?)new Piece(G, PieceKind.Stronghold, 0), s.Map[43]);
        Assert.Null(s[G].StrongholdBank[0]);
        Assert.Equal(6, s[G].Coins);
        Assert.Equal(0, s[G].Workers);
    }

    [Fact]
    public void A_temple_gives_a_priest_and_the_Giants_stronghold_four_power()
    {
        // Base 1 worker; dwelling slot 0 empty: 1 worker; pass tile: 3 power. The trading post is back in its bank.
        var withTemple = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Temple, 0, 43)).State;
        Assert.Equal(new Income(0, 2, 1, 3), withTemple[G].Income);

        var withStronghold = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Stronghold, 0, 43)).State;
        Assert.Equal(new Income(0, 2, 0, 7), withStronghold[G].Income);
    }

    [Fact]
    public void A_temple_upgrades_to_a_sanctuary()
    {
        var result = Rules.Apply(Play(TempleBuilt), UpgradeTo(G, PieceKind.Sanctuary, 0, 43));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal((Piece?)new Piece(G, PieceKind.Sanctuary, 0), s.Map[43]);
        Assert.Equal((Piece?)new Piece(G, PieceKind.Temple, 0), s[G].TempleBank[0]);
        Assert.Equal(1, s[G].Coins);
        Assert.Equal(-2, s[G].Workers);
        // The temple's priest slot is covered again; the sanctuary's slot gives a priest instead.
        Assert.Equal(new Income(0, 2, 1, 3), s[G].Income);
    }

    [Fact]
    public void Each_upgrade_needs_the_building_below_it()
    {
        var s = Play(TradingPostBuilt);
        Assert.Equal("Only a trading post can be upgraded to a temple.",
            Assert.Single(Rules.Apply(s, UpgradeTo(G, PieceKind.Temple, 0, 5)).Issues).Message);
        Assert.Equal("Only a temple can be upgraded to a sanctuary.",
            Assert.Single(Rules.Apply(s, UpgradeTo(G, PieceKind.Sanctuary, 0, 43)).Issues).Message);
        Assert.Equal("There is no trading post there to upgrade.",
            Assert.Single(Rules.Apply(s, UpgradeTo(G, PieceKind.Stronghold, 0, 4)).Issues).Message);
    }

    [Fact]
    public void A_dwelling_is_not_built_by_upgrading()
    {
        var result = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Dwelling, 0, 43));
        Assert.Equal("Only trading posts, temples, strongholds and sanctuaries are built by upgrading.",
            Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void The_only_stronghold_cannot_be_built_twice()
    {
        var s = Play(Concat(TradingPostBuilt, new[]
        {
            UpgradeTo(G, PieceKind.Stronghold, 0, 43), End(G),
            UpgradeTo(G, PieceKind.TradingPost, 0, 5), End(G),
        }));
        Assert.Equal("That stronghold has already been built.",
            Assert.Single(Rules.Apply(s, UpgradeTo(G, PieceKind.Stronghold, 0, 5)).Issues).Message);
    }

    [Fact]
    public void Temples_and_sanctuaries_earn_favours_strongholds_do_not()
    {
        var temple = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Temple, 0, 43)).State;
        Assert.Equal(1, temple[G].FavoursOwed);

        var stronghold = Rules.Apply(Play(TradingPostBuilt), UpgradeTo(G, PieceKind.Stronghold, 0, 43)).State;
        Assert.Equal(0, stronghold[G].FavoursOwed);

        var sanctuary = Rules.Apply(Play(TempleBuilt), UpgradeTo(G, PieceKind.Sanctuary, 0, 43)).State;
        Assert.Equal(1, sanctuary[G].FavoursOwed);
    }

    [Fact]
    public void A_temple_is_offered_exactly_your_trading_posts()
    {
        var s = Play(TradingPostBuilt);
        var temple = new Piece(G, PieceKind.Temple, 0);
        var options = Rules.LegalDestinations(s, temple);

        Assert.Equal(new HexDestination(43), Assert.Single(options.Destinations));
        Assert.Equal(new UpgradeBuilding(temple, 43), Rules.MoveTo(s, temple, new HexDestination(43)));
    }

    [Fact]
    public void A_trading_post_has_two_upgrades()
    {
        Assert.Equal(new[] { PieceKind.Temple, PieceKind.Stronghold }, Buildings.UpgradesOf(PieceKind.TradingPost));
        Assert.Equal(new[] { PieceKind.Sanctuary }, Buildings.UpgradesOf(PieceKind.Temple));
        Assert.Empty(Buildings.UpgradesOf(PieceKind.Sanctuary));
    }
}
