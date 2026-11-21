using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Fire & Ice's final scoring tiles (FinalScoring), on the base map with the solo Giants (shipping 0: only touching
// buildings are connected). Base map rows: 0–12, 13–24 (shifted), 25–37, 38–49 (shifted), 50–62, ...
// Row 4 starts S P W L (50–53); 27 and 29 are land with river 28 between them; 0 and 1 are on the top edge.
public class FinalScoringTests
{
    private static int _number = 60; // piece numbers for buildings put down by the tests (only IDs)

    // The solo Giants with nothing on the map but these buildings.
    private static GameState With(params (int Hex, PieceKind Kind)[] buildings)
    {
        var s = Play(SoloStart);
        s = s with { Map = ValueList<Piece?>.Repeat(null, s.Map.Count) };
        foreach (var (hex, kind) in buildings)
            s = s with { Map = s.Map.SetItem(hex, new Piece(G, kind, _number++)) };
        return s;
    }

    private static GameState Shipping(GameState s, int level) => s.WithPlayer(s[G] with { Shipping = level });

    [Theory]
    [InlineData(0, 12, 12)] // along the top row
    [InlineData(0, 50, 4)]  // four rows straight down
    [InlineData(0, 13, 1)]  // touching
    [InlineData(50, 53, 3)]
    public void Distances_are_as_the_crow_flies(int a, int b, int expected)
    {
        Assert.Equal(expected, FinalScoring.Distance(Maps.Base.Layout, a, b));
    }

    [Fact]
    public void Only_hexes_touching_the_outside_are_on_the_edge()
    {
        var edge = FinalScoring.OuterEdge(Maps.Base.Layout);
        Assert.Contains(0, edge);
        Assert.Contains(50, edge);
        Assert.DoesNotContain(51, edge);
    }

    [Fact]
    public void Stronghold_to_sanctuary_counts_only_when_both_are_built_and_connected()
    {
        var joined = With((50, PieceKind.Stronghold), (51, PieceKind.Dwelling), (52, PieceKind.Sanctuary));
        Assert.Equal(2, FinalScoring.Value(joined, G, FinalScoringTile.StrongholdSanctuary));

        var apart = With((50, PieceKind.Stronghold), (52, PieceKind.Sanctuary));
        Assert.Equal(0, FinalScoring.Value(apart, G, FinalScoringTile.StrongholdSanctuary));

        var noSanctuary = With((50, PieceKind.Stronghold), (51, PieceKind.Dwelling));
        Assert.Equal(0, FinalScoring.Value(noSanctuary, G, FinalScoringTile.StrongholdSanctuary));
    }

    [Fact]
    public void Distance_is_the_longest_between_two_connected_buildings()
    {
        var s = With((50, PieceKind.Dwelling), (51, PieceKind.Dwelling), (52, PieceKind.Dwelling), (53, PieceKind.Dwelling),
            (12, PieceKind.Dwelling)); // 12 is far away but on its own
        Assert.Equal(3, FinalScoring.Value(s, G, FinalScoringTile.Distance));
        Assert.Equal(0, FinalScoring.Value(With((12, PieceKind.Dwelling)), G, FinalScoringTile.Distance));
    }

    [Fact]
    public void Clusters_count_neighbourhoods_joined_by_shipping()
    {
        var s = With((27, PieceKind.Dwelling), (29, PieceKind.Dwelling));
        Assert.Equal(1, FinalScoring.Value(s, G, FinalScoringTile.Clusters));          // not connected: one each
        Assert.Equal(2, FinalScoring.Value(Shipping(s, 1), G, FinalScoringTile.Clusters)); // across river 28
    }

    [Fact]
    public void A_key_on_a_river_hex_joins_the_clusters_on_either_side()
    {
        var s = Shipping(With((27, PieceKind.Dwelling), (29, PieceKind.Dwelling)), 1); // two clusters across river 28
        var keyed = s with { PlacedKeys = s.PlacedKeys.Add(new PlacedKey(s.KeyBank[0][0], G, 28)) }; // a river town's key
        Assert.Equal(1, FinalScoring.Value(keyed, G, FinalScoringTile.Clusters));
    }

    [Fact]
    public void Edges_count_the_connected_buildings_on_the_edge()
    {
        var s = With((0, PieceKind.Dwelling), (1, PieceKind.Dwelling), (51, PieceKind.Dwelling));
        Assert.Equal(2, FinalScoring.Value(s, G, FinalScoringTile.Edges));
        Assert.Equal(0, FinalScoring.Value(With((51, PieceKind.Dwelling)), G, FinalScoringTile.Edges));
    }

    [Fact]
    public void A_final_scoring_tile_needs_fire_and_ice()
    {
        var refused = Rules.Apply(GameState.Initial,
            new ChooseGame(GameKind.TerraMystica, new GameOptions(FireAndIce: false, FinalScoring: FinalScoringTile.Edges)));
        Assert.False(refused.Accepted);
        Assert.True(Rules.Apply(GameState.Initial,
            new ChooseGame(GameKind.TerraMystica, new GameOptions(FinalScoring: FinalScoringTile.Edges))).Accepted);
    }

    [Fact]
    public void The_chosen_tile_scores_like_the_network_and_a_value_of_zero_scores_nothing()
    {
        var s = Play(TwoPlayerStart); // each faction's two starting dwellings are apart
        s = s with
        {
            Map = s.Map.SetItem(4, new Piece(G, PieceKind.Dwelling, 2)), // Giants: 4 and 5, distance 1
            ChosenOptions = new GameOptions(FinalScoring: FinalScoringTile.Distance),
        };
        var finished = EndGame.Finish(s, new List<GameEvent>());
        FinalScore giants = finished.FinalScores.First(score => score.Faction == G);
        FinalScore witches = finished.FinalScores.First(score => score.Faction == W);

        Assert.Equal(FinalScoringTile.Distance, giants.Tile);
        Assert.Equal(1, giants.TileValue);
        Assert.Equal(18, giants.TilePoints);
        Assert.Equal(0, witches.TileValue);
        Assert.Equal(0, witches.TilePoints); // second, but a value of 0 scores nothing
    }

    [Fact]
    public void The_preview_gives_what_final_scoring_would_give_now()
    {
        var s = Play(TwoPlayerStart);
        s = s with
        {
            Map = s.Map.SetItem(4, new Piece(G, PieceKind.Dwelling, 2)), // Giants: a network of 2, the Witches 1
            ChosenOptions = new GameOptions(FinalScoring: FinalScoringTile.Distance),
        };
        var previews = EndGame.Preview(s);
        ScorePreview giants = previews.First(preview => preview.Faction == G);
        ScorePreview witches = previews.First(preview => preview.Faction == W);
        Assert.Equal((2, 18, 1, 18), (giants.Network, giants.NetworkPoints, giants.TileValue, giants.TilePoints));
        Assert.Equal((1, 12, 0, 0), (witches.Network, witches.NetworkPoints, witches.TileValue, witches.TilePoints));

        var finished = EndGame.Finish(s, new List<GameEvent>());
        foreach (FinalScore score in finished.FinalScores)
        {
            ScorePreview preview = previews.First(p => p.Faction == score.Faction);
            Assert.Equal(score.TrackPoints.Sum(), preview.CultPoints);
            Assert.Equal((score.Network, score.NetworkPoints), (preview.Network, preview.NetworkPoints));
            Assert.Equal((score.TileValue, score.TilePoints), (preview.TileValue, preview.TilePoints));
        }
    }
}
