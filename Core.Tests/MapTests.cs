using Core;

namespace Core.Tests;

public class MapTests
{
    private static readonly MapLayout Base = Maps.Base.Layout;

    [Fact]
    public void Base_map_has_nine_alternating_rows()
    {
        Assert.Equal(ValueList.Of(13, 12, 13, 12, 13, 12, 13, 12, 13), Base.RowLengths);
        Assert.Equal(113, Base.HexCount);
        Assert.Equal(113, Maps.Base.HexTerrain.Count);
    }

    [Fact]
    public void Letters_become_terrain()
    {
        var terrain = Maps.Base.HexTerrain;
        Assert.Equal(Terrain.Plains, terrain[0]);
        Assert.Equal(Terrain.Wasteland, terrain[5]);
        Assert.Equal(Terrain.River, terrain[18]);
        Assert.Equal(Terrain.Wasteland, terrain[112]);
    }

    [Theory]
    [InlineData(0, new[] { 1, 13 })]                   // top-left corner
    [InlineData(5, new[] { 4, 6, 17, 18 })]            // top row
    [InlineData(18, new[] { 5, 6, 17, 19, 30, 31 })]   // a shifted (short) row: six neighbours
    [InlineData(31, new[] { 18, 19, 30, 32, 43, 44 })] // a long row in the middle
    public void Neighbours_on_the_base_map(int hex, int[] expected)
    {
        Assert.Equal(expected.OrderBy(h => h), Base.Neighbours(hex).OrderBy(h => h));
    }

    [Fact]
    public void Neighbours_are_mutual()
    {
        for (var hex = 0; hex < Base.HexCount; hex++)
            foreach (var neighbour in Base.Neighbours(hex))
                Assert.Contains(hex, Base.Neighbours(neighbour));
    }

    [Fact]
    public void A_map_may_start_with_a_short_row()
    {
        var layout = Maps.Parse("PP", "PPP", "PP").Layout;
        Assert.True(layout.IsShifted(0));
        Assert.False(layout.IsShifted(1));
        // Hex 0 (short row, shifted right) touches hex 1 beside it and hexes 2 and 3 below it.
        Assert.Equal(new[] { 1, 2, 3 }, layout.Neighbours(0).OrderBy(h => h));
    }

    // ---------- The other maps ----------

    public static IEnumerable<object[]> AllMaps() => Maps.Premade.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Every_map_has_nine_alternating_rows(MapId id)
    {
        var layout = Maps.ById(id).Layout;
        Assert.Equal(9, layout.RowCount);
        Assert.Equal(layout.HexCount, Maps.ById(id).HexTerrain.Count);
        for (var row = 1; row < layout.RowCount; row++)
            Assert.Equal(1, Math.Abs(layout.RowLengths[row] - layout.RowLengths[row - 1]));
        Assert.All(layout.RowLengths, length => Assert.InRange(length, 12, 13));
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Every_map_has_neighbours_that_are_mutual(MapId id)
    {
        var layout = Maps.ById(id).Layout;
        for (var hex = 0; hex < layout.HexCount; hex++)
            foreach (var neighbour in layout.Neighbours(hex))
                Assert.Contains(hex, layout.Neighbours(neighbour));
    }

    [Theory]
    [InlineData(MapId.Original)]
    [InlineData(MapId.OriginalV2)]
    [InlineData(MapId.FireAndIce)]
    [InlineData(MapId.Lakes)]
    [InlineData(MapId.Archipelago)]
    public void Balanced_maps_have_eleven_hexes_of_each_land_terrain(MapId id)
    {
        var terrain = Maps.ById(id).HexTerrain;
        foreach (var land in new[] { Terrain.Plains, Terrain.Swamp, Terrain.Lake, Terrain.Forest, Terrain.Mountain, Terrain.Wasteland, Terrain.Desert })
            Assert.Equal(11, terrain.Count(t => t == land));
    }

    [Theory]
    [InlineData(MapId.FireAndIce)]
    [InlineData(MapId.Lakes)]
    public void Fire_and_Ice_and_Lakes_start_with_a_short_row(MapId id)
    {
        var layout = Maps.ById(id).Layout;
        Assert.Equal(ValueList.Of(12, 13, 12, 13, 12, 13, 12, 13, 12), layout.RowLengths);
        Assert.Equal(112, layout.HexCount);
        Assert.True(layout.IsShifted(0));
        Assert.False(layout.IsShifted(1));
        // Hex 0 sits half a hex right of hex 12 (the start of the long row below), so it touches 12 and 13.
        Assert.Equal(new[] { 1, 12, 13 }, layout.Neighbours(0).OrderBy(h => h));
        // Hex 24 (end of row 1, a long row) touches only hex 11 above (row 0 has no 13th hex) and 36 below.
        Assert.Equal(new[] { 11, 23, 36 }, layout.Neighbours(24).OrderBy(h => h));
    }

    [Fact]
    public void Original_V2_keeps_the_original_rivers()
    {
        var original = Maps.Base.HexTerrain;
        var v2 = Maps.OriginalV2.HexTerrain;
        Assert.Equal(Maps.Base.Layout, Maps.OriginalV2.Layout);
        for (var hex = 0; hex < original.Count; hex++)
            Assert.Equal(original[hex] == Terrain.River, v2[hex] == Terrain.River);
    }

    [Fact]
    public void Lakes_terrain_matches_its_labels()
    {
        var map = Maps.Lakes;
        int At(int row, int column) => map.Layout.IndexOf(row, column);
        Assert.Equal(Terrain.Mountain, map.HexTerrain[At(0, 0)]);  // A1
        Assert.Equal(Terrain.River, map.HexTerrain[At(0, 8)]);     // A9
        Assert.Equal(Terrain.Lake, map.HexTerrain[At(0, 11)]);     // A12
        Assert.Equal(Terrain.Swamp, map.HexTerrain[At(1, 12)]);    // B13
        Assert.Equal(Terrain.Swamp, map.HexTerrain[At(8, 11)]);    // I12
    }

    // ---------- Custom maps ----------

    // A custom map: the first three hexes of each row after the first seven hold one terrain each (rows B to H: 3 of
    // each of the seven), the rest river.
    internal static string CustomRows(bool twelveTop, int perTerrain = 3)
    {
        MapLayout layout = Maps.CustomLayout(twelveTop);
        var terrain = Enumerable.Repeat(Terrain.River, layout.HexCount).ToArray();
        for (var t = 0; t < Terrains.Wheel.Length; t++)
            for (var i = 0; i < perTerrain; i++)
                terrain[layout.IndexOf(t + 1, i)] = Terrains.Wheel[t];
        return Maps.Rows(layout, terrain);
    }

    private static GameState AtTheMap() => TestHelpers.Play(TestHelpers.Concat(
        TestHelpers.Confirmed(Faction.Witches, Faction.Giants), new Move[] { new ConfirmTurnOrder() }));

    [Fact]
    public void Custom_layouts_alternate_thirteen_and_twelve()
    {
        Assert.Equal(Maps.Base.Layout, Maps.CustomLayout(twelveTop: false));
        Assert.Equal(Maps.FireAndIce.Layout, Maps.CustomLayout(twelveTop: true));
        Assert.Equal(112, Maps.CustomLayout(twelveTop: true).HexCount);
    }

    [Fact]
    public void A_map_written_as_rows_reads_back_the_same()
    {
        string rows = Maps.Rows(Maps.Base.Layout, Maps.Base.HexTerrain);
        Assert.StartsWith("PMFLDWPSWFLWS/DRRPSRRDSRRD/", rows);
        Assert.Equal(Maps.Base, Maps.FromRows(rows));
        Assert.Null(Maps.FromRows("PMX/RR"));
        Assert.Null(Maps.FromRows("PM//RR"));
    }

    [Fact]
    public void A_custom_map_is_chosen_from_its_rows()
    {
        string rows = CustomRows(twelveTop: true);
        var s = TestHelpers.PlayFrom(AtTheMap(), new ChooseCustomMap(rows));
        Assert.Equal(Phase.ChoosingRoundTiles, s.Phase);
        Assert.Equal(MapId.Custom, s.ChosenMap);
        Assert.Equal(Maps.CustomLayout(twelveTop: true), s.Layout);
        Assert.Equal(Maps.FromRows(rows)!.HexTerrain, s.HexTerrain);
        Assert.Equal(112, s.Map.Count);
        Assert.Equal("Map: custom (12-top)", GameLog.Describe(AtTheMap(), new ChooseCustomMap(rows), Array.Empty<GameEvent>(), s));
    }

    [Fact]
    public void A_custom_map_needs_three_hexes_of_every_terrain_and_its_usual_shape()
    {
        var s = AtTheMap();
        Assert.Empty(Maps.Shortfall(Maps.FromRows(CustomRows(twelveTop: false))!.HexTerrain));
        var two = Rules.Apply(s, new ChooseCustomMap(CustomRows(twelveTop: false, perTerrain: 2)));
        Assert.False(two.Accepted);
        Assert.Equal("Every terrain needs at least 3 hexes: 1 more Wasteland, 1 more Desert, 1 more Plains, 1 more Swamp, "
            + "1 more Lake, 1 more Forest, 1 more Mountain.", Assert.Single(two.Issues).Message);
        Assert.False(Rules.Apply(s, new ChooseCustomMap("WWWDDDPPPSSSLLLFFFMMM")).Accepted); // one row
        Assert.False(Rules.Apply(s, new ChooseCustomMap(CustomRows(twelveTop: false).Replace('R', 'I'))).Accepted); // ice
        Assert.False(Rules.Apply(s, new ChooseMap(MapId.Custom)).Accepted);
    }

    [Fact]
    public void A_custom_map_file_reads_back_the_same_and_refuses_anything_else()
    {
        MapDefinition map = Maps.FromRows(CustomRows(twelveTop: true, perTerrain: 1))!; // unfinished maps save too
        string json = MapFile.ToJson(map.Layout, map.HexTerrain);
        Assert.Contains("\"Format\": \"tmmap\"", json);
        MapLoadResult loaded = MapFile.FromJson(json);
        Assert.Null(loaded.Error);
        Assert.True(loaded.TwelveTop);
        Assert.Equal(map, loaded.Map);

        Assert.Equal("Not a Terra Mystica Calculator map.", MapFile.FromJson("{ \"Format\": \"tmcalc\", \"Version\": 1 }").Error);
        Assert.StartsWith("Not a readable map", MapFile.FromJson("not json").Error);
        Assert.Equal("A custom map has nine rows of 13 and 12 hexes in turn.",
            MapFile.FromJson("{ \"Format\": \"tmmap\", \"Version\": 1, \"Rows\": [ \"RRR\" ] }").Error);
        Assert.Equal("A custom map has river and the seven terrains of the wheel only.",
            MapFile.FromJson(MapFile.ToJson(map.Layout, map.HexTerrain.Select(t => t == Terrain.River ? Terrain.Ice : t).ToList())).Error);
    }

    [Fact]
    public void At_the_board_s_edge_a_bridge_needs_only_the_river_hex_on_the_board()
    {
        // The Fire & Ice map's B13–D13 (hexes 24 and 49): between them river C12 and, off the board, nothing. BGA allows
        // a bridge there.
        var s = TestHelpers.Play(TestHelpers.Concat(TestHelpers.Confirmed(Faction.Witches, Faction.Giants),
            new Move[] { new ConfirmTurnOrder(), new ChooseMap(MapId.FireAndIce) }));
        Assert.Contains((24, 49), Rules.BridgeEdges(s));
    }
}
