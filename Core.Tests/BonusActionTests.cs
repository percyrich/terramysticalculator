using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class BonusActionTests
{
    private static readonly BonusActionId FavourCultStep = new(BonusSource.Favour, BonusAction.CultStep);
    private static readonly BonusActionId GiantsSpades = new(BonusSource.Stronghold, BonusAction.TwoSpades);
    private static readonly BonusActionId SwarmlingsTradingPost = new(BonusSource.Stronghold, BonusAction.FreeTradingPost);

    // Giants with favour 4 (Water +2, unlocks CultStep), their next turn. Witches have passed. Power 2/10/0.
    // The Giants start with Fire 1, Air 1.
    private static readonly Move[] CultStepUnlocked = Concat(BuildingTests.TradingPostBuilt, new Move[]
    {
        UpgradeTo(G, PieceKind.Temple, 0, 43), new TakeFavour(G, 4), End(G),
    });

    // Giants with their stronghold on 43, their next turn: 6 coins, 0 workers. Hex 4 (Desert) is next to 5.
    private static readonly Move[] GiantsStronghold = Concat(BuildingTests.TradingPostBuilt, new[]
    {
        UpgradeTo(G, PieceKind.Stronghold, 0, 43), End(G),
    });

    // Gives a faction its stronghold's bonus action by emptying its stronghold bank (as if built).
    private static GameState WithStronghold(GameState s, Faction f) =>
        s.WithPlayer(s[f] with { StrongholdBank = ValueList<Piece?>.Repeat(null, Config.StrongholdsPerPlayer) });

    // ----- Unlocking -----

    [Fact]
    public void Favour_four_and_strongholds_unlock_bonus_actions()
    {
        Assert.Equal(ValueList.Of(FavourCultStep), Play(CultStepUnlocked)[G].BonusActions);
        Assert.Equal(ValueList.Of(GiantsSpades), Play(GiantsStronghold)[G].BonusActions);
        Assert.Empty(Play(BuildingTests.TradingPostBuilt)[G].BonusActions);
    }

    [Fact]
    public void A_locked_bonus_action_is_refused()
    {
        var result = Rules.Apply(Play(BuildingTests.TradingPostBuilt), new BonusCultStep(G, BonusSource.Favour, Track.Fire));
        Assert.Equal("The Giants have not unlocked that bonus action.", Assert.Single(result.Issues).Message);
    }

    // ----- Cult step -----

    [Fact]
    public void Cult_step_moves_one_step_up_the_chosen_track_as_the_action()
    {
        var result = Rules.Apply(Play(CultStepUnlocked), new BonusCultStep(G, BonusSource.Favour, Track.Earth));

        Assert.True(result.Accepted);
        Assert.Equal(1, result.State[G].TrackSteps[(int)Track.Earth]);
        Assert.True(result.State.ActionTaken);
        Assert.Equal(ValueList.Of(FavourCultStep), result.State[G].BonusActionsUsed);
    }

    [Fact]
    public void Cult_step_earns_track_rewards()
    {
        var s = Play(CultStepUnlocked);
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, 2) });
        var result = Rules.Apply(s, new BonusCultStep(G, BonusSource.Favour, Track.Fire));
        Assert.Equal(new Power(1, 11, 0), result.State[G].Power);
    }

    [Fact]
    public void Cult_step_at_the_top_is_allowed_but_lost()
    {
        var s = Play(CultStepUnlocked);
        s = s.WithPlayer(s[G] with { TrackSteps = s[G].TrackSteps.SetItem((int)Track.Fire, Config.TrackMax) });
        var result = Rules.Apply(s, new BonusCultStep(G, BonusSource.Favour, Track.Fire));

        Assert.True(result.Accepted);
        Assert.Equal(Config.TrackMax, result.State[G].TrackSteps[(int)Track.Fire]);
        Assert.Equal(Severity.Note, Assert.Single(result.Issues).Severity);
    }

    [Fact]
    public void A_bonus_action_is_the_turns_action()
    {
        // The temple was this turn's action, so the newly unlocked bonus action must wait.
        var s = Play(Concat(BuildingTests.TradingPostBuilt, new Move[] { UpgradeTo(G, PieceKind.Temple, 0, 43), new TakeFavour(G, 4) }));
        Assert.Equal("The Giants have already taken their action this turn. Press End turn.",
            Assert.Single(Rules.Apply(s, new BonusCultStep(G, BonusSource.Favour, Track.Fire)).Issues).Message);
    }

    [Fact]
    public void A_bonus_action_is_used_once_per_round()
    {
        var used = Play(Concat(CultStepUnlocked, new Move[] { new BonusCultStep(G, BonusSource.Favour, Track.Fire), End(G) }));
        Assert.Equal("The Giants have already used that bonus action this round.",
            Assert.Single(Rules.Apply(used, new BonusCultStep(G, BonusSource.Favour, Track.Air)).Issues).Message);

        // Round 2: the Giants pass to end round 1; the Witches act first and pass.
        var nextRound = PlayFrom(used, Concat(PassAndEnd(G), PassAndEnd(W)));
        Assert.Equal(2, nextRound.Round);
        Assert.Empty(nextRound[G].BonusActionsUsed);
        Assert.True(Rules.Apply(nextRound, new BonusCultStep(G, BonusSource.Favour, Track.Air)).Accepted);
    }

    [Fact]
    public void Unlocking_another_bonus_action_does_not_reset_a_used_one()
    {
        // All in round 1 (the Witches have passed): the Giants use their stronghold's action (both spades on 4,
        // not built on), then build a trading post and a temple on 5 and take favour 4.
        var s = Play(Concat(GiantsStronghold, new Move[]
        {
            new BonusSpades(G, BonusSource.Stronghold), new SpendSpades(G, 4, Terrain.Wasteland), new FinishSpades(G), End(G),
            UpgradeTo(G, PieceKind.TradingPost, 0, 5), End(G),
            UpgradeTo(G, PieceKind.Temple, 0, 5), new TakeFavour(G, 4), End(G),
        }));

        Assert.Equal(1, s.Round);
        Assert.Equal(ValueList.Of(GiantsSpades, FavourCultStep), s[G].BonusActions);
        Assert.Equal(ValueList.Of(GiantsSpades), s[G].BonusActionsUsed);
        Assert.Equal("The Giants have already used that bonus action this round.",
            Assert.Single(Rules.Apply(s, new BonusSpades(G, BonusSource.Stronghold)).Issues).Message);
        Assert.True(Rules.Apply(s, new BonusCultStep(G, BonusSource.Favour, Track.Fire)).Accepted);
    }

    [Fact]
    public void Cult_step_can_target_any_track()
    {
        var options = Rules.BonusOptions(Play(CultStepUnlocked), G, FavourCultStep);
        Assert.Equal(4, options.Destinations.Count);
        Assert.Contains<Destination>(new TrackDestination(Track.Air), options.Destinations);
    }

    // ----- Giants: two free spades -----
    // GiantsStronghold: buildings on 43 and 5; next to 5: 4 Desert, 6 Plains, 17 Swamp. 6 coins, 0 workers.

    [Fact]
    public void The_Giants_stronghold_gives_two_free_spades_as_the_action()
    {
        var s = PlayFrom(Play(GiantsStronghold), new BonusSpades(G, BonusSource.Stronghold));

        Assert.Equal(2, s[G].SpadesOwed);
        Assert.True(s.ActionTaken);
        Assert.True(s.SpadesPending);
        Assert.Equal(ValueList.Of(GiantsSpades), s[G].BonusActionsUsed);
    }

    [Fact]
    public void The_Giants_two_spades_make_one_hex_home_terrain()
    {
        var s = PlayFrom(Play(GiantsStronghold), new BonusSpades(G, BonusSource.Stronghold),
            new SpendSpades(G, 4, Terrain.Wasteland)); // 2 spades, as always for the Giants
        Assert.Equal(0, s[G].SpadesOwed);
        Assert.True(s.SpadesPending); // the dwelling may still be built

        var built = Rules.Apply(s, new BuildOnSpadedHex(new Piece(G, PieceKind.Dwelling, 0), 4));
        Assert.True(built.Accepted);
        Assert.Equal(4, built.State[G].Coins);     // the dwelling is paid as normal
        Assert.Equal(-1, built.State[G].Workers);
        Assert.False(built.State.SpadesPending);
    }

    [Fact]
    public void Spending_ends_by_itself_when_nothing_can_be_built()
    {
        var s = PlayFrom(Play(GiantsStronghold), new BonusSpades(G, BonusSource.Stronghold),
            new SpendSpades(G, 4, Terrain.Plains)); // not Wasteland
        Assert.False(s.SpadesPending);
        Assert.True(Rules.Apply(s, End(G)).Accepted);
    }

    // ----- Free spades in general: the Witches with the 2-spade power action -----
    // TwoPlayerStart, charged: the Witches act first (15 coins, 7 workers). Next to their 9: 10 Lake (1 spade to
    // Forest), 21 Swamp (2), 8 Plains (3).

    private static GameState WitchesWithTwoSpades() => PlayFrom(Charged(Play(TwoPlayerStart)), new TakePowerAction(W, 5));

    [Fact]
    public void A_leftover_spade_goes_on_another_hex_then_one_hex_is_built_on()
    {
        var s = PlayFrom(WitchesWithTwoSpades(), new SpendSpades(W, 10, Terrain.Forest)); // 1 spade, 1 left
        Assert.Equal(1, s[W].SpadesOwed);

        // Swamp to Forest needs 2: the free one plus one paid (3 workers), allowed in the main phase.
        var topped = Rules.Apply(s, new SpendSpades(W, 21, Terrain.Forest));
        Assert.Contains<GameEvent>(new SpadesSpent(W, 21, Terrain.Swamp, Terrain.Forest, 1, 1), topped.Events);
        Assert.Equal(4, topped.State[W].Workers);

        var built = Rules.Apply(topped.State, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 2), 10));
        Assert.True(built.Accepted);
        Assert.Equal((Piece?)new Piece(W, PieceKind.Dwelling, 2), built.State.Map[10]);
        Assert.Equal(13, built.State[W].Coins);  // the dwelling is paid as normal
        Assert.Equal(3, built.State[W].Workers);
        Assert.False(built.State.SpadesPending);
        Assert.False(Rules.Apply(built.State, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 3), 21)).Accepted); // only one
    }

    [Fact]
    public void Free_spades_must_be_finished_before_the_turn_ends()
    {
        var s = PlayFrom(WitchesWithTwoSpades(), new SpendSpades(W, 10, Terrain.Forest));
        Assert.StartsWith("The Witches must finish spending", Assert.Single(Rules.Apply(s, End(W)).Issues).Message);

        // Hex 10 is now Forest: Done gives up the spade left, but the dwelling may still be built...
        var done = Rules.Apply(s, new FinishSpades(W));
        Assert.Contains<GameEvent>(new SpadesForfeited(W, 1), done.Events);
        Assert.Equal(0, done.State[W].SpadesOwed);
        Assert.True(done.State.SpadesPending);
        Assert.False(Rules.Apply(done.State, End(W)).Accepted);

        // ...and a second Done gives that up too.
        var doneAgain = Rules.Apply(done.State, new FinishSpades(W));
        Assert.True(Rules.Apply(doneAgain.State, End(W)).Accepted);
    }

    [Fact]
    public void A_dwelling_waits_until_every_free_spade_is_spent()
    {
        var s = PlayFrom(WitchesWithTwoSpades(), new SpendSpades(W, 10, Terrain.Forest)); // 1 spade, 1 left
        var early = Rules.Apply(s, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 2), 10));
        Assert.False(early.Accepted);
        Assert.StartsWith("The Witches have free spades left", Assert.Single(early.Issues).Message);

        // The leftover spade goes on another hex; then the dwelling goes on hex 10.
        s = PlayFrom(s, new SpendSpades(W, 8, Terrain.Desert)); // Plains to Desert: 1 spade
        Assert.Equal(0, s[W].SpadesOwed);
        Assert.True(Rules.Apply(s, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 2), 10)).Accepted);
    }

    [Fact]
    public void Free_spades_follow_the_hex_rules_and_go_on_each_hex_once()
    {
        var s = WitchesWithTwoSpades();
        Assert.Equal("That hex is out of range of the Witches' buildings.",
            Assert.Single(Rules.Apply(s, new SpendSpades(W, 100, Terrain.Desert)).Issues).Message);

        s = PlayFrom(s, new SpendSpades(W, 10, Terrain.Swamp)); // 1 spade, not Forest
        Assert.Equal("Free spades have already been spent on that hex.",
            Assert.Single(Rules.Apply(s, new SpendSpades(W, 10, Terrain.Forest)).Issues).Message);

        s = PlayFrom(s, new SpendSpades(W, 21, Terrain.Forest)); // 1 free + 1 paid: Forest, so building is open
        Assert.Equal("Only a hex just transformed with free spades can be built on now.",
            Assert.Single(Rules.Apply(s, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 2), 8)).Issues).Message);
    }

    // ----- Witches: a free dwelling on any Forest -----

    [Fact]
    public void Witches_build_free_on_any_forest_even_out_of_range()
    {
        var before = WithStronghold(Play(TwoPlayerStart), W); // Witches on 9 and 60; hex 2 is Forest, far from both
        var result = Rules.Apply(before, new BonusBuild(W, BonusSource.Stronghold, 2));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal((Piece?)new Piece(W, PieceKind.Dwelling, 2), s.Map[2]);
        Assert.Equal(before[W].Coins, s[W].Coins);
        Assert.Equal(before[W].Workers, s[W].Workers);
    }

    [Fact]
    public void Witches_bonus_build_needs_an_empty_forest()
    {
        var s = WithStronghold(Play(TwoPlayerStart), W);
        Assert.Equal("This bonus action builds only on Forest.", Assert.Single(Rules.Apply(s, new BonusBuild(W, BonusSource.Stronghold, 0)).Issues).Message);
        Assert.Equal("That hex is already occupied.", Assert.Single(Rules.Apply(s, new BonusBuild(W, BonusSource.Stronghold, WitchesStartA)).Issues).Message);
    }

    // ----- Swarmlings: a free trading post -----
    // Witches (on 9 and 60) then Swarmlings (on the Lakes 3 and 89); the Witches pass, so the Swarmlings act.

    private const Faction Sw = Faction.Swarmlings;

    private static GameState SwarmlingsToAct() => WithStronghold(
        PlayFrom(Play(StartWith(new[] { W, Sw }, WitchesStartA, 3, 89, WitchesStartB)), PassAndEnd(W)), Sw);

    [Fact]
    public void Swarmlings_upgrade_a_dwelling_for_free()
    {
        var before = SwarmlingsToAct();
        var result = Rules.Apply(before, new BonusTradingPost(Sw, BonusSource.Stronghold, 3));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal((Piece?)new Piece(Sw, PieceKind.TradingPost, 0), s.Map[3]);
        Assert.Equal(before[Sw].Coins, s[Sw].Coins);
        Assert.Equal(before[Sw].Workers, s[Sw].Workers);
        Assert.Equal(ValueList.Of(SwarmlingsTradingPost), s[Sw].BonusActionsUsed);
    }

    [Fact]
    public void Swarmlings_free_trading_post_is_offered_exactly_their_dwellings()
    {
        var options = Rules.BonusOptions(SwarmlingsToAct(), Sw, SwarmlingsTradingPost);

        Assert.Equal(2, options.Destinations.Count);
        Assert.Contains<Destination>(new HexDestination(3), options.Destinations);
        Assert.Contains<Destination>(new HexDestination(89), options.Destinations);
    }
}
