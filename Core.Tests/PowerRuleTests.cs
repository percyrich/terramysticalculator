using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Power inside the game: conversions, gains from tracks, and gains from neighbours building.
public class PowerRuleTests
{
    private static GameState WithPower(GameState s, Faction f, Power power) =>
        s.WithPlayer(s[f] with { Power = power });

    // ---------- Conversions (free actions) ----------

    [Fact]
    public void One_power_from_bowl_3_buys_a_coin()
    {
        var s = Charged(Play(SoloStart)); // 15 coins, 0/0/12
        var result = Rules.Apply(s, new ConvertPowerToCoin(G));

        Assert.True(result.Accepted);
        Assert.Equal(16, result.State[G].Coins);
        Assert.Equal(new Power(1, 0, 11), result.State[G].Power);
    }

    [Fact]
    public void A_coin_needs_power_in_bowl_3()
    {
        var result = Rules.Apply(Play(SoloStart), new ConvertPowerToCoin(G)); // 2/10/0
        Assert.False(result.Accepted);
        Assert.Contains("power in bowl 3", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Five_power_from_bowl_3_buys_a_priest()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), new ConvertPowerToPriest(G));

        Assert.True(result.Accepted);
        Assert.Equal(ValueList.Of(new Piece(G, PieceKind.Priest, 0)), result.State[G].Priests);
        Assert.Equal(Config.PriestsPerPlayer - 1, result.State[G].PriestSupply.Count);
        Assert.Equal(new Power(5, 0, 7), result.State[G].Power);
    }

    [Fact]
    public void A_priest_needs_five_in_bowl_3()
    {
        var s = WithPower(Play(SoloStart), G, new Power(0, 8, 4));
        Assert.False(Rules.Apply(s, new ConvertPowerToPriest(G)).Accepted);
    }

    [Fact]
    public void Burning_first_burns_just_enough_for_the_conversion()
    {
        var s = WithPower(Play(SoloStart), G, new Power(0, 4, 1));
        Assert.False(Rules.Apply(s, new ConvertPowerToWorker(G)).Accepted);

        var result = Rules.Apply(s, new ConvertPowerToWorker(G, BurnFirst: true)); // burn twice: 0/0/3, spend 3
        Assert.True(result.Accepted);
        Assert.Equal(new Power(3, 0, 0), result.State[G].Power);
        Assert.Equal(s[G].Workers + 1, result.State[G].Workers);
        Assert.Contains(result.Events, e => e is PowerBurned);
    }

    [Fact]
    public void Burning_first_burns_nothing_when_bowl_3_has_enough()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), new ConvertPowerToPriest(G, BurnFirst: true)); // 0/0/12
        Assert.True(result.Accepted);
        Assert.Equal(new Power(5, 0, 7), result.State[G].Power);
        Assert.DoesNotContain(result.Events, e => e is PowerBurned);
    }

    [Fact]
    public void Burning_first_still_needs_enough_in_bowl_2()
    {
        var s = WithPower(Play(SoloStart), G, new Power(0, 3, 1)); // a worker would need 2 burns: 4 in bowl 2
        var result = Rules.Apply(s, new ConvertPowerToWorker(G, BurnFirst: true));
        Assert.False(result.Accepted);
        Assert.Contains("3 power in bowl 3, burning included,", Assert.Single(result.Issues).Message);
        Assert.Equal(s[G], result.State[G]);
    }

    [Fact]
    public void Burning_moves_one_token_to_bowl_3_and_removes_another()
    {
        var result = Rules.Apply(Play(SoloStart), new BurnPower(G)); // 2/10/0

        Assert.True(result.Accepted);
        Assert.Equal(new Power(2, 8, 1), result.State[G].Power);
    }

    [Fact]
    public void Burning_needs_two_tokens_in_bowl_2()
    {
        var s = WithPower(Play(SoloStart), G, new Power(6, 1, 5));
        Assert.False(Rules.Apply(s, new BurnPower(G)).Accepted);
    }

    [Fact]
    public void Conversions_do_not_use_the_action()
    {
        // 0/4/8: coin -> 1/4/7, burn -> 1/2/8, priest -> 6/2/3.
        var s = PlayFrom(WithPower(Play(SoloStart), G, new Power(0, 4, 8)),
            new ConvertPowerToCoin(G), new BurnPower(G), new ConvertPowerToPriest(G));
        Assert.Equal(new Power(6, 2, 3), s[G].Power);
        Assert.False(s.ActionTaken);
        Assert.True(Rules.Apply(s, Build(G, 2, 4)).Accepted);
    }

    [Fact]
    public void Conversions_are_allowed_after_the_action_and_after_passing()
    {
        Assert.True(Rules.Apply(PlayCharged(SoloStart, Build(G, 2, 4)), new ConvertPowerToCoin(G)).Accepted);
        Assert.True(Rules.Apply(PlayCharged(SoloStart, PassKeeping(G)), new ConvertPowerToCoin(G)).Accepted);
    }

    [Fact]
    public void Conversions_out_of_turn_or_before_round_one_are_refused()
    {
        Assert.False(Rules.Apply(Charged(Play(TwoPlayerStart)), new ConvertPowerToCoin(G)).Accepted); // Witches' turn

        var placing = Play(Placing(G));
        Assert.False(Rules.Apply(placing, new BurnPower(G)).Accepted);
    }

    // ---------- Gains from tracks ----------
    // After GetPriest from 0/0/12 the Giants hold 5/0/7.

    [Fact]
    public void Reaching_step_3_gains_one_power()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        var result = Rules.Apply(s, Place(G, 0, Track.Fire, 0)); // 1 -> 4 (the Giants start with Fire 1)

        Assert.Equal(new Power(4, 1, 7), result.State[G].Power);
        Assert.Contains<GameEvent>(
            new PowerGained(G, PowerSource.Track, 1, new Power(5, 0, 7), new Power(4, 1, 7)), result.Events);
    }

    [Fact]
    public void Passing_two_reward_steps_at_once_gains_both_in_order()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 2) });

        var result = Rules.Apply(s, Place(G, 0, Track.Fire, 0)); // 2 -> 5: steps 3 and 5

        Assert.Equal(new Power(2, 3, 7), result.State[G].Power);
        Assert.Equal(2, result.Events.OfType<PowerGained>().Count());
    }

    [Fact]
    public void Steps_already_passed_give_nothing_again()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 3) });

        var result = Rules.Apply(s, Place(G, 0, Track.Fire, 1)); // 3 -> 5: only step 5

        Assert.Equal(new Power(3, 2, 7), result.State[G].Power);
    }

    [Fact]
    public void Step_10_needs_a_key_and_gains_three()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 8) });

        var noKey = Rules.Apply(s, Place(G, 0, Track.Fire, 1)).State; // 8 -> 9 only
        Assert.Equal(9, noKey[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(new Power(5, 0, 7), noKey[G].Power);

        var withKey = Rules.Apply(s.WithPlayer(s[G] with { Keys = 1 }), Place(G, 0, Track.Fire, 1)).State; // 8 -> 10
        Assert.Equal(10, withKey[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(0, withKey[G].Keys);
        Assert.Equal(new Power(2, 3, 7), withKey[G].Power);
    }

    [Fact]
    public void Undo_takes_back_the_power_gained()
    {
        var history = new History(PlayCharged(SoloStart, GetPriest(G)));
        var before = history.Current;

        history.Apply(Place(G, 0, Track.Fire, 0));
        Assert.NotEqual(before[G].Power, history.Current[G].Power);

        history.Undo();
        Assert.Same(before, history.Current);
    }

    // ---------- Gains from neighbours building ----------
    // Witches start on 31 and 9, Giants on 43 and 5. Hexes 31 and 43 touch, and so do 44 and 31.
    // Neighbours stops before the pass tile picks; NeighboursPlaying goes on to round 1 (power 2/10/0 each).

    private static readonly Move[] Neighbours = Concat(
        Placing(W, G),
        new[] { StartAt(W, 0, 31), StartAt(G, 0, 43), StartAt(G, 1, 5), StartAt(W, 1, 9) });

    private static readonly Move[] NeighboursPlaying = Concat(Neighbours, Picks(W, G));

    [Fact]
    public void Starting_dwellings_give_neighbours_no_power()
    {
        Assert.Equal(Config.StartingPower, Play(Neighbours)[W].Power);
    }

    [Fact]
    public void One_power_from_a_neighbouring_build_is_free_and_taken_without_asking()
    {
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        var result = Rules.Apply(s, Build(G, 2, 44)); // next to the Witches' dwelling on 31 (and the Giants' 43)

        Assert.True(result.Accepted);
        Assert.Contains<GameEvent>(new PowerOffered(W, 1), result.Events); // no offer for your own buildings
        Assert.Contains<GameEvent>(new PowerOfferAnswered(W, true, 1, 0), result.Events);
        Assert.Contains(result.Events, e => e is PowerGained { Faction: Faction.Witches, Source: PowerSource.NeighbouringBuild, Amount: 1 });
        Assert.Empty(result.State.PowerOffers);
        Assert.Equal(new Power(1, 11, 0), result.State[W].Power);
        Assert.Equal(20, result.State[W].VictoryPoints);
        Assert.True(Rules.Apply(result.State, End(G)).Accepted); // nothing to wait for
    }

    [Fact]
    public void Power_offered_is_the_power_value_of_the_buildings_and_costs_one_less_in_VP()
    {
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        s = s with { Map = s.Map.SetItem(31, new Piece(W, PieceKind.Temple, 0)) }; // a temple is worth 2
        s = PlayFrom(s, Build(G, 2, 44));
        Assert.Equal(ValueList.Of(new PowerOffer(W, 2, 44, G)), s.PowerOffers);
        Assert.Equal(new Power(2, 10, 0), s[W].Power); // nothing until accepted

        var accepted = PlayFrom(s, Accept(W));
        Assert.Equal(new Power(0, 12, 0), accepted[W].Power);
        Assert.Equal(19, accepted[W].VictoryPoints);
    }

    [Fact]
    public void No_more_power_is_taken_than_the_VP_pay_for()
    {
        // As on BGA (table 171020282 #186: the Witches at 0 VP take 1 of 3, for nothing).
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        s = s with { Map = s.Map.SetItem(31, new Piece(W, PieceKind.Temple, 0)) }; // 2 offered
        s = s.WithPlayer(s[W] with { VictoryPoints = 0 });
        var built = PlayFrom(s, Build(G, 2, 44));
        var after = built.PowerOffers.Count > 0 ? PlayFrom(built, Accept(W)) : built;
        Assert.Equal(new Power(1, 11, 0), after[W].Power); // 1 power, free
        Assert.Equal(0, after[W].VictoryPoints);
    }

    [Fact]
    public void When_only_one_power_fits_the_offer_is_free_and_taken_without_asking()
    {
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        s = s with { Map = s.Map.SetItem(31, new Piece(W, PieceKind.Temple, 0)) }; // 2 offered
        s = WithPower(s, W, new Power(0, 1, 11)); // room for 1 more
        var built = PlayFrom(s, Build(G, 2, 44));

        Assert.Empty(built.PowerOffers);
        Assert.Equal(new Power(0, 0, 12), built[W].Power);
        Assert.Equal(20, built[W].VictoryPoints); // 1 power costs nothing
    }

    [Fact]
    public void A_free_offer_waits_behind_an_earlier_offer_that_needs_an_answer()
    {
        // Order Witches, Giants, Mermaids: after the Giants build, the Mermaids are offered power first.
        var s = Play(Concat(ThreePlayerStart, PassAndEnd(W)));
        int witchesHex = s.Layout.Neighbours(4).First(n => n != 3 && s.Map[n] is null && s.HexTerrain[n] != Terrain.River);
        s = s with
        {
            Map = s.Map.SetItem(3, new Piece(M, PieceKind.Temple, 0)) // 2 for the Mermaids: needs an answer
                .SetItem(witchesHex, new Piece(W, PieceKind.Dwelling, 7)), // 1 for the Witches: free
        };
        var built = PlayFrom(s, Build(G, 2, 4));
        Assert.Equal(ValueList.Of(new PowerOffer(M, 2, 4, G), new PowerOffer(W, 1, 4, G)), built.PowerOffers);
        Assert.Equal(s[W].Power, built[W].Power);

        var answered = Rules.Apply(built, Decline(M));
        Assert.Empty(answered.State.PowerOffers);
        Assert.Equal(s[W].Power.Gain(1, out _), answered.State[W].Power);
        Assert.Contains<GameEvent>(new PowerOfferAnswered(W, true, 1, 0), answered.Events);
    }

    [Fact]
    public void A_power_offer_can_be_declined_but_the_turn_waits_for_an_answer()
    {
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        s = s with { Map = s.Map.SetItem(31, new Piece(W, PieceKind.Temple, 0)) }; // 2 offered: not free
        s = PlayFrom(s, Build(G, 2, 44));
        Assert.Equal("Waiting for the Witches to accept or decline power.", Assert.Single(Rules.Apply(s, End(G)).Issues).Message);

        var declined = PlayFrom(s, Decline(W));
        Assert.Equal(new Power(2, 10, 0), declined[W].Power);
        Assert.True(Rules.Apply(declined, End(G)).Accepted);
    }

    [Fact]
    public void Building_away_from_opponents_gives_no_power()
    {
        var s = Play(Concat(NeighboursPlaying, PassAndEnd(W)));
        var result = Rules.Apply(s, Build(G, 2, 4)); // next to 5, far from the Witches

        Assert.Equal(new Power(2, 10, 0), result.State[W].Power);
    }
}
