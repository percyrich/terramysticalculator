using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class RoundTests
{
    [Fact]
    public void End_turn_needs_an_action_first()
    {
        var result = Rules.Apply(Ruled(Play(SoloStart)), End(G));
        Assert.False(result.Accepted);
        Assert.Contains("must take an action", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void End_turn_hands_over_to_the_next_player()
    {
        var s = Play(Concat(TwoPlayerStart, new[] { Build(W, 2, 8), End(W) }));
        Assert.Equal(G, s.ActivePlayer);
        Assert.False(s.ActionTaken);
    }

    [Fact]
    public void Passing_is_the_action_but_the_turn_lasts_until_end_turn()
    {
        var s = Play(Concat(TwoPlayerStart, new Move[] { new Pass(W, 0) }));
        Assert.Equal(W, s.ActivePlayer);
        Assert.True(s.ActionTaken);
        Assert.Equal(ValueList.Of(W), s.PassOrder);

        s = PlayFrom(s, new BurnPower(W), End(W)); // a conversion after passing, then end
        Assert.Equal(G, s.ActivePlayer);
    }

    [Fact]
    public void Passed_players_are_skipped_and_pass_order_sets_next_turn_order()
    {
        // Order Witches, Giants, Mermaids.
        var s = Play(Concat(ThreePlayerStart, PassAndEnd(W), new[] { Build(G, 2, 4), End(G) })); // 4 touches the Mermaids' 3 (1 free power)
        Assert.Equal(M, s.ActivePlayer);

        s = PlayFrom(s, PassAndEnd(M));
        Assert.Equal(G, s.ActivePlayer); // Witches have passed, so skipped

        s = PlayFrom(s, PassAndEnd(G));  // everyone has now passed
        Assert.Equal(2, s.Round);
        Assert.Equal(ValueList.Of(W, M, G), s.TurnOrder);
        Assert.Empty(s.PassOrder);
        Assert.Equal(W, s.ActivePlayer);
    }

    [Fact]
    public void A_passed_player_cannot_act_again_this_round()
    {
        var s = Play(Concat(TwoPlayerStart, PassAndEnd(W)));
        Assert.Equal(G, s.ActivePlayer);
        Assert.False(Rules.Apply(s, new Pass(W)).Accepted);
    }

    [Fact]
    public void The_last_player_still_in_keeps_the_turn()
    {
        var s = Play(Concat(TwoPlayerStart, PassAndEnd(W), new[] { Build(G, 2, 4), End(G) }));
        Assert.Equal(G, s.ActivePlayer);
    }

    [Fact]
    public void Turn_queue_starts_with_the_active_player_and_rotates_at_end_of_turn()
    {
        var s = Play(ThreePlayerStart);                           // Witches, Giants, Mermaids
        Assert.Equal(ValueList.Of(W, G, M), s.TurnQueue);

        s = PlayFrom(s, Build(W, 2, 8));                          // acted, but turn not over
        Assert.Equal(ValueList.Of(W, G, M), s.TurnQueue);

        s = PlayFrom(s, End(W));                                  // now the Witches go to the bottom
        Assert.Equal(ValueList.Of(G, M, W), s.TurnQueue);
    }

    [Fact]
    public void A_passing_player_stays_on_top_until_their_turn_ends()
    {
        var s = Play(Concat(ThreePlayerStart, new Move[] { new Pass(W, 1) }));
        Assert.Equal(ValueList.Of(W, G, M), s.TurnQueue);         // still the Witches' turn
        Assert.Equal(ValueList.Of(W), s.PassOrder);

        s = PlayFrom(s, End(W));
        Assert.Equal(ValueList.Of(G, M), s.TurnQueue);
    }

    [Fact]
    public void Turn_queue_is_empty_when_nobody_is_acting()
    {
        Assert.Empty(Play(ThreePlayersConfirmed).TurnQueue);
    }

    [Fact]
    public void The_game_ends_when_everyone_passes_in_round_six()
    {
        var round6 = Play(Concat(SoloStart, PassAndEnd(G, G, G, G, G)));
        Assert.Equal(6, round6.Round);

        var result = Rules.Apply(PlayFrom(round6, new Pass(G)), End(G));
        var s = result.State;
        Assert.Equal(Phase.Finished, s.Phase);
        Assert.Equal(6, s.Round);
        Assert.Null(s.ActivePlayer);
        Assert.Contains(result.Events, e => e is GameEnded);
        Assert.False(Rules.Apply(s, new Pass(G)).Accepted);
    }

    // Round 6 with the Witches to pass first, every resource cleared, both on 50 VP and 2 steps on a track of
    // their own (Air, Earth): final scoring then gives both the same (no coins; 8 for their track; a largest
    // group of 1 each: (18 + 12) / 2 = 15).
    private static GameState EvenRoundSix()
    {
        var s = Play(Concat(TwoPlayerStart, Enumerable.Range(0, Config.Rounds - 1).SelectMany(_ => PassAndEnd(W, G)).ToArray()));
        foreach (var f in new[] { W, G })
            s = s.WithPlayer(s[f] with
            {
                Coins = 0, Workers = 0, Power = new Power(12, 0, 0), VictoryPoints = 50,
                TrackSteps = f == W ? ValueList.Of(0, 0, 0, 2) : ValueList.Of(0, 0, 2, 0),
            });
        return s;
    }

    [Fact]
    public void Most_victory_points_wins()
    {
        var s = EvenRoundSix();
        s = s.WithPlayer(s[W] with { VictoryPoints = 51 });
        s = PlayFrom(s, PassAndEnd(W, G));

        Assert.Equal(Phase.Finished, s.Phase);
        Assert.Equal(74, s[W].VictoryPoints);
        Assert.Equal(73, s[G].VictoryPoints);
        Assert.Equal(ValueList.Of(W), s.Winners);
    }

    [Fact]
    public void Tied_players_all_win()
    {
        var s = PlayFrom(EvenRoundSix(), PassAndEnd(W, G));

        Assert.Equal(2, s.Winners.Count);
        Assert.Contains(W, s.Winners);
        Assert.Contains(G, s.Winners);
    }

    // Witches pass first every round, so turn order stays Witches, Giants.
    private static Move[] SixRoundsOfPassing() =>
        Enumerable.Range(0, Config.Rounds).SelectMany(_ => PassAndEnd(W, G)).ToArray();
}
