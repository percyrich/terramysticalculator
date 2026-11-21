using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class EndGameTests
{
    private static readonly int[] Track = { 8, 4, 2 };
    private static readonly int[] Network = { 18, 12, 6 };

    private static GameState Edit(GameState s, Faction f, Func<PlayerState, PlayerState> change) => s.WithPlayer(change(s[f]));

    private static FinalScore ScoreOf(GameState finished, Faction f) => finished.FinalScores.First(score => score.Faction == f);

    private static GameState Finish(GameState s) => EndGame.Finish(s, new List<GameEvent>());

    // ---------- Sharing places ----------

    [Theory]
    [InlineData(new[] { 5, 3, 1 }, new[] { 8, 4, 2 })]
    [InlineData(new[] { 5, 5, 1 }, new[] { 6, 6, 2 })]       // two tied for first
    [InlineData(new[] { 5, 5, 5 }, new[] { 4, 4, 4 })]       // all three tied
    [InlineData(new[] { 5, 3, 3 }, new[] { 8, 3, 3 })]       // tied for second: (4 + 2) / 2
    [InlineData(new[] { 5, 3, 0 }, new[] { 8, 4, 0 })]       // no steps: nothing
    [InlineData(new[] { 5, 3 }, new[] { 8, 4 })]             // two players: no third place
    [InlineData(new[] { 5, 5 }, new[] { 6, 6 })]
    [InlineData(new[] { 9, 5, 5, 5, 5 }, new[] { 8, 1, 1, 1, 1 })] // four tied for second: (4 + 2 + 0 + 0) / 4
    public void Tied_players_share_the_places_they_cover(int[] values, int[] expected)
    {
        Assert.Equal(expected, EndGame.RankPoints(values, Track));
    }

    // ---------- Coins ----------

    [Fact]
    public void Priests_workers_and_power_become_coins_then_one_point_per_three()
    {
        var s = PlayCharged(SoloStart, GetPriest(G)); // 1 priest ready
        s = Edit(s, G, p => p with { Coins = 4, Workers = 2, Power = new Power(5, 5, 2) });
        var score = ScoreOf(Finish(s), G);

        // 4 coins + 3 workers (1 from the priest) + 4 power (2 in bowl 3, 2 more by burning 4) = 11.
        Assert.Equal(11, score.Coins);
        Assert.Equal(3, score.CoinPoints);
    }

    [Fact]
    public void Debt_scores_nothing_for_coins()
    {
        var s = Edit(Play(SoloStart), G, p => p with { Coins = -9, Workers = 0, Power = new Power(12, 0, 0) });
        Assert.Equal(0, ScoreOf(Finish(s), G).CoinPoints);
    }

    // ---------- Tracks ----------

    [Fact]
    public void Each_track_rewards_the_highest_players()
    {
        var s = Play(ThreePlayerStart);
        s = Edit(s, W, p => p with { TrackSteps = ValueList.Of(5, 0, 2, 0) });
        s = Edit(s, G, p => p with { TrackSteps = ValueList.Of(5, 1, 0, 0) });
        s = Edit(s, M, p => p with { TrackSteps = ValueList.Of(1, 0, 0, 0) });
        var finished = Finish(s);

        Assert.Equal(ValueList.Of(6, 0, 8, 0), ScoreOf(finished, W).TrackPoints);
        Assert.Equal(ValueList.Of(6, 8, 0, 0), ScoreOf(finished, G).TrackPoints);
        Assert.Equal(ValueList.Of(2, 0, 0, 0), ScoreOf(finished, M).TrackPoints);
    }

    // ---------- Largest network ----------

    [Fact]
    public void The_largest_group_counts_and_shipping_joins_groups()
    {
        var s = Play(SoloStart); // Giants on 5 and 112
        s = s with { Map = s.Map.SetItem(4, new Piece(G, PieceKind.Dwelling, 2)).SetItem(31, new Piece(G, PieceKind.Dwelling, 3)) };
        Assert.Equal(2, EndGame.LargestNetwork(s, G)); // 4 and 5; 31 is across river 18

        s = Edit(s, G, p => p with { Shipping = 1 });
        Assert.Equal(3, EndGame.LargestNetwork(s, G));
    }

    [Fact]
    public void A_piece_on_a_river_joins_the_buildings_on_either_side()
    {
        // BGA table 242946108: the Riverwalkers' landscape on a river joined their groups. Here: the Giants (no
        // shipping) on 1 and 27, a piece of theirs on river 14 between them; whichever building the count starts from.
        var s = Play(SoloStart);
        s = s with
        {
            Map = s.Map.SetItem(1, new Piece(G, PieceKind.Dwelling, 2)).SetItem(27, new Piece(G, PieceKind.Dwelling, 3))
                .SetItem(14, new Piece(G, PieceKind.Landscape, 0)),
        };
        Assert.Contains(EndGame.Networks(s, G), network => network.Count == 3 && network.Contains(1) && network.Contains(27));
    }

    [Fact]
    public void The_largest_networks_score_eighteen_and_twelve_in_a_two_player_game()
    {
        var s = Play(TwoPlayerStart); // each faction's two starting dwellings are apart: 1 each
        s = s with { Map = s.Map.SetItem(4, new Piece(G, PieceKind.Dwelling, 2)) }; // Giants: 4 and 5
        var finished = Finish(s);

        Assert.Equal(2, ScoreOf(finished, G).Network);
        Assert.Equal(18, ScoreOf(finished, G).NetworkPoints);
        Assert.Equal(12, ScoreOf(finished, W).NetworkPoints);
    }

    // ---------- The whole game ----------

    [Fact]
    public void After_round_six_the_game_ends_with_final_scores()
    {
        var round6 = Play(Concat(SoloStart, PassAndEnd(G, G, G, G, G)));
        var result = Rules.Apply(PlayFrom(round6, new Pass(G)), End(G));
        var s = result.State;

        Assert.Equal(Phase.Finished, s.Phase);
        var score = Assert.Single(s.FinalScores);
        Assert.Equal(score.Total, s[G].VictoryPoints);
        Assert.Equal(score.VictoryPointsBefore + score.CoinPoints + score.TrackPoints.Sum() + score.NetworkPoints, score.Total);
        Assert.Equal(18, score.NetworkPoints); // alone: the largest network
        Assert.Equal(ValueList.Of(G), s.Winners);
        Assert.Contains(result.Events, e => e is GameEnded);
    }
}
