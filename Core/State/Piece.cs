namespace Core;

// One physical piece. Identity = owner + kind + number. Two Piece values with the same three
// fields are the same piece.
// Number is only an ID: it lets the view follow one particular piece as it moves. It means nothing
// in the rules, and no rule may read a position or order from it (pieces move between slots freely,
// e.g. a dwelling returned by an upgrade goes to whichever slot is free).
// Colour is not stored: a piece is drawn in its owner's colour (Factions.ColourOf).
public readonly record struct Piece(Faction Owner, PieceKind Kind, int Number);
