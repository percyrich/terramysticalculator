using System.Collections.Immutable;

namespace Core;

// Riverwalkers: a priest still on their terrain wheel, locking that terrain (they can't build there yet).
public readonly record struct WheelPriest(Terrain Terrain, Piece Priest);

// Everything belonging to one player. Immutable; "changes" are copies made with `with { ... }`.
// Income and total steps are not stored: they are calculated from what is stored, so they can
// never disagree with it.
public sealed record PlayerState(
    Faction Faction,
    int Coins,
    int Workers,
    int VictoryPoints,
    ValueList<Piece?> DwellingBank,    // fixed slots, left to right; null = emptied slot
    ValueList<Piece?> TradingPostBank, // the same, for each other building kind
    ValueList<Piece?> TempleBank,
    ValueList<Piece?> StrongholdBank,
    ValueList<Piece?> SanctuaryBank,
    ValueList<Piece> Priests,          // gained priests, ready to place: bottom first, top last
    ValueList<Piece> PriestSupply,     // priests not yet gained: the next one to be gained first
    ValueList<FavourTile> FavourTiles, // favours taken, in the order taken
    int FavoursOwed,                   // favours earned but not yet chosen (must be chosen before End turn)
    ValueList<BonusActionId> BonusActionsUsed, // used this round, in order; cleared when a round begins
    int? PassTile,                     // the pass tile held; null before the setup pick
    int SpadesOwed,                    // free spades to spend now (from a bonus action, or at the end of a round)
    int SpadeUpgrades,                 // how many times spades have been upgraded (each makes a spade cheaper)
    int Keys,                          // unspent keys (from towns); one is spent to reach the top of a track
    ValueList<int> TownHexes,          // hexes of buildings already part of a town (they cannot form another)
    int BridgesOwed,                   // bridges to place now (from the bridge power action)
    bool PassTileOwed,                 // has passed (rounds 1-5) and must still pick a new pass tile
    ValueList<int> TrackSteps,         // indexed by (int)Track, each 0..Config.TrackMax
    int Shipping,                      // how many river hexes a build may reach across (before any pass tile bonus)
    Power Power,                       // tokens in bowls 1, 2 and 3
    Terrain? StartingTerrain,          // chosen at setup by the ice, volcano and variable factions; null for the base game's
    Terrain? ShapeshifterHome,         // Shapeshifters: where their ring is, their home terrain; null for everyone else
    ValueList<WheelPriest> WheelPriests, // Riverwalkers: priests still on their terrain wheel; empty for everyone else
    int CultStepsOwed = 0,             // steps to take on a track of their choice (Cultists: an opponent took their power;
                                       // Acolytes: a spade)
    int PriestExchangesLeft = 0,       // Darklings, the turn their stronghold is built: workers that may still become priests
    int PriestChoicesOwed = 0,         // Riverwalkers: priests gained, each to be taken or swapped for a terrain (ChoosePriest)
    int CultStepsToPay = 0,            // Acolytes: steps to pay from one track for a hex just made Volcano (PayCultSteps)
    bool TokenOfferOwed = false,       // Shapeshifters: may pay 1 VP for a token in bowl 3 (AnswerTokenOffer)
    bool BridgesOptional = false,      // the bridges owed may be left unplaced (Riverwalkers' stronghold: SkipBridges)
    int PriestDebt = 0,                // priests owed: paid for with none in hand; each priest gained pays one back
    LandscapeOffer LandscapeOffers = LandscapeOffer.None, // what the special landscape offers, to take or decline now
    bool AltarStepsToPay = false,      // Acolytes: the steps owed (CultStepsToPay) pay for their Altar of Sacrifice,
                                       // so going down for them gains no power
    (Track Track, int Steps)? VolcanoStepsPaid = null, // Acolytes: what their last transform to Volcano was paid with
                                       // (the Altar laid there instead gives back what it cost beyond its 3)
    int? WitchesTownBonusHex = null,   // Witches with Raven's Blunder: a town just formed (its key's hex), its bonus
                                       // still to choose: 5 VP or 3 steps on a track (ChooseTownBonus)
    // ---------- Fan factions ----------
    int Lamps = 0,                     // Djinni: magic lamps left (SwapTracks)
    int Treasures = 0,                 // Goblins: treasure tokens (SpendTreasure)
    int IncomeTrack = 0,               // Chash Dallah: their income track's step (1-5); 0 for everyone else
    int SafeCoins = 0,                 // Treasurers: what lies in their Safe, doubled as the next round begins
    int SafeWorkers = 0,
    ValueList<Piece>? SafePriests = null,
    int? ExtraPassTile = null,         // Archivists with their stronghold: the second pass tile they hold
    bool ExtraPassTileOwed = false,    // ... still to pick after passing (after PassTileOwed)
    ValueList<int>? HeldPassTiles = null, // ... while picking: the tiles they gave back, into the bank after both picks
    int StartingStepsOwed = 0,         // Djinni: steps on one track of their choice, before the pass tiles are picked
    bool LakeDwellingOwed = false,     // Wisps' stronghold: a free dwelling on any empty Lake hex
    ValueList<ValueList<int>>? IceMassesOwed = null, // Snow Shamans' stronghold: masses of Ice each still to take a free dwelling
    bool FreeUpgradeOwed = false,      // Snow Shamans, after passing: a free shipping or spade step (no VP of its own)
    Terrain? TerrainB = null,          // Changelings: their B and C terrains (A is their starting terrain, their home)
    Terrain? TerrainC = null,
    Piece? ExtraDwelling = null,       // Changelings: the C dwelling beside their stronghold (buildable once it is built)
    int LavaOwed = 0,                  // Kingdom of Ember: lava still to place under a dwelling (spades, upgrades)
    int LavaOwedAny = 0,               // ... under any structure (their stronghold's)
    int Pyro = 0,                      // Firewalkers: the pyro token's place on the VP track (never ahead of their VP)
    ValueList<Terrain>? Rocks = null,  // Geologists: the terrains covered by glowing rocks on their wheel
    int RockMovesOwed = 0,             // Geologists: spades gained, each a rock to place or remove (MoveRock)
    bool StartingTownOwed = false,     // Atlanteans: the key token for their starting stronghold, after the first structures
    int AtlanteansRewards = 0,         // Atlanteans: how many of their town's power thresholds have paid out (0-3)
    ValueList<Track>? TopStepsHeld = null, // tracks where a step onto 10 waits for a key during this action (a town formed
                                           // in it brings one: Rules.ReleaseHeldTopSteps); dropped when the action ends
    int TokensHeld = 0,                    // new power tokens the action gives at its end, for bowl 1 (the Children of the
                                           // Wyrm's stronghold, as on BGA); released with PowerHeld
    int PowerHeld = 0,                     // power the action gives at its end (the Alchemists' stronghold, as on
                                           // BGA): gained once its power offers are answered
                                           // (Rules.ReleaseHeldPowerWhenDone), at the latest at End turn
    ValueList<int>? PassScoringHeld = null, // Snow Shamans: the pass tiles they held when passing, whose pass VP (with
                                           // the favours' and the stronghold's) wait for the free step (as on
                                           // BGA: the step first, then the pass scores)
    int? ArchitectsBuildHex = null)        // Architects: the hex their bridge saved free spades on; a dwelling must go
                                           // there before the spending ends
{
    public ValueList<Piece> SafePriestList => SafePriests ?? ValueList<Piece>.Empty;
    // Coins, workers and victory points start at 0; starting resources are given when the game starts.
    public static PlayerState New(Faction faction) => new(
        faction,
        0,
        0,
        0,
        FullBank(faction, PieceKind.Dwelling),
        FullBank(faction, PieceKind.TradingPost),
        FullBank(faction, PieceKind.Temple),
        FullBank(faction, PieceKind.Stronghold),
        FullBank(faction, PieceKind.Sanctuary),
        ValueList<Piece>.Empty,
        new ValueList<Piece>(Enumerable.Range(0, Config.PriestsPerPlayer)
            .Select(i => new Piece(faction, PieceKind.Priest, i))),
        ValueList<FavourTile>.Empty,
        0,
        ValueList<BonusActionId>.Empty,
        null,
        0,
        0,
        0,
        ValueList<int>.Empty,
        0,
        false,
        Factions.StartingTrackSteps(faction),
        Factions.StartingShipping(faction),
        Factions.StartingPower(faction),
        null,
        null,
        ValueList<WheelPriest>.Empty)
    {
        Lamps = faction == Faction.Djinni ? Config.DjinniLamps : 0,
        Treasures = faction == Faction.Goblins ? Config.GoblinsStartingTreasures : 0,
        IncomeTrack = faction == Faction.ChashDallah ? 1 : 0,
        StartingStepsOwed = faction == Faction.Djinni ? Config.DjinniStartingSteps : 0,
        ExtraDwelling = faction == Faction.Changelings ? new Piece(faction, PieceKind.Dwelling, Buildings.PerPlayer(PieceKind.Dwelling)) : null,
    };
    public ValueList<Terrain> RockList => Rocks ?? ValueList<Terrain>.Empty;

    private static ValueList<Piece?> FullBank(Faction faction, PieceKind kind) =>
        new(Enumerable.Range(0, Buildings.PerPlayer(kind)).Select(i => (Piece?)new Piece(faction, kind, i)));

    public FactionKind Kind => Factions.Kind(Faction);

    // The faction's colour: its own, or for the variable factions their starting terrain's (white until chosen); the
    // Shapeshifters' follows their home terrain wherever their ring moves.
    public PlayerColour Colour =>
        Kind == FactionKind.Variable && (ShapeshifterHome ?? StartingTerrain) is Terrain chosen
            ? Terrains.ColourOf(chosen)
            : Factions.ColourOf(Faction);

    // Its home terrain, if it has one: the base game's, or wherever the Shapeshifters' ring is. The ice, volcano
    // factions and the Riverwalkers have none (so a volcano faction never pays extra for their terrains).
    public Terrain? Home =>
        Kind == FactionKind.Base ? Factions.HomeTerrain(Faction)
        : Faction == Faction.Shapeshifters ? ShapeshifterHome
        : Faction == Faction.Changelings ? StartingTerrain // A only
        : null;

    // The terrain its dwellings go on, which its transforms aim at: its home terrain; Ice; Volcano (during setup,
    // the starting terrain, which the starting dwellings turn into it). Null for the Riverwalkers: any terrain they
    // have unlocked (CanBuildOn).
    public Terrain? BuildTerrain => Kind switch
    {
        FactionKind.Ice => Terrain.Ice,
        FactionKind.Volcano => Terrain.Volcano,
        _ => Home,
    };

    // Can its dwellings go on this terrain (as it is now, without transforming)?
    // Changelings: A, B or C (which one depends on the dwelling: Rules.DwellingTerrain); Geologists: a terrain a rock
    // could go on (Rules.RockCanCover).
    public bool CanBuildOn(Terrain terrain) => Faction switch
    {
        Faction.Riverwalkers => StartingTerrain is not null && Terrains.IsBasic(terrain) && WheelPriests.All(locked => locked.Terrain != terrain),
        Faction.Changelings => terrain == StartingTerrain || terrain == TerrainB || terrain == TerrainC,
        Faction.Geologists => Rules.RockCanCover(this, terrain),
        _ => terrain == BuildTerrain,
    };

    // Spades it needs to turn one terrain into another: round the wheel (the Giants always 2), and for an ice
    // faction into Ice as if Ice stood where its starting terrain is (that terrain itself takes 1 spade). Null if it
    // can't be done with spades: not basic terrains, or a faction without spades (volcano factions, Riverwalkers).
    public int? SpadesNeeded(Terrain from, Terrain to)
    {
        if (Kind == FactionKind.Volcano || Faction is Faction.Riverwalkers or Faction.Geologists)
            return null;
        if (to != Terrain.Ice)
            return Factions.SpadesNeeded(Faction, from, to);
        if (Kind != FactionKind.Ice || StartingTerrain is not Terrain start || Terrains.Spades(from, start) is not int steps)
            return null;
        return steps == 0 ? Config.IceSpadesFromStartingTerrain : steps;
    }

    // The bank holding this kind of building.
    public ValueList<Piece?> Bank(PieceKind kind) => kind switch
    {
        PieceKind.Dwelling    => DwellingBank,
        PieceKind.TradingPost => TradingPostBank,
        PieceKind.Temple      => TempleBank,
        PieceKind.Stronghold  => StrongholdBank,
        PieceKind.Sanctuary   => SanctuaryBank,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a building."),
    };

    // A copy with this kind's bank replaced.
    public PlayerState WithBank(PieceKind kind, ValueList<Piece?> bank) => kind switch
    {
        PieceKind.Dwelling    => this with { DwellingBank = bank },
        PieceKind.TradingPost => this with { TradingPostBank = bank },
        PieceKind.Temple      => this with { TempleBank = bank },
        PieceKind.Stronghold  => this with { StrongholdBank = bank },
        PieceKind.Sanctuary   => this with { SanctuaryBank = bank },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a building."),
    };

    // Income received at the start of the next round: the faction's base income, plus the income
    // printed under every emptied slot of every bank, plus the income of every favour and the pass tile held.
    public Income Income =>
        Factions.BaseIncome(Faction)
        + Buildings.All.Aggregate(default(Income), (sum, kind) => sum + Revealed(Bank(kind), Factions.SlotIncome(Faction, kind)))
        + FavourTiles.Aggregate(default(Income), (sum, tile) => sum + Favours.All[tile.Id].Income)
        + (PassTile is int passTile ? PassTiles.All[passTile].Income : default)
        + (ExtraPassTile is int extraTile ? PassTiles.All[extraTile].Income : default)
        + Factions.IncomeTrackIncome(IncomeTrack)
        + (Faction == Faction.Changelings && ExtraDwelling is null
            ? new Income(Coins: 0, Workers: Config.ChangelingsExtraDwellingWorkers, Priests: 0, Power: 0) : default);

    private static Income Revealed(ValueList<Piece?> bank, ImmutableArray<Income> slotIncome) =>
        Enumerable.Range(0, bank.Count)
            .Where(slot => bank[slot] is null)
            .Aggregate(default(Income), (sum, slot) => sum + slotIncome[slot]);

    // The only slot of a bank that may be built from. Null if the bank is empty.
    public int? LeftmostSlot(PieceKind kind) => LeftmostOccupied(Bank(kind));
    public int? LeftmostDwellingSlot => LeftmostSlot(PieceKind.Dwelling);

    // The building of this kind that would be built next (the leftmost in its bank), if any is left.
    public Piece? NextBuilding(PieceKind kind) => LeftmostSlot(kind) is int slot ? Bank(kind)[slot] : null;

    // Where a building coming back to the board goes: the rightmost empty slot. Null if none is empty.
    public static int? RightmostEmpty(ValueList<Piece?> bank)
    {
        for (var slot = bank.Count - 1; slot >= 0; slot--)
            if (bank[slot] is null) return slot;
        return null;
    }

    private static int? LeftmostOccupied(ValueList<Piece?> bank)
    {
        for (var slot = 0; slot < bank.Count; slot++)
            if (bank[slot] is not null) return slot;
        return null;
    }

    // Coins and workers only: priests are pieces, paid by the rules one at a time (Rules.PayPriest).
    // In debt (coins, workers or priests), or below 0 VP.
    public bool InDebt => Coins < 0 || Workers < 0 || PriestDebt > 0 || VictoryPoints < 0;
    // In debt as Strict counts it: coins, workers or priests owed. Below 0 VP is allowed even there (an auction bid can
    // put a player there; Andrew 2026-11-22), with a warning as in Lenient.
    public bool OwesResources => Coins < 0 || Workers < 0 || PriestDebt > 0;

    public PlayerState Pay(Cost cost) => this with { Coins = Coins - cost.Coins, Workers = Workers - cost.Workers };

    // The only priest that may be placed. Null if no priest is ready.
    public Piece? TopPriest => Priests.Count == 0 ? null : (Piece?)Priests[Priests.Count - 1];

    public int WorkersPerSpade => Factions.WorkersPerSpade(Faction, SpadeUpgrades);

    // What one paid spade costs now: workers (fewer with upgrades), or a priest for the Darklings; the Prospectors'
    // golden spades cost coins (fewer with their stronghold); the Enlightened's, power (SpadePower), so nothing here.
    public Cost SpadeCost => Faction switch
    {
        Faction.Prospectors => new Cost(Coins: StrongholdBuilt ? Config.ProspectorsSpadeCoinsWithStronghold : Config.ProspectorsSpadeCoins, Workers: 0),
        Faction.Enlightened => default,
        _ => Factions.SpadeCost(Faction, SpadeUpgrades),
    };

    // Enlightened: the power one paid spade costs now (3, less with upgrades); 0 for everyone else.
    public int SpadePower => Faction == Faction.Enlightened ? Config.EnlightenedSpadePower - SpadeUpgrades : 0;

    public bool HoldsFavour(int id) => FavourTiles.Any(tile => tile.Id == id);

    // Bonus actions are not stored as unlocked: they follow from what the player has, so they can never
    // disagree with it (and undoing the stronghold or the favour removes the bonus action too).
    public bool HasUnlocked(BonusActionId bonus) => BonusActions.Contains(bonus);

    public bool StrongholdBuilt => StrongholdBank.All(slot => slot is null);

    // Every bonus action unlocked: the stronghold's (if built), the pass tile's (if it has one), then the
    // favours' in the order taken. A stable order: a new favour's action goes at the end.
    public ValueList<BonusActionId> BonusActions
    {
        get
        {
            var unlocked = new List<BonusActionId>();
            if (StrongholdBuilt && Factions.StrongholdAction(Faction) is BonusAction fromStronghold)
                unlocked.Add(new BonusActionId(BonusSource.Stronghold, fromStronghold));
            if (PassTile is int passTile && PassTiles.All[passTile].BonusAction is BonusAction fromTile)
                unlocked.Add(new BonusActionId(BonusSource.PassTile, fromTile));
            if (ExtraPassTile is int extraTile && PassTiles.All[extraTile].BonusAction is BonusAction fromExtra)
                unlocked.Add(new BonusActionId(BonusSource.PassTile, fromExtra));
            foreach (var tile in FavourTiles)
                if (Favours.All[tile.Id].BonusAction is BonusAction fromFavour)
                    unlocked.Add(new BonusActionId(BonusSource.Favour, fromFavour));
            return new(unlocked.Distinct());
        }
    }

    // The total of one lasting favour effect over every favour held, e.g. VP per dwelling built.
    public int FavourBonus(Func<FavourDefinition, int> effect) =>
        FavourTiles.Sum(tile => effect(Favours.All[tile.Id]));
}
