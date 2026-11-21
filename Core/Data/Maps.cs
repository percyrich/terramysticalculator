namespace Core;

// A map ready to play on: its shape and the starting terrain of every hex.
public sealed record MapDefinition(MapLayout Layout, ValueList<Terrain> HexTerrain);

public static class Maps
{
    // The base map. One letter per hex, one string per row:
    // W Wasteland, D Desert, P Plains, S Swamp, L Lake, F Forest, M Mountain, I Ice, V Volcano, R River.
    // As BGA's board and the printed board: 11 hexes of each land terrain (row A's A9 and A12 are Wasteland).
    public static readonly MapDefinition Base = Parse(
        "PMFLDWPSWFLWS",
        "DRRPSRRDSRRD",
        "RRSRMRFRFRMRR",
        "FLDRRWLRWRWP",
        "SPWLSPMDRRFSL",
        "MFRRDFRRRPMP",
        "RRRMRWRFRDSLD",
        "DLPRRRLSRMPM",
        "WSMLWFDPMRLFW");

    // The revised base map. Same rows and rivers as the original; some land hexes differ.
    public static readonly MapDefinition OriginalV2 = Parse(
        "PMFLPWPSWLFWS",
        "DRRDSRRDFRRD",
        "RRSRMRFRSRWRR",
        "FLDRRWLRWRMP",
        "SPWLDPFDRRFSW",
        "MFRRSMRRRPMD",
        "RRRMRWRFRDSLP",
        "DLPRRRLSRMPW",
        "LSMLWFDPMRLFM");

    // Fire & Ice (side 1). Starts with a short row: 12, 13, 12, ... (112 hexes).
    public static readonly MapDefinition FireAndIce = Parse(
        "PRPSDRMFWLDL",
        "WDRLMWRRRDPSM",
        "FSRRRPFDRRRR",
        "DMFDSRLWPRFLF",
        "RRPRRWSFMRPS",
        "FWRRFRRRPLRMW",
        "MRDMLWFRWMRS",
        "SLRSPMLRDSRWL",
        "MFRWDSDRLPRP");

    public static readonly MapDefinition Fjords = Parse(
        "FSRPDMSMDWSLD",
        "LPRLFWRRRRRP",
        "MFWRRPRSMPDRM",
        "RRRMRRFWLFWR",
        "WMDRLWRPDMPRS",
        "SPRFDFRMLFRM",
        "DLRSMSLRPSRFW",
        "FRPWPDWRRRWL",
        "SRRFLMLRFDSPD");

    // Starts with a short row: 12, 13, 12, ... (112 hexes).
    public static readonly MapDefinition Lakes = Parse(
        "MLWPDLDWRRFL",
        "DSFRRSPRFMRPS",
        "PRRFWMRSLWRD",
        "LWMRDPFRRDRSW",
        "FDRSLRRWRMFP",
        "MRPMRDRMRPSLW",
        "WRRRWFPSDRRM",
        "DLSRLMLRRMFRL",
        "SPRFRRRFWPDS");

    public static readonly MapDefinition Archipelago = Parse(
        "SLWFLPRWPSRLF",
        "FMSWMDRRFRSM",
        "DSDPLFRLRRRRL",
        "RMWRRRPDRWDR",
        "LRFRWRRWFRPSP",
        "WRRMSRRRRMFR",
        "MPFLDLRMSRRRL",
        "SMDWMFRPRDSW",
        "LDFPSPRDMRWPD");

    // The premade maps, in the map screen's order (MapId.Custom is made with ChooseCustomMap instead).
    public static readonly IReadOnlyList<MapId> Premade = new[]
        { MapId.Original, MapId.OriginalV2, MapId.FireAndIce, MapId.Fjords, MapId.Lakes, MapId.Archipelago };

    public static MapDefinition ById(MapId id) => id switch
    {
        MapId.Original => Base,
        MapId.OriginalV2 => OriginalV2,
        MapId.FireAndIce => FireAndIce,
        MapId.Fjords => Fjords,
        MapId.Lakes => Lakes,
        MapId.Archipelago => Archipelago,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    public static string Name(MapId id) => id switch
    {
        MapId.Original => "Base Map",
        MapId.OriginalV2 => "Revised Base Map",
        MapId.FireAndIce => "Fire & Ice",
        MapId.Fjords => "Fjords",
        MapId.Lakes => "Lakes",
        MapId.Archipelago => "Archipelago",
        MapId.Custom => "Custom",
        _ => id.ToString(),
    };

    // ---------- Custom maps ----------

    // A custom map's shape: nine rows alternating 13 and 12 hexes, starting with 12 (twelveTop, as Fire & Ice: 112
    // hexes) or 13 (as the original: 113).
    public static MapLayout CustomLayout(bool twelveTop) =>
        new(new ValueList<int>(Enumerable.Range(0, 9).Select(row => (row % 2 == 0) == twelveTop ? 12 : 13)));

    // A map as text, as ChooseCustomMap carries it: one letter per hex (as in Parse), rows separated by '/'.
    public static string Rows(MapLayout layout, IReadOnlyList<Terrain> terrain)
    {
        var rows = new List<string>();
        var hex = 0;
        foreach (int length in layout.RowLengths)
        {
            rows.Add(new string(Enumerable.Range(hex, length).Select(i => ToLetter(terrain[i])).ToArray()));
            hex += length;
        }
        return string.Join("/", rows);
    }

    // The map in that text, or null if it isn't one (an unknown letter, an empty row).
    public static MapDefinition? FromRows(string rows)
    {
        string[] parts = (rows ?? "").Split('/');
        if (parts.Any(part => part.Length == 0) || parts.SelectMany(part => part).Any(letter => !Letters.Contains(letter)))
            return null;
        return Parse(parts);
    }

    // How many more hexes of each terrain a custom map needs (Config.CustomMapMinimumHexes each; none missing: empty),
    // in the wheel's order.
    public static List<(Terrain Terrain, int Missing)> Shortfall(IEnumerable<Terrain> terrain)
    {
        var counts = terrain.GroupBy(t => t).ToDictionary(group => group.Key, group => group.Count());
        return Terrains.Wheel.Select(t => (t, Config.CustomMapMinimumHexes - counts.GetValueOrDefault(t)))
            .Where(part => part.Item2 > 0).ToList();
    }

    private const string Letters = "WDPSLFMIVR";

    private static char ToLetter(Terrain terrain) => terrain switch
    {
        Terrain.Wasteland => 'W',
        Terrain.Desert => 'D',
        Terrain.Plains => 'P',
        Terrain.Swamp => 'S',
        Terrain.Lake => 'L',
        Terrain.Forest => 'F',
        Terrain.Mountain => 'M',
        Terrain.Ice => 'I',
        Terrain.Volcano => 'V',
        _ => 'R',
    };

    public static MapDefinition Parse(params string[] rows)
    {
        var layout = new MapLayout(new ValueList<int>(rows.Select(row => row.Length)));
        var terrain = new ValueList<Terrain>(rows.SelectMany(row => row).Select(FromLetter));
        return new MapDefinition(layout, terrain);
    }

    private static Terrain FromLetter(char letter) => letter switch
    {
        'W' => Terrain.Wasteland,
        'D' => Terrain.Desert,
        'P' => Terrain.Plains,
        'S' => Terrain.Swamp,
        'L' => Terrain.Lake,
        'F' => Terrain.Forest,
        'M' => Terrain.Mountain,
        'I' => Terrain.Ice,
        'V' => Terrain.Volcano,
        'R' => Terrain.River,
        _ => throw new ArgumentException($"Unknown terrain letter '{letter}'."),
    };
}
