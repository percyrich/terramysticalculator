using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// The Special Landscapes expansion (Landscapes, Rules.Landscapes.cs). Most tests start one faction alone (Solo, Calculator,
// Free play) on the base map, with landscapes and buildings put straight onto the map where the test isn't about
// placing them. Fire & Ice factions' landscapes: FireAndIceTests.
// Hexes (base map): Auren or Witches on Forest 9 and 60; next to 9: 8 Wasteland, 10 Lake, 21 Swamp; 11 Mountain touches
// only 10 of those; 33 Forest touches 21. Mermaids on Lake 3 and 89; river 15 touches 3, river 77 touches 89.
public class LandscapeTests
{
    private const Faction Au = Faction.Auren, Al = Faction.Alchemists, Dk = Faction.Darklings, Cu = Faction.Cultists,
        H = Faction.Halflings, Dw = Faction.Dwarves, E = Faction.Engineers, Sw = Faction.Swarmlings, CM = Faction.ChaosMagicians,
        N = Faction.Nomads, RW = Faction.Riverwalkers;

    private static int _nextNumber = 50; // piece numbers for buildings put down by the tests (only IDs)

    // Buildings put straight onto the map (replacing whatever is there).
    private static GameState Put(GameState s, Faction f, params (int Hex, PieceKind Kind)[] buildings)
    {
        foreach (var (hex, kind) in buildings)
            s = s with { Map = s.Map.SetItem(hex, new Piece(f, kind, _nextNumber++)) };
        return s;
    }

    // The faction's landscape put straight onto the hex.
    private static GameState Landscape(GameState s, Faction f, int hex) =>
        s with { Map = s.Map.SetItem(hex, new Piece(f, PieceKind.Landscape, 0)) };

    private static GameState Rich(GameState s, Faction f) => s.WithPlayer(s[f] with { Coins = 30, Workers = 30 });

    private static string Refusal(GameState s, Move move) => Assert.Single(Rules.Apply(s, move).Issues).Message;

    // ---------- Placing ----------

    [Fact]
    public void A_landscape_is_laid_instead_of_a_terrain_tile_as_the_action()
    {
        var s = Rich(Play(Solo(Au, 9, 60)), Au);
        var placed = PlayFrom(s, new PlaceLandscape(Au, 10));
        Assert.Equal(new Piece(Au, PieceKind.Landscape, 0), placed.Map[10]);
        Assert.Equal(Terrain.Forest, placed.HexTerrain[10]);
        Assert.Equal(s[Au].Workers - s[Au].SpadeCost.Workers, placed[Au].Workers); // Lake to Forest: 1 spade
        Assert.True(placed.ActionTaken);
        Assert.Equal(10, placed.LandscapeHex(Au));
        Assert.Contains(10, placed.HexesBuiltOnBy(Au)); // a structure

        Assert.Equal("The Auren have already placed their Tree of Serenity.", Refusal(placed, new PlaceLandscape(Au, 21)));
        Assert.Equal("The special landscapes are not in this game.",
            Refusal(s with { ChosenOptions = s.Options with { Landscapes = false } }, new PlaceLandscape(Au, 10)));
    }

    [Fact]
    public void A_hex_already_of_the_home_terrain_cannot_take_a_landscape()
    {
        var s = Put(Rich(Play(Solo(Au, 9, 60)), Au), Au, (21, PieceKind.Dwelling));
        Assert.Equal("The Tree of Serenity is laid instead of a terrain tile: only a hex being transformed into Forest can take it.",
            Refusal(s, new PlaceLandscape(Au, 33)));
    }

    [Fact]
    public void A_hex_just_transformed_takes_the_landscape_instead_of_a_dwelling()
    {
        var s = PlayFrom(Rich(Play(Solo(Au, 9, 60)), Au), new TransformTerrain(Au, 10, Terrain.Forest));
        Assert.Contains(10, Rules.SpadedLandscapeTargets(s, Au));
        var placed = PlayFrom(s, new PlaceLandscape(Au, 10));
        Assert.Equal(PieceKind.Landscape, placed.Map[10]!.Value.Kind);
        Assert.Equal(s[Au].Workers, placed[Au].Workers); // the transform was already paid
        Assert.False(placed.SpadesPending);
    }

    [Fact]
    public void Free_spades_can_lay_it_but_the_rest_of_the_batch_cannot_reach_through_it()
    {
        var s = PlayFrom(Charged(Play(Solo(Au, 9, 60))), new TakePowerAction(Au, 5)); // 2 spades
        s = PlayFrom(s, new PlaceLandscape(Au, 10));                                   // Lake to Forest: 1 spade
        Assert.Equal(1, s[Au].SpadesOwed);
        Assert.Equal("That hex is in range only through the Tree of Serenity just placed with these spades.",
            Refusal(s, new SpendSpades(Au, 11, Terrain.Forest)));
        Assert.True(Rules.Apply(s, new SpendSpades(Au, 21, Terrain.Forest)).Accepted); // next to their 9
    }

    [Fact]
    public void The_prerequisite_must_stand_on_the_map_as_that_very_kind()
    {
        var s = Rich(Play(Solo(W, 9, 60)), W);
        Assert.Equal("The Witches need 2 temples on the map before placing their Raven's Blunder.", Refusal(s, new PlaceLandscape(W, 10)));
        Assert.Equal("The Witches need 2 temples on the map before placing their Raven's Blunder.",
            Refusal(Put(s, W, (9, PieceKind.Temple), (60, PieceKind.Sanctuary)), new PlaceLandscape(W, 10)));
        Assert.True(Rules.Apply(Put(s, W, (9, PieceKind.Temple), (60, PieceKind.Temple)), new PlaceLandscape(W, 10)).Accepted);
    }

    [Fact]
    public void The_log_names_the_landscape()
    {
        var s = Rich(Play(Solo(Au, 9, 60)), Au);
        var result = Rules.Apply(s, new PlaceLandscape(Au, 10));
        Assert.StartsWith("Auren place their Tree of Serenity at A11, turning Lake into Forest (",
            GameLog.Describe(s, new PlaceLandscape(Au, 10), result.Events, result.State)); // then what it cost
    }

    // ---------- Rivers and keys ----------

    [Fact]
    public void The_Coral_Reef_goes_on_a_river_next_to_the_Mermaids_for_a_spade()
    {
        var s = Rich(Play(Solo(M, 3, 89)), M);
        var placed = PlayFrom(s, new PlaceLandscape(M, 15));
        Assert.Equal(new Piece(M, PieceKind.Landscape, 0), placed.Map[15]);
        Assert.Equal(Terrain.River, placed.HexTerrain[15]);
        Assert.Equal(s[M].Workers - s[M].SpadeCost.Workers, placed[M].Workers);
        Assert.Equal("The Coral Reef must go next to one of the Mermaids' structures.", Refusal(s, new PlaceLandscape(M, 22)));
        Assert.Equal("The Mermaids' Coral Reef goes on a river hex.", Refusal(s, new PlaceLandscape(M, 4)));
    }

    [Fact]
    public void The_Mermaids_never_place_a_key_on_another_factions_landscape()
    {
        var s = Landscape(Play(Solo(M, 3, 89)), RW, 77);
        Assert.Equal("A key can't go on another faction's building or landscape.", Refusal(s, new FormTown(M, 0, 77)));
        Assert.DoesNotContain(Rules.RiverTowns(s, M), town => town.RiverHex == 77);
    }

    // ---------- Abilities ----------

    [Fact]
    public void The_Auren_score_4_VP_reaching_the_top_of_a_track_with_their_Tree()
    {
        var s = Play(Solo(Au, 9, 60));
        s = s.WithPlayer(s[Au] with { TrackSteps = s[Au].TrackSteps.SetItem((int)Track.Fire, 9), Keys = 1, CultStepsOwed = 1 });
        var without = PlayFrom(s, new TakeCultStep(Au, Track.Fire));
        var landscaped = PlayFrom(Landscape(s, Au, 10), new TakeCultStep(Au, Track.Fire));
        Assert.Equal(10, landscaped[Au].TrackSteps[(int)Track.Fire]);
        Assert.Equal(without[Au].VictoryPoints + Landscapes.AurenVictoryPoints, landscaped[Au].VictoryPoints);
    }

    [Fact]
    public void The_Witches_choose_3_steps_or_5_VP_for_a_town()
    {
        var s = Put(Play(Solo(W, 9, 60)), W, (8, PieceKind.TradingPost), (9, PieceKind.TradingPost), (10, PieceKind.TradingPost),
            (21, PieceKind.Dwelling));
        var plain = PlayFrom(s, new FormTown(W, 0, 9));
        Assert.Null(plain[W].WitchesTownBonusHex);

        var formed = PlayFrom(Landscape(s, W, 60), new FormTown(W, 0, 9));
        Assert.Equal(9, formed[W].WitchesTownBonusHex);
        Assert.Equal(plain[W].VictoryPoints - Config.WitchesTownVictoryPoints, formed[W].VictoryPoints); // not yet
        Assert.True(Rules.IsMidAction(formed, W));

        var points = PlayFrom(formed, new ChooseTownBonus(W, null));
        var steps = PlayFrom(formed, new ChooseTownBonus(W, Track.Fire));
        Assert.Equal(plain[W].VictoryPoints, points[W].VictoryPoints);
        Assert.Equal(formed[W].VictoryPoints, steps[W].VictoryPoints);
        Assert.Equal(formed[W].TrackSteps[(int)Track.Fire] + Landscapes.WitchesTownSteps, steps[W].TrackSteps[(int)Track.Fire]);
        Assert.Null(steps[W].WitchesTownBonusHex);
        Assert.Equal("The Witches have no town bonus to choose.", Refusal(steps, new ChooseTownBonus(W, null)));
    }

    [Fact]
    public void The_Darklings_Camp_counts_as_a_dwelling_when_passing()
    {
        var tile = Enumerable.Range(0, PassTiles.Count).First(id => PassTiles.All[id].PassScoredBuildings.Contains(PieceKind.Dwelling));
        var s = Play(Solo(Dk, 7, 17));
        var camp = Landscape(s, Dk, 4);
        Assert.Equal(PassTiles.PassVictoryPoints(s, Dk, tile) + PassTiles.All[tile].PassVictoryPointsPerBuilding,
            PassTiles.PassVictoryPoints(camp, Dk, tile));
        Assert.Equal(PassTiles.PassVictoryPoints(s, Dk, tile), PassTiles.PassVictoryPoints(Landscape(s, Au, 4), Dk, tile));
    }

    [Fact]
    public void The_Giants_lower_their_spade_rate_for_free_with_the_Cave_of_Wisdom()
    {
        var s = Put(Play(SoloStart), G, (112, PieceKind.Temple));
        var placed = PlayFrom(s, new PlaceLandscape(G, 4));
        Assert.Equal(s[G].SpadeUpgrades + 1, placed[G].SpadeUpgrades);
        Assert.Equal(s[G].VictoryPoints + Config.SpadeUpgradeVictoryPoints, placed[G].VictoryPoints);
    }

    [Fact]
    public void The_Halflings_need_a_spade_fewer_next_to_their_Driller()
    {
        var s = Landscape(Play(Solo(H, 6, 0)), H, 21);
        Assert.Equal(2, Rules.SpadesFor(s, H, 33, Terrain.Plains)); // Forest: 3, one fewer
        Assert.Equal(1, Rules.SpadesFor(s, H, 20, Terrain.Plains)); // Desert: 1, never fewer
        Assert.Equal(3, Rules.SpadesFor(s, H, 2, Terrain.Plains));  // Forest away from the Driller
    }

    [Fact]
    public void The_Alchemists_may_upgrade_without_a_priest_once_the_Workshop_is_placed()
    {
        var s = Rich(Put(Play(Solo(Al, 7, 17)), Al, (17, PieceKind.Stronghold)), Al);
        s = PlayFrom(s, new PlaceLandscape(Al, 6)); // Plains to Swamp
        Assert.Equal(LandscapeOffer.Shipping | LandscapeOffer.Spades, s[Al].LandscapeOffers);
        Assert.True(Rules.IsMidAction(s, Al));

        var shipped = PlayFrom(s, new UpgradeShipping(Al));
        Assert.Equal(s[Al].Shipping + 1, shipped[Al].Shipping);
        Assert.Equal(0, shipped[Al].PriestDebt);
        Assert.Equal(s[Al].Coins - Factions.ShippingUpgradeCost(Al).Coins, shipped[Al].Coins);
        Assert.Equal(LandscapeOffer.Spades, shipped[Al].LandscapeOffers);

        var declined = PlayFrom(shipped, new DeclineLandscapeBonus(Al));
        Assert.Equal(LandscapeOffer.None, declined[Al].LandscapeOffers);
        Assert.False(Rules.IsMidAction(declined, Al));
    }

    [Fact]
    public void The_Chaos_Portal_goes_on_any_free_Wasteland_as_the_stronghold_is_built()
    {
        var s = Rich(Play(Solo(CM, 5)), CM);
        Assert.Equal("The Chaos Magicians place their Chaos Portal only as their stronghold is built.", Refusal(s, new PlaceLandscape(CM, 4)));
        s = s.WithPlayer(s[CM] with { TradingPostBank = s[CM].TradingPostBank.SetItem(0, null) }) with
        {
            Map = s.Map.SetItem(5, new Piece(CM, PieceKind.TradingPost, 0)),
        };
        s = PlayFrom(s, UpgradeTo(CM, PieceKind.Stronghold, 0, 5));
        Assert.Equal(LandscapeOffer.Portal, s[CM].LandscapeOffers);
        Assert.Equal("The Chaos Portal goes on a Wasteland hex.", Refusal(s, new PlaceLandscape(CM, 4)));
        var placed = PlayFrom(s, new PlaceLandscape(CM, 112)); // across the map
        Assert.Equal(112, placed.LandscapeHex(CM));
        Assert.Equal(LandscapeOffer.None, placed[CM].LandscapeOffers);
    }

    [Fact]
    public void The_Nomads_may_take_a_coin_for_a_build_next_to_their_Trading_Camp()
    {
        var s = Landscape(Play(Solo(N, 4, 13, 20)), N, 17);
        var offered = s with { PowerOffers = ValueList.Of(new PowerOffer(N, 1, 16, N)) }; // 16 touches 17
        var coin = PlayFrom(offered, new TakeCoinInstead(N));
        Assert.Equal(s[N].Coins + 1, coin[N].Coins);
        Assert.Equal(s[N].VictoryPoints, coin[N].VictoryPoints);
        Assert.Empty(coin.PowerOffers);
        Assert.Equal("Only a build next to the Nomads' Trading Camp can be answered with a coin.",
            Refusal(s with { PowerOffers = ValueList.Of(new PowerOffer(N, 1, 112, N)) }, new TakeCoinInstead(N)));
    }

    [Fact]
    public void The_Dwarves_tunnel_two_hexes_from_their_Great_Tunnel()
    {
        var s = Play(Solo(Dw, 1, 108));
        Assert.Equal(Reach.None, Rules.ReachOf(Put(s, Dw, (35, PieceKind.Dwelling)), Dw, 12, allowSkip: true));
        Assert.Equal(Reach.Tunnel, Rules.ReachOf(Landscape(s, Dw, 35), Dw, 12, allowSkip: true)); // 3 hexes from 35
    }

    [Fact]
    public void The_Engineers_build_land_bridges_from_their_Bridge_Tower()
    {
        var s = Play(Solo(E, 1, 108));
        var tower = Landscape(s, E, 35);
        Assert.Equal(6, Rules.LandBridgeEdges(tower, E).Count());
        Assert.Contains((35, 60), Rules.BridgeSpots(tower, E)); // across Wasteland 48 and river 47: no river bridge
        Assert.True(Rules.Apply(tower.WithPlayer(tower[E] with { BridgesOwed = 1 }), new PlaceBridge(E, 35, 60)).Accepted);

        var dwelling = Put(s, E, (35, PieceKind.Dwelling));
        Assert.Equal("A bridge must join two land hexes across the edge between two river hexes.",
            Refusal(dwelling.WithPlayer(dwelling[E] with { BridgesOwed = 1 }), new PlaceBridge(E, 35, 60)));
    }

    [Fact]
    public void The_Swarmlings_town_holding_the_Breeding_Ground_takes_more_keys_as_it_grows()
    {
        var s = Play(Solo(Sw, 3, 10));
        // Row A, 0 to 7: four temples, two trading posts, the Breeding Ground and a dwelling: 14 power, one key on 0.
        s = Put(s, Sw, (0, PieceKind.Temple), (1, PieceKind.Temple), (2, PieceKind.Temple), (3, PieceKind.Temple),
            (4, PieceKind.TradingPost), (5, PieceKind.TradingPost), (7, PieceKind.Dwelling));
        s = Landscape(s, Sw, 6);
        var key = s.KeyBank[0][^1];
        s = s.WithPlayer(s[Sw] with { TownHexes = ValueList.Of(0, 1, 2, 3, 4, 5, 6, 7) }) with
        {
            PlacedKeys = ValueList.Of(new PlacedKey(key, Sw, 0)),
            KeyBank = s.KeyBank.SetItem(0, s.KeyBank[0].RemoveAt(s.KeyBank[0].Count - 1)),
        };
        Assert.Equal(ValueList.Of(0, 1, 2, 3, 4, 5, 6, 7), Assert.Single(Rules.FormableTowns(s, Sw)));
        Assert.Equal("A key already lies on that hex.", Refusal(s, new FormTown(Sw, 1, 0)));
        var city = PlayFrom(s, new FormTown(Sw, 1, 1));
        Assert.Equal(2, city.PlacedKeys.Count);
        Assert.Empty(Rules.FormableTowns(city, Sw)); // a metropolis needs 21

        var noGround = Put(s, Sw, (6, PieceKind.Dwelling)); // the same power without the Breeding Ground
        Assert.Empty(Rules.FormableTowns(noGround, Sw));
    }

    [Fact]
    public void The_Cultists_Palace_breaks_ties_on_the_tracks_at_the_end()
    {
        var s = Play(StartWith(new[] { Cu, W }, 6, 9, 60, 0));
        s = s.WithPlayer(s[Cu] with { TrackSteps = ValueList.Of(5, 0, 0, 0) });
        s = s.WithPlayer(s[W] with { TrackSteps = ValueList.Of(5, 0, 0, 0) });
        int Fire(GameState state, Faction f) => EndGame.Preview(state).Single(p => p.Faction == f).TrackPoints[(int)Track.Fire];
        Assert.Equal(6, Fire(s, Cu));
        Assert.Equal(6, Fire(s, W));
        var palace = Landscape(s, Cu, 8);
        Assert.Equal(8, Fire(palace, Cu));
        Assert.Equal(4, Fire(palace, W));
    }
}
