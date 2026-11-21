using Core;

namespace BgaImport;

// BGA's numbers and names for Terra Mystica's pieces, and Core's. Matched by what each tile does (the type tables in
// gameui.gamedatas carry BGA's descriptions).
public static class Tables
{
    // gamedatas.scoring_tiles[round].scoring_type → Core RoundTiles id.
    private static readonly Dictionary<int, int> RoundTileIds = new()
    {
        [1] = 2, [2] = 1, [3] = 7, [4] = 3, [5] = 6, [6] = 0, [7] = 4, [8] = 5, [9] = 8,
    };

    // gamedatas.bonus_cards[id].bonus_type → Core PassTiles id.
    private static readonly Dictionary<int, int> PassTileIds = new()
    {
        [1] = 6, [2] = 3, [3] = 0, [4] = 5, [5] = 1, [6] = 8, [7] = 7, [8] = 2, [9] = 4, [10] = 9,
    };

    // favor_type → Core Favours id.
    private static readonly Dictionary<int, int> FavourIds = new()
    {
        [1] = 2, [2] = 5, [3] = 8, [4] = 11, [5] = 1, [6] = 4, [7] = 7, [8] = 10, [9] = 0, [10] = 3, [11] = 6, [12] = 9,
    };

    // town_type → Core KeyTiles id.
    private static readonly Dictionary<int, int> KeyTileIds = new()
    {
        [1] = 0, [2] = 1, [3] = 2, [4] = 3, [5] = 4, [6] = 6, [7] = 5, [8] = 7,
    };

    // power action_id → Core PowerActions id (BGA has 7 coins and 2 workers the other way round).
    private static readonly Dictionary<int, int> PowerActionIds = new()
    {
        [1] = 0, [2] = 1, [3] = 3, [4] = 2, [5] = 4, [6] = 5,
    };

    public static int RoundTile(int bgaType) => Look(RoundTileIds, bgaType, "scoring tile type");
    public static int PassTile(int bgaType) => Look(PassTileIds, bgaType, "bonus card type");
    public static int Favour(int bgaType) => Look(FavourIds, bgaType, "favour tile type");
    public static int KeyTile(int bgaType) => Look(KeyTileIds, bgaType, "town tile type");
    public static int PowerAction(int bgaId) => Look(PowerActionIds, bgaId, "power action");

    // order_space_id (cos_no): 0 is the 1-step space (the priest comes back), 1–4 the 3, 2, 2, 2 spaces.
    public static int PriestSlot(int cosNo) => cosNo == 0 ? 4 : cosNo - 1;

    // cult_id 1–4, or a name such as "Cult of Earth" / "earth".
    public static Track ToTrack(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t switch
        {
            "1" => Track.Fire, "2" => Track.Water, "3" => Track.Earth, "4" => Track.Air,
            _ when t.Contains("fire") => Track.Fire,
            _ when t.Contains("water") => Track.Water,
            _ when t.Contains("earth") => Track.Earth,
            _ when t.Contains("air") => Track.Air,
            _ => throw new InvalidDataException($"Unknown cult \"{text}\"."),
        };
    }

    public static Terrain ToTerrain(string text) => text.Trim().ToLowerInvariant() switch
    {
        "plains" => Terrain.Plains,
        "swamp" => Terrain.Swamp,
        "lakes" or "lake" => Terrain.Lake,
        "forest" => Terrain.Forest,
        "mountains" or "mountain" => Terrain.Mountain,
        "wasteland" => Terrain.Wasteland,
        "desert" => Terrain.Desert,
        "ice" => Terrain.Ice,
        "volcano" => Terrain.Volcano,
        "river" => Terrain.River,
        _ => throw new InvalidDataException($"Unknown terrain \"{text}\"."),
    };

    public static PieceKind ToBuilding(string text) => text.Trim().ToLowerInvariant() switch
    {
        "dwelling" => PieceKind.Dwelling,
        "tradinghouse" => PieceKind.TradingPost,
        "temple" => PieceKind.Temple,
        "stronghold" => PieceKind.Stronghold,
        "sanctuary" => PieceKind.Sanctuary,
        "landscape" => PieceKind.Landscape,
        _ => throw new InvalidDataException($"Unknown structure \"{text}\"."),
    };

    // BGA's faction type ("chaosmagicians") → Core's Faction. The fan factions' names are guesses where they differ.
    public static Faction ToFaction(string bgaType)
    {
        var t = bgaType.Trim().ToLowerInvariant();
        var special = t switch
        {
            "theenlightened" => Faction.Enlightened,
            "golddiggers" => Faction.Prospectors, // BGA's name for them (its factions table says "Prospectors")
            _ => (Faction?)null,
        };
        if (special is Faction found)
            return found;
        if (Enum.TryParse<Faction>(t, ignoreCase: true, out var faction))
            return faction;
        throw new InvalidDataException($"Unknown faction \"{bgaType}\".");
    }

    // fire_ice_end_scoring_tile: index 4 is "Greatest Distance" (seen); the others are matched by their title.
    public static FinalScoringTile? ToFinalScoring(string? index, string? title)
    {
        var t = (title ?? "").ToLowerInvariant();
        if (index == "4" || t.Contains("distance"))
            return FinalScoringTile.Distance;
        if (t.Contains("stronghold") || t.Contains("sanctuary"))
            return FinalScoringTile.StrongholdSanctuary;
        if (t.Contains("cluster") || t.Contains("settlement"))
            return FinalScoringTile.Clusters;
        if (t.Contains("edge") || t.Contains("border") || t.Contains("outpost"))
            return FinalScoringTile.Edges;
        return null;
    }

    private static int Look(Dictionary<int, int> table, int key, string what) =>
        table.TryGetValue(key, out var id) ? id : throw new InvalidDataException($"Unknown {what} {key}.");
}
