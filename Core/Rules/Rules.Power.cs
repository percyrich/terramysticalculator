namespace Core;

// Power: gaining it and spending it, the power offered to neighbours when somebody builds, and the power actions.
// (Converting power to coins, workers and priests is in Rules.FreeActions.cs.)
public static partial class Rules
{
    // ---------- Gaining, spending, tokens ----------

    private static GameState GainPower(GameState state, Faction faction, int amount, PowerSource source, List<GameEvent> events)
    {
        var player = state[faction];
        var after = player.Power.Gain(amount, out var moved);
        if (moved == 0)
            return state; // nothing could move: all tokens already in bowl 3 (or none left)
        events.Add(new PowerGained(faction, source, moved, player.Power, after));
        return state.WithPlayer(player with { Power = after });
    }

    // Spends this much power from bowl 3, burning first as needed. Null if even burning can't make enough.
    private static GameState? BurnAndSpend(GameState state, Faction faction, int cost, List<GameEvent> events)
    {
        var player = state[faction];
        var wyrm = faction == Faction.ChildrenOfTheWyrm; // 2 tokens on for every one burned
        if (player.Power.BurnsFor(cost, wyrm) is not int burns)
            return null;
        var power = player.Power;
        for (var i = 0; i < burns; i++)
        {
            power = power.BurnOnce(wyrm);
            events.Add(new PowerBurned(faction, power));
        }
        power = power.Spend(cost);
        return state.WithPlayer(player with { Power = power });
    }

    // Power tokens leave the game for good, from the lowest bowls first.
    private static GameState RemoveTokens(GameState state, Faction faction, int count, List<GameEvent> events)
    {
        var player = state[faction];
        var after = player.Power.RemoveTokens(count);
        events.Add(new PowerTokensRemoved(faction, player.Power.Total - after.Total, after));
        return state.WithPlayer(player with { Power = after });
    }

    // New power tokens join a bowl.
    private static GameState AddTokens(GameState state, Faction faction, int bowl, int count, List<GameEvent> events)
    {
        if (count <= 0)
            return state;
        var player = state[faction];
        var after = player.Power.AddTokens(bowl, count);
        events.Add(new PowerTokensAdded(faction, bowl, count, after));
        return state.WithPlayer(player with { Power = after });
    }

    // ---------- Power from neighbours' builds (automatic, part of the move that causes it) ----------

    // When a player builds, every other player gains power for each of their buildings next to that hex.
    // When a player builds (or upgrades), every other player with buildings next to the hex (their own
    // bridges included) is offered power: the total power value of those buildings. Offers wait in player
    // order (the players after the builder, going round) and are answered with AcceptPower / DeclinePower.
    // A player who could gain no power at all (everything in bowl 3) gets no offer. Free offers are accepted
    // automatically (see AcceptFreeOffers).
    private static GameState NeighboursGainPower(GameState state, Faction builder, int hex, List<GameEvent> events)
    {
        if (state.IsPlanning)
            return state; // Free play: nobody is offered power (and the Cultists and Shapeshifters get nothing for it)
        var start = state.TurnOrder.IndexOf(builder);
        var others = Enumerable.Range(1, state.TurnOrder.Count - 1)
            .Select(offset => state.TurnOrder[(start + offset) % state.TurnOrder.Count]);
        foreach (var faction in others)
        {
            var total = OfferNeighbours(state, faction, builder, hex)
                .Where(n => state.Map[n] is { } piece && piece.Owner == faction)
                .Sum(n => Factions.BuildingPower(faction, state.Map[n]!.Value.Kind));
            // Nomads: a build next to their Trading Camp may be answered with a coin, so it is offered even when no power fits.
            if (total == 0 || (state[faction].Power.GainCapacity == 0 && !CampNextTo(state, faction, hex)))
                continue;
            events.Add(new PowerOffered(faction, total));
            state = state with { PowerOffers = state.PowerOffers.Add(new PowerOffer(faction, total, hex, builder)) };
            if (builder is Faction.Cultists or Faction.Shapeshifters)
                state = state with { PendingCultistReaction = new CultistReaction(builder, AnyAccepted: false) };
        }
        return ResolveCultists(AcceptFreeOffers(state, events), events);
    }

    // The hexes next to a build for a neighbour's power: touching it, across the neighbour's own bridges (and the
    // Wyrm's token chains: AdjacentHexes), and across the builder's bridges (as on BGA: 608085870 #326).
    private static IEnumerable<int> OfferNeighbours(GameState state, Faction receiver, Faction builder, int hex) =>
        AdjacentHexes(state, receiver, hex).Concat(state.Bridges
            .Where(bridge => bridge.Owner == builder && (bridge.HexA == hex || bridge.HexB == hex))
            .Select(bridge => bridge.HexA == hex ? bridge.HexB : bridge.HexA)).Distinct();

    // Once every offer from the Cultists' build is answered: a step on a track of their choice if anyone accepted
    // (CultStepsOwed, then TakeCultStep), or 1 power if everyone declined.
    private static GameState ResolveCultists(GameState state, List<GameEvent> events)
    {
        if (state.PendingCultistReaction is not CultistReaction reaction
            || state.PowerOffers.Any(offer => offer.Builder == reaction.Cultists))
            return state;
        state = state with { PendingCultistReaction = null };
        if (reaction.Cultists == Faction.Shapeshifters)
        {
            if (!reaction.AnyAccepted)
                return GainPower(state, reaction.Cultists, Config.ShapeshiftersPowerWhenDeclined, PowerSource.Shapeshifters, events);
            return state.WithPlayer(state[reaction.Cultists] with { TokenOfferOwed = true });
        }
        if (!reaction.AnyAccepted)
            return GainPower(state, reaction.Cultists, Config.CultistsPowerWhenDeclined, PowerSource.Cultists, events);
        events.Add(new CultStepOwed(reaction.Cultists));
        return state.WithPlayer(state[reaction.Cultists] with { CultStepsOwed = state[reaction.Cultists].CultStepsOwed + 1 });
    }

    // The first waiting offer is accepted without asking while it costs nothing (at most 1 power would be
    // gained) and its faction always takes free power; offers from the Cultists' builds are always asked (they
    // care whether anyone accepts). An offer that needs an answer holds up the ones after it, so a player's
    // offers are always settled in order (each one changes how much the next can gain).
    private static GameState AcceptFreeOffers(GameState state, List<GameEvent> events)
    {
        while (state.PowerOffers.Count > 0)
        {
            var first = state.PowerOffers[0];
            if (GainFrom(state, first) > 1 || first.Builder is Faction.Cultists or Faction.Shapeshifters
                || !Factions.AcceptsFreePowerAutomatically(first.Player) || CampNextTo(state, first.Player, first.Hex))
                break;
            state = AcceptOffer(state with { PowerOffers = state.PowerOffers.RemoveAt(0) }, first, events);
        }
        return state;
    }

    // How much of the offer would be gained now: as much as fits (power beyond bowl 3 is lost).
    // (The Firewalkers: only as much as their VP can pay without falling behind their pyro token.)
    private static int GainFrom(GameState state, PowerOffer offer) =>
        Math.Min(Math.Min(offer.Power, state[offer.Player].Power.GainCapacity), AffordableGain(state, offer.Player));

    // Gains the offered power that fits, paying one victory point less than that.
    private static GameState AcceptOffer(GameState state, PowerOffer offer, List<GameEvent> events)
    {
        var gain = GainFrom(state, offer);
        var cost = Math.Max(0, gain - 1 - (offer.Player == Faction.ChildrenOfTheWyrm ? Config.WyrmOfferVictoryPointDiscount : 0));
        state = GainPower(state, offer.Player, gain, PowerSource.NeighbouringBuild, events);
        // Set off by the accepting player's neighbouring building with the most power (the first, if tied).
        var neighbour = OfferNeighbours(state, offer.Player, offer.Builder, offer.Hex)
            .Where(n => state.Map[n] is { } piece && piece.Owner == offer.Player)
            .OrderByDescending(n => Factions.BuildingPower(offer.Player, state.Map[n]!.Value.Kind))
            .Select(n => (int?)n).FirstOrDefault() ?? offer.Hex;
        state = GainVictoryPoints(state, offer.Player, -cost, events, new PointsArea.OnHex(offer.Hex), new PointsArea.OnHex(neighbour));
        events.Add(new PowerOfferAnswered(offer.Player, true, gain, cost));
        if (state.PendingCultistReaction is CultistReaction reaction && offer.Builder == reaction.Cultists)
            state = state with { PendingCultistReaction = reaction with { AnyAccepted = true } };
        return state;
    }

    // Accepting gains as much of the offer as fits (power beyond bowl 3 is lost) and costs one victory
    // point less than that. Only the first waiting offer can be answered.
    private static MoveResult ApplyAnswerPower(GameState state, Faction player, bool accept)
    {
        if (state.PowerOffers.Count == 0 || state.PowerOffers.All(offer => offer.Player != player))
            return MoveResult.Rejected(state, $"The {Name(player)} have no power offer to answer.");
        var first = state.PowerOffers[0];
        if (first.Player != player)
            return MoveResult.Rejected(state, $"The {Name(first.Player)} answer their power offer first.");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state with { PowerOffers = state.PowerOffers.RemoveAt(0) };
        if (!accept)
            events.Add(new PowerOfferAnswered(player, false, 0, 0));
        else
        {
            next = AcceptOffer(next, first, events);
            if (next[player].VictoryPoints < 0)
                warnings.Add(new Issue(Severity.Warning, $"The {Name(player)} now have {next[player].VictoryPoints} victory points."));
        }
        // Offers waiting behind this one that cost nothing are now taken; then the Cultists may react.
        return MoveResult.Ok(ResolveCultists(AcceptFreeOffers(next, events), events), events, warnings);
    }

    // Shapeshifters, after an opponent took the power from their build: 1 VP for a new token in bowl 3, or nothing.
    private static MoveResult ApplyAnswerTokenOffer(GameState state, AnswerTokenOffer move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (!state[move.Player].TokenOfferOwed)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no power token offer to answer.");
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state.WithPlayer(state[move.Player] with { TokenOfferOwed = false });
        if (move.Accept)
        {
            next = AddTokens(next, move.Player, 3, 1, events);
            next = GainVictoryPoints(next, move.Player, -Config.ShapeshiftersTokenVictoryPoints, events,
                new PointsArea.Portrait(), new PointsArea.BowlThree());
            if (next[move.Player].VictoryPoints < 0)
                warnings.Add(new Issue(Severity.Warning, $"The {Name(move.Player)} now have {next[move.Player].VictoryPoints} victory points."));
        }
        return MoveResult.Ok(next, events, warnings);
    }

    // ---------- Special power actions ----------

    private static MoveResult ApplyTakePowerAction(GameState state, TakePowerAction move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Action < 0 || move.Action >= PowerActions.Count)
            return MoveResult.Rejected(state, "There is no such power action.");
        if (!PowerActionOpen(state, move.Player, move.Action))
            return MoveResult.Rejected(state, "That power action has already been taken this round.");

        var action = PowerActions.All[move.Action];
        var cost = PowerActionCost(state, move.Player, move.Action);
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        GameState paid;
        if (move.PayWithCoins) // Chash Dallah's stronghold: coins instead of power, all of it
        {
            if (move.Player != Faction.ChashDallah || !state[move.Player].StrongholdBuilt)
                return MoveResult.Rejected(state, "Only Chash Dallah, with their stronghold, pay for power actions with coins.");
            var payer = state[move.Player] with { Coins = state[move.Player].Coins - cost };
            paid = state.WithPlayer(payer);
            warnings.AddRange(DebtWarning(payer));
        }
        else if (BurnAndSpend(state, move.Player, cost, events) is GameState spent)
            paid = spent;
        else
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {cost} power in bowl 3 (burning included) for that.");

        var next = paid with
        {
            PowerActionsTaken = paid.PowerActionsTaken.Contains(move.Action) ? paid.PowerActionsTaken : paid.PowerActionsTaken.Add(move.Action),
            ActionTaken = true,
        };
        next = Receive(next, move.Player, action.Income, PowerSource.Income, events, warnings);
        if (action.Spades > 0)
            next = GainSpades(next with { SpadedHexes = ValueList<int>.Empty }, move.Player, action.Spades, events, warnings);
        if (action.Bridges > 0)
        {
            if (next.BridgesLeft(move.Player) - next[move.Player].BridgesOwed > 0 && BridgeSpots(next, move.Player).Count > 0)
                next = next.WithPlayer(next[move.Player] with { BridgesOwed = next[move.Player].BridgesOwed + action.Bridges });
            else
                warnings.Add(new Issue(Severity.Note, $"The {Name(move.Player)} have no bridge left or nowhere to place one, so the power action is wasted."));
        }
        return MoveResult.Ok(next, events, warnings);
    }
}
