namespace Core;

// What the view asks the rules: where a piece can go, what something costs, what is in range. Nothing here changes
// the game.
public static partial class Rules
{
    // ---------- Where a piece can go ----------

    // Every destination this piece could legally be moved to right now, or a reason why there are none.
    // Legality is decided by Apply itself: each candidate move is tried and its result kept or discarded.
    // So this query can never disagree with the rules, because it IS the rules.
    // Moves that are accepted with a warning (e.g. going into debt) count as legal.
    public static PieceOptions LegalDestinations(GameState state, Piece piece) =>
        Options(state, CandidateDestinations(state, piece.Kind), destination => MoveTo(state, piece, destination),
            "There is nowhere this piece can go.");

    // Every target this bonus action could legally be used on right now, or a reason why there are none.
    // Decided by Apply, exactly like LegalDestinations. Spade bonus actions and the double action have no target
    // of their own: use BonusSpades (then SpendSpades for each hex) or BonusDoubleAction directly.
    public static PieceOptions BonusOptions(GameState state, Faction faction, BonusActionId bonus)
    {
        if (bonus.Action == BonusAction.Shapeshift) // a terrain, not a hex or track: see ShapeshiftTargets
            return new PieceOptions(ValueList<Destination>.Empty, "The Shapeshifters choose a terrain for their home.");
        // The fan factions' stronghold actions have moves of their own (BonusMoveBridge, BonusRiverTokens, ...).
        if (bonus.Action is not (BonusAction.CultStep or BonusAction.TwoCultSteps or BonusAction.OneSpade or BonusAction.TwoSpades
                or BonusAction.SelkiesSpade or BonusAction.BuildOnHomeTerrain or BonusAction.FreeTradingPost
                or BonusAction.DoubleAction or BonusAction.Sandstorm))
            return new PieceOptions(ValueList<Destination>.Empty, "That bonus action has a move of its own.");
        var candidates = bonus.Action is BonusAction.CultStep or BonusAction.TwoCultSteps
            ? Enum.GetValues<Track>().Select(track => (Destination)new TrackDestination(track))
            : Enumerable.Range(0, state.Layout.HexCount).Select(hex => (Destination)new HexDestination(hex));
        return Options(state, candidates, destination => BonusMove(faction, bonus, destination),
            "There is nowhere this bonus action can be used right now.");
    }

    // The move that uses this bonus action on this target.
    public static Move BonusMove(Faction faction, BonusActionId bonus, Destination destination) => (bonus.Action, destination) switch
    {
        (BonusAction.CultStep or BonusAction.TwoCultSteps, TrackDestination d) => new BonusCultStep(faction, bonus.Source, d.Track),
        (BonusAction.OneSpade or BonusAction.TwoSpades or BonusAction.SelkiesSpade, _) => new BonusSpades(faction, bonus.Source),
        (BonusAction.BuildOnHomeTerrain, HexDestination d) => new BonusBuild(faction, bonus.Source, d.Hex),
        (BonusAction.FreeTradingPost, HexDestination d)    => new BonusTradingPost(faction, bonus.Source, d.Hex),
        (BonusAction.DoubleAction, _)                      => new BonusDoubleAction(faction, bonus.Source),
        (BonusAction.Sandstorm, HexDestination d)          => new BonusSandstorm(faction, bonus.Source, d.Hex),
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, $"Not a target for {bonus.Action}."),
    };

    // Tries the move for every candidate and keeps the accepted ones (see LegalDestinations).
    // nowhere: the reason given when candidates fail for different reasons.
    private static PieceOptions Options(GameState state, IEnumerable<Destination> candidates, Func<Destination, Move> moveFor,
        string nowhere)
    {
        var legal = new List<Destination>();
        var refusals = new HashSet<string>();

        foreach (var destination in candidates)
        {
            var result = Apply(state, moveFor(destination));
            if (result.Accepted)
                legal.Add(destination);
            else
                refusals.UnionWith(result.Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message));
        }

        if (legal.Count > 0)
            return new PieceOptions(new ValueList<Destination>(legal), null);

        // If every candidate failed for the same reason (wrong turn, not the leftmost, ...) report it.
        // Different reasons for different candidates means the piece is fine but every spot is unavailable.
        var reason = refusals.Count == 1 ? refusals.First() : nowhere;
        return new PieceOptions(ValueList<Destination>.Empty, reason);
    }

    // The move that puts this piece at this destination. The view uses this when a piece is dropped,
    // so it never needs to know which Move type belongs to which kind of piece or which phase.
    public static Move MoveTo(GameState state, Piece piece, Destination destination) => destination switch
    {
        HexDestination d when state.Phase == Phase.PlacingDwellings => new PlaceStartingDwelling(piece, d.Hex), // the Atlanteans' stronghold too
        HexDestination d when Buildings.IsUpgrade(piece.Kind) => new UpgradeBuilding(piece, d.Hex),
        HexDestination d when piece.Kind == PieceKind.Landscape => new PlaceLandscape(piece.Owner, d.Hex),
        // Just made home terrain (with spades, a transform or the sandstorm): the dwelling goes there as part of that.
        HexDestination d when piece.Kind == PieceKind.Dwelling && state.SpadesPending && state.SpadedHexes.Contains(d.Hex)
                                => new BuildOnSpadedHex(piece, d.Hex),
        // Owed a free dwelling (the Wisps' Lake, the Snow Shamans' masses of Ice).
        HexDestination d when piece.Kind == PieceKind.Dwelling && state.IsInGame(piece.Owner)
            && (state[piece.Owner].LakeDwellingOwed || state[piece.Owner].IceMassesOwed is { Count: > 0 })
                                => new PlaceFreeDwelling(piece, d.Hex),
        HexDestination d        => new BuildDwelling(piece, d.Hex),
        PriestSlotDestination d => new PlacePriest(piece, d.Track, d.Slot),
        _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, "Unknown destination type."),
    };

    // Every spot a piece of this kind could conceivably go, legal or not.
    // Buildings (dwellings and every upgrade) go on hexes; priests go in priest slots.
    private static IEnumerable<Destination> CandidateDestinations(GameState state, PieceKind kind) => kind switch
    {
        PieceKind.Priest => Enum.GetValues<Track>()
            .SelectMany(track => Enumerable.Range(0, Config.PriestSlotsPerZone)
                .Select(slot => (Destination)new PriestSlotDestination(track, slot))),
        _ => Enumerable.Range(0, state.Layout.HexCount)
            .Select(hex => (Destination)new HexDestination(hex)),
    };

    // ---------- Transforming ----------

    // Every terrain this hex can be turned into right now, each legal per Apply: with free spades while
    // the player is spending them (SpendSpades), otherwise as the turn's action (TransformTerrain).
    public static ValueList<TransformChoice> TransformChoices(GameState state, Faction faction, int hex)
    {
        if (!state.Layout.Contains(hex) || !state.IsInGame(faction))
            return ValueList<TransformChoice>.Empty;
        var spending = state.SpadesPending;
        var from = state.HexTerrain[hex];
        var player = state[faction];
        var reach = ReachCost(state, faction, ReachOf(state, faction, hex,
            allowSkip: !spending || ((state.Phase == Phase.Playing || TunnelsBetweenRounds(state, faction)) && !state.SkipUsed)));
        var choices = new List<TransformChoice>();
        // Volcano factions: only Volcano, for their own price (never with free spades: they get none).
        if (player.Kind == FactionKind.Volcano)
        {
            Move volcano = new TransformTerrain(faction, hex, Terrain.Volcano);
            if (!spending && VolcanoPrice(state, faction, hex) is int price && Apply(state, volcano).Accepted)
                choices.Add(new TransformChoice(Terrain.Volcano, 0, 0, reach, volcano, price));
            return new ValueList<TransformChoice>(choices);
        }
        // The wheel's terrains, and Ice for the ice factions.
        foreach (var to in player.Kind == FactionKind.Ice ? Terrains.Wheel.Add(Terrain.Ice) : Terrains.Wheel)
        {
            if (SpadesFor(state, faction, hex, to) is not int spades || spades == 0)
                continue;
            Move move = spending ? new SpendSpades(faction, hex, to) : new TransformTerrain(faction, hex, to);
            if (!Apply(state, move).Accepted)
                continue;
            var free = spending ? Math.Min(spades, player.SpadesOwed) : 0;
            choices.Add(new TransformChoice(to, spades, free, player.SpadeCost * (spades - free) + reach, move));
        }
        return new ValueList<TransformChoice>(choices);
    }

    // While free spades are being spent: every hex they could go on now.
    public static ValueList<int> SpadeTargets(GameState state, Faction faction) =>
        new(Enumerable.Range(0, state.Layout.HexCount).Where(hex => TransformChoices(state, faction, hex).Count > 0));

    // While free spades are being spent in the main phase: the hexes a dwelling could now be built on.
    // (Changelings with their stronghold: the dwelling beside it too.)
    public static ValueList<int> SpadedBuildTargets(GameState state, Faction faction) =>
        new(new[] { state[faction].NextBuilding(PieceKind.Dwelling), state[faction].StrongholdBuilt ? state[faction].ExtraDwelling : null }
            .OfType<Piece>()
            .SelectMany(dwelling => state.SpadedHexes.Where(hex => Apply(state, new BuildOnSpadedHex(dwelling, hex)).Accepted))
            .Distinct());

    // Shapeshifters: the terrains their home could move to now: basic, and no faction's home terrain (their own included).
    public static ValueList<Terrain> ShapeshiftTargets(GameState state, Faction faction) =>
        new(Terrains.Wheel.Where(terrain => state.Players.All(player => player.Home != terrain)));

    // ---------- Costs ----------

    // What paying for every spade to turn this hex into `to` costs this faction (workers per spade, by its spade
    // upgrades, or priests for the Darklings; the Giants always need 2 spades). Null if the hex cannot be turned
    // into it (not a basic terrain). A carpet flight or tunnel to reach the hex is extra (ReachCost).
    public static Cost? TransformCost(GameState state, Faction faction, int hex, Terrain to) =>
        SpadesFor(state, faction, hex, to) is int spades
            ? state[faction].SpadeCost * spades
            : null;

    // Everything a dwelling on this hex costs this faction in coins, workers and priests: the dwelling, the spades to
    // make it the terrain it builds on, and the carpet flight or tunnel if that is how the hex is reached. A volcano
    // faction's price for making it Volcano is separate (VolcanoPrice).
    public static Cost BuildCost(GameState state, Faction faction, int hex) =>
        DwellingCost(state, faction, hex)
        + (state[faction].BuildTerrain is Terrain build && SpadesFor(state, faction, hex, build) is int spades
            ? state[faction].SpadeCost * Math.Max(0, spades - ArchitectsDiscount(state, faction, hex)) // the Architects' bridges
            : default)
        + ReachCost(state, faction, ReachOf(state, faction, hex, allowSkip: true));

    // What upgrading the building on this hex to this kind costs. Only a trading post costs more
    // when no opponent's building is next to the hex; the other upgrades cost the same anywhere.
    public static Cost UpgradeCost(GameState state, PieceKind kind, Faction faction, int hex)
    {
        var cost = Factions.BuildingCost(faction, kind);
        if (kind != PieceKind.TradingPost && faction != Faction.ChildrenOfTheWyrm) // the Wyrm: every structure
            return cost;
        return OpponentNext(state, faction, hex) ? cost : cost + new Cost(cost.Coins, 0);
    }

    // What making this hex Volcano costs this volcano faction: power tokens removed (Dragonlords) or steps paid from
    // one track (Acolytes); more if it is another faction's home terrain. Null for other factions, or a hex that
    // can't become Volcano (not a basic terrain).
    public static int? VolcanoPrice(GameState state, Faction faction, int hex)
    {
        if (!state.Layout.Contains(hex) || !state.IsInGame(faction) || !Terrains.IsBasic(state.HexTerrain[hex]))
            return null;
        var home = state.IsOthersHome(faction, state.HexTerrain[hex]);
        return faction switch
        {
            Faction.Dragonlords => home ? Config.DragonlordsVolcanoHomeTokens : Config.DragonlordsVolcanoTokens,
            Faction.Acolytes => home ? Config.AcolytesVolcanoHomeSteps : Config.AcolytesVolcanoSteps,
            Faction.Firewalkers => home ? Config.FirewalkersVolcanoHomePoints : Config.FirewalkersVolcanoPoints, // VP
            _ => null, // the Kingdom of Ember: only by letting the lava flow
        };
    }

    // Acolytes: the tracks they could pay these steps from now (each has at least that many).
    public static ValueList<Track> TracksThatCanPay(GameState state, Faction faction, int steps) =>
        new(Enum.GetValues<Track>().Where(track => state[faction].TrackSteps[(int)track] >= steps));

    // Riverwalkers: what unlocking this terrain costs: 1 coin, or 2 if it is another faction's home terrain.
    public static int UnlockCost(GameState state, Faction faction, Terrain terrain) =>
        state.IsOthersHome(faction, terrain) ? Config.RiverwalkersUnlockHomeCoins : Config.RiverwalkersUnlockCoins;

    // What this power action costs this faction (the Yetis pay 1 less).
    public static int PowerActionCost(GameState state, Faction faction, int action) =>
        Math.Max(0, PowerActions.All[action].Cost - (faction == Faction.Yetis ? Config.YetisPowerActionDiscount : 0));

    // Could this faction take this power action this round, as far as who has taken it goes? The Yetis, with their
    // stronghold, may take any; everyone else only one nobody has taken.
    public static bool PowerActionOpen(GameState state, Faction faction, int action) =>
        !state.PowerActionsTaken.Contains(action) || (faction == Faction.Yetis && state.IsInGame(faction) && state[faction].StrongholdBuilt);

    // The extra cost of reaching a hex this way: a priest for the Fakirs' carpet, workers for the Dwarves' tunnel.
    public static Cost ReachCost(GameState state, Faction faction, Reach reach) => reach switch
    {
        Reach.Carpet => new Cost(Coins: 0, Workers: 0, Priests: Config.FakirsCarpetPriests),
        Reach.Tunnel => state.Phase == Phase.RoundEndSpades ? default : new Cost(Coins: 0, Workers: TunnelWorkers(state, faction)),
        _ => default,
    };

    private static int TunnelWorkers(GameState state, Faction faction) =>
        state[faction].StrongholdBuilt ? Config.DwarvesTunnelWorkersWithStronghold : Config.DwarvesTunnelWorkers;

    // Can this faction take this favour (ignoring whose turn it is)? Some must be left, and nobody
    // may hold two of the same favour.
    public static bool CanTakeFavour(GameState state, Faction faction, int id) =>
        id >= 0 && id < Favours.Count && state.FavourBank[id].Count > 0 && !state[faction].HoldsFavour(id);

    // ---------- Range ----------

    // Can this faction reach this hex from its buildings (with a carpet flight or tunnel if it has one)?
    public static bool IsInRange(GameState state, Faction faction, int hex) =>
        ReachOf(state, faction, hex, allowSkip: true) != Reach.None;

    // How this faction reaches this hex from its buildings, if it can:
    //   Adjacent: next to one of its buildings (its bridges count as touching);
    //   Shipping: across at most ShippingOf river hexes;
    //   Carpet (Fakirs): not adjacent, but at most CarpetRange hexes of any kind (rivers too) skipped;
    //   Tunnel (Dwarves): not adjacent, with exactly one hex of any kind skipped.
    // allowSkip: whether carpets and tunnels may be used (not for spades between rounds).
    // Rivers themselves are never reachable (nothing is built on them).
    public static Reach ReachOf(GameState state, Faction faction, int hex, bool allowSkip)
    {
        if (!state.Layout.Contains(hex) || state.HexTerrain[hex] == Terrain.River || !state.IsInGame(faction))
            return Reach.None;
        // The Riverwalkers reach only across rivers (by shipping), never by land.
        if (faction == Faction.Riverwalkers)
            return RiversCrossedTo(state, faction, hex, byLand: false) is int _ ? Reach.Shipping : Reach.None;
        if (RiversCrossedTo(state, faction, hex) is int crossed)
            return crossed == 0 ? Reach.Adjacent : Reach.Shipping;
        var skip = SkipRange(state, faction);
        if (!allowSkip || skip == 0)
            return Reach.None;
        var skipping = faction == Faction.Dwarves ? Reach.Tunnel : Reach.Carpet;
        if (DistanceFrom(state, state.HexesBuiltOnBy(faction), hex, skip + 1) is int distance && distance >= 2)
            return skipping;
        // Dwarves: from their Great Tunnel, a tunnel skips 2 hexes.
        if (faction == Faction.Dwarves && state.LandscapeHex(faction) is int tunnel
            && DistanceFrom(state, new[] { tunnel }, hex, Landscapes.DwarvesTunnelSkip + 1) is int fromTunnel && fromTunnel >= 2)
            return skipping;
        return Reach.None;
    }

    // How many hexes this faction may skip to reach one: the Fakirs' carpet range, 1 for the Dwarves' tunnel, else 0.
    public static int SkipRange(GameState state, Faction faction) => faction switch
    {
        Faction.Fakirs => state.CarpetRange(faction),
        Faction.Dwarves => 1,
        _ => 0,
    };

    // The fewest rivers crossed to reach this land hex from the faction's buildings, within its shipping, or null.
    // Breadth-first: start from the faction's buildings, and step outwards only through rivers.
    // "frontier" is the set of hexes reached so far by crossing exactly "riversCrossed" rivers.
    // byLand false: hexes next to the buildings don't count unless reached across a river (Riverwalkers).
    private static int? RiversCrossedTo(GameState state, Faction faction, int hex, bool byLand = true)
    {
        var shipping = state.ShippingOf(faction);
        var frontier = state.HexesBuiltOnBy(faction).ToList();
        var riversSeen = new HashSet<int>();

        for (var riversCrossed = 0; frontier.Count > 0; riversCrossed++)
        {
            var nextFrontier = new List<int>();
            foreach (var from in frontier)
            {
                foreach (var neighbour in AdjacentHexes(state, faction, from)) // bridges count as touching
                {
                    if (state.HexTerrain[neighbour] != Terrain.River)
                    {
                        // land next to a building, or next to a reachable river (a structure on a river: that river)
                        if (neighbour == hex && (byLand || riversCrossed > 0 || state.HexTerrain[from] == Terrain.River))
                            return riversCrossed;
                    }
                    else if (riversCrossed < shipping && riversSeen.Add(neighbour))
                    {
                        nextFrontier.Add(neighbour); // one more river crossed; still within shipping
                    }
                }
            }
            frontier = nextFrontier;
        }
        return null;
    }

    // How many hexes away (counting through any hex, land or river; bridges ignored) the nearest of these hexes is, if
    // it is at most maxDistance away.
    private static int? DistanceFrom(GameState state, IEnumerable<int> starts, int hex, int maxDistance)
    {
        var seen = starts.ToHashSet();
        var frontier = seen.ToList();
        for (var distance = 0; distance <= maxDistance && frontier.Count > 0; distance++)
        {
            if (frontier.Contains(hex))
                return distance;
            frontier = frontier.SelectMany(state.Layout.Neighbours).Where(seen.Add).ToList();
        }
        return null;
    }
}
