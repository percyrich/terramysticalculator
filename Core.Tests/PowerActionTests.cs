using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Power actions: 0 bridge (3), 1 priest (3), 2 seven coins (4), 3 two workers (4), 4 one spade (4), 5 two spades (6).
// Solo Giants start round 1 with 15 coins, 6 workers, power 2/10/0 (Charged: 0/0/12). They need 2 spades for any transform.
public class PowerActionTests
{
    private static Move Take(Faction f, int action) => new TakePowerAction(f, action);

    private static GameState WithDwelling(GameState s, Faction f, int hex) =>
        s with { Map = s.Map.SetItem(hex, new Piece(f, PieceKind.Dwelling, 60 + hex)) };

    [Fact]
    public void A_power_action_spends_power_and_is_the_action()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), Take(G, 2));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal(22, s[G].Coins);
        Assert.Equal(new Power(4, 0, 8), s[G].Power);
        Assert.True(s.ActionTaken);
        Assert.Equal(ValueList.Of(2), s.PowerActionsTaken);
    }

    [Fact]
    public void Each_power_action_is_taken_by_one_player_per_round()
    {
        var s = PlayFrom(Charged(Play(TwoPlayerStart)), Take(W, 3), End(W));
        Assert.Equal("That power action has already been taken this round.", Assert.Single(Rules.Apply(s, Take(G, 3)).Issues).Message);
        Assert.True(Rules.Apply(s, Take(G, 2)).Accepted);
    }

    [Fact]
    public void Power_actions_are_available_again_next_round()
    {
        var s = PlayFrom(Charged(Play(SoloStart)), Take(G, 2), End(G), PassKeeping(G), End(G));
        Assert.Equal(2, s.Round);
        Assert.Empty(s.PowerActionsTaken);
        Assert.True(Rules.Apply(s, Take(G, 2)).Accepted);
    }

    [Fact]
    public void Power_is_burned_as_needed()
    {
        // 2/10/0: three burns make 2/4/3; spending 3 leaves 5/4/0.
        var result = Rules.Apply(Play(SoloStart), Take(G, 1));

        Assert.True(result.Accepted);
        Assert.Equal(new Power(5, 4, 0), result.State[G].Power);
        Assert.Equal(3, result.Events.OfType<PowerBurned>().Count());
        Assert.Single(result.State[G].Priests);
    }

    [Fact]
    public void Without_enough_power_even_after_burning_it_is_refused()
    {
        var s = Play(SoloStart);
        s = s.WithPlayer(s[G] with { Power = new Power(12, 0, 0) });
        Assert.False(Rules.Apply(s, Take(G, 1)).Accepted);
    }

    [Fact]
    public void The_spade_actions_give_free_spades_to_spend_at_once_and_allow_a_build()
    {
        var s = PlayFrom(Charged(Play(SoloStart)), Take(G, 5));
        Assert.Equal(2, s[G].SpadesOwed);

        s = PlayFrom(s, new SpendSpades(G, 4, Terrain.Wasteland)); // both spades: the Giants always need 2
        Assert.Equal(0, s[G].SpadesOwed);
        Assert.True(Rules.Apply(s, new BuildOnSpadedHex(new Piece(G, PieceKind.Dwelling, 2), 4)).Accepted);
    }

    [Fact]
    public void The_Giants_top_up_a_single_free_spade_in_the_main_phase()
    {
        var result = Rules.Apply(PlayFrom(Charged(Play(SoloStart)), Take(G, 4)), new SpendSpades(G, 4, Terrain.Wasteland));

        Assert.True(result.Accepted);
        Assert.Contains<GameEvent>(new SpadesSpent(G, 4, Terrain.Desert, Terrain.Wasteland, 1, 1), result.Events);
        Assert.Equal(3, result.State[G].Workers); // the paid spade: 3 workers
    }

    // ---------- Bridges ----------
    // Hexes 17 and 31 face each other across the edge between rivers 18 and 30.

    [Fact]
    public void The_bridge_action_gives_a_bridge_to_place_at_once()
    {
        var s = PlayFrom(WithDwelling(Charged(Play(SoloStart)), G, 17), Take(G, 0));
        Assert.Equal(1, s[G].BridgesOwed);
        Assert.Contains((17, 31), Rules.BridgeSpots(s, G));
        Assert.Equal("The Giants must place their bridge first.", Assert.Single(Rules.Apply(s, End(G)).Issues).Message);
        Assert.False(Rules.IsInRange(s, G, 31));

        s = PlayFrom(s, new PlaceBridge(G, 31, 17));
        Assert.Equal(ValueList.Of(new Bridge(G, 17, 31)), s.Bridges);
        Assert.Equal(0, s[G].BridgesOwed);
        Assert.Equal(Config.BridgesPerPlayer - 1, s.BridgesLeft(G));
        Assert.True(Rules.IsInRange(s, G, 31)); // across the bridge counts as touching
    }

    [Fact]
    public void A_bridge_needs_a_bridge_edge_and_a_building_at_one_end()
    {
        var s = PlayFrom(WithDwelling(Charged(Play(SoloStart)), G, 17), Take(G, 0));
        Assert.Equal("A bridge must join two land hexes across the edge between two river hexes.",
            Assert.Single(Rules.Apply(s, new PlaceBridge(G, 4, 6)).Issues).Message);
        Assert.Equal("The Giants need a building at one end of the bridge.",
            Assert.Single(Rules.Apply(s, new PlaceBridge(G, 6, 31)).Issues).Message);
    }

    [Fact]
    public void With_nowhere_for_a_bridge_the_action_is_wasted()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), Take(G, 0)); // no building next to a bridge edge

        Assert.True(result.Accepted);
        Assert.Equal(Severity.Note, Assert.Single(result.Issues).Severity);
        Assert.Equal(0, result.State[G].BridgesOwed);
        Assert.Equal(ValueList.Of(0), result.State.PowerActionsTaken);
    }

    [Fact]
    public void Each_player_has_three_bridges()
    {
        var s = WithDwelling(Charged(Play(SoloStart)), G, 17);
        s = s with { Bridges = ValueList.Of(new Bridge(G, 2, 27), new Bridge(G, 13, 27), new Bridge(G, 16, 27)) };
        Assert.Equal(0, s.BridgesLeft(G));
        Assert.Equal(0, Rules.Apply(s, Take(G, 0)).State[G].BridgesOwed); // wasted
    }

    [Fact]
    public void A_bridge_joins_buildings_into_a_town()
    {
        var s = Play(SoloStart);
        s = s with
        {
            Map = s.Map.SetItem(5, new Piece(G, PieceKind.TradingPost, 70)).SetItem(4, new Piece(G, PieceKind.TradingPost, 71))
                .SetItem(17, new Piece(G, PieceKind.Dwelling, 72)).SetItem(31, new Piece(G, PieceKind.TradingPost, 73)),
        };
        Assert.Empty(Rules.FormableTowns(s, G)); // 31 is across the river: only 5 power touching

        s = s with { Bridges = ValueList.Of(new Bridge(G, 17, 31)) };
        Assert.Equal(ValueList.Of(4, 5, 17, 31), Assert.Single(Rules.FormableTowns(s, G)));
    }

    [Fact]
    public void A_bridge_brings_power_from_opponents_building_at_the_other_end()
    {
        // TradingPostTests.Setup: Witches on 31 (and 9), Giants on 43 and 5, the Giants to act.
        var s = Play(TradingPostTests.Setup);
        Assert.False(WitchesOffered(s)); // 17 does not touch 31

        var bridged = s with { Bridges = ValueList.Of(new Bridge(W, 17, 31)) };
        Assert.True(WitchesOffered(bridged));

        // The builder's bridge counts too (as on BGA: 608085870 #326).
        var giantsBridge = s with { Bridges = ValueList.Of(new Bridge(G, 17, 31)) };
        Assert.True(WitchesOffered(giantsBridge));

        static bool WitchesOffered(GameState state) =>
            Rules.Apply(state, Build(G, 2, 17)).Events.Contains(new PowerOffered(W, 1));
    }
}
