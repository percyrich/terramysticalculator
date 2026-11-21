using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Mostly solo Giants (dwellings on 5 and 112, 15 coins, 6 workers, round 1). Hex 4 (Desert) is next to hex 5.
// The Giants need 2 spades (6 workers) for any transform.
public class DwellingTests
{
    [Fact]
    public void Building_on_other_terrain_transforms_it_and_costs_both()
    {
        // The Witches (7 workers) build on 10, a Lake next to their 9: 1 spade to Forest.
        var result = Rules.Apply(Play(TwoPlayerStart), Build(W, 2, 10));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal(Terrain.Forest, s.HexTerrain[10]);
        Assert.Equal((Piece?)new Piece(W, PieceKind.Dwelling, 2), s.Map[10]);
        Assert.Null(s[W].DwellingBank[2]);
        Assert.Equal(13, s[W].Coins);  // 15 - dwelling 2
        Assert.Equal(3, s[W].Workers); // 7 - 1 spade (3 workers) - dwelling 1
        Assert.Contains<GameEvent>(new TerrainTransformed(10, Terrain.Lake, Terrain.Forest), result.Events);
    }

    [Fact]
    public void Building_does_not_end_the_turn()
    {
        var s = Play(Concat(TwoPlayerStart, new[] { Build(W, 2, 8) })); // 8 is Wasteland, next to 9
        Assert.Equal(W, s.ActivePlayer);
        Assert.True(s.ActionTaken);
    }

    [Fact]
    public void Building_on_home_terrain_costs_only_the_dwelling()
    {
        // Round 1: transform hex 4 to Wasteland (2 spades: no workers left), give up building, end turn. Then build there.
        var s = Play(Concat(SoloStart, new Move[] { new TransformTerrain(G, 4, Terrain.Wasteland), new FinishSpades(G), End(G) }));
        var result = Rules.Apply(s, Build(G, 2, 4));

        Assert.True(result.Accepted);
        Assert.Equal(13, result.State[G].Coins);   // 15 - dwelling 2
        Assert.Equal(-1, result.State[G].Workers); // 0 - dwelling 1
        Assert.DoesNotContain(result.Events, e => e is TerrainTransformed);
    }

    [Fact]
    public void Only_one_action_per_turn()
    {
        var s = Ruled(Play(Concat(SoloStart, new[] { Build(G, 2, 4) })));
        var result = Rules.Apply(s, Build(G, 3, 6));

        Assert.False(result.Accepted);
        Assert.Contains("already taken their action", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Out_of_range_hexes_are_refused()
    {
        var result = Rules.Apply(Play(SoloStart), Build(G, 2, 0));
        Assert.False(result.Accepted);
        Assert.StartsWith("That hex is out of range", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Rivers_cannot_be_built_on()
    {
        Assert.False(Rules.Apply(Play(SoloStart), Build(G, 2, 18)).Accepted);
    }

    [Fact]
    public void Occupied_hexes_are_refused()
    {
        var result = Rules.Apply(Play(SoloStart), Build(G, 2, GiantsStartA));
        Assert.Equal("That hex is already occupied.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Only_the_leftmost_dwelling_can_be_built()
    {
        Assert.False(Rules.Apply(Play(SoloStart), Build(G, 3, 4)).Accepted);
    }

    [Fact]
    public void A_built_dwelling_cannot_be_built_again()
    {
        Assert.False(Rules.Apply(Play(SoloStart), Build(G, 0, 4)).Accepted);
    }

    [Fact]
    public void Building_out_of_turn_is_refused()
    {
        var start = Play(TwoPlayerStart);
        var result = Rules.Apply(start, Build(G, 2, 4));
        Assert.False(result.Accepted);
        Assert.Same(start, result.State);
    }

    [Fact]
    public void Hexes_off_the_map_are_refused()
    {
        var s = Play(SoloStart);
        Assert.False(Rules.Apply(s, Build(G, 2, -1)).Accepted);
        Assert.False(Rules.Apply(s, Build(G, 2, s.Layout.HexCount)).Accepted);
    }

    [Fact]
    public void Building_without_enough_coins_is_allowed_with_a_debt_warning()
    {
        // 1 coin; a dwelling costs 2.
        var s = Play(SoloStart);
        var result = Rules.Apply(s.WithPlayer(s[G] with { Coins = 1 }), Build(G, 2, 4));

        Assert.True(result.Accepted);
        var warning = Assert.Single(result.Issues);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal("The Giants are in debt: -1 coins, -1 workers.", warning.Message); // 6 - 2 spades - 1
    }

    [Fact]
    public void A_priest_cannot_be_built_as_a_dwelling()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        Assert.False(Rules.Apply(s, new BuildDwelling(new Piece(G, PieceKind.Priest, 0), 4)).Accepted);
    }
}
