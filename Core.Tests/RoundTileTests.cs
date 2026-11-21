using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Solo Giants on 5 and 112 with pass tile 9 (3 power), and the round tiles given. Round 1 has begun:
// 15 coins, 6 workers, 20 VP. Hex 4 (Desert) is next to 5. The Giants always need 2 spades.
// Solo Witches on 9 and 60 with pass tile 3 (1 worker, 3 power): 15 coins, 7 workers, 20 VP. Next to 9:
// 10 Lake (1 spade to Forest), 21 Swamp (2), 8 Plains (3).
public class RoundTileTests
{
    private static Move[] SoloWith(params int[] roundTiles) => Concat(
        Confirmed(G),
        SetUpAndStart(new[] { G }, roundTiles),
        new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) },
        new Move[] { new PickPassTile(G, 9) });

    private static Move[] WitchesWith(params int[] roundTiles) => Concat(
        Confirmed(W),
        SetUpAndStart(new[] { W }, roundTiles),
        new[] { StartAt(W, 0, WitchesStartA), StartAt(W, 1, WitchesStartB) },
        new Move[] { new PickPassTile(W, 3) });

    private static GameState WithSteps(GameState s, Track track, int steps, Faction f = Faction.Giants) =>
        s.WithPlayer(s[f] with { TrackSteps = s[f].TrackSteps.SetItem((int)track, steps) });

    // ---------- During the round ----------

    [Fact]
    public void A_dwelling_tile_scores_each_dwelling_built()
    {
        var s = Rules.Apply(Play(SoloWith(1, 0, 2, 3, 4, 6)), Build(G, 2, 4)).State;
        Assert.Equal(22, s[G].VictoryPoints);

        var otherTile = Rules.Apply(Play(SoloWith(0, 1, 2, 3, 4, 6)), Build(G, 2, 4)).State;
        Assert.Equal(20, otherTile[G].VictoryPoints);
    }

    [Fact]
    public void A_spade_tile_scores_every_spade()
    {
        var oneSpade = Rules.Apply(Play(WitchesWith(4, 0, 1, 2, 3, 6)), new TransformTerrain(W, 10, Terrain.Forest)).State;
        Assert.Equal(22, oneSpade[W].VictoryPoints);

        var threeSpades = Rules.Apply(Play(WitchesWith(4, 0, 1, 2, 3, 6)), new TransformTerrain(W, 8, Terrain.Forest)).State; // Wasteland: 2 spades
        Assert.Equal(24, threeSpades[W].VictoryPoints);

        var builtThere = Rules.Apply(Play(WitchesWith(4, 0, 1, 2, 3, 6)), Build(W, 2, 10)).State; // Lake: 1 spade too
        Assert.Equal(22, builtThere[W].VictoryPoints);
    }

    [Fact]
    public void The_Giants_score_two_spades_for_every_transform()
    {
        var nextDoor = Rules.Apply(Play(SoloWith(4, 0, 1, 2, 3, 6)), new TransformTerrain(G, 4, Terrain.Wasteland)).State;
        Assert.Equal(24, nextDoor[G].VictoryPoints);

        var farAway = Rules.Apply(Play(SoloWith(4, 0, 1, 2, 3, 6)), new TransformTerrain(G, 17, Terrain.Wasteland)).State; // Swamp
        Assert.Equal(24, farAway[G].VictoryPoints);
    }

    [Fact]
    public void A_stronghold_tile_scores_the_stronghold()
    {
        // TradingPostTests.Setup uses TestRoundTiles: round 1 is tile 0 (5 VP per stronghold or sanctuary).
        var s = Rules.Apply(Play(BuildingTests.TradingPostBuilt), UpgradeTo(G, PieceKind.Stronghold, 0, 43)).State;
        Assert.Equal(25, s[G].VictoryPoints);
    }

    // ---------- End of the round ----------

    [Fact]
    public void Track_positions_are_rewarded_at_the_end_of_the_round()
    {
        // Tile 0: 1 worker for every 2 Fire. 5 Fire: 2 workers. Then round 2's income adds 3 more.
        var s = WithSteps(Play(SoloWith(0, 1, 2, 3, 4, 6)), Track.Fire, 5);
        var result = Rules.Apply(PlayFrom(s, PassKeeping(G)), End(G));

        Assert.Contains<GameEvent>(new RoundEndRewardReceived(G, new Income(0, 2, 0, 0), 0), result.Events);
        Assert.Equal(2, result.State.Round);
        Assert.Equal(11, result.State[G].Workers);
        Assert.Equal(new Income(0, 2, 0, 0), Rules.RoundEndIncome(s, G)); // foreseen during the round (the panel's income)
    }

    [Fact]
    public void Priests_on_the_tracks_earn_coins_on_tile_eight()
    {
        var s = Charged(Play(SoloWith(8, 0, 1, 2, 3, 4)));
        s = PlayFrom(s, GetPriest(G), Place(G, 0, Track.Fire, 0), End(G), PassKeeping(G));
        var result = Rules.Apply(s, End(G));

        Assert.Contains<GameEvent>(new RoundEndRewardReceived(G, new Income(2, 0, 0, 0), 0), result.Events);
    }

    [Fact]
    public void Round_end_spades_are_spent_before_the_next_round()
    {
        // Tile 3: 1 spade for every 4 Water. 8 Water: 2 spades.
        var s = WithSteps(Play(WitchesWith(3, 0, 1, 2, 4, 6)), Track.Water, 8, W);
        s = PlayFrom(s, PassKeeping(W), End(W));

        Assert.Equal(Phase.RoundEndSpades, s.Phase);
        Assert.Equal(W, s.ActivePlayer);
        Assert.Equal(2, s[W].SpadesOwed);
        Assert.Equal(1, s.Round);
        Assert.False(Rules.Apply(s, Build(W, 2, 10)).Accepted);

        s = PlayFrom(s, new SpendSpades(W, 10, Terrain.Forest));
        Assert.Equal(Terrain.Forest, s.HexTerrain[10]);
        Assert.Equal(1, s[W].SpadesOwed);
        Assert.Equal(Phase.RoundEndSpades, s.Phase);
        Assert.False(Rules.Apply(s, new BuildOnSpadedHex(new Piece(W, PieceKind.Dwelling, 2), 10)).Accepted); // no building

        s = PlayFrom(s, new FinishSpades(W)); // the last spade is given up
        Assert.Equal(Phase.Playing, s.Phase);
        Assert.Equal(2, s.Round);
        Assert.Equal(0, s[W].SpadesOwed);
    }

    [Fact]
    public void Round_end_spades_are_free_cannot_be_topped_up_and_need_a_hex_in_range()
    {
        var s = WithSteps(Play(WitchesWith(3, 0, 1, 2, 4, 6)), Track.Water, 4, W); // 1 spade
        s = PlayFrom(s, PassKeeping(W), End(W));
        var coins = s[W].Coins;
        var workers = s[W].Workers;

        Assert.StartsWith("That hex is out of range", Assert.Single(Rules.Apply(s, new SpendSpades(W, 0, Terrain.Desert)).Issues).Message);
        Assert.StartsWith("That needs 2 spades", Assert.Single(Rules.Apply(s, new SpendSpades(W, 8, Terrain.Forest)).Issues).Message);

        var result = Rules.Apply(s, new SpendSpades(W, 10, Terrain.Forest));
        Assert.Equal(2, result.State.Round);              // the only spade: round 2 begins
        Assert.Equal(coins, result.State[W].Coins);         // nothing paid, and no coin income
        Assert.Equal(workers + 4, result.State[W].Workers); // round 2's income only (1 base, 2 slots, 1 from tile 3)
    }

    [Fact]
    public void Round_end_spades_do_not_score_the_round_tile()
    {
        // Tile 3 gives a spade; round 1's tile is then swapped for tile 4 (2 VP per spade) to check
        // that "during the round" scoring is off between rounds. (No real tile does both.)
        var s = WithSteps(Play(WitchesWith(3, 0, 1, 2, 4, 6)), Track.Water, 4, W);
        s = PlayFrom(s, PassKeeping(W), End(W));
        s = s with { ChosenRoundTiles = s.ChosenRoundTiles.SetItem(0, 4) };

        Assert.Null(s.CurrentRoundTile);
        var result = Rules.Apply(s, new SpendSpades(W, 10, Terrain.Forest));
        Assert.Equal(20, result.State[W].VictoryPoints);
    }

    [Fact]
    public void The_Giants_lose_an_odd_round_end_spade_at_once()
    {
        var one = WithSteps(Play(SoloWith(3, 0, 1, 2, 4, 6)), Track.Water, 4); // 1 spade: useless to the Giants
        var result = Rules.Apply(PlayFrom(one, PassKeeping(G)), End(G));
        Assert.Equal(Phase.Playing, result.State.Phase);
        Assert.Equal(2, result.State.Round);
        Assert.Contains(result.Issues, issue => issue.Message == "The Giants need 2 spades for every transform, so 1 spade is lost.");
        Assert.Contains<GameEvent>(new SpadesForfeited(G, 1), result.Events);

        var three = WithSteps(Play(SoloWith(3, 0, 1, 2, 4, 6)), Track.Water, 9); // 2 spades (9 / 4): kept
        var kept = PlayFrom(three, PassKeeping(G), End(G));
        Assert.Equal(Phase.RoundEndSpades, kept.Phase);
        Assert.Equal(2, kept[G].SpadesOwed);
    }

    [Fact]
    public void The_last_round_gives_no_reward()
    {
        var round6 = WithSteps(Play(Concat(SoloWith(1, 2, 4, 6, 8, 0), PassAndEnd(G, G, G, G, G))), Track.Fire, 10);
        var result = Rules.Apply(PlayFrom(round6, new Pass(G)), End(G));

        Assert.Equal(Phase.Finished, result.State.Phase);
        Assert.DoesNotContain(result.Events, e => e is RoundEndRewardReceived);
    }
}
