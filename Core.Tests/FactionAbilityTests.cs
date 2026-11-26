using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// What makes each faction different, beyond the numbers on its board. Most tests start one faction alone (Solo):
// pass tile 9 (3 power, so 2/10/0 from the usual 5/7/0), 15 coins and 6 workers unless said otherwise, round 1
// with tile 0 (5 VP per stronghold or sanctuary), 20 VP.
public class FactionAbilityTests
{
    private const Faction Au = Faction.Auren;
    private const Faction CM = Faction.ChaosMagicians;
    private const Faction N = Faction.Nomads;
    private const Faction F = Faction.Fakirs;
    private const Faction H = Faction.Halflings;
    private const Faction Cu = Faction.Cultists;
    private const Faction Al = Faction.Alchemists;
    private const Faction Dk = Faction.Darklings;
    private const Faction Sw = Faction.Swarmlings;
    private const Faction Dw = Faction.Dwarves;
    private const Faction E = Faction.Engineers;

    private static int _nextNumber = 50; // piece numbers for buildings put down by the tests (only IDs)

    // Buildings put straight onto the map (replacing whatever is there).
    private static GameState Put(GameState s, Faction f, params (int Hex, PieceKind Kind)[] buildings)
    {
        foreach (var (hex, kind) in buildings)
            s = s with { Map = s.Map.SetItem(hex, new Piece(f, kind, _nextNumber++)) };
        return s;
    }

    // The faction's first trading post on this hex (out of its bank), ready to be upgraded.
    private static GameState WithTradingPost(GameState s, Faction f, int hex) =>
        s.WithPlayer(s[f] with { TradingPostBank = s[f].TradingPostBank.SetItem(0, null) }) with
        {
            Map = s.Map.SetItem(hex, new Piece(f, PieceKind.TradingPost, 0)),
        };

    // As if the stronghold had been built: its bank emptied (its lasting effects and bonus action follow from that).
    private static GameState WithStronghold(GameState s, Faction f) =>
        s.WithPlayer(s[f] with { StrongholdBank = ValueList<Piece?>.Repeat(null, Config.StrongholdsPerPlayer) });

    // ---------- Auren ----------

    [Fact]
    public void The_Auren_stronghold_gives_a_favour_and_two_cult_steps_a_round()
    {
        var s = WithTradingPost(Play(Solo(Au, 9, 60)), Au, 9);
        Assert.Equal(new Cost(8, 4), Rules.UpgradeCost(s, PieceKind.Sanctuary, Au, 9));

        var built = PlayFrom(s, UpgradeTo(Au, PieceKind.Stronghold, 0, 9));
        Assert.Equal(1, built[Au].FavoursOwed);
        Assert.Equal(ValueList.Of(new BonusActionId(BonusSource.Stronghold, BonusAction.TwoCultSteps)), built[Au].BonusActions);

        var stepped = PlayFrom(built, new TakeFavour(Au, 0), End(Au), new BonusCultStep(Au, BonusSource.Stronghold, Track.Earth));
        Assert.Equal(2, stepped[Au].TrackSteps[(int)Track.Earth]);
        Assert.True(stepped.ActionTaken);
    }

    // ---------- Witches ----------

    [Fact]
    public void The_Witches_score_five_more_for_a_town()
    {
        var s = Put(Play(Solo(W, 9, 60)), W, (9, PieceKind.TradingPost), (8, PieceKind.TradingPost), (10, PieceKind.Dwelling), (21, PieceKind.TradingPost));
        var result = Rules.Apply(s, new FormTown(W, 0, 9)); // 9 VP, 1 priest, 1 key
        Assert.True(result.Accepted);
        Assert.Equal(34, result.State[W].VictoryPoints);
    }

    // ---------- Chaos Magicians ----------
    // Solo, their one dwelling on 5 (Wasteland). Next to 5: 4 Desert, 6 Plains, 17 Swamp. Their next dwelling is number 1.

    private static GameState ChaosReadyToDouble() => Ruled(WithStronghold(Play(Solo(CM, 5)), CM)); // the double action outside Free

    [Fact]
    public void Chaos_Magicians_earn_two_favours_for_a_temple_and_pay_less_for_their_stronghold()
    {
        var s = WithTradingPost(Play(Solo(CM, 5)), CM, 5);
        Assert.Equal(2, PlayFrom(s, UpgradeTo(CM, PieceKind.Temple, 0, 5))[CM].FavoursOwed);
        Assert.Equal(new Cost(4, 4), Rules.UpgradeCost(s, PieceKind.Stronghold, CM, 5));
        Assert.Equal(new Cost(8, 4), Rules.UpgradeCost(s, PieceKind.Sanctuary, CM, 5));
    }

    [Fact]
    public void The_double_action_gives_two_actions_in_one_turn()
    {
        var s = PlayFrom(ChaosReadyToDouble(), new BonusDoubleAction(CM, BonusSource.Stronghold));
        Assert.Equal(2, s.ActionsOwed);
        Assert.False(s.ActionTaken);

        s = PlayFrom(s, Build(CM, 1, 4)); // the first: a dwelling on 4
        Assert.Equal(1, s.ActionsOwed);
        Assert.False(s.ActionTaken);

        s = PlayFrom(s, new TransformTerrain(CM, 6, Terrain.Wasteland), new FinishSpades(CM)); // the second (no dwelling)
        Assert.Equal(0, s.ActionsOwed);
        Assert.True(s.ActionTaken);
        Assert.False(Rules.Apply(s, new TransformTerrain(CM, 17, Terrain.Wasteland)).Accepted); // no third
        Assert.True(Rules.Apply(s, End(CM)).Accepted);
    }

    [Fact]
    public void Passing_can_be_the_second_action_but_not_the_first()
    {
        var s = PlayFrom(ChaosReadyToDouble(), new BonusDoubleAction(CM, BonusSource.Stronghold));
        Assert.Equal("The Chaos Magicians take their first action before passing (passing can be the second).",
            Assert.Single(Rules.Apply(s, PassKeeping(CM)).Issues).Message);

        s = PlayFrom(s, Build(CM, 1, 4), PassKeeping(CM));
        Assert.True(s.HasPassed(CM));
        Assert.True(s.ActionTaken);
    }

    [Fact]
    public void The_turn_may_end_after_the_first_action_giving_up_the_second()
    {
        var s = PlayFrom(ChaosReadyToDouble(), new BonusDoubleAction(CM, BonusSource.Stronghold));
        Assert.False(Rules.Apply(s, End(CM)).Accepted); // not even one action yet

        var result = Rules.Apply(PlayFrom(s, Build(CM, 1, 4)), End(CM));
        Assert.True(result.Accepted);
        Assert.Contains(result.Issues, issue => issue.Message == "The Chaos Magicians end their turn without taking their second action.");
        Assert.Equal(0, result.State.ActionsOwed);
    }

    [Fact]
    public void Free_spades_from_the_first_action_are_spent_before_the_second()
    {
        var s = PlayFrom(Charged(ChaosReadyToDouble()), new BonusDoubleAction(CM, BonusSource.Stronghold), new TakePowerAction(CM, 4)); // 1 spade
        Assert.Equal("The Chaos Magicians must finish spending their free spades first (or press Done).",
            Assert.Single(Rules.Apply(s, Build(CM, 1, 6)).Issues).Message);

        s = PlayFrom(s, new SpendSpades(CM, 4, Terrain.Wasteland), new BuildOnSpadedHex(new Piece(CM, PieceKind.Dwelling, 1), 4));
        Assert.Equal(1, s.ActionsOwed);
        Assert.False(s.ActionTaken);
        Assert.True(Rules.Apply(s, new TransformTerrain(CM, 6, Terrain.Wasteland)).Accepted);
    }

    // ---------- Nomads ----------
    // Solo on 4, 88 and 106 (Desert), round 1 with tile 4 (2 VP per spade). Next to 4: 3 Lake, 5 Wasteland, 16 Plains, 17 Swamp.

    private static GameState NomadsWithStronghold() => WithStronghold(Play(Concat(
        Confirmed(N), SetUpAndStart(new[] { N }, new[] { 4, 0, 1, 2, 3, 6 }),
        new[] { StartAt(N, 0, 4), StartAt(N, 1, 88), StartAt(N, 2, 106) },
        new Move[] { new PickPassTile(N, 9) })), N);

    [Fact]
    public void The_sandstorm_turns_a_neighbouring_hex_into_desert_without_spades()
    {
        var s = NomadsWithStronghold();
        var result = Rules.Apply(s, new BonusSandstorm(N, BonusSource.Stronghold, 5));

        Assert.True(result.Accepted);
        Assert.Equal(Terrain.Desert, result.State.HexTerrain[5]);
        Assert.Equal(s[N].Workers, result.State[N].Workers);
        Assert.Equal(20, result.State[N].VictoryPoints); // no spades, so nothing for the spade tile
        Assert.True(result.State.SpadesPending);         // a dwelling may follow

        var built = PlayFrom(result.State, new BuildOnSpadedHex(new Piece(N, PieceKind.Dwelling, 3), 5));
        Assert.Equal((Piece?)new Piece(N, PieceKind.Dwelling, 3), built.Map[5]);
        Assert.Equal(13, built[N].Coins);
        Assert.Equal(5, built[N].Workers);
    }

    [Fact]
    public void The_sandstorm_reaches_only_neighbouring_hexes_and_the_dwelling_is_optional()
    {
        var s = NomadsWithStronghold();
        Assert.Equal("The sandstorm only reaches hexes next to the Nomads' buildings.",
            Assert.Single(Rules.Apply(s, new BonusSandstorm(N, BonusSource.Stronghold, 6)).Issues).Message);

        var stormed = PlayFrom(s, new BonusSandstorm(N, BonusSource.Stronghold, 16), new FinishSpades(N));
        Assert.False(stormed.SpadesPending);
        Assert.Null(stormed.Map[16]);
        Assert.True(Rules.Apply(stormed, End(N)).Accepted);
        Assert.Equal(new Cost(8, 4), Rules.UpgradeCost(s, PieceKind.Stronghold, N, 4));
    }

    // ---------- Fakirs ----------
    // Solo on 4 and 106 (Desert). Next to 4: 3, 5, 16, 17. Two hexes from 4: 2 Forest, 6 Plains, 29 Mountain.
    // Three hexes from 4: 40 Desert.

    [Fact]
    public void A_carpet_flight_skips_a_hex_for_a_priest_and_scores_four()
    {
        var s = PlayCharged(Solo(F, 4, 106), GetPriest(F));
        Assert.Equal(Reach.Carpet, Rules.ReachOf(s, F, 6, allowSkip: true));

        var result = Rules.Apply(s, Build(F, 2, 6)); // Plains: 1 spade to Desert
        Assert.True(result.Accepted);
        var after = result.State[F];
        Assert.Equal(24, after.VictoryPoints);
        Assert.Empty(after.Priests);
        Assert.Equal(Config.PriestsPerPlayer, after.PriestSupply.Count);
        Assert.Equal(13, after.Coins);
        Assert.Equal(2, after.Workers); // 6 - 3 for the spade - 1
    }

    [Fact]
    public void The_carpet_is_not_needed_next_door_and_reaches_further_with_the_stronghold()
    {
        var s = Play(Solo(F, 4, 106));
        Assert.Equal(Reach.Adjacent, Rules.ReachOf(s, F, 16, allowSkip: true));
        Assert.Equal(20, Rules.Apply(s, new TransformTerrain(F, 16, Terrain.Desert)).State[F].VictoryPoints); // no carpet: no VP

        Assert.Equal(1, s.CarpetRange(F));
        Assert.Equal(Reach.None, Rules.ReachOf(s, F, 40, allowSkip: true));
        Assert.Equal(2, WithStronghold(s, F).CarpetRange(F));
        Assert.Equal(Reach.Carpet, Rules.ReachOf(WithStronghold(s, F), F, 40, allowSkip: true));
        Assert.Equal(Reach.None, Rules.ReachOf(s, F, 6, allowSkip: false)); // e.g. spades between rounds
    }

    [Fact]
    public void The_carpet_reaches_only_one_hex_per_action_even_with_two_free_spades()
    {
        // The 2-spade power action; 6 (Plains) and 2 (Forest) are both two hexes from 4, so only reached by carpet.
        var s = PlayCharged(Solo(F, 4, 106), new TakePowerAction(F, 5));
        Assert.Equal(2, s[F].SpadesOwed);
        var flown = PlayFrom(s, new SpendSpades(F, 6, Terrain.Desert));
        Assert.True(flown.SkipUsed);
        Assert.Equal("The Fakirs have already used their carpet flight in this action: only one hex per action can be reached that way.",
            Assert.Single(Rules.Apply(flown, new SpendSpades(F, 2, Terrain.Mountain)).Issues).Message);

        // The next action may fly again (solo: the Fakirs' turn again). Done gives up the spade, then the dwelling.
        var later = PlayFrom(flown, new FinishSpades(F), new FinishSpades(F), End(F));
        Assert.Equal(F, later.ActivePlayer);
        var again = Rules.Apply(later, new TransformTerrain(F, 2, Terrain.Mountain));
        Assert.True(again.Accepted);
    }

    [Fact]
    public void Without_a_priest_the_carpet_still_flies_and_the_priest_is_owed()
    {
        var result = Rules.Apply(Play(Solo(F, 4, 106)), new TransformTerrain(F, 6, Terrain.Desert));
        Assert.True(result.Accepted);
        Assert.Equal(1, result.State[F].PriestDebt);
        Assert.Contains(result.Issues, issue => issue.Severity == Severity.Warning && issue.Message.Contains("1 priest"));
        Assert.Contains<GameEvent>(new PriestDebtIncurred(F, 1), result.Events);
    }

    [Fact]
    public void Fakirs_upgrade_spades_only_once_and_the_shipping_key_lengthens_the_carpet()
    {
        var s = Play(Solo(F, 4, 106));
        Assert.Equal("The Fakirs cannot upgrade their spades any further.",
            Assert.Single(Rules.Apply(s.WithPlayer(s[F] with { SpadeUpgrades = 1 }), new UpgradeSpades(F)).Issues).Message);

        var town = Put(s, F, (4, PieceKind.TradingPost), (3, PieceKind.TradingPost), (16, PieceKind.TradingPost), (17, PieceKind.Dwelling));
        var result = Rules.Apply(town, new FormTown(F, 5, 4)); // 4 VP and, for the Fakirs, +1 carpet range
        Assert.True(result.Accepted);
        Assert.Equal(2, result.State.CarpetRange(F));
        Assert.Equal(0, result.State[F].Shipping);
        Assert.Equal(24, result.State[F].VictoryPoints);
        Assert.Contains<GameEvent>(new CarpetRangeIncreased(F, 2), result.Events);
    }

    // ---------- Dwarves ----------
    // Solo on 1 and 108 (Mountain). Two hexes from 1: 3 Lake, 27 Swamp. Three hexes from 1: 16 Plains.

    [Fact]
    public void Tunnelling_skips_one_hex_for_two_workers_and_scores_four()
    {
        var s = Play(Solo(Dw, 1, 108));
        Assert.Equal(Reach.Tunnel, Rules.ReachOf(s, Dw, 3, allowSkip: true));
        Assert.Equal(Reach.None, Rules.ReachOf(s, Dw, 16, allowSkip: true));
        Assert.Equal(new Cost(2, 9), Rules.BuildCost(s, Dw, 3)); // dwelling, 2 spades (Lake to Mountain), tunnel

        var result = Rules.Apply(s, new TransformTerrain(Dw, 3, Terrain.Forest)); // 1 spade
        Assert.True(result.Accepted);
        Assert.Equal(1, result.State[Dw].Workers); // 6 - 3 - 2
        Assert.Equal(24, result.State[Dw].VictoryPoints);

        var cheaper = Rules.Apply(WithStronghold(s, Dw), new TransformTerrain(Dw, 3, Terrain.Forest));
        Assert.Equal(2, cheaper.State[Dw].Workers); // the stronghold: 1 worker to tunnel
    }

    [Fact]
    public void The_shipping_key_gives_the_Dwarves_nothing()
    {
        var town = Put(Play(Solo(Dw, 1, 108)), Dw, (1, PieceKind.TradingPost), (0, PieceKind.TradingPost), (2, PieceKind.TradingPost), (13, PieceKind.Dwelling));
        var result = Rules.Apply(town, new FormTown(Dw, 5, 1));
        Assert.True(result.Accepted);
        Assert.Equal(24, result.State[Dw].VictoryPoints);
        Assert.Contains(result.Issues, issue => issue.Message == "The Dwarves have no shipping, so the free shipping upgrade is lost.");
    }

    [Fact]
    public void Carpets_and_tunnels_join_networks_instead_of_shipping()
    {
        Assert.Equal(2, EndGame.LargestNetwork(Put(Play(Solo(F, 4, 106)), F, (6, PieceKind.Dwelling)), F));
        Assert.Equal(2, EndGame.LargestNetwork(Put(Play(Solo(Dw, 1, 108)), Dw, (3, PieceKind.Dwelling)), Dw));
        Assert.Equal(1, EndGame.LargestNetwork(Put(Play(SoloStart), G, (3, PieceKind.Dwelling)), G)); // two hexes from 5
    }

    // ---------- Halflings ----------
    // Solo on 16 and 107 (Plains), power 0/12/0. Next to 16: 3 Lake (2 spades to Plains), 4 Desert (1), 17 Swamp (1).

    [Fact]
    public void Halflings_score_a_point_per_spade()
    {
        var s = Play(Solo(H, 16, 107));
        Assert.Equal(21, Rules.Apply(s, new TransformTerrain(H, 4, Terrain.Plains)).State[H].VictoryPoints);
        Assert.Equal(22, Rules.Apply(s, new TransformTerrain(H, 3, Terrain.Plains)).State[H].VictoryPoints);
    }

    [Fact]
    public void The_Halflings_stronghold_gives_three_spades_at_once()
    {
        var built = PlayFrom(WithTradingPost(Play(Solo(H, 16, 107)), H, 16), UpgradeTo(H, PieceKind.Stronghold, 0, 16));
        Assert.Equal(3, built[H].SpadesOwed);
        Assert.True(built.SpadesPending);
        Assert.Equal(7, built[H].Coins); // 15 - 8

        // 4 (1 spade) and 3 (2) become Plains: a VP per spade, after 5 for the stronghold.
        var spent = PlayFrom(built, new SpendSpades(H, 4, Terrain.Plains), new SpendSpades(H, 3, Terrain.Plains));
        Assert.Equal(28, spent[H].VictoryPoints);
        Assert.True(Rules.Apply(spent, new BuildOnSpadedHex(new Piece(H, PieceKind.Dwelling, 2), 4)).Accepted);
    }

    // The stronghold's spades are a reward after the action, so conversions may come between them.
    [Fact]
    public void The_Halflings_may_convert_between_their_stronghold_spades()
    {
        var built = PlayFrom(WithTradingPost(Play(Solo(H, 16, 107)), H, 16), UpgradeTo(H, PieceKind.Stronghold, 0, 16));
        built = built.WithPlayer(built[H] with { Workers = 3 });
        var spent = PlayFrom(built, new SpendSpades(H, 4, Terrain.Plains));
        var converted = PlayFrom(spent, new ConvertWorkerToCoin(H));
        Assert.Equal(spent[H].Coins + 1, converted[H].Coins);
        Assert.True(Rules.Apply(PlayFrom(converted, new SpendSpades(H, 3, Terrain.Plains)), new ConvertWorkerToCoin(H)).Accepted);
    }

    [Fact]
    public void Halflings_upgrade_spades_for_two_workers_a_coin_and_a_priest()
    {
        var after = Rules.Apply(PlayCharged(Solo(H, 16, 107), GetPriest(H)), new UpgradeSpades(H)).State[H];
        Assert.Equal(14, after.Coins);
        Assert.Equal(4, after.Workers);
        Assert.Equal(26, after.VictoryPoints);
        Assert.Empty(after.Priests);
    }

    // ---------- Cultists ----------
    // Witches on 9 and 60, Cultists on 49 and 0 (Plains); the Witches pass, then the Cultists build on 61 (Swamp),
    // next to their 49 and the Witches' 60.

    private static GameState CultistsBuiltNextToWitches() => PlayFrom(
        Play(StartWith(new[] { W, Cu }, WitchesStartA, 49, 0, WitchesStartB)),
        Concat(PassAndEnd(W), new[] { Build(Cu, 2, 61) }));

    [Fact]
    public void Opponents_are_asked_even_about_one_free_power()
    {
        Assert.Equal(ValueList.Of(new PowerOffer(W, 1, 61, Cu)), CultistsBuiltNextToWitches().PowerOffers);
    }

    [Fact]
    public void When_an_opponent_accepts_the_Cultists_take_a_cult_step()
    {
        var result = Rules.Apply(CultistsBuiltNextToWitches(), Accept(W));
        var s = result.State;
        Assert.Equal(1, s[Cu].CultStepsOwed);
        Assert.Contains<GameEvent>(new CultStepOwed(Cu), result.Events);
        Assert.Equal("The Cultists must choose a track for their cult step first.", Assert.Single(Rules.Apply(s, End(Cu)).Issues).Message);

        s = PlayFrom(s, new TakeCultStep(Cu, Track.Water));
        Assert.Equal(1, s[Cu].TrackSteps[(int)Track.Water]);
        Assert.Equal(0, s[Cu].CultStepsOwed);
        Assert.True(Rules.Apply(s, End(Cu)).Accepted);
    }

    // As on BGA (854471936 #316): "declines advancing on a Cult track (Cultists Ability)".
    [Fact]
    public void The_Cultists_may_give_up_their_cult_step()
    {
        var s = PlayFrom(CultistsBuiltNextToWitches(), Accept(W));
        var steps = s[Cu].TrackSteps;
        Assert.Equal("Only the Cultists may give up their cult step.", Assert.Single(Rules.Apply(s, new DeclineCultStep(W)).Issues).Message);

        s = PlayFrom(s, new DeclineCultStep(Cu));
        Assert.Equal(0, s[Cu].CultStepsOwed);
        Assert.Equal(steps, s[Cu].TrackSteps);
        Assert.True(Rules.Apply(s, End(Cu)).Accepted);
        Assert.False(Rules.Apply(s, new DeclineCultStep(Cu)).Accepted);
    }

    [Fact]
    public void When_every_opponent_declines_the_Cultists_gain_one_power()
    {
        var before = CultistsBuiltNextToWitches();
        var s = PlayFrom(before, Decline(W));
        Assert.Equal(before[Cu].Power.Gain(1), s[Cu].Power);
        Assert.Equal(0, s[Cu].CultStepsOwed);
        Assert.Null(s.PendingCultistReaction);
    }

    [Fact]
    public void The_Cultists_stronghold_scores_seven()
    {
        var s = PlayFrom(WithTradingPost(Play(Solo(Cu, 6, 0)), Cu, 6), UpgradeTo(Cu, PieceKind.Stronghold, 0, 6));
        Assert.Equal(32, s[Cu].VictoryPoints); // 5 for round 1's tile, 7 for the Cultists
        Assert.Equal(7, s[Cu].Coins);         // 15 - 8
    }

    // ---------- Alchemists ----------
    // Solo on 7 and 101 (Swamp). Next to 7: 6 Plains (1 spade to Swamp), 8 Wasteland, 20 Desert.

    [Fact]
    public void Alchemists_trade_victory_points_and_coins()
    {
        var s = Play(Solo(Al, 7, 101));
        var sold = PlayFrom(s, new ConvertVictoryPointToCoin(Al));
        Assert.Equal(19, sold[Al].VictoryPoints);
        Assert.Equal(16, sold[Al].Coins);
        Assert.False(sold.ActionTaken);

        var bought = PlayFrom(s, new ConvertCoinsToVictoryPoint(Al));
        Assert.Equal(21, bought[Al].VictoryPoints);
        Assert.Equal(13, bought[Al].Coins);

        Assert.False(Rules.Apply(s.WithPlayer(s[Al] with { Coins = 1 }), new ConvertCoinsToVictoryPoint(Al)).Accepted);
        Assert.Equal("Only the Alchemists can turn victory points into coins.",
            Assert.Single(Rules.Apply(Play(SoloStart), new ConvertVictoryPointToCoin(G)).Issues).Message);
    }

    [Fact]
    public void The_Alchemists_stronghold_gives_twelve_power_then_two_per_spade()
    {
        var s = WithTradingPost(Play(Solo(Al, 7, 101)), Al, 7);
        Assert.Equal(s[Al].Power, PlayFrom(s, new TransformTerrain(Al, 6, Terrain.Swamp))[Al].Power); // no stronghold: no power

        var built = PlayFrom(s, UpgradeTo(Al, PieceKind.Stronghold, 0, 7), End(Al));
        Assert.Equal(new Power(0, 2, 10), built[Al].Power); // 2/10/0 + 12
        Assert.Equal(new Power(0, 0, 12), PlayFrom(built, new TransformTerrain(Al, 6, Terrain.Swamp))[Al].Power); // 1 spade: 2
    }

    [Fact]
    public void The_Alchemists_stronghold_power_is_taken_after_the_power_offers()
    {
        // Witches on 9 and 60, with a trading post put on 8, next to the Alchemists' trading post on 7: offered 2 power.
        const Faction W = Faction.Witches;
        var s = WithTradingPost(Play(StartWith(new[] { Al, W }, 7, 9, 60, 101)), Al, 7);
        s = s with { Map = s.Map.SetItem(8, new Piece(W, PieceKind.TradingPost, 0)) };
        var built = PlayFrom(s, UpgradeTo(Al, PieceKind.Stronghold, 0, 7));
        Assert.Contains(built.PowerOffers, offer => offer.Player == W && offer.Builder == Al);
        Assert.Equal(s[Al].Power, built[Al].Power); // not yet: held for the end of the action
        Assert.Equal(Config.AlchemistsStrongholdPower, built[Al].PowerHeld);

        Assert.Equal("Waiting for the Witches to accept or decline power.", Assert.Single(Rules.Apply(built, new TakeHeldPower(Al)).Issues).Message);

        // A reward after the action, taken when the Alchemists choose (241070597: after a conversion).
        var answered = PlayFrom(built, Decline(W));
        Assert.Equal(s[Al].Power, answered[Al].Power);
        Assert.Equal(Config.AlchemistsStrongholdPower, answered[Al].PowerHeld);
        var taken = PlayFrom(answered, new TakeHeldPower(Al));
        Assert.NotEqual(s[Al].Power, taken[Al].Power);
        Assert.Equal(0, taken[Al].PowerHeld);
        Assert.Equal(taken[Al].Power, PlayFrom(taken, End(Al))[Al].Power); // gained once only
        Assert.Equal(taken[Al].Power, PlayFrom(answered, End(Al))[Al].Power); // not taken: at End turn
    }

    // The power offers are part of the action; its rewards (a favour, a town, the held power) wait.
    [Fact]
    public void Rewards_after_the_action_wait_for_its_power_offers()
    {
        const Faction W = Faction.Witches;
        var s = WithTradingPost(Play(StartWith(new[] { Al, W }, 7, 9, 60, 101)), Al, 7);
        s = s with { Map = s.Map.SetItem(8, new Piece(W, PieceKind.TradingPost, 0)) };
        var built = PlayFrom(s, UpgradeTo(Al, PieceKind.Stronghold, 0, 7));
        var owing = built.WithPlayer(built[Al] with { FavoursOwed = 1 });
        Assert.Equal("Waiting for the Witches to accept or decline power.", Assert.Single(Rules.Apply(owing, new TakeFavour(Al, 0)).Issues).Message);
        Assert.False(Rules.Apply(owing, new ConvertWorkerToCoin(Al)).Accepted);
        Assert.True(Rules.Apply(PlayFrom(owing, Decline(W)), new TakeFavour(Al, 0)).Accepted);
    }

    [Fact]
    public void Alchemists_score_a_point_per_two_coins_at_the_end()
    {
        var s = Play(Solo(Al, 7, 101));
        s = s.WithPlayer(s[Al] with { Coins = 7, Workers = 0, Power = new Power(12, 0, 0) });
        Assert.Equal(3, Assert.Single(EndGame.Finish(s, new List<GameEvent>()).FinalScores).CoinPoints);
    }

    // ---------- Darklings ----------
    // Solo on 17 and 101 (Swamp): 4 workers, 1 priest. Next to 17: 4 Desert (2 spades to Swamp), 16 Plains (1).

    [Fact]
    public void Darklings_pay_a_priest_per_spade_and_score_two_for_it()
    {
        var s = Play(Solo(Dk, 17, 101));
        Assert.Equal(new Cost(0, 0, 1), Rules.TransformCost(s, Dk, 16, Terrain.Swamp));

        var result = Rules.Apply(s, new TransformTerrain(Dk, 16, Terrain.Swamp));
        Assert.True(result.Accepted);
        Assert.Empty(result.Issues);
        Assert.Empty(result.State[Dk].Priests);
        Assert.Equal(4, result.State[Dk].Workers);
        Assert.Equal(22, result.State[Dk].VictoryPoints);

        // With no priest ready the spades are still made, the priests owed (with a warning) and scored as paid.
        var unpaid = Rules.Apply(PlayFrom(result.State, new FinishSpades(Dk), End(Dk)), new TransformTerrain(Dk, 4, Terrain.Swamp));
        Assert.True(unpaid.Accepted);
        Assert.True(unpaid.State[Dk].PriestDebt > 0);
        Assert.Contains(unpaid.Issues, issue => issue.Severity == Severity.Warning && issue.Message.Contains("priest"));
        Assert.Equal(22 + Config.DarklingsVictoryPointsPerPriestSpade * unpaid.State[Dk].PriestDebt, unpaid.State[Dk].VictoryPoints);

        Assert.Equal("The Darklings cannot upgrade their spades.", Assert.Single(Rules.Apply(s, new UpgradeSpades(Dk)).Issues).Message);
    }

    [Fact]
    public void The_Darklings_stronghold_exchanges_up_to_three_workers_for_priests()
    {
        var s = WithTradingPost(Play(Solo(Dk, 17, 101)), Dk, 17);
        s = s.WithPlayer(s[Dk] with { Workers = 10 });
        Assert.Equal(new Cost(10, 4), Rules.UpgradeCost(s, PieceKind.Sanctuary, Dk, 17));

        var built = PlayFrom(s, UpgradeTo(Dk, PieceKind.Stronghold, 0, 17)); // 6 workers left
        Assert.Equal(3, built[Dk].PriestExchangesLeft);
        Assert.False(Rules.Apply(built, new ExchangeWorkersForPriests(Dk, 4)).Accepted);

        var exchanged = PlayFrom(built, new ExchangeWorkersForPriests(Dk, 1), new ExchangeWorkersForPriests(Dk, 1)); // one at a time
        Assert.Equal(4, exchanged[Dk].Workers);
        Assert.Equal(3, exchanged[Dk].Priests.Count); // the one they started with, and two more
        Assert.Equal(1, exchanged[Dk].PriestExchangesLeft);
        Assert.False(Rules.Apply(exchanged, new ExchangeWorkersForPriests(Dk, 2)).Accepted);

        // The one not made is lost with the turn.
        var ended = PlayFrom(exchanged, End(Dk));
        Assert.Equal(0, ended[Dk].PriestExchangesLeft);
        Assert.False(Rules.Apply(ended, new ExchangeWorkersForPriests(Dk, 1)).Accepted);

        // Or given up before the end of the turn (the step button's tick): no more exchanges, nothing else changes.
        var finished = Rules.Apply(exchanged, new FinishPriestExchange(Dk));
        Assert.True(finished.Accepted);
        Assert.Equal(0, finished.State[Dk].PriestExchangesLeft);
        Assert.Equal(4, finished.State[Dk].Workers);
        Assert.False(Rules.Apply(finished.State, new ExchangeWorkersForPriests(Dk, 1)).Accepted);
        Assert.False(Rules.Apply(finished.State, new FinishPriestExchange(Dk)).Accepted);
    }

    // ---------- Mermaids ----------
    // Solo on 3 and 89 (Lake), pass tile 0. Rivers 14 and 15 both touch 2 and 3 on one side and 27 on the other.

    private static GameState MermaidsByTheRiver() => Put(Play(Solo(M, 3, 89)), M,
        (3, PieceKind.TradingPost), (2, PieceKind.TradingPost), (27, PieceKind.TradingPost), (39, PieceKind.Dwelling));

    [Fact]
    public void The_Mermaids_stronghold_upgrades_shipping_at_once()
    {
        var s = WithTradingPost(Play(Solo(M, 3, 89)), M, 3);
        Assert.Equal(new Cost(8, 4), Rules.UpgradeCost(s, PieceKind.Sanctuary, M, 3));

        var result = Rules.Apply(s, UpgradeTo(M, PieceKind.Stronghold, 0, 3));
        Assert.Equal(2, result.State[M].Shipping);
        Assert.Equal(27, result.State[M].VictoryPoints); // 5 for round 1's tile, 2 for the upgrade
        Assert.Contains<GameEvent>(new ShippingUpgraded(M, 2), result.Events);
        Assert.Empty(result.State[M].BonusActions);
    }

    [Fact]
    public void A_key_on_a_river_hex_joins_two_groups_into_a_town()
    {
        var s = MermaidsByTheRiver();
        Assert.Empty(Rules.FormableTowns(s, M)); // each group alone is too small
        var rivers = Rules.RiverTowns(s, M);
        Assert.Equal(new[] { 14, 15 }, rivers.Select(river => river.RiverHex));
        Assert.All(rivers, river => Assert.Equal(ValueList.Of(2, 3, 27, 39), river.Hexes));

        var result = Rules.Apply(s, new FormTown(M, 0, 15)); // 9 VP, 1 priest, 1 key
        Assert.True(result.Accepted);
        Assert.Equal(ValueList.Of(2, 3, 27, 39), result.State[M].TownHexes);
        Assert.Equal(15, Assert.Single(result.State.PlacedKeys).Hex);
        Assert.Equal(29, result.State[M].VictoryPoints);
        Assert.Empty(Rules.RiverTowns(result.State, M));
    }

    [Fact]
    public void A_river_town_is_optional_and_only_for_the_Mermaids()
    {
        var s = MermaidsByTheRiver();
        Assert.True(Rules.Apply(PlayFrom(s, PassKeeping(M)), End(M)).Accepted);
        Assert.Equal("That river hex does not join buildings into a town that can be formed now.",
            Assert.Single(Rules.Apply(s, new FormTown(M, 0, 26)).Issues).Message); // touches only 27 and 39
    }

    [Fact]
    public void A_town_through_the_Coral_Reef_is_a_choice_with_its_key_on_the_Reef()
    {
        // The Reef on river 14 joins {2, 3} and {27, 39}; river 15 joins them too, but through the Reef's town only.
        var s = MermaidsByTheRiver();
        s = s with { Map = s.Map.SetItem(14, new Piece(M, PieceKind.Landscape, 0)) };
        Assert.Empty(Rules.FormableTowns(s, M)); // never forced
        var river = Assert.Single(Rules.RiverTowns(s, M));
        Assert.Equal(14, river.RiverHex);
        Assert.Equal(ValueList.Of(2, 3, 14, 27, 39), river.Hexes);
        Assert.Equal("That river hex does not join buildings into a town that can be formed now.",
            Assert.Single(Rules.Apply(s, new FormTown(M, 0, 15)).Issues).Message);
        Assert.Equal("That hex is not part of a town that can be formed now.",
            Assert.Single(Rules.Apply(s, new FormTown(M, 0, 2)).Issues).Message);

        var result = Rules.Apply(s, new FormTown(M, 0, 14));
        Assert.True(result.Accepted);
        Assert.Equal(ValueList.Of(2, 3, 14, 27, 39), result.State[M].TownHexes);
        Assert.Equal(14, Assert.Single(result.State.PlacedKeys).Hex);
    }

    // ---------- Swarmlings ----------
    // Solo on 3 and 89 (Lake): 20 coins, 12 workers.

    [Fact]
    public void Swarmlings_pay_more_for_buildings()
    {
        var s = Play(Solo(Sw, 3, 89));
        Assert.Equal(20, s[Sw].Coins);
        Assert.Equal(12, s[Sw].Workers); // 8 starting, 2 base income, 2 under the emptied slots
        Assert.Equal(new Cost(3, 11), Rules.BuildCost(s, Sw, 4));                     // dwelling and 3 spades (Desert to Lake)
        Assert.Equal(new Cost(8, 3), Rules.UpgradeCost(s, PieceKind.TradingPost, Sw, 3)); // no opponent nearby: coins doubled
        Assert.Equal(new Cost(6, 3), Rules.UpgradeCost(s, PieceKind.Temple, Sw, 3));
        Assert.Equal(new Cost(8, 5), Rules.UpgradeCost(s, PieceKind.Stronghold, Sw, 3));
        Assert.Equal(new Cost(8, 5), Rules.UpgradeCost(s, PieceKind.Sanctuary, Sw, 3));
    }

    [Fact]
    public void Swarmlings_gain_three_workers_for_a_town()
    {
        var s = Put(Play(Solo(Sw, 3, 89)), Sw, (3, PieceKind.TradingPost), (2, PieceKind.TradingPost), (16, PieceKind.TradingPost), (4, PieceKind.Dwelling));
        var result = Rules.Apply(s, new FormTown(Sw, 0, 3)); // 9 VP, 1 priest, 1 key
        Assert.True(result.Accepted);
        Assert.Equal(s[Sw].Workers + 3, result.State[Sw].Workers);
        Assert.Single(result.State[Sw].Priests);
    }

    // ---------- Engineers ----------
    // Solo on 1 and 108 (Mountain): 10 coins, 5 workers; a dwelling put on 17 (bridge edge 17-31 across rivers 18
    // and 30). 108 is at one end of the bridge edge 97-108.

    private static GameState EngineersAtTheBridge() => Put(Play(Solo(E, 1, 108)), E, (17, PieceKind.Dwelling));

    [Fact]
    public void Engineers_build_a_bridge_for_two_workers_as_their_action()
    {
        var result = Rules.Apply(EngineersAtTheBridge(), new BuildBridge(E));
        Assert.True(result.Accepted);
        Assert.Equal(1, result.State[E].BridgesOwed);
        Assert.Equal(2, result.State[E].Workers); // 2 at the start, 2 income (no base worker), 2 spent
        Assert.True(result.State.ActionTaken);

        var again = PlayFrom(result.State, new PlaceBridge(E, 17, 31), End(E), new BuildBridge(E)); // any number of times
        Assert.Single(again.Bridges);
        Assert.Equal(1, again[E].BridgesOwed);
        Assert.Equal(0, again[E].Workers);

        Assert.Equal("Only the Engineers can build a bridge as an action.",
            Assert.Single(Rules.Apply(Play(SoloStart), new BuildBridge(G)).Issues).Message);
    }

    [Fact]
    public void With_their_stronghold_the_Engineers_score_their_bridges_between_their_buildings_when_passing()
    {
        var joined = Put(EngineersAtTheBridge(), E, (31, PieceKind.Dwelling)) with { Bridges = ValueList.Of(new Bridge(E, 17, 31)) };
        Assert.Equal(20, PlayFrom(joined, PassKeeping(E))[E].VictoryPoints); // no stronghold yet
        Assert.Equal(23, PlayFrom(WithStronghold(joined, E), PassKeeping(E))[E].VictoryPoints);

        var oneEnd = EngineersAtTheBridge() with { Bridges = ValueList.Of(new Bridge(E, 17, 31)) };
        Assert.Equal(20, PlayFrom(WithStronghold(oneEnd, E), PassKeeping(E))[E].VictoryPoints);
    }

    [Fact]
    public void Engineers_build_cheaply()
    {
        var s = EngineersAtTheBridge();
        Assert.Equal(new Cost(1, 4), Rules.BuildCost(s, E, 2));                         // dwelling 1/1 and 1 spade (Forest)
        Assert.Equal(new Cost(4, 1), Rules.UpgradeCost(s, PieceKind.TradingPost, E, 1)); // 2/4 coins
        Assert.Equal(new Cost(4, 1), Rules.UpgradeCost(s, PieceKind.Temple, E, 1));
        Assert.Equal(new Cost(6, 3), Rules.UpgradeCost(s, PieceKind.Stronghold, E, 1));
        Assert.Equal(new Cost(6, 3), Rules.UpgradeCost(s, PieceKind.Sanctuary, E, 1));
    }
}
