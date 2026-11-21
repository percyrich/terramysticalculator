namespace Core;

// Adjacency, bridges and towns.
public static partial class Rules
{
    // ---------- Adjacency ----------

    // The hexes adjacent to this one for this faction: touching it, or joined to it by one of its bridges.
    // Children of the Wyrm: also the land hexes joined to it by a chain of their power tokens on rivers.
    public static IEnumerable<int> AdjacentHexes(GameState state, Faction faction, int hex)
    {
        var near = state.Layout.Neighbours(hex).Concat(state.Bridges
            .Where(bridge => bridge.Owner == faction && (bridge.HexA == hex || bridge.HexB == hex))
            .Select(bridge => bridge.HexA == hex ? bridge.HexB : bridge.HexA));
        return faction == Faction.ChildrenOfTheWyrm && state.WyrmTokenHexes.Count > 0
            ? near.Concat(WyrmChainHexes(state, hex)).Distinct()
            : near;
    }

    private static bool OwnsBuildingAt(GameState state, Faction faction, int hex) =>
        state.Map[hex] is { } piece && piece.Owner == faction;

    // ---------- Bridges ----------

    // Every place a bridge could ever go on this map: two land hexes on either side of the edge between
    // two touching river hexes (the two hexes share exactly those two neighbours). HexA < HexB.
    // At the board's edge one of the two may be off the board: the other, on it, must be river (as on BGA: the Fire &
    // Ice map's B13–D13).
    public static IEnumerable<(int HexA, int HexB)> BridgeEdges(GameState state)
    {
        var layout = state.Layout;
        bool IsRiver(int hex) => state.HexTerrain[hex] == Terrain.River;
        // Doubled coordinates (MapLayout.Neighbours): x = 2 × column, plus 1 in shifted rows. Places off the board
        // (beyond a row's ends, or above or below the rows) are kept as places, with no hex (-1).
        (int Row, int X) At(int hex)
        {
            var (row, column) = layout.Position(hex);
            return (row, 2 * column + (layout.IsShifted(row) ? 1 : 0));
        }
        int HexAt(int row, int x)
        {
            if (row < 0 || row >= layout.RowCount)
                return -1;
            var doubled = x - (layout.IsShifted(row) ? 1 : 0);
            return doubled < 0 || doubled % 2 != 0 ? -1 : layout.IndexOf(row, doubled / 2);
        }
        bool IsPlace(int row, int x) => row < 0 || row >= layout.RowCount || (x - (layout.IsShifted(row) ? 1 : 0)) % 2 == 0;
        var steps = new[] { (0, -2), (0, 2), (-1, -1), (-1, 1), (1, -1), (1, 1) };
        HashSet<(int, int)> Around(int hex)
        {
            var (row, x) = At(hex);
            return steps.Select(step => (row + step.Item1, x + step.Item2)).Where(p => IsPlace(p.Item1, p.Item2)).ToHashSet();
        }
        for (var a = 0; a < layout.HexCount; a++)
        {
            if (IsRiver(a))
                continue;
            var aNeighbours = layout.Neighbours(a).ToHashSet();
            var aAround = Around(a);
            for (var b = a + 1; b < layout.HexCount; b++)
            {
                if (IsRiver(b) || aNeighbours.Contains(b))
                    continue;
                var shared = Around(b).Where(aAround.Contains).ToList();
                if (shared.Count != 2)
                    continue;
                var hexes = shared.Select(p => HexAt(p.Item1, p.Item2)).ToList();
                var onBoard = hexes.Where(hex => hex >= 0).ToList();
                if (onBoard.Count == 0 || !onBoard.All(IsRiver))
                    continue;
                if (onBoard.Count == 2 && !layout.Neighbours(onBoard[0]).Contains(onBoard[1]))
                    continue;
                yield return (a, b);
            }
        }
    }

    // Where this faction could place a bridge now: a free bridge edge with one of its buildings at an end.
    // The Engineers' land bridges from their Bridge Tower are among them (LandBridgeEdges).
    public static ValueList<(int HexA, int HexB)> BridgeSpots(GameState state, Faction faction) =>
        new(BridgeEdges(state).Concat(LandBridgeEdges(state, faction)).Distinct().Where(edge =>
            !state.IsBridged(edge.HexA, edge.HexB)
            && (OwnsBuildingAt(state, faction, edge.HexA) || OwnsBuildingAt(state, faction, edge.HexB))));

    // Engineers' book action: 2 workers for a bridge, placed at once (PlaceBridge). The turn's action.
    private static MoveResult ApplyBuildBridge(GameState state, BuildBridge move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player is not (Faction.Engineers or Faction.Architects or Faction.Atlanteans))
            return MoveResult.Rejected(state, state.Options.FanFactions
                ? "Only the Engineers, Architects and Atlanteans can build a bridge as an action."
                : "Only the Engineers can build a bridge as an action.");
        var player = state[move.Player];
        if (state.BridgesLeft(move.Player) - player.BridgesOwed <= 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no bridges left.");
        if (BridgeSpots(state, move.Player).Count == 0)
            return MoveResult.Rejected(state, $"There is nowhere the {Name(move.Player)} could place a bridge.");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        GameState next;
        if (move.Player == Faction.Architects) // a priest
        {
            next = state.WithPlayer(player with { BridgesOwed = player.BridgesOwed + 1 }) with { ActionTaken = true };
            for (var i = 0; i < Config.ArchitectsBridgePriests; i++)
                next = PayPriest(next, move.Player, events, warnings);
        }
        else
        {
            var paid = player.Pay(new Cost(Coins: 0, Workers: Config.EngineersBridgeWorkers)) with { BridgesOwed = player.BridgesOwed + 1 };
            next = state.WithPlayer(paid) with { ActionTaken = true };
        }
        warnings.AddRange(DebtWarning(next[move.Player]));
        return MoveResult.Ok(next, events, warnings);
    }

    private static MoveResult ApplyPlaceBridge(GameState state, PlaceBridge move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (state[move.Player].BridgesOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no bridge to place.");
        var (a, b) = (Math.Min(move.HexA, move.HexB), Math.Max(move.HexA, move.HexB));
        if (!state.Layout.Contains(a) || !state.Layout.Contains(b)
            || !(BridgeEdges(state).Contains((a, b)) || LandBridgeEdges(state, move.Player).Contains((a, b))))
            return MoveResult.Rejected(state, "A bridge must join two land hexes across the edge between two river hexes.");
        if (state.IsBridged(a, b))
            return MoveResult.Rejected(state, "There is already a bridge there.");
        if (!OwnsBuildingAt(state, move.Player, a) && !OwnsBuildingAt(state, move.Player, b))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need a building at one end of the bridge.");

        var bridge = new Bridge(move.Player, a, b);
        var player = state[move.Player];
        var next = state.WithPlayer(player with { BridgesOwed = player.BridgesOwed - 1, BridgesOptional = player.BridgesOwed > 1 && player.BridgesOptional }) with
        {
            Bridges = state.Bridges.Add(bridge),
        };
        return MoveResult.Ok(next);
    }

    // Riverwalkers' stronghold bridges: the ones not placed yet are given up.
    private static MoveResult ApplySkipBridges(GameState state, SkipBridges move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.BridgesOwed == 0 || !player.BridgesOptional)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no free bridges to skip.");
        var next = state.WithPlayer(player with { BridgesOwed = 0, BridgesOptional = false });
        return MoveResult.Ok(next, new BridgesSkipped(move.Player, player.BridgesOwed));
    }

    // Riverwalkers' stronghold: up to this many bridges, placed at once for free; any may be left unplaced (SkipBridges).
    private static GameState FreeBridges(GameState state, Faction faction, int count, List<Issue> warnings)
    {
        var player = state[faction];
        var bridges = Math.Min(count, state.BridgesLeft(faction) - player.BridgesOwed);
        if (bridges <= 0 || BridgeSpots(state, faction).Count == 0)
        {
            warnings.Add(new Issue(Severity.Note, $"The {Name(faction)} have no bridge left or nowhere to place one, so their free bridges are lost."));
            return state;
        }
        return state.WithPlayer(player with { BridgesOwed = player.BridgesOwed + bridges, BridgesOptional = true });
    }

    // ---------- Towns ----------

    // The power a town needs for this faction (less with the favour that lowers it).
    public static int TownPowerNeeded(GameState state, Faction faction) =>
        Config.TownPower - state[faction].FavourBonus(f => f.TownPowerReduction);

    // Every group of this faction's buildings that would form a town now: joined by adjacency (its
    // bridges included, shipping not), none of them already in a town, with enough power and enough
    // buildings (fewer with the sanctuary). Empty when no key token is left to form one with.
    public static ValueList<ValueList<int>> FormableTowns(GameState state, Faction faction)
    {
        if (!state.IsInGame(faction) || state.KeyBank.All(stack => stack.Count == 0))
            return ValueList<ValueList<int>>.Empty;

        var player = state[faction];
        var needed = TownPowerNeeded(state, faction);
        var towns = BuildingGroups(state, faction, withoutReef: true)
            .Where(group => (!group.Any(player.TownHexes.Contains) && IsTown(state, faction, group, needed))
                || CityGrows(state, faction, group, needed))
            .Select(group => new ValueList<int>(group.OrderBy(hex => hex)));
        return new ValueList<ValueList<int>>(towns);
    }

    // Mermaids only: every river hex (without a key on it) where a key token would join two or more groups of
    // their buildings, none already in a town, into a town (enough power and buildings together). Optional:
    // unlike FormableTowns, these never hold up the end of the turn.
    public static ValueList<RiverTown> RiverTowns(GameState state, Faction faction)
    {
        if (faction != Faction.Mermaids || !state.IsInGame(faction) || state.KeyBank.All(stack => stack.Count == 0))
            return ValueList<RiverTown>.Empty;

        // A river town is always the Mermaids' free choice, never forced. The Coral Reef
        // takes part in a town only this way, with the key on the Reef itself (its town is it and the untowned groups
        // touching it). Another river hex joining groups that the Reef already joins is not offered.
        var player = state[faction];
        var needed = TownPowerNeeded(state, faction);
        var groups = BuildingGroups(state, faction, withoutReef: true).Where(group => !group.Any(player.TownHexes.Contains)).ToList();
        var reef = state.LandscapeHex(faction) is int r && !player.TownHexes.Contains(r) ? r : (int?)null;
        List<List<int>> Touching(int river)
        {
            var touching = state.Layout.Neighbours(river).ToHashSet();
            return groups.Where(group => group.Any(touching.Contains)).ToList();
        }
        var byReef = reef is int coral ? Touching(coral) : new List<List<int>>();
        var towns = new List<RiverTown>();
        for (var river = 0; river < state.Layout.HexCount; river++)
        {
            if (state.HexTerrain[river] != Terrain.River || state.PlacedKeys.Any(key => key.Hex == river))
                continue;
            if (river == reef)
            {
                var withReef = byReef.SelectMany(group => group).Append(river).OrderBy(hex => hex).ToList();
                if (byReef.Count > 0 && IsTown(state, faction, withReef, needed))
                    towns.Add(new RiverTown(river, new ValueList<int>(withReef)));
                continue;
            }
            if (state.Map[river] is not null) // never on another piece on a river (a landscape, a Selkies' dwelling)
                continue;
            var joined = Touching(river);
            if (joined.Count < 2 || joined.Count(byReef.Contains) >= 2)
                continue;
            var hexes = joined.SelectMany(group => group).OrderBy(hex => hex).ToList();
            if (IsTown(state, faction, hexes, needed))
                towns.Add(new RiverTown(river, new ValueList<int>(hexes)));
        }
        return new ValueList<RiverTown>(towns);
    }

    // The faction's buildings in groups joined by adjacency (its bridges included, shipping not).
    // withoutReef: the Mermaids' Coral Reef left out (it joins a town only through their river-town choice).
    private static List<List<int>> BuildingGroups(GameState state, Faction faction, bool withoutReef = false)
    {
        var groups = new List<List<int>>();
        var seen = new HashSet<int>();
        if (withoutReef && faction == Faction.Mermaids && state.LandscapeHex(faction) is int reefHex)
            seen.Add(reefHex);
        foreach (var start in state.HexesBuiltOnBy(faction))
        {
            if (!seen.Add(start))
                continue;
            // Breadth-first: every building reachable from this one through adjacent buildings.
            var group = new List<int> { start };
            for (var i = 0; i < group.Count; i++)
                foreach (var n in AdjacentHexes(state, faction, group[i]))
                    if (OwnsBuildingAt(state, faction, n) && seen.Add(n))
                        group.Add(n);
            groups.Add(group);
        }
        return groups;
    }

    // Enough power and enough buildings (fewer with the sanctuary) for a town.
    private static bool IsTown(GameState state, Faction faction, IReadOnlyCollection<int> hexes, int powerNeeded)
    {
        var power = hexes.Sum(hex => Factions.BuildingPower(faction, state.Map[hex]!.Value.Kind)) + BridgePower(state, faction, hexes);
        // The sanctuary lowers the buildings needed (and the Dynion Geifr's stronghold does the same).
        var hasSanctuary = hexes.Any(hex => state.Map[hex]!.Value.Kind == PieceKind.Sanctuary
            || (faction == Faction.DynionGeifr && state.Map[hex]!.Value.Kind == PieceKind.Stronghold));
        var enough = hexes.Count >= Config.TownBuildings || (hasSanctuary && hexes.Count >= Config.TownBuildingsWithSanctuary);
        return enough && power >= powerNeeded;
    }

    // The town is formed by placing the key token on one of its hexes. Any time during the player's turn;
    // the turn cannot end while a town could be formed.
    // Atlanteans: their starting town, after the first structures are placed: the key on their stronghold.
    private static MoveResult ApplyFormTown(GameState state, FormTown move)
    {
        var startingTown = state.Phase == Phase.PickingPassTiles && state.IsInGame(move.Player) && state[move.Player].StartingTownOwed;
        if (!startingTown && CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (move.Token < 0 || move.Token >= KeyTiles.Count)
            return MoveResult.Rejected(state, "There is no such key token.");
        var stack = state.KeyBank[move.Token];
        if (stack.Count == 0)
            return MoveResult.Rejected(state, "That key token has run out.");
        if (state.PlacedKeys.Any(placed => placed.Hex == move.Hex))
            return MoveResult.Rejected(state, "A key already lies on that hex.");
        // A key on an empty river hex: a Mermaids' river town. Never on another faction's landscape on a river.
        if (state.Layout.Contains(move.Hex) && state.Map[move.Hex] is Piece there && there.Owner != move.Player)
            return MoveResult.Rejected(state, "A key can't go on another faction's building or landscape.");
        // A key on a river hex: only the Mermaids' river town (an empty river hex, or their Coral Reef); no one else's,
        // not even on their own piece there (the Riverwalkers' landscape, a Selkies' dwelling).
        var onRiver = state.Layout.Contains(move.Hex) && state.HexTerrain[move.Hex] == Terrain.River;
        if (onRiver && !startingTown && move.Player != Faction.Mermaids)
            return MoveResult.Rejected(state, "A key can't go on a river hex.");
        var group = startingTown ? (StrongholdHex(state, move.Player) == move.Hex ? ValueList.Of(move.Hex) : (ValueList<int>?)null)
            : onRiver
            ? RiverTowns(state, move.Player).Where(river => river.RiverHex == move.Hex).Select(river => (ValueList<int>?)river.Hexes).FirstOrDefault()
            : FormableTowns(state, move.Player).Where(town => town.Contains(move.Hex)).Select(town => (ValueList<int>?)town).FirstOrDefault();
        if (group is not ValueList<int> town)
            return MoveResult.Rejected(state, startingTown ? $"The {Name(move.Player)}' starting key token goes on their stronghold."
                : onRiver
                ? "That river hex does not join buildings into a town that can be formed now."
                : "That hex is not part of a town that can be formed now.");
        // Kingdom of Ember: the key goes under one of the town's structures with the most power.
        if (move.Player == Faction.KingdomOfEmber && state.Map[move.Hex] is Piece under
            && town.Any(hex => Factions.BuildingPower(move.Player, state.Map[hex]!.Value.Kind) > Factions.BuildingPower(move.Player, under.Kind)))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} put the key under one of the town's structures with the most power.");

        var tile = stack[stack.Count - 1];
        var token = KeyTiles.All[move.Token];
        var player = state[move.Player];
        var events = new List<GameEvent> { new TownFormed(move.Player, tile, move.Hex, town) };
        var warnings = new List<Issue>();
        var next = state.WithPlayer(player with
        {
            TownHexes = new ValueList<int>(player.TownHexes.Concat(town).Distinct()),
            Keys = player.Keys + token.Keys,
            StartingTownOwed = false,
        }) with
        {
            KeyBank = state.KeyBank.SetItem(move.Token, stack.RemoveAt(stack.Count - 1)),
            PlacedKeys = state.PlacedKeys.Add(new PlacedKey(tile, move.Player, move.Hex)),
        };

        // Set off by the building or bridge that made the town formable this turn; the key itself if nothing did (a
        // favour lowering the power needed, a Mermaids' river town).
        var key = new PointsArea.KeyOn(move.Hex);
        var maker = onRiver ? null : next.Makers.FirstOrDefault(m => m.Faction == move.Player && m.Hexes.Any(town.Contains));
        var trigger = maker?.Trigger ?? key;
        var makersLeft = next.Makers.Where(m => !(m.Faction == move.Player && m.Hexes.Any(town.Contains))).ToList();
        next = next with { TownMakers = makersLeft.Count == 0 ? null : new ValueList<TownMaker>(makersLeft) };
        next = GainVictoryPoints(next, move.Player, token.VictoryPoints, events, key, trigger);
        next = GainVictoryPoints(next, move.Player, RoundTilePoints(next, move.Player, tile => tile.VictoryPointsPerTown), events,
            new PointsArea.RoundTile(), trigger);
        if (move.Player == Faction.Witches && next.HasLandscape(move.Player)) // Raven's Blunder: 5 VP or 3 steps, chosen next
        {
            events.Add(new TownBonusOwed(move.Player, move.Hex));
            next = next.WithPlayer(next[move.Player] with { WitchesTownBonusHex = move.Hex });
        }
        else
            next = GainVictoryPoints(next, move.Player, Factions.TownVictoryPoints(move.Player), events, new PointsArea.Portrait(), key);
        next = Receive(next, move.Player, token.Income + Factions.TownIncome(move.Player), PowerSource.Town, events, warnings);
        if (token.StepsOnEveryTrack > 0)
            foreach (var track in Enum.GetValues<Track>())
            {
                var before = next[move.Player].TrackSteps[(int)track];
                next = ClimbTrack(next, move.Player, track, token.StepsOnEveryTrack, events, out var after);
                events.Add(new TrackAdvanced(move.Player, track, before, after));
                next = TrackRewards(next, move.Player, before, after, events);
            }
        next = ReleaseHeldTopSteps(next, move.Player, events); // a favour's step onto 10 held earlier in this action
        if (token.ShippingUpgrade)
            next = FreeShippingUpgrade(next, move.Player, key, events, warnings);
        if (move.Player == Faction.Goblins && next[move.Player].StrongholdBuilt) // a treasure with every town
        {
            next = next.WithPlayer(next[move.Player] with { Treasures = next[move.Player].Treasures + 1 });
            events.Add(new TreasureGained(move.Player, next[move.Player].Treasures));
        }
        return MoveResult.Ok(next, events, warnings);
    }

    // Fakirs: instead their carpet range grows (CarpetRange counts the key token, already placed). Dwarves: nothing.
    // source: what gave the upgrade (the Mermaids' stronghold, the key token), for its VP.
    private static GameState FreeShippingUpgrade(GameState state, Faction faction, PointsArea source, List<GameEvent> events,
        List<Issue> warnings)
    {
        if (faction == Faction.Fakirs)
        {
            events.Add(new CarpetRangeIncreased(faction, state.CarpetRange(faction)));
            return state;
        }
        if (!Factions.HasShipping(faction))
        {
            warnings.Add(new Issue(Severity.Note, $"The {Name(faction)} have no shipping, so the free shipping upgrade is lost."));
            return state;
        }
        var player = state[faction];
        if (player.Shipping >= Factions.MaxShipping(faction))
        {
            warnings.Add(new Issue(Severity.Note, $"The {Name(faction)} are already at their highest shipping, so the free upgrade is lost."));
            return state;
        }
        var upgradesBefore = player.Shipping - Factions.StartingShipping(faction);
        var next = state.WithPlayer(player with { Shipping = player.Shipping + 1 });
        events.Add(new ShippingUpgraded(faction, player.Shipping + 1));
        return GainVictoryPoints(next, faction, Config.ShippingUpgradeVictoryPoints(upgradesBefore), events, source,
            new PointsArea.Shipping());
    }
}
