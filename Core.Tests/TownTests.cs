using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Solo Giants (round 1, 15 coins, 6 workers, 20 VP). Around hex 5: 4, 6, 17 all touch 5; 4 and 17 touch too.
// Buildings are put on the map directly; their power values: dwelling 1, trading post / temple 2,
// stronghold / sanctuary 3.
public class TownTests
{
    private static int _nextNumber = 50; // piece numbers for buildings put down by the tests (only IDs)

    private static GameState With(GameState s, params (int Hex, PieceKind Kind)[] buildings)
    {
        foreach (var (hex, kind) in buildings)
            s = s with { Map = s.Map.SetItem(hex, new Piece(G, kind, _nextNumber++)) };
        return s;
    }

    // 2 + 2 + 1 + 2 = 7 power in four buildings: a town.
    private static GameState SevenPower() => With(Play(SoloStart),
        (5, PieceKind.TradingPost), (4, PieceKind.TradingPost), (6, PieceKind.Dwelling), (17, PieceKind.TradingPost));

    [Fact]
    public void Four_touching_buildings_with_seven_power_form_a_town()
    {
        var towns = Rules.FormableTowns(SevenPower(), G);
        Assert.Equal(ValueList.Of(4, 5, 6, 17), Assert.Single(towns));
    }

    [Fact]
    public void Six_power_is_not_enough_without_the_favour()
    {
        var six = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.Dwelling), (6, PieceKind.Dwelling), (17, PieceKind.TradingPost));
        Assert.Empty(Rules.FormableTowns(six, G));

        var withFavour = six.WithPlayer(six[G] with { FavourTiles = ValueList.Of(new FavourTile(1, 0)) }); // Fire +2
        Assert.Single(Rules.FormableTowns(withFavour, G));
    }

    [Fact]
    public void Three_buildings_are_enough_with_the_sanctuary()
    {
        var sanctuary = With(Play(SoloStart), (5, PieceKind.Sanctuary), (4, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        Assert.Single(Rules.FormableTowns(sanctuary, G));

        var stronghold = With(Play(SoloStart), (5, PieceKind.Stronghold), (4, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        Assert.Empty(Rules.FormableTowns(stronghold, G));
    }

    [Fact]
    public void Forming_a_town_places_the_key_token_and_gives_its_rewards()
    {
        var result = Rules.Apply(SevenPower(), new FormTown(G, 0, 5)); // 9 VP, 1 priest, 1 key

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal(29, s[G].VictoryPoints);
        Assert.Equal(1, s[G].Keys);
        Assert.Single(s[G].Priests);
        Assert.Equal(ValueList.Of(4, 5, 6, 17), s[G].TownHexes);
        Assert.Equal(ValueList.Of(new PlacedKey(new KeyTile(0, 1), G, 5)), s.PlacedKeys); // the top copy
        Assert.Single(s.KeyBank[0]);
        Assert.Empty(Rules.FormableTowns(s, G));
    }

    [Fact]
    public void Buildings_already_in_a_town_cannot_form_another()
    {
        var s = PlayFrom(SevenPower(), new FormTown(G, 0, 5));
        s = With(s, (16, PieceKind.TradingPost), (3, PieceKind.TradingPost), (15, PieceKind.TradingPost), (28, PieceKind.TradingPost));
        Assert.Empty(Rules.FormableTowns(s, G)); // joined to the town through 16 and 4
    }

    [Fact]
    public void The_key_token_must_go_on_a_hex_of_the_town()
    {
        var result = Rules.Apply(SevenPower(), new FormTown(G, 0, GiantsStartB));
        Assert.Equal("That hex is not part of a town that can be formed now.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void The_turn_cannot_end_while_a_town_can_be_formed()
    {
        var s = PlayFrom(SevenPower(), new Pass(G, 0));
        Assert.Equal("The Giants must form their town first (place a key token on it).", Assert.Single(Rules.Apply(s, End(G)).Issues).Message);
        Assert.True(Rules.Apply(PlayFrom(s, new FormTown(G, 6, 5)), End(G)).Accepted);
    }

    // As on BGA (931629591 #359): conversions while the town is still to be formed.
    [Fact]
    public void Free_actions_are_allowed_while_a_town_is_still_to_be_formed()
    {
        var s = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        s = PlayFrom(s.WithPlayer(s[G] with { Power = new Power(0, 0, 5) }), Build(G, 2, 17));
        Assert.Single(Rules.FormableTowns(s, G));
        var converted = PlayFrom(s, new ConvertPowerToCoin(G));
        Assert.Equal(s[G].Coins + 1, converted[G].Coins);
        Assert.Equal("The Giants must form their town first (place a key token on it).", Assert.Single(Rules.Apply(converted, End(G)).Issues).Message);
    }

    [Fact]
    public void Building_can_complete_a_town()
    {
        var s = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        Assert.Empty(Rules.FormableTowns(s, G));

        s = PlayFrom(s, Build(G, 2, 17));
        Assert.Equal(ValueList.Of(4, 5, 6, 17), Assert.Single(Rules.FormableTowns(s, G)));
    }

    [Fact]
    public void A_town_s_points_are_set_off_by_the_building_that_made_it_formable()
    {
        var s = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        s = PlayFrom(s, Build(G, 2, 17));
        var result = Rules.Apply(s, new FormTown(G, 0, 5));

        var key = Assert.Single(result.Events.OfType<VictoryPointsGained>());
        Assert.Equal(new PointsArea.KeyOn(5), key.Source);
        Assert.Equal(new PointsArea.OnHex(17), key.Trigger);
        Assert.Empty(result.State.Makers); // forgotten once formed
    }

    [Fact]
    public void A_town_made_formable_otherwise_sets_off_its_points_from_the_key()
    {
        var result = Rules.Apply(SevenPower(), new FormTown(G, 0, 5)); // buildings put down directly: no maker
        var key = Assert.Single(result.Events.OfType<VictoryPointsGained>());
        Assert.Equal(new PointsArea.KeyOn(5), key.Source);
        Assert.Equal(new PointsArea.KeyOn(5), key.Trigger);
    }

    [Fact]
    public void With_no_key_token_left_no_town_forms()
    {
        var s = SevenPower();
        s = s with { KeyBank = ValueList<ValueList<KeyTile>>.Repeat(ValueList<KeyTile>.Empty, KeyTiles.Count) };
        Assert.Empty(Rules.FormableTowns(s, G));
    }

    [Fact]
    public void A_town_round_tile_scores_five_more()
    {
        var setup = Concat(Confirmed(G), SetUpAndStart(new[] { G }, new[] { 5, 0, 1, 2, 3, 4 }),
            new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) }, new Move[] { new PickPassTile(G, 9) });
        var s = With(Play(setup), (5, PieceKind.TradingPost), (4, PieceKind.TradingPost), (6, PieceKind.Dwelling), (17, PieceKind.TradingPost));

        Assert.Equal(36, Rules.Apply(s, new FormTown(G, 6, 5)).State[G].VictoryPoints); // 20 + 11 + 5
    }

    [Fact]
    public void Some_tokens_give_steps_and_two_keys()
    {
        var s = Rules.Apply(SevenPower(), new FormTown(G, 7, 5)).State; // 2 VP, 2 keys, +2 on every track
        Assert.Equal(2, s[G].Keys);
        Assert.Equal(22, s[G].VictoryPoints);
        Assert.Equal(ValueList.Of(3, 2, 2, 3), s[G].TrackSteps); // the Giants started with Fire 1 and Air 1
    }

    [Fact]
    public void The_new_key_can_take_the_player_to_the_top_at_once()
    {
        var s = SevenPower();
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 9) });
        var after = Rules.Apply(s, new FormTown(G, 1, 5)).State; // 1 key, +1 on every track

        Assert.Equal(10, after[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(0, after[G].Keys);
    }

    // As on BGA (927967865 #357): one key, and the town's step takes Fire, Water and Air to 10: the player chooses.
    [Fact]
    public void With_fewer_keys_than_tracks_reaching_the_top_the_player_chooses_where_the_key_goes()
    {
        var s = PlayFrom(SevenPower(), new Pass(G, 0));
        var steps = s[G].TrackSteps.SetItem((int)Track.Fire, 9).SetItem((int)Track.Water, 9).SetItem((int)Track.Air, 9);
        s = s.WithPlayer(s[G] with { TrackSteps = steps });
        var town = PlayFrom(s, new FormTown(G, 1, 5)); // 1 key, +1 on every track

        Assert.True(town[G].TopStepChoiceOwed);
        Assert.Equal(1, town[G].Keys);
        Assert.Equal(ValueList.Of(9, 9, 1, 9), town[G].TrackSteps);
        Assert.Equal("The Giants must choose which track their key takes to the top first.", Assert.Single(Rules.Apply(town, End(G)).Issues).Message);
        Assert.Equal("The Giants cannot take their key to the top of Earth: choose Fire or Water or Air.",
            Assert.Single(Rules.Apply(town, new TakeTopStep(G, Track.Earth)).Issues).Message);

        var chosen = PlayFrom(town, new TakeTopStep(G, Track.Air));
        Assert.Equal(ValueList.Of(9, 9, 1, 10), chosen[G].TrackSteps);
        Assert.Equal(0, chosen[G].Keys);
        Assert.False(chosen[G].TopStepChoiceOwed);
        Assert.Equal(town[G].Power.Gain(3), chosen[G].Power); // the top step's reward
        Assert.True(Rules.Apply(chosen, End(G)).Accepted);
    }

    [Fact]
    public void A_key_for_every_track_reaching_the_top_needs_no_choice()
    {
        var s = SevenPower();
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 9).SetItem((int)Track.Water, 9), Keys = 1 });
        var town = Rules.Apply(s, new FormTown(G, 1, 5)).State; // a second key

        Assert.False(town[G].TopStepChoiceOwed);
        Assert.Equal(10, town[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(10, town[G].TrackSteps[(int)Track.Water]);
        Assert.Equal(0, town[G].Keys);
    }

    [Fact]
    public void A_favours_step_onto_10_uses_the_key_of_the_town_it_makes_at_once()
    {
        // Andrew 2026-11-22 (as BGA, 931629591 #359): Fire 8, no key; the temple's favour is Fire +2 (towns need 6),
        // which makes the town; its key is used at once, with the top step's power, before the token is taken.
        var six = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.Dwelling), (6, PieceKind.Dwelling), (17, PieceKind.TradingPost));
        six = six.WithPlayer(six[G] with { TrackSteps = six[G].TrackSteps.SetItem((int)Track.Fire, 8), FavoursOwed = 1 }) with { ActionTaken = true };
        Assert.Empty(Rules.FormableTowns(six, G));

        var favoured = PlayFrom(six, new TakeFavour(G, 1)); // Fire +2
        Assert.Equal(10, favoured[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(0, favoured[G].Keys);
        Assert.Equal(1, favoured[G].KeysBorrowed);
        Assert.Null(favoured[G].TopStepsHeld);
        Assert.Equal(six[G].Power.Gain(3), favoured[G].Power); // the top step's reward, now

        var converted = PlayFrom(favoured, new ConvertWorkerToCoin(G)); // a free action between the favour and the town
        var town = PlayFrom(converted, new FormTown(G, 0, 5)); // 1 key, already used
        Assert.Equal(0, town[G].Keys);
        Assert.Equal(0, town[G].KeysBorrowed);
    }

    [Fact]
    public void A_key_is_used_at_once_only_for_a_town_still_to_form()
    {
        // Fire 9, no key, no town to form: the step onto 10 waits (TopStepsHeld) for a town later in the action.
        var s = With(Play(SoloStart), (5, PieceKind.TradingPost), (4, PieceKind.Dwelling));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 8), FavoursOwed = 1 }) with { ActionTaken = true };
        var favoured = PlayFrom(s, new TakeFavour(G, 1)); // Fire +2
        Assert.Equal(9, favoured[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(0, favoured[G].KeysBorrowed);
        Assert.Equal<ValueList<Track>?>(ValueList.Of(Track.Fire), favoured[G].TopStepsHeld);
    }

    [Fact]
    public void The_shipping_token_upgrades_shipping_and_scores_the_upgrade_too()
    {
        var s = Rules.Apply(SevenPower(), new FormTown(G, 5, 5)).State; // 4 VP and a free shipping upgrade (2 VP)
        Assert.Equal(1, s[G].Shipping);
        Assert.Equal(26, s[G].VictoryPoints);
    }

    [Fact]
    public void Only_one_player_can_reach_the_top_of_a_track()
    {
        var s = Charged(Play(TwoPlayerStart)); // Witches to act
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 10) });
        s = s.WithPlayer(s[W] with { TrackSteps = s[W].TrackSteps.SetItem((int)Track.Fire, 9), Keys = 1 });

        var after = PlayFrom(s, GetPriest(W), Place(W, 0, Track.Fire, 1));
        Assert.Equal(9, after[W].TrackSteps[(int)Track.Fire]);
        Assert.Equal(1, after[W].Keys); // not spent
    }
}
