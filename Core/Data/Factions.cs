using System.Collections.Immutable;

namespace Core;

// What is printed on one faction's board: everything that makes it different from the usual values (Config).
// Abilities that are more than numbers are applied by the rules where they happen (Rules, look for the faction).
// Home: the base game's home terrain; for the others, the terrain they build on (Ice, Volcano), or River for the variable
// factions, whose terrains are chosen (see PlayerState.Home and PlayerState.BuildTerrain, which the rules use).
public sealed record FactionDefinition(string Name, Terrain Home, PlayerColour Colour)
{
    public FactionKind Kind { get; init; } = FactionKind.Base;

    private static readonly Income OnePriest = new(Coins: 0, Workers: 0, Priests: 1, Power: 0);
    private static readonly ImmutableArray<Income> UsualDwellingIncome = WorkerIncomes(1, 1, 1, 1, 1, 1, 1, 0);
    private static readonly ImmutableArray<Income> UsualTradingPostIncome =
        CoinsAndPower((2, 1), (2, 1), (2, 2), (2, 2));
    private static readonly ImmutableArray<Income> UsualTempleIncome = ImmutableArray.Create(OnePriest, OnePriest, OnePriest);

    // Setup.
    public int StartingCoins { get; init; } = Config.StartingCoins;
    public int StartingWorkers { get; init; } = Config.StartingWorkers;
    public int StartingPriests { get; init; }
    public Power StartingPower { get; init; } = Config.StartingPower;
    public ImmutableArray<int> StartingSteps { get; init; } = ImmutableArray.Create(0, 0, 0, 0); // [(int)Track]
    public int StartingDwellings { get; init; } = 2; // 3: the third after everyone's two; 1: after everyone else's

    // Shipping and spades.
    public bool HasShipping { get; init; } = true;  // false: shipping effects are ignored (Fakirs, Dwarves)
    public int StartingShipping { get; init; }
    public int MaxShipping { get; init; } = 3;
    public Cost ShippingUpgradeCost { get; init; } = new(Coins: 4, Workers: 0); // plus 1 priest
    public int MaxSpadeUpgrades { get; init; } = 2;
    public Cost SpadeUpgradeCost { get; init; } = Config.SpadeUpgradeCost;      // plus 1 priest
    public int SpadeUpgradeVictoryPoints { get; init; } = Config.SpadeUpgradeVictoryPoints;
    public bool SpadesCostPriests { get; init; } // Darklings: 1 priest per spade instead of workers

    // Buildings: cost, and the income under each slot of each bank (left to right).
    public Cost DwellingCost { get; init; } = Config.DwellingCost;
    public Cost TradingPostCost { get; init; } = Config.TradingPostCost; // coins doubled away from opponents
    public Cost TempleCost { get; init; } = Config.TempleCost;
    public Cost StrongholdCost { get; init; } = Config.StrongholdCost;
    public Cost SanctuaryCost { get; init; } = Config.SanctuaryCost;
    public Income BaseIncome { get; init; } = new(Coins: 0, Workers: 1, Priests: 0, Power: 0);
    public ImmutableArray<Income> DwellingIncome { get; init; } = UsualDwellingIncome;
    public ImmutableArray<Income> TradingPostIncome { get; init; } = UsualTradingPostIncome;
    public ImmutableArray<Income> TempleIncome { get; init; } = UsualTempleIncome;
    public Income StrongholdIncome { get; init; }
    public Income SanctuaryIncome { get; init; } = OnePriest;
    public int FavoursPerTemple { get; init; } = Config.FavoursPerTemple;
    public int FavoursPerSanctuary { get; init; } = Config.FavoursPerSanctuary;

    // The bonus action the stronghold unlocks, if it has one (its other effects are in Rules).
    public BonusAction? StrongholdAction { get; init; }

    // Towns and the end of the game.
    public int TownVictoryPoints { get; init; }    // on top of the key token's
    public Income TownIncome { get; init; }        // on top of the key token's
    public int CoinsPerVictoryPoint { get; init; } = EndGame.CoinsPerVictoryPoint; // in final scoring

    // ---------- Helpers for writing the table below ----------

    internal static ImmutableArray<Income> WorkerIncomes(params int[] workers) =>
        workers.Select(w => new Income(Coins: 0, Workers: w, Priests: 0, Power: 0)).ToImmutableArray();

    internal static ImmutableArray<Income> CoinsAndPower(params (int Coins, int Power)[] slots) =>
        slots.Select(slot => new Income(Coins: slot.Coins, Workers: 0, Priests: 0, Power: slot.Power)).ToImmutableArray();

    internal static Income PowerIncome(int power) => new(Coins: 0, Workers: 0, Priests: 0, Power: power);
    internal static Income PriestIncome(int priests) => new(Coins: 0, Workers: 0, Priests: priests, Power: 0);

    // Steps in track order: fire, water, earth, air.
    internal static ImmutableArray<int> Steps(int fire, int water, int earth, int air) => ImmutableArray.Create(fire, water, earth, air);
}

// Every faction's board (All), and one-line lookups into it.
public static class Factions
{
    public static readonly ImmutableDictionary<Faction, FactionDefinition> All = new Dictionary<Faction, FactionDefinition>
    {
        [Faction.Auren] = new("Auren", Terrain.Forest, PlayerColour.Green)
        {
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2),
            StrongholdAction = BonusAction.TwoCultSteps, // and a favour when built
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 0, air: 1),
        },
        [Faction.Witches] = new("Witches", Terrain.Forest, PlayerColour.Green)
        {
            StrongholdIncome = FactionDefinition.PowerIncome(2),
            StrongholdAction = BonusAction.BuildOnHomeTerrain,
            TownVictoryPoints = Config.WitchesTownVictoryPoints,
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 0, air: 2),
        },
        [Faction.Giants] = new("Giants", Terrain.Wasteland, PlayerColour.Red)
        {
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StrongholdAction = BonusAction.TwoSpades, // and every transform takes exactly 2 spades
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 0, air: 1),
        },
        [Faction.ChaosMagicians] = new("Chaos Magicians", Terrain.Wasteland, PlayerColour.Red)
        {
            StartingWorkers = 4,
            StartingDwellings = 1,
            StrongholdCost = new(Coins: 4, Workers: 4),
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = new(Coins: 0, Workers: 2, Priests: 0, Power: 0),
            StrongholdAction = BonusAction.DoubleAction,
            FavoursPerTemple = 2,
            FavoursPerSanctuary = 2,
            StartingSteps = FactionDefinition.Steps(fire: 2, water: 0, earth: 0, air: 0),
        },
        [Faction.Nomads] = new("Nomads", Terrain.Desert, PlayerColour.Yellow)
        {
            StartingWorkers = 2,
            StartingDwellings = 3,
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 1), (2, 1), (3, 1), (4, 1)),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2),
            StrongholdAction = BonusAction.Sandstorm,
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 1, air: 0),
        },
        [Faction.Fakirs] = new("Fakirs", Terrain.Desert, PlayerColour.Yellow)
        {
            HasShipping = false, // a flying carpet instead (Rules.ReachOf)
            MaxShipping = 0,
            MaxSpadeUpgrades = 1,
            StrongholdCost = new(Coins: 10, Workers: 4),
            StrongholdIncome = FactionDefinition.PriestIncome(1), // and +1 carpet range
            StartingPower = new(7, 5, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 0, air: 1),
        },
        [Faction.Halflings] = new("Halflings", Terrain.Plains, PlayerColour.Brown)
        {
            SpadeUpgradeCost = new(Coins: 1, Workers: 2),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and 3 spades at once
            StartingPower = new(3, 9, 0),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 1, air: 1),
        },
        [Faction.Cultists] = new("Cultists", Terrain.Plains, PlayerColour.Brown)
        {
            StrongholdCost = new(Coins: 8, Workers: 4),
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and 7 VP at once
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 1, air: 0),
        },
        [Faction.Alchemists] = new("Alchemists", Terrain.Swamp, PlayerColour.Black)
        {
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 1), (2, 1), (3, 1), (4, 1)),
            StrongholdIncome = new(Coins: 6, Workers: 0, Priests: 0, Power: 0), // and 12 power at once
            CoinsPerVictoryPoint = Config.AlchemistsCoinsPerVictoryPoint,
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 1, earth: 0, air: 0),
        },
        [Faction.Darklings] = new("Darklings", Terrain.Swamp, PlayerColour.Black)
        {
            StartingWorkers = 1,
            StartingPriests = 1,
            SpadesCostPriests = true,
            MaxSpadeUpgrades = 0,
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and up to 3 workers exchanged for priests at once
            SanctuaryCost = new(Coins: 10, Workers: 4),
            SanctuaryIncome = FactionDefinition.PriestIncome(2),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 1, air: 0),
        },
        [Faction.Mermaids] = new("Mermaids", Terrain.Lake, PlayerColour.Blue)
        {
            StartingShipping = 1,
            MaxShipping = 5,
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and a free shipping upgrade at once
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StartingPower = new(3, 9, 0),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 2, earth: 0, air: 0),
        },
        [Faction.Swarmlings] = new("Swarmlings", Terrain.Lake, PlayerColour.Blue)
        {
            StartingCoins = 20,
            StartingWorkers = 8,
            BaseIncome = new(Coins: 0, Workers: 2, Priests: 0, Power: 0),
            DwellingCost = new(Coins: 3, Workers: 2),
            TradingPostCost = new(Coins: 4, Workers: 3),
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 2), (2, 2), (2, 2), (3, 2)),
            TempleCost = new(Coins: 6, Workers: 3),
            StrongholdCost = new(Coins: 8, Workers: 5),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StrongholdAction = BonusAction.FreeTradingPost,
            SanctuaryCost = new(Coins: 8, Workers: 5),
            SanctuaryIncome = FactionDefinition.PriestIncome(2),
            TownIncome = new(Coins: 0, Workers: Config.SwarmlingsTownWorkers, Priests: 0, Power: 0),
            StartingPower = new(3, 9, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 1, earth: 1, air: 1),
        },
        [Faction.Dwarves] = new("Dwarves", Terrain.Mountain, PlayerColour.Grey)
        {
            HasShipping = false, // tunnelling instead (Rules.ReachOf)
            MaxShipping = 0,
            TradingPostIncome = FactionDefinition.CoinsAndPower((3, 1), (2, 1), (2, 2), (3, 2)),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and tunnelling costs 1 worker less
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 2, air: 0),
        },
        [Faction.Engineers] = new("Engineers", Terrain.Mountain, PlayerColour.Grey)
        {
            StartingCoins = 10,
            StartingWorkers = 2,
            DwellingCost = new(Coins: 1, Workers: 1),
            BaseIncome = default, // no worker printed before the first dwelling (BGA's board agrees: 0, 1, 2, 2, 3, 4, 4, 5, 6)
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 0, 1, 1, 0, 1, 1),
            TradingPostCost = new(Coins: 2, Workers: 1),
            TempleCost = new(Coins: 4, Workers: 1),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(5), FactionDefinition.PriestIncome(1)),
            StrongholdCost = new(Coins: 6, Workers: 3),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and VP for their bridges when passing
            SanctuaryCost = new(Coins: 6, Workers: 3),
            StartingPower = new(3, 9, 0),
        },

        // ---------- Fire & Ice ----------
        [Faction.IceMaidens] = new("Ice Maidens", Terrain.Ice, PlayerColour.White)
        {
            Kind = FactionKind.Ice, // and a favour as soon as the starting terrain is chosen
            SpadeUpgradeCost = new(Coins: 5, Workers: 1),
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 1, 1, 1, 1, 1),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and 3 VP per temple on the map when passing
            StartingPower = new(6, 6, 0),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 0, air: 1),
        },
        [Faction.Yetis] = new("Yetis", Terrain.Ice, PlayerColour.White)
        {
            Kind = FactionKind.Ice, // and every power action costs 1 power less
            SpadeUpgradeCost = new(Coins: 5, Workers: 1),
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 1, 1, 1, 1, 1),
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 2), (2, 2), (2, 2), (2, 2)),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and power actions already taken may be taken again
            StartingPower = new(0, 12, 0),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 1, air: 1),
        },
        [Faction.Dragonlords] = new("Dragonlords", Terrain.Volcano, PlayerColour.Orange)
        {
            Kind = FactionKind.Volcano, // Volcano for power tokens; spades become tokens
            MaxSpadeUpgrades = 0,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and a power token in bowl 1 per player at once
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StartingPower = new(4, 4, 0),
            StartingSteps = FactionDefinition.Steps(fire: 2, water: 0, earth: 0, air: 0),
        },
        [Faction.Acolytes] = new("Acolytes", Terrain.Volcano, PlayerColour.Orange)
        {
            Kind = FactionKind.Volcano, // Volcano for steps down a track; spades become steps
            MaxSpadeUpgrades = 0,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and priests send them 1 step further
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StartingPower = new(6, 6, 0),
            StartingSteps = FactionDefinition.Steps(fire: 3, water: 3, earth: 3, air: 3),
        },
        [Faction.Riverwalkers] = new("Riverwalkers", Terrain.River, PlayerColour.White)
        {
            Kind = FactionKind.Variable, // priests on their terrain wheel lock terrains; only by rivers; no spades
            StartingShipping = 1,
            MaxShipping = 1,
            MaxSpadeUpgrades = 0,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 0, 1, 1, 0, 1, 1), // the 8th a worker too (as on BGA)
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(5), FactionDefinition.PriestIncome(1)),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and up to 2 free bridges at once
            StartingPower = new(10, 2, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 0, air: 1),
        },
        [Faction.Shapeshifters] = new("Shapeshifters", Terrain.River, PlayerColour.White)
        {
            Kind = FactionKind.Variable, // the starting terrain is their home, until they move it
            MaxSpadeUpgrades = 0,
            StrongholdCost = new(Coins: 6, Workers: 3),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StrongholdAction = BonusAction.Shapeshift,
            StartingPower = new(4, 4, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 1, earth: 0, air: 0),
        },

        // ---------- Fan factions ----------
        [Faction.Architects] = new("Architects", Terrain.Wasteland, PlayerColour.Red)
        {
            StrongholdIncome = FactionDefinition.PowerIncome(4), // their bridges count 1 power in towns; a bridge for a priest
            StrongholdAction = BonusAction.MoveBridge,
            StartingPower = new(3, 9, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 0, air: 1),
        },
        [Faction.Treasurers] = new("Treasurers", Terrain.Wasteland, PlayerColour.Red)
        {
            StartingWorkers = 4,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 0, 1, 0, 1, 1, 0),
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 1), (0, 0), (2, 2), (2, 2)),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(4), FactionDefinition.PriestIncome(1)),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and anything received may go into their Safe
            StartingPower = new(4, 8, 0),
            StartingSteps = FactionDefinition.Steps(fire: 2, water: 0, earth: 0, air: 0),
        },
        [Faction.Djinni] = new("Djinni", Terrain.Desert, PlayerColour.Yellow)
        {
            StrongholdCost = new(Coins: 6, Workers: 3),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and VP for their priests on the tracks when passing
            // no starting steps: 2 on one track of their choice, once the starting buildings are placed
        },
        [Faction.Archivists] = new("Archivists", Terrain.Desert, PlayerColour.Yellow)
        {
            BaseIncome = new(Coins: 0, Workers: 2, Priests: 0, Power: 0),
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            StrongholdIncome = FactionDefinition.PowerIncome(2), // and two pass tiles taken when passing
        },
        [Faction.TimeTravellers] = new("Time Travellers", Terrain.Plains, PlayerColour.Brown)
        {
            MaxSpadeUpgrades = 0,
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2),
            StrongholdAction = BonusAction.TokensToBowlThree,
            StartingSteps = FactionDefinition.Steps(fire: 2, water: 0, earth: 0, air: 0),
        },
        [Faction.Prospectors] = new("Prospectors", Terrain.Plains, PlayerColour.Brown)
        {
            StartingWorkers = 2,
            MaxSpadeUpgrades = 0, // golden spades, bought with coins
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 0, 1, 0),
            TradingPostIncome = FactionDefinition.CoinsAndPower((4, 1), (3, 1), (2, 2), (1, 2)),
            TempleIncome = ImmutableArray.Create(new Income(Coins: 3, Workers: 0, Priests: 0, Power: 0), FactionDefinition.PriestIncome(1),
                new Income(Coins: 4, Workers: 0, Priests: 0, Power: 0)),
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdCost = new(Coins: 11, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(3), // and golden spades cost 3 coins
            StrongholdAction = BonusAction.CoinsForTradingPosts,
            StartingPower = new(7, 5, 0), // as BGA plays them (its material, every table seen); the printed board shows 8/4/0
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 3, air: 0),
        },
        [Faction.Goblins] = new("Goblins", Terrain.Swamp, PlayerColour.Black)
        {
            TempleCost = new(Coins: 6, Workers: 2),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and a treasure with every town
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 1, air: 1),
        },
        [Faction.ChildrenOfTheWyrm] = new("Children of the Wyrm", Terrain.Swamp, PlayerColour.Black)
        {
            StartingCoins = 12,
            DwellingCost = new(Coins: 1, Workers: 1), // every structure's coins doubled with no opponent next to it (board: 1/2)
            StrongholdAction = BonusAction.RiverTokens,
            TempleCost = new(Coins: 4, Workers: 2),
            SanctuaryCost = new(Coins: 5, Workers: 4),
            StrongholdCost = new(Coins: 5, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 1, air: 0),
        },
        [Faction.Wisps] = new("Wisps", Terrain.Lake, PlayerColour.Blue)
        {
            StrongholdCost = new(Coins: 4, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(3), // and 7 VP and a free dwelling on any lake at once
            StartingPower = new(7, 5, 0),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 0, air: 1),
        },
        [Faction.Atlanteans] = new("Atlanteans", Terrain.Lake, PlayerColour.Blue)
        {
            SpadeUpgradeCost = new(Coins: 4, Workers: 1),
            SpadeUpgradeVictoryPoints = 4, // as on BGA
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(6),
            StartingDwellings = 1, // their stronghold instead, placed after everyone else's dwellings (Rules)
            StartingPower = new(11, 1, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 1, earth: 0, air: 0),
        },
        [Faction.ChashDallah] = new("Chash Dallah", Terrain.Forest, PlayerColour.Green)
        {
            MaxSpadeUpgrades = 0, // an income track instead
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 1), (2, 1), (3, 1), (4, 1)), // as on BGA
            StrongholdIncome = new(Coins: 4, Workers: 0, Priests: 0, Power: 0), // and power actions paid in coins
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 1, air: 1),
        },
        [Faction.Enlightened] = new("Enlightened", Terrain.Forest, PlayerColour.Green)
        {
            BaseIncome = FactionDefinition.PowerIncome(3),
            DwellingIncome = ImmutableArray.Create(FactionDefinition.PowerIncome(2), FactionDefinition.PowerIncome(3),
                FactionDefinition.PowerIncome(2), FactionDefinition.PowerIncome(3), FactionDefinition.PowerIncome(2),
                FactionDefinition.PowerIncome(3), FactionDefinition.PowerIncome(2), FactionDefinition.PowerIncome(3)),
            TradingPostIncome = FactionDefinition.CoinsAndPower((2, 1), (2, 1), (3, 1), (4, 1)),
            TempleCost = new(Coins: 5, Workers: 0, Priests: 1),
            SanctuaryCost = new(Coins: 6, Workers: 0, Priests: 2),
            StrongholdCost = new(Coins: 4, Workers: 0, Priests: 1), // no income; conversions doubled
            StrongholdAction = BonusAction.FourPower,
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 0, air: 2),
        },
        [Faction.Conspirators] = new("Conspirators", Terrain.Mountain, PlayerColour.Grey)
        {
            TradingPostIncome = FactionDefinition.CoinsAndPower((3, 1), (2, 1), (2, 2), (3, 2)),
            StrongholdAction = BonusAction.SwapFavour, // no income; a favour at once
        },
        [Faction.DynionGeifr] = new("Dynion Geifr", Terrain.Mountain, PlayerColour.Grey)
        {
            StartingWorkers = 2, // and the Fire-2 favour
            BaseIncome = new(Coins: 0, Workers: 2, Priests: 0, Power: 0),
            DwellingCost = new(Coins: 2, Workers: 2),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(5), FactionDefinition.PriestIncome(1)),
            StrongholdIncome = FactionDefinition.PowerIncome(4), // and 2 priests at once; a town of 3 with it
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 0, earth: 1, air: 1),
        },
        [Faction.Selkies] = new("Selkies", Terrain.Ice, PlayerColour.White)
        {
            Kind = FactionKind.Ice,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 1, 1, 1, 1, 1), // the 8th too, as every Ice faction
            SpadeUpgradeCost = new(Coins: 5, Workers: 1),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StrongholdAction = BonusAction.SelkiesSpade,
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 2, earth: 0, air: 0),
        },
        [Faction.SnowShamans] = new("Snow Shamans", Terrain.Ice, PlayerColour.White)
        {
            Kind = FactionKind.Ice,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 1, 1, 1, 1, 1),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PowerIncome(4), FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(4)),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StartingSteps = FactionDefinition.Steps(fire: 0, water: 1, earth: 1, air: 0),
        },
        [Faction.Firewalkers] = new("Firewalkers", Terrain.Volcano, PlayerColour.Orange)
        {
            Kind = FactionKind.Volcano,
            MaxSpadeUpgrades = 0,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), new Income(Coins: 0, Workers: 0, Priests: 0, Power: 0, VictoryPoints: 2),
                FactionDefinition.PriestIncome(1)),
            StrongholdCost = new(Coins: 8, Workers: 4),
            StrongholdIncome = FactionDefinition.PowerIncome(2),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 0, air: 1),
        },
        [Faction.KingdomOfEmber] = new("Kingdom of Ember", Terrain.Volcano, PlayerColour.Orange)
        {
            Kind = FactionKind.Volcano,
            MaxShipping = 0, // no shipping at all
            MaxSpadeUpgrades = 0,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            TempleIncome = ImmutableArray.Create(FactionDefinition.PriestIncome(1), FactionDefinition.PowerIncome(5), FactionDefinition.PriestIncome(1)),
            SanctuaryCost = new(Coins: 8, Workers: 4),
            StrongholdCost = new(Coins: 5, Workers: 3),
            StrongholdIncome = new(Coins: 0, Workers: 1, Priests: 0, Power: 1),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 0, earth: 1, air: 0),
        },
        [Faction.Geologists] = new("Geologists", Terrain.River, PlayerColour.White)
        {
            Kind = FactionKind.Variable,
            MaxSpadeUpgrades = 0,
            BaseIncome = default,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 0, 1, 1, 1, 0),
            ShippingUpgradeCost = new(Coins: 6, Workers: 0, Priests: 1), // plus the usual priest: 2 in all
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StrongholdAction = BonusAction.MoveRock,
            StartingPower = new(7, 5, 0),
        },
        [Faction.Changelings] = new("Changelings", Terrain.River, PlayerColour.White)
        {
            Kind = FactionKind.Variable,
            DwellingIncome = FactionDefinition.WorkerIncomes(1, 1, 1, 1, 1, 0, 1, 0),
            TradingPostIncome = FactionDefinition.CoinsAndPower((1, 1), (2, 2), (2, 2), (3, 2)), // board: the first 1 coin
            ShippingUpgradeCost = new(Coins: 6, Workers: 0),
            StrongholdCost = new(Coins: 8, Workers: 5),
            StrongholdIncome = FactionDefinition.PowerIncome(4),
            StartingPower = new(7, 5, 0),
            StartingSteps = FactionDefinition.Steps(fire: 1, water: 1, earth: 0, air: 1),
        },
    }.ToImmutableDictionary();

    public static FactionDefinition Of(Faction faction) => All[faction];

    // For messages and labels: "Chaos Magicians".
    public static string Name(Faction faction) => All[faction].Name;

    // The faction's own colour. The variable factions take their starting terrain's: use PlayerState.Colour.
    public static PlayerColour ColourOf(Faction faction) => All[faction].Colour;

    public static FactionKind Kind(Faction faction) => All[faction].Kind;

    // The base game's home terrain: the terrain the faction builds on. For the others, what the table says
    // (Ice, Volcano, River): the rules use PlayerState.Home and PlayerState.BuildTerrain instead.
    public static Terrain HomeTerrain(Faction faction) => All[faction].Home;

    // Received when the game starts (see Rules.ApplyStartGame).
    public static int StartingCoins(Faction faction) => All[faction].StartingCoins;
    public static int StartingWorkers(Faction faction) => All[faction].StartingWorkers;
    public static int StartingPriests(Faction faction) => All[faction].StartingPriests;
    public static Power StartingPower(Faction faction) => All[faction].StartingPower;
    public static int StartingDwellings(Faction faction) => All[faction].StartingDwellings;

    // Steps each faction starts with on the tracks, [(int)Track]. Set directly, so they give no power.
    public static ValueList<int> StartingTrackSteps(Faction faction) => new(All[faction].StartingSteps);

    // Shipping: how many river hexes a build or transform may reach across. Fakirs and Dwarves have none at all.
    public static bool HasShipping(Faction faction) => All[faction].HasShipping;
    public static int StartingShipping(Faction faction) => All[faction].StartingShipping;
    public static int MaxShipping(Faction faction) => All[faction].MaxShipping;
    // Coins for one shipping upgrade (plus 1 priest).
    public static Cost ShippingUpgradeCost(Faction faction) => All[faction].ShippingUpgradeCost;
    public static int SpadeUpgradeVictoryPoints(Faction faction) => All[faction].SpadeUpgradeVictoryPoints;

    // Spade upgrades: how many the faction may make, what each costs (plus 1 priest), and what a spade costs after
    // `upgrades` of them: workers, or a priest for the Darklings (whose spades never get cheaper).
    public static int MaxSpadeUpgrades(Faction faction) => All[faction].MaxSpadeUpgrades;
    public static Cost SpadeUpgradeCost(Faction faction) => All[faction].SpadeUpgradeCost;
    public static bool SpadesCostPriests(Faction faction) => All[faction].SpadesCostPriests;
    public static int WorkersPerSpade(Faction faction, int upgrades) =>
        SpadesCostPriests(faction) ? 0 : Config.WorkersPerSpade - upgrades;
    public static Cost SpadeCost(Faction faction, int upgrades) =>
        SpadesCostPriests(faction)
            ? new Cost(Coins: 0, Workers: 0, Priests: Config.DarklingsPriestsPerSpade)
            : new Cost(Coins: 0, Workers: WorkersPerSpade(faction, upgrades));

    // Spades this faction needs to turn one basic terrain into another: the steps round the wheel (Terrains.Spades),
    // except that the Giants always need exactly 2. Null if either is not a basic terrain. The rules use
    // PlayerState.SpadesNeeded, which also knows the ice factions' Ice and the factions without spades.
    public static int? SpadesNeeded(Faction faction, Terrain from, Terrain to) =>
        Terrains.Spades(from, to) is not int spades ? null
            : faction == Faction.Giants && spades > 0 ? Config.GiantsSpadesPerTransform
            : spades;

    // Whether the faction always takes power offered by a neighbour's build when it costs nothing (only 1 power
    // would be gained): then it is accepted without asking. Not the Time Travellers: they are always asked, since
    // they may want to keep bowl 1's tokens for their stronghold's action.
    // (Offers from the Cultists' builds are always asked: see Rules.AcceptFreeOffers.)
    public static bool AcceptsFreePowerAutomatically(Faction faction) => faction != Faction.TimeTravellers;

    // A building's power value: what it counts for towns and for opponents' power when building next to it (the Yetis'
    // stronghold and sanctuary count 4).
    public static int BuildingPower(Faction faction, PieceKind kind) => kind switch
    {
        PieceKind.Priest => 0,
        _ when faction == Faction.DynionGeifr => Config.DynionGeifrBuildingPower, // every structure 2
        PieceKind.Dwelling or PieceKind.Landscape => 1,
        PieceKind.TradingPost or PieceKind.Temple => 2,
        PieceKind.Stronghold or PieceKind.Sanctuary => faction == Faction.Yetis ? Config.YetisBigBuildingPower : 3,
        _ => 0,
    };

    // The bonus action a faction unlocks by building its stronghold, if any.
    public static BonusAction? StrongholdAction(Faction faction) => All[faction].StrongholdAction;

    // What building one of these costs this faction (a trading post: next to an opponent; see LonelyTradingPostSurcharge).
    public static Cost BuildingCost(Faction faction, PieceKind kind) => kind switch
    {
        PieceKind.Dwelling    => All[faction].DwellingCost,
        PieceKind.TradingPost => All[faction].TradingPostCost,
        PieceKind.Temple      => All[faction].TempleCost,
        PieceKind.Stronghold  => All[faction].StrongholdCost,
        PieceKind.Sanctuary   => All[faction].SanctuaryCost,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a building."),
    };

    // Extra coins for a trading post with no opponent's building next to it: its coins again (3/6, 4/8, 2/4).
    public static int LonelyTradingPostSurcharge(Faction faction) => All[faction].TradingPostCost.Coins;

    // Favours earned when a building of this kind is built.
    public static int FavoursFor(Faction faction, PieceKind kind) => kind switch
    {
        PieceKind.Temple    => All[faction].FavoursPerTemple,
        PieceKind.Sanctuary => All[faction].FavoursPerSanctuary,
        _ => 0,
    };

    // Income printed on the faction board, received every round whatever is built.
    public static Income BaseIncome(Faction faction) => All[faction].BaseIncome;

    // Income revealed by emptying each slot of a bank, left to right (slot 0 first).
    public static ImmutableArray<Income> DwellingSlotIncome(Faction faction) => All[faction].DwellingIncome;
    public static ImmutableArray<Income> TradingPostSlotIncome(Faction faction) => All[faction].TradingPostIncome;
    public static ImmutableArray<Income> TempleSlotIncome(Faction faction) => All[faction].TempleIncome;
    public static ImmutableArray<Income> StrongholdSlotIncome(Faction faction) => ImmutableArray.Create(All[faction].StrongholdIncome);
    public static ImmutableArray<Income> SanctuarySlotIncome(Faction faction) => ImmutableArray.Create(All[faction].SanctuaryIncome);

    // The slot incomes of any building kind's bank.
    public static ImmutableArray<Income> SlotIncome(Faction faction, PieceKind kind) => kind switch
    {
        PieceKind.Dwelling    => DwellingSlotIncome(faction),
        PieceKind.TradingPost => TradingPostSlotIncome(faction),
        PieceKind.Temple      => TempleSlotIncome(faction),
        PieceKind.Stronghold  => StrongholdSlotIncome(faction),
        PieceKind.Sanctuary   => SanctuarySlotIncome(faction),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a building."),
    };

    // Forming a town: extra victory points and income on top of the key token's (Witches, Swarmlings).
    public static int TownVictoryPoints(Faction faction) => All[faction].TownVictoryPoints;
    public static Income TownIncome(Faction faction) => All[faction].TownIncome;

    // Final scoring: coins for one victory point (the Alchemists need only 2).
    public static int CoinsPerVictoryPoint(Faction faction) => All[faction].CoinsPerVictoryPoint;

    // ---------- Fan factions ----------

    // A fan faction (GameOptions.FanFactions)?
    public static bool IsFanFaction(Faction faction) => faction >= Faction.Architects;

    // Chash Dallah: the income their income track gives at this step (1-5), every step up to it added; nothing at 0.
    public static Income IncomeTrackIncome(int step)
    {
        Income income = default;
        if (step >= 1) income += new Income(Coins: 2, Workers: 0, Priests: 0, Power: 0);
        if (step >= 2) income += new Income(Coins: 0, Workers: 1, Priests: 0, Power: 0);
        if (step >= 3) income += new Income(Coins: 2, Workers: 0, Priests: 0, Power: 0);
        if (step >= 4) income += new Income(Coins: 0, Workers: 1, Priests: 0, Power: 0);
        if (step >= 5) income += new Income(Coins: 2, Workers: 0, Priests: 0, Power: 0);
        return income;
    }

    // Chash Dallah: VP for reaching this step of their income track (2-5).
    public static int IncomeTrackVictoryPoints(int step) => Math.Max(0, step - 1);
}
