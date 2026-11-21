namespace Core;

// A move is pure data describing an intention. It does nothing by itself.
public abstract record Move;

// ----- Setup, in order: game, factions, turn order, map, round tiles, pass tiles, Start -----
// Options: what the game includes (null: everything, GameOptions.All).
public sealed record ChooseGame(GameKind Game, GameOptions? Options = null) : Move;
public sealed record AddPlayer(Faction Faction) : Move;
public sealed record RemovePlayer(Faction Faction) : Move;
// Ice and variable factions, straight after being chosen (then the Ice Maidens take a favour, TakeFavour); volcano
// factions once every faction is chosen (ChoosingVolcanoTerrain). A basic terrain no faction in the game has taken.
public sealed record ChooseStartingTerrain(Faction Player, Terrain Terrain) : Move;
// "Proceed": the chosen factions are final; turn order is chosen next.
public sealed record ConfirmFactions() : Move;
// That faction takes the next place in the turn order. When one faction is left, it is placed automatically.
public sealed record ChooseTurnOrder(Faction Faction) : Move;
// That faction loses its place, and so does every faction placed after it.
public sealed record UnchooseTurnOrder(Faction Faction) : Move;
// "Proceed": the turn order is final; the map is chosen next.
public sealed record ConfirmTurnOrder() : Move;
// While the turn order is chosen: a faction's starting VP up or down (auctions, variants); never below 0. Until Start
// the faction's VictoryPoints holds the difference from Config.StartingVictoryPoints (Rules.StartingVictoryPoints).
public sealed record AdjustStartingVictoryPoints(Faction Player, int Delta) : Move;
public sealed record ChooseMap(MapId Map) : Move;
// A map made in the custom map editor (MapId.Custom): one letter per hex, rows separated by '/' (Maps.Rows). Nine rows,
// alternating 13 and 12 hexes (Maps.CustomLayout); river and the seven wheel terrains only, at least
// Config.CustomMapMinimumHexes of each of those seven.
public sealed record ChooseCustomMap(string Rows) : Move;
// That tile becomes the next round's tile (round 1 first).
public sealed record ChooseRoundTile(int Tile) : Move;
// That tile loses its round, and so does every tile chosen after it.
public sealed record UnchooseRoundTile(int Tile) : Move;
// "Proceed": all six round tiles are chosen; pass tiles are chosen next.
public sealed record ConfirmRoundTiles() : Move;
// Pass tiles in or out of this game.
public sealed record AddPassTile(int Tile) : Move;
public sealed record RemovePassTile(int Tile) : Move;
// "Start": enough pass tiles are in the game; starting resources are paid and starting dwellings are placed.
public sealed record StartGame() : Move;
// A starting dwelling, placed free on the faction's starting terrain. The next placement follows at once.
public sealed record PlaceStartingDwelling(Piece Dwelling, int Hex) : Move;
// Takes a pass tile from the bank: after the starting dwellings (each player, in reverse turn order),
// and after passing in rounds 1-5 (swapping it for the player's own).
public sealed record PickPassTile(Faction Player, int Tile) : Move;

// ----- Actions: one per turn. The turn continues until EndTurn. -----
// Transform the hex to the builder's home terrain if needed, and build on it, as one action.
public sealed record BuildDwelling(Piece Dwelling, int Hex) : Move;
// Replace one of the player's buildings on the map with the leftmost building of the next kind up:
// dwelling -> trading post; trading post -> temple or stronghold; temple -> sanctuary.
// The kind of Building says which upgrade it is. The replaced building goes back to the rightmost
// empty slot of its own bank.
// Changelings with their stronghold, upgrading a dwelling: ReturnBesideStronghold puts it back in the empty spot beside
// their stronghold instead of the dwellings' row.
public sealed record UpgradeBuilding(Piece Building, int Hex, bool ReturnBesideStronghold = false) : Move;
// Transform a hex to any basic terrain, paying workers for each spade (see Terrains.Spades). Made home terrain,
// a dwelling may then be built there (BuildOnSpadedHex) or given up (FinishSpades).
public sealed record TransformTerrain(Faction Player, int Hex, Terrain To) : Move;
public sealed record PlacePriest(Piece Priest, Track Track, int Slot) : Move;
// Passing is the action: the player is out for the rest of the round, but may still convert before EndTurn.
// In rounds 1-5 the player then picks a new pass tile from the bank (PickPassTile) before ending the turn,
// or names it here (PassTile) to pass and swap in one move. Null in the last round.
// KeepTile: a calculator shortcut (rounds 1-5): no new tile is taken, the player keeps theirs, with a warning.
public sealed record Pass(Faction Player, int? PassTile = null, bool KeepTile = false) : Move;

// ----- Bonus actions: each is the player's action for the turn, once per round, once unlocked -----
// Source says which octagon is used when the same action is unlocked twice.
public sealed record BonusCultStep(Faction Player, BonusSource Source, Track Track) : Move;
// Take the free spades of a spade bonus action; they are then spent with SpendSpades.
public sealed record BonusSpades(Faction Player, BonusSource Source) : Move;
// Build the leftmost dwelling for free on any empty hex of home terrain, whether in range or not.
public sealed record BonusBuild(Faction Player, BonusSource Source, int Hex) : Move;
// Upgrade one of the player's dwellings to their leftmost trading post for free.
public sealed record BonusTradingPost(Faction Player, BonusSource Source, int Hex) : Move;
// Chaos Magicians: two full actions follow, one after the other (passing may be the second).
public sealed record BonusDoubleAction(Faction Player, BonusSource Source) : Move;
// Nomads: a hex next to one of their buildings becomes Desert (no spades); a dwelling may then be built there
// (BuildOnSpadedHex, paid), or Done (FinishSpades).
public sealed record BonusSandstorm(Faction Player, BonusSource Source, int Hex) : Move;
// Shapeshifters: their home terrain moves to To (a basic terrain that is no faction's home), for 5 power spent
// (burning first if needed) or, with PayWithTokens, 5 power tokens removed for the game (lowest bowls first).
public sealed record BonusShapeshift(Faction Player, BonusSource Source, Terrain To, bool PayWithTokens) : Move;

// Engineers' book action (any number of times, each the turn's action): 2 workers for a bridge, placed at once.
// Also the Architects' (1 priest) and the Atlanteans' (2 workers).
public sealed record BuildBridge(Faction Player) : Move;

// Upgrades, each the turn's action: shipping (+1, 1 priest and coins) and spades (cheaper spades).
public sealed record UpgradeShipping(Faction Player) : Move;
public sealed record UpgradeSpades(Faction Player) : Move;

// A special power action (see PowerActions), the turn's action. Power is burned first if needed.
// PayWithCoins: Chash Dallah with their stronghold pay the power in coins instead (all of it).
public sealed record TakePowerAction(Faction Player, int Action, bool PayWithCoins = false) : Move;

// ----- Straight after something that gives them -----
// Place a bridge (from the bridge power action) between two land hexes; see Rules.BridgeSpots.
public sealed record PlaceBridge(Faction Player, int HexA, int HexB) : Move;
// Riverwalkers' stronghold: leave the free bridges still owed unplaced.
public sealed record SkipBridges(Faction Player) : Move;
// Form a town: take the top key token of this kind and place it on a hex of a town that can be formed.
// The Witches with Raven's Blunder placed then choose their town bonus (ChooseTownBonus).
public sealed record FormTown(Faction Player, int Token, int Hex) : Move;
// The player with the first power offer gains the power (paying VP), or declines it.
public sealed record AcceptPower(Faction Player) : Move;
public sealed record DeclinePower(Faction Player) : Move;

// ----- Spending free spades: at once, after a spade bonus action or between rounds -----
// Turn a hex into any basic terrain, using free spades first; in the main phase, the rest may be paid for.
public sealed record SpendSpades(Faction Player, int Hex, Terrain To) : Move;
// Main phase only: build the leftmost dwelling (paid) on one hex just turned into home terrain (free spades,
// TransformTerrain or the sandstorm).
// This ends the spending; spades left over are lost.
public sealed record BuildOnSpadedHex(Piece Dwelling, int Hex) : Move;
// Done: stop spending; spades left over are lost.
public sealed record FinishSpades(Faction Player) : Move;

// ----- Free actions: any time during the player's own turn, before or after the action -----
// BurnFirst (the power conversions): burn first just as much as is needed for enough power in bowl 3.
public sealed record ConvertPowerToCoin(Faction Player, bool BurnFirst = false) : Move;   // spend 1 power: 1 coin
public sealed record ConvertPowerToWorker(Faction Player, bool BurnFirst = false) : Move; // spend 3 power: 1 worker
public sealed record ConvertPriestToWorker(Faction Player) : Move; // top ready priest back to the supply: 1 worker
public sealed record ConvertWorkerToCoin(Faction Player) : Move;   // 1 worker: 1 coin (may go into debt)
public sealed record ConvertPowerToPriest(Faction Player, bool BurnFirst = false) : Move; // spend 5 power: 1 priest
public sealed record BurnPower(Faction Player) : Move;             // 2 tokens from bowl 2: 1 to bowl 3, 1 gone

// Alchemists' free actions: 1 victory point for 1 coin, and 2 coins for 1 victory point.
public sealed record ConvertVictoryPointToCoin(Faction Player) : Move;
public sealed record ConvertCoinsToVictoryPoint(Faction Player) : Move;

// ----- Owed after a build, during the player's own turn -----
// Cultists: an opponent took the power from their build, so they move up a track of their choice.
public sealed record TakeCultStep(Faction Player, Track Track) : Move;
// Darklings, the turn their stronghold is built: exchange workers for priests, up to 3 in all (one at a time or
// several at once). Those not exchanged by the end of the turn are lost.
public sealed record ExchangeWorkersForPriests(Faction Player, int Workers) : Move;
// Darklings: done exchanging for this turn; the exchanges not made are given up now (as at the end of the turn).
public sealed record FinishPriestExchange(Faction Player) : Move;
// Acolytes, after making a hex Volcano: pay the steps owed from this track (it must have that many).
public sealed record PayCultSteps(Faction Player, Track Track) : Move;
// Shapeshifters, after an opponent took the power from their build: pay 1 VP for a new token in bowl 3, or not.
public sealed record AnswerTokenOffer(Faction Player, bool Accept) : Move;
// ----- Fan factions (GameOptions.FanFactions) -----
// Book actions, each the turn's action, any number of times in a round:
// Djinni: a lamp spent, two of their trackers change tracks (each keeping its step); no power for it. Neither may arrive
// on a top step someone else holds.
public sealed record SwapTracks(Faction Player, Track A, Track B) : Move;
// Goblins: a treasure spent for a reward by the number of their buildings of a kind on the map: per dwelling 1 power,
// per trading post 2 coins, per temple 1 worker; Stronghold (or Sanctuary): a step for the stronghold and two for the
// sanctuary (each a step on a track of their choice: TakeCultStep).
public sealed record SpendTreasure(Faction Player, PieceKind Kind) : Move;
// Chash Dallah: their income track up a step, for 2 workers and 2 coins, scoring that step's VP.
public sealed record AdvanceIncomeTrack(Faction Player) : Move;
// Stronghold actions (each once a round, the turn's action):
// Architects: one of their bridges moved to another spot a bridge could go (+3 VP); every town must still stand.
public sealed record BonusMoveBridge(Faction Player, BonusSource Source, int FromA, int FromB, int ToA, int ToB) : Move;
// Time Travellers: up to 4 power tokens from bowl 1 to bowl 3.
public sealed record BonusTokensToBowlThree(Faction Player, BonusSource Source, int Count) : Move;
// Prospectors: a coin for every opponent's trading post on the map.
public sealed record BonusCoinsForTradingPosts(Faction Player, BonusSource Source) : Move;
// Enlightened: 4 power.
public sealed record BonusFourPower(Faction Player, BonusSource Source) : Move;
// Conspirators: a favour handed back (its steps lost, no power changes) for another (its steps, power and coins).
public sealed record BonusSwapFavour(Faction Player, BonusSource Source, int Return, int Take) : Move;
// Free actions and choices:
// Enlightened: 1 coin for a new power token in bowl 1 (any time in their turn).
public sealed record BuyPowerToken(Faction Player) : Move;
// Treasurers: some of what they just received (GameState.TreasuryOffer) into their Safe, doubled next round.
public sealed record StoreInTreasury(Faction Player, int Coins, int Workers, int Priests) : Move;
// Treasurers: what of the offer is going into their Safe so far (the whole choice, not a change), before they settle it
// with StoreInTreasury. Repeated choices and the settling merge into one record (Rules.MergeKey).
public sealed record ChooseForSafe(Faction Player, int Coins, int Workers, int Priests) : Move;
// Djinni: their starting steps (2) on this track, once the starting buildings are placed.
public sealed record TakeStartingSteps(Faction Player, Track Track) : Move;
// Wisps' stronghold: the leftmost dwelling, free, on any empty Lake hex (range doesn't matter). Snow Shamans' stronghold:
// one on each mass of Ice offered (any empty Ice hex of it).
public sealed record PlaceFreeDwelling(Piece Dwelling, int Hex) : Move;
// Wisps: their stronghold's free Lake dwelling given up (the tick). Snow Shamans: those left on their masses of Ice.
public sealed record SkipFreeDwelling(Faction Player) : Move;
// Batches 2 and 3:
// Changelings, once every faction (and any volcano faction's starting terrain) is chosen, before Start: their B and C
// terrains (basic, not A, nobody else's home or starting terrain).
public sealed record ChooseChangelingTerrains(Faction Player, Terrain B, Terrain C) : Move;
// Kingdom of Ember: a lava tile owed (a spade, an upgrade, the stronghold) placed under this structure of theirs (a
// dwelling, unless it is the stronghold's).
public sealed record PlaceLava(Faction Player, int Hex) : Move;
// Kingdom of Ember, the turn's action: the structure on From (with lava, no key) flows along Path (each hex next to
// the one before), leaving a tile on each land hex it leaves; it stops on the last. A river hex crossed may then
// take a free bridge (BridgeA-BridgeB, across it; no structure of theirs needed at either end).
public sealed record LetLavaFlow(Faction Player, int From, ValueList<int> Path, int? BridgeA = null, int? BridgeB = null) : Move;
// Firewalkers, any time in their turn: the pyro token 1 forward (never past their VP) for 1 power.
public sealed record AdvancePyro(Faction Player) : Move;
// Geologists, for a spade gained: a rock onto this terrain (uncovered, their ring's or next to a covered one) or off it
// (covered, next to an uncovered one). FinishSpades gives the spade up.
public sealed record MoveRock(Faction Player, Terrain Terrain) : Move;
// Geologists' stronghold action: the same, once a round.
public sealed record BonusMoveRock(Faction Player, BonusSource Source, Terrain Terrain) : Move;
// Children of the Wyrm's stronghold action: power tokens (bowl 1 first) onto one or two river hexes, each next to one
// of their structures or tokens.
public sealed record BonusRiverTokens(Faction Player, BonusSource Source, int? HexA, int? HexB = null) : Move; // none: the action used for nothing
// Selkies' stronghold action: this dwelling built on this hex with their shipping 1 more and 1 spade free.
public sealed record BonusSelkiesBuild(Piece Dwelling, BonusSource Source, int Hex) : Move;

// ----- Special landscapes (GameOptions.Landscapes) -----
// The faction's special landscape onto this hex (one per faction, once its prerequisite stands on the map; see
// Landscapes). It is laid instead of a terrain tile when a hex is transformed into the faction's building terrain, in
// the main phase only:
//   as the turn's action: the transform paid as usual (spades; the Dragonlords' tokens), the Acolytes always 3 steps
//     from one track (PayCultSteps), a carpet flight or tunnel as for a transform;
//   with free spades being spent (main phase): as SpendSpades, the spades free first;
//   on a hex just transformed into that terrain (a transform, free spades or the sandstorm, open to a dwelling).
// Mermaids and Riverwalkers: a river hex next to one of their structures, for 1 spade (Mermaids) or 1 priest
// (Riverwalkers), as the action (the Mermaids' spade may be a free one). Chaos Magicians: any free Wasteland hex, for
// nothing, only when offered as their stronghold is built (LandscapeOffer.Portal).
public sealed record PlaceLandscape(Faction Player, int Hex) : Move;
// The landscape's offers not taken are given up (PlayerState.LandscapeOffers). The Alchemists take theirs with
// UpgradeShipping / UpgradeSpades, the Chaos Magicians with PlaceLandscape.
public sealed record DeclineLandscapeBonus(Faction Player) : Move;
// Shapeshifters, once their Spectral Polestar is placed: their home terrain moves for free (as BonusShapeshift).
public sealed record LandscapeShapeshift(Faction Player, Terrain To) : Move;
// Witches with Raven's Blunder, after forming a town: their 5 VP (Track null), or 3 steps on this track instead.
public sealed record ChooseTownBonus(Faction Player, Track? Track) : Move;
// Nomads: a power offer from a build next to their Trading Camp answered with exactly 1 coin instead (no VP paid).
public sealed record TakeCoinInstead(Faction Player) : Move;

// Riverwalkers, for each priest gained: take it (Unlock null), or instead move the priest on this terrain of their
// wheel to their supply, unlocking the terrain (1 coin, 2 for another faction's home terrain). Out of turn too.
public sealed record ChoosePriest(Faction Player, Terrain? Unlock) : Move;

// ----- Choosing a favour: any time during the player's own turn while a favour is owed -----
// The top tile of that favour moves to the player, who gains its track steps at once.
public sealed record TakeFavour(Faction Player, int Favour) : Move;

// ----- Ending the turn: allowed once the action has been taken and every favour owed is chosen -----
public sealed record EndTurn(Faction Player) : Move;


// ----- Calculator: the neutral opponent's pieces, at any time once the game has started; nothing else changes -----
public sealed record PlaceNeutralDwelling(int Hex) : Move;          // on any empty land hex
public sealed record RemoveNeutralDwelling(int Hex) : Move;
public sealed record PlaceNeutralBridge(int HexA, int HexB) : Move; // on any free bridge spot
public sealed record RemoveNeutralBridge(int HexA, int HexB) : Move;
public sealed record PlaceNeutralPriest(Track Track, int Slot) : Move; // in any free priest slot (no steps)
public sealed record RemoveNeutralPriest(Track Track, int Slot) : Move;
// One coin, worker, priest (the next of the supply; a priest owed is paid back first) or power (bowl I → II → III)
// given to the faction, at any time once play has begun. Not an action; repeated clicks merge (Rules.MergeKey).
public sealed record CalculatorGain(Faction Player, GainKind Kind) : Move;
// A coin taken off this pass tile in the bank (right-click). Repeated clicks merge (Rules.MergeKey).
public sealed record TakePassTileCoin(int Tile) : Move;

// ----- Play modes (see PlayMode) -----
// Into Strict: refused while anyone is in debt or below 0 VP. Into Free: Free play begins (from round 1 on; before it,
// it waits for round 1 to begin). Out of Free: the checks of FinishSetup first; then, if a round was advanced in Free,
// the turn-order step (OrderingTurns), otherwise the turn goes back to the faction whose turn it was when Free began.
// While a game in progress is set up, only Strict and Lenient (the mode for afterwards).
public sealed record SetMode(PlayMode Mode) : Move;
// Setup and Free: the faction the board's tools (Setup) or the actions (Free) are for. In Free it becomes the active
// player, starting afresh (the turn order stays as it is); the faction selected before must have finished its action.
public sealed record SelectFaction(Faction Faction) : Move;
// Setup and Free: one priest of the faction's priest debt is cancelled.
public sealed record CancelPriestDebt(Faction Player) : Move;

// ----- Setting up a game in progress (Phase.SettingUp) -----
// Every value is set directly: nothing costs, and nothing triggers anything else, except that priests in zones and
// favours carry their cult steps, keys on the map give their faction a key, and towns and keys are kept consistent
// (a group that stops being a town loses its keys; see Rules.KeysFit). Repeated edits of one value merge in History.
// VP, coins or workers up or down. Only VP may go below 0. (Keys follow from the key tokens placed.)
public sealed record EditResource(Faction Player, Resource Resource, int Delta) : Move;
// A new power token in ToBowl (FromBowl null), a token moved between bowls, or one removed for the game (ToBowl null).
public sealed record EditPowerToken(Faction Player, int? FromBowl, int? ToBowl) : Move;
// The next priest of the supply into the hand.
public sealed record EditGainPriest(Faction Player) : Move;
// Treasurers: their Safe's coins, workers and priests up or down (never below 0); priests come from the supply and go
// back to it.
public sealed record EditSafe(Faction Player, int Coins, int Workers, int Priests) : Move;
// A priest in the hand or in a priest zone back to the supply (from a zone: its slot's steps are taken back).
public sealed record EditReturnPriest(Piece Priest) : Move;
// A priest from the supply or the hand into a free slot that keeps its priest, with that slot's steps (no power).
public sealed record EditPlacePriest(Piece Priest, Track Track, int Slot) : Move;
// One step up or down a track, no power. Step 10 needs a key, which it uses, and only one faction per track may be
// there; leaving it gives the key back.
public sealed record EditTrack(Faction Player, Track Track, int Delta) : Move;
// Take a favour from the bank, with its steps (no duplicates, no more than the buildings give: Rules.FavoursDue), or
// return one (its steps taken back).
public sealed record EditFavour(Faction Player, int Favour, bool Take) : Move;
// Shipping or spade level up or down, within the faction's limits, no VP.
public sealed record EditLevel(Faction Player, Level Level, int Delta) : Move;
// A bridge on a free bridge spot (at most 3 each), or taken back.
public sealed record EditBridge(Faction Player, int HexA, int HexB, bool Place) : Move;
// A building to a land hex (Hex), or back to its board (Hex null). From the board: the leftmost of its kind. From the
// map: moved. A building already on the hex goes back to its owner's board. The hex becomes the faction's building
// terrain (the Riverwalkers' keeps its terrain, which is unlocked on their wheel, and must be beside a river).
// Terrain: the Shapeshifters and Riverwalkers only, the terrain the hex becomes (Shapeshifters: any not another
// faction's home or starting terrain) or is unlocked as (Riverwalkers).
public sealed record EditBuilding(Piece Building, int? Hex, Terrain? Terrain = null) : Move;
// Setup: the faction's special landscape onto a hex (Hex), or off the map (Hex null). Land: the hex becomes the faction's
// building terrain (kept for the Shapeshifters and Riverwalkers' river landscapes); the Mermaids' and Riverwalkers' go
// on a river hex. A building already there goes back to its board. No prerequisite is checked (see SetupIssues).
public sealed record EditLandscape(Faction Player, int? Hex) : Move;
// An empty land hex becomes a basic terrain (Ice or Volcano only with an ice or volcano faction in the game).
public sealed record EditTerrain(int Hex, Terrain To) : Move;
// The top key of that kind placed on a hex of the faction's (a building, or the Mermaids' river hex), giving the
// faction its keys. A group may hold more than one key only as Rules.KeysFit allows.
public sealed record EditPlaceKey(Faction Player, int Token, int Hex) : Move;
// The key on that hex back to the bank; its faction loses its keys.
public sealed record EditRemoveKey(int Hex) : Move;
// The round being played (1 to 6).
public sealed record SetRound(int Round) : Move;
// The faction's pass tile swapped for that bank tile (the coins stay on the tiles).
public sealed record EditPassTile(Faction Player, int Tile) : Move;
// Coins on a bank tile up or down.
public sealed record EditPassTileCoins(int Tile, int Delta) : Move;
// The faction marked passed (last in the pass order) or not.
public sealed record MarkPassed(Faction Player, bool Passed) : Move;
// A power action marked taken this round, or not.
public sealed record TogglePowerAction(int Action) : Move;
// A bonus action marked used this round, or not. The faction must have it.
public sealed record ToggleBonusAction(Faction Player, BonusActionId Bonus) : Move;
// Riverwalkers: a terrain of their wheel locked (a supply priest put on it) or unlocked (its priest to the supply).
public sealed record EditWheel(Faction Player, Terrain Terrain) : Move;
// Shapeshifters: their home terrain (any not another faction's home or starting terrain).
public sealed record EditHome(Faction Player, Terrain Terrain) : Move;
// Fan factions: a lava tile under one of the Kingdom of Ember's structures; a Wyrm power token from a bowl onto a river
// hex; a Geologists' rock onto (or off) a terrain, as the rules allow (a seventh clears them all).
public sealed record EditLava(int Hex) : Move;
public sealed record EditWyrmToken(Faction Player, int FromBowl, int Hex) : Move;
public sealed record EditRock(Faction Player, Terrain Terrain) : Move;
// Done: every check of Rules.SetupIssues must pass. Then the turn-order step (or, if everyone has passed, the round
// ends).
public sealed record FinishSetup() : Move;
