using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Priests now come from power (5 from bowl 3). Most tests start "charged": everyone at 0/0/12.
public class PriestTests
{
    [Fact]
    public void Top_left_slot_gives_three_steps_and_uses_the_action()
    {
        var s = PlayCharged(TwoPlayerStart, GetPriest(W));
        var result = Rules.Apply(s, Place(W, 0, Track.Fire, 0));

        Assert.True(result.Accepted);
        var after = result.State;
        Assert.Equal(3, after[W].TrackSteps[(int)Track.Fire]);
        Assert.Empty(after[W].Priests);
        Assert.Equal((Piece?)new Piece(W, PieceKind.Priest, 0), after.PriestZones[(int)Track.Fire][0]);
        Assert.True(after.ActionTaken);
        Assert.Equal(W, after.ActivePlayer); // still the Witches' turn until End turn
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Other_slots_give_two_steps(int slot)
    {
        var s = PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Earth, slot));
        Assert.Equal(2, s[G].TrackSteps[(int)Track.Earth]);
    }

    [Fact]
    public void A_taken_slot_cannot_be_used_by_anyone()
    {
        var s = PlayCharged(TwoPlayerStart, GetPriest(W), Place(W, 0, Track.Fire, 0), End(W), GetPriest(G));
        Assert.False(Rules.Apply(s, Place(G, 0, Track.Fire, 0)).Accepted);
    }

    [Fact]
    public void Only_the_top_priest_can_be_placed()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), GetPriest(G));
        Assert.False(Rules.Apply(s, Place(G, 0, Track.Fire, 0)).Accepted); // bottom of stack
        Assert.True(Rules.Apply(s, Place(G, 1, Track.Fire, 0)).Accepted);  // top of stack
    }

    [Fact]
    public void A_priest_cannot_be_placed_without_being_gained()
    {
        Assert.False(Rules.Apply(Play(SoloStart), Place(G, 0, Track.Fire, 0)).Accepted);
    }

    [Fact]
    public void Placing_a_priest_is_the_action_for_the_turn()
    {
        var s = Ruled(PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Fire, 0)));
        Assert.False(Rules.Apply(s, Build(G, 2, 4)).Accepted);
    }

    [Fact]
    public void Priest_numbers_keep_counting_after_placement()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Fire, 0), End(G), GetPriest(G));
        Assert.Equal(ValueList.Of(new Piece(G, PieceKind.Priest, 1)), s[G].Priests);
    }

    [Fact]
    public void Steps_are_capped_at_ten()
    {
        // Four priests on one track give at most 3+2+2+2 = 9, so the cap is tested from a prepared state.
        // The top step also needs a key.
        var s = PlayCharged(SoloStart, GetPriest(G));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 9), Keys = 1 });

        var result = Rules.Apply(s, Place(G, 0, Track.Fire, 0));

        Assert.Equal(10, result.State[G].TrackSteps[(int)Track.Fire]);
        Assert.Contains<GameEvent>(new PriestPlaced(new Piece(G, PieceKind.Priest, 0), Track.Fire, 0, 9, 10), result.Events);
    }

    // ---------- The supply ----------

    [Fact]
    public void Priests_in_the_supply_cannot_be_placed()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), Place(G, 0, Track.Fire, 0));
        Assert.False(result.Accepted);
        Assert.Contains("still in the Giants' supply", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Gaining_a_priest_takes_the_front_of_the_supply()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        Assert.Equal(ValueList.Of(new Piece(G, PieceKind.Priest, 0)), s[G].Priests);
        Assert.Equal(new Piece(G, PieceKind.Priest, 1), s[G].PriestSupply[0]);
    }

    [Fact]
    public void Gaining_from_an_empty_supply_warns_and_gains_nothing()
    {
        var s = Charged(Play(SoloStart));
        s = s.WithPlayer(s[G] with { PriestSupply = ValueList<Piece>.Empty });

        var result = Rules.Apply(s, GetPriest(G));

        Assert.True(result.Accepted);                                    // the move still happens...
        Assert.Equal(Severity.Note, Assert.Single(result.Issues).Severity);
        Assert.Empty(result.State[G].Priests);                           // ...but no priest arrives
        Assert.Equal(new Power(5, 0, 7), result.State[G].Power);         // and the power is spent
    }

    // ---------- The returning 1-step slot (slot 4) ----------

    [Fact]
    public void The_one_step_slot_gives_one_step()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Earth, 4));
        Assert.Equal(1, s[G].TrackSteps[(int)Track.Earth]); // the Giants start with none
        Assert.Equal((Piece?)new Piece(G, PieceKind.Priest, 0), s.PriestZones[(int)Track.Earth][4]);
    }

    [Fact]
    public void A_priest_in_the_one_step_slot_returns_to_the_front_of_the_supply_at_end_of_turn()
    {
        var result = Rules.Apply(PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Earth, 4)), End(G));
        var s = result.State;

        Assert.Null(s.PriestZones[(int)Track.Earth][4]);
        Assert.Equal(new Piece(G, PieceKind.Priest, 0), s[G].PriestSupply[0]); // the next one gained
        Assert.Equal(Config.PriestsPerPlayer, s[G].PriestSupply.Count);
        Assert.Equal(1, s[G].TrackSteps[(int)Track.Earth]); // the step is kept
    }

    [Fact]
    public void Priests_in_other_slots_stay()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Earth, 0), End(G));
        Assert.Equal((Piece?)new Piece(G, PieceKind.Priest, 0), s.PriestZones[(int)Track.Earth][0]);
        Assert.Equal(Config.PriestsPerPlayer - 1, s[G].PriestSupply.Count);
    }

    [Fact]
    public void The_one_step_slot_can_be_used_at_ten_for_no_steps()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Air, 10) });

        var result = Rules.Apply(s, Place(G, 0, Track.Air, 4));

        Assert.True(result.Accepted);
        Assert.Equal(10, result.State[G].TrackSteps[(int)Track.Air]);
    }

    // ---------- Arrival order on a step (for drawing trackers side by side) ----------

    [Fact]
    public void Factions_are_listed_in_the_order_they_joined()
    {
        var s = Play(ChooseTM, new AddPlayer(G), new AddPlayer(M), new AddPlayer(W), new RemovePlayer(M));
        Assert.All(s.TrackArrivals, order => Assert.Equal(ValueList.Of(G, W), order));
    }

    [Fact]
    public void A_faction_arriving_on_a_step_comes_after_those_already_there()
    {
        // Witches and Giants both start on Earth 0, Witches listed first.
        var s = PlayCharged(TwoPlayerStart, GetPriest(W), Place(W, 0, Track.Earth, 1)); // Witches: Earth 2
        Assert.Equal(ValueList.Of(W), s.OnStep(Track.Earth, 2));
        Assert.Equal(ValueList.Of(G, W), s.TrackArrivals[(int)Track.Earth]);

        s = PlayFrom(s, End(W), GetPriest(G), Place(G, 0, Track.Earth, 2)); // Giants: Earth 2 too
        Assert.Equal(ValueList.Of(W, G), s.OnStep(Track.Earth, 2));
    }

    [Fact]
    public void Leaving_and_undoing_restores_the_order()
    {
        var both = PlayCharged(TwoPlayerStart, GetPriest(W), Place(W, 0, Track.Earth, 1), End(W),
            GetPriest(G), Place(G, 0, Track.Earth, 2), End(G));
        var history = new History(both);
        history.Apply(GetPriest(W));
        history.Apply(Place(W, 1, Track.Earth, 3)); // the Witches move on to Earth 4
        Assert.Equal(ValueList.Of(G), history.Current.OnStep(Track.Earth, 2));

        history.Undo();
        Assert.Equal(ValueList.Of(W, G), history.Current.OnStep(Track.Earth, 2));
    }
}
