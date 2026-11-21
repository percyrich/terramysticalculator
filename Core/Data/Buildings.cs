namespace Core;

// Fixed facts about each kind of building, so rules can treat every building kind the same way.
public static class Buildings
{
    // Every building kind, in the order the banks are listed.
    public static readonly PieceKind[] All =
    {
        PieceKind.Dwelling, PieceKind.TradingPost, PieceKind.Temple, PieceKind.Stronghold, PieceKind.Sanctuary,
    };

    public static int PerPlayer(PieceKind kind) => kind switch
    {
        PieceKind.Dwelling    => Config.DwellingsPerPlayer,
        PieceKind.TradingPost => Config.TradingPostsPerPlayer,
        PieceKind.Temple      => Config.TemplesPerPlayer,
        PieceKind.Stronghold  => Config.StrongholdsPerPlayer,
        PieceKind.Sanctuary   => Config.SanctuariesPerPlayer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a building."),
    };

    // The building an upgrade to this kind replaces on the map.
    // Null for a dwelling, which is built on an empty hex rather than by upgrading.
    public static PieceKind? UpgradedFrom(PieceKind kind) => kind switch
    {
        PieceKind.TradingPost => PieceKind.Dwelling,
        PieceKind.Temple      => PieceKind.TradingPost,
        PieceKind.Stronghold  => PieceKind.TradingPost,
        PieceKind.Sanctuary   => PieceKind.Temple,
        _ => null,
    };

    public static bool IsUpgrade(PieceKind kind) => UpgradedFrom(kind) is not null;

    // Every kind an existing building of this kind can be upgraded to (a trading post has two choices).
    public static IEnumerable<PieceKind> UpgradesOf(PieceKind kind) => All.Where(to => UpgradedFrom(to) == kind);

    // For messages: "trading post", "temple", ...
    public static string Noun(PieceKind kind) => kind switch
    {
        PieceKind.TradingPost => "trading post",
        PieceKind.Landscape => "special landscape",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
