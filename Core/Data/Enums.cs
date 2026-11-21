namespace Core;

// Every faction (Factions.All has what each one is like): the base game's, two per home terrain, then Fire & Ice's:
// two ice, two volcano and two variable factions; then the fan factions (GameOptions.FanFactions): two per base home
// terrain, then two ice, two volcano and two variable. New ones go at the end (the view keys pictures by them).
public enum Faction
{
    Auren, Witches, Giants, ChaosMagicians, Nomads, Fakirs, Halflings, Cultists,
    Alchemists, Darklings, Mermaids, Swarmlings, Dwarves, Engineers,
    IceMaidens, Yetis, Dragonlords, Acolytes, Riverwalkers, Shapeshifters,
    Architects, Treasurers, Djinni, Archivists, TimeTravellers, Prospectors, Goblins, ChildrenOfTheWyrm,
    Wisps, Atlanteans, ChashDallah, Enlightened, Conspirators, DynionGeifr,
    Selkies, SnowShamans, Firewalkers, KingdomOfEmber, Geologists, Changelings,
}

// What sort of faction it is (Factions.Kind):
//   Base: a fixed home terrain, its colour;
//   Ice: chooses a starting terrain when chosen; builds on Ice, which takes that terrain's place on its wheel; white;
//   Volcano: chooses a starting terrain once every faction is chosen; builds on Volcano, made without spades; orange;
//   Variable: chooses a starting terrain when chosen, and takes its colour (Riverwalkers, Shapeshifters).
public enum FactionKind { Base, Ice, Volcano, Variable }

// One per basic terrain, in the order of the terrain wheel (see Terrains.Wheel), then the ice and volcano factions'.
public enum PlayerColour { Red, Yellow, Brown, Black, Blue, Green, Grey, White, Orange }

// How a faction reaches a hex it builds on or transforms (see Rules.ReachOf): next to one of its buildings
// (its bridges included), across rivers by shipping, or by skipping hexes (the Fakirs' flying carpet, the
// Dwarves' tunnelling), which costs extra and scores.
public enum Reach { None, Adjacent, Shipping, Carpet, Tunnel }

// Every building kind is listed in Buildings.All; Priest and Landscape are the pieces that are not buildings.
// Landscape: a faction's special landscape (Landscapes expansion; one per faction, see Landscapes): a structure on the
// map with power 1, but none of the building kinds; never upgraded.
public enum PieceKind { Dwelling, TradingPost, Temple, Stronghold, Sanctuary, Priest, Landscape }

// What a special landscape offers its faction once it is placed, taken (or declined) before the turn ends
// (PlayerState.LandscapeOffers): the Alchemists' shipping and spade upgrades without the priest, the Shapeshifters'
// free move of their home terrain; the Chaos Magicians' portal, placed for free when their stronghold is built.
[Flags]
public enum LandscapeOffer { None = 0, Shipping = 1, Spades = 2, Shapeshift = 4, Portal = 8 }

// The four priest tracks. Names are placeholders (TM's cults); rename freely.
public enum Track { Fire, Water, Earth, Air }

// The game of the family being played. Only one so far.
public enum GameKind { TerraMystica }

// Fire & Ice's final scoring tiles (FinalScoring): one or none is chosen with the game (GameOptions.FinalScoring).
public enum FinalScoringTile { StrongholdSanctuary, Distance, Clusters, Edges }

// The maps that can be chosen (see Maps.ById). Append new ones at the end.
public enum MapId { Original, OriginalV2, FireAndIce, Fjords, Lakes, Archipelago, Custom } // Custom: ChooseCustomMap

// TODO: these setup phases are not in TM's traditional order (there the map, round tiles and pass tiles
// are laid out before factions are chosen). The current order favours the calculator use case
// (less friction); revisit.
// In the order they happen. Everything up to and including ChoosingPassTiles is setup (before Start).
// After Start: starting dwellings, then each player picks a pass tile (reverse turn order), then the rounds.
// Between rounds, players given spades by the round tile spend them (RoundEndSpades).
// Setting up a game in progress (GameOptions.SetUpInProgress): Start leads to SettingUp instead of the starting
// dwellings, then (FinishSetup) to OrderingTurns, then Playing in the round set up. OrderingTurns also follows
// leaving Free play once a round has been advanced in it.
public enum Phase
{
    ChoosingGame,
    ChoosingFactions,
    ChoosingVolcanoTerrain, // only with a volcano faction (its starting terrain) or the Changelings (B and C) in the game
    ChoosingTurnOrder,
    ChoosingMap,
    ChoosingRoundTiles,
    ChoosingPassTiles,
    PlacingDwellings,
    PickingPassTiles,
    SettingUp,      // a game in progress is being recorded: edit moves only (see Rules.EditPosition.cs), until FinishSetup
    OrderingTurns,  // the factions still in the round are clicked into turn order (ChooseTurnOrder), then play goes on
    Playing,
    RoundEndSpades,
    Finished,
}

// Extra actions a player can unlock. Using one is the player's action for the turn; each can be used
// once per round. Unlocked by a stronghold (Factions.StrongholdAction), a pass tile (PassTiles.All)
// or a favour (Favours.All). New ones go at the end (the view keys pictures by them).
public enum BonusAction
{
    CultStep,           // one step up any track
    OneSpade,           // one free spade, spent at once (see Rules.SpendSpades); a dwelling may then be built (paid)
    TwoSpades,          // the same with two free spades
    BuildOnHomeTerrain, // build a dwelling for free on any empty hex of home terrain, in range or not
    FreeTradingPost,    // upgrade any own dwelling to a trading post for free
    TwoCultSteps,       // two steps up one track (the Auren's stronghold)
    DoubleAction,       // two full actions in a row (the Chaos Magicians' stronghold)
    Sandstorm,          // a hex next to one of your buildings becomes Desert, no spades (the Nomads' stronghold)
    Shapeshift,         // move the home terrain to another terrain, for 5 power or 5 power tokens (the Shapeshifters' stronghold)
    // Fan factions' strongholds:
    MoveBridge,         // move one of their bridges to another spot, +3 VP (Architects)
    TokensToBowlThree,  // up to 4 power tokens from bowl 1 to bowl 3 (Time Travellers)
    CoinsForTradingPosts, // a coin for every opponent's trading post on the map (Prospectors)
    FourPower,          // gain 4 power (Enlightened)
    SwapFavour,         // hand back a favour for another (Conspirators)
    RiverTokens,        // up to 2 power tokens onto river hexes (Children of the Wyrm)
    SelkiesSpade,       // a transform (and build) with 1 free spade, shipping +1 for it (Selkies)
    MoveRock,           // a glowing rock placed or removed, as for a spade (Geologists)
}

// Where a bonus action comes from. The same action from two sources is two separate actions
// (e.g. the Giants' stronghold spades and pass tile 1's spade can both be used in one round).
public enum BonusSource { Stronghold, PassTile, Favour }

// One usable bonus action: one octagon on the player's panel.
public readonly record struct BonusActionId(BonusSource Source, BonusAction Action);

public enum Terrain { Wasteland, Desert, Plains, Swamp, Lake, Forest, Mountain, Ice, Volcano, River }

// How strictly the rules are applied (GameState.Mode; changed with SetMode):
//   Strict: by the rules, and any move that leaves a player in debt (coins, workers, priests) or below 0 VP is refused;
//   Lenient: by the rules, but debt and negative VP are allowed, with a warning;
//   Free: planning during play: the selected faction takes any number of normal actions, with normal costs and
//     rewards, without the turn order (GameState.Planning). Before round 1 it waits for round 1 to begin.
public enum PlayMode { Strict, Lenient, Free }

// The resources Setup edits directly (EditResource). Keys are not among them: they come from the key tokens placed.
public enum Resource { VictoryPoints, Coins, Workers }

// What Calculator's +1 panel gives (CalculatorGain).
public enum GainKind { Coin, Worker, Priest, Power }

// The two levels Setup edits directly (EditLevel).
public enum Level { Shipping, Spades }
