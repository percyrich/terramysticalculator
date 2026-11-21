namespace Core;

// Terrain: transforming a hex as the turn's action, what it costs to reach and to dig, the volcano factions' way of
// doing it, and free spades (from a bonus action, a power action or a round tile), which are spent at once.
public static partial class Rules
{
    // ---------- Transforming (the turn's action) ----------

    private static MoveResult ApplyTransformTerrain(GameState state, TransformTerrain move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (CheckHexTarget(state, move.Player, move.Hex, allowSkip: true, out var reach) is { } hexRefusal)
            return hexRefusal;

        if (CheckTransformTo(state, move.Player, move.Hex, move.To) is { } terrainRefusal)
            return terrainRefusal;
        var volcano = state[move.Player].Kind == FactionKind.Volcano;
        if (volcano && CheckVolcanoPrice(state, move.Player, move.Hex) is { } priceRefusal)
            return priceRefusal;
        if (!volcano && CheckSpadePower(state, move.Player, SpadesFor(state, move.Player, move.Hex, move.To) ?? 0) is { } powerRefusal)
            return powerRefusal;

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = PayForReach(state, move.Player, move.Hex, reach, events, warnings);
        next = volcano
            ? MakeVolcano(next, move.Player, move.Hex, events) with { ActionTaken = true }
            : Transform(next, move.Player, move.Hex, move.To, freeSpades: 0, events, warnings) with { ActionTaken = true };
        warnings.AddRange(DebtWarning(next[move.Player]));
        // Made home terrain: a dwelling may now be built there, paid (BuildOnSpadedHex), or the landscape placed
        // (PlaceLandscape), or Done gives that up. (Architects: not on a hex their bridges make cheaper: building there
        // with the discount is done by dragging, as BuildDwelling.)
        var buildable = next with { SpadedHexes = ValueList.Of(move.Hex) };
        if (SpadingOpen(buildable, move.Player) && ArchitectsDiscount(state, move.Player, move.Hex) == 0)
            next = buildable;
        return MoveResult.Ok(next, events, warnings);
    }

    // Turns the hex into `to` with spades: free ones first (up to freeSpades), the rest paid at the faction's
    // spade cost (workers, or priests for the Darklings, who score for them). Then whatever spades trigger:
    // the round tile's VP per spade (main phase only; not the Prospectors' golden spades), the Halflings' VP, the Alchemists' power (with their
    // stronghold). Nothing happens if the hex already is `to`.
    // discount: spades not needed at all (the Architects' bridges).
    private static GameState Transform(GameState state, Faction faction, int hex, Terrain to, int freeSpades,
        List<GameEvent> events, List<Issue> warnings, int discount = 0)
    {
        var needed = SpadesFor(state, faction, hex, to) ?? 0;
        if (needed == 0)
            return state;
        var spades = Math.Max(0, needed - discount);
        var free = Math.Min(spades, freeSpades);
        var next = PaySpades(state, faction, hex, spades - free, events, warnings);
        next = ChangeTerrain(next, hex, to, events);
        // The Prospectors' golden spades don't score the round tile's spades (as on BGA).
        if (faction != Faction.Prospectors)
            next = GainVictoryPoints(next, faction, spades * RoundTilePoints(next, faction, tile => tile.VictoryPointsPerSpade), events,
                new PointsArea.RoundTile(), new PointsArea.OnHex(hex));
        if (faction == Faction.Halflings)
            next = GainVictoryPoints(next, faction, spades * Config.HalflingsVictoryPointsPerSpade, events,
                new PointsArea.Portrait(), new PointsArea.OnHex(hex));
        if (faction == Faction.Alchemists && next[faction].StrongholdBuilt)
            next = GainPower(next, faction, spades * Config.AlchemistsPowerPerSpade, PowerSource.Spades, events);
        return next;
    }

    // Pays for this many spades: workers (debt allowed), or for the Darklings a priest each (2 VP each priest paid;
    // without a ready priest the spade is still made, with a warning, like the upgrades' priests).
    private static GameState PaySpades(GameState state, Faction faction, int hex, int spades, List<GameEvent> events,
        List<Issue> warnings)
    {
        if (spades <= 0)
            return state;
        var player = state[faction];
        if (faction == Faction.Enlightened) // power instead of workers (checked beforehand: CheckSpadePower)
            return BurnAndSpend(state, faction, player.SpadePower * spades, events) ?? state;
        if (faction == Faction.Prospectors) // golden spades: coins, each bringing 1 VP and 1 power
        {
            state = state.WithPlayer(player.Pay(player.SpadeCost * spades));
            state = GainVictoryPoints(state, faction, spades * Config.ProspectorsVictoryPointsPerSpade, events,
                new PointsArea.Spades(), new PointsArea.OnHex(hex));
            return GainPower(state, faction, spades * Config.ProspectorsPowerPerSpade, PowerSource.Spades, events);
        }
        if (!Factions.SpadesCostPriests(faction))
            return state.WithPlayer(player.Pay(player.SpadeCost * spades));
        // A priest not in hand is owed (PriestDebt), and counts as paid.
        var paid = spades * Config.DarklingsPriestsPerSpade;
        for (var i = 0; i < paid; i++)
            state = PayPriest(state, faction, events, warnings);
        return GainVictoryPoints(state, faction, paid * Config.DarklingsVictoryPointsPerPriestSpade, events,
            new PointsArea.Portrait(), new PointsArea.OnHex(hex));
    }

    // A carpet flight or a tunnel to reach the hex: its cost (a priest; workers) and 4 VP. Nothing otherwise.
    private static GameState PayForReach(GameState state, Faction faction, int hex, Reach reach, List<GameEvent> events,
        List<Issue> warnings)
    {
        if (reach is not (Reach.Carpet or Reach.Tunnel))
            return state;
        state = state with { SkipUsed = true }; // not again in this action (Apply clears it when the next action begins)
        if (reach == Reach.Carpet)
        {
            for (var i = 0; i < Config.FakirsCarpetPriests; i++)
                state = PayPriest(state, faction, events, warnings);
            if (faction == Faction.Fakirs && state.HasLandscape(faction)) // the Flight School
            {
                var coins = state[faction].Coins + Landscapes.FakirsCarpetCoins;
                state = state.WithPlayer(state[faction] with { Coins = coins });
                events.Add(new CoinGained(faction, coins));
            }
            return GainVictoryPoints(state, faction, Config.CarpetVictoryPoints, events, new PointsArea.Portrait(), new PointsArea.OnHex(hex));
        }
        state = state.WithPlayer(state[faction].Pay(ReachCost(state, faction, reach)));
        return GainVictoryPoints(state, faction, Config.TunnelVictoryPoints, events, new PointsArea.Portrait(), new PointsArea.OnHex(hex));
    }

    // This faction can turn the hex into `to`: it is a basic terrain (Ice and Volcano never change again), and `to` is
    // another basic terrain, or Ice for an ice faction. Volcano factions only make Volcano; the Riverwalkers never
    // transform.
    private static MoveResult? CheckTransformTo(GameState state, Faction faction, int hex, Terrain to)
    {
        var from = state.HexTerrain[hex];
        var player = state[faction];
        if (faction is Faction.Riverwalkers or Faction.Geologists)
            return MoveResult.Rejected(state, $"The {Name(faction)} can't transform terrain.");
        if (faction == Faction.KingdomOfEmber)
            return MoveResult.Rejected(state, $"The {Name(faction)} make Volcano only by letting the lava flow.");
        if (player.Kind == FactionKind.Volcano && to != Terrain.Volcano)
            return MoveResult.Rejected(state, $"The {Name(faction)} only ever make hexes Volcano.");
        var allowed = Terrains.IsBasic(to) || (to == Terrain.Ice && player.Kind == FactionKind.Ice)
            || (to == Terrain.Volcano && player.Kind == FactionKind.Volcano);
        if (!allowed)
            return MoveResult.Rejected(state, $"A hex can only be transformed into one of the seven basic terrains, not {to}.");
        if (!Terrains.IsBasic(from))
            return MoveResult.Rejected(state, $"{from} cannot be transformed.");
        if (from == to)
            return MoveResult.Rejected(state, $"That hex is already {to}.");
        return null;
    }

    // The hex becomes `to` (nothing happens if it already is). Spades and what they score are Transform's.
    private static GameState ChangeTerrain(GameState state, int hex, Terrain to, List<GameEvent> events)
    {
        var from = state.HexTerrain[hex];
        if (from == to)
            return state;
        events.Add(new TerrainTransformed(hex, from, to));
        return state with { HexTerrain = state.HexTerrain.SetItem(hex, to) };
    }

    // ---------- Volcano ----------

    // A volcano faction can pay to make this hex Volcano: enough power tokens (Dragonlords), or one track with enough
    // steps (Acolytes).
    private static MoveResult? CheckVolcanoPrice(GameState state, Faction faction, int hex)
    {
        if (VolcanoPrice(state, faction, hex) is not int price)
            return MoveResult.Rejected(state, $"{state.HexTerrain[hex]} cannot be made Volcano.");
        if (faction == Faction.Dragonlords && state[faction].Power.Total < price)
            return MoveResult.Rejected(state, $"The {Name(faction)} need {price} power token{(price == 1 ? "" : "s")} to remove to make that hex Volcano.");
        if (faction == Faction.Acolytes && TracksThatCanPay(state, faction, price).Count == 0)
            return MoveResult.Rejected(state, $"The {Name(faction)} need {price} steps on one track to pay to make that hex Volcano.");
        return null;
    }

    // A volcano faction makes the hex Volcano (no spades, so nothing scores for spades): the Dragonlords remove power
    // tokens for the game (lowest bowls first); the Acolytes owe steps from a track of their choice (PayCultSteps).
    // The caller has checked the price can be paid (CheckVolcanoPrice).
    private static GameState MakeVolcano(GameState state, Faction faction, int hex, List<GameEvent> events)
    {
        var price = VolcanoPrice(state, faction, hex)!.Value;
        var next = ChangeTerrain(state, hex, Terrain.Volcano, events);
        if (faction == Faction.Dragonlords)
            return RemoveTokens(next, faction, price, events);
        if (faction == Faction.Firewalkers) // VP (never behind their pyro token: Apply)
            return GainVictoryPoints(next, faction, -price, events, new PointsArea.Portrait(), new PointsArea.OnHex(hex));
        return next.WithPlayer(next[faction] with { CultStepsToPay = next[faction].CultStepsToPay + price, VolcanoStepsPaid = null });
    }

    // ---------- Free spades ----------

    // Spades gained, to spend at once (SpendSpades). Factions without spades get something else: the Dragonlords a new
    // power token in bowl 1 per spade, the Acolytes a step per spade on a track of their choice (TakeCultStep); the
    // Riverwalkers lose them.
    private static GameState GainSpades(GameState state, Faction faction, int count, List<GameEvent> events, List<Issue>? warnings = null)
    {
        if (count == 0)
            return state;
        events.Add(new SpadesGained(faction, count));
        switch (faction)
        {
            case Faction.Dragonlords:
                return AddTokens(state, faction, 1, count * Config.DragonlordsTokensPerSpade, events);
            case Faction.Acolytes:
                for (var i = 0; i < count * Config.AcolytesStepsPerSpade; i++)
                    events.Add(new CultStepOwed(faction));
                return state.WithPlayer(state[faction] with { CultStepsOwed = state[faction].CultStepsOwed + count * Config.AcolytesStepsPerSpade });
            case Faction.Prospectors: // a priest for every spade, which still scores the round tile's spades (as on BGA)
                for (var i = 0; i < count; i++)
                    state = GainPriest(state, faction, events, warnings ?? new List<Issue>());
                return GainVictoryPoints(state, faction, count * RoundTilePoints(state, faction, tile => tile.VictoryPointsPerSpade), events,
                    new PointsArea.RoundTile());
            case Faction.Firewalkers: // the pyro token 4 back for each (never below 0)
            {
                var player = state[faction];
                var to = Math.Max(0, player.Pyro - count * Config.FirewalkersPyroPerSpade);
                events.Add(new PyroMoved(faction, player.Pyro, to));
                return state.WithPlayer(player with { Pyro = to });
            }
            case Faction.KingdomOfEmber: // lava under a dwelling for each
                return OweLava(state, faction, count, anyStructure: false, events, warnings);
            case Faction.Geologists: // a rock placed or removed for each (MoveRock)
                events.Add(new RockMovesOwed(faction, count));
                return state.WithPlayer(state[faction] with { RockMovesOwed = state[faction].RockMovesOwed + count });
            case Faction.Riverwalkers:
                events.Add(new SpadesForfeited(faction, count));
                warnings?.Add(new Issue(Severity.Note, $"The {Name(faction)} can't use spades, so {(count == 1 ? "the spade is" : $"the {count} spades are")} lost."));
                return state;
        }
        return state.WithPlayer(state[faction] with { SpadesOwed = state[faction].SpadesOwed + count });
    }

    // Free spades go on a hex in range, turning it into any basic terrain. Free spades are used first;
    // in the main phase any further spades the change needs may be paid for (workers), between rounds not.
    // Each hex takes free spades once per batch, and must take at least one.
    private static MoveResult ApplySpendSpades(GameState state, SpendSpades move)
    {
        if (CheckSpending(state, move.Player) is { } refusal)
            return refusal;
        var owed = state[move.Player].SpadesOwed;
        if (owed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no free spades left. Build on a highlighted hex, or press Done.");
        // Carpets and tunnels reach hexes for spades during the round, not between rounds (except the Dwarves' tunnels
        // with their Great Tunnel).
        if (CheckHexTarget(state, move.Player, move.Hex, allowSkip: state.Phase == Phase.Playing || TunnelsBetweenRounds(state, move.Player),
                out var reach) is { } hexRefusal)
            return hexRefusal;
        // The landscape placed with this batch's spades doesn't bring other hexes in range for the rest of the batch.
        if (state.LandscapeBatchHex is int landscape
            && ReachOf(state with { Map = state.Map.SetItem(landscape, null) }, move.Player, move.Hex, allowSkip: !state.SkipUsed) == Reach.None)
            return MoveResult.Rejected(state, $"That hex is in range only through the {Landscapes.Name(move.Player)} just placed with these spades.");
        if (state.SpadedHexes.Contains(move.Hex))
            return MoveResult.Rejected(state, "Free spades have already been spent on that hex.");
        if (CheckTransformTo(state, move.Player, move.Hex, move.To) is { } terrainRefusal)
            return terrainRefusal;

        var from = state.HexTerrain[move.Hex];
        if (SpadesFor(state, move.Player, move.Hex, move.To) is not int spades)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} can't make {move.To} with spades.");
        // Architects, in the main phase, making the hex at the end of their bridge their home terrain: the bridge saves
        // spades, and a dwelling must then go there (as on BGA: 649833286 #342).
        var discount = state.Phase == Phase.Playing && state[move.Player].CanBuildOn(move.To)
            ? Math.Min(spades, ArchitectsDiscount(state, move.Player, move.Hex)) : 0;
        spades -= discount;
        var free = Math.Min(spades, owed);
        var paidSpades = spades - free;
        if (paidSpades > 0 && state.Phase != Phase.Playing)
            return MoveResult.Rejected(state,
                $"That needs {spades} spades but only {owed} {(owed == 1 ? "is" : "are")} left: spades between rounds cannot be topped up.");
        // Wisps' trading post spade: next to it (a bridge counts), exactly 1 spade.
        if (state.SpadeAnchorHex is int anchor && !AdjacentHexes(state, move.Player, anchor).Contains(move.Hex))
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' free spade goes on a hex next to their new trading post.");
        if (state.SpadeAnchorHex is not null && paidSpades > 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' free spade can't be topped up: choose a terrain 1 spade away.");
        if (CheckSpadePower(state, move.Player, paidSpades) is { } powerRefusal)
            return powerRefusal;

        var events = new List<GameEvent> { new SpadesSpent(move.Player, move.Hex, from, move.To, free, paidSpades) };
        var warnings = new List<Issue>();
        var next = PayForReach(state, move.Player, move.Hex, reach, events, warnings);
        next = Transform(next, move.Player, move.Hex, move.To, free, events, warnings, discount);
        next = next.WithPlayer(next[move.Player] with
        {
            SpadesOwed = owed - free,
            ArchitectsBuildHex = discount > 0 ? move.Hex : next[move.Player].ArchitectsBuildHex,
        }) with { SpadedHexes = next.SpadedHexes.Add(move.Hex) };
        if (paidSpades > 0 || reach == Reach.Tunnel)
            warnings.AddRange(DebtWarning(next[move.Player]));

        if (state.SpadeAnchorHex is not null) // Wisps: nothing is built there now
            next = FinishSpading(next, move.Player, events);
        else if (next[move.Player].SpadesOwed == 0)
            next = EndSpadingIfNothingLeft(next, move.Player, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // Main phase only, once every free spade is spent (or given up with Done): build on one hex just made home
    // terrain (with free spades, a transform as the action, or the sandstorm). Ends the spending.
    private static MoveResult ApplyBuildOnSpadedHex(GameState state, BuildOnSpadedHex move)
    {
        var dwelling = move.Dwelling;
        if (dwelling.Kind != PieceKind.Dwelling)
            return MoveResult.Rejected(state, "That piece is not a dwelling.");
        if (CheckSpending(state, dwelling.Owner) is { } refusal)
            return refusal;
        if (state.Phase != Phase.Playing)
            return MoveResult.Rejected(state, "Spades from the end of a round do not allow building.");
        if (state[dwelling.Owner].SpadesOwed > 0)
            return MoveResult.Rejected(state,
                $"The {Name(dwelling.Owner)} have free spades left: spend them first (or press Done to give them up), then build.");
        if (!state.SpadedHexes.Contains(move.Hex))
            return MoveResult.Rejected(state, "Only a hex just transformed with free spades can be built on now.");
        if (state[dwelling.Owner].ArchitectsBuildHex is int saved && saved != move.Hex)
            return MoveResult.Rejected(state, $"The {Name(dwelling.Owner)}' bridge saved spades on {GameLog.HexName(state, saved)}: their dwelling goes there.");
        if (CheckDwellingAvailable(state, dwelling) is { } bankRefusal)
            return bankRefusal;
        if (state.IsOccupied(move.Hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        var buildable = dwelling.Owner == Faction.Changelings
            ? state.HexTerrain[move.Hex] == DwellingTerrain(state, dwelling)
            : state[dwelling.Owner].CanBuildOn(state.HexTerrain[move.Hex]);
        if (!buildable)
            return MoveResult.Rejected(state, $"Only {DwellingTerrain(state, dwelling)} can be built on.");

        var events = new List<GameEvent>();
        var next = FinishSpading(state, dwelling.Owner, events);
        next = PlaceDwelling(next, dwelling, move.Hex, DwellingCost(next, dwelling.Owner, move.Hex), events);
        return MoveResult.Ok(next, events, DebtWarning(next[dwelling.Owner]));
    }

    // Done: any spades left are lost; between rounds, the next player (or round) follows. In the main phase, with
    // spades left and a hex already made home terrain, Done gives up only the spades: the dwelling may still be
    // built (a second Done gives that up too).
    private static MoveResult ApplyFinishSpades(GameState state, FinishSpades move)
    {
        if (state.IsInGame(move.Player) && state[move.Player].RockMovesOwed > 0) // Geologists: their rock spades given up
            return GiveUpRockSpades(state, move.Player);
        if (CheckSpending(state, move.Player) is { } refusal)
            return refusal;

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var owed = state[move.Player].SpadesOwed;
        // The Architects' bridge saved spades on a hex: the dwelling there can't be given up (the spades left can).
        if (state[move.Player].ArchitectsBuildHex is int saved && owed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' bridge saved spades on {GameLog.HexName(state, saved)}: build their dwelling there.");
        if (state.Phase == Phase.Playing && owed > 0)
        {
            var spadesGone = state.WithPlayer(state[move.Player] with { SpadesOwed = 0 });
            if (SpadingOpen(spadesGone, move.Player))
            {
                events.Add(new SpadesForfeited(move.Player, owed));
                return MoveResult.Ok(spadesGone, events, warnings);
            }
        }
        var next = FinishSpading(state, move.Player, events);
        if (next.Phase == Phase.RoundEndSpades)
            next = ContinueAfterRoundEnd(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // The player is spending free spades now (the main phase after a spade bonus action, or between rounds).
    private static MoveResult? CheckSpending(GameState state, Faction player)
    {
        if (state.Phase == Phase.RoundEndSpades)
            return player == state.ActivePlayer ? null : MoveResult.Rejected(state, $"It is the {Name(state.ActivePlayer)}' turn to spend spades.");
        if (CheckTurn(state, player) is { } refusal)
            return refusal;
        if (!state.SpadesPending)
            return MoveResult.Rejected(state, $"The {Name(player)} have no free spades to spend.");
        return null;
    }

    // All free spades are spent. In the main phase the batch stays open if a dwelling could still be built
    // on one of the hexes; otherwise it ends (and between rounds, play moves on).
    private static GameState EndSpadingIfNothingLeft(GameState state, Faction faction, List<GameEvent> events, List<Issue> warnings)
    {
        if (state.Phase == Phase.Playing && SpadingOpen(state, faction))
            return state;
        var next = FinishSpading(state, faction, events);
        return next.Phase == Phase.RoundEndSpades ? ContinueAfterRoundEnd(next, events, warnings) : next;
    }

    private static GameState FinishSpading(GameState state, Faction faction, List<GameEvent> events)
    {
        var owed = state[faction].SpadesOwed;
        if (owed > 0)
            events.Add(new SpadesForfeited(faction, owed));
        return state.WithPlayer(state[faction] with { SpadesOwed = 0, ArchitectsBuildHex = null }) with
        {
            SpadedHexes = ValueList<int>.Empty, LandscapeBatchHex = null, SpadeAnchorHex = null, ShippingBoost = false,
        };
    }
}
