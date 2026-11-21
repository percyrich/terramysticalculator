namespace Core;

// A place a piece can be moved to. Pure data, like a Move, but without saying which piece.
public abstract record Destination;

public sealed record HexDestination(int Hex) : Destination;

public sealed record PriestSlotDestination(Track Track, int Slot) : Destination;

// A whole track (not one step of it), for bonus actions that move a player up a track of their choice.
public sealed record TrackDestination(Track Track) : Destination;

// One way to transform a hex: into To, with this many spades, of which FreeSpades are free and the rest
// cost Cost (workers). Move is the move that does it (SpendSpades or TransformTerrain).
// Volcano factions use no spades: VolcanoPrice is what making the hex Volcano costs them instead (Dragonlords:
// power tokens removed; Acolytes: steps paid from one track); Cost is then only a carpet or tunnel (never).
public sealed record TransformChoice(Terrain To, int Spades, int FreeSpades, Cost Cost, Move Move, int VolcanoPrice = 0);

// Mermaids: a key token on this river hex would join these buildings (two or more groups) into a town.
public sealed record RiverTown(int RiverHex, ValueList<int> Hexes);

// The answer to "where can this piece go?".
// Reason explains an empty list (e.g. "It is not the Giants' turn."); it is null when the list is not empty.
public sealed record PieceOptions(ValueList<Destination> Destinations, string? Reason)
{
    public bool CanMove => Destinations.Count > 0;
}
