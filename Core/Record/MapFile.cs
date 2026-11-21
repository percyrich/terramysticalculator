using System.Text.Json;

namespace Core;

// A custom map on its own (the custom map editor's Save, Save as and Load): a .tmmap file, readable JSON, the map only.
//   { "Format": "tmmap", "Version": 1, "Rows": [ "PMFLDWPSWFLWS", "DRRPSRRDSRRD", ... ] }
// One string a row, top row first, one letter a hex (as Maps.Parse: W D P S L F M for the wheel's terrains, R river).
// Loading accepts only what the editor can make: one of the two custom layouts (Maps.CustomLayout) and river and the
// seven terrains of the wheel. It need not be finished (at least 3 of each terrain is ChooseCustomMap's rule).
public sealed record MapLoadResult(MapDefinition? Map, bool TwelveTop, string? Error);

public static class MapFile
{
    public const string Format = "tmmap";
    public const int Version = 1;
    public const string Extension = "tmmap";

    private sealed record MapData(string Format, int Version, string[] Rows);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(MapLayout layout, IReadOnlyList<Terrain> terrain) =>
        JsonSerializer.Serialize(new MapData(Format, Version, Maps.Rows(layout, terrain).Split('/')), Options);

    public static MapLoadResult FromJson(string json)
    {
        MapData? data;
        try
        {
            data = JsonSerializer.Deserialize<MapData>(json, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return new MapLoadResult(null, false, $"Not a readable map: {e.Message}");
        }
        if (data is null || data.Format != Format || data.Rows is null)
            return new MapLoadResult(null, false, "Not a Terra Mystica Calculator map.");
        if (data.Version > Version)
            return new MapLoadResult(null, false, $"Saved by a newer version (format {data.Version}; this one reads up to {Version}).");
        if (Maps.FromRows(string.Join("/", data.Rows)) is not MapDefinition map)
            return new MapLoadResult(null, false, "The map has an empty row or an unknown letter.");
        bool twelveTop = map.Layout.Equals(Maps.CustomLayout(twelveTop: true));
        if (!twelveTop && !map.Layout.Equals(Maps.CustomLayout(twelveTop: false)))
            return new MapLoadResult(null, false, "A custom map has nine rows of 13 and 12 hexes in turn.");
        if (map.HexTerrain.Any(terrain => terrain != Terrain.River && !Terrains.IsBasic(terrain)))
            return new MapLoadResult(null, false, "A custom map has river and the seven terrains of the wheel only.");
        return new MapLoadResult(map, twelveTop, null);
    }
}
