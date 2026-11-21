namespace Core;

// The small records a GameState is made of.

// A bridge joins two land hexes across the edge between two river hexes. HexA < HexB.
// The two hexes count as adjacent for its owner only (range, towns, power from opponents' builds).
public readonly record struct Bridge(Faction Owner, int HexA, int HexB);

// Builder built on Hex, next to this player's buildings, whose power values add up to Power.
// The player may accept (gaining up to that much power, for one victory point less) or decline.
public readonly record struct PowerOffer(Faction Player, int Power, int Hex, Faction Builder);

// The Cultists or the Shapeshifters built and opponents were offered power: once every offer is answered, the
// builder reacts to whether anyone accepted (Cultists: a cult step, or 1 power; Shapeshifters: may pay 1 VP for a
// token in bowl 3, or 1 power).
public readonly record struct CultistReaction(Faction Cultists, bool AnyAccepted);

// Free play under way (GameState.Planning): where it began, to hand the turn back on leaving, and who has acted.
//   EntryPlayer, EntryActionTaken, EntryActionsOwed: the turn as it stood when Free began;
//   EntryRound: the round then (a round advanced in Free leads to the turn-order step on leaving);
//   Acted: every faction that has taken an action in Free (the entry player then counts as having taken theirs);
//   Selected: the faction selected, kept when a round ends.
public sealed record FreePlan(Faction? EntryPlayer, bool EntryActionTaken, int EntryActionsOwed, int EntryRound,
    ValueList<Faction> Acted, Faction? Selected);

// Calculator: the neutral opponent's pieces (any number), standing in for the other players: dwellings on hexes (the
// hex keeps its terrain underneath; shown in the power purple), bridges on bridge spots, priests in priest slots. They
// only take up room: no power, towns, steps or scoring; a neutral dwelling counts as an opponent's building beside a
// trading post (its cost).
public sealed record NeutralBoard(ValueList<int> Hexes, ValueList<(int HexA, int HexB)> Bridges, ValueList<(Track Track, int Slot)> Priests)
{
    public static readonly NeutralBoard Empty = new(ValueList<int>.Empty, ValueList<(int, int)>.Empty, ValueList<(Track, int)>.Empty);
}

// A key token placed on the map, on one hex of the town it formed.
public readonly record struct PlacedKey(KeyTile Tile, Faction Owner, int Hex);

// A town that a building or bridge (Trigger) made formable this turn, not yet formed: forming it shows its points
// set off from there (Rules.NoteTownMakers). Hexes: the buildings it had then.
public sealed record TownMaker(Faction Faction, ValueList<int> Hexes, PointsArea Trigger);
