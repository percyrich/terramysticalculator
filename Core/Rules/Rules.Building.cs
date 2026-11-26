namespace Core;

// Building: dwellings, upgrades (and what a stronghold does as it is built), shipping and spade upgrades.
// Each is the turn's action.
public static partial class Rules
{
    // ---------- Dwellings ----------

    private static MoveResult ApplyBuildDwelling(GameState state, BuildDwelling move) => BuildDwellingWith(state, move, freeSpades: 0);

    // freeSpades: spades the transform gets for nothing (the Selkies' stronghold action).
    private static MoveResult BuildDwellingWith(GameState state, BuildDwelling move, int freeSpades)
    {
        var dwelling = move.Dwelling;
        if (dwelling.Kind != PieceKind.Dwelling)
            return MoveResult.Rejected(state, "That piece is not a dwelling.");
        if (CheckAction(state, dwelling.Owner) is { } refusal)
            return refusal;

        var owner = dwelling.Owner;
        var player = state[owner];
        if (CheckDwellingAvailable(state, dwelling) is { } bankRefusal)
            return bankRefusal;
        if (owner == Faction.Selkies && state.Layout.Contains(move.Hex) && state.HexTerrain[move.Hex] == Terrain.River)
            return SelkiesRiverDwelling(state, dwelling, move.Hex);
        if (CheckHexTarget(state, owner, move.Hex, allowSkip: true, out var reach) is { } hexRefusal)
            return hexRefusal;
        var terrain = state.HexTerrain[move.Hex];
        var target = DwellingTerrain(state, dwelling); // the Changelings': the dwelling's own terrain
        var transform = owner == Faction.Changelings ? terrain != target : !player.CanBuildOn(terrain);
        if (transform && owner == Faction.Riverwalkers)
            return MoveResult.Rejected(state, $"The {Name(owner)} can't transform terrain, and haven't unlocked {terrain} (a priest still stands on it on their wheel).");
        if (transform && owner == Faction.Geologists)
            return MoveResult.Rejected(state, $"The {Name(owner)} can't transform terrain: they build on a terrain a glowing rock could cover (their ring's, or next to a covered one on their wheel).");
        if (transform && player.Kind == FactionKind.Volcano)
        {
            if ((CheckTransformTo(state, owner, move.Hex, Terrain.Volcano) ?? CheckVolcanoPrice(state, owner, move.Hex)) is { } volcanoRefusal)
                return volcanoRefusal;
        }
        else if (transform && player.SpadesNeeded(terrain, target!.Value) is null)
            return MoveResult.Rejected(state, $"{terrain} cannot be transformed.");
        var discount = ArchitectsDiscount(state, owner, move.Hex); // only when a dwelling is built there
        if (transform && player.Kind != FactionKind.Volcano
            && CheckSpadePower(state, owner, Math.Max(0, (SpadesFor(state, owner, move.Hex, target!.Value) ?? 0) - discount - freeSpades)) is { } powerRefusal)
            return powerRefusal;

        // Reach the hex (carpet or tunnel), make it the terrain the faction builds on (paying every spade, or the
        // volcano faction's price), then build.
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = PayForReach(state, owner, move.Hex, reach, events, warnings);
        if (transform && player.Kind == FactionKind.Volcano)
            next = MakeVolcano(next, owner, move.Hex, events);
        else if (transform)
            next = Transform(next, owner, move.Hex, target!.Value, freeSpades, events, warnings, discount);
        next = PlaceDwelling(next, dwelling, move.Hex, DwellingCost(next, owner, move.Hex), events);
        if (owner == Faction.Geologists) // a rock covers the terrain built on
            next = CoverRock(next, owner, terrain, events);
        warnings.AddRange(DebtWarning(next[owner]));
        return MoveResult.Ok(next, events, warnings);
    }

    // Takes the dwelling from its bank, pays the cost, builds on the hex (already home terrain), and applies
    // everything a new dwelling triggers. Marks the action as taken.
    // The caller has already checked that all of this is allowed.
    private static GameState PlaceDwelling(GameState state, Piece dwelling, int hex, Cost cost, List<GameEvent> events)
    {
        var player = state[dwelling.Owner];
        var extra = player.ExtraDwelling == dwelling; // Changelings: the one beside their stronghold (its spot is slot 8)
        var fromSlot = extra ? player.DwellingBank.Count : player.DwellingBank.IndexOf(dwelling);
        var built = extra ? player.Pay(cost) with { ExtraDwelling = null }
            : player.Pay(cost) with { DwellingBank = player.DwellingBank.SetItem(fromSlot, null) };

        var next = state.WithPlayer(built) with { Map = state.Map.SetItem(hex, dwelling), ActionTaken = true };
        events.Add(new DwellingBuilt(dwelling, fromSlot, hex));
        next = NeighboursGainPower(next, dwelling.Owner, hex, events);
        next = GainVictoryPoints(next, dwelling.Owner, RoundTilePoints(next, dwelling.Owner, tile => tile.VictoryPointsFor(PieceKind.Dwelling)), events,
            new PointsArea.RoundTile(), new PointsArea.OnHex(hex));
        return FavourPoints(next, dwelling.Owner, f => f.VictoryPointsPerDwelling, hex, events);
    }

    // ---------- Upgrading a building ----------

    private static MoveResult ApplyUpgradeBuilding(GameState state, UpgradeBuilding move) =>
        Upgrade(state, move.Building, move.Hex, bonus: null, move.ReturnBesideStronghold);

    // Every upgrade works the same way; the kind of the new building says which one it is.
    // bonus: null for a normal upgrade (the player's action, paid); otherwise the bonus action that
    // makes it free (and is then used up).
    // returnBeside: Changelings with their stronghold, a dwelling going back beside it (UpgradeBuilding).
    private static MoveResult Upgrade(GameState state, Piece building, int hex, BonusActionId? bonus, bool returnBeside = false)
    {
        var owner = building.Owner;
        if (Buildings.UpgradedFrom(building.Kind) is not PieceKind fromKind)
            return MoveResult.Rejected(state, "Only trading posts, temples, strongholds and sanctuaries are built by upgrading.");
        var refusal = bonus is BonusActionId free ? CheckBonusAction(state, owner, free) : CheckAction(state, owner);
        if (refusal is not null)
            return refusal;

        var player = state[owner];
        var noun = Buildings.Noun(building.Kind);
        var fromNoun = Buildings.Noun(fromKind);
        var bank = player.Bank(building.Kind);
        if (CheckLeftmostInBank(state, bank, building, noun) is { } bankRefusal)
            return bankRefusal;
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.Map[hex] is not Piece replaced)
            return MoveResult.Rejected(state, $"There is no {fromNoun} there to upgrade.");
        if (replaced.Owner != owner)
            return MoveResult.Rejected(state, $"That building belongs to the {Name(replaced.Owner)}.");
        if (replaced.Kind != fromKind)
            return MoveResult.Rejected(state, $"Only a {fromNoun} can be upgraded to a {noun}.");
        if (owner == Faction.KingdomOfEmber && fromKind == PieceKind.Dwelling && state.LavaAt(hex) > 0)
            return MoveResult.Rejected(state, "A dwelling with lava under it (more than one volcano tile) can't be upgraded.");
        if (owner == Faction.Selkies && state.HexTerrain[hex] == Terrain.River)
            return MoveResult.Rejected(state, $"The {Name(owner)}' dwellings on rivers can't be upgraded.");
        if (owner == Faction.Changelings && ChangelingLetter(building) != ChangelingLetter(replaced))
            return MoveResult.Rejected(state,
                $"The {Name(owner)}' next {noun} is {ChangelingTerrain(player, building)}: it can only replace a {fromNoun} of that colour.");

        // The replaced building comes off the map and goes back to the rightmost empty slot of its own
        // bank, covering that slot's income again. A slot is always free: it came out of that bank.
        // Changelings with their stronghold: a dwelling may go back beside it instead (and must, with its row full).
        var fromBank = player.Bank(fromKind);
        var besideStronghold = owner == Faction.Changelings && fromKind == PieceKind.Dwelling && player.StrongholdBuilt
            && player.ExtraDwelling is null && (returnBeside || PlayerState.RightmostEmpty(fromBank) is null);
        if (returnBeside && !besideStronghold)
            return MoveResult.Rejected(state, "Only the Changelings, with their stronghold, put a dwelling back beside it, and only into its empty spot.");
        var returnSlot = besideStronghold ? fromBank.Count : PlayerState.RightmostEmpty(fromBank)!.Value;
        var fromSlot = bank.IndexOf(building);
        var cost = bonus is null ? UpgradeCost(state, building.Kind, owner, hex) : default(Cost);
        var upgraded = player.Pay(cost).WithBank(building.Kind, bank.SetItem(fromSlot, null));
        upgraded = besideStronghold ? upgraded with { ExtraDwelling = replaced } : upgraded.WithBank(fromKind, fromBank.SetItem(returnSlot, replaced));

        var events = new List<GameEvent>
        {
            new BuildingUpgraded(building, fromSlot, hex),
            new BuildingReturned(replaced, hex, returnSlot),
        };
        var warnings = new List<Issue>(bonus is null ? DebtWarning(upgraded) : MoveResult.NoIssues);
        var next = state.WithPlayer(upgraded) with
        {
            Map = state.Map.SetItem(hex, building),
            ActionTaken = true,
        };
        for (var i = 0; i < cost.Priests; i++) // the Enlightened's buildings cost priests
            next = PayPriest(next, owner, events, warnings);
        // Upgrading counts as building on that hex, so neighbours gain power as for a new dwelling.
        next = NeighboursGainPower(next, owner, hex, events);
        if (building.Kind == PieceKind.TradingPost)
            next = FavourPoints(next, owner, f => f.VictoryPointsPerTradingPost, hex, events);
        next = EarnFavours(next, owner, Factions.FavoursFor(owner, building.Kind), events, warnings);
        next = GainVictoryPoints(next, owner, RoundTilePoints(next, owner, tile => tile.VictoryPointsFor(building.Kind)), events,
            new PointsArea.RoundTile(), new PointsArea.OnHex(hex));
        if (building.Kind == PieceKind.Stronghold)
            next = OnStrongholdBuilt(next, owner, hex, events, warnings);
        // Goblins: a treasure with every temple and sanctuary.
        if (owner == Faction.Goblins && building.Kind is PieceKind.Temple or PieceKind.Sanctuary)
        {
            next = next.WithPlayer(next[owner] with { Treasures = next[owner].Treasures + 1 });
            events.Add(new TreasureGained(owner, next[owner].Treasures));
        }
        // Wisps: a free spade with every trading post, for a hex next to it.
        if (owner == Faction.Wisps && building.Kind == PieceKind.TradingPost)
            next = WispsSpade(next, owner, hex, events, warnings);
        // Kingdom of Ember: 1 power and a lava tile under a dwelling with every upgrade.
        if (owner == Faction.KingdomOfEmber)
        {
            next = GainPower(next, owner, Config.EmberUpgradePower, PowerSource.Upgrade, events);
            next = OweLava(next, owner, 1, anyStructure: false, events, warnings);
        }
        // Ice Maidens with their Winterfall: a spade with every temple, spent at once.
        if (building.Kind == PieceKind.Temple && owner == Faction.IceMaidens && next.HasLandscape(owner))
            next = GainSpades(next with { SpadedHexes = ValueList<int>.Empty }, owner, Landscapes.IceMaidensSpadesPerTemple, events, warnings);
        if (bonus is BonusActionId used)
            next = MarkBonusActionUsed(next, owner, used);

        return MoveResult.Ok(next, events, warnings);
    }

    // What a faction's stronghold does at once when it is built (its lasting effects follow from StrongholdBuilt).
    private static GameState OnStrongholdBuilt(GameState state, Faction faction, int hex, List<GameEvent> events,
        List<Issue> warnings) =>
        faction switch
        {
            Faction.Auren => EarnFavours(state, faction, Config.AurenStrongholdFavours, events, warnings),
            Faction.Halflings => GainSpades(state with { SpadedHexes = ValueList<int>.Empty, RewardSpades = true }, faction,
                Config.HalflingsStrongholdSpades, events),
            Faction.Cultists => GainVictoryPoints(state, faction, Config.CultistsStrongholdVictoryPoints, events,
                new PointsArea.OnHex(hex), new PointsArea.OnHex(hex)),
            // A reward after the action: held (PowerHeld) until the Alchemists take it (TakeHeldPower), at the latest at
            // End turn.
            Faction.Alchemists => state.WithPlayer(state[faction] with { PowerHeld = state[faction].PowerHeld + Config.AlchemistsStrongholdPower }),
            Faction.Darklings => state.WithPlayer(state[faction] with { PriestExchangesLeft = Config.DarklingsPriestExchange }),
            Faction.Mermaids => FreeShippingUpgrade(state, faction, new PointsArea.OnHex(hex), events, warnings),
            Faction.Dragonlords => AddTokens(state, faction, 1, state.Players.Count, events),
            Faction.Riverwalkers => FreeBridges(state, faction, Config.RiverwalkersStrongholdBridges, warnings),
            Faction.ChaosMagicians => OfferPortal(state, faction, events),
            _ => OnFanStrongholdBuilt(state, faction, hex, events, warnings), // Rules.FanFactions.cs
        };

    // The hex of the faction's stronghold on the map, if built.
    public static int? StrongholdHex(GameState state, Faction faction)
    {
        for (var hex = 0; hex < state.Map.Count; hex++)
            if (state.Map[hex] is { Kind: PieceKind.Stronghold } piece && piece.Owner == faction)
                return hex;
        return null;
    }

    // ---------- Shipping and spade upgrades ----------

    private static MoveResult ApplyUpgradeShipping(GameState state, UpgradeShipping move)
    {
        if (state.IsInGame(move.Player) && state[move.Player].LandscapeOffers.HasFlag(LandscapeOffer.Shipping))
            return LandscapeShippingUpgrade(state, move.Player);
        if (move.Player == Faction.SnowShamans && state.IsInGame(move.Player))
            return SnowShamansFreeUpgrade(state, move.Player, shipping: true);
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (!Factions.HasShipping(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no shipping to upgrade.");
        if (player.Shipping >= Factions.MaxShipping(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are already at their highest shipping ({player.Shipping}).");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var upgradesBefore = player.Shipping - Factions.StartingShipping(move.Player);
        var next = PayPriest(state, move.Player, events, warnings);
        for (var i = 0; i < Factions.ShippingUpgradeCost(move.Player).Priests; i++) // the Geologists' second priest
            next = PayPriest(next, move.Player, events, warnings);
        var paid = next[move.Player].Pay(Factions.ShippingUpgradeCost(move.Player)) with { Shipping = player.Shipping + 1 };
        next = next.WithPlayer(paid) with { ActionTaken = true };
        events.Add(new ShippingUpgraded(move.Player, paid.Shipping));
        next = GainVictoryPoints(next, move.Player, Config.ShippingUpgradeVictoryPoints(upgradesBefore), events,
            new PointsArea.Shipping(), new PointsArea.Shipping());
        warnings.AddRange(DebtWarning(paid));
        return MoveResult.Ok(next, events, warnings);
    }

    private static MoveResult ApplyUpgradeSpades(GameState state, UpgradeSpades move)
    {
        if (state.IsInGame(move.Player) && state[move.Player].LandscapeOffers.HasFlag(LandscapeOffer.Spades))
            return LandscapeSpadeUpgrade(state, move.Player);
        if (move.Player == Faction.SnowShamans && state.IsInGame(move.Player))
            return SnowShamansFreeUpgrade(state, move.Player, shipping: false);
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (Factions.MaxSpadeUpgrades(move.Player) == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} cannot upgrade their spades.");
        if (player.SpadeUpgrades >= Factions.MaxSpadeUpgrades(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} cannot upgrade their spades any further.");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = PayPriest(state, move.Player, events, warnings);
        var paid = next[move.Player].Pay(Factions.SpadeUpgradeCost(move.Player)) with { SpadeUpgrades = player.SpadeUpgrades + 1 };
        next = next.WithPlayer(paid) with { ActionTaken = true };
        next = GainVictoryPoints(next, move.Player, Factions.SpadeUpgradeVictoryPoints(move.Player), events,
            new PointsArea.Spades(), new PointsArea.Spades());
        warnings.AddRange(DebtWarning(paid));
        return MoveResult.Ok(next, events, warnings);
    }
}
