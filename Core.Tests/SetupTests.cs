using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Setup, in order: game, factions, turn order, map, round tiles, pass tiles, Start;
// then starting dwellings and the pass tile picks.
public class SetupTests
{
    // ---------- Game and factions ----------

    [Fact]
    public void Initial_state_waits_for_the_game_to_be_chosen()
    {
        var s = GameState.Initial;
        Assert.Equal(Phase.ChoosingGame, s.Phase);
        Assert.Null(s.Game);
        Assert.Equal(0, s.Round);
        Assert.Empty(s.Players);
        Assert.Null(s.ActivePlayer);
        Assert.Equal(s.Layout.HexCount, s.Map.Count);
        Assert.Equal(s.Layout.HexCount, s.HexTerrain.Count);
        Assert.All(s.Map, hex => Assert.Null(hex));
        Assert.Equal(Config.TrackCount, s.PriestZones.Count);
        Assert.All(s.PriestZones, zone => Assert.All(zone, slot => Assert.Null(slot)));
        Assert.Empty(s.ChosenRoundTiles);
        Assert.Empty(s.PassTileBank);
        Assert.All(s.PassTileCoins, coins => Assert.Equal(0, coins));
    }

    [Fact]
    public void The_game_is_chosen_before_factions()
    {
        Assert.False(Rules.Apply(GameState.Initial, new AddPlayer(G)).Accepted);

        var s = Play(ChooseTM);
        Assert.Equal(Phase.ChoosingFactions, s.Phase);
        Assert.Equal(GameKind.TerraMystica, s.Game);
        Assert.False(Rules.Apply(s, ChooseTM).Accepted); // only once
    }

    [Fact]
    public void Adding_a_faction_creates_its_player_with_a_full_dwelling_bank()
    {
        var p = Play(ChooseTM, new AddPlayer(M))[M];

        Assert.Equal(PlayerColour.Blue, p.Colour);
        Assert.Equal(0, p.Coins);
        Assert.Equal(0, p.Workers);
        Assert.Equal(1, p.Shipping); // the Mermaids start with shipping 1
        Assert.Null(p.PassTile);
        Assert.Equal(Config.PriestsPerPlayer, p.PriestSupply.Count);
        Assert.Equal((Piece?)new Piece(M, PieceKind.Priest, 0), p.PriestSupply[0]); // gained first
        Assert.Empty(p.Priests);
        Assert.Equal(ValueList.Of(0, 2, 0, 0), p.TrackSteps); // the Mermaids start with 2 Water (no power for it)
        Assert.Equal(Config.DwellingsPerPlayer, p.DwellingBank.Count);
        for (var i = 0; i < Config.DwellingsPerPlayer; i++)
            Assert.Equal((Piece?)new Piece(M, PieceKind.Dwelling, i), p.DwellingBank[i]);
    }

    [Fact]
    public void A_faction_cannot_be_added_twice()
    {
        var before = Play(ChooseTM, new AddPlayer(G));
        var result = Rules.Apply(before, new AddPlayer(G));
        Assert.False(result.Accepted);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_faction_can_be_removed_while_factions_are_chosen()
    {
        var s = Play(ChooseTM, new AddPlayer(G), new AddPlayer(W), new RemovePlayer(G));
        Assert.False(s.IsInGame(G));
        Assert.True(s.IsInGame(W));
    }

    [Fact]
    public void Proceeding_with_no_factions_is_refused()
    {
        Assert.False(Rules.Apply(Play(ChooseTM), new ConfirmFactions()).Accepted);
    }

    [Fact]
    public void Start_is_refused_before_the_setup_is_finished()
    {
        Assert.Equal("Finish the setup first.",
            Assert.Single(Rules.Apply(Play(ChooseTM, new AddPlayer(G)), new StartGame()).Issues).Message);
    }

    [Fact]
    public void Factions_cannot_change_after_confirming()
    {
        var s = Play(Confirmed(G));
        Assert.False(Rules.Apply(s, new AddPlayer(W)).Accepted);
        Assert.False(Rules.Apply(s, new RemovePlayer(G)).Accepted);
    }

    // ---------- Turn order ----------

    [Fact]
    public void One_faction_alone_plays_in_Calculator_without_a_turn_order()
    {
        Assert.Equal("Choose at least two factions (one faction alone plays in Calculator mode).",
            Assert.Single(Rules.Apply(Play(ChooseTM, new AddPlayer(G)), new ConfirmFactions()).Issues).Message);
        Assert.Equal("Calculator plays one faction.",
            Assert.Single(Rules.Apply(Play(ChooseCalculator, new AddPlayer(G)), new AddPlayer(W)).Issues).Message);

        var s = Play(Confirmed(G));
        Assert.Equal(Phase.ChoosingMap, s.Phase);
        Assert.Equal(ValueList.Of(G), s.TurnOrder);
        Assert.Equal(PlayMode.Free, s.Mode);
        Assert.False(Rules.Apply(s, new SetMode(PlayMode.Lenient)).Accepted);
    }

    [Fact]
    public void Multiplayer_confirm_waits_for_turn_order()
    {
        var s = Play(ChooseTM, new AddPlayer(G), new AddPlayer(W), new ConfirmFactions());
        Assert.Equal(Phase.ChoosingTurnOrder, s.Phase);
        Assert.Empty(s.TurnOrder);
        Assert.False(s.IsTurnOrderComplete);
        Assert.Null(s.ActivePlayer);
        Assert.Equal(0, s[G].Coins);
    }

    [Fact]
    public void Choosing_all_but_one_places_the_last_automatically()
    {
        var s = Play(Concat(ThreePlayersConfirmed, new Move[] { new ChooseTurnOrder(M) }));
        Assert.Equal(ValueList.Of(M), s.TurnOrder); // two still unplaced: nothing automatic yet

        s = PlayFrom(s, new ChooseTurnOrder(G));
        Assert.Equal(ValueList.Of(M, G, W), s.TurnOrder);
        Assert.True(s.IsTurnOrderComplete);
        Assert.Equal(Phase.ChoosingTurnOrder, s.Phase); // still waiting for Proceed
    }

    [Fact]
    public void Proceeding_is_refused_until_the_turn_order_is_complete()
    {
        var s = Play(Concat(ThreePlayersConfirmed, new Move[] { new ChooseTurnOrder(M) }));
        Assert.Equal("Finish choosing the turn order first.", Assert.Single(Rules.Apply(s, new ConfirmTurnOrder()).Issues).Message);
    }

    [Fact]
    public void Turn_order_choices_must_be_new_factions_that_are_in_the_game()
    {
        var choosing = Play(Concat(ThreePlayersConfirmed, new Move[] { new ChooseTurnOrder(W) }));
        Assert.False(Rules.Apply(choosing, new ChooseTurnOrder(W)).Accepted); // already placed

        var twoPlayers = Play(ChooseTM, new AddPlayer(G), new AddPlayer(W), new ConfirmFactions());
        Assert.False(Rules.Apply(twoPlayers, new ChooseTurnOrder(M)).Accepted); // not playing
    }

    [Fact]
    public void Unchoosing_removes_that_place_and_every_later_one()
    {
        var full = Play(Concat(ThreePlayersConfirmed, new Move[] { new ChooseTurnOrder(W), new ChooseTurnOrder(G) }));
        Assert.Equal(ValueList.Of(W, G, M), full.TurnOrder);

        var result = Rules.Apply(full, new UnchooseTurnOrder(G));
        Assert.True(result.Accepted);
        Assert.Equal(ValueList.Of(W), result.State.TurnOrder);

        Assert.Empty(PlayFrom(full, new UnchooseTurnOrder(W)).TurnOrder);
    }

    [Fact]
    public void The_automatic_last_place_cannot_be_unchosen()
    {
        var full = Play(Concat(ThreePlayersConfirmed, new Move[] { new ChooseTurnOrder(W), new ChooseTurnOrder(G) }));
        Assert.False(Rules.Apply(full, new UnchooseTurnOrder(M)).Accepted);
    }


    [Fact]
    public void An_unplaced_faction_cannot_be_unchosen()
    {
        var s = Play(ThreePlayersConfirmed);
        Assert.False(Rules.Apply(s, new UnchooseTurnOrder(W)).Accepted);
    }

    // ---------- Map ----------

    [Fact]
    public void Confirming_the_turn_order_leads_to_the_map_then_the_round_tiles()
    {
        var s = Play(Concat(Confirmed(W, G), new Move[] { new ConfirmTurnOrder() }));
        Assert.Equal(Phase.ChoosingMap, s.Phase);

        s = PlayFrom(s, new ChooseMap(MapId.Original));
        Assert.Equal(Phase.ChoosingRoundTiles, s.Phase);
        Assert.Equal(MapId.Original, s.ChosenMap);
        Assert.Equal(Maps.Base.HexTerrain, s.HexTerrain);
    }

    [Fact]
    public void Choosing_another_map_replaces_the_layout_and_terrain()
    {
        var s = Play(Concat(Confirmed(W, G), new Move[] { new ConfirmTurnOrder(), new ChooseMap(MapId.FireAndIce) }));
        Assert.Equal(MapId.FireAndIce, s.ChosenMap);
        Assert.Equal(Maps.FireAndIce.Layout, s.Layout);
        Assert.Equal(Maps.FireAndIce.HexTerrain, s.HexTerrain);
        Assert.Equal(112, s.Map.Count);
    }

    // ---------- Round tiles ----------

    private static GameState ChoosingRoundTiles() =>
        Play(Concat(Confirmed(W, G), new Move[] { new ConfirmTurnOrder(), new ChooseMap(MapId.Original) }));

    [Fact]
    public void Round_tiles_are_numbered_in_the_order_chosen()
    {
        var result = Rules.Apply(PlayFrom(ChoosingRoundTiles(), new ChooseRoundTile(3)), new ChooseRoundTile(0));

        Assert.Equal(ValueList.Of(3, 0), result.State.ChosenRoundTiles);
        Assert.Contains<GameEvent>(new RoundTileChosen(0, 2), result.Events);
        Assert.False(Rules.Apply(result.State, new ChooseRoundTile(3)).Accepted); // already chosen
    }

    [Fact]
    public void Unchoosing_a_round_tile_removes_it_and_every_later_one()
    {
        var s = PlayFrom(ChoosingRoundTiles(), new ChooseRoundTile(3), new ChooseRoundTile(0), new ChooseRoundTile(5));
        var result = Rules.Apply(s, new UnchooseRoundTile(0));

        Assert.Equal(ValueList.Of(3), result.State.ChosenRoundTiles);
    }

    [Fact]
    public void Exactly_six_round_tiles_are_needed_to_proceed()
    {
        var five = PlayFrom(ChoosingRoundTiles(), Enumerable.Range(0, 5).Select(t => (Move)new ChooseRoundTile(t)).ToArray());
        Assert.False(Rules.Apply(five, new ConfirmRoundTiles()).Accepted);

        var six = PlayFrom(five, new ChooseRoundTile(8));
        Assert.False(Rules.Apply(six, new ChooseRoundTile(7)).Accepted); // no seventh

        var proceeded = PlayFrom(six, new ConfirmRoundTiles());
        Assert.Equal(Phase.ChoosingPassTiles, proceeded.Phase);
        Assert.Equal(ValueList.Of(0, 1, 2, 3, 4, 8), proceeded.ChosenRoundTiles);
    }

    // ---------- Pass tiles ----------

    private static GameState ChoosingPassTiles() =>
        PlayFrom(ChoosingRoundTiles(), Concat(TestRoundTiles.Select(t => (Move)new ChooseRoundTile(t)).ToArray(),
            new Move[] { new ConfirmRoundTiles() }));

    [Fact]
    public void Pass_tiles_are_added_and_removed()
    {
        var s = PlayFrom(ChoosingPassTiles(), new AddPassTile(2), new AddPassTile(7));
        Assert.Equal(ValueList.Of(2, 7), s.ChosenPassTiles);
        Assert.False(Rules.Apply(s, new AddPassTile(2)).Accepted);

        s = PlayFrom(s, new RemovePassTile(2));
        Assert.Equal(ValueList.Of(7), s.ChosenPassTiles);
    }

    [Fact]
    public void Start_needs_exactly_three_more_pass_tiles_than_factions()
    {
        var four = PlayFrom(ChoosingPassTiles(), new[] { 0, 1, 2, 3 }.Select(t => (Move)new AddPassTile(t)).ToArray());
        Assert.Equal("Choose 5 pass tiles (3 more than the number of factions).",
            Assert.Single(Rules.Apply(four, new StartGame()).Issues).Message);

        var five = PlayFrom(four, new AddPassTile(4));
        Assert.True(Rules.Apply(five, new StartGame()).Accepted);
        Assert.Equal("A game with 2 factions has 5 pass tiles (3 more than the number of factions).",
            Assert.Single(Rules.Apply(five, new AddPassTile(9)).Issues).Message);
    }

    [Fact]
    public void Calculator_takes_any_round_tiles_and_all_pass_tiles_or_at_least_a_two_player_game_s()
    {
        var s = Play(Concat(Confirmed(G), new Move[] { new ChooseMap(MapId.Original), new ChooseRoundTile(0), new ConfirmRoundTiles() }));
        Assert.Equal(ValueList.Of(0), s.ChosenRoundTiles); // rounds 2-6 blank

        var all = PlayFrom(s, new StartGame());
        Assert.Equal(PassTiles.Count, all.PassTileBank.Count);

        var three = PlayFrom(s, new AddPassTile(0), new AddPassTile(1), new AddPassTile(2));
        // At least a two-player game's 5 (GameState.PassTilesNeeded).
        Assert.Equal("Choose at least 5 pass tiles, or none for all of them.", Assert.Single(Rules.Apply(three, new StartGame()).Issues).Message);
        Assert.True(Rules.Apply(PlayFrom(three, new AddPassTile(3), new AddPassTile(4)), new StartGame()).Accepted);
    }

    [Fact]
    public void Start_fills_the_bank_in_tile_order()
    {
        var s = PlayFrom(ChoosingPassTiles(), new[] { 7, 2, 4, 0, 9 }.Select(t => (Move)new AddPassTile(t)).ToArray());
        s = PlayFrom(s, new StartGame());
        Assert.Equal(ValueList.Of<int?>(0, 2, 4, 7, 9), s.PassTileBank);
    }

    // ---------- Starting dwellings ----------

    [Fact]
    public void Start_pays_starting_coins_and_begins_snake_order_placement()
    {
        var s = Play(Placing(W, G));

        Assert.Equal(Phase.PlacingDwellings, s.Phase);
        Assert.Equal(ValueList.Of(W, G, G, W), s.PlacementQueue);
        Assert.Equal(W, s.ActivePlayer);
        Assert.Equal(Config.StartingCoins, s[W].Coins);
        Assert.Equal(Config.StartingCoins, s[G].Coins);
        Assert.Equal(3, s[W].Workers);
        Assert.Equal(3, s[G].Workers);
        Assert.Equal(0, s.Round);
    }

    [Fact]
    public void A_starting_dwelling_is_free_and_the_next_placement_follows_at_once()
    {
        var s = Play(Concat(Placing(W, G), new[] { StartAt(W, 0, WitchesStartA) }));

        Assert.Equal((Piece?)new Piece(W, PieceKind.Dwelling, 0), s.Map[WitchesStartA]);
        Assert.Null(s[W].DwellingBank[0]);
        Assert.Equal(Config.StartingCoins, s[W].Coins);
        Assert.Equal(G, s.ActivePlayer);
        Assert.Equal(ValueList.Of(G, G, W), s.PlacementQueue);
    }

    [Fact]
    public void Starting_dwellings_must_go_on_starting_terrain()
    {
        var result = Rules.Apply(Play(Placing(W)), StartAt(W, 0, 8)); // hex 8 is Wasteland

        Assert.False(result.Accepted);
        Assert.Equal("The Witches' starting dwellings must be placed on Forest.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Starting_dwellings_are_placed_in_turn_and_not_on_occupied_hexes()
    {
        var s = Play(Placing(W, G));
        Assert.False(Rules.Apply(s, StartAt(G, 0, GiantsStartA)).Accepted); // Witches place first

        s = PlayFrom(s, StartAt(W, 0, WitchesStartA));
        Assert.False(Rules.Apply(s, StartAt(G, 0, WitchesStartA)).Accepted); // occupied (and not Wasteland)
    }

    [Fact]
    public void Round_moves_are_refused_while_starting_dwellings_are_placed()
    {
        var s = Play(Placing(G));
        Assert.False(Rules.Apply(s, Build(G, 0, 4)).Accepted);
        Assert.False(Rules.Apply(s, new ConvertPowerToCoin(G)).Accepted);
        Assert.False(Rules.Apply(s, new Pass(G)).Accepted);
    }

    // ---------- Pass tile picks ----------

    private static readonly Move[] TwoPlayersPlaced = Concat(Placing(W, G), new[]
    {
        StartAt(W, 0, WitchesStartA), StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB), StartAt(W, 1, WitchesStartB),
    });

    [Fact]
    public void After_the_starting_dwellings_pass_tiles_are_picked_in_reverse_turn_order()
    {
        var s = Play(TwoPlayersPlaced);

        Assert.Equal(Phase.PickingPassTiles, s.Phase);
        Assert.Equal(G, s.ActivePlayer);
        Assert.Equal(ValueList.Of(G, W), s.PlacementQueue);
        Assert.False(Rules.Apply(s, new PickPassTile(W, 0)).Accepted);
        Assert.False(Rules.Apply(s, Build(G, 2, 4)).Accepted);
    }

    [Fact]
    public void Picking_takes_the_tile_out_of_the_bank()
    {
        var s = PlayFrom(Play(TwoPlayersPlaced), new PickPassTile(G, 5));

        Assert.Equal(5, s[G].PassTile);
        Assert.DoesNotContain((int?)5, s.PassTileBank);
        Assert.Equal(9, s.PassTileBank[3]); // the tiles after it close up to the left (bank 0, 1, 3, 5, 9)
        Assert.Null(s.PassTileBank[^1]);
        Assert.Equal(W, s.ActivePlayer);
        Assert.False(Rules.Apply(s, new PickPassTile(W, 5)).Accepted); // already taken
    }

    [Fact]
    public void After_the_last_pick_coins_go_on_the_bank_and_round_one_begins_with_income()
    {
        var s = Play(TwoPlayerStart);

        Assert.Equal(Phase.Playing, s.Phase);
        Assert.Equal(1, s.Round);
        Assert.Equal(W, s.ActivePlayer);
        Assert.False(s.ActionTaken);
        Assert.Empty(s.PlacementQueue);
        Assert.Equal(1, s.PassTileCoins[0]);   // left in the bank
        Assert.Equal(0, s.PassTileCoins[9]);   // picked by the Giants
        Assert.Equal(15, s[W].Coins);          // 15 starting; no coin income yet
        Assert.Equal(15, s[G].Coins);
        Assert.Equal(7, s[W].Workers);         // 3 starting + 1 base + 1 under each of two emptied slots + 1 (pass tile 3)
        Assert.Equal(6, s[G].Workers);         // 3 starting + 1 base + 2
        Assert.Equal(new Power(2, 10, 0), s[G].Power); // 5/7/0, and pass tile 9: 3 power
    }

    [Fact]
    public void A_solo_player_places_both_starting_dwellings()
    {
        var s = Play(SoloStart);
        Assert.Equal(1, s.Round);
        Assert.Equal(G, s.Map[GiantsStartA]?.Owner);
        Assert.Equal(G, s.Map[GiantsStartB]?.Owner);
        Assert.Equal(15, s[G].Coins);
        Assert.Equal(6, s[G].Workers);
    }

    [Fact]
    public void Picking_is_only_for_after_the_starting_dwellings()
    {
        Assert.False(Rules.Apply(Play(SoloStart), new PickPassTile(G, 0)).Accepted);
    }

    [Fact]
    public void Game_moves_are_refused_before_round_one()
    {
        var choosing = Play(ChooseTM, new AddPlayer(G), new AddPlayer(W), new ConfirmFactions());
        Assert.False(Rules.Apply(choosing, Build(G, 0, 0)).Accepted);
        Assert.False(Rules.Apply(choosing, StartAt(G, 0, GiantsStartA)).Accepted);
        Assert.False(Rules.Apply(choosing, new ConvertPowerToCoin(G)).Accepted);
        Assert.False(Rules.Apply(choosing, new Pass(G)).Accepted);
        Assert.False(Rules.Apply(choosing, End(G)).Accepted);
    }
}
