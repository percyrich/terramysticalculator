using System.Collections.Immutable;

namespace Core;

// Every fixed number in the rules, in one place.
public static class Config
{
    public const int Rounds = 6;
    public const int CustomMapMinimumHexes = 3; // a custom map: at least this many hexes of each of the seven terrains
    public const int MaxPlayers = 5;

    // The usual values printed on the faction boards. Factions.All says where a faction differs.
    public const int StartingCoins = 15;  // received when Start is pressed, before starting dwellings
    public const int StartingWorkers = 3;
    public static readonly Cost DwellingCost = new(Coins: 2, Workers: 1);   // starting dwellings are free
    public static readonly Cost TradingPostCost = new(Coins: 3, Workers: 2);
    // Extra coins for a trading post when no opponent's building is next to the hex: for the usual cost.
    // A faction's own surcharge is its trading post's coins again (Factions.LonelyTradingPostSurcharge).
    public const int LonelyTradingPostSurcharge = 3;
    public static readonly Cost TempleCost = new(Coins: 5, Workers: 2);      // no surcharge, wherever it is
    public static readonly Cost StrongholdCost = new(Coins: 6, Workers: 4);
    public static readonly Cost SanctuaryCost = new(Coins: 6, Workers: 4);
    public static readonly Power StartingPower = new(5, 7, 0);

    // Power conversions (free actions): spend this much power for one coin / one priest.
    public const int PowerPerCoin = 1;
    public const int PowerPerWorker = 3;
    public const int PowerPerPriest = 5;
    public const int StartingVictoryPoints = 20; // received when Start is pressed, like the starting coins

    public const int DwellingsPerPlayer = 8;
    public const int TradingPostsPerPlayer = 4;
    public const int TemplesPerPlayer = 3;
    public const int StrongholdsPerPlayer = 1;
    public const int SanctuariesPerPlayer = 1;
    public const int PriestsPerPlayer = 7;   // each player's supply; a priest must be gained before use

    public const int TrackCount = 4;      // one per Track value
    public const int TrackMax = 10;
    // The slots of every priest zone, in order. Slot 0 is the top left.
    // A priest in a returning slot goes back to its owner's supply when that turn ends;
    // in any other slot it stays for the rest of the game.
    public static readonly ImmutableArray<PriestSlotRule> PriestSlots = ImmutableArray.Create(
        new PriestSlotRule(Steps: 3, ReturnsAtEndOfTurn: false),
        new PriestSlotRule(Steps: 2, ReturnsAtEndOfTurn: false),
        new PriestSlotRule(Steps: 2, ReturnsAtEndOfTurn: false),
        new PriestSlotRule(Steps: 2, ReturnsAtEndOfTurn: false),
        new PriestSlotRule(Steps: 1, ReturnsAtEndOfTurn: true));

    public static int PriestSlotsPerZone => PriestSlots.Length;

    // Power gained on reaching these steps of a track (each at most once per track: steps never go down).
    // The top step needs a key (see Rules.ClimbTrack).
    public static readonly ImmutableArray<(int Step, int Power)> TrackPowerRewards =
        ImmutableArray.Create((3, 1), (5, 2), (7, 2), (10, 3));

    // Towns: a group of buildings (joined by adjacency or the owner's bridges, not by shipping) with
    // at least this much power and this many buildings (fewer if it contains the sanctuary).
    public const int TownPower = 7;
    public const int TownBuildings = 4;
    public const int TownBuildingsWithSanctuary = 3;

    public const int BridgesPerPlayer = 3;

    // Power tokens a faction can have (checked when a game in progress is set up), except the Dragonlords and the
    // Shapeshifters, who gain more.
    public const int PowerTokenCap = 12;

    // Setup: at least this many more pass tiles than players must be in the game.
    public const int ExtraPassTiles = 3;
    // Coins added to every pass tile left in the bank after the setup pick and at the end of rounds 1-5.
    public const int PassTileCoinsPerRound = 1;

    // Spades: one spade moves a hex one step around the terrain wheel (see Terrains; the Giants always need 2).
    public const int WorkersPerSpade = 3;                              // before any spade upgrade
    public static readonly Cost SpadeUpgradeCost = new(Coins: 5, Workers: 2); // plus 1 priest
    public const int SpadeUpgradeVictoryPoints = 6;

    // Shipping upgrades cost 1 priest plus coins (Factions.ShippingUpgradeCost). The n-th upgrade scores n + 1 VP.
    public static int ShippingUpgradeVictoryPoints(int upgradesBefore) => upgradesBefore + 2;

    // Favours earned for building a temple or a sanctuary (by upgrading). The Chaos Magicians earn 2.
    public const int FavoursPerTemple = 1;
    public const int FavoursPerSanctuary = 1;

    // ---------- Faction abilities ----------
    public const int GiantsSpadesPerTransform = 2;        // whatever the two terrains
    public const int AurenStrongholdFavours = 1;
    public const int HalflingsStrongholdSpades = 3;
    public const int HalflingsVictoryPointsPerSpade = 1;
    public const int CultistsStrongholdVictoryPoints = 7;
    public const int CultistsPowerWhenDeclined = 1;       // every opponent declined the power from their build
    public const int AlchemistsStrongholdPower = 12;
    public const int AlchemistsPowerPerSpade = 2;         // once their stronghold is built
    public const int AlchemistsCoinsPerVictoryPoint = 2;  // bought as a free action; 1 VP sells for 1 coin
    public const int DarklingsPriestsPerSpade = 1;
    public const int DarklingsVictoryPointsPerPriestSpade = 2;
    public const int DarklingsPriestExchange = 3;         // at most this many workers, once, when the stronghold is built
    public const int FakirsCarpetRange = 1;               // hexes skipped; +1 with the stronghold, +1 per shipping key
    public const int FakirsCarpetPriests = 1;
    public const int CarpetVictoryPoints = 4;
    public const int DwarvesTunnelWorkers = 2;
    public const int DwarvesTunnelWorkersWithStronghold = 1;
    public const int TunnelVictoryPoints = 4;
    public const int EngineersBridgeWorkers = 2;          // their book action: a bridge
    public const int EngineersVictoryPointsPerBridge = 3; // when passing, with the stronghold: per bridge joining two of their buildings
    public const int WitchesTownVictoryPoints = 5;
    public const int SwarmlingsTownWorkers = 3;

    // ---------- Fire & Ice faction abilities ----------
    public const int IceSpadesFromStartingTerrain = 1;     // an ice faction turning its own starting terrain into Ice
    public const int IceMaidensVictoryPointsPerTemple = 3; // when passing, with the stronghold: per temple on the map
    public const int YetisPowerActionDiscount = 1;         // every power action costs this much less power
    public const int YetisBigBuildingPower = 4;            // the power value of their stronghold and sanctuary (towns, neighbours' power)
    public const int DragonlordsVolcanoTokens = 1;         // power tokens removed for the game to make a hex Volcano
    public const int DragonlordsVolcanoHomeTokens = 2;     // ... when it is another faction's home terrain
    public const int DragonlordsTokensPerSpade = 1;        // a spade gained is a new power token in bowl 1 instead
    public const int AcolytesVolcanoSteps = 3;             // steps paid from one track to make a hex Volcano
    public const int AcolytesVolcanoHomeSteps = 4;         // ... when it is another faction's home terrain
    public const int AcolytesStepsPerSpade = 1;            // a spade gained is a step on a track of their choice instead
    public const int AcolytesPriestBonusSteps = 1;         // with the stronghold: a priest sent to a track goes this much further
    public const int RiverwalkersUnlockCoins = 1;          // a terrain unlocked instead of gaining a priest
    public const int RiverwalkersUnlockHomeCoins = 2;      // ... when it is another faction's home terrain
    public const int RiverwalkersStrongholdBridges = 2;    // placed at once, free (any may be left unplaced)
    public const int ShapeshiftCost = 5;                   // power (spent), or power tokens (removed for the game)
    public const int ShapeshiftersTokenVictoryPoints = 1;  // for a new token in bowl 3, when an opponent took their power
    public const int ShapeshiftersPowerWhenDeclined = 1;   // every opponent declined the power from their build

    // ---------- Fan faction abilities ----------
    public const int ArchitectsBridgePriests = 1;          // their book action: a bridge for a priest
    public const int ArchitectsMoveBridgeVictoryPoints = 3; // their stronghold's action
    public const int DjinniLamps = 3;                      // magic lamps at the start: each swaps two of their trackers
    public const int DjinniStartingSteps = 2;              // on one track of their choice, after the starting buildings
    public const int DjinniVictoryPointsPerPriest = 1;     // when passing, with the stronghold: per own priest on the tracks
    public const int ArchivistsExtraPassTiles = 1;         // one more pass tile in a game with them
    public const int ArchivistsPowerPerPassTileCoin = 2;   // when passing: per coin on the tile taken
    public const int TimeTravellersTokensToBowlThree = 4;  // their stronghold's action: at most this many
    public const int ProspectorsSpadeCoins = 4;            // a golden spade
    public const int ProspectorsSpadeCoinsWithStronghold = 3;
    public const int ProspectorsVictoryPointsPerSpade = 1; // and 1 power, for each golden spade bought
    public const int ProspectorsPowerPerSpade = 1;
    public const int GoblinsStartingTreasures = 1;
    public const int GoblinsPowerPerDwelling = 1;          // spending a treasure: per building of the chosen kind
    public const int GoblinsCoinsPerTradingPost = 2;
    public const int GoblinsWorkersPerTemple = 1;
    public const int GoblinsStepsForStronghold = 1;
    public const int GoblinsStepsForSanctuary = 2;
    public const int WispsStrongholdVictoryPoints = 7;
    public const int ChashDallahIncomeTrackTop = 5;
    public static readonly Cost ChashDallahIncomeStepCost = new(Coins: 2, Workers: 2);
    public const int EnlightenedSpadePower = 3;            // power per spade before upgrades (2, then 1)
    public const int EnlightenedTokenCoins = 1;            // a new power token in bowl 1
    public const int EnlightenedStrongholdPower = 4;       // their stronghold's action
    public const int ConspiratorsCoinsPerFavour = 2;
    public const int ConspiratorsStrongholdFavours = 1;
    public const int DynionGeifrBuildingPower = 2;         // every structure of theirs
    public const int DynionGeifrStrongholdPriests = 2;
    public const int DynionGeifrPriestWorkers = 2;         // a priest converted: 2 workers and 2 coins
    public const int DynionGeifrPriestCoins = 2;
    public const int PowerTokens = 12;                     // the Children of the Wyrm's stronghold tops their tokens up to this
    public const int WyrmOfferVictoryPointDiscount = 1;    // power from a neighbour's build costs 1 VP less
    public static readonly int[] AtlanteansTownThresholds = { 7, 10, 16 }; // power of the town with their stronghold
    public const int AtlanteansStepsOnEveryTrack = 2;      // at 10 power
    public const int AtlanteansTownVictoryPoints = 20;     // at 16 power
    public const int SelkiesRiverDwellingWorkers = 1;      // a dwelling on a river hex: 1 worker more, 2 VP
    public const int SelkiesRiverDwellingVictoryPoints = 2;
    public const int SelkiesShippingBonus = 1;             // their stronghold's action
    public const int SelkiesFreeSpades = 1;                // and its free spade
    public const int FirewalkersPyroBehind = 4;            // the pyro token starts this far behind their VP
    public const int FirewalkersVolcanoPoints = 4;         // VP lost to make a hex Volcano
    public const int FirewalkersVolcanoHomePoints = 6;     // ... another faction's home terrain
    public const int FirewalkersPyroPerSpade = 4;          // a spade gained moves the pyro back this far
    public const int FirewalkersVictoryPointsPerGroup = 1;  // when passing, with the stronghold
    public const int EmberStrongholdLava = 2;              // lava placed under any of their structures, once
    public const int EmberUpgradePower = 1;                // and 1 lava under a dwelling, with every upgrade
    public const int ChangelingsExtraDwellingWorkers = 2;  // income once the dwelling beside the stronghold is built
}

// One slot of a priest zone (Config.PriestSlots): the steps a priest placed there gives, and whether it comes back.
public readonly record struct PriestSlotRule(int Steps, bool ReturnsAtEndOfTurn);
