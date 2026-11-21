using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// From TwoPlayerStart: Witches hold tile 3, Giants tile 9. Every other tile is in the bank, in the slot
// with its own number, with 1 coin on it. The Witches act first, with 15 coins.
public class PassTileTests
{
    private static readonly Move[] TwoPlayersPlaced = Concat(Placing(W, G), new[]
    {
        StartAt(W, 0, WitchesStartA), StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB), StartAt(W, 1, WitchesStartB),
    });

    [Fact]
    public void The_pass_tile_adds_its_income()
    {
        // Base 1 worker, 2 workers (two emptied dwelling slots), tile 9: 3 power.
        Assert.Equal(new Income(0, 3, 0, 3), Play(TwoPlayerStart)[G].Income);
    }

    [Fact]
    public void Passing_swaps_tiles_and_takes_the_coins()
    {
        var result = Rules.Apply(Play(TwoPlayerStart), new Pass(W, 0));

        Assert.True(result.Accepted);
        Assert.Empty(result.Issues);
        var s = result.State;
        Assert.Equal(0, s[W].PassTile);
        Assert.Equal(16, s[W].Coins);          // the coin lying on tile 0
        // Bank before: 0, 1, 5 (of 0, 1, 3, 5, 9: 9 and 3 picked). Tile 0 goes, the rest close up, and 3 goes back at the end.
        Assert.Equal(ValueList.Of<int?>(1, 5, 3, null, null), s.PassTileBank);
        Assert.Equal(0, s.PassTileCoins[0]);
        Assert.Contains<GameEvent>(new PassTileTaken(W, 0, 0, 1), result.Events);
    }

    [Fact]
    public void After_passing_a_new_tile_is_picked_before_the_turn_ends()
    {
        var passed = Rules.Apply(Play(TwoPlayerStart), new Pass(W));
        Assert.Empty(passed.Issues);
        Assert.True(passed.State[W].PassTileOwed);
        Assert.Equal("The Witches must pick a new pass tile from the bank first.",
            Assert.Single(Rules.Apply(passed.State, End(W)).Issues).Message);

        // Picking the tile is part of passing: no conversion before it, only after.
        Assert.Equal("The Witches must pick a new pass tile from the bank first.",
            Assert.Single(Rules.Apply(passed.State, new BurnPower(W)).Issues).Message);
        var picked = PlayFrom(passed.State, new PickPassTile(W, 0), new BurnPower(W));
        Assert.Equal(0, picked[W].PassTile);
        Assert.False(picked[W].PassTileOwed);
        Assert.Equal(16, picked[W].Coins);
        Assert.True(Rules.Apply(picked, End(W)).Accepted);
    }

    [Fact]
    public void A_new_tile_can_only_be_picked_after_passing()
    {
        Assert.False(Rules.Apply(Play(TwoPlayerStart), new PickPassTile(W, 0)).Accepted);
    }

    [Fact]
    public void Keeping_the_tile_is_a_calculator_shortcut_with_a_warning()
    {
        var result = Rules.Apply(Play(TwoPlayerStart), PassKeeping(W));

        Assert.True(result.Accepted);
        Assert.Equal(Severity.Note, Assert.Single(result.Issues).Severity);
        Assert.Equal(3, result.State[W].PassTile);
        Assert.False(result.State[W].PassTileOwed);
    }

    [Fact]
    public void Only_a_tile_in_the_bank_can_be_taken()
    {
        var result = Rules.Apply(Play(TwoPlayerStart), new Pass(W, 9)); // the Giants hold it
        Assert.Equal("That pass tile is not in the bank.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void The_tile_given_up_scores_when_passing()
    {
        // The Witches pick tile 7 (1 VP per dwelling) and have 2 dwellings. Pass tiles 0, 3, 5, 7, 9 in this game.
        var placed = Concat(Confirmed(W, G), SetUpAndStart(new[] { W, G }, TestRoundTiles, new[] { 0, 3, 5, 7, 9 }), new[]
        {
            StartAt(W, 0, WitchesStartA), StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB), StartAt(W, 1, WitchesStartB),
        });
        var s = Play(Concat(placed, new Move[] { new PickPassTile(G, 5), new PickPassTile(W, 7) }));
        var result = Rules.Apply(s, new Pass(W, 0));

        Assert.Equal(22, result.State[W].VictoryPoints);
        // One point from each dwelling, scored by the tile.
        var gained = result.Events.OfType<VictoryPointsGained>().ToList();
        Assert.Equal(new[] { 21, 22 }, gained.Select(e => e.NewTotal));
        Assert.All(gained, e => Assert.Equal(new PointsArea.PassTile(7), e.Source));
        Assert.Equal(2, gained.Select(e => e.Trigger).OfType<PointsArea.OnHex>().Distinct().Count());
    }

    [Fact]
    public void Tiles_left_in_the_bank_gain_a_coin_at_the_end_of_the_round()
    {
        var s = Play(Concat(TwoPlayerStart, new Move[] { new Pass(W, 0), End(W), new Pass(G, 1), End(G) }));

        Assert.Equal(2, s.Round);
        Assert.Equal(2, s.PassTileCoins[5]); // 1 after the setup pick, 1 after round 1
        Assert.Equal(1, s.PassTileCoins[3]); // returned by the Witches during round 1
        Assert.Equal(0, s.PassTileCoins[0]); // held by the Witches
    }

    [Fact]
    public void The_new_tile_pays_its_income_the_next_round()
    {
        // Witches: 15 coins + 1 on tile 0 = 16. Round 2 income: tile 0's 6 more.
        var s = Play(Concat(TwoPlayerStart, new Move[] { new Pass(W, 0), End(W), new Pass(G, 1), End(G) }));
        Assert.Equal(22, s[W].Coins);
    }

    [Fact]
    public void In_the_last_round_no_tile_is_taken()
    {
        var round6 = Play(Concat(SoloStart, PassAndEnd(G, G, G, G, G)));
        Assert.Equal(6, round6.Round);

        Assert.Equal("No pass tiles are taken in the last round.",
            Assert.Single(Rules.Apply(round6, new Pass(G, 0)).Issues).Message);
        var result = Rules.Apply(round6, new Pass(G));
        Assert.True(result.Accepted);
        Assert.Empty(result.Issues);

        // The tile held goes back to the bank (after scoring), and none is owed.
        int held = round6[G].PassTile!.Value;
        Assert.Null(result.State[G].PassTile);
        Assert.False(result.State[G].PassTileOwed);
        Assert.Contains((int?)held, result.State.PassTileBank);
        Assert.Equal(round6.PassTileBank.Count, result.State.PassTileBank.Count);
        Assert.Contains(result.Events, e => e is PassTileReturned { Tile: var tile } && tile == held);
    }

    [Fact]
    public void Pass_tile_one_gives_a_free_spade()
    {
        var s = Play(Concat(Placing(G), new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) },
            new Move[] { new PickPassTile(G, 1) }));
        Assert.Equal(ValueList.Of(new BonusActionId(BonusSource.PassTile, BonusAction.OneSpade)), s[G].BonusActions);

        s = PlayFrom(s, new BonusSpades(G, BonusSource.PassTile));
        Assert.Equal(1, s[G].SpadesOwed);
        Assert.True(s.ActionTaken);
    }

    [Fact]
    public void A_pass_tile_can_unlock_a_bonus_action()
    {
        var s = Play(Concat(Placing(G), new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) },
            new Move[] { new PickPassTile(G, 8) })); // 4 coins, bonus action: cult step

        var fromTile = new BonusActionId(BonusSource.PassTile, BonusAction.CultStep);
        Assert.Equal(ValueList.Of(fromTile), s[G].BonusActions);
        Assert.True(Rules.Apply(s, new BonusCultStep(G, BonusSource.PassTile, Track.Fire)).Accepted);
        Assert.False(Rules.Apply(s, new BonusCultStep(G, BonusSource.Favour, Track.Fire)).Accepted); // no such favour
    }
}
