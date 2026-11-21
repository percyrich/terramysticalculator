using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class ShippingTests
{
    private static GameState WithShipping(GameState s, Faction f, int shipping) =>
        s.WithPlayer(s[f] with { Shipping = shipping });

    [Fact]
    public void Only_the_Mermaids_start_with_shipping()
    {
        Assert.Equal(1, PlayerState.New(M).Shipping);
        Assert.Equal(0, PlayerState.New(G).Shipping);
        Assert.Equal(0, PlayerState.New(W).Shipping);
    }

    [Fact]
    public void Upgrading_costs_a_priest_and_four_coins_and_scores_two()
    {
        var s = PlayCharged(SoloStart, GetPriest(G)); // 15 coins
        var result = Rules.Apply(s, new UpgradeShipping(G));

        Assert.True(result.Accepted);
        var after = result.State[G];
        Assert.Equal(1, after.Shipping);
        Assert.Equal(11, after.Coins);
        Assert.Equal(22, after.VictoryPoints);
        Assert.Empty(after.Priests);
        Assert.True(result.State.ActionTaken);
        Assert.Contains<GameEvent>(new ShippingUpgraded(G, 1), result.Events);
    }

    [Fact]
    public void The_Fakirs_and_Dwarves_have_no_shipping_at_all()
    {
        var fakirs = Play(Solo(Faction.Fakirs, 4, 106));
        Assert.Equal("The Fakirs have no shipping to upgrade.",
            Assert.Single(Rules.Apply(fakirs, new UpgradeShipping(Faction.Fakirs)).Issues).Message);

        var dwarves = Play(Solo(Faction.Dwarves, 1, 108));
        Assert.False(Rules.Apply(dwarves, new UpgradeShipping(Faction.Dwarves)).Accepted);

        // Pass tile 5's extra shipping is ignored too.
        var withTile = fakirs.WithPlayer(fakirs[Faction.Fakirs] with { PassTile = 5 });
        Assert.Equal(0, withTile.ShippingOf(Faction.Fakirs));
    }

    [Fact]
    public void Later_upgrades_score_more_up_to_shipping_three()
    {
        var two = WithShipping(Play(SoloStart), G, 2);
        var result = Rules.Apply(two, new UpgradeShipping(G)); // third upgrade: 4 VP
        Assert.Equal(3, result.State[G].Shipping);
        Assert.Equal(24, result.State[G].VictoryPoints);

        var three = WithShipping(Play(SoloStart), G, 3);
        Assert.Equal("The Giants are already at their highest shipping (3).",
            Assert.Single(Rules.Apply(three, new UpgradeShipping(G)).Issues).Message);
    }

    [Fact]
    public void The_Mermaids_can_reach_shipping_five_for_five_points()
    {
        var s = PlayFrom(Play(ThreePlayerStart), PassAndEnd(W, G)); // Mermaids to act, 20 VP
        var result = Rules.Apply(WithShipping(s, M, 4), new UpgradeShipping(M)); // their fourth upgrade

        Assert.Equal(5, result.State[M].Shipping);
        Assert.Equal(25, result.State[M].VictoryPoints);
        Assert.False(Rules.Apply(WithShipping(s, M, 5), new UpgradeShipping(M)).Accepted);
    }

    [Fact]
    public void Pass_tile_five_adds_one_shipping_in_the_main_phase()
    {
        var s = Play(Concat(Placing(G), new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) },
            new Move[] { new PickPassTile(G, 5) }));

        Assert.Equal(0, s[G].Shipping);
        Assert.Equal(1, s.ShippingOf(G));
        Assert.True(Rules.IsInRange(s, G, 31)); // across river 18 from hex 5
        Assert.Equal(0, (s with { Phase = Phase.RoundEndSpades }).ShippingOf(G));
    }

    [Fact]
    public void Pass_tile_nine_scores_three_per_shipping_level_when_passing()
    {
        var s = WithShipping(Play(SoloStart), G, 2); // the Giants hold tile 9
        Assert.Equal(26, Rules.Apply(s, new Pass(G, 0)).State[G].VictoryPoints);
    }
}
