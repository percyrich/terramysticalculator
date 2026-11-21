namespace Core;

// Events describe what happened as a result of a move.
// The view uses them to choose animations and the game log to word its lines; the rules never read them.
public abstract record GameEvent;

// ---------- Setup ----------
public sealed record TurnOrderChosen(Faction Faction, int Position) : GameEvent;
public sealed record RoundTileChosen(int Tile, int Round) : GameEvent;
public sealed record TurnOrderStepBegan() : GameEvent;
public sealed record KeyRemoved(Faction Faction, KeyTile Tile, int Hex) : GameEvent; // its group no longer allows it

// ---------- Rounds and turns ----------
public sealed record RoundStarted(int Round) : GameEvent;
public sealed record IncomeReceived(Faction Faction, Income Income) : GameEvent;
public sealed record Passed(Faction Player) : GameEvent;
public sealed record PassTileTaken(Faction Faction, int Tile, int FromSlot, int Coins) : GameEvent;
public sealed record PassTileReturned(Faction Faction, int Tile, int ToSlot) : GameEvent; // passing in the last round
public sealed record RoundEndRewardReceived(Faction Faction, Income Income, int Spades) : GameEvent;
public sealed record RoundEnded(int Round) : GameEvent;
public sealed record GameEnded(ValueList<Faction> Winners) : GameEvent;
public sealed record PlanningStarted(Faction? Selected) : GameEvent;

// ---------- The map ----------
public sealed record TerrainTransformed(int Hex, Terrain From, Terrain To) : GameEvent;
public sealed record DwellingBuilt(Piece Dwelling, int FromSlot, int Hex) : GameEvent;
public sealed record BuildingUpgraded(Piece Building, int FromSlot, int Hex) : GameEvent; // the new building
public sealed record BuildingReturned(Piece Building, int FromHex, int ToSlot) : GameEvent; // the one it replaced
public sealed record SpadesGained(Faction Faction, int Count) : GameEvent;
public sealed record SpadesSpent(Faction Faction, int Hex, Terrain From, Terrain To, int FreeSpades, int PaidSpades) : GameEvent;
public sealed record SpadesForfeited(Faction Faction, int Count) : GameEvent;
public sealed record TownFormed(Faction Faction, KeyTile Tile, int Hex, ValueList<int> Hexes) : GameEvent;
public sealed record LandscapePlaced(Piece Landscape, int Hex) : GameEvent;
public sealed record LandscapeBonusOffered(Faction Faction, LandscapeOffer Offer) : GameEvent;
public sealed record BridgesSkipped(Faction Faction, int Count) : GameEvent;

// ---------- Priests, tracks, favours ----------
public sealed record PriestGained(Piece Priest) : GameEvent;
public sealed record PriestPlaced(Piece Priest, Track Track, int Slot, int StepsBefore, int StepsAfter) : GameEvent;
public sealed record PriestDebtIncurred(Faction Faction, int Debt) : GameEvent;   // Debt: the new total
public sealed record PriestDebtPaid(Faction Faction, int Debt) : GameEvent;       // by a priest gained, or cancelled
public sealed record TrackAdvanced(Faction Faction, Track Track, int StepsBefore, int StepsAfter) : GameEvent;
public sealed record TrackDescended(Faction Faction, Track Track, int StepsBefore, int StepsAfter) : GameEvent;
public sealed record CultStepOwed(Faction Faction) : GameEvent;
public sealed record KeySpent(Faction Faction, Track Track) : GameEvent;
public sealed record KeyReturned(Faction Faction, Track Track) : GameEvent; // left the top step: the key is theirs again
public sealed record FavoursEarned(Faction Faction, int Count) : GameEvent;

// ---------- Coins and levels ----------
public sealed record CoinGained(Faction Faction, int NewCoins) : GameEvent;
public sealed record ShippingUpgraded(Faction Faction, int Level) : GameEvent;
public sealed record CarpetRangeIncreased(Faction Faction, int Range) : GameEvent;

// ---------- Power ----------
public enum PowerSource { Track, NeighbouringBuild, Income, RoundEnd, Town, Stronghold, Cultists, Spades, Shapeshifters, Pyro, Upgrade, Calculator }
public sealed record PowerGained(Faction Faction, PowerSource Source, int Amount, Power Before, Power After) : GameEvent;
public sealed record PowerBurned(Faction Faction, Power After) : GameEvent;
public sealed record PowerOffered(Faction Faction, int Power) : GameEvent;
public sealed record PowerOfferAnswered(Faction Faction, bool Accepted, int Gained, int VictoryPointsPaid) : GameEvent;
public sealed record PowerTokensRemoved(Faction Faction, int Count, Power After) : GameEvent; // gone for the game
public sealed record PowerTokensAdded(Faction Faction, int Bowl, int Count, Power After) : GameEvent;

// ---------- Victory points ----------
// Source: what scored the points; Trigger: what set that off (the view's light runs from both to the VP count).
// Both null: not shown (the Alchemists' trades, which the player makes at the VP count itself).
public sealed record VictoryPointsGained(Faction Faction, int Amount, int NewTotal,
    PointsArea? Source = null, PointsArea? Trigger = null) : GameEvent;

// Where victory points come from or what set them off, for the view: something on the board or on the scoring
// player's panel.
public abstract record PointsArea
{
    public sealed record RoundTile : PointsArea;               // the current round tile
    public sealed record Favour(int Id) : PointsArea;          // that favour on the player's panel
    public sealed record PassTile(int Id) : PointsArea;        // that pass tile (being given back, when passing)
    public sealed record Portrait : PointsArea;                // the player's faction portrait
    public sealed record Shipping : PointsArea;                // the player's shipping area
    public sealed record Spades : PointsArea;                  // the player's spade area
    public sealed record BowlThree : PointsArea;               // the player's power bowl III
    public sealed record OnHex(int Hex) : PointsArea;          // a hex of the map (the building there)
    public sealed record OnBridge(Bridge Bridge) : PointsArea; // a bridge on the map
    public sealed record KeyOn(int Hex) : PointsArea;          // the key token placed on that hex
    public sealed record Landscape(int Hex) : PointsArea;      // a special landscape on the map
}

// ---------- Faction abilities ----------
public sealed record TownBonusOwed(Faction Faction, int Hex) : GameEvent;                 // Witches' Raven's Blunder
public sealed record TreasureGained(Faction Faction, int Treasures) : GameEvent;           // Goblins: the new count
public sealed record IncomeTrackAdvanced(Faction Faction, int Step) : GameEvent;          // Chash Dallah
public sealed record SafeOpened(Faction Faction, int Coins, int Workers, int Priests) : GameEvent; // Treasurers: doubled, received
public sealed record BridgeMoved(Bridge From, Bridge To) : GameEvent;                     // Architects
public sealed record FavourReturned(Faction Faction, FavourTile Tile, int StepsBefore, int StepsAfter) : GameEvent; // Conspirators
public sealed record FreeDwellingOffered(Faction Faction) : GameEvent;                     // Wisps' stronghold
public sealed record IceDwellingsOffered(Faction Faction, int Masses) : GameEvent;          // Snow Shamans' stronghold
public sealed record ChangelingTerrainsChosen(Faction Faction, Terrain B, Terrain C) : GameEvent;
public sealed record LavaOwed(Faction Faction, int Count, bool AnyStructure) : GameEvent;  // Kingdom of Ember
public sealed record LavaPlaced(Faction Faction, int Hex, int Lava) : GameEvent;            // the hex's lava now
public sealed record LavaFlowed(Faction Faction, int From, ValueList<int> Path, ValueList<int> Left) : GameEvent; // Left: hexes left Volcano
public sealed record PyroMoved(Faction Faction, int From, int To) : GameEvent;              // Firewalkers
public sealed record RockMoved(Faction Faction, Terrain Terrain, bool Placed) : GameEvent;  // Geologists
public sealed record RocksCleared(Faction Faction) : GameEvent;                            // all seven covered: all off
public sealed record RockMovesOwed(Faction Faction, int Count) : GameEvent;
public sealed record RiverTokensPlaced(Faction Faction, ValueList<int> Hexes, Power After) : GameEvent; // Children of the Wyrm
public sealed record StartingTownOwed(Faction Faction) : GameEvent;                         // Atlanteans
public sealed record AtlanteansTownReward(Faction Faction, int Power) : GameEvent;          // the threshold reached
public sealed record FreeUpgradeOwed(Faction Faction) : GameEvent;                          // Snow Shamans, after passing
