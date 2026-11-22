namespace Core;

// The fan factions (GameOptions.FanFactions; their boards in Factions), one
// section each, in the order of the Faction enum: their own moves and the helpers the rest of the rules call.
// Abilities woven into the usual rules are there, each marked with the faction's name: spades (PaySpades, GainSpades,
// Transform), power offers (AcceptFreeOffers), towns (IsTown, FormTown), passing (ApplyPass, TakePassTile),
// conversions, favours, upgrades. Setting their extras up directly (EditLava, EditWyrmToken, EditRock) is in
// Rules.EditPosition.cs.
public static partial class Rules
{
    // ---------- Strongholds, as they are built ----------

    // Fan factions' strongholds, as they are built (with OnStrongholdBuilt).
    private static GameState OnFanStrongholdBuilt(GameState state, Faction faction, int hex, List<GameEvent> events, List<Issue> warnings)
    {
        switch (faction)
        {
            case Faction.Wisps:
                state = GainVictoryPoints(state, faction, Config.WispsStrongholdVictoryPoints, events, new PointsArea.OnHex(hex), new PointsArea.OnHex(hex));
                if (state[faction].NextBuilding(PieceKind.Dwelling) is not null
                    && Enumerable.Range(0, state.Layout.HexCount).Any(h => state.HexTerrain[h] == Terrain.Lake && !state.IsOccupied(h)))
                {
                    events.Add(new FreeDwellingOffered(faction));
                    state = state.WithPlayer(state[faction] with { LakeDwellingOwed = true });
                }
                return state;
            case Faction.Conspirators:
                return EarnFavours(state, faction, Config.ConspiratorsStrongholdFavours, events, warnings);
            case Faction.DynionGeifr:
                for (var i = 0; i < Config.DynionGeifrStrongholdPriests; i++)
                    state = GainPriest(state, faction, events, warnings);
                return state;
            case Faction.ChildrenOfTheWyrm: // the tokens burned so far come back, into bowl 1: a reward after the action,
            {                               // held until they take it (TakeHeldPower), at the latest at End turn
                var back = Math.Max(0, Config.PowerTokens - state[faction].Power.Total);
                return back == 0 ? state : state.WithPlayer(state[faction] with { TokensHeld = state[faction].TokensHeld + back });
            }
            case Faction.KingdomOfEmber:
                return OweLava(state, faction, Config.EmberStrongholdLava, anyStructure: true, events, warnings);
            case Faction.SnowShamans:
            {
                var masses = IceMasses(state).Where(mass => mass.Any(h => !state.IsOccupied(h))).ToList();
                if (masses.Count == 0 || state[faction].NextBuilding(PieceKind.Dwelling) is null)
                    return state;
                events.Add(new IceDwellingsOffered(faction, masses.Count));
                return state.WithPlayer(state[faction] with
                {
                    IceMassesOwed = new ValueList<ValueList<int>>(masses.Select(mass => new ValueList<int>(mass))),
                });
            }
            default:
                return state;
        }
    }

    // ---------- Architects ----------

    // Architects: a bridge of theirs moved to another spot a new bridge could go; +3 VP. Refused if a town would then
    // fall short (its group no longer fits its keys).
    private static MoveResult ApplyBonusMoveBridge(GameState state, BonusMoveBridge move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.MoveBridge);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var from = new Bridge(move.Player, Math.Min(move.FromA, move.FromB), Math.Max(move.FromA, move.FromB));
        var (toA, toB) = (Math.Min(move.ToA, move.ToB), Math.Max(move.ToA, move.ToB));
        if (!state.Bridges.Contains(from))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no bridge there.");
        if ((toA, toB) == (from.HexA, from.HexB))
            return MoveResult.Rejected(state, "Move the bridge somewhere else.");
        var lifted = state with { Bridges = state.Bridges.RemoveAt(state.Bridges.IndexOf(from)) };
        if (!BridgeSpots(lifted, move.Player).Contains((toA, toB)))
            return MoveResult.Rejected(state, "A new bridge couldn't go there.");
        var to = new Bridge(move.Player, toA, toB);
        var next = lifted with { Bridges = lifted.Bridges.Add(to), ActionTaken = true };
        // A town the move splits: the part holding its key stays a town, whatever its power; every other part is in no
        // town any more and may form another (as on BGA: 661421495 #394 → #475, 738842993 #370 → #371;
        // as the Kingdom of Ember's lava). (Before: a move leaving a town short was refused.)
        var warnings = new List<Issue>();
        next = LeaveKeylessTownParts(next, move.Player, warnings);

        var events = new List<GameEvent> { new BridgeMoved(from, to) };
        next = GainVictoryPoints(next, move.Player, Config.ArchitectsMoveBridgeVictoryPoints, events,
            new PointsArea.OnBridge(to), new PointsArea.OnBridge(to));
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, warnings);
    }

    // The faction's town hexes in parts still joined together (touching or bridged): a part holding none of their keys
    // leaves its town (TownHexes), with a Note.
    private static GameState LeaveKeylessTownParts(GameState state, Faction faction, List<Issue> warnings)
    {
        var player = state[faction];
        var leaving = JoinedParts(state, player.TownHexes.ToList())
            .Where(part => !state.PlacedKeys.Any(key => key.Owner == faction && part.Contains(key.Hex)))
            .SelectMany(part => part).ToList();
        if (leaving.Count == 0)
            return state;
        warnings.Add(new Issue(Severity.Note, $"Part of a town of the {Name(faction)} is cut off from its key: its structures may form another."));
        return state.WithPlayer(player with { TownHexes = new ValueList<int>(player.TownHexes.Where(hex => !leaving.Contains(hex))) });
    }

    // Architects: spades saved transforming this hex to build on it: one per bridge of theirs from it to one of their
    // structures.
    private static int ArchitectsDiscount(GameState state, Faction faction, int hex) =>
        faction != Faction.Architects ? 0
            : state.Bridges.Count(bridge => bridge.Owner == faction
                && ((bridge.HexA == hex && OwnsBuildingAt(state, faction, bridge.HexB)) || (bridge.HexB == hex && OwnsBuildingAt(state, faction, bridge.HexA))));

    // Architects: the power their bridges add to a town (1 each, at either end of it).
    private static int BridgePower(GameState state, Faction faction, IReadOnlyCollection<int> hexes) =>
        faction != Faction.Architects ? 0
            : state.Bridges.Count(bridge => bridge.Owner == faction && (hexes.Contains(bridge.HexA) || hexes.Contains(bridge.HexB)));

    // ---------- Treasurers ----------

    // Treasurers: some of what they just received into the Safe.
    private static MoveResult ApplyStoreInTreasury(GameState state, StoreInTreasury move)
    {
        if (SafeRefusal(state, move.Player, move.Coins, move.Workers, move.Priests) is string refusal)
            return MoveResult.Rejected(state, refusal);
        var player = state[move.Player];
        var priests = player.Priests.Skip(player.Priests.Count - move.Priests).ToList(); // the top ones
        var stored = player with
        {
            Coins = player.Coins - move.Coins,
            Workers = player.Workers - move.Workers,
            Priests = new ValueList<Piece>(player.Priests.Take(player.Priests.Count - move.Priests)),
            SafeCoins = player.SafeCoins + move.Coins,
            SafeWorkers = player.SafeWorkers + move.Workers,
            SafePriests = new ValueList<Piece>(player.SafePriestList.Concat(priests)),
        };
        // The rest stays out: the offer is settled (nothing at all stored: declined).
        var next = state.WithPlayer(stored) with { TreasuryOffer = null, TreasuryChosen = null };
        if (!state.SafeOpeningHeld)
            return MoveResult.Ok(next);
        // A round-end cult reward settled: the round's end goes on (round-end spades, then the next round: the Safe opens).
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        next = ContinueAfterRoundEnd(next with { SafeOpeningHeld = false }, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // Treasurers: what is going into the Safe so far (settled later by StoreInTreasury).
    private static MoveResult ApplyChooseForSafe(GameState state, ChooseForSafe move)
    {
        if (SafeRefusal(state, move.Player, move.Coins, move.Workers, move.Priests) is string refusal)
            return MoveResult.Rejected(state, refusal);
        var chosen = new Income(move.Coins, move.Workers, move.Priests, 0);
        Income? now = chosen == default ? null : chosen;
        if (now == state.TreasuryChosen)
            return MoveResult.Rejected(state, "That is already what is going into the Safe.");
        return MoveResult.Ok(state with { TreasuryChosen = now });
    }

    // Why this can't go into the Treasurers' Safe now (null: it can).
    private static string? SafeRefusal(GameState state, Faction faction, int coins, int workers, int priests)
    {
        if (faction != Faction.Treasurers || !state.IsInGame(faction))
            return "Only the Treasurers have a Safe.";
        if (state.TreasuryOffer is not Income offer)
            return $"The {Name(faction)} have received nothing they could put in their Safe just now.";
        if (coins < 0 || workers < 0 || priests < 0)
            return "Nothing can be taken out of the Safe.";
        if (coins > offer.Coins || workers > offer.Workers || priests > offer.Priests)
            return $"Only what was just received can go in: {offer}.";
        var player = state[faction];
        if (coins > player.Coins || workers > player.Workers || priests > player.Priests.Count)
            return $"The {Name(faction)} don't have that much.";
        return null;
    }

    // As a round begins, before income: the Treasurers' Safe emptied, everything in it doubled (this isn't receiving).
    private static GameState OpenSafe(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        if (!state.IsInGame(Faction.Treasurers))
            return state;
        var player = state[Faction.Treasurers];
        var priests = player.SafePriestList;
        if (player.SafeCoins == 0 && player.SafeWorkers == 0 && priests.Count == 0)
            return state;
        var next = state.WithPlayer(player with
        {
            Coins = player.Coins + 2 * player.SafeCoins,
            Workers = player.Workers + 2 * player.SafeWorkers,
            Priests = new ValueList<Piece>(player.Priests.Concat(priests)),
            SafeCoins = 0,
            SafeWorkers = 0,
            SafePriests = null,
        });
        for (var i = 0; i < priests.Count; i++) // the doubling: as many again from the supply (as far as it goes)
            next = TakePriestFromSupply(next, Faction.Treasurers, events, warnings);
        events.Add(new SafeOpened(Faction.Treasurers, 2 * player.SafeCoins, 2 * player.SafeWorkers, 2 * priests.Count));
        return next;
    }

    // With their stronghold, the Treasurers' conversions while an offer waits: allowed, and what they give joins it.
    private static bool TreasurersConverting(GameState state, Move move) =>
        state.TreasuryOffer is not null && state.IsInGame(Faction.Treasurers) && state[Faction.Treasurers].StrongholdBuilt
        && move is ConvertPowerToCoin { Player: Faction.Treasurers } or ConvertPowerToWorker { Player: Faction.Treasurers }
            or ConvertPowerToPriest { Player: Faction.Treasurers } or ConvertPriestToWorker { Player: Faction.Treasurers }
            or ConvertWorkerToCoin { Player: Faction.Treasurers } or BurnPower { Player: Faction.Treasurers };

    // After every move: what the Treasurers may put in their Safe now (Apply then holds everything else until they
    // settle it: StoreInTreasury). Their income (set by BeginRound) right after the round begins; with their
    // stronghold, every coin, worker and priest the move gave them (round-end cult rewards too; not what their Safe
    // gives back), and their conversions may go on before it is settled, each one's gain added to the offer
    // Never in the last round (the Safe would never open). Setup mode's edits offer nothing.
    private static GameState TreasuryOfferAfter(GameState before, Move move, GameState after, IReadOnlyList<GameEvent> events)
    {
        if (move is StoreInTreasury or ChooseForSafe)
            return after;
        if (IsEdit(move))
            return after with { TreasuryOffer = before.TreasuryOffer, TreasuryChosen = before.TreasuryChosen };
        Income? offer = null;
        if (after.Phase == Phase.Playing && after.Round < Config.Rounds && after.IsInGame(Faction.Treasurers))
        {
            if (after[Faction.Treasurers].StrongholdBuilt && before.IsInGame(Faction.Treasurers))
            {
                PlayerState was = before[Faction.Treasurers], now = after[Faction.Treasurers];
                var opened = events.OfType<SafeOpened>().FirstOrDefault();
                var gained = new Income(Math.Max(0, now.Coins - was.Coins - (opened?.Coins ?? 0)),
                    Math.Max(0, now.Workers - was.Workers - (opened?.Workers ?? 0)),
                    Math.Max(0, now.Priests.Count - was.Priests.Count - (opened?.Priests ?? 0)), 0);
                if (TreasurersConverting(before, move) && before.TreasuryOffer is Income held)
                    // A conversion while an offer waits: its gain joins the offer, which never holds more than they have
                    // (a converted worker or priest leaves it).
                    gained = new Income(Math.Clamp(held.Coins + gained.Coins, 0, Math.Max(0, now.Coins)),
                        Math.Clamp(held.Workers + gained.Workers, 0, Math.Max(0, now.Workers)),
                        Math.Clamp(held.Priests + gained.Priests, 0, now.Priests.Count), 0);
                if (gained != default)
                    offer = gained;
            }
            else if (events.Any(e => e is IncomeReceived { Faction: Faction.Treasurers }))
                offer = after.TreasuryOffer; // set by BeginRound
        }
        // What was chosen for the Safe so far, as far as the offer now goes (nothing without one).
        Income? chosen = offer is Income o && after.TreasuryChosen is Income c
            ? new Income(Math.Min(c.Coins, o.Coins), Math.Min(c.Workers, o.Workers), Math.Min(c.Priests, o.Priests), 0)
            : null;
        if (chosen == default(Income))
            chosen = null;
        return after.TreasuryOffer == offer && after.TreasuryChosen == chosen ? after
            : after with { TreasuryOffer = offer, TreasuryChosen = chosen };
    }

    // ---------- Djinni ----------

    // Djinni: a lamp for two trackers trading tracks, each keeping its step (no power for the one going up).
    private static MoveResult ApplySwapTracks(GameState state, SwapTracks move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Djinni)
            return MoveResult.Rejected(state, "Only the Djinni swap their trackers.");
        var player = state[move.Player];
        if (player.Lamps == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no magic lamp left.");
        if (move.A == move.B)
            return MoveResult.Rejected(state, "Choose two different tracks.");
        int a = player.TrackSteps[(int)move.A], b = player.TrackSteps[(int)move.B];
        if (a == b)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are on the same step of both tracks: nothing would change.");
        bool TopTaken(Track track) =>
            state.Players.Any(other => other.Faction != move.Player && other.TrackSteps[(int)track] == Config.TrackMax);
        if ((a == Config.TrackMax && TopTaken(move.B)) || (b == Config.TrackMax && TopTaken(move.A)))
            return MoveResult.Rejected(state, "Someone else already holds the top of that track.");

        var steps = player.TrackSteps.SetItem((int)move.A, b).SetItem((int)move.B, a);
        var next = state.WithPlayer(player with { TrackSteps = steps, Lamps = player.Lamps - 1 }) with { ActionTaken = true };
        foreach (var track in new[] { move.A, move.B }) // each arrives last on its new step
        {
            var order = next.TrackArrivals[(int)track];
            order = order.RemoveAt(order.IndexOf(move.Player)).Add(move.Player);
            next = next with { TrackArrivals = next.TrackArrivals.SetItem((int)track, order) };
        }
        return MoveResult.Ok(next);
    }

    // Djinni: their 2 starting steps on one track, at their turn among the last starting placements (IsStartingStepsTurn).
    private static MoveResult ApplyTakeStartingSteps(GameState state, TakeStartingSteps move)
    {
        if (!state.IsInGame(move.Player) || state[move.Player].StartingStepsOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no starting steps to take.");
        if (state.ActivePlayer != move.Player || !IsStartingStepsTurn(state))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} take their starting steps in turn order with the last starting buildings.");
        var player = state[move.Player];
        var events = new List<GameEvent>();
        var before = player.TrackSteps[(int)move.Track];
        var next = state.WithPlayer(player with { StartingStepsOwed = 0 });
        next = ClimbTrack(next, move.Player, move.Track, player.StartingStepsOwed, events, out var after);
        events.Insert(0, new TrackAdvanced(move.Player, move.Track, before, after));
        return MoveResult.Ok(AfterStartingSteps(next), events, MoveResult.NoIssues);
    }

    // ---------- Archivists ----------

    // Archivists: taking a pass tile after passing, 2 power per coin on it.
    private static GameState ArchivistsPower(GameState state, Faction faction, int coins, List<GameEvent> events) =>
        faction == Faction.Archivists && state.Phase == Phase.Playing && coins > 0
            ? GainPower(state, faction, coins * Config.ArchivistsPowerPerPassTileCoin, PowerSource.Income, events)
            : state;

    // Archivists with their stronghold, after passing: one of their two new tiles, swapped for the one in that place (the
    // first, then the second; the first time, the second place is empty). Done once both are taken, or when no tile is
    // left that they may take (not one they held when passing: HeldPassTiles).
    private static GameState TakeArchivistsTile(GameState state, Faction faction, int tile, List<GameEvent> events)
    {
        var slot = state.PassTileBank.IndexOf(tile);
        var coins = state.PassTileCoins[tile];
        var player = state[faction];
        var firstPlace = player.PassTileOwed;
        events.Add(new PassTileTaken(faction, tile, slot, coins));
        var left = state.PassTileBank.Where(t => t is int other && other != tile).ToList();
        if ((firstPlace ? player.PassTile : player.ExtraPassTile) is int back)
            left.Add(back);
        var taking = firstPlace
            ? player with { PassTile = tile, PassTileOwed = false, Coins = player.Coins + coins }
            : player with { ExtraPassTile = tile, ExtraPassTileOwed = false, Coins = player.Coins + coins };
        var given = player.HeldPassTiles ?? ValueList<int>.Empty;
        if (!taking.PassTileOwed && (!taking.ExtraPassTileOwed || !left.Any(t => t is int other && !given.Contains(other))))
            taking = taking with { HeldPassTiles = null, ExtraPassTileOwed = false };
        var next = state.WithPlayer(taking) with
        {
            PassTileBank = new ValueList<int?>(left.Concat(Enumerable.Repeat((int?)null, state.PassTileBank.Count - left.Count))),
            PassTileCoins = state.PassTileCoins.SetItem(tile, 0),
        };
        return ArchivistsPower(next, faction, coins, events);
    }

    // ---------- Time Travellers ----------

    // The round tiles whose "during the round" scoring applies to this faction now: this round's (CurrentRoundTile);
    // for the Time Travellers instead the previous and the next round's (round 1's previous is round 6's, round 6's
    // next is round 1's). Missing tiles (Calculator's blank rounds) score nothing.
    public static IEnumerable<RoundTileDefinition> ScoringTiles(GameState state, Faction faction)
    {
        if (faction != Faction.TimeTravellers)
        {
            if (state.CurrentRoundTile is RoundTileDefinition current)
                yield return current;
            yield break;
        }
        if (state.Phase != Phase.Playing || state.Round < 1 || state.Round > Config.Rounds)
            yield break;
        foreach (var round in new[] { state.Round == 1 ? Config.Rounds : state.Round - 1, state.Round == Config.Rounds ? 1 : state.Round + 1 })
            if (round <= state.ChosenRoundTiles.Count)
                yield return RoundTiles.All[state.ChosenRoundTiles[round - 1]];
    }

    // The VP this faction's round tiles give for something (see ScoringTiles).
    private static int RoundTilePoints(GameState state, Faction faction, Func<RoundTileDefinition, int> points) =>
        ScoringTiles(state, faction).Sum(points);

    // Time Travellers: up to 4 tokens straight from bowl 1 to bowl 3.
    private static MoveResult ApplyBonusTokensToBowlThree(GameState state, BonusTokensToBowlThree move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.TokensToBowlThree);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var power = state[move.Player].Power;
        if (move.Count < 1 || move.Count > Config.TimeTravellersTokensToBowlThree)
            return MoveResult.Rejected(state, $"Move 1 to {Config.TimeTravellersTokensToBowlThree} tokens.");
        if (move.Count > power.Bowl1)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have only {power.Bowl1} tokens in bowl 1.");
        var after = new Power(power.Bowl1 - move.Count, power.Bowl2, power.Bowl3 + move.Count);
        var events = new List<GameEvent> { new PowerGained(move.Player, PowerSource.Stronghold, 2 * move.Count, power, after) };
        var next = state.WithPlayer(state[move.Player] with { Power = after }) with { ActionTaken = true };
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Prospectors ----------

    // Prospectors: a coin for each opponent's trading post on the map.
    private static MoveResult ApplyBonusCoinsForTradingPosts(GameState state, BonusCoinsForTradingPosts move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.CoinsForTradingPosts);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var posts = state.Map.Count(piece => piece is { Kind: PieceKind.TradingPost } post && post.Owner != move.Player);
        var player = state[move.Player];
        var next = state.WithPlayer(player with { Coins = player.Coins + posts }) with { ActionTaken = true };
        var events = new List<GameEvent> { new CoinGained(move.Player, player.Coins + posts) };
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Goblins ----------

    // Goblins: a treasure for a reward by the number of their buildings of the chosen kind on the map.
    private static MoveResult ApplySpendTreasure(GameState state, SpendTreasure move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Goblins)
            return MoveResult.Rejected(state, "Only the Goblins have treasures.");
        var player = state[move.Player];
        if (player.Treasures == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no treasure left.");
        int Count(PieceKind kind) => state.HexesBuiltOnBy(move.Player).Count(hex => state.Map[hex]!.Value.Kind == kind);

        var events = new List<GameEvent>();
        var spent = player with { Treasures = player.Treasures - 1 };
        GameState next;
        switch (move.Kind)
        {
            case PieceKind.Dwelling when Count(PieceKind.Dwelling) > 0:
                next = GainPower(state.WithPlayer(spent), move.Player, Count(PieceKind.Dwelling) * Config.GoblinsPowerPerDwelling,
                    PowerSource.Income, events);
                break;
            case PieceKind.TradingPost when Count(PieceKind.TradingPost) > 0:
                next = state.WithPlayer(spent with { Coins = spent.Coins + Count(PieceKind.TradingPost) * Config.GoblinsCoinsPerTradingPost });
                events.Add(new CoinGained(move.Player, next[move.Player].Coins));
                break;
            case PieceKind.Temple when Count(PieceKind.Temple) > 0:
                next = state.WithPlayer(spent with { Workers = spent.Workers + Count(PieceKind.Temple) * Config.GoblinsWorkersPerTemple });
                break;
            case PieceKind.Stronghold or PieceKind.Sanctuary when Count(PieceKind.Stronghold) + Count(PieceKind.Sanctuary) > 0:
                var owed = Count(PieceKind.Stronghold) * Config.GoblinsStepsForStronghold + Count(PieceKind.Sanctuary) * Config.GoblinsStepsForSanctuary;
                for (var i = 0; i < owed; i++)
                    events.Add(new CultStepOwed(move.Player));
                next = state.WithPlayer(spent with { CultStepsOwed = spent.CultStepsOwed + owed });
                break;
            case PieceKind.Dwelling or PieceKind.TradingPost or PieceKind.Temple or PieceKind.Stronghold or PieceKind.Sanctuary:
                return MoveResult.Rejected(state, $"The {Name(move.Player)} have no {Buildings.Noun(move.Kind)} on the map.");
            default:
                return MoveResult.Rejected(state, "Choose dwellings, trading posts, temples, or the stronghold and sanctuary.");
        }
        return MoveResult.Ok(next with { ActionTaken = true }, events, MoveResult.NoIssues);
    }

    // ---------- Children of the Wyrm ----------

    // Children of the Wyrm: the land hexes joined to this land hex by a chain of their power tokens on rivers (a token
    // touching it, then tokens touching each other): adjacent to it for them only.
    private static IEnumerable<int> WyrmChainHexes(GameState state, int hex)
    {
        if (state.HexTerrain[hex] == Terrain.River)
            return Enumerable.Empty<int>();
        var tokens = state.WyrmTokenHexes.ToHashSet();
        var chain = state.Layout.Neighbours(hex).Where(tokens.Contains).ToList();
        var seen = chain.ToHashSet();
        for (var i = 0; i < chain.Count; i++)
            foreach (var n in state.Layout.Neighbours(chain[i]))
                if (tokens.Contains(n) && seen.Add(n))
                    chain.Add(n);
        return chain.SelectMany(state.Layout.Neighbours)
            .Where(n => n != hex && state.HexTerrain[n] != Terrain.River)
            .Distinct();
    }

    // An opponent's structure (or a neutral dwelling) next to this hex, for this faction: touching it, or across one of
    // the faction's own bridges (as on BGA: 608085870 #326); for the Children of the Wyrm also through
    // their token chains. Without one, trading posts (and all the Wyrm's structures) cost double coins.
    private static bool OpponentNext(GameState state, Faction faction, int hex) =>
        AdjacentHexes(state, faction, hex)
            .Any(n => (state.Map[n] is { } piece && piece.Owner != faction) || state.Neutrals.Hexes.Contains(n));

    // What a dwelling on this hex costs this faction: its board's cost, the coins doubled for the Children of the Wyrm
    // with no opponent next to it.
    public static Cost DwellingCost(GameState state, Faction faction, int hex)
    {
        var cost = Factions.BuildingCost(faction, PieceKind.Dwelling);
        return faction == Faction.ChildrenOfTheWyrm && !OpponentNext(state, faction, hex) ? cost + new Cost(cost.Coins, 0) : cost;
    }

    // Children of the Wyrm's stronghold action: one or two power tokens (bowl 1 first, then 2, then 3) onto river hexes,
    // each next to one of their structures or tokens (the first counts for the second).
    private static MoveResult ApplyBonusRiverTokens(GameState state, BonusRiverTokens move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.RiverTokens);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var hexes = new[] { move.HexA, move.HexB }.OfType<int>().ToArray(); // none: the action used for nothing
        var player = state[move.Player];
        if (player.Power.Total < hexes.Length)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have only {Plural(player.Power.Total, "power token")}.");
        var tokens = state.WyrmTokenHexes.ToList();
        foreach (var hex in hexes)
        {
            if (!state.Layout.Contains(hex) || state.HexTerrain[hex] != Terrain.River)
                return MoveResult.Rejected(state, "Power tokens go on river hexes.");
            if (tokens.Contains(hex))
                return MoveResult.Rejected(state, $"One of the {Name(move.Player)}' power tokens already lies there.");
            if (!state.Layout.Neighbours(hex).Any(n => OwnsBuildingAt(state, move.Player, n) || tokens.Contains(n)))
                return MoveResult.Rejected(state, $"A power token goes next to one of the {Name(move.Player)}' structures or power tokens.");
            tokens.Add(hex);
        }
        var after = player.Power.RemoveTokens(hexes.Length);
        var events = new List<GameEvent> { new RiverTokensPlaced(move.Player, new ValueList<int>(hexes), after) };
        var next = state.WithPlayer(player with { Power = after }) with { WyrmTokens = new ValueList<int>(tokens), ActionTaken = true };
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Wisps ----------

    // Wisps: their stronghold's free Lake dwelling given up (the tick). Snow Shamans: the dwellings left on their masses
    // of Ice.
    private static MoveResult ApplySkipFreeDwelling(GameState state, SkipFreeDwelling move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (!player.LakeDwellingOwed && player.IceMassesOwed is not { Count: > 0 })
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no free dwelling to give up.");
        return MoveResult.Ok(state.WithPlayer(player with { LakeDwellingOwed = false, IceMassesOwed = null }));
    }

    // Wisps' stronghold: the leftmost dwelling, free, on any empty Lake hex. Snow Shamans' stronghold: one on each mass
    // of Ice offered, on any empty Ice hex of it (once their dwellings run out, the rest are lost).
    private static MoveResult ApplyPlaceFreeDwelling(GameState state, PlaceFreeDwelling move)
    {
        var owner = move.Dwelling.Owner;
        if (CheckTurn(state, owner) is { } refusal)
            return refusal;
        var player = state[owner];
        var lake = player.LakeDwellingOwed;
        if (!lake && player.IceMassesOwed is not { Count: > 0 })
            return MoveResult.Rejected(state, $"The {Name(owner)} have no free dwelling to place.");
        if (move.Dwelling.Kind != PieceKind.Dwelling)
            return MoveResult.Rejected(state, "That piece is not a dwelling.");
        if (CheckLeftmostInBank(state, player.DwellingBank, move.Dwelling, "dwelling") is { } bankRefusal)
            return bankRefusal;
        var terrain = lake ? Terrain.Lake : Terrain.Ice;
        if (!state.Layout.Contains(move.Hex) || state.HexTerrain[move.Hex] != terrain)
            return MoveResult.Rejected(state, $"The free dwelling goes on {(lake ? "a Lake" : "an Ice")} hex.");
        if (state.IsOccupied(move.Hex) || state.PlacedKeys.Any(key => key.Hex == move.Hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        var masses = player.IceMassesOwed ?? ValueList<ValueList<int>>.Empty;
        var mass = lake ? -1 : Enumerable.Range(0, masses.Count).FirstOrDefault(i => masses[i].Contains(move.Hex), -1);
        if (!lake && mass < 0)
            return MoveResult.Rejected(state, "Each mass of Ice takes one free dwelling: that one already has it.");
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        ValueList<ValueList<int>>? left = lake ? null : masses.RemoveAt(mass);
        var next = state.WithPlayer(player with { LakeDwellingOwed = false, IceMassesOwed = left is { Count: > 0 } ? left : null });
        next = PlaceDwelling(next, move.Dwelling, move.Hex, default, events);
        if (next[owner].IceMassesOwed is not null && next[owner].NextBuilding(PieceKind.Dwelling) is null)
        {
            warnings.Add(new Issue(Severity.Note, $"The {Name(owner)} have no dwellings left, so the other masses of Ice get none."));
            next = next.WithPlayer(next[owner] with { IceMassesOwed = null });
        }
        return MoveResult.Ok(next, events, warnings);
    }

    // Wisps: a trading post just built brings 1 free spade, for a hex next to it (across their bridges too), exactly 1 spade,
    // nothing built there after (GameState.SpadeAnchorHex). Wasted if no hex next to it could take it.
    private static GameState WispsSpade(GameState state, Faction faction, int hex, List<GameEvent> events, List<Issue> warnings)
    {
        bool usable = AdjacentHexes(state, faction, hex).Any(n => Terrains.IsBasic(state.HexTerrain[n]) && !state.IsOccupied(n));
        if (!usable)
        {
            warnings.Add(new Issue(Severity.Note, $"No hex next to the {Name(faction)}' trading post can be transformed, so their free spade is lost."));
            return state;
        }
        return GainSpades(state with { SpadedHexes = ValueList<int>.Empty, SpadeAnchorHex = hex }, faction, 1, events, warnings);
    }

    // ---------- Atlanteans ----------

    // Atlanteans: the power of the town holding their stronghold reaching 7, 10 and 16 pays out once each (a shipping
    // step, 2 steps on every track, 20 VP). Checked after every move in the main phase and between rounds.
    private static GameState AtlanteansTownRewards(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        const Faction faction = Faction.Atlanteans;
        if (!state.IsInGame(faction) || state.Phase is not (Phase.Playing or Phase.RoundEndSpades or Phase.SettingUp))
            return state; // (setting up a game in progress: they apply as the map is edited)
        var thresholds = Config.AtlanteansTownThresholds;
        var claimed = state[faction].AtlanteansRewards;
        if (claimed >= thresholds.Length || StrongholdHex(state, faction) is not int hex)
            return state;
        var group = BuildingGroups(state, faction).First(g => g.Contains(hex));
        var power = group.Sum(h => Factions.BuildingPower(faction, state.Map[h]!.Value.Kind)) + BridgePower(state, faction, group);
        var source = new PointsArea.OnHex(hex);
        while (claimed < thresholds.Length && power >= thresholds[claimed])
        {
            events.Add(new AtlanteansTownReward(faction, thresholds[claimed]));
            claimed++;
            state = state.WithPlayer(state[faction] with { AtlanteansRewards = claimed });
            switch (claimed)
            {
                case 1:
                    state = FreeShippingUpgrade(state, faction, source, events, warnings);
                    break;
                case 2:
                    foreach (var track in Enum.GetValues<Track>())
                    {
                        var before = state[faction].TrackSteps[(int)track];
                        state = ClimbTrack(state, faction, track, Config.AtlanteansStepsOnEveryTrack, events, out var after);
                        events.Add(new TrackAdvanced(faction, track, before, after));
                        state = TrackRewards(state, faction, before, after, events);
                    }
                    break;
                default:
                    state = GainVictoryPoints(state, faction, Config.AtlanteansTownVictoryPoints, events, source, source);
                    break;
            }
        }
        return state;
    }

    // ---------- Chash Dallah ----------

    // Chash Dallah: their income track a step up (2 workers, 2 coins), for that step's VP.
    private static MoveResult ApplyAdvanceIncomeTrack(GameState state, AdvanceIncomeTrack move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.ChashDallah)
            return MoveResult.Rejected(state, "Only Chash Dallah have an income track.");
        var player = state[move.Player];
        if (player.IncomeTrack >= Config.ChashDallahIncomeTrackTop)
            return MoveResult.Rejected(state, $"{Name(move.Player)} are already at the top of their income track.");
        var paid = player.Pay(Config.ChashDallahIncomeStepCost) with { IncomeTrack = player.IncomeTrack + 1 };
        var events = new List<GameEvent> { new IncomeTrackAdvanced(move.Player, paid.IncomeTrack) };
        var next = GainVictoryPoints(state.WithPlayer(paid) with { ActionTaken = true }, move.Player,
            Factions.IncomeTrackVictoryPoints(paid.IncomeTrack), events, new PointsArea.Portrait(), new PointsArea.Portrait());
        return MoveResult.Ok(next, events, DebtWarning(paid));
    }

    // ---------- Enlightened ----------

    // Enlightened: 1 coin for a new token in bowl 1.
    private static MoveResult ApplyBuyPowerToken(GameState state, BuyPowerToken move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Enlightened)
            return MoveResult.Rejected(state, "Only the Enlightened buy power tokens.");
        var player = state[move.Player];
        var paid = player with { Coins = player.Coins - Config.EnlightenedTokenCoins, Power = player.Power.AddTokens(1, 1) };
        return MoveResult.Ok(state.WithPlayer(paid), Array.Empty<GameEvent>(), DebtWarning(paid));
    }

    // Enlightened: 4 power.
    private static MoveResult ApplyBonusFourPower(GameState state, BonusFourPower move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.FourPower);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var events = new List<GameEvent>();
        var next = GainPower(state with { ActionTaken = true }, move.Player, Config.EnlightenedStrongholdPower, PowerSource.Stronghold, events);
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Enlightened: refused if the power for these paid spades can't be found in bowl 3, burning included.
    private static MoveResult? CheckSpadePower(GameState state, Faction faction, int paidSpades)
    {
        var power = state[faction].SpadePower * paidSpades;
        if (power == 0)
            return null;
        var player = state[faction];
        return player.Power.CanBurn(Math.Max(0, power - player.Power.Bowl3))
            ? null
            : MoveResult.Rejected(state, $"The {Name(faction)} need {power} power in bowl 3 (burning included) for {Plural(paidSpades, "spade")}.");
    }

    // What one power conversion (to a coin, a worker or a priest) gives: 2 for the Enlightened with their stronghold.
    internal static int ConversionGain(GameState state, Faction faction) =>
        faction == Faction.Enlightened && state[faction].StrongholdBuilt ? 2 : 1;

    // ---------- Conspirators ----------

    // Conspirators: a favour back to the supply (its steps lost, no power gained or lost on the way, nothing it gave
    // taken back) for another they don't hold (its steps and power, and their 2 coins).
    private static MoveResult ApplyBonusSwapFavour(GameState state, BonusSwapFavour move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.SwapFavour);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (!player.HoldsFavour(move.Return))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} don't hold that favour.");
        if (move.Take == move.Return)
            return MoveResult.Rejected(state, "Take a different favour.");
        if (move.Take < 0 || move.Take >= Favours.Count)
            return MoveResult.Rejected(state, "There is no such favour.");
        if (player.HoldsFavour(move.Take))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} already have that favour.");
        if (state.FavourBank[move.Take].Count == 0)
            return MoveResult.Rejected(state, "That favour has run out.");

        var events = new List<GameEvent>();
        var returned = player.FavourTiles.First(tile => tile.Id == move.Return);
        var given = Favours.All[move.Return];
        var before = player.TrackSteps[(int)given.Track];
        var next = state.WithPlayer(player with { FavourTiles = player.FavourTiles.RemoveAt(player.FavourTiles.IndexOf(returned)) }) with
        {
            FavourBank = state.FavourBank.SetItem(move.Return, state.FavourBank[move.Return].Add(returned)),
            ActionTaken = true,
        };
        next = DescendTrack(next, move.Player, given.Track, given.Steps, events);
        events.Insert(0, new FavourReturned(move.Player, returned, before, next[move.Player].TrackSteps[(int)given.Track]));
        next = TakeFavourTile(next, move.Player, move.Take, events);
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Dynion Geifr ----------

    // Dynion Geifr: the Fire-2 favour (the one lowering a town's power) at the start, with its steps.
    private static GameState DynionStartingFavour(GameState state, Faction faction, List<GameEvent> events)
    {
        var id = Enumerable.Range(0, Favours.Count).First(i => Favours.All[i].Track == Track.Fire && Favours.All[i].TownPowerReduction > 0);
        if (state.FavourBank[id].Count == 0)
            return state;
        return TakeFavourTile(state, faction, id, events);
    }

    // ---------- Selkies ----------

    // Selkies: a dwelling on a river hex, as the turn's action: between two of their structures that don't touch each
    // other; 1 worker more, 2 VP. The river stays a river.
    // Selkies' stronghold action: a dwelling built (dragged) with their shipping 1 more and 1 spade free for the transform.
    private static MoveResult ApplyBonusSelkiesBuild(GameState state, BonusSelkiesBuild move)
    {
        var owner = move.Dwelling.Owner;
        var bonus = new BonusActionId(move.Source, BonusAction.SelkiesSpade);
        if (owner != Faction.Selkies)
            return MoveResult.Rejected(state, "Only the Selkies have that stronghold action.");
        if (CheckBonusAction(state, owner, bonus) is { } refusal)
            return refusal;
        var built = BuildDwellingWith(state with { ShippingBoost = true }, new BuildDwelling(move.Dwelling, move.Hex), Config.SelkiesFreeSpades);
        if (!built.Accepted)
            return MoveResult.Rejected(state, built.Issues[0].Message);
        var events = built.Events.ToList();
        var next = MarkBonusActionUsed(built.State with { ShippingBoost = false }, owner, bonus);
        return MoveResult.Ok(next, events, built.Issues);
    }

    private static MoveResult SelkiesRiverDwelling(GameState state, Piece dwelling, int hex)
    {
        var owner = dwelling.Owner;
        if (state.IsOccupied(hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        if (state.PlacedKeys.Any(key => key.Hex == hex))
            return MoveResult.Rejected(state, "A key lies on that river hex.");
        var touching = state.Layout.Neighbours(hex).Where(n => OwnsBuildingAt(state, owner, n)).ToList();
        if (!touching.Any(a => touching.Any(b => a < b && !AdjacentHexes(state, owner, a).Contains(b))))
            return MoveResult.Rejected(state, $"The {Name(owner)} build on a river hex only next to two of their structures that don't touch each other.");
        var cost = DwellingCost(state, owner, hex) + new Cost(Coins: 0, Workers: Config.SelkiesRiverDwellingWorkers);
        var events = new List<GameEvent>();
        var next = PlaceDwelling(state, dwelling, hex, cost, events);
        next = GainVictoryPoints(next, owner, Config.SelkiesRiverDwellingVictoryPoints, events, new PointsArea.Portrait(), new PointsArea.OnHex(hex));
        return MoveResult.Ok(next, events, DebtWarning(next[owner]));
    }

    // ---------- Snow Shamans ----------

    // Every mass of Ice on the map: Ice hexes joined by touching.
    private static List<List<int>> IceMasses(GameState state)
    {
        var masses = new List<List<int>>();
        var seen = new HashSet<int>();
        for (var start = 0; start < state.Layout.HexCount; start++)
        {
            if (state.HexTerrain[start] != Terrain.Ice || !seen.Add(start))
                continue;
            var mass = new List<int> { start };
            for (var i = 0; i < mass.Count; i++)
                foreach (var n in state.Layout.Neighbours(mass[i]))
                    if (state.HexTerrain[n] == Terrain.Ice && seen.Add(n))
                        mass.Add(n);
            masses.Add(mass);
        }
        return masses;
    }

    // Snow Shamans, after passing: a free step on their shipping or spade track (no VP of its own, not an action), then
    // the pass scores (PassScoringHeld). They never pay for either.
    private static MoveResult SnowShamansFreeUpgrade(GameState state, Faction faction, bool shipping)
    {
        if (CheckTurn(state, faction) is { } refusal)
            return refusal;
        var player = state[faction];
        if (!player.FreeUpgradeOwed)
            return MoveResult.Rejected(state, $"The {Name(faction)} don't pay to advance: they take one free step each time they pass.");
        if (shipping && player.Shipping >= Factions.MaxShipping(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} are already at their highest shipping.");
        if (!shipping && player.SpadeUpgrades >= Factions.MaxSpadeUpgrades(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} cannot upgrade their spades any further.");
        var next = state.WithPlayer(shipping
            ? player with { Shipping = player.Shipping + 1, FreeUpgradeOwed = false, PassScoringHeld = null }
            : player with { SpadeUpgrades = player.SpadeUpgrades + 1, FreeUpgradeOwed = false, PassScoringHeld = null });
        var events = new List<GameEvent>();
        if (shipping)
            events.Add(new ShippingUpgraded(faction, player.Shipping + 1));
        // The pass scores now, with the step taken (a shipping pass tile counts the new level).
        next = ScorePass(next, faction, player.PassScoringHeld ?? ValueList<int>.Empty, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Firewalkers ----------

    // Firewalkers, any time in their turn: the pyro token 1 forward (never past their VP) for 1 power.
    private static MoveResult ApplyAdvancePyro(GameState state, AdvancePyro move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Firewalkers)
            return MoveResult.Rejected(state, "Only the Firewalkers have a pyro token.");
        var player = state[move.Player];
        if (player.Pyro + 1 > player.VictoryPoints)
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' pyro token can't pass their VP ({player.VictoryPoints}).");
        var events = new List<GameEvent> { new PyroMoved(move.Player, player.Pyro, player.Pyro + 1) };
        var next = GainPower(state.WithPlayer(player with { Pyro = player.Pyro + 1 }), move.Player, 1, PowerSource.Pyro, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // The most power this faction may take from a neighbour's build: no more than its VP pay for (the first power is
    // free; the Children of the Wyrm pay 1 VP less), as the rules and BGA have it (BGA table 171020282 #186: 1 of 3 at
    // 0 VP). The Firewalkers' VP may only fall to their pyro.
    public static int AffordableGain(GameState state, Faction faction) => faction switch
    {
        Faction.Firewalkers => Math.Max(1, state[faction].VictoryPoints - state[faction].Pyro + 1),
        Faction.ChildrenOfTheWyrm => Math.Max(1, state[faction].VictoryPoints + 1 + Config.WyrmOfferVictoryPointDiscount),
        _ => Math.Max(1, state[faction].VictoryPoints + 1),
    };

    // Firewalkers: a move that leaves their VP behind their pyro token (lowering the VP or moving the pyro): the
    // message, or null. Refused in Strict and Lenient, a warning in Free (Apply).
    private static string? PyroOvertaken(GameState before, GameState after)
    {
        if (!before.IsInGame(Faction.Firewalkers) || !after.IsInGame(Faction.Firewalkers) || after.Phase == Phase.SettingUp)
            return null;
        PlayerState was = before[Faction.Firewalkers], now = after[Faction.Firewalkers];
        if (now.VictoryPoints >= now.Pyro || (now.VictoryPoints >= was.VictoryPoints && now.Pyro <= was.Pyro))
            return null;
        return $"The {Name(Faction.Firewalkers)}' VP ({now.VictoryPoints}) can't fall behind their pyro token ({now.Pyro}).";
    }

    // ---------- Kingdom of Ember ----------

    // Kingdom of Ember: lava owed, to place under a dwelling (or, from their stronghold, any structure). Lost if there is
    // no dwelling to put it under.
    private static GameState OweLava(GameState state, Faction faction, int count, bool anyStructure, List<GameEvent> events,
        List<Issue>? warnings)
    {
        if (count <= 0)
            return state;
        if (!anyStructure && !state.HexesBuiltOnBy(faction).Any(hex => state.Map[hex]!.Value.Kind == PieceKind.Dwelling))
        {
            warnings?.Add(new Issue(Severity.Note, $"The {Name(faction)} have no dwelling on the map to put lava under, so it is lost."));
            return state;
        }
        events.Add(new LavaOwed(faction, count, anyStructure));
        var player = state[faction];
        return state.WithPlayer(anyStructure
            ? player with { LavaOwedAny = player.LavaOwedAny + count }
            : player with { LavaOwed = player.LavaOwed + count });
    }

    // Something still to settle between rounds (the player whose turn it is then keeps it until nothing is left).
    private static bool OwesBetweenRounds(PlayerState player) =>
        player.SpadesOwed > 0 || player.CultStepsOwed > 0 || player.LavaOwed > 0 || player.LavaOwedAny > 0 || player.RockMovesOwed > 0;

    private static MoveResult ApplyPlaceLava(GameState state, PlaceLava move)
    {
        var betweenRounds = state.Phase == Phase.RoundEndSpades && move.Player == state.ActivePlayer;
        if (!betweenRounds && CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        var player = state[move.Player];
        if (player.LavaOwed + player.LavaOwedAny == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no lava to place.");
        if (!state.Layout.Contains(move.Hex) || !OwnsBuildingAt(state, move.Player, move.Hex))
            return MoveResult.Rejected(state, $"Lava goes under one of the {Name(move.Player)}' structures.");
        var dwelling = state.Map[move.Hex]!.Value.Kind == PieceKind.Dwelling;
        if (!dwelling && player.LavaOwedAny == 0)
            return MoveResult.Rejected(state, "This lava goes under a dwelling.");
        var paid = dwelling && player.LavaOwed > 0
            ? player with { LavaOwed = player.LavaOwed - 1 }
            : player with { LavaOwedAny = player.LavaOwedAny - 1 };
        var lava = state.Lava ?? ValueList<int>.Repeat(0, state.Layout.HexCount);
        var count = lava[move.Hex] + 1;
        var events = new List<GameEvent> { new LavaPlaced(move.Player, move.Hex, count) };
        var warnings = new List<Issue>();
        var next = state.WithPlayer(paid) with { Lava = lava.SetItem(move.Hex, count) };
        if (betweenRounds && !OwesBetweenRounds(next[move.Player]))
            next = ContinueAfterRoundEnd(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // Kingdom of Ember, the turn's action: a structure with lava under it (and no key) flows along the path. On every
    // land hex it leaves (its start included) one tile stays, making it Volcano; a river hex or another faction's home
    // terrain takes a tile (never the last); it can't stop on a river, never enters Volcano, and keeps going past a land
    // hex only with 2 tiles or more under it. Not a build: nobody is offered power, nothing scores. A river crossed may
    // take a free bridge across it (no structure of theirs needed at either end).
    private static MoveResult ApplyLetLavaFlow(GameState state, LetLavaFlow move)
    {
        MoveResult flowed = FlowAlong(state, move, riverEnd: false);
        if (!flowed.Accepted)
            return flowed;
        var next = flowed.State;
        var faction = move.Player;
        var rivers = move.Path.Where(hex => state.HexTerrain[hex] == Terrain.River).ToList(); // the river hexes crossed

        if (move.BridgeA is int bridgeA && move.BridgeB is int bridgeB)
        {
            var (a, b) = (Math.Min(bridgeA, bridgeB), Math.Max(bridgeA, bridgeB));
            if (!BridgeEdges(next).Contains((a, b)))
                return MoveResult.Rejected(state, "A bridge must join two land hexes across the edge between two river hexes.");
            if (!next.Layout.Neighbours(a).Where(next.Layout.Neighbours(b).Contains).Any(rivers.Contains))
                return MoveResult.Rejected(state, "The free bridge goes across a river hex the lava crossed.");
            if (next.IsBridged(a, b))
                return MoveResult.Rejected(state, "There is already a bridge there.");
            // No structure of theirs needed at either end (as on BGA: 557813258 #239, E3–G3 across F2
            // with the structure gone on to H2).
            if (next.BridgesLeft(faction) <= 0)
                return MoveResult.Rejected(state, $"The {Name(faction)} have no bridges left.");
            var bridge = new Bridge(faction, a, b);
            next = next with { Bridges = next.Bridges.Add(bridge) };
        }
        else if (move.BridgeA is not null || move.BridgeB is not null)
            return MoveResult.Rejected(state, "A bridge needs both its hexes.");
        return MoveResult.Ok(next, flowed.Events, flowed.Issues);
    }

    // The Kingdom of Ember's flow so far, as the board shows it while the path is being chosen: as if it stopped at
    // the path's end (FlowAlong), a river end allowed (the structure over the river on all the tiles it still has).
    // The state itself for no path yet; null if the path so far isn't one the lava can take.
    public static GameState? LavaFlowSoFar(GameState state, Faction faction, int from, IReadOnlyList<int> path)
    {
        if (path.Count == 0)
            return state;
        MoveResult flowed = FlowAlong(state, new LetLavaFlow(faction, from, new ValueList<int>(path)), riverEnd: true);
        return flowed.Accepted ? flowed.State : null;
    }

    // The flow itself, without its free bridge (ApplyLetLavaFlow). riverEnd: the path may end on a river (the flow so
    // far, LavaFlowSoFar): the structure stops over it with all the tiles it still has, and the river stays a river.
    private static MoveResult FlowAlong(GameState state, LetLavaFlow move, bool riverEnd)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        var faction = move.Player;
        if (faction != Faction.KingdomOfEmber)
            return MoveResult.Rejected(state, "Only the Kingdom of Ember let the lava flow.");
        if (!state.Layout.Contains(move.From) || !OwnsBuildingAt(state, faction, move.From))
            return MoveResult.Rejected(state, $"Choose one of the {Name(faction)}' structures.");
        var moving = state.LavaAt(move.From); // the tiles moving with it: all but the one left behind
        if (moving == 0)
            return MoveResult.Rejected(state, "Only a structure with lava under it (more than one volcano tile) can flow.");
        if (state.PlacedKeys.Any(key => key.Hex == move.From))
            return MoveResult.Rejected(state, "A structure with a key token under it can't flow.");
        if (move.Path.Count == 0)
            return MoveResult.Rejected(state, "Choose where the lava flows.");

        var left = new List<int>();   // land hexes left behind (Volcano now)
        var rivers = new List<int>(); // river hexes crossed
        var at = move.From;
        for (var i = 0; i < move.Path.Count; i++)
        {
            var to = move.Path[i];
            var last = i == move.Path.Count - 1;
            if (!state.Layout.Contains(to) || !state.Layout.Neighbours(at).Contains(to))
                return MoveResult.Rejected(state, "Lava flows only to a hex next to the one before.");
            var terrain = state.HexTerrain[to];
            if (terrain == Terrain.Volcano)
                return MoveResult.Rejected(state, "Lava can't flow onto a Volcano hex.");
            if (state.IsOccupied(to) || to == move.From || left.Contains(to) || rivers.Contains(to) || state.PlacedKeys.Any(key => key.Hex == to))
                return MoveResult.Rejected(state, "Lava flows only onto empty hexes, each once.");
            if (terrain == Terrain.River || state.IsOthersHome(faction, terrain))
            {
                moving--;
                if (moving < 1)
                    return MoveResult.Rejected(state, terrain == Terrain.River
                        ? "Crossing that river would take the last volcano tile under the structure."
                        : $"{terrain} is another faction's home terrain: it would take the last volcano tile under the structure.");
            }
            if (terrain == Terrain.River)
            {
                if (last && !riverEnd)
                    return MoveResult.Rejected(state, "Lava can't stop on a river hex.");
                rivers.Add(to);
            }
            else if (!last)
            {
                if (moving < 2)
                    return MoveResult.Rejected(state, "Lava flows on from a land hex only with more than one volcano tile under the structure.");
                moving--;
                left.Add(to);
            }
            at = to;
        }

        var piece = state.Map[move.From]!.Value;
        var onRiver = state.HexTerrain[at] == Terrain.River; // (only the flow so far ends on one)
        var lava = (state.Lava ?? ValueList<int>.Repeat(0, state.Layout.HexCount)).SetItem(move.From, 0)
            .SetItem(at, onRiver ? moving : moving - 1); // on land, one of them is the hex's own (Volcano now)
        var events = new List<GameEvent> { new LavaFlowed(faction, move.From, move.Path, new ValueList<int>(left)) };
        var warnings = new List<Issue>();
        var next = state with { Map = state.Map.SetItem(move.From, null).SetItem(at, piece), Lava = lava, ActionTaken = true };
        foreach (var hex in onRiver ? left : left.Append(at))
            next = ChangeTerrain(next, hex, Terrain.Volcano, events);
        var player = next[faction];
        if (player.TownHexes.Contains(move.From)) // it leaves its town (its key stays behind)
        {
            // What's left of the town, in parts still joined together (touching or bridged): the part holding the
            // town's key stays a town, whatever its power; every other part is in no town any more and may form
            // another. As on BGA (585709876 #244, #261, #278, then towns at #269 and #281; 557813258
            // #288: the key's part, below a town, kept as one).
            var rest = TownOf(state, faction, move.From).Where(hex => hex != move.From).ToList();
            var leaving = new List<int>();
            foreach (var part in JoinedParts(next, rest))
                if (!next.PlacedKeys.Any(key => key.Owner == faction && part.Contains(key.Hex)))
                    leaving.AddRange(part);
            next = next.WithPlayer(player with
            {
                TownHexes = new ValueList<int>(player.TownHexes.Where(hex => hex != move.From && !leaving.Contains(hex))),
            });
            if (leaving.Count > 0)
                warnings.Add(new Issue(Severity.Note, $"Part of the {Name(faction)}' town there is no longer a town: its structures may form another."));
        }

        return MoveResult.Ok(next, events, warnings);
    }

    // The structures of the town this hex is in: its town hexes joined to it (touching or bridged).
    private static List<int> TownOf(GameState state, Faction faction, int hex) =>
        JoinedParts(state, state[faction].TownHexes.Append(hex).Distinct().ToList()).First(part => part.Contains(hex));

    // These hexes in groups joined together (touching or bridged), each in the order found.
    private static List<List<int>> JoinedParts(GameState state, IReadOnlyCollection<int> hexes)
    {
        var parts = new List<List<int>>();
        var left = hexes.ToList();
        while (left.Count > 0)
        {
            var part = new List<int> { left[0] };
            left.RemoveAt(0);
            for (var i = 0; i < part.Count; i++)
                foreach (var other in left.ToList())
                    if (state.Layout.Neighbours(part[i]).Contains(other)
                        || state.IsBridged(Math.Min(part[i], other), Math.Max(part[i], other)))
                    {
                        part.Add(other);
                        left.Remove(other);
                    }
            parts.Add(part);
        }
        return parts;
    }

    // ---------- Geologists ----------

    private static bool WheelNeighbours(Terrain a, Terrain b)
    {
        int i = Terrains.Wheel.IndexOf(a), j = Terrains.Wheel.IndexOf(b);
        var apart = Math.Abs(i - j);
        return i >= 0 && j >= 0 && (apart == 1 || apart == Terrains.Wheel.Length - 1);
    }

    // Geologists: may a rock cover this terrain (and so a dwelling go on it)? Uncovered, and their ring's terrain or next
    // to a covered one on their wheel.
    public static bool RockCanCover(PlayerState player, Terrain terrain) =>
        Terrains.IsBasic(terrain) && !player.RockList.Contains(terrain)
        && (terrain == player.StartingTerrain || player.RockList.Any(rock => WheelNeighbours(rock, terrain)));

    // Geologists: may the rock on this terrain be lifted (with a spade)? Covered, next to an uncovered one.
    public static bool RockCanLift(PlayerState player, Terrain terrain) =>
        player.RockList.Contains(terrain) && Terrains.Wheel.Any(other => !player.RockList.Contains(other) && WheelNeighbours(other, terrain));

    // A rock onto this terrain; all seven covered: all come off.
    private static GameState CoverRock(GameState state, Faction faction, Terrain terrain, List<GameEvent> events)
    {
        var player = state[faction];
        var rocks = player.RockList.Add(terrain);
        events.Add(new RockMoved(faction, terrain, true));
        if (rocks.Count >= Terrains.Wheel.Length)
        {
            events.Add(new RocksCleared(faction));
            rocks = ValueList<Terrain>.Empty;
        }
        return state.WithPlayer(player with { Rocks = rocks });
    }

    // A spade's use (or the stronghold's): a rock onto the terrain if it may go there, else off it if it may come off.
    // Null if neither.
    private static GameState? UseRockSpade(GameState state, Faction faction, Terrain terrain, List<GameEvent> events)
    {
        var player = state[faction];
        if (RockCanCover(player, terrain))
            return CoverRock(state, faction, terrain, events);
        if (!RockCanLift(player, terrain))
            return null;
        events.Add(new RockMoved(faction, terrain, false));
        return state.WithPlayer(player with { Rocks = player.RockList.RemoveAt(player.RockList.IndexOf(terrain)) });
    }

    private const string RockRule =
        "A rock goes onto an uncovered terrain at their ring or next to a covered one, or off a covered terrain next to an uncovered one.";

    private static MoveResult ApplyMoveRock(GameState state, MoveRock move)
    {
        var betweenRounds = state.Phase == Phase.RoundEndSpades && move.Player == state.ActivePlayer;
        if (!betweenRounds && CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (!state.IsInGame(move.Player) || state[move.Player].RockMovesOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no spade to use on their rocks.");
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        if (UseRockSpade(state, move.Player, move.Terrain, events) is not GameState moved)
            return MoveResult.Rejected(state, RockRule);
        var next = moved.WithPlayer(moved[move.Player] with { RockMovesOwed = moved[move.Player].RockMovesOwed - 1 });
        // The spade scores the round tile's spades as a transform's would (as on BGA: 920031063 #129).
        // None between rounds (no round tile then).
        next = GainVictoryPoints(next, move.Player, RoundTilePoints(next, move.Player, tile => tile.VictoryPointsPerSpade), events,
            new PointsArea.RoundTile());
        if (betweenRounds && !OwesBetweenRounds(next[move.Player]))
            next = ContinueAfterRoundEnd(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // Done with rock spades still owed: they are given up.
    private static MoveResult GiveUpRockSpades(GameState state, Faction faction)
    {
        var betweenRounds = state.Phase == Phase.RoundEndSpades && faction == state.ActivePlayer;
        if (!betweenRounds && CheckTurn(state, faction) is { } refusal)
            return refusal;
        var events = new List<GameEvent> { new SpadesForfeited(faction, state[faction].RockMovesOwed) };
        var warnings = new List<Issue>();
        var next = state.WithPlayer(state[faction] with { RockMovesOwed = 0 });
        if (betweenRounds && !OwesBetweenRounds(next[faction]))
            next = ContinueAfterRoundEnd(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    private static MoveResult ApplyBonusMoveRock(GameState state, BonusMoveRock move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.MoveRock);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var events = new List<GameEvent>();
        if (UseRockSpade(state, move.Player, move.Terrain, events) is not GameState moved)
            return MoveResult.Rejected(state, RockRule);
        var next = MarkBonusActionUsed(moved with { ActionTaken = true }, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Changelings ----------

    // The letter of each spot on the Changelings' board, left to right; the ninth dwelling's spot is beside the
    // stronghold.
    private static readonly char[] ChangelingDwellingLetters = { 'A', 'B', 'C', 'A', 'B', 'C', 'A', 'B', 'C' };
    private static readonly char[] ChangelingTradingPostLetters = { 'A', 'C', 'B', 'A' };
    private static readonly char[] ChangelingTempleLetters = { 'C', 'A', 'B' };

    // Changelings: a piece's letter (A, B or C), from the spot it started on: it keeps its colour wherever it goes back.
    public static char ChangelingLetter(Piece piece) => piece.Kind switch
    {
        PieceKind.Dwelling => ChangelingDwellingLetters[Math.Clamp(piece.Number, 0, ChangelingDwellingLetters.Length - 1)],
        PieceKind.TradingPost => ChangelingTradingPostLetters[Math.Clamp(piece.Number, 0, ChangelingTradingPostLetters.Length - 1)],
        PieceKind.Temple => ChangelingTempleLetters[Math.Clamp(piece.Number, 0, ChangelingTempleLetters.Length - 1)],
        PieceKind.Stronghold => 'B',
        _ => 'A',
    };

    // Changelings: the terrain (and colour) of one of their pieces: A their starting terrain, B and C chosen later
    // (null until then).
    public static Terrain? ChangelingTerrain(PlayerState player, Piece piece) => ChangelingLetter(piece) switch
    {
        'A' => player.StartingTerrain,
        'B' => player.TerrainB,
        _ => player.TerrainC,
    };

    // The terrain this dwelling is built on: its faction's (PlayerState.BuildTerrain), or the Changelings' dwelling's own.
    public static Terrain? DwellingTerrain(GameState state, Piece dwelling) =>
        dwelling.Owner == Faction.Changelings ? ChangelingTerrain(state[dwelling.Owner], dwelling) : state[dwelling.Owner].BuildTerrain;

    // May this dwelling be built now: the leftmost in its row, or (Changelings with their stronghold) the one beside it.
    private static MoveResult? CheckDwellingAvailable(GameState state, Piece dwelling)
    {
        var player = state[dwelling.Owner];
        if (player.ExtraDwelling == dwelling)
            return player.StrongholdBuilt ? null
                : MoveResult.Rejected(state, $"The {Name(dwelling.Owner)}' dwelling beside their stronghold can be built once the stronghold is.");
        return CheckLeftmostInBank(state, player.DwellingBank, dwelling, "dwelling");
    }

    private static MoveResult ApplyChooseChangelingTerrains(GameState state, ChooseChangelingTerrains move)
    {
        if (move.Player != Faction.Changelings || !state.IsInGame(move.Player))
            return MoveResult.Rejected(state, "Only the Changelings choose B and C terrains.");
        var player = state[move.Player];
        if (player.TerrainB is not null)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have already chosen {player.TerrainB} and {player.TerrainC}.");
        // Once the factions are confirmed, with any volcano faction's starting terrain, in the order the factions were
        // chosen (setting up a game in progress: any time before the dwellings).
        if (state.Options.SetUpInProgress
                ? state.Phase <= Phase.ChoosingVolcanoTerrain || state.Phase >= Phase.PlacingDwellings
                : state.Phase != Phase.ChoosingVolcanoTerrain)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} choose their B and C terrains once every faction is chosen.");
        if (!state.Options.SetUpInProgress && LateTerrainChooser(state) is Faction first && first != move.Player)
            return MoveResult.Rejected(state, $"The {Name(first)} choose first (in the order the factions were chosen).");
        if (move.B == move.C)
            return MoveResult.Rejected(state, "Choose two different terrains.");
        foreach (var terrain in new[] { move.B, move.C })
        {
            if (!Terrains.IsBasic(terrain))
                return MoveResult.Rejected(state, $"{terrain} is not one of the seven basic terrains.");
            if (terrain == player.StartingTerrain)
                return MoveResult.Rejected(state, $"{terrain} is already the {Name(move.Player)}' A terrain.");
            if (state.TakenTerrains(move.Player).Contains(terrain))
                return MoveResult.Rejected(state, $"{terrain} is another faction's home or starting terrain.");
        }
        var events = new List<GameEvent> { new ChangelingTerrainsChosen(move.Player, move.B, move.C) };
        var next = state.WithPlayer(player with { TerrainB = move.B, TerrainC = move.C });
        if (state.Phase == Phase.ChoosingVolcanoTerrain)
            next = AfterLateTerrains(next, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }
}
