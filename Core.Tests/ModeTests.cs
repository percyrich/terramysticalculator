using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Strict and Lenient, priest debt, setting up a game in progress, and Free play.
public class ModeTests
{
    private static string Refusal(GameState s, Move move) => Assert.Single(Rules.Apply(s, move).Issues).Message;

    // A game in progress being set up with these factions (one alone: Calculator, with every pass tile): every screen
    // done, Start pressed, the first one selected.
    private static Move[] SetupStart(params Faction[] factions) => Concat(
        new Move[] { new ChooseGame(GameKind.TerraMystica, GameOptions.All with { SetUpInProgress = true, Calculator = factions.Length == 1 }) },
        factions.Select(f => (Move)new AddPlayer(f)).ToArray(),
        new Move[] { new ConfirmFactions(), new ChooseMap(MapId.Original) },
        TestRoundTiles.Select(t => (Move)new ChooseRoundTile(t)).ToArray(),
        new Move[] { new ConfirmRoundTiles() },
        factions.Length > 1 ? PassTileChoice(factions).Select(t => (Move)new AddPassTile(t)).ToArray() : System.Array.Empty<Move>(),
        new Move[] { new StartGame() });

    // The faction's leftmost building of that kind put on the hex.
    private static GameState Put(GameState s, Faction f, PieceKind kind, int hex) =>
        PlayFrom(s, new EditBuilding(s[f].NextBuilding(kind)!.Value, hex));

    private static GameState Put(GameState s, Faction f, params (PieceKind Kind, int Hex)[] buildings)
    {
        foreach (var (kind, hex) in buildings)
            s = Put(s, f, kind, hex);
        return s;
    }

    // ---------- Strict and Lenient ----------

    [Fact]
    public void Strict_refuses_a_move_into_debt_that_Lenient_allows_with_a_warning()
    {
        var s = Play(SoloStart);
        s = s.WithPlayer(s[G] with { Workers = 0 });

        var lenient = Rules.Apply(s, new ConvertWorkerToCoin(G));
        Assert.True(lenient.Accepted);
        Assert.Contains(lenient.Issues, issue => issue.Severity == Severity.Warning);

        var strict = Rules.Apply(s with { Mode = PlayMode.Strict }, new ConvertWorkerToCoin(G));
        Assert.False(strict.Accepted);
        Assert.Equal("Strict: the Giants would be in debt (-1 workers).", Assert.Single(strict.Issues).Message);
    }

    [Fact]
    public void Strict_can_be_entered_only_while_nobody_is_in_debt()
    {
        var s = Play(TwoPlayerStart);
        var inDebt = s.WithPlayer(s[G] with { Workers = -1 });
        Assert.Equal("Strict can't begin while the Giants are in debt (-1 workers).",
            Assert.Single(Rules.Apply(inDebt, new SetMode(PlayMode.Strict)).Issues).Message);

        var strict = Rules.Apply(s, new SetMode(PlayMode.Strict));
        Assert.True(strict.Accepted);
        Assert.Equal(PlayMode.Strict, strict.State.Mode);
    }

    [Fact]
    public void Starting_VP_are_set_while_the_turn_order_is_chosen()
    {
        var s = PlayFrom(Play(Confirmed(W, G)), new AdjustStartingVictoryPoints(G, 10), new AdjustStartingVictoryPoints(W, -25));
        Assert.Equal(30, Rules.StartingVictoryPoints(s[G]));
        Assert.Equal(-5, Rules.StartingVictoryPoints(s[W])); // a high bid can take it below 0

        var started = PlayFrom(s, SetUpAndStart(W, G));
        Assert.Equal(30, started[G].VictoryPoints);
        Assert.Equal(-5, started[W].VictoryPoints);
        Assert.False(Rules.Apply(started, new AdjustStartingVictoryPoints(G, 1)).Accepted);
    }

    [Fact]
    public void In_Strict_an_auction_may_start_a_faction_below_20_VP()
    {
        // BGA table 144401189: the Chaos Magicians bid down to 12.
        var strict = new ChooseGame(GameKind.TerraMystica, GameOptions.All with { StartingMode = PlayMode.Strict });
        var moves = Confirmed(W, G);
        moves[0] = strict;
        var result = Rules.Apply(Play(moves), new AdjustStartingVictoryPoints(G, -8));
        Assert.True(result.Accepted);
        Assert.Equal(12, Rules.StartingVictoryPoints(result.State[G]));
        Assert.Equal(12, PlayFrom(result.State, SetUpAndStart(W, G))[G].VictoryPoints);

        // A bid past 20: below 0 VP, and Strict still plays on (only coins, workers and priests owed are refused).
        var below = Rules.Apply(Play(moves), new AdjustStartingVictoryPoints(G, -23));
        Assert.True(below.Accepted);
        var playing = PlayFrom(below.State, SetUpAndStart(W, G));
        Assert.Equal(PlayMode.Strict, playing.Mode);
        Assert.Equal(-3, playing[G].VictoryPoints);
        Assert.True(Rules.Apply(playing, new SetMode(PlayMode.Lenient)).Accepted);
        Assert.True(Rules.Apply(playing with { Mode = PlayMode.Lenient }, new SetMode(PlayMode.Strict)).Accepted);
    }

    [Fact]
    public void Back_at_session_start_a_Calculator_game_can_follow_a_normal_one()
    {
        var history = new History(GameState.Initial);
        foreach (var move in TwoPlayerStart)
            Assert.True(history.Apply(move).Accepted);
        Assert.True(history.JumpTo(history.Map().Vertices.Select(v => v.Bookmark).OfType<Bookmark>().First(b => b.Name == History.SessionStart)));

        Assert.True(history.Apply(ChooseCalculator).Accepted);
        Assert.True(history.Current.Options.Calculator);
        Assert.Equal(PlayMode.Free, history.Current.Mode);
        Assert.True(history.Apply(new AddPlayer(G)).Accepted);
        Assert.False(history.Apply(new AddPlayer(W)).Accepted);
        Assert.True(history.Apply(new ConfirmFactions()).Accepted);
        Assert.Equal(Phase.ChoosingMap, history.Current.Phase);
    }

    // Calculator's pass tiles: none (all of them), or at least as many as a two-player game has.
    [Fact]
    public void In_Calculator_the_pass_tiles_are_all_or_at_least_a_two_player_game_s()
    {
        var s = Play(ChooseCalculator, new AddPlayer(G), new ConfirmFactions(), new ChooseMap(MapId.Original), new ConfirmRoundTiles());
        Assert.Equal(Phase.ChoosingPassTiles, s.Phase);
        Assert.Equal(2 + Config.ExtraPassTiles, s.PassTilesNeeded);
        var four = PlayFrom(s, new AddPassTile(0), new AddPassTile(1), new AddPassTile(2), new AddPassTile(3));
        Assert.Equal($"Choose at least {2 + Config.ExtraPassTiles} pass tiles, or none for all of them.",
            Assert.Single(Rules.Apply(four, new StartGame()).Issues).Message);
        Assert.True(Rules.Apply(PlayFrom(four, new AddPassTile(4)), new StartGame()).Accepted);
        Assert.True(Rules.Apply(s, new StartGame()).Accepted); // none: all of them
    }

    [Fact]
    public void In_Calculator_a_new_action_ends_the_turn_before_it()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), Place(G, 0, Track.Fire, 4)); // the slot whose priest returns
        Assert.NotNull(s.PriestZones[(int)Track.Fire][4]);

        var next = Rules.Apply(s, Build(G, 2, 4)); // no End turn in between
        Assert.True(next.Accepted);
        Assert.Null(next.State.PriestZones[(int)Track.Fire][4]);
        Assert.Contains(new Piece(G, PieceKind.Priest, 0), next.State[G].PriestSupply);
    }

    [Fact]
    public void In_Calculator_the_plus_one_panel_gives_a_coin_worker_priest_or_power()
    {
        var s = Play(SoloStart);
        var before = s[G];
        s = PlayFrom(s, new CalculatorGain(G, GainKind.Coin), new CalculatorGain(G, GainKind.Worker),
            new CalculatorGain(G, GainKind.Priest), new CalculatorGain(G, GainKind.Power));
        Assert.Equal(before.Coins + 1, s[G].Coins);
        Assert.Equal(before.Workers + 1, s[G].Workers);
        Assert.Equal(before.Priests.Count + 1, s[G].Priests.Count);
        Assert.Equal(before.Power.Gain(1), s[G].Power);
        Assert.False(s.ActionTaken); // not an action

        var full = s.WithPlayer(s[G] with { Power = new Power(0, 0, 12) });
        Assert.False(Rules.Apply(full, new CalculatorGain(G, GainKind.Power)).Accepted);
        Assert.False(Rules.Apply(Play(TwoPlayerStart), new CalculatorGain(Faction.Giants, GainKind.Coin)).Accepted); // not Calculator
    }

    [Fact]
    public void Calculator_Strict_refuses_debt_and_gives_only_power()
    {
        var s = Play(SoloStart);
        var strict = s with { ChosenOptions = s.Options with { CalculatorStrict = true } };
        Assert.Equal("Calculator Strict gives power only.", Refusal(strict, new CalculatorGain(G, GainKind.Coin)));
        PlayFrom(strict, new CalculatorGain(G, GainKind.Power));
        Assert.StartsWith("Strict:", Refusal(strict.WithPlayer(strict[G] with { Workers = 0 }), new ConvertWorkerToCoin(G)));
        PlayFrom(s.WithPlayer(s[G] with { Workers = 0 }), new ConvertWorkerToCoin(G)); // plain Calculator: debt allowed
    }

    [Fact]
    public void In_Calculator_a_coin_can_be_taken_off_a_pass_tile_in_the_bank()
    {
        var s = Play(SoloStart);
        int tile = s.PassTileBank.First(t => t is int && s.PassTileCoins[t.Value] > 0)!.Value;
        var taken = PlayFrom(s, new TakePassTileCoin(tile));
        Assert.Equal(s.PassTileCoins[tile] - 1, taken.PassTileCoins[tile]);
        var none = s with { PassTileCoins = s.PassTileCoins.SetItem(tile, 0) };
        Assert.Equal("There is no coin on that pass tile.", Refusal(none, new TakePassTileCoin(tile)));
    }

    [Fact]
    public void The_starting_mode_is_a_game_option()
    {
        var s = Play(new ChooseGame(GameKind.TerraMystica, GameOptions.All with { StartingMode = PlayMode.Strict }));
        Assert.Equal(PlayMode.Strict, s.Mode);
        Assert.False(Rules.Apply(GameState.Initial,
            new ChooseGame(GameKind.TerraMystica, GameOptions.All with { StartingMode = PlayMode.Free, SetUpInProgress = true })).Accepted);
    }

    // ---------- Priest debt ----------

    [Fact]
    public void A_priest_gained_pays_back_a_priest_owed()
    {
        var owing = PlayFrom(Play(SoloStart), new UpgradeSpades(G));
        Assert.Equal(1, owing[G].PriestDebt);
        owing = owing.WithPlayer(owing[G] with { Power = new Power(0, 0, 12) });

        var paid = Rules.Apply(owing, GetPriest(G));
        Assert.True(paid.Accepted);
        Assert.Equal(0, paid.State[G].PriestDebt);
        Assert.Empty(paid.State[G].Priests);
        Assert.Equal(owing[G].PriestSupply, paid.State[G].PriestSupply);
        Assert.Contains<GameEvent>(new PriestDebtPaid(G, 0), paid.Events);
    }

    [Fact]
    public void A_priest_owed_is_paid_back_even_with_the_supply_empty()
    {
        var s = Play(SoloStart);
        s = s.WithPlayer(s[G] with { PriestDebt = 1, PriestSupply = ValueList<Piece>.Empty, Power = new Power(0, 0, 12) });

        var result = Rules.Apply(s, GetPriest(G));
        Assert.True(result.Accepted);
        Assert.Equal(0, result.State[G].PriestDebt);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Strict_refuses_a_priest_owed()
    {
        var s = Play(SoloStart) with { Mode = PlayMode.Strict };
        var result = Rules.Apply(s, new UpgradeSpades(G));
        Assert.False(result.Accepted);
        Assert.Contains("1 priest", Assert.Single(result.Issues).Message);
    }

    // ---------- Setting up a game in progress ----------

    [Fact]
    public void Setting_up_fills_and_empties_the_Treasurers_Safe()
    {
        const Faction Tr = Faction.Treasurers;
        var s = Play(Concat(
            new Move[]
            {
                new ChooseGame(GameKind.TerraMystica, GameOptions.All with
                    { SetUpInProgress = true, Calculator = true, Landscapes = false, FanFactions = true }),
                new AddPlayer(Tr), new ConfirmFactions(), new ChooseMap(MapId.Original),
            },
            TestRoundTiles.Select(t => (Move)new ChooseRoundTile(t)).ToArray(),
            new Move[] { new ConfirmRoundTiles(), new StartGame() }));
        Assert.Equal(Phase.SettingUp, s.Phase);
        int supply = s[Tr].PriestSupply.Count;
        s = PlayFrom(s, new EditSafe(Tr, 3, 0, 0), new EditSafe(Tr, 0, 2, 0), new EditSafe(Tr, 0, 0, 2));
        Assert.Equal((3, 2, 2), (s[Tr].SafeCoins, s[Tr].SafeWorkers, s[Tr].SafePriestList.Count));
        Assert.Equal(supply - 2, s[Tr].PriestSupply.Count); // the priests came from the supply

        s = PlayFrom(s, new EditSafe(Tr, -10, 0, -1)); // never below 0; a priest back to the supply
        Assert.Equal((0, 2, 1), (s[Tr].SafeCoins, s[Tr].SafeWorkers, s[Tr].SafePriestList.Count));
        Assert.Equal(supply - 1, s[Tr].PriestSupply.Count);
        Assert.False(Rules.Apply(s, new EditSafe(Tr, -1, 0, 0)).Accepted); // no coins left in it
        Assert.False(Rules.Apply(Play(SetupStart(G)), new EditSafe(G, 1, 0, 0)).Accepted); // only the Treasurers
    }

    [Fact]
    public void Setting_up_skips_the_turn_order_and_the_starting_dwellings()
    {
        var s = Play(SetupStart(G, W));
        Assert.Equal(Phase.SettingUp, s.Phase);
        Assert.Equal(ValueList.Of(G, W), s.TurnOrder);
        Assert.Equal(G, s.ActivePlayer);
        Assert.Equal(15, s[G].Coins);
        Assert.Equal(20, s[W].VictoryPoints);
        Assert.All(s.Map, piece => Assert.Null(piece));
        Assert.False(Rules.Apply(s, Build(G, 0, GiantsStartA)).Accepted);

        Assert.Equal(W, PlayFrom(s, new SelectFaction(W)).ActivePlayer);
    }

    [Fact]
    public void Resources_never_go_below_zero_except_VP()
    {
        var s = PlayFrom(Play(SetupStart(G)), new EditResource(G, Resource.Coins, -20), new EditResource(G, Resource.VictoryPoints, -25));
        Assert.Equal(0, s[G].Coins);
        Assert.Equal(-5, s[G].VictoryPoints);
        Assert.False(Rules.Apply(s, new EditResource(G, Resource.Coins, -1)).Accepted);
    }

    [Fact]
    public void A_building_terraforms_its_hex_and_replaces_whatever_stood_there()
    {
        var s = Put(Play(SetupStart(G, W)), G, PieceKind.Dwelling, 6);
        Assert.Equal(Terrain.Wasteland, s.HexTerrain[6]);
        Assert.Equal(G, s.Map[6]!.Value.Owner);

        s = Put(s, W, PieceKind.Dwelling, 6);
        Assert.Equal(W, s.Map[6]!.Value.Owner);
        Assert.Equal(Terrain.Forest, s.HexTerrain[6]);
        Assert.All(s[G].DwellingBank, slot => Assert.NotNull(slot)); // the Giants' dwelling went back to its board

        var back = PlayFrom(s, new EditBuilding(s.Map[6]!.Value, null));
        Assert.Null(back.Map[6]);
        Assert.Equal(Terrain.Forest, back.HexTerrain[6]);
    }

    [Fact]
    public void A_priest_in_a_zone_carries_its_steps_both_ways()
    {
        var s = Play(SetupStart(G));
        var priest = s[G].PriestSupply[0];
        var placed = PlayFrom(s, new EditPlacePriest(priest, Track.Water, 0));
        Assert.Equal(3, placed[G].TrackSteps[(int)Track.Water]);
        Assert.Equal(s[G].Power, placed[G].Power); // no power for the steps

        var returned = PlayFrom(placed, new EditReturnPriest(priest));
        Assert.Equal(0, returned[G].TrackSteps[(int)Track.Water]);
        Assert.Equal(s[G].PriestSupply.Count, returned[G].PriestSupply.Count);
        Assert.False(Rules.Apply(s, new EditPlacePriest(priest, Track.Water, 4)).Accepted); // a returning slot
    }

    [Fact]
    public void Favours_follow_the_temples_and_sanctuary()
    {
        var s = Play(SetupStart(G));
        Assert.Equal("The Giants have no favour to take: their buildings give 0.",
            Assert.Single(Rules.Apply(s, new EditFavour(G, 0, Take: true)).Issues).Message);

        s = Put(s, G, PieceKind.Temple, 5);
        Assert.Equal(1, Rules.FavoursToClaim(s, G));
        Assert.Equal(1, s[G].FavoursOwed); // a blank favour on the board
        var taken = PlayFrom(s, new EditFavour(G, 0, Take: true));
        Assert.Equal(0, Rules.FavoursToClaim(taken, G));
        Assert.Equal(0, taken[G].FavoursOwed);
        Assert.False(Rules.Apply(taken, new EditFavour(G, 1, Take: true)).Accepted);

        var templeGone = PlayFrom(taken, new EditBuilding(taken.Map[5]!.Value, null));
        Assert.Equal(1, Rules.FavoursOver(templeGone, G));
        Assert.Contains(Rules.SetupIssues(templeGone), issue => issue.StartsWith("Too many favours: Giants"));
    }

    [Fact]
    public void A_sanctuary_counts_the_temple_it_was_upgraded_from()
    {
        var s = Put(Play(SetupStart(G)), G, PieceKind.Sanctuary, 5);
        Assert.Equal(2, Rules.FavoursDue(s, G));
        Assert.Equal(2, s[G].FavoursOwed);
        s = Put(s, G, PieceKind.Temple, 6);
        Assert.Equal(3, Rules.FavoursToClaim(s, G));

        var chaos = Put(Play(SetupStart(Faction.ChaosMagicians)), Faction.ChaosMagicians, PieceKind.Sanctuary, 5);
        Assert.Equal(4, Rules.FavoursDue(chaos, Faction.ChaosMagicians)); // two for each
    }

    [Fact]
    public void A_town_needs_a_key_and_a_group_that_stops_being_a_town_loses_it()
    {
        var s = Put(Play(SetupStart(G)), G,
            (PieceKind.TradingPost, 0), (PieceKind.TradingPost, 1), (PieceKind.TradingPost, 2), (PieceKind.Dwelling, 3));
        Assert.Equal(ValueList.Of(0, 1, 2, 3), Assert.Single(Rules.PendingTowns(s, G)));
        Assert.Contains(Rules.SetupIssues(s), issue => issue == "Town without a key: Giants at A1");

        var keyed = PlayFrom(s, new EditPlaceKey(G, 0, 1));
        Assert.Empty(Rules.PendingTowns(keyed, G));
        Assert.Equal(1, keyed[G].Keys);
        Assert.Equal(ValueList.Of(0, 1, 2, 3), keyed[G].TownHexes);

        var broken = Rules.Apply(keyed, new EditBuilding(keyed.Map[3]!.Value, null));
        Assert.True(broken.Accepted);
        Assert.Empty(broken.State.PlacedKeys);
        Assert.Equal(0, broken.State[G].Keys);
        Assert.Contains(broken.Events, e => e is KeyRemoved);
    }

    [Fact]
    public void A_second_key_needs_a_group_that_splits_into_two_towns()
    {
        // 14 power in 7 buildings: never two towns of 4.
        var one = Put(Play(SetupStart(G)), G,
            (PieceKind.TradingPost, 0), (PieceKind.TradingPost, 1), (PieceKind.TradingPost, 2), (PieceKind.TradingPost, 3),
            (PieceKind.Temple, 4), (PieceKind.Temple, 5), (PieceKind.Temple, 6));
        one = PlayFrom(one, new EditPlaceKey(G, 0, 0));
        Assert.False(Rules.Apply(one, new EditPlaceKey(G, 0, 6)).Accepted);

        // Two towns of 8 and 9 power joined by a dwelling at 4: taking it away splits them.
        var two = Put(Play(SetupStart(G)), G,
            (PieceKind.TradingPost, 0), (PieceKind.TradingPost, 1), (PieceKind.TradingPost, 2), (PieceKind.Temple, 3),
            (PieceKind.Dwelling, 4),
            (PieceKind.TradingPost, 5), (PieceKind.Temple, 6), (PieceKind.Temple, 7), (PieceKind.Stronghold, 8));
        two = PlayFrom(two, new EditPlaceKey(G, 0, 0), new EditPlaceKey(G, 0, 8));
        Assert.Equal(2, two.PlacedKeys.Count);
        Assert.DoesNotContain(Rules.SetupIssues(two), issue => issue.StartsWith("Too many keys"));
    }

    [Fact]
    public void Done_lists_what_is_missing_then_leads_to_the_turn_order_step()
    {
        var s = Play(SetupStart(G));
        var refused = Rules.Apply(s, new FinishSetup());
        Assert.False(refused.Accepted);
        Assert.Equal(1, s.Round); // round 1 unless set
        Assert.Equal(new[]
        {
            "No pass tile: Giants",
            "Too few buildings: Giants (0 on the map, 2 to start with)",
        }, refused.Issues.Select(issue => issue.Message));

        s = PlayFrom(s, new SetRound(3), new EditPassTile(G, TestPassTile(G)));
        s = Put(s, G, (PieceKind.Dwelling, GiantsStartA), (PieceKind.Dwelling, GiantsStartB));
        Assert.Empty(Rules.SetupIssues(s));

        var playing = PlayFrom(s, new FinishSetup()); // Calculator: no turn-order step, Free at once
        Assert.Equal(Phase.Playing, playing.Phase);
        Assert.True(playing.IsPlanning);
        Assert.Equal(3, playing.Round);
        Assert.Equal(G, playing.ActivePlayer);
        Assert.Equal(15, playing[G].Coins); // no income: the round is under way
    }

    [Fact]
    public void Repeated_edits_of_one_value_are_one_record_and_one_undo()
    {
        var history = new History(Play(SetupStart(G)));
        for (var i = 0; i < 7; i++)
            Assert.True(history.Apply(new EditResource(G, Resource.Coins, 1)).Accepted);
        var log = history.Log();
        Assert.Equal(2, log.Count);
        Assert.Equal("Giants coins 15 → 22", GameLog.Describe(log[^1]));

        history.Apply(new EditResource(G, Resource.Workers, 1));
        history.Apply(new EditResource(G, Resource.Coins, 1));
        Assert.Equal(4, history.Log().Count);

        history.Undo();
        history.Undo();
        history.Undo();
        Assert.Equal(15, history.Current[G].Coins);
    }

    // ---------- Free play ----------

    [Fact]
    public void Free_play_takes_any_number_of_actions_and_hands_the_turn_back()
    {
        var history = new History(Play(TwoPlayerStart));
        Assert.True(history.Apply(new BurnPower(W)).Accepted);
        Assert.True(history.Apply(new SetMode(PlayMode.Free)).Accepted);
        Assert.True(history.Current.IsPlanning);
        Assert.NotNull(history.Log()[^2].Bookmark); // where Free began

        Assert.True(history.Apply(new SelectFaction(G)).Accepted);
        Assert.True(history.Apply(UpgradeTo(G, PieceKind.TradingPost, 0, GiantsStartA)).Accepted);
        Assert.True(history.Apply(UpgradeTo(G, PieceKind.TradingPost, 1, GiantsStartB)).Accepted); // a second action at once
        Assert.True(history.Apply(End(G)).Accepted);
        Assert.Equal(G, history.Current.ActivePlayer);
        Assert.EndsWith("(Free)", GameLog.Describe(history.Log()[^2]).Split(" · ")[0]);

        Assert.True(history.Apply(new SetMode(PlayMode.Lenient)).Accepted);
        var s = history.Current;
        Assert.False(s.IsPlanning);
        Assert.Equal(W, s.ActivePlayer);
        Assert.False(s.ActionTaken);
        Assert.Equal(PieceKind.TradingPost, s.Map[GiantsStartB]!.Value.Kind);
    }

    [Fact]
    public void Leaving_Free_for_Strict_runs_the_checks()
    {
        var s = Play(TwoPlayerStart);
        s = s.WithPlayer(s[G] with { Coins = 0 });
        s = PlayFrom(s, new SetMode(PlayMode.Free), new SelectFaction(G), UpgradeTo(G, PieceKind.TradingPost, 0, GiantsStartA));
        Assert.True(s[G].Coins < 0);

        var strict = Rules.Apply(s, new SetMode(PlayMode.Strict));
        Assert.False(strict.Accepted);
        Assert.Equal("Strict can't begin while the Giants are in debt (-6 coins).", Assert.Single(strict.Issues).Message);
    }
}
