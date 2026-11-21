using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class DestinationTests
{
    private static Piece Dwelling(Faction f, int number) => new(f, PieceKind.Dwelling, number);
    private static Piece Priest(Faction f, int number) => new(f, PieceKind.Priest, number);

    [Fact]
    public void A_dwelling_is_offered_exactly_the_empty_land_hexes_in_range()
    {
        var s = Play(SoloStart);
        var options = Rules.LegalDestinations(s, Dwelling(G, 2));

        Assert.True(options.CanMove);
        Assert.Contains<Destination>(new HexDestination(4), options.Destinations);
        Assert.DoesNotContain<Destination>(new HexDestination(0), options.Destinations);  // out of range
        Assert.DoesNotContain<Destination>(new HexDestination(18), options.Destinations); // river
        Assert.DoesNotContain<Destination>(new HexDestination(GiantsStartA), options.Destinations); // occupied

        var expected = Enumerable.Range(0, s.Layout.HexCount)
            .Count(hex => s.Map[hex] is null && s.HexTerrain[hex] != Terrain.River && Rules.IsInRange(s, G, hex));
        Assert.Equal(expected, options.Destinations.Count);
    }

    [Fact]
    public void Starting_dwellings_are_offered_every_empty_starting_terrain_hex()
    {
        var s = Play(Placing(W));
        var options = Rules.LegalDestinations(s, Dwelling(W, 0));

        var forests = Enumerable.Range(0, s.Layout.HexCount).Count(hex => s.HexTerrain[hex] == Terrain.Forest);
        Assert.Equal(forests, options.Destinations.Count);
    }

    [Fact]
    public void Being_in_debt_does_not_remove_options()
    {
        var s = Play(SoloStart);
        var normal = Rules.LegalDestinations(s, Dwelling(G, 2)).Destinations.Count;

        var inDebt = s.WithPlayer(s[G] with { Coins = -2 });
        Assert.Equal(normal, Rules.LegalDestinations(inDebt, Dwelling(G, 2)).Destinations.Count);
    }

    [Fact]
    public void A_piece_that_cannot_move_comes_with_the_reason()
    {
        var s = Play(TwoPlayerStart);

        var outOfTurn = Rules.LegalDestinations(s, Dwelling(G, 2));
        Assert.False(outOfTurn.CanMove);
        Assert.Equal("It is not the Giants' turn.", outOfTurn.Reason);

        var notLeftmost = Rules.LegalDestinations(s, Dwelling(W, 3));
        Assert.Equal("Only the leftmost dwelling can be built.", notLeftmost.Reason);
    }

    [Fact]
    public void Before_the_game_starts_nothing_can_move()
    {
        var s = Play(ChooseTM, new AddPlayer(G));
        Assert.Equal("The game has not started yet.", Rules.LegalDestinations(s, Dwelling(G, 0)).Reason);
    }

    [Fact]
    public void Top_priest_can_go_to_every_empty_slot()
    {
        var s = PlayCharged(TwoPlayerStart, GetPriest(W), Place(W, 0, Track.Earth, 2), End(W), GetPriest(G));
        var options = Rules.LegalDestinations(s, Priest(G, 0));

        Assert.Equal(Config.TrackCount * Config.PriestSlotsPerZone - 1, options.Destinations.Count);
        Assert.DoesNotContain<Destination>(new PriestSlotDestination(Track.Earth, 2), options.Destinations);
    }

    [Fact]
    public void Lower_priest_in_the_stack_cannot_move()
    {
        var s = PlayCharged(SoloStart, GetPriest(G), GetPriest(G));
        var options = Rules.LegalDestinations(s, Priest(G, 0));

        Assert.False(options.CanMove);
        Assert.Equal("Only the top priest can be placed.", options.Reason);
    }

    [Fact]
    public void Every_offered_destination_is_accepted_by_the_rules()
    {
        var s = PlayCharged(TwoPlayerStart, GetPriest(W));
        foreach (var piece in new[] { Priest(W, 0), Dwelling(W, 2) })
            foreach (var destination in Rules.LegalDestinations(s, piece).Destinations)
                Assert.True(Rules.Apply(s, Rules.MoveTo(s, piece, destination)).Accepted);
    }

    [Fact]
    public void MoveTo_builds_the_matching_move_for_the_phase()
    {
        var playing = Play(SoloStart);
        Assert.Equal(new BuildDwelling(Dwelling(G, 2), 7), Rules.MoveTo(playing, Dwelling(G, 2), new HexDestination(7)));
        Assert.Equal(new PlacePriest(Priest(G, 0), Track.Air, 1),
                     Rules.MoveTo(playing, Priest(G, 0), new PriestSlotDestination(Track.Air, 1)));

        var placing = Play(Placing(G));
        Assert.Equal(new PlaceStartingDwelling(Dwelling(G, 0), 5), Rules.MoveTo(placing, Dwelling(G, 0), new HexDestination(5)));
    }
}
