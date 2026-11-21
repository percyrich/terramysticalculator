using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class FavourTests
{
    // The Giants have just upgraded 43 to a temple and owe a favour. Power 2/10/0, 20 victory points, Fire 1.
    // Giants' bank slots empty: dwelling 0 and temple 0 (the trading post is back in slot 0).
    private static readonly Move[] TempleJustBuilt = Concat(BuildingTests.TradingPostBuilt, new[]
    {
        UpgradeTo(G, PieceKind.Temple, 0, 43),
    });

    private static Move Take(int favour) => new TakeFavour(G, favour);

    [Fact]
    public void The_bank_starts_with_every_copy_of_every_favour()
    {
        var bank = GameState.Initial.FavourBank;
        Assert.Equal(12, bank.Count);
        Assert.Equal(new[] { 3, 3, 1, 3, 3, 1, 3, 3, 1, 3, 3, 1 }, bank.Select(stack => stack.Count));
    }

    [Fact]
    public void Building_a_temple_owes_a_favour()
    {
        var result = Rules.Apply(Play(BuildingTests.TradingPostBuilt), UpgradeTo(G, PieceKind.Temple, 0, 43));
        Assert.Equal(1, result.State[G].FavoursOwed);
        Assert.Contains<GameEvent>(new FavoursEarned(G, 1), result.Events);
    }

    [Fact]
    public void The_turn_cannot_end_until_the_favour_is_taken()
    {
        var result = Rules.Apply(Play(TempleJustBuilt), End(G));
        Assert.Equal("The Giants must take their favour before ending their turn.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Taking_a_favour_moves_the_top_tile_to_the_player()
    {
        var s = PlayFrom(Play(TempleJustBuilt), Take(0));

        Assert.Equal(2, s.FavourBank[0].Count);
        Assert.Equal(ValueList.Of(new FavourTile(0, 2)), s[G].FavourTiles);
        Assert.Equal(0, s[G].FavoursOwed);
        Assert.True(Rules.Apply(s, End(G)).Accepted);
    }

    [Fact]
    public void Favour_steps_move_up_the_track_with_its_rewards()
    {
        var result = Rules.Apply(Play(TempleJustBuilt), Take(2)); // Fire +3: 1 to 4, past the reward on step 3

        Assert.Equal(4, result.State[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(new Power(1, 11, 0), result.State[G].Power);
    }

    [Fact]
    public void A_favour_cannot_be_taken_unless_one_is_owed()
    {
        var result = Rules.Apply(Play(BuildingTests.TradingPostBuilt), Take(0));
        Assert.Equal("The Giants have no favour to take.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Nobody_holds_two_of_the_same_favour()
    {
        var s = Play(Concat(BuildingTests.TempleBuilt, new[] { UpgradeTo(G, PieceKind.Sanctuary, 0, 43) })); // holds favour 1
        Assert.Equal("The Giants already have that favour.", Assert.Single(Rules.Apply(s, Take(1)).Issues).Message);
        Assert.True(Rules.Apply(s, Take(4)).Accepted);
    }

    [Fact]
    public void A_favour_that_has_run_out_cannot_be_taken()
    {
        var s = Play(TempleJustBuilt);
        s = s with { FavourBank = s.FavourBank.SetItem(2, ValueList<FavourTile>.Empty) };
        Assert.Equal("That favour has run out.", Assert.Single(Rules.Apply(s, Take(2)).Issues).Message);
    }

    [Fact]
    public void With_no_favour_left_to_take_none_is_owed()
    {
        var s = Play(BuildingTests.TradingPostBuilt);
        s = s with { FavourBank = ValueList<ValueList<FavourTile>>.Repeat(ValueList<FavourTile>.Empty, Favours.Count) };

        var result = Rules.Apply(s, UpgradeTo(G, PieceKind.Temple, 0, 43));

        Assert.True(result.Accepted);
        Assert.Equal(0, result.State[G].FavoursOwed);
        Assert.Contains(result.Issues, issue => issue.Severity == Severity.Note
            && issue.Message.StartsWith("There are not enough favours left"));
        Assert.True(Rules.Apply(result.State, End(G)).Accepted);
    }

    [Theory]
    [InlineData(0, 3, 2, 1, 3)]  // +3 coins
    [InlineData(7, 0, 3, 1, 4)]  // +1 worker, +1 power
    [InlineData(10, 0, 2, 1, 7)] // +4 power
    [InlineData(1, 0, 2, 1, 3)]  // steps only
    public void Some_favours_add_income(int favour, int coins, int workers, int priests, int power)
    {
        // Without a favour: base 1 worker, dwelling slot 0 gives 1 worker, temple slot 0 gives 1 priest,
        // the pass tile gives 3 power.
        var s = PlayFrom(Play(TempleJustBuilt), Take(favour));
        Assert.Equal(new Income(coins, workers, priests, power), s[G].Income);
    }

    [Fact]
    public void Victory_points_start_at_twenty()
    {
        Assert.Equal(20, Play(SoloStart)[G].VictoryPoints);
    }

    [Fact]
    public void Favour_six_scores_for_each_dwelling_built()
    {
        var s = PlayFrom(Play(TempleJustBuilt), Take(6), End(G));
        var result = Rules.Apply(s, Build(G, 0, 4)); // dwelling 0 is back in slot 1, the leftmost

        Assert.Equal(22, result.State[G].VictoryPoints);
        Assert.Contains<GameEvent>(new VictoryPointsGained(G, 2, 22, new PointsArea.Favour(6), new PointsArea.OnHex(4)), result.Events);
    }

    [Fact]
    public void Favour_three_scores_for_each_trading_post_built()
    {
        var s = PlayFrom(Play(TempleJustBuilt), Take(3), End(G));
        var result = Rules.Apply(s, UpgradeTo(G, PieceKind.TradingPost, 0, 5));
        Assert.Equal(23, result.State[G].VictoryPoints);
    }

    [Fact]
    public void Favour_nine_scores_for_trading_posts_when_passing()
    {
        var withoutTradingPosts = PlayFrom(Play(TempleJustBuilt), Take(9), End(G));
        Assert.Equal(20, Rules.Apply(withoutTradingPosts, new Pass(G)).State[G].VictoryPoints);

        var withOne = PlayFrom(withoutTradingPosts, UpgradeTo(G, PieceKind.TradingPost, 0, 5), End(G));
        Assert.Equal(22, Rules.Apply(withOne, new Pass(G)).State[G].VictoryPoints); // 1 trading post: 2 VP
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 4)] // more than four score as four
    public void Favour_nine_pass_table(int tradingPosts, int points)
    {
        Assert.Equal(points, Favours.All[9].PassVictoryPointsFor(tradingPosts));
    }

    [Fact]
    public void A_tile_describes_its_favour()
    {
        Assert.Equal("Fire +1\nIncome: 3 coins", Favours.Describe(0));
        Assert.Equal("Earth +2\nIncome: 1 worker, 1 power", Favours.Describe(7));
    }
}
