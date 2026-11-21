namespace Core;

// The Special Landscapes expansion (GameOptions.Landscapes): one special landscape per faction, its name, what must be
// on the map before it can be placed, and where it goes. What each does is in Rules.Landscapes.cs.
public static class Landscapes
{
    public const int AurenVictoryPoints = 4;           // Tree of Serenity: reaching the top step of a track
    public const int WitchesTownSteps = 3;             // Raven's Blunder: instead of the Witches' 5 VP for a town
    public const int MermaidsSpades = 1;               // Coral Reef: its cost
    public const int SwarmlingsMaxKeys = 4;            // Breeding Ground: town, city, metropolis, megacity
    public const int FakirsCarpetCoins = 2;            // Flight School: every carpet flight
    public const int YetisTokens = 2;                  // Frozen Peak: new tokens in bowl 3, once
    public const int DragonlordsTokensPerRound = 1;    // Lava Pond: a new token in bowl 3 as every round begins
    public const int AcolytesSteps = 3;                // Altar of Sacrifice: its cost, from one track, whatever the terrain
    public const int IceMaidensSpadesPerTemple = 1;    // Winterfall
    public const int DwarvesTunnelSkip = 2;            // Great Tunnel: hexes a tunnel from it may skip
    public const int HalflingsSpadesSaved = 1;         // Driller: per transform next to it (at least 1 spade still paid)

    public static string Name(Faction faction) => faction switch
    {
        Faction.Auren => "Tree of Serenity",
        Faction.Witches => "Raven's Blunder",
        Faction.Alchemists => "Gloomy Workshop",
        Faction.Darklings => "Camp",
        Faction.Cultists => "Cult Palace",
        Faction.Halflings => "Driller",
        Faction.Dwarves => "Great Tunnel",
        Faction.Engineers => "Bridge Tower",
        Faction.Mermaids => "Coral Reef",
        Faction.Swarmlings => "Breeding Ground",
        Faction.ChaosMagicians => "Chaos Portal",
        Faction.Giants => "Cave of Wisdom",
        Faction.Fakirs => "Flight School",
        Faction.Nomads => "Trading Camp",
        Faction.IceMaidens => "Winterfall",
        Faction.Yetis => "Frozen Peak",
        Faction.Acolytes => "Altar of Sacrifice",
        Faction.Dragonlords => "Lava Pond",
        Faction.Riverwalkers => "Floating Palace",
        Faction.Shapeshifters => "Spectral Polestar",
        _ => "special landscape",
    };

    // What must stand on the map (the faction's own, of exactly this kind) before the landscape can be placed; null:
    // nothing. Setup's checks count a higher building of the same line too (Rules.SetupIssues).
    public static (PieceKind Kind, int Count)? Prerequisite(Faction faction) => faction switch
    {
        Faction.Witches => (PieceKind.Temple, 2),
        Faction.Giants => (PieceKind.Temple, 1),
        Faction.Nomads => (PieceKind.TradingPost, 1),
        Faction.Alchemists or Faction.Darklings or Faction.Halflings or Faction.ChaosMagicians => (PieceKind.Stronghold, 1),
        Faction.Swarmlings or Faction.Dragonlords => (PieceKind.Sanctuary, 1),
        _ => null,
    };

    // What a piece on the map counts as for the tiles that score kinds of building: the Darklings' Camp as a dwelling.
    public static PieceKind CountsAs(Piece piece) =>
        piece is { Kind: PieceKind.Landscape, Owner: Faction.Darklings } ? PieceKind.Dwelling : piece.Kind;

    // Placed on a river hex next to one of the faction's structures, not on land.
    public static bool OnRiver(Faction faction) => faction is Faction.Mermaids or Faction.Riverwalkers;

    // The prerequisite in words ("2 temples", "a stronghold").
    public static string PrerequisiteText(Faction faction) => Prerequisite(faction) is { } needed
        ? needed.Count == 1 ? $"a {Buildings.Noun(needed.Kind)}" : $"{needed.Count} {Buildings.Noun(needed.Kind)}s"
        : "nothing";
}
