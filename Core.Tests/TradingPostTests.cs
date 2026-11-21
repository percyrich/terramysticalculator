using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Witches start on 31 and 9, Giants on 43 and 5 (Giants' dwelling slots 0 and 1 are empty).
// Hex 43 touches the Witches' 31; hex 5 touches no Witches' building.
// After the Witches pass, it is the Giants' turn with 15 coins and 6 workers.
public class TradingPostTests
{
    public static readonly Move[] Setup = Concat(
        Placing(W, G),
        new[] { StartAt(W, 0, 31), StartAt(G, 0, 43), StartAt(G, 1, 5), StartAt(W, 1, 9) },
        Picks(W, G),
        PassAndEnd(W));

    private static Move Upgrade(Faction f, int number, int hex) =>
        UpgradeTo(f, PieceKind.TradingPost, number, hex);

    [Fact]
    public void Upgrading_replaces_the_dwelling_with_the_leftmost_trading_post()
    {
        var result = Rules.Apply(Play(Setup), Upgrade(G, 0, 43));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal((Piece?)new Piece(G, PieceKind.TradingPost, 0), s.Map[43]);
        Assert.Null(s[G].TradingPostBank[0]);
        Assert.True(s.ActionTaken);
    }

    [Fact]
    public void The_dwelling_returns_to_the_rightmost_empty_slot()
    {
        var result = Rules.Apply(Play(Setup), Upgrade(G, 0, 43));
        var s = result.State;

        // Slots 0 and 1 were empty: the dwelling from hex 43 goes back into slot 1.
        Assert.Null(s[G].DwellingBank[0]);
        Assert.Equal((Piece?)new Piece(G, PieceKind.Dwelling, 0), s[G].DwellingBank[1]);
        Assert.Equal(1, s[G].LeftmostDwellingSlot); // and it is the next one built
        Assert.Contains<GameEvent>(new BuildingReturned(new Piece(G, PieceKind.Dwelling, 0), 43, 1), result.Events);
    }

    [Fact]
    public void Next_to_an_opponent_it_costs_three_coins_and_two_workers()
    {
        var s = Rules.Apply(Play(Setup), Upgrade(G, 0, 43)).State;
        Assert.Equal(12, s[G].Coins);
        Assert.Equal(4, s[G].Workers);
    }

    [Fact]
    public void Away_from_opponents_it_costs_three_coins_more()
    {
        var s = Rules.Apply(Play(Setup), Upgrade(G, 0, 5)).State;
        Assert.Equal(9, s[G].Coins);
        Assert.Equal(4, s[G].Workers);
    }

    [Fact]
    public void Upgrade_cost_query_matches()
    {
        var s = Play(Setup);
        Assert.Equal(new Cost(3, 2), Rules.UpgradeCost(s, PieceKind.TradingPost, G, 43));
        Assert.Equal(new Cost(6, 2), Rules.UpgradeCost(s, PieceKind.TradingPost, G, 5));
    }

    [Fact]
    public void Income_changes_both_ways()
    {
        // Before: base 1 worker, 2 workers (slots 0, 1), 3 power (pass tile). After: trading post slot 0 adds
        // 2 coins and 1 power, and the returned dwelling covers slot 1 again, so 2 workers.
        var s = Rules.Apply(Play(Setup), Upgrade(G, 0, 43)).State;
        Assert.Equal(new Income(2, 2, 0, 4), s[G].Income);
    }

    [Fact]
    public void Neighbours_gain_power_when_a_dwelling_is_upgraded()
    {
        var result = Rules.Apply(Play(Setup), Upgrade(G, 0, 43));
        Assert.Contains<GameEvent>(new PowerOffered(W, 1), result.Events); // the Witches' dwelling on 31

        // 1 power costs nothing, so it is taken without asking.
        Assert.Empty(result.State.PowerOffers);
        Assert.Equal(new Power(1, 11, 0), result.State[W].Power); // 2/10/0 after round 1's income
        Assert.Equal(20, result.State[W].VictoryPoints);
    }

    [Fact]
    public void Only_your_own_dwellings_can_be_upgraded()
    {
        var s = Play(Setup);
        Assert.Equal("That building belongs to the Witches.", Assert.Single(Rules.Apply(s, Upgrade(G, 0, 31)).Issues).Message);
        Assert.Equal("There is no dwelling there to upgrade.", Assert.Single(Rules.Apply(s, Upgrade(G, 0, 4)).Issues).Message);
    }

    [Fact]
    public void A_trading_post_cannot_be_upgraded_again()
    {
        var s = Play(Concat(Setup, new[] { Upgrade(G, 0, 43), End(G) }));
        Assert.False(Rules.Apply(s, Upgrade(G, 1, 43)).Accepted);
    }

    [Fact]
    public void Only_the_leftmost_trading_post_and_only_one_action()
    {
        var s = Play(Setup);
        Assert.False(Rules.Apply(s, Upgrade(G, 1, 43)).Accepted);

        var acted = Play(Concat(Setup, new[] { Upgrade(G, 0, 43) }));
        Assert.False(Rules.Apply(acted, Upgrade(G, 1, 5)).Accepted);
    }

    [Fact]
    public void With_no_trading_posts_left_nothing_can_be_upgraded()
    {
        var s = Play(Setup);
        s = s.WithPlayer(s[G] with { TradingPostBank = ValueList<Piece?>.Repeat(null, Config.TradingPostsPerPlayer) });
        Assert.False(Rules.Apply(s, Upgrade(G, 0, 43)).Accepted);
    }

    [Fact]
    public void A_trading_post_is_offered_exactly_your_dwellings()
    {
        var s = Play(Setup);
        var options = Rules.LegalDestinations(s, new Piece(G, PieceKind.TradingPost, 0));

        Assert.Equal(2, options.Destinations.Count);
        Assert.Contains<Destination>(new HexDestination(43), options.Destinations);
        Assert.Contains<Destination>(new HexDestination(5), options.Destinations);
        Assert.Equal(Upgrade(G, 0, 43), Rules.MoveTo(s, new Piece(G, PieceKind.TradingPost, 0), new HexDestination(43)));
    }
}
