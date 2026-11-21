using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Fan faction tests without Fire & Ice (Rules.FanFactions.cs; those with it are in FanFactionFireAndIceTests).
// Most tests start one faction alone (Solo, Calculator, Free play) on the base map with the fan factions chosen
// instead of the special landscapes.
// Home hexes: Wasteland 5 and 112; Desert 4 and 88; Plains 16 and 107; Swamp 17 and 101; Lake 3 and 89; Forest 9 and
// 60; Mountain 1 and 108. Next to 3: 2 Forest, 4 Desert, 16 Plains. Next to 9: 10 Lake. Next to 16: 4 Desert.
public class FanFactionTests
{
    private const Faction Ar = Faction.Architects, Tr = Faction.Treasurers, Dj = Faction.Djinni, Av = Faction.Archivists,
        TT = Faction.TimeTravellers, Pr = Faction.Prospectors, Gb = Faction.Goblins, Wi = Faction.Wisps,
        CD = Faction.ChashDallah, En = Faction.Enlightened, Co = Faction.Conspirators, Dy = Faction.DynionGeifr;

    private static readonly GameOptions FanOptions = GameOptions.All with { Calculator = true, Landscapes = false, FanFactions = true };

    // Solo (TestHelpers) with the fan factions in the game.
    private static Move[] FanSolo(Faction f, params int[] hexes)
    {
        var moves = Solo(f, hexes);
        moves[0] = new ChooseGame(GameKind.TerraMystica, FanOptions);
        return moves;
    }

    // The Djinni alone on 4 and 88, their starting steps on Water.
    private static GameState DjinniStart()
    {
        var moves = FanSolo(Dj, 4, 88).ToList();
        moves.Insert(moves.Count - 1, new TakeStartingSteps(Dj, Track.Water)); // before the pass tile is picked
        return Play(moves.ToArray());
    }

    private static int _nextNumber = 50; // piece numbers for buildings put down by the tests (only IDs)

    private static GameState Put(GameState s, Faction f, params (int Hex, PieceKind Kind)[] buildings)
    {
        foreach (var (hex, kind) in buildings)
            s = s with { Map = s.Map.SetItem(hex, new Piece(f, kind, _nextNumber++)) };
        return s;
    }

    private static GameState WithTradingPost(GameState s, Faction f, int hex) =>
        s.WithPlayer(s[f] with { TradingPostBank = s[f].TradingPostBank.SetItem(0, null) }) with
        {
            Map = s.Map.SetItem(hex, new Piece(f, PieceKind.TradingPost, 0)),
        };

    private static GameState WithStronghold(GameState s, Faction f) =>
        s.WithPlayer(s[f] with { StrongholdBank = ValueList<Piece?>.Repeat(null, Config.StrongholdsPerPlayer) });

    private static GameState Rich(GameState s, Faction f) => s.WithPlayer(s[f] with { Coins = 30, Workers = 30 });

    private static string Refusal(GameState s, Move move) => Assert.Single(Rules.Apply(s, move).Issues).Message;

    // ---------- Joining ----------

    [Fact]
    public void Fan_factions_join_only_with_their_option_and_once_playable()
    {
        Assert.Equal("The Djinni are a fan faction, and the fan factions are not in this game.",
            Refusal(Play(ChooseCalculator), new AddPlayer(Dj)));
        var fan = Play(new ChooseGame(GameKind.TerraMystica, FanOptions));
        Assert.True(Rules.Apply(fan, new AddPlayer(Faction.Selkies)).Accepted);
        Assert.Equal("The Selkies are from Fire & Ice, which is not in this game.",
            Refusal(Play(new ChooseGame(GameKind.TerraMystica, FanOptions with { FireAndIce = false })), new AddPlayer(Faction.Selkies)));
        Assert.True(Rules.Apply(fan, new AddPlayer(Dj)).Accepted);
    }

    [Fact]
    public void The_Archivists_add_a_pass_tile_to_the_game()
    {
        var s = Play(new ChooseGame(GameKind.TerraMystica, FanOptions with { Calculator = false }), new AddPlayer(Av), new AddPlayer(G));
        Assert.Equal(2 + Config.ExtraPassTiles + Config.ArchivistsExtraPassTiles, s.PassTilesNeeded);
    }

    // ---------- Djinni ----------

    [Fact]
    public void Djinni_take_their_starting_steps_after_their_dwellings_before_the_pass_tiles()
    {
        var moves = FanSolo(Dj, 4, 88);
        var placed = Play(moves[..^1]);
        Assert.Equal(Phase.PlacingDwellings, placed.Phase); // their turn among the last placements
        Assert.True(Rules.IsStartingStepsTurn(placed));
        Assert.Equal("The Djinni choose the track for their starting steps now.",
            Refusal(placed, new PlaceStartingDwelling(new Piece(Dj, PieceKind.Dwelling, 2), 16)));
        var before = placed[Dj].TrackSteps[(int)Track.Water];
        var started = PlayFrom(placed, new TakeStartingSteps(Dj, Track.Water), moves[^1]);
        Assert.Equal(before + Config.DjinniStartingSteps, started[Dj].TrackSteps[(int)Track.Water]);
        Assert.Equal(0, started[Dj].StartingStepsOwed);
    }

    [Fact]
    public void Djinni_swap_two_trackers_for_a_lamp()
    {
        var s = DjinniStart();
        int water = s[Dj].TrackSteps[(int)Track.Water], fire = s[Dj].TrackSteps[(int)Track.Fire];
        var result = Rules.Apply(s, new SwapTracks(Dj, Track.Water, Track.Fire));
        Assert.True(result.Accepted);
        Assert.Equal(fire, result.State[Dj].TrackSteps[(int)Track.Water]);
        Assert.Equal(water, result.State[Dj].TrackSteps[(int)Track.Fire]);
        Assert.Equal(Config.DjinniLamps - 1, result.State[Dj].Lamps);
        Assert.Equal("Djinni swap their Water and Fire steps (book action)",
            GameLog.Describe(s, new SwapTracks(Dj, Track.Water, Track.Fire), result.Events, result.State));

        Assert.Equal("The Djinni have no magic lamp left.", Refusal(s.WithPlayer(s[Dj] with { Lamps = 0 }), new SwapTracks(Dj, Track.Water, Track.Fire)));
    }

    // ---------- Time Travellers ----------

    [Fact]
    public void Time_Travellers_score_the_previous_and_next_round_tiles()
    {
        var s = Play(FanSolo(TT, 16, 107)); // round 1: round 6's tile and round 2's
        Assert.Equal(new[] { RoundTiles.All[TestRoundTiles[5]], RoundTiles.All[TestRoundTiles[1]] }, Rules.ScoringTiles(s, TT));
        Assert.Equal(new[] { RoundTiles.All[TestRoundTiles[0]] }, Rules.ScoringTiles(s, Pr));
    }

    // ---------- Prospectors ----------

    [Fact]
    public void Prospectors_pay_coins_for_spades_and_gain_a_point_and_power_for_each()
    {
        var s = Play(FanSolo(Pr, 16, 107));
        s = s.WithPlayer(s[Pr] with { Coins = 20, Power = new Power(5, 7, 0) });
        var after = PlayFrom(s, new TransformTerrain(Pr, 4, Terrain.Plains)); // Desert to Plains: 1 spade
        Assert.Equal(20 - Config.ProspectorsSpadeCoins, after[Pr].Coins);
        Assert.Equal(s[Pr].Workers, after[Pr].Workers);
        Assert.Equal(s[Pr].VictoryPoints + Config.ProspectorsVictoryPointsPerSpade, after[Pr].VictoryPoints);
        Assert.Equal(new Power(4, 8, 0), after[Pr].Power);
    }

    [Fact]
    public void Prospectors_golden_spades_do_not_score_the_spade_round_tile()
    {
        var s = Play(FanSolo(Pr, 16, 107));
        s = s.WithPlayer(s[Pr] with { Coins = 20 }) with { ChosenRoundTiles = ValueList.Of(4, 0, 1, 2, 3, 6) }; // round 1: 2 VP a spade
        var after = PlayFrom(s, new TransformTerrain(Pr, 4, Terrain.Plains)); // Desert to Plains: 1 spade
        Assert.Equal(s[Pr].VictoryPoints + Config.ProspectorsVictoryPointsPerSpade, after[Pr].VictoryPoints); // as on BGA (table 932291383)
    }

    [Fact]
    public void Prospectors_take_a_priest_for_every_spade_they_are_given()
    {
        var s = Charged(Play(FanSolo(Pr, 16, 107)));
        var after = PlayFrom(s, new TakePowerAction(Pr, 5)); // 2 spades
        Assert.Equal(s[Pr].Priests.Count + 2, after[Pr].Priests.Count);
        Assert.Equal(0, after[Pr].SpadesOwed);
    }

    [Fact]
    public void Prospectors_spades_given_as_priests_still_score_the_spade_round_tile()
    {
        // As on BGA (table 433470452 #110: 2 spades from the power action, 2 priests, +4 VP on a 2-VP-a-spade tile).
        var s = Charged(Play(FanSolo(Pr, 16, 107))) with { ChosenRoundTiles = ValueList.Of(4, 0, 1, 2, 3, 6) };
        var after = PlayFrom(s, new TakePowerAction(Pr, 5)); // 2 spades
        Assert.Equal(s[Pr].VictoryPoints + 4, after[Pr].VictoryPoints);
    }

    // ---------- Goblins ----------

    [Fact]
    public void Goblins_spend_a_treasure_for_power_by_their_dwellings()
    {
        var s = Play(FanSolo(Gb, 17, 101));
        s = s.WithPlayer(s[Gb] with { Power = new Power(5, 7, 0) });
        var after = PlayFrom(s, new SpendTreasure(Gb, PieceKind.Dwelling));
        Assert.Equal(new Power(3, 9, 0), after[Gb].Power); // 2 dwellings
        Assert.Equal(Config.GoblinsStartingTreasures - 1, after[Gb].Treasures);
        Assert.Equal("The Goblins have no treasure left.",
            Refusal(s.WithPlayer(s[Gb] with { Treasures = 0 }), new SpendTreasure(Gb, PieceKind.Dwelling)));
        Assert.Equal("The Goblins have no temple on the map.", Refusal(s, new SpendTreasure(Gb, PieceKind.Temple)));
    }

    [Fact]
    public void Goblins_find_a_treasure_with_every_temple()
    {
        var s = Rich(WithTradingPost(Play(FanSolo(Gb, 17, 101)), Gb, 17), Gb);
        var after = PlayFrom(s, UpgradeTo(Gb, PieceKind.Temple, 0, 17));
        Assert.Equal(s[Gb].Treasures + 1, after[Gb].Treasures);
    }

    // ---------- Wisps ----------

    [Fact]
    public void Wisps_get_a_spade_next_to_every_new_trading_post()
    {
        var s = PlayFrom(Rich(Play(FanSolo(Wi, 3, 89)), Wi), UpgradeTo(Wi, PieceKind.TradingPost, 0, 3));
        Assert.Equal(3, s.SpadeAnchorHex);
        Assert.Equal(1, s[Wi].SpadesOwed);
        Assert.Equal("The Wisps' free spade goes on a hex next to their new trading post.",
            Refusal(s, new SpendSpades(Wi, 90, Terrain.Swamp)));
        Assert.Equal("The Wisps' free spade can't be topped up: choose a terrain 1 spade away.",
            Refusal(s, new SpendSpades(Wi, 4, Terrain.Lake))); // Desert to Lake: 3 spades

        var spent = PlayFrom(s, new SpendSpades(Wi, 16, Terrain.Swamp)); // Plains to Swamp: 1 spade
        Assert.Equal(Terrain.Swamp, spent.HexTerrain[16]);
        Assert.Null(spent.SpadeAnchorHex);
        Assert.Equal(0, spent[Wi].SpadesOwed);
        Assert.Null(spent.Map[16]); // nothing built there
    }

    [Fact]
    public void The_Wisps_spade_may_go_across_their_bridge()
    {
        // 89 and 64 are joined by a bridge across rivers 76 and 77 (BGA table 932291383: the spade went that way).
        var s = Rich(Play(FanSolo(Wi, 3, 89)), Wi) with { Bridges = ValueList.Of(new Bridge(Wi, 64, 89)) };
        s = PlayFrom(s, UpgradeTo(Wi, PieceKind.TradingPost, 0, 89));
        Assert.Equal(1, s[Wi].SpadesOwed);

        var spent = PlayFrom(s, new SpendSpades(Wi, 64, Terrain.Lake)); // Forest to Lake: 1 spade
        Assert.Equal(Terrain.Lake, spent.HexTerrain[64]);
        Assert.Equal(0, spent[Wi].SpadesOwed);
    }

    // ---------- Architects ----------

    [Fact]
    public void Architects_build_bridges_for_a_priest()
    {
        var s = Put(PlayFrom(Charged(Play(FanSolo(Ar, 5, 112))), GetPriest(Ar)), Ar, (17, PieceKind.Dwelling));
        var after = PlayFrom(s, new BuildBridge(Ar));
        Assert.Equal(1, after[Ar].BridgesOwed);
        Assert.Equal(s[Ar].Priests.Count - 1, after[Ar].Priests.Count);
        Assert.Equal(s[Ar].Workers, after[Ar].Workers);
    }

    [Fact]
    public void Architects_save_a_spade_building_across_their_own_bridge()
    {
        var s = Rich(Put(Play(FanSolo(Ar, 5, 112)), Ar, (17, PieceKind.Dwelling)), Ar);
        s = s with { Bridges = s.Bridges.Add(new Bridge(Ar, 17, 31)) };
        var after = PlayFrom(s, Build(Ar, 2, 31)); // Forest to Wasteland: 2 spades, 1 saved
        var dwelling = Factions.BuildingCost(Ar, PieceKind.Dwelling);
        Assert.Equal(s[Ar].Workers - s[Ar].SpadeCost.Workers - dwelling.Workers, after[Ar].Workers);
        Assert.Equal(Terrain.Wasteland, after.HexTerrain[31]);
    }

    // As on BGA (649833286 #342): free spades in the main phase take the bridge's discount too, and the
    // dwelling must then go there.
    [Fact]
    public void Architects_free_spades_take_the_bridge_discount_and_the_dwelling_must_follow()
    {
        var s = Rich(Put(Charged(Play(FanSolo(Ar, 5, 112))), Ar, (17, PieceKind.Dwelling)), Ar);
        s = s with { Bridges = s.Bridges.Add(new Bridge(Ar, 17, 31)) };
        var spade = PlayFrom(s, new TakePowerAction(Ar, 4)); // 1 spade
        var spent = PlayFrom(spade, new SpendSpades(Ar, 31, Terrain.Wasteland)); // Forest to Wasteland: 2 spades, 1 saved
        Assert.Equal(spade[Ar].Workers, spent[Ar].Workers); // nothing paid
        Assert.Equal(Terrain.Wasteland, spent.HexTerrain[31]);
        Assert.Equal(31, spent[Ar].ArchitectsBuildHex);
        Assert.StartsWith("The Architects' bridge saved spades", Refusal(spent, new FinishSpades(Ar)));
        var built = PlayFrom(spent, new BuildOnSpadedHex(spent[Ar].NextBuilding(PieceKind.Dwelling)!.Value, 31));
        Assert.Equal(Ar, built.Map[31]!.Value.Owner);
        Assert.Null(built[Ar].ArchitectsBuildHex);
    }

    // As on BGA (608085870 #326): an opponent across the builder's own bridge makes a trading post cheap.
    [Fact]
    public void An_opponent_across_their_own_bridge_makes_a_trading_post_cheap()
    {
        var s = Put(Play(FanSolo(Ar, 5, 112)), Ar, (17, PieceKind.Dwelling));
        s = s with { Neutral = new NeutralBoard(ValueList.Of(31), ValueList<(int, int)>.Empty, ValueList<(Track, int)>.Empty) };
        var cost = Factions.BuildingCost(Ar, PieceKind.TradingPost);
        Assert.Equal(cost + new Cost(cost.Coins, 0), Rules.UpgradeCost(s, PieceKind.TradingPost, Ar, 17)); // 31 doesn't touch 17
        var bridged = s with { Bridges = s.Bridges.Add(new Bridge(Ar, 17, 31)) };
        Assert.Equal(cost, Rules.UpgradeCost(bridged, PieceKind.TradingPost, Ar, 17));
    }

    // As on BGA (661421495 #394, 738842993 #370): moving a bridge out of a town leaves the part holding
    // its key a town; the rest leaves it.
    [Fact]
    public void Architects_moving_a_bridge_out_of_a_town_leave_only_the_key_s_part_in_it()
    {
        var s = WithStronghold(Put(Play(FanSolo(Ar, 5, 112)), Ar, (16, PieceKind.TradingPost), (17, PieceKind.Dwelling), (31, PieceKind.TradingPost)), Ar);
        s = s.WithPlayer(s[Ar] with { TownHexes = ValueList.Of(16, 17, 31) }) with
        {
            Bridges = s.Bridges.Add(new Bridge(Ar, 17, 31)),
            PlacedKeys = s.PlacedKeys.Add(new PlacedKey(s.KeyBank[0][0], Ar, 16)),
        };
        var moved = PlayFrom(s, new BonusMoveBridge(Ar, BonusSource.Stronghold, 17, 31, 16, 27)); // across 15 now
        Assert.Equal(ValueList.Of(16, 17), moved[Ar].TownHexes); // 31, cut off from the key, leaves
    }

    // ---------- Treasurers ----------

    [Fact]
    public void Treasurers_put_some_income_in_their_Safe_and_it_comes_back_doubled()
    {
        var s = Play(FanSolo(Tr, 5, 112));
        var offer = Assert.IsType<Income>(s.TreasuryOffer);
        Assert.True(offer.Workers > 0);
        Assert.Equal($"Only what was just received can go in: {offer}.",
            Refusal(s, new StoreInTreasury(Tr, 0, offer.Workers + 1, 0)));

        Assert.Equal("Waiting for the Treasurers to choose what goes into their Safe.", Refusal(s, PassKeeping(Tr)));

        var stored = PlayFrom(s, new StoreInTreasury(Tr, 0, 1, 0));
        Assert.Equal(1, stored[Tr].SafeWorkers);
        Assert.Equal(s[Tr].Workers - 1, stored[Tr].Workers);
        Assert.Null(stored.TreasuryOffer); // settled: the rest stays out

        var passed = PlayFrom(stored, PassKeeping(Tr));
        Assert.Null(passed.TreasuryOffer);
        var round2 = Rules.Apply(passed, End(Tr));
        Assert.Contains((GameEvent)new SafeOpened(Tr, 0, 2, 0), round2.Events);
        Assert.Equal(0, round2.State[Tr].SafeWorkers);
    }

    [Fact]
    public void Treasurers_with_their_stronghold_put_a_cult_reward_in_the_Safe_before_it_opens()
    {
        // Round 1's tile: 1 worker for every 2 Fire steps (the Treasurers start with 2).
        var s = WithStronghold(PlayFrom(Play(FanSolo(Tr, 5, 112)), new StoreInTreasury(Tr, 0, 0, 0)), Tr);
        var ended = Rules.Apply(PlayFrom(s, PassKeeping(Tr)), End(Tr));
        Assert.True(ended.Accepted);
        Assert.True(ended.State.SafeOpeningHeld);
        Assert.Equal(new Income(0, 1, 0, 0), ended.State.TreasuryOffer);
        Assert.Equal(1, ended.State.Round); // waiting for the choice
        Assert.DoesNotContain(ended.Events, e => e is RoundStarted);

        var next = Rules.Apply(ended.State, new StoreInTreasury(Tr, 0, 1, 0));
        Assert.True(next.Accepted);
        Assert.Equal(2, next.State.Round);
        Assert.False(next.State.SafeOpeningHeld);
        Assert.Contains((GameEvent)new SafeOpened(Tr, 0, 2, 0), next.Events); // the worker, doubled at once
    }

    [Fact]
    public void Treasurers_may_keep_everything_out_of_their_Safe()
    {
        var s = Play(FanSolo(Tr, 5, 112));
        var declined = PlayFrom(s, new StoreInTreasury(Tr, 0, 0, 0));
        Assert.Null(declined.TreasuryOffer);
        Assert.Equal(0, declined[Tr].SafeWorkers);
        Assert.Equal(s[Tr].Workers, declined[Tr].Workers);
    }

    [Fact]
    public void Treasurers_with_their_stronghold_convert_before_settling_and_the_gains_join_the_offer()
    {
        var s = PlayFrom(Play(FanSolo(Tr, 5, 112)), new StoreInTreasury(Tr, 0, 0, 0));
        s = WithStronghold(Rich(s, Tr), Tr);
        s = s.WithPlayer(s[Tr] with { Power = new Power(0, 0, 12) });

        var one = PlayFrom(s, new ConvertPowerToCoin(Tr));
        Assert.Equal(new Income(1, 0, 0, 0), one.TreasuryOffer);
        var two = PlayFrom(one, new ConvertPowerToWorker(Tr)); // allowed while the offer waits
        Assert.Equal(new Income(1, 1, 0, 0), two.TreasuryOffer);
        var three = PlayFrom(two, new ConvertWorkerToCoin(Tr));
        Assert.Equal(new Income(2, 1, 0, 0), three.TreasuryOffer);

        Assert.Equal("Waiting for the Treasurers to choose what goes into their Safe.", Refusal(three, PassKeeping(Tr)));
        var stored = PlayFrom(three, new StoreInTreasury(Tr, 2, 1, 0));
        Assert.Equal(2, stored[Tr].SafeCoins);
        Assert.Equal(1, stored[Tr].SafeWorkers);
        Assert.Null(stored.TreasuryOffer);
    }

    [Fact]
    public void The_Treasurers_choice_for_their_Safe_is_a_move_logged_and_undone_with_the_settling()
    {
        var s = PlayFrom(Play(FanSolo(Tr, 5, 112)), new StoreInTreasury(Tr, 0, 0, 0));
        s = WithStronghold(Rich(s, Tr), Tr);
        s = s.WithPlayer(s[Tr] with { Power = new Power(0, 0, 12) });
        var history = new History(s);
        for (var i = 0; i < 3; i++)
            Assert.True(history.Apply(new ConvertPowerToCoin(Tr)).Accepted);
        Assert.Equal(new Income(3, 0, 0, 0), history.Current.TreasuryOffer);
        Assert.Equal($"Only what was just received can go in: {history.Current.TreasuryOffer}.",
            Refusal(history.Current, new ChooseForSafe(Tr, 4, 0, 0)));

        Assert.True(history.Apply(new ChooseForSafe(Tr, 1, 0, 0)).Accepted);
        Assert.True(history.Apply(new ChooseForSafe(Tr, 3, 0, 0)).Accepted); // merges with the one before
        Assert.Equal(new Income(3, 0, 0, 0), history.Current.TreasuryChosen);
        Assert.StartsWith("Treasurers choose 3c for their Safe", GameLog.Describe(history.Log()[^1]));
        Assert.True(history.Apply(new StoreInTreasury(Tr, 3, 0, 0)).Accepted); // and so does the settling
        Assert.Equal(5, history.Log().Count); // Session start, three conversions, the Safe
        Assert.StartsWith("Treasurers put 3c in their Safe", GameLog.Describe(history.Log()[^1]));
        Assert.Null(history.Current.TreasuryChosen);

        // Undone back before the conversions and converted again: nothing is chosen for the Safe yet.
        for (var i = 0; i < 4; i++)
            history.Undo();
        Assert.Null(history.Current.TreasuryOffer);
        Assert.True(history.Apply(new ConvertPowerToCoin(Tr)).Accepted);
        Assert.Equal(new Income(1, 0, 0, 0), history.Current.TreasuryOffer);
        Assert.Null(history.Current.TreasuryChosen);

        // A conversion while a choice waits: the choice kept as far as the offer goes.
        var chosen = PlayFrom(history.Current, new ChooseForSafe(Tr, 1, 0, 0));
        var more = PlayFrom(chosen, new ConvertPowerToWorker(Tr));
        Assert.Equal(new Income(1, 1, 0, 0), more.TreasuryOffer);
        Assert.Equal(new Income(1, 0, 0, 0), more.TreasuryChosen);
    }

    // ---------- Chash Dallah ----------

    [Fact]
    public void Chash_Dallah_advance_their_income_track()
    {
        var s = Rich(Play(FanSolo(CD, 9, 60)), CD);
        var after = PlayFrom(s, new AdvanceIncomeTrack(CD));
        Assert.Equal(2, after[CD].IncomeTrack);
        Assert.Equal(s[CD].Coins - Config.ChashDallahIncomeStepCost.Coins, after[CD].Coins);
        Assert.Equal(s[CD].Workers - Config.ChashDallahIncomeStepCost.Workers, after[CD].Workers);
        Assert.Equal(s[CD].VictoryPoints + Factions.IncomeTrackVictoryPoints(2), after[CD].VictoryPoints);
        Assert.Equal(s[CD].Income + new Income(0, 1, 0, 0), after[CD].Income); // step 2 adds a worker
    }

    [Fact]
    public void Chash_Dallah_with_their_stronghold_pay_power_actions_in_coins()
    {
        var s = Rich(Play(FanSolo(CD, 9, 60)), CD);
        Assert.Equal("Only Chash Dallah, with their stronghold, pay for power actions with coins.",
            Refusal(s, new TakePowerAction(CD, 1, PayWithCoins: true)));
        s = WithStronghold(s, CD);
        var after = PlayFrom(s, new TakePowerAction(CD, 1, PayWithCoins: true));
        Assert.Equal(s[CD].Power, after[CD].Power);
        Assert.Equal(s[CD].Coins - PowerActions.All[1].Cost, after[CD].Coins);
        Assert.Equal(s[CD].Priests.Count + 1, after[CD].Priests.Count);
    }

    // ---------- Enlightened ----------

    [Fact]
    public void Enlightened_pay_power_for_spades()
    {
        var s = Charged(Play(FanSolo(En, 9, 60)));
        var after = PlayFrom(s, new TransformTerrain(En, 10, Terrain.Forest)); // Lake to Forest: 1 spade
        Assert.Equal(new Power(Config.EnlightenedSpadePower, 0, 12 - Config.EnlightenedSpadePower), after[En].Power);
        Assert.Equal(s[En].Workers, after[En].Workers);
        Assert.Equal("The Enlightened need 3 power in bowl 3 (burning included) for 1 spade.",
            Refusal(s.WithPlayer(s[En] with { Power = new Power(12, 0, 0) }), new TransformTerrain(En, 10, Terrain.Forest)));
    }

    [Fact]
    public void Enlightened_buy_power_tokens_and_convert_double_with_their_stronghold()
    {
        var s = Charged(Play(FanSolo(En, 9, 60)));
        var bought = PlayFrom(s, new BuyPowerToken(En));
        Assert.Equal(s[En].Coins - Config.EnlightenedTokenCoins, bought[En].Coins);
        Assert.Equal(new Power(1, 0, 12), bought[En].Power);

        Assert.Equal(s[En].Coins + 1, PlayFrom(s, new ConvertPowerToCoin(En))[En].Coins);
        var strong = WithStronghold(s, En);
        Assert.Equal(s[En].Coins + 2, PlayFrom(strong, new ConvertPowerToCoin(En))[En].Coins);
        Assert.Equal(s[En].Priests.Count + 2, PlayFrom(strong, GetPriest(En))[En].Priests.Count);
    }

    // As BGA (893445224): the final conversions too.
    [Fact]
    public void Enlightened_with_their_stronghold_convert_power_double_in_final_scoring()
    {
        var s = WithStronghold(Charged(Play(FanSolo(En, 9, 60))), En);
        s = s.WithPlayer(s[En] with { Coins = 0, Workers = 0, Priests = ValueList<Piece>.Empty, Power = new Power(9, 0, 3) });
        Assert.Equal(2, Assert.Single(EndGame.Finish(s, new List<GameEvent>()).FinalScores).CoinPoints); // 6 coins
    }

    // ---------- Conspirators ----------

    [Fact]
    public void Conspirators_take_two_coins_with_every_favour()
    {
        var s = PlayFrom(Rich(WithTradingPost(Play(FanSolo(Co, 1, 108)), Co, 1), Co), UpgradeTo(Co, PieceKind.Temple, 0, 1));
        Assert.Equal(1, s[Co].FavoursOwed);
        var after = PlayFrom(s, new TakeFavour(Co, 0));
        Assert.Equal(s[Co].Coins + Config.ConspiratorsCoinsPerFavour, after[Co].Coins);
    }

    // ---------- Dynion Geifr ----------

    [Fact]
    public void Dynion_Geifr_start_with_the_Fire_favour_and_trade_priests_for_more()
    {
        var s = Charged(Play(FanSolo(Dy, 1, 108)));
        var favour = Assert.Single(s[Dy].FavourTiles);
        Assert.Equal(Track.Fire, Favours.All[favour.Id].Track);
        Assert.True(Favours.All[favour.Id].TownPowerReduction > 0);

        var priest = PlayFrom(s, GetPriest(Dy));
        var after = PlayFrom(priest, new ConvertPriestToWorker(Dy));
        Assert.Equal(priest[Dy].Workers + Config.DynionGeifrPriestWorkers, after[Dy].Workers);
        Assert.Equal(priest[Dy].Coins + Config.DynionGeifrPriestCoins, after[Dy].Coins);
    }

    // The final conversions too, 2 workers and 2 coins a priest (4 coins).
    [Fact]
    public void Dynion_Geifr_priests_are_worth_four_coins_in_final_scoring()
    {
        var s = Charged(Play(FanSolo(Dy, 1, 108)));
        var supply = s[Dy].PriestSupply;
        s = s.WithPlayer(s[Dy] with
        {
            Coins = 0, Workers = 0, Power = new Power(12, 0, 0),
            Priests = new ValueList<Piece>(supply.Take(3)), PriestSupply = new ValueList<Piece>(supply.Skip(3)),
        });
        Assert.Equal(4, Assert.Single(EndGame.Finish(s, new List<GameEvent>()).FinalScores).CoinPoints); // 12 coins
    }
}
