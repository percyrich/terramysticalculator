using System;
using System.Collections.Generic;
using System.Linq;
using Core;

namespace TMCalc;

// Everything the UI counts with an icon: resources, and the things triggers count (buildings, towns,
// track steps, ...). Within one bundle, amounts are always drawn in this order.
public enum ResourceKind
{
    Priest,
    Worker,
    Coin,
    Power,
    PowerSpend,    // power spent from bowl 3 (a power action's cost)
    Spade,
    Bridge,
    Key,
    VictoryPoint,
    PowerValue,    // a building's power value (for towns and neighbours' power), drawn as small power tokens
    Shipping,      // shipping levels
    Stronghold,    // buildings, e.g. in "per stronghold or sanctuary"
    TradingPost,
    Dwelling,
    Temple,
    Sanctuary,
    WhenDwellingBuilt,    // "each time one is built", as on round tiles
    WhenTradingPostBuilt,
    WhenTempleBuilt,
    WhenBigBuildingBuilt, // a stronghold or a sanctuary
    Town,
    FireStep,      // steps on one track
    WaterStep,
    EarthStep,
    AirStep,
    EveryCultStep, // a step on every track at once
    PriestOnTrack, // a priest in a priest zone
    AnyCultStep,   // a step on one track of the player's choice (new kinds go at the end)
    CarpetRange,   // the Fakirs' flying carpet: hexes it may skip
    Tunnel,        // the Dwarves' tunnelling
    TokenRemoved,  // a power token removed for the rest of the game (the Dragonlords' price for Volcano)
    Lamp,          // fan factions: the Djinni's magic lamps
    Treasure,      // the Goblins' treasure tokens
    GoldenSpade,   // the Prospectors' spades (bought with coins)
    Pyro,          // the Firewalkers' pyro token
}

public readonly record struct Amount(ResourceKind Kind, int Count);

// Several amounts together, e.g. "1 priest, 2 workers, 5 coins": the one way amounts are described,
// written and drawn across the UI (costs, incomes, rewards, triggers; see ResourceBundle). Always in
// ResourceKind order, each kind at most once, with no zeros. Two Amounts with the same parts are equal.
public sealed record Amounts
{
    public static readonly Amounts None = new(Array.Empty<Amount>());

    public ValueList<Amount> Parts { get; }

    private Amounts(IEnumerable<Amount> parts) =>
        Parts = new ValueList<Amount>(parts
            .GroupBy(part => part.Kind)
            .Select(group => new Amount(group.Key, group.Sum(part => part.Count)))
            .Where(part => part.Count != 0)
            .OrderBy(part => part.Kind));

    public static Amounts Of(params (ResourceKind Kind, int Count)[] parts) =>
        new(parts.Select(part => new Amount(part.Kind, part.Count)));

    public static Amounts Of(ResourceKind kind, int count) => Of((kind, count));

    // One of each, e.g. Each(Stronghold, Sanctuary) for "per stronghold or sanctuary".
    public static Amounts Each(IEnumerable<ResourceKind> kinds) => new(kinds.Select(kind => new Amount(kind, 1)));

    public static Amounts From(Cost cost) =>
        Of((ResourceKind.Priest, cost.Priests), (ResourceKind.Worker, cost.Workers), (ResourceKind.Coin, cost.Coins));

    public static Amounts From(Income income) => Of(
        (ResourceKind.Priest, income.Priests), (ResourceKind.Worker, income.Workers),
        (ResourceKind.Coin, income.Coins), (ResourceKind.Power, income.Power));

    public static Amounts operator +(Amounts a, Amounts b) => new(a.Parts.Concat(b.Parts));

    public bool IsEmpty => Parts.Count == 0;

    // How many of one kind (0 if none).
    public int this[ResourceKind kind] => Parts.FirstOrDefault(part => part.Kind == kind).Count;

    // The kind for a building, and for steps on a track.
    public static ResourceKind KindOf(PieceKind building) => building switch
    {
        PieceKind.Dwelling => ResourceKind.Dwelling,
        PieceKind.TradingPost => ResourceKind.TradingPost,
        PieceKind.Temple => ResourceKind.Temple,
        PieceKind.Stronghold => ResourceKind.Stronghold,
        PieceKind.Sanctuary => ResourceKind.Sanctuary,
        _ => ResourceKind.Priest,
    };

    // The "when built" kind for buildings scored together (a stronghold and a sanctuary share one).
    public static ResourceKind WhenBuilt(System.Collections.Generic.IReadOnlyCollection<PieceKind> buildings) =>
        buildings.Contains(PieceKind.Stronghold) || buildings.Contains(PieceKind.Sanctuary) ? ResourceKind.WhenBigBuildingBuilt
        : buildings.Contains(PieceKind.TradingPost) ? ResourceKind.WhenTradingPostBuilt
        : buildings.Contains(PieceKind.Temple) ? ResourceKind.WhenTempleBuilt
        : ResourceKind.WhenDwellingBuilt;

    public static ResourceKind StepsOn(Track track) => track switch
    {
        Track.Fire => ResourceKind.FireStep,
        Track.Water => ResourceKind.WaterStep,
        Track.Earth => ResourceKind.EarthStep,
        _ => ResourceKind.AirStep,
    };

    // e.g. "1 priest, 2 workers, 5 coins"; "nothing" when empty.
    public override string ToString() =>
        IsEmpty ? "nothing" : string.Join(", ", Parts.Select(part => $"{part.Count} {Name(part.Kind, part.Count)}"));

    private static readonly (ResourceKind Kind, string One, string Many)[] Names =
    {
        (ResourceKind.Priest, "priest", "priests"),
        (ResourceKind.Worker, "worker", "workers"),
        (ResourceKind.Coin, "coin", "coins"),
        (ResourceKind.Power, "power", "power"),
        (ResourceKind.PowerSpend, "power spent", "power spent"),
        (ResourceKind.Spade, "spade", "spades"),
        (ResourceKind.Bridge, "bridge", "bridges"),
        (ResourceKind.Key, "key", "keys"),
        (ResourceKind.VictoryPoint, "VP", "VP"),
        (ResourceKind.PowerValue, "power value", "power value"),
        (ResourceKind.Shipping, "shipping", "shipping"),
        (ResourceKind.Stronghold, "stronghold", "strongholds"),
        (ResourceKind.TradingPost, "trading post", "trading posts"),
        (ResourceKind.Dwelling, "dwelling", "dwellings"),
        (ResourceKind.Temple, "temple", "temples"),
        (ResourceKind.Sanctuary, "sanctuary", "sanctuaries"),
        (ResourceKind.WhenDwellingBuilt, "dwelling built", "dwellings built"),
        (ResourceKind.WhenTradingPostBuilt, "trading post built", "trading posts built"),
        (ResourceKind.WhenTempleBuilt, "temple built", "temples built"),
        (ResourceKind.WhenBigBuildingBuilt, "stronghold or sanctuary built", "strongholds or sanctuaries built"),
        (ResourceKind.Town, "town", "towns"),
        (ResourceKind.FireStep, "fire step", "fire steps"),
        (ResourceKind.WaterStep, "water step", "water steps"),
        (ResourceKind.EarthStep, "earth step", "earth steps"),
        (ResourceKind.AirStep, "air step", "air steps"),
        (ResourceKind.EveryCultStep, "step on every track", "steps on every track"),
        (ResourceKind.PriestOnTrack, "priest on a track", "priests on the tracks"),
        (ResourceKind.AnyCultStep, "cult step", "cult steps"),
        (ResourceKind.CarpetRange, "carpet range", "carpet range"),
        (ResourceKind.Tunnel, "tunnel", "tunnels"),
        (ResourceKind.TokenRemoved, "power token removed", "power tokens removed"),
        (ResourceKind.Lamp, "lamp", "lamps"),
        (ResourceKind.Treasure, "treasure", "treasures"),
        (ResourceKind.GoldenSpade, "golden spade", "golden spades"),
        (ResourceKind.Pyro, "pyro", "pyro"),
    };

    private static string Name(ResourceKind kind, int count)
    {
        var (_, one, many) = Names.First(name => name.Kind == kind);
        return Math.Abs(count) == 1 ? one : many;
    }

    // Reads the written form back: "1 priest, 2 workers, 5 coins", "3 workers and 2 coins", "2 VP".
    // Case, commas and "and" don't matter; names are singular or plural as in ToString.
    // Anything it cannot read is reported in `problem` (null if all was read). Used for amounts typed in the editor.
    public static Amounts Parse(string text, out string problem)
    {
        problem = null;
        var parts = new List<(ResourceKind, int)>();
        string[] words = Words(text);
        for (var i = 0; i < words.Length;)
        {
            if (!int.TryParse(words[i], out int count))
            {
                problem = $"Expected a number at \"{words[i]}\".";
                break;
            }
            i++;
            if (!TryReadName(words, ref i, out ResourceKind kind))
            {
                problem = i < words.Length ? $"Unknown resource \"{words[i]}\"." : "A number with no resource after it.";
                break;
            }
            parts.Add((kind, count));
        }
        return Of(parts.ToArray());
    }

    // Reads a list of names with no numbers, in the order written: "workers, coins, VP, keys".
    public static List<ResourceKind> ParseKinds(string text, out string problem)
    {
        problem = null;
        var kinds = new List<ResourceKind>();
        string[] words = Words(text);
        for (var i = 0; i < words.Length;)
        {
            if (!TryReadName(words, ref i, out ResourceKind kind))
            {
                problem = $"Unknown resource \"{words[i]}\".";
                break;
            }
            kinds.Add(kind);
        }
        return kinds;
    }

    private static string[] Words(string text) =>
        (text ?? "").Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !word.Equals("and", StringComparison.OrdinalIgnoreCase)).ToArray();

    // The longest name, singular or plural, that the words from i on spell ("power value" before "power").
    // Moves i past it.
    private static bool TryReadName(string[] words, ref int i, out ResourceKind kind)
    {
        int start = i;
        var match = Names
            .SelectMany(name => new[] { (name.Kind, Text: name.One), (name.Kind, Text: name.Many) })
            .Where(name => Matches(words, start, name.Text))
            .OrderByDescending(name => name.Text.Split(' ').Length)
            .FirstOrDefault();
        kind = match.Kind;
        if (match.Text is null)
            return false;
        i += match.Text.Split(' ').Length;
        return true;
    }

    private static bool Matches(string[] words, int start, string name)
    {
        string[] nameWords = name.Split(' ');
        if (start + nameWords.Length > words.Length)
            return false;
        for (var k = 0; k < nameWords.Length; k++)
            if (!words[start + k].Equals(nameWords[k], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }
}
