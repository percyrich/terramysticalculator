using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Fire & Ice's factions: choosing starting terrains, Ice, Volcano, and each faction's abilities.
// Starts used (base map; see MapTests for the neighbours):
//   Ice Maidens or Yetis on Swamp 7 and 101 (both become Ice), Giants on 5 and 112. Next to 7: 6 Plains, 8 Plains, 20 Desert.
//   Dragonlords or Acolytes on Lake 3 and 89 (both become Volcano), Witches on 9 and 60. Next to 3: 2 Forest (the
//   Witches' home), 4 Desert.
//   Riverwalkers on Plains 6 and 90 (both by rivers), Giants on 5 and 112. 7 Swamp is across river 19 from 6;
//   102 Mountain touches 90 by land only.
//   Shapeshifters on Mountain 99 and 1, Giants on 5 and 112. 111 Forest touches 99 and the Giants' 112.
public class FireAndIceTests
{
    private const Faction IM = Faction.IceMaidens, Ye = Faction.Yetis, DL = Faction.Dragonlords, Ac = Faction.Acolytes,
        RW = Faction.Riverwalkers, SS = Faction.Shapeshifters;
    private const int MaidensFavour = 5; // Water +3 (one copy)

    // Factions added in turn order, each ice or variable faction choosing its starting terrain at once (the Ice Maidens
    // then taking MaidensFavour), confirmed, then a volcano faction's terrain, then the turn order. terrains: the
    // starting terrains of the factions that choose one, in turn order.
    private static Move[] Chosen(Faction[] turnOrder, params Terrain[] terrains)
    {
        var moves = new List<Move> { turnOrder.Length == 1 ? ChooseCalculator : ChooseTM };
        var queue = new Queue<Terrain>(terrains);
        var volcano = new List<Move>();
        foreach (var f in turnOrder)
        {
            moves.Add(new AddPlayer(f));
            switch (Factions.Kind(f))
            {
                case FactionKind.Ice or FactionKind.Variable:
                    moves.Add(new ChooseStartingTerrain(f, queue.Dequeue()));
                    if (f == IM)
                        moves.Add(new TakeFavour(f, MaidensFavour));
                    break;
                case FactionKind.Volcano:
                    volcano.Add(new ChooseStartingTerrain(f, queue.Dequeue()));
                    break;
            }
        }
        moves.Add(new ConfirmFactions());
        moves.AddRange(volcano);
        moves.AddRange(turnOrder.Take(turnOrder.Length - 1).Select(f => (Move)new ChooseTurnOrder(f)));
        return moves.ToArray();
    }

    // As StartWith: chosen, set up, starting dwellings on these hexes (placement order), pass tiles picked. Round 1.
    private static Move[] Started(Faction[] turnOrder, Terrain[] terrains, params int[] hexes)
    {
        var order = PlacementOrder(turnOrder);
        var placed = new Dictionary<Faction, int>();
        var placements = new List<Move>();
        for (var i = 0; i < order.Length; i++)
        {
            placed.TryGetValue(order[i], out var number);
            placed[order[i]] = number + 1;
            placements.Add(StartAt(order[i], number, hexes[i]));
        }
        var tiles = PassTilesFor(turnOrder);
        var picks = Enumerable.Range(0, turnOrder.Length).Reverse().Select(i => (Move)new PickPassTile(turnOrder[i], tiles[i])).ToArray();
        return Concat(Chosen(turnOrder, terrains), SetUpAndStart(turnOrder), placements.ToArray(), picks);
    }

    private static Move[] IceStart(Faction ice) => Started(new[] { ice, G }, new[] { Terrain.Swamp }, 7, 5, 112, 101);
    private static Move[] VolcanoStart(Faction volcano) => Started(new[] { volcano, W }, new[] { Terrain.Lake }, 3, 9, 60, 89);
    private static readonly Move[] RiverwalkersStart = Started(new[] { RW, G }, new[] { Terrain.Plains }, 6, 5, 112, 90);
    private static readonly Move[] ShapeshiftersStart = Started(new[] { SS, G }, new[] { Terrain.Mountain }, 99, 5, 112, 1);

    private static GameState Built(GameState s, Faction f) =>
        s.WithPlayer(s[f] with { StrongholdBank = new ValueList<Piece?>(new Piece?[] { null }) });

    private static string Refusal(GameState s, Move move) => Assert.Single(Rules.Apply(s, move).Issues).Message;

    // ---------- Choosing factions and starting terrains ----------

    [Fact]
    public void Ice_factions_choose_a_starting_terrain_before_anything_else_and_take_its_colour()
    {
        var s = Play(ChooseTM, new AddPlayer(IM));
        Assert.Equal("The Ice Maidens choose their starting terrain first.", Refusal(s, new AddPlayer(G)));
        Assert.Equal("The Ice Maidens choose their starting terrain first.", Refusal(s, new ConfirmFactions()));
        Assert.False(Rules.Apply(s, new ChooseStartingTerrain(IM, Terrain.Ice)).Accepted); // a basic terrain

        s = PlayFrom(s, new ChooseStartingTerrain(IM, Terrain.Forest));
        Assert.Equal(Terrain.Forest, s[IM].StartingTerrain);
        Assert.Null(s[IM].Home); // a starting terrain is not a home terrain
        Assert.Equal(PlayerColour.White, s[IM].Colour);
        Assert.Equal("The Ice Maidens choose their favour first.", Refusal(s, new AddPlayer(G)));

        s = PlayFrom(s, new TakeFavour(IM, MaidensFavour));
        Assert.Equal("The Ice Maidens have chosen Forest as their starting terrain: only one faction of each colour can play.",
            Refusal(s, new AddPlayer(W)));
        Assert.Equal("The Ice Maidens are already in the game: only one ice faction can play.", Refusal(s, new AddPlayer(Ye)));
        Assert.True(Rules.Apply(s, new AddPlayer(G)).Accepted);

        var taken = Play(ChooseTM, new AddPlayer(W), new AddPlayer(Ye));
        Assert.Equal("Forest is the home terrain of the Witches: choose another.", Refusal(taken, new ChooseStartingTerrain(Ye, Terrain.Forest)));
    }

    [Fact]
    public void The_Ice_Maidens_take_a_favour_with_its_steps_and_power_at_once()
    {
        var s = Play(ChooseTM, new AddPlayer(IM), new ChooseStartingTerrain(IM, Terrain.Swamp));
        Assert.Equal(1, s[IM].FavoursOwed);

        var taken = PlayFrom(s, new TakeFavour(IM, MaidensFavour));
        Assert.Equal(4, taken[IM].TrackSteps[(int)Track.Water]); // 1 + 3
        Assert.Equal(new Power(5, 7, 0), taken[IM].Power);         // past step 3: 1 power
        Assert.Empty(taken.FavourBank[MaidensFavour]);

        Assert.Single(PlayFrom(taken, new RemovePlayer(IM)).FavourBank[MaidensFavour]); // back in the bank
    }

    [Fact]
    public void A_volcano_faction_chooses_its_terrain_once_every_faction_is_chosen()
    {
        var s = Play(ChooseTM, new AddPlayer(DL), new AddPlayer(W));
        Assert.Equal("The Dragonlords choose their starting terrain once every faction has been chosen.",
            Refusal(s, new ChooseStartingTerrain(DL, Terrain.Lake)));
        Assert.Equal("The Dragonlords are already in the game: only one volcano faction can play.", Refusal(s, new AddPlayer(Ac)));

        s = PlayFrom(s, new ConfirmFactions());
        Assert.Equal(Phase.ChoosingVolcanoTerrain, s.Phase);
        Assert.Equal("Forest is the home terrain of the Witches: choose another.", Refusal(s, new ChooseStartingTerrain(DL, Terrain.Forest)));

        s = PlayFrom(s, new ChooseStartingTerrain(DL, Terrain.Lake));
        Assert.Equal(Phase.ChoosingTurnOrder, s.Phase);
        Assert.Equal(PlayerColour.Orange, s[DL].Colour);
    }

    [Fact]
    public void Starting_dwellings_go_on_the_starting_terrain_and_turn_into_Ice_or_Volcano()
    {
        var placing = Play(Concat(Chosen(new[] { IM, G }, Terrain.Swamp), SetUpAndStart(IM, G)));
        Assert.Equal("The Ice Maidens' starting dwellings must be placed on Swamp.", Refusal(placing, StartAt(IM, 0, 6)));

        var ice = Play(IceStart(IM));
        Assert.Equal(Terrain.Ice, ice.HexTerrain[7]);
        Assert.Equal(Terrain.Ice, ice.HexTerrain[101]);
        Assert.Equal(Terrain.Wasteland, ice.HexTerrain[5]);

        var volcano = Play(VolcanoStart(DL));
        Assert.Equal(Terrain.Volcano, volcano.HexTerrain[3]);
        Assert.Equal(Terrain.Volcano, volcano.HexTerrain[89]);
    }

    // ---------- Ice ----------

    [Fact]
    public void Ice_takes_the_starting_terrains_place_on_the_wheel_and_never_changes_again()
    {
        var s = Play(IceStart(IM));
        var maidens = s[IM];
        Assert.Equal(1, maidens.SpadesNeeded(Terrain.Swamp, Terrain.Ice));     // the starting terrain itself
        Assert.Equal(1, maidens.SpadesNeeded(Terrain.Plains, Terrain.Ice));
        Assert.Equal(2, maidens.SpadesNeeded(Terrain.Desert, Terrain.Ice));
        Assert.Equal(3, maidens.SpadesNeeded(Terrain.Wasteland, Terrain.Ice));
        Assert.Null(maidens.SpadesNeeded(Terrain.Ice, Terrain.Plains));
        Assert.Null(s[G].SpadesNeeded(Terrain.Plains, Terrain.Ice));
        Assert.Contains(Rules.TransformChoices(s, IM, 6), choice => choice.To == Terrain.Ice && choice.Spades == 1);

        var iced = PlayFrom(s, new TransformTerrain(IM, 6, Terrain.Ice), new FinishSpades(IM), End(IM));
        Assert.Equal(Terrain.Ice, iced.HexTerrain[6]);
        Assert.Equal("Ice cannot be transformed.", Refusal(iced, new TransformTerrain(G, 6, Terrain.Wasteland)));
    }

    [Fact]
    public void The_Ice_Maidens_stronghold_scores_their_temples_when_they_pass()
    {
        var s = Play(IceStart(IM));
        var temples = s with { Map = s.Map.SetItem(8, new Piece(IM, PieceKind.Temple, 0)).SetItem(20, new Piece(IM, PieceKind.Temple, 1)) };
        var without = PlayFrom(temples, PassKeeping(IM));
        var with = PlayFrom(Built(temples, IM), PassKeeping(IM));
        Assert.Equal(without[IM].VictoryPoints + 2 * Config.IceMaidensVictoryPointsPerTemple, with[IM].VictoryPoints);
    }

    [Fact]
    public void The_Yetis_pay_one_power_less_and_with_their_stronghold_take_taken_power_actions()
    {
        var s = Charged(Play(IceStart(Ye)));
        Assert.Equal(2, Rules.PowerActionCost(s, Ye, 1));
        Assert.Equal(3, Rules.PowerActionCost(s, G, 1));
        Assert.Equal(10, PlayFrom(s, new TakePowerAction(Ye, 1))[Ye].Power.Bowl3);

        var taken = s with { PowerActionsTaken = ValueList.Of(1) };
        Assert.Equal("That power action has already been taken this round.", Refusal(taken, new TakePowerAction(Ye, 1)));
        Assert.True(Rules.Apply(Built(taken, Ye), new TakePowerAction(Ye, 1)).Accepted);
    }

    [Fact]
    public void The_Yetis_stronghold_and_sanctuary_have_a_power_value_of_four()
    {
        Assert.Equal(4, Factions.BuildingPower(Ye, PieceKind.Stronghold));
        Assert.Equal(4, Factions.BuildingPower(Ye, PieceKind.Sanctuary));
        Assert.Equal(2, Factions.BuildingPower(Ye, PieceKind.Temple));
        Assert.Equal(3, Factions.BuildingPower(G, PieceKind.Stronghold));
    }

    [Theory]
    [InlineData(Faction.IceMaidens)]
    [InlineData(Faction.Yetis)]
    public void Ice_factions_upgrade_spades_for_a_worker_and_five_coins_plus_a_priest(Faction ice) =>
        Assert.Equal(new Cost(Coins: 5, Workers: 1), Factions.SpadeUpgradeCost(ice));

    // ---------- Volcano ----------

    [Fact]
    public void The_Dragonlords_remove_power_tokens_for_Volcano_two_for_a_home_terrain()
    {
        var start = Play(VolcanoStart(DL));
        var s = start.WithPlayer(start[DL] with { Power = new Power(1, 2, 3) });
        Assert.Equal(1, Rules.VolcanoPrice(s, DL, 4)); // Desert
        Assert.Equal(2, Rules.VolcanoPrice(s, DL, 2)); // Forest: the Witches' home

        var made = Rules.Apply(s, new TransformTerrain(DL, 4, Terrain.Volcano));
        Assert.True(made.Accepted);
        Assert.Equal(Terrain.Volcano, made.State.HexTerrain[4]);
        Assert.Equal(new Power(0, 2, 3), made.State[DL].Power); // lowest bowl first
        Assert.Equal(new Power(0, 1, 3), PlayFrom(s, new TransformTerrain(DL, 2, Terrain.Volcano))[DL].Power);
        Assert.Equal("The Dragonlords only ever make hexes Volcano.", Refusal(s, new TransformTerrain(DL, 4, Terrain.Plains)));

        var built = PlayFrom(s, Build(DL, 2, 4)); // made Volcano and built on in one move
        Assert.Equal(DL, built.Map[4]?.Owner);
        Assert.Equal(Terrain.Volcano, built.HexTerrain[4]);

        var empty = s.WithPlayer(s[DL] with { Power = new Power(0, 0, 0) });
        Assert.Equal("The Dragonlords need 1 power token to remove to make that hex Volcano.",
            Refusal(empty, new TransformTerrain(DL, 4, Terrain.Volcano)));
    }

    [Fact]
    public void The_Dragonlords_gain_a_token_for_a_spade_and_a_token_per_player_for_their_stronghold()
    {
        var s = Play(VolcanoStart(DL));
        var spade = PlayFrom(Charged(s), new TakePowerAction(DL, 4));
        Assert.Equal(new Power(5, 0, 8), spade[DL].Power); // 4 spent, then the spade's new token in bowl 1
        Assert.Equal(0, spade[DL].SpadesOwed);
        Assert.False(spade.SpadesPending);

        // A trading post on 3, upgraded to the stronghold: 2 players, 2 new tokens in bowl 1.
        var tradingPost = new Piece(DL, PieceKind.TradingPost, 0);
        var withPost = (s with { Map = s.Map.SetItem(3, tradingPost) })
            .WithPlayer(s[DL] with { TradingPostBank = s[DL].TradingPostBank.SetItem(0, null) });
        var stronghold = PlayFrom(withPost, UpgradeTo(DL, PieceKind.Stronghold, 0, 3));
        Assert.Equal(s[DL].Power.Bowl1 + 2, stronghold[DL].Power.Bowl1);
    }

    [Fact]
    public void The_Acolytes_pay_steps_from_one_track_for_Volcano()
    {
        var s = Play(VolcanoStart(Ac));
        var made = PlayFrom(s, new TransformTerrain(Ac, 4, Terrain.Volcano));
        Assert.Equal(Terrain.Volcano, made.HexTerrain[4]);
        Assert.Equal(3, made[Ac].CultStepsToPay);
        Assert.Equal(ValueList.Of(Track.Fire, Track.Water, Track.Earth, Track.Air), Rules.TracksThatCanPay(made, Ac, 3));
        Assert.Equal("The Acolytes must pay their cult steps first.", Refusal(PlayFrom(made, new FinishSpades(Ac)), End(Ac)));

        var short_ = made.WithPlayer(made[Ac] with { TrackSteps = new ValueList<int>(new[] { 2, 3, 3, 3 }) });
        Assert.Equal("The Acolytes have only 2 steps on Fire: 3 are owed, all from one track.", Refusal(short_, new PayCultSteps(Ac, Track.Fire)));

        var paid = PlayFrom(made, new PayCultSteps(Ac, Track.Fire), new FinishSpades(Ac));
        Assert.Equal(0, paid[Ac].TrackSteps[(int)Track.Fire]);
        Assert.Equal(s[Ac].Power, paid[Ac].Power); // going down gains and costs no power
        Assert.True(Rules.Apply(paid, End(Ac)).Accepted);

        // The Witches' Forest needs 4 steps: no track has them.
        Assert.Equal("The Acolytes need 4 steps on one track to pay to make that hex Volcano.",
            Refusal(s, new TransformTerrain(Ac, 2, Terrain.Volcano)));
        Assert.Equal(3, PlayFrom(s, Build(Ac, 2, 4))[Ac].CultStepsToPay); // building there owes them too
    }

    [Fact]
    public void Acolytes_leaving_the_top_of_a_track_get_their_key_back()
    {
        var start = Play(VolcanoStart(Ac));
        var top = start.WithPlayer(start[Ac] with { TrackSteps = new ValueList<int>(new[] { 3, 10, 3, 3 }) });
        var back = PlayFrom(top, new TransformTerrain(Ac, 4, Terrain.Volcano), new PayCultSteps(Ac, Track.Water));
        Assert.Equal(7, back[Ac].TrackSteps[(int)Track.Water]);
        Assert.Equal(1, back[Ac].Keys);
    }

    [Fact]
    public void The_Acolytes_take_a_step_for_a_spade_and_priests_go_further_with_their_stronghold()
    {
        var s = Play(VolcanoStart(Ac));
        var spade = PlayFrom(Charged(s), new TakePowerAction(Ac, 4));
        Assert.Equal(1, spade[Ac].CultStepsOwed);
        Assert.False(spade.SpadesPending);
        Assert.Equal(4, PlayFrom(spade, new TakeCultStep(Ac, Track.Air))[Ac].TrackSteps[(int)Track.Air]);

        Assert.Equal(3, Rules.PriestSlotSteps(s, Ac, 0));
        Assert.Equal(4, Rules.PriestSlotSteps(Built(s, Ac), Ac, 0));
        Assert.Equal(2, Rules.PriestSlotSteps(Built(s, Ac), Ac, 4));
        Assert.Equal(3, Rules.PriestSlotSteps(Built(s, Ac), W, 0));
    }

    // ---------- Riverwalkers ----------

    [Fact]
    public void The_Riverwalkers_start_with_one_priest_the_rest_locking_terrains_and_build_by_rivers()
    {
        var chosen = Play(ChooseTM, new AddPlayer(RW), new ChooseStartingTerrain(RW, Terrain.Plains));
        Assert.Single(chosen[RW].PriestSupply);
        Assert.Equal(6, chosen[RW].WheelPriests.Count);
        Assert.DoesNotContain(chosen[RW].WheelPriests, locked => locked.Terrain == Terrain.Plains);
        Assert.Equal(PlayerColour.Brown, chosen[RW].Colour);
        Assert.Equal("The Riverwalkers are already in the game: only one variable faction can play.", Refusal(chosen, new AddPlayer(SS)));

        var placing = Play(Concat(Chosen(new[] { RW, G }, Terrain.Plains), SetUpAndStart(RW, G)));
        Assert.Equal("The Riverwalkers build only next to a river.", Refusal(placing, StartAt(RW, 0, 0))); // Plains, no river beside it

        var s = Play(RiverwalkersStart);
        Assert.Equal(Reach.None, Rules.ReachOf(s, RW, 102, allowSkip: true));     // touches 90 by land only
        Assert.Equal(Reach.Shipping, Rules.ReachOf(s, RW, 7, allowSkip: true));   // across river 19
        Assert.Equal("The Riverwalkers can't transform terrain, and haven't unlocked Swamp (a priest still stands on it on their wheel).",
            Refusal(s, Build(RW, 2, 7)));
        Assert.Empty(Rules.TransformChoices(s, RW, 7));
    }

    [Fact]
    public void A_priest_gained_by_the_Riverwalkers_may_unlock_a_terrain_instead()
    {
        var s = Play(RiverwalkersStart);
        var gained = PlayFrom(Charged(s), new ConvertPowerToPriest(RW));
        Assert.Equal(1, gained[RW].PriestChoicesOwed);
        Assert.Empty(gained[RW].Priests);
        Assert.Equal("Waiting for the Riverwalkers to choose what to do with their priest.", Refusal(gained, Build(RW, 2, 7)));
        Assert.Equal(1, Rules.UnlockCost(gained, RW, Terrain.Swamp));
        Assert.Equal(2, Rules.UnlockCost(gained, RW, Terrain.Wasteland)); // the Giants' home

        var unlocked = PlayFrom(gained, new ChoosePriest(RW, Terrain.Swamp));
        Assert.Equal(gained[RW].Coins - 1, unlocked[RW].Coins);
        Assert.Equal(5, unlocked[RW].WheelPriests.Count);
        Assert.Equal(2, unlocked[RW].PriestSupply.Count);
        var built = PlayFrom(unlocked, Build(RW, 2, 7));
        Assert.Equal(RW, built.Map[7]?.Owner);
        Assert.Equal(Terrain.Swamp, built.HexTerrain[7]);

        Assert.Single(PlayFrom(gained, new ChoosePriest(RW, null))[RW].Priests);
        Assert.False(Rules.Apply(gained, new ChoosePriest(RW, Terrain.Plains)).Accepted); // not locked
    }

    [Fact]
    public void The_Riverwalkers_lose_spades_and_may_skip_their_strongholds_bridges()
    {
        var s = Play(RiverwalkersStart);
        var spade = Rules.Apply(Charged(s), new TakePowerAction(RW, 4));
        Assert.True(spade.Accepted);
        Assert.Equal(0, spade.State[RW].SpadesOwed);
        Assert.Contains(spade.Issues, issue => issue.Message == "The Riverwalkers can't use spades, so the spade is lost.");

        var owed = (s with { ActionTaken = true });
        owed = owed.WithPlayer(owed[RW] with { BridgesOwed = 2, BridgesOptional = true });
        Assert.Equal("The Riverwalkers must place their bridges first (or skip them).", Refusal(owed, End(RW)));
        Assert.True(Rules.Apply(owed.WithPlayer(owed[RW] with { Workers = 1 }), new ConvertWorkerToCoin(RW)).Accepted); // a reward after the action (Andrew 2026-11-23)
        Assert.True(Rules.Apply(PlayFrom(owed, new SkipBridges(RW)), End(RW)).Accepted);
        Assert.Equal("The Riverwalkers have no free bridges to skip.",
            Refusal(owed.WithPlayer(owed[RW] with { BridgesOwed = 1, BridgesOptional = false }), new SkipBridges(RW)));
    }

    // ---------- Shapeshifters ----------

    [Fact]
    public void The_Shapeshifters_may_buy_a_token_when_an_opponent_takes_their_power()
    {
        var s = Play(ShapeshiftersStart);
        Assert.Equal(Terrain.Mountain, s[SS].Home);
        Assert.Equal(PlayerColour.Grey, s[SS].Colour);

        var offered = PlayFrom(s, Build(SS, 2, 111));
        Assert.Equal(G, Assert.Single(offered.PowerOffers).Player); // 1 power, still asked

        var token = PlayFrom(offered, Accept(G));
        Assert.True(token[SS].TokenOfferOwed);
        Assert.Equal("The Shapeshifters must answer the power token offer first.", Refusal(token, End(SS)));
        var paid = PlayFrom(token, new AnswerTokenOffer(SS, true));
        Assert.Equal(token[SS].Power.Bowl3 + 1, paid[SS].Power.Bowl3);
        Assert.Equal(token[SS].VictoryPoints - 1, paid[SS].VictoryPoints);
        Assert.True(Rules.Apply(paid, End(SS)).Accepted);

        var declined = PlayFrom(offered, Decline(G));
        Assert.False(declined[SS].TokenOfferOwed);
        Assert.Equal(offered[SS].Power.Gain(1), declined[SS].Power);
    }

    [Fact]
    public void The_Shapeshifters_stronghold_moves_their_home_for_power_or_tokens()
    {
        var s = Charged(Built(Play(ShapeshiftersStart), SS));
        Assert.Contains(new BonusActionId(BonusSource.Stronghold, BonusAction.Shapeshift), s[SS].BonusActions);
        Assert.Contains(Terrain.Plains, Rules.ShapeshiftTargets(s, SS));
        Assert.DoesNotContain(Terrain.Wasteland, Rules.ShapeshiftTargets(s, SS));

        var moved = PlayFrom(s, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Plains, PayWithTokens: false));
        Assert.Equal(Terrain.Plains, moved[SS].Home);
        Assert.Equal(PlayerColour.Brown, moved[SS].Colour); // their colour follows their home
        Assert.Equal(new Power(5, 0, 7), moved[SS].Power);
        Assert.Equal(new Power(0, 0, 7),
            PlayFrom(s, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Plains, PayWithTokens: true))[SS].Power);

        Assert.Equal("Wasteland is the Giants' home terrain.",
            Refusal(s, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Wasteland, PayWithTokens: false)));
        Assert.Equal("Mountain already is the Shapeshifters' home terrain.",
            Refusal(s, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Mountain, PayWithTokens: false)));
    }

    [Fact]
    public void The_Shapeshifters_stronghold_shapeshift_is_not_once_a_round()
    {
        var s = Charged(Built(Play(ShapeshiftersStart), SS));
        var moved = PlayFrom(s, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Plains, PayWithTokens: false));
        Assert.DoesNotContain(new BonusActionId(BonusSource.Stronghold, BonusAction.Shapeshift), moved[SS].BonusActionsUsed);
        Assert.Equal("The Shapeshifters have already taken their action this turn. Press End turn.",
            Refusal(moved, new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Forest, PayWithTokens: false))); // still the action
        var again = PlayFrom(moved, End(SS), PassKeeping(G), End(G), // the Shapeshifters' turn again, same round
            new BonusShapeshift(SS, BonusSource.Stronghold, Terrain.Forest, PayWithTokens: false));
        Assert.Equal(Terrain.Forest, again[SS].Home);
    }

    // ---------- Special landscapes (LandscapeTests has the base game's) ----------

    private static GameState WithLandscape(GameState s, Faction f, int hex) =>
        s with { Map = s.Map.SetItem(hex, new Piece(f, PieceKind.Landscape, 0)) };

    private static GameState Rich(GameState s, Faction f) => s.WithPlayer(s[f] with { Coins = 30, Workers = 30 });

    [Fact]
    public void The_Altar_costs_the_Acolytes_3_steps_anywhere_and_then_going_down_gains_power()
    {
        var s = Play(VolcanoStart(Ac));
        var placed = PlayFrom(s, new PlaceLandscape(Ac, 2)); // the Witches' Forest: 4 steps as a plain Volcano
        Assert.Equal(new Piece(Ac, PieceKind.Landscape, 0), placed.Map[2]);
        Assert.Equal(Terrain.Volcano, placed.HexTerrain[2]);
        Assert.Equal(Landscapes.AcolytesSteps, placed[Ac].CultStepsToPay);

        var paid = PlayFrom(placed, new PayCultSteps(Ac, Track.Fire));
        Assert.Equal(s[Ac].Power, paid[Ac].Power); // paying for the Altar itself gains nothing

        var owing = paid.WithPlayer(paid[Ac] with { TrackSteps = paid[Ac].TrackSteps.SetItem((int)Track.Water, 5), CultStepsToPay = 3 });
        var result = Rules.Apply(owing, new PayCultSteps(Ac, Track.Water)); // 5 to 2: past 5 (2 power) and 3 (1 power)
        Assert.True(result.Accepted);
        Assert.Equal(3, result.Events.OfType<PowerGained>().Sum(gained => gained.Amount));
    }

    [Fact]
    public void The_Altar_on_a_hex_just_made_Volcano_costs_only_3_steps()
    {
        var s = Play(VolcanoStart(Ac));
        s = s.WithPlayer(s[Ac] with { TrackSteps = s[Ac].TrackSteps.SetItem((int)Track.Water, 5) });
        var made = PlayFrom(s, new TransformTerrain(Ac, 2, Terrain.Volcano)); // the Witches' Forest: 4 steps
        Assert.Equal(4, made[Ac].CultStepsToPay);

        // Owed still: only 3 are paid, for the Altar.
        var owing = PlayFrom(made, new PlaceLandscape(Ac, 2));
        Assert.Equal(Landscapes.AcolytesSteps, owing[Ac].CultStepsToPay);

        // Paid already: one comes back, with no power.
        var paid = PlayFrom(made, new PayCultSteps(Ac, Track.Water));
        var placed = PlayFrom(paid, new PlaceLandscape(Ac, 2));
        Assert.Equal(paid[Ac].TrackSteps[(int)Track.Water] + 1, placed[Ac].TrackSteps[(int)Track.Water]);
        Assert.Equal(paid[Ac].Power, placed[Ac].Power);
        Assert.Equal(new Piece(Ac, PieceKind.Landscape, 0), placed.Map[2]);
    }

    [Fact]
    public void The_Frozen_Peak_brings_the_Yetis_2_tokens_in_bowl_3()
    {
        var s = Rich(Play(IceStart(Ye)), Ye);
        var placed = PlayFrom(s, new PlaceLandscape(Ye, 6)); // Plains into Ice: 1 spade
        Assert.Equal(s[Ye].Power.Bowl3 + Landscapes.YetisTokens, placed[Ye].Power.Bowl3);
        Assert.Equal(s[Ye].Power.Total + Landscapes.YetisTokens, placed[Ye].Power.Total);
    }

    [Fact]
    public void The_Lava_Pond_adds_a_Dragonlords_token_as_every_round_begins()
    {
        var s = Play(VolcanoStart(DL));
        var without = PlayFrom(s, PassAndEnd(DL, W));
        var landscaped = PlayFrom(WithLandscape(s, DL, 2), PassAndEnd(DL, W));
        Assert.Equal(2, landscaped.Round);
        Assert.Equal(without[DL].Power.Total + Landscapes.DragonlordsTokensPerRound, landscaped[DL].Power.Total);
    }

    [Fact]
    public void Winterfall_gives_the_Ice_Maidens_a_spade_with_every_temple()
    {
        var s = Rich(Play(IceStart(IM)), IM);
        s = WithLandscape(s.WithPlayer(s[IM] with { TradingPostBank = s[IM].TradingPostBank.SetItem(0, null) }) with
        {
            Map = s.Map.SetItem(7, new Piece(IM, PieceKind.TradingPost, 0)),
        }, IM, 6);
        var temple = PlayFrom(s, UpgradeTo(IM, PieceKind.Temple, 0, 7));
        Assert.Equal(Landscapes.IceMaidensSpadesPerTemple, temple[IM].SpadesOwed);
    }

    [Fact]
    public void The_Spectral_Polestar_moves_the_Shapeshifters_home_for_free()
    {
        var s = Rich(Play(ShapeshiftersStart), SS);
        var placed = PlayFrom(s, new PlaceLandscape(SS, 111)); // Forest into their Mountain
        Assert.Equal(LandscapeOffer.Shapeshift, placed[SS].LandscapeOffers);
        var moved = PlayFrom(placed, new LandscapeShapeshift(SS, Terrain.Lake));
        Assert.Equal(Terrain.Lake, moved[SS].ShapeshifterHome);
        Assert.Equal(LandscapeOffer.None, moved[SS].LandscapeOffers);
        Assert.Equal(placed[SS].Power, moved[SS].Power); // nothing paid
    }

    [Fact]
    public void The_Floating_Palace_goes_on_a_river_next_to_the_Riverwalkers_for_a_priest()
    {
        var s = Play(RiverwalkersStart);
        var placed = PlayFrom(s, new PlaceLandscape(RW, 19)); // river 19 touches their 6
        Assert.Equal(new Piece(RW, PieceKind.Landscape, 0), placed.Map[19]);
        Assert.Equal(1, s[RW].Priests.Count - placed[RW].Priests.Count + placed[RW].PriestDebt - s[RW].PriestDebt);
    }
}
