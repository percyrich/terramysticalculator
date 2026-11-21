using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Fan faction tests with Fire & Ice in play (Rules.FanFactions.cs; the rest are in FanFactionTests). One faction alone
// (Calculator, Free play) on the base map, fan factions and Fire & Ice in. Hexes: Lake 3 and 89; Desert 4 and 88;
// Plains 16 and 107; Swamp 17 and 101; Forest 9 and 60. Next to 3: 2 Forest, 4 Desert, river 15, 16 Plains. River 15
// touches 2, 3, 16 and 27 (Swamp). Next to 4: 3, 5, 16, 17. Next to 17: 16, river 18, 4, 5, 29, river 30; river 18
// touches 5, 6, 31 (Forest), 30; river 30 touches 29, 31, 42, 43 (Wasteland). Next to 9: 8 Plains, 10 Lake, 21 Swamp.
public class FanFactionFireAndIceTests
{
    private const Faction Wy = Faction.ChildrenOfTheWyrm, At = Faction.Atlanteans, Se = Faction.Selkies,
        Sn = Faction.SnowShamans, Fw = Faction.Firewalkers, Em = Faction.KingdomOfEmber, Ge = Faction.Geologists,
        Ch = Faction.Changelings;

    private static readonly GameOptions FanOptions = GameOptions.All with { Calculator = true, Landscapes = false, FanFactions = true };

    // The faction alone: chosen (with its starting terrain, if it has one to choose), the extra setup moves, the map and
    // round tiles, then its starting dwellings (the Atlanteans' stronghold) on these hexes, the moves before the pass
    // tile, and the pick: round 1 has begun.
    private static Move[] Start(Faction f, Terrain? terrain, int[] hexes, Move[]? beforeMap = null, Move[]? beforePick = null)
    {
        var moves = new List<Move> { new ChooseGame(GameKind.TerraMystica, FanOptions), new AddPlayer(f) };
        var kind = Factions.Kind(f);
        if (kind is FactionKind.Ice or FactionKind.Variable)
            moves.Add(new ChooseStartingTerrain(f, terrain!.Value));
        moves.Add(new ConfirmFactions());
        if (kind == FactionKind.Volcano)
            moves.Add(new ChooseStartingTerrain(f, terrain!.Value));
        moves.AddRange(beforeMap ?? System.Array.Empty<Move>());
        moves.AddRange(SetUpAndStart(new[] { f }, TestRoundTiles));
        var kindPlaced = f == Faction.Atlanteans ? PieceKind.Stronghold : PieceKind.Dwelling;
        for (var i = 0; i < hexes.Length; i++)
            moves.Add(new PlaceStartingDwelling(new Piece(f, kindPlaced, i), hexes[i]));
        moves.AddRange(beforePick ?? System.Array.Empty<Move>());
        moves.Add(new PickPassTile(f, 9));
        return moves.ToArray();
    }

    private static int _nextNumber = 50;

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

    private static GameState Rich(GameState s, Faction f) => s.WithPlayer(s[f] with { Coins = 30, Workers = 30 });

    private static string Refusal(GameState s, Move move) => Assert.Single(Rules.Apply(s, move).Issues).Message;

    private static GameState WithLava(GameState s, int hex, int lava) =>
        s with { Lava = ValueList<int>.Repeat(0, s.Layout.HexCount).SetItem(hex, lava) };

    // ---------- Children of the Wyrm ----------

    [Fact]
    public void Wyrm_structures_cost_double_coins_with_no_opponent_next_to_them()
    {
        var s = Play(Start(Wy, null, new[] { 17, 101 }));
        Assert.Equal(new Cost(2, 1), Rules.DwellingCost(s, Wy, 16));
        var neighbour = s with { Neutral = new NeutralBoard(ValueList.Of(4), ValueList<(int, int)>.Empty, ValueList<(Track, int)>.Empty) };
        Assert.Equal(new Cost(1, 1), Rules.DwellingCost(neighbour, Wy, 16));
    }

    [Fact]
    public void Wyrm_burning_moves_two_tokens_on()
    {
        var s = Play(Start(Wy, null, new[] { 17, 101 }));
        s = s.WithPlayer(s[Wy] with { Power = new Power(0, 5, 0) });
        Assert.Equal(new Power(0, 2, 2), PlayFrom(s, new BurnPower(Wy))[Wy].Power);
    }

    [Fact]
    public void Wyrm_stronghold_brings_back_tokens_and_puts_tokens_on_rivers()
    {
        var s = Rich(WithTradingPost(Play(Start(Wy, null, new[] { 17, 101 })), Wy, 17), Wy);
        s = s.WithPlayer(s[Wy] with { Power = new Power(2, 3, 1) });
        var built = PlayFrom(s, UpgradeTo(Wy, PieceKind.Stronghold, 0, 17));
        Assert.Equal(Config.PowerTokens, built[Wy].Power.Total);
        Assert.Equal(8, built[Wy].Power.Bowl1);

        Assert.Equal("Power tokens go on river hexes.", Refusal(built, new BonusRiverTokens(Wy, BonusSource.Stronghold, 16)));
        var placed = PlayFrom(built, new BonusRiverTokens(Wy, BonusSource.Stronghold, 18, 30));
        Assert.Equal(ValueList.Of(18, 30), placed.WyrmTokenHexes);
        Assert.Equal(6, placed[Wy].Power.Bowl1); // bowl 1 first
        var adjacent = Rules.AdjacentHexes(placed, Wy, 17).ToList();
        Assert.Contains(31, adjacent); // across the chain
        Assert.Contains(43, adjacent);
        Assert.DoesNotContain(31, Rules.AdjacentHexes(placed, Faction.Giants, 17));

        var nothing = PlayFrom(built, new BonusRiverTokens(Wy, BonusSource.Stronghold, null)); // the action, used for nothing
        Assert.Empty(nothing.WyrmTokenHexes);
        Assert.Contains(new BonusActionId(BonusSource.Stronghold, BonusAction.RiverTokens), nothing[Wy].BonusActionsUsed);
    }

    [Fact]
    public void Wyrm_stronghold_tokens_come_back_before_a_town_still_to_form_when_no_offer_waits()
    {
        // 3 (trading post), 4, 16 and 17 (stronghold) touch: a town once the stronghold stands, still to form. No one
        // to offer power to: the tokens come at once, before the town (as BGA, 717052298).
        var s = Rich(WithTradingPost(Play(Start(Wy, null, new[] { 17, 101 })), Wy, 17), Wy);
        s = Put(s, Wy, (3, PieceKind.TradingPost), (4, PieceKind.Dwelling), (16, PieceKind.Dwelling));
        s = s.WithPlayer(s[Wy] with { Power = new Power(2, 3, 1) });
        var built = PlayFrom(s, UpgradeTo(Wy, PieceKind.Stronghold, 0, 17));
        Assert.NotEmpty(Rules.FormableTowns(built, Wy));
        Assert.Equal(Config.PowerTokens, built[Wy].Power.Total);
        Assert.Equal(8, built[Wy].Power.Bowl1); // 2 + the 6 back
        Assert.Equal(0, built[Wy].TokensHeld);
    }

    // ---------- Atlanteans ----------

    [Fact]
    public void Atlanteans_start_with_their_stronghold_and_its_town()
    {
        var moves = Start(At, null, new[] { 3 });
        var placed = Play(moves[..^1]);
        Assert.True(placed[At].StartingTownOwed);
        Assert.True(placed[At].StrongholdBuilt);
        Assert.Equal("The Atlanteans take the key token for their starting town first.", Refusal(placed, moves[^1]));
        Assert.Equal("The Atlanteans' starting key token goes on their stronghold.", Refusal(placed, new FormTown(At, 4, 2)));

        var town = PlayFrom(placed, new FormTown(At, 4, 3)); // 5 VP, 6 coins
        Assert.Contains(3, town[At].TownHexes);
        Assert.Equal(placed[At].Coins + 6, town[At].Coins);
        Assert.False(town[At].StartingTownOwed);
        PlayFrom(town, moves[^1]);
    }

    [Fact]
    public void Atlanteans_town_pays_out_as_its_power_grows()
    {
        var s = Play(Start(At, null, new[] { 3 }, beforePick: new Move[] { new FormTown(At, 4, 3) }));
        s = Put(s, At, (2, PieceKind.TradingPost), (4, PieceKind.TradingPost)); // 3 + 2 + 2
        var after = PlayFrom(s, new ConvertWorkerToCoin(At));
        Assert.Equal(1, after[At].AtlanteansRewards);
        Assert.Equal(s[At].Shipping + 1, after[At].Shipping);
    }

    // ---------- Selkies ----------

    [Fact]
    public void Selkies_stronghold_builds_with_a_free_spade()
    {
        var s = Rich(WithTradingPost(Play(Start(Se, Terrain.Lake, new[] { 3, 89 })), Se, 89), Se);
        var built = PlayFrom(s, UpgradeTo(Se, PieceKind.Stronghold, 0, 89));
        var paid = PlayFrom(built, Build(Se, 2, 16));
        var bonus = PlayFrom(built, new BonusSelkiesBuild(new Piece(Se, PieceKind.Dwelling, 2), BonusSource.Stronghold, 16));
        Assert.Equal(paid[Se].Workers + built[Se].SpadeCost.Workers, bonus[Se].Workers);
        Assert.Contains(new BonusActionId(BonusSource.Stronghold, BonusAction.SelkiesSpade), bonus[Se].BonusActionsUsed);
        Assert.False(bonus.ShippingBoost);
    }

    [Fact]
    public void Selkies_build_on_a_river_between_two_structures_apart()
    {
        var s = Rich(Play(Start(Se, Terrain.Lake, new[] { 3, 89 })), Se);
        s = Put(s, Se, (2, PieceKind.Dwelling));
        Assert.Equal("The Selkies build on a river hex only next to two of their structures that don't touch each other.",
            Refusal(s, Build(Se, 2, 15))); // 2 and 3 touch
        s = Put(s, Se, (27, PieceKind.Dwelling));
        var built = PlayFrom(s, Build(Se, 2, 15));
        Assert.Equal(Terrain.River, built.HexTerrain[15]);
        Assert.Equal(PieceKind.Dwelling, built.Map[15]!.Value.Kind);
        Assert.Equal(s[Se].Workers - 2, built[Se].Workers);
        Assert.Equal(s[Se].VictoryPoints + Config.SelkiesRiverDwellingVictoryPoints, built[Se].VictoryPoints);
        Assert.Equal("The Selkies' dwellings on rivers can't be upgraded.", Refusal(built, UpgradeTo(Se, PieceKind.TradingPost, 0, 15)));
    }

    [Fact]
    public void A_Selkies_town_never_takes_its_key_on_their_river_dwelling()
    {
        var s = Put(Play(Start(Se, Terrain.Lake, new[] { 3, 89 })), Se,
            (2, PieceKind.TradingPost), (3, PieceKind.TradingPost), (27, PieceKind.TradingPost), (15, PieceKind.Dwelling));
        Assert.Contains(Rules.FormableTowns(s, Se), town => town.Contains(15));
        Assert.Equal("A key can't go on a river hex.", Refusal(s, new FormTown(Se, 0, 15)));
        Assert.True(Rules.Apply(s, new FormTown(Se, 0, 2)).Accepted);
    }

    [Theory]
    [InlineData(Faction.IceMaidens)]
    [InlineData(Faction.Yetis)]
    [InlineData(Faction.Selkies)]
    [InlineData(Faction.SnowShamans)]
    public void Every_Ice_faction_s_8th_dwelling_gives_a_worker(Faction ice)
    {
        Assert.Equal(1, Factions.All[ice].DwellingIncome[7].Workers);
    }

    // ---------- Snow Shamans ----------

    [Fact]
    public void Snow_Shamans_step_up_for_free_when_passing_and_never_pay()
    {
        var s = Play(Start(Sn, Terrain.Lake, new[] { 3, 89 }));
        Assert.Equal("The Snow Shamans don't pay to advance: they take one free step each time they pass.", Refusal(s, new UpgradeShipping(Sn)));
        var passed = PlayFrom(s, PassKeeping(Sn));
        Assert.True(passed[Sn].FreeUpgradeOwed);
        Assert.Equal("The Snow Shamans must take their free shipping or spade step first.", Refusal(passed, End(Sn)));
        var stepped = PlayFrom(passed, new UpgradeSpades(Sn));
        Assert.Equal(1, stepped[Sn].SpadeUpgrades);
        Assert.Equal(passed[Sn].VictoryPoints, stepped[Sn].VictoryPoints);
        Assert.False(stepped[Sn].FreeUpgradeOwed);
    }

    // As on BGA (819403314 #65): the free step first, then the pass scores, so a shipping pass tile
    // counts the new level.
    [Fact]
    public void Snow_Shamans_pass_scores_after_their_free_step()
    {
        var shippingTile = Enumerable.Range(0, PassTiles.Count).First(i => PassTiles.All[i].PassVictoryPointsPerShipping > 0);
        var s = Play(Start(Sn, Terrain.Lake, new[] { 3, 89 }));
        s = s.WithPlayer(s[Sn] with { PassTile = shippingTile });
        var passed = PlayFrom(s, PassKeeping(Sn));
        Assert.Equal(s[Sn].VictoryPoints, passed[Sn].VictoryPoints); // nothing scored yet
        var shipped = PlayFrom(passed, new UpgradeShipping(Sn));
        Assert.Equal(s[Sn].Shipping + 1, shipped[Sn].Shipping);
        Assert.Equal(s[Sn].VictoryPoints + (s[Sn].Shipping + 1) * PassTiles.All[shippingTile].PassVictoryPointsPerShipping,
            shipped[Sn].VictoryPoints);
        Assert.Null(shipped[Sn].PassScoringHeld);
    }

    [Fact]
    public void Snow_Shamans_stronghold_puts_a_dwelling_on_each_mass_of_ice()
    {
        var s = Play(Start(Sn, Terrain.Lake, new[] { 3, 89 }));
        s = Rich(WithTradingPost(s with { HexTerrain = s.HexTerrain.SetItem(2, Terrain.Ice).SetItem(4, Terrain.Ice) }, Sn, 3), Sn);
        var built = PlayFrom(s, UpgradeTo(Sn, PieceKind.Stronghold, 0, 3));
        Assert.Single(built[Sn].IceMassesOwed!.Value); // 2, 3, 4; 89 is full
        var placed = PlayFrom(built, new PlaceFreeDwelling(new Piece(Sn, PieceKind.Dwelling, 2), 4));
        Assert.Null(placed[Sn].IceMassesOwed);
        Assert.Equal(PieceKind.Dwelling, placed.Map[4]!.Value.Kind);

        var declined = PlayFrom(built, new SkipFreeDwelling(Sn)); // Done: the dwellings declined
        Assert.Null(declined[Sn].IceMassesOwed);
        Assert.Null(declined.Map[4]);
    }

    // ---------- Firewalkers ----------

    [Fact]
    public void Firewalkers_pay_points_for_volcano_never_past_their_pyro()
    {
        var s = Play(Start(Fw, Terrain.Desert, new[] { 4, 88 }));
        Assert.Equal(s[Fw].VictoryPoints - Config.FirewalkersPyroBehind, s[Fw].Pyro);
        var made = PlayFrom(s, new TransformTerrain(Fw, 16, Terrain.Volcano));
        Assert.Equal(Terrain.Volcano, made.HexTerrain[16]);
        Assert.Equal(s[Fw].VictoryPoints - Config.FirewalkersVolcanoPoints, made[Fw].VictoryPoints);

        var ruled = Ruled(s);
        ruled = ruled.WithPlayer(ruled[Fw] with { VictoryPoints = s[Fw].Pyro + 2 });
        Assert.Equal($"The Firewalkers' VP ({s[Fw].Pyro - 2}) can't fall behind their pyro token ({s[Fw].Pyro}).",
            Refusal(ruled, new TransformTerrain(Fw, 16, Terrain.Volcano)));
    }

    [Fact]
    public void Firewalkers_move_their_pyro_for_power_and_back_for_spades()
    {
        var s = Charged(Play(Start(Fw, Terrain.Desert, new[] { 4, 88 })));
        var forward = PlayFrom(s, new AdvancePyro(Fw));
        Assert.Equal(s[Fw].Pyro + 1, forward[Fw].Pyro);
        Assert.Equal("The Firewalkers' pyro token can't pass their VP (20).",
            Refusal(s.WithPlayer(s[Fw] with { Pyro = 20, VictoryPoints = 20 }), new AdvancePyro(Fw)));
        var spade = PlayFrom(s, new TakePowerAction(Fw, 4)); // 1 spade
        Assert.Equal(s[Fw].Pyro - Config.FirewalkersPyroPerSpade, spade[Fw].Pyro);
    }

    // ---------- Kingdom of Ember ----------

    [Fact]
    public void Ember_spades_become_lava_under_a_dwelling()
    {
        var s = Charged(Play(Start(Em, Terrain.Desert, new[] { 4, 88 })));
        Assert.Equal("The Kingdom of Ember make Volcano only by letting the lava flow.", Refusal(s, new TransformTerrain(Em, 16, Terrain.Volcano)));
        var spade = PlayFrom(s, new TakePowerAction(Em, 4));
        Assert.Equal(1, spade[Em].LavaOwed);
        var placed = PlayFrom(spade, new PlaceLava(Em, 4));
        Assert.Equal(1, placed.LavaAt(4));
        Assert.Equal(0, placed[Em].LavaOwed);
        Assert.Equal("A dwelling with lava under it (more than one volcano tile) can't be upgraded.",
            Refusal(Rich(placed, Em), UpgradeTo(Em, PieceKind.TradingPost, 0, 4)));
    }

    [Fact]
    public void Ember_lava_flows_and_leaves_volcano_behind()
    {
        var s = Play(Start(Em, Terrain.Desert, new[] { 4, 88 }));
        Assert.Equal("Only a structure with lava under it (more than one volcano tile) can flow.",
            Refusal(s, new LetLavaFlow(Em, 4, ValueList.Of(16))));
        var flowed = PlayFrom(WithLava(s, 4, 2), new LetLavaFlow(Em, 4, ValueList.Of(16)));
        Assert.Null(flowed.Map[4]);
        Assert.Equal(Terrain.Volcano, flowed.HexTerrain[4]);
        Assert.Equal(Em, flowed.Map[16]!.Value.Owner);
        Assert.Equal(Terrain.Volcano, flowed.HexTerrain[16]);
        Assert.Equal(1, flowed.LavaAt(16));
        Assert.Equal("Lava can't stop on a river hex.", Refusal(WithLava(s, 4, 3), new LetLavaFlow(Em, 4, ValueList.Of(3, 15))));
    }

    // The board shows the flow as it goes (Rules.LavaFlowSoFar), the structure over a river too.
    [Fact]
    public void Ember_lava_so_far_is_the_flow_stopped_where_the_path_ends()
    {
        var s = WithLava(Play(Start(Em, Terrain.Desert, new[] { 4, 88 })), 4, 3);
        Assert.Same(s, Rules.LavaFlowSoFar(s, Em, 4, System.Array.Empty<int>()));

        var one = Rules.LavaFlowSoFar(s, Em, 4, new[] { 16 })!;
        var flowed = PlayFrom(s, new LetLavaFlow(Em, 4, ValueList.Of(16))); // as the flow itself
        Assert.Equal(flowed.Map, one.Map);
        Assert.Equal(flowed.HexTerrain, one.HexTerrain);
        Assert.Equal(flowed.Lava, one.Lava);
        Assert.Equal(2, one.LavaAt(16));

        var river = Rules.LavaFlowSoFar(s, Em, 4, new[] { 3, 15 })!; // on over Lake 3, onto river 15
        Assert.Equal(Terrain.Volcano, river.HexTerrain[3]);
        Assert.Equal(Terrain.River, river.HexTerrain[15]);
        Assert.Equal(Em, river.Map[15]!.Value.Owner);
        Assert.Equal(1, river.LavaAt(15)); // 3: one left on 3, one taken by the river, one still under it
        Assert.Null(Rules.LavaFlowSoFar(s, Em, 4, new[] { 28 })); // not next to it
    }

    // As on BGA (585709876, 557813258): a structure flowing out of a town leaves the part of the rest
    // holding the town's key a town, whatever its power; the other parts are in no town and may form another.
    [Fact]
    public void Ember_lava_leaves_only_the_key_s_part_of_a_town_in_it()
    {
        // 2-3-4-5-6 in a line, the key on 5: 4 flows out, leaving 2-3 and 5-6 (4 power each).
        var s = Play(Start(Em, Terrain.Desert, new[] { 4, 88 }));
        s = Put(s, Em, (2, PieceKind.TradingPost), (3, PieceKind.TradingPost), (5, PieceKind.TradingPost), (6, PieceKind.TradingPost));
        s = WithLava(s.WithPlayer(s[Em] with { TownHexes = ValueList.Of(2, 3, 4, 5, 6) }), 4, 2);
        s = s with { PlacedKeys = s.PlacedKeys.Add(new PlacedKey(s.KeyBank[0][0], Em, 5)) };
        var split = PlayFrom(s, new LetLavaFlow(Em, 4, ValueList.Of(16)));
        Assert.Equal(ValueList.Of(5, 6), split[Em].TownHexes); // the key's part, below a town, stays one
    }

    // As on BGA (557813258 #239): the free bridge across a river the lava crossed needs no structure of
    // theirs at either end.
    [Fact]
    public void Ember_lava_bridge_needs_no_structure_at_its_ends()
    {
        var s = Play(Start(Em, Terrain.Desert, new[] { 4, 88 }));
        s = WithLava(Put(s, Em, (16, PieceKind.Dwelling)), 16, 5);
        var flowed = PlayFrom(s, new LetLavaFlow(Em, 16, ValueList.Of(15, 27, 39), 16, 27)); // across 15, on to 39
        Assert.True(flowed.IsBridged(16, 27));
        Assert.Null(flowed.Map[16]);
        Assert.Null(flowed.Map[27]);
        Assert.Equal(Em, flowed.Map[39]!.Value.Owner);
    }

    [Fact]
    public void Ember_have_no_shipping()
    {
        var s = Rich(Play(Start(Em, Terrain.Desert, new[] { 4, 88 })), Em);
        s = s.WithPlayer(s[Em] with { Priests = s[Em].Priests.Add(s[Em].PriestSupply[0]), PriestSupply = s[Em].PriestSupply.RemoveAt(0) });
        Assert.Equal(0, s[Em].Shipping);
        Assert.False(Rules.Apply(s, new UpgradeShipping(Em)).Accepted);
    }

    // ---------- Geologists ----------

    [Fact]
    public void Geologists_build_next_to_their_rocks_without_transforming()
    {
        var s = Rich(Play(Start(Ge, Terrain.Forest, new[] { 9, 60 })), Ge);
        Assert.Equal(ValueList.Of(Terrain.Forest), s[Ge].RockList);
        Assert.StartsWith("The Geologists can't transform terrain", Refusal(s, Build(Ge, 2, 8))); // Plains: not next to Forest
        var built = PlayFrom(s, Build(Ge, 2, 10)); // Lake: next to Forest on the wheel
        Assert.Equal(Terrain.Lake, built.HexTerrain[10]);
        Assert.Contains(Terrain.Lake, built[Ge].RockList);
    }

    [Fact]
    public void Geologists_spades_move_rocks_and_seven_rocks_all_come_off()
    {
        var s = Charged(Play(Start(Ge, Terrain.Forest, new[] { 9, 60 })));
        var spade = PlayFrom(s, new TakePowerAction(Ge, 4));
        Assert.Equal(1, spade[Ge].RockMovesOwed);
        Assert.StartsWith("A rock goes onto", Refusal(spade, new MoveRock(Ge, Terrain.Desert)));
        Assert.Contains(Terrain.Mountain, PlayFrom(spade, new MoveRock(Ge, Terrain.Mountain))[Ge].RockList);

        var six = spade.WithPlayer(spade[Ge] with { Rocks = new ValueList<Terrain>(Terrains.Wheel.Where(t => t != Terrain.Mountain)) });
        Assert.Empty(PlayFrom(six, new MoveRock(Ge, Terrain.Mountain))[Ge].RockList);
    }

    // As on BGA (920031063 #129): a spade used on the rocks scores the round tile's spades.
    [Fact]
    public void Geologists_rock_spades_score_the_round_tile_s_spades()
    {
        var s = Charged(Play(Start(Ge, Terrain.Forest, new[] { 9, 60 })));
        var spadeTile = RoundTiles.All.ToList().FindIndex(tile => tile.VictoryPointsPerSpade > 0);
        s = s with { ChosenRoundTiles = s.ChosenRoundTiles.SetItem(s.Round - 1, spadeTile) };
        var spade = PlayFrom(s, new TakePowerAction(Ge, 4));
        var moved = PlayFrom(spade, new MoveRock(Ge, Terrain.Mountain));
        Assert.Equal(spade[Ge].VictoryPoints + RoundTiles.All[spadeTile].VictoryPointsPerSpade, moved[Ge].VictoryPoints);
    }

    // ---------- Changelings ----------

    [Fact]
    public void Changelings_choose_B_and_C_and_build_each_dwelling_on_its_own_terrain()
    {
        var setUp = new Move[] { new ChooseChangelingTerrains(Ch, Terrain.Swamp, Terrain.Desert) };
        var moves = Start(Ch, Terrain.Plains, new[] { 16, 17 }, beforeMap: setUp);
        var confirmed = Play(moves.TakeWhile(move => move is not ChooseChangelingTerrains).ToArray());
        Assert.Equal(Phase.ChoosingVolcanoTerrain, confirmed.Phase); // chosen alongside the volcano factions' terrain
        Assert.Equal(Ch, Rules.LateTerrainChooser(confirmed));
        Assert.Equal(Phase.ChoosingMap, PlayFrom(confirmed, setUp[0]).Phase); // Calculator: the map next

        var placing = Play(moves.TakeWhile(move => move is not PlaceStartingDwelling).Append(StartAt(Ch, 0, 16)).ToArray());
        Assert.Equal("The Changelings' starting dwellings must be placed on Swamp.", Refusal(placing, StartAt(Ch, 1, 107)));

        var s = Rich(Play(moves), Ch);
        var built = PlayFrom(s, Build(Ch, 2, 4)); // the third dwelling is C: Desert
        Assert.Equal(Terrain.Desert, built.HexTerrain[4]);
        Assert.Equal("The Changelings' next trading post is Plains: it can only replace a dwelling of that colour.",
            Refusal(s, UpgradeTo(Ch, PieceKind.TradingPost, 0, 17)));
        PlayFrom(s, UpgradeTo(Ch, PieceKind.TradingPost, 0, 16));
    }
}
