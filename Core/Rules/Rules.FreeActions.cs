namespace Core;

// Free actions: any time during your own turn, even after your action or passing; not mid-action.
// Conversions and burning, then the choices a faction owes before its turn can end.
public static partial class Rules
{
    // ---------- Conversions ----------

    // Why this player can't make a free action (convert or burn) right now (not their turn, mid-action, ...), or
    // nothing if they can: the same refusal the conversions and BurnPower give before looking at their power.
    public static IReadOnlyList<Issue> FreeActionIssues(GameState state, Faction player) =>
        CheckFreeAction(state, player)?.Issues ?? MoveResult.NoIssues;

    // The power to pay `cost` from bowl 3 with: as it is or, with burnFirst, after burning just enough to have it
    // (`burns` times; 0 without burnFirst). Null if it can't be had.
    // doubleBurn: the Children of the Wyrm, whose burns move 2 tokens on.
    private static Power? PowerToSpend(Power power, int cost, bool burnFirst, bool doubleBurn, out int burns)
    {
        burns = 0;
        if (burnFirst)
        {
            if (power.BurnsFor(cost, doubleBurn) is not int needed)
                return null;
            burns = needed;
        }
        Power burned = power.BurnTimes(burns, doubleBurn);
        return burned.CanSpend(cost) ? burned : null;
    }

    // "1 power in bowl 3", or with burning "1 power in bowl 3, burning included".
    private static string PowerNeeded(int cost, bool burnFirst) =>
        $"{cost} power in bowl 3{(burnFirst ? ", burning included," : "")}";

    // The events of a conversion's payment: the burning, if any.
    private static List<GameEvent> PaymentEvents(Faction player, int burns, Power burned)
    {
        var events = new List<GameEvent>();
        if (burns > 0)
            events.Add(new PowerBurned(player, burned));
        return events;
    }

    private static MoveResult ApplyConvertPowerToCoin(GameState state, ConvertPowerToCoin move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (PowerToSpend(player.Power, Config.PowerPerCoin, move.BurnFirst, move.Player == Faction.ChildrenOfTheWyrm, out int burns) is not Power burned)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {PowerNeeded(Config.PowerPerCoin, move.BurnFirst)} for a coin.");

        var converted = player with
        {
            Power = burned.Spend(Config.PowerPerCoin),
            Coins = player.Coins + ConversionGain(state, move.Player),
        };
        var events = PaymentEvents(move.Player, burns, burned);
        events.Add(new CoinGained(move.Player, converted.Coins));
        return MoveResult.Ok(state.WithPlayer(converted), events.ToArray());
    }

    private static MoveResult ApplyConvertPowerToWorker(GameState state, ConvertPowerToWorker move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (PowerToSpend(player.Power, Config.PowerPerWorker, move.BurnFirst, move.Player == Faction.ChildrenOfTheWyrm, out int burns) is not Power burned)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {PowerNeeded(Config.PowerPerWorker, move.BurnFirst)} for a worker.");

        var converted = player with
        {
            Power = burned.Spend(Config.PowerPerWorker),
            Workers = player.Workers + ConversionGain(state, move.Player),
        };
        var events = PaymentEvents(move.Player, burns, burned);
        return MoveResult.Ok(state.WithPlayer(converted), events.ToArray());
    }

    private static MoveResult ApplyConvertPriestToWorker(GameState state, ConvertPriestToWorker move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (player.TopPriest is not Piece priest)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no priest ready to convert.");

        // The priest goes back to the front of the supply, so it is the next one gained.
        var dynion = move.Player == Faction.DynionGeifr; // 2 workers and 2 coins
        var converted = player with
        {
            Priests = player.Priests.RemoveAt(player.Priests.Count - 1),
            PriestSupply = player.PriestSupply.Insert(0, priest),
            Workers = player.Workers + (dynion ? Config.DynionGeifrPriestWorkers : 1),
            Coins = player.Coins + (dynion ? Config.DynionGeifrPriestCoins : 0),
        };
        return dynion
            ? MoveResult.Ok(state.WithPlayer(converted), new CoinGained(move.Player, converted.Coins))
            : MoveResult.Ok(state.WithPlayer(converted));
    }

    private static MoveResult ApplyConvertWorkerToCoin(GameState state, ConvertWorkerToCoin move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        var converted = player with { Workers = player.Workers - 1, Coins = player.Coins + 1 };
        var events = new GameEvent[] { new CoinGained(move.Player, converted.Coins) };
        return MoveResult.Ok(state.WithPlayer(converted), events, DebtWarning(converted)); // may go into debt
    }

    private static MoveResult ApplyConvertPowerToPriest(GameState state, ConvertPowerToPriest move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (PowerToSpend(player.Power, Config.PowerPerPriest, move.BurnFirst, move.Player == Faction.ChildrenOfTheWyrm, out int burns) is not Power burned)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {PowerNeeded(Config.PowerPerPriest, move.BurnFirst)} for a priest.");

        var paid = player with { Power = burned.Spend(Config.PowerPerPriest) };
        var events = PaymentEvents(move.Player, burns, burned);
        var warnings = new List<Issue>();
        var next = state.WithPlayer(paid);
        for (var i = 0; i < ConversionGain(state, move.Player); i++)
            next = GainPriest(next, move.Player, events, warnings); // may warn: supply empty
        return MoveResult.Ok(next, events, warnings);
    }

    private static MoveResult ApplyBurnPower(GameState state, BurnPower move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (!player.Power.CanBurn(1))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need at least 2 power in bowl 2 to burn.");

        var burned = player with { Power = player.Power.BurnOnce(move.Player == Faction.ChildrenOfTheWyrm) };
        return MoveResult.Ok(state.WithPlayer(burned), new PowerBurned(move.Player, burned.Power));
    }

    // Alchemists: 1 victory point becomes 1 coin (going below 0 VP is allowed, with a warning).
    private static MoveResult ApplyConvertVictoryPointToCoin(GameState state, ConvertVictoryPointToCoin move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Alchemists)
            return MoveResult.Rejected(state, "Only the Alchemists can turn victory points into coins.");

        // The VP lost is logged (VictoryPointsGained, no source), so the statistics count it under the faction's ability.
        var events = new List<GameEvent>();
        var next = GainVictoryPoints(state, move.Player, -1, events);
        var converted = next[move.Player] with { Coins = next[move.Player].Coins + 1 };
        events.Add(new CoinGained(move.Player, converted.Coins));
        var warnings = converted.VictoryPoints < 0
            ? new[] { new Issue(Severity.Warning, $"The {Name(move.Player)} now have {converted.VictoryPoints} victory points.") }
            : MoveResult.NoIssues;
        return MoveResult.Ok(next.WithPlayer(converted), events, warnings);
    }

    // Alchemists: 2 coins become 1 victory point.
    private static MoveResult ApplyConvertCoinsToVictoryPoint(GameState state, ConvertCoinsToVictoryPoint move)
    {
        if (CheckFreeAction(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Alchemists)
            return MoveResult.Rejected(state, "Only the Alchemists can turn coins into victory points.");
        var player = state[move.Player];
        if (player.Coins < Config.AlchemistsCoinsPerVictoryPoint)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {Config.AlchemistsCoinsPerVictoryPoint} coins for a victory point.");

        var events = new List<GameEvent>();
        var next = state.WithPlayer(player with { Coins = player.Coins - Config.AlchemistsCoinsPerVictoryPoint });
        next = GainVictoryPoints(next, move.Player, 1, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Choices owed ----------

    // Riverwalkers, for a priest gained: take it from the supply, or instead move the priest on that terrain of their
    // wheel to the supply, unlocking the terrain (coins, debt allowed). Answered whoever's turn it is.
    private static MoveResult ApplyChoosePriest(GameState state, ChoosePriest move)
    {
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        var player = state[move.Player];
        if (player.PriestChoicesOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no priest to choose about.");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state.WithPlayer(player with { PriestChoicesOwed = player.PriestChoicesOwed - 1 });
        if (move.Unlock is not Terrain terrain)
            return MoveResult.Ok(TakePriestFromSupply(next, move.Player, events, warnings), events, warnings);

        var index = player.WheelPriests.ToList().FindIndex(locked => locked.Terrain == terrain);
        if (index < 0)
            return MoveResult.Rejected(state, $"{terrain} is not locked on the {Name(move.Player)}' wheel.");
        var wheelPriest = player.WheelPriests[index];
        var coins = UnlockCost(state, move.Player, terrain);
        var unlocked = next[move.Player] with
        {
            WheelPriests = player.WheelPriests.RemoveAt(index),
            PriestSupply = player.PriestSupply.Add(wheelPriest.Priest),
            Coins = player.Coins - coins,
        };
        warnings.AddRange(DebtWarning(unlocked));
        return MoveResult.Ok(next.WithPlayer(unlocked), events, warnings);
    }

    // Darklings: no more exchanges this turn (those left are given up).
    private static MoveResult ApplyFinishPriestExchange(GameState state, FinishPriestExchange move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.PriestExchangesLeft == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no workers to exchange for priests now.");
        var next = state.WithPlayer(player with { PriestExchangesLeft = 0 });
        return MoveResult.Ok(next);
    }

    // Darklings, the turn they build their stronghold: workers become priests (each from the supply), up to 3 in all.
    private static MoveResult ApplyExchangeWorkersForPriests(GameState state, ExchangeWorkersForPriests move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.PriestExchangesLeft == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no workers to exchange for priests now.");
        if (move.Workers < 1 || move.Workers > player.PriestExchangesLeft)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} can exchange 1 to {player.PriestExchangesLeft} more worker{(player.PriestExchangesLeft == 1 ? "" : "s")} for priests.");

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state.WithPlayer(player with
        {
            Workers = player.Workers - move.Workers,
            PriestExchangesLeft = player.PriestExchangesLeft - move.Workers,
        });
        for (var i = 0; i < move.Workers; i++)
            next = GainPriest(next, move.Player, events, warnings);
        warnings.AddRange(DebtWarning(next[move.Player]));
        return MoveResult.Ok(next, events, warnings);
    }
}
