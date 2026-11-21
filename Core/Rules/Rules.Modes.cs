namespace Core;

// Play modes (Strict, Lenient, Free), Free play during a game, and the turn-order step that follows setting up a game
// in progress (and leaving Free once a round has been advanced in it).
public static partial class Rules
{
    // Repeated edits of one value by one faction share a key, and merge in History into one record and one undo step;
    // null for every other move.
    public static string? MergeKey(Move move) => move switch
    {
        EditResource m => $"{m.Player}:{m.Resource}",
        EditPowerToken m => $"{m.Player}:power",
        EditGainPriest m => $"{m.Player}:priests",
        EditSafe m => $"{m.Player}:safe contents",
        CalculatorGain m => $"{m.Player}:calculator {m.Kind}",
        TakePassTileCoin m => $"pass tile {m.Tile}:coin taken",
        ChooseForSafe m => $"{m.Player}:safe",
        StoreInTreasury m => $"{m.Player}:safe", // settling joins the choices before it
        EditTrack m => $"{m.Player}:{m.Track}",
        EditLevel m => $"{m.Player}:{m.Level}",
        EditPassTileCoins m => $"pass tile {m.Tile}:coins",
        CancelPriestDebt m => $"{m.Player}:priest debt",
        AdjustStartingVictoryPoints m => $"{m.Player}:starting VP",
        _ => null,
    };

    public static string ModeName(PlayMode mode) => mode switch
    {
        PlayMode.Strict => "Strict",
        PlayMode.Lenient => "Lenient",
        _ => "Free",
    };

    private static MoveResult ApplySetMode(GameState state, SetMode move)
    {
        if (state.Phase == Phase.ChoosingGame)
            return MoveResult.Rejected(state, "Choose the game first: the mode to start in is one of its options.");
        if (state.Options.Calculator)
            return MoveResult.Rejected(state, "Calculator plays in Free mode throughout.");
        if (move.Mode == state.Mode)
            return MoveResult.Rejected(state, $"The game is already in {ModeName(move.Mode)} mode.");
        if (move.Mode == PlayMode.Free && state.Phase == Phase.SettingUp)
            return MoveResult.Rejected(state, "Free is not available while a game in progress is set up: choose Strict or Lenient for afterwards.");
        if (move.Mode == PlayMode.Free && state.Phase == Phase.OrderingTurns)
            return MoveResult.Rejected(state, "Finish the turn order first.");
        if (move.Mode == PlayMode.Strict && state.Phase != Phase.SettingUp && state.AnyoneInDebt)
            return MoveResult.Rejected(state, $"Strict can't begin while {DebtText(state, would: false)}");

        var events = new List<GameEvent>();
        var next = state with { Mode = move.Mode };
        if (state.Planning is FreePlan plan)
            return LeaveFree(state, next, plan, events);
        // Free begins at once during the rounds; before round 1, once it begins (BeginRound).
        if (move.Mode == PlayMode.Free && state.Phase is Phase.Playing or Phase.RoundEndSpades)
        {
            var active = state.ActivePlayer;
            next = next with { Planning = new FreePlan(active, state.ActionTaken, state.ActionsOwed, state.Round, ValueList<Faction>.Empty, active) };
            events.Add(new PlanningStarted(active));
        }
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Out of Free: the selected faction's action must be finished, and every check of FinishSetup must pass. Then, if a
    // round was advanced in Free, the turn-order step; otherwise the faction whose turn it was on entering has it back,
    // with its action taken if it acted in Free.
    private static MoveResult LeaveFree(GameState state, GameState next, FreePlan plan, List<GameEvent> events)
    {
        next = next with { Planning = null };
        if (state.Phase == Phase.Finished)
        {
            return MoveResult.Ok(next, events, MoveResult.NoIssues);
        }
        if (state.Phase == Phase.RoundEndSpades)
            return MoveResult.Rejected(state, "Finish spending the round-end spades first.");
        if (state.ActivePlayer is Faction selected && MidAction(state, selected, endingTurn: false) is { } unfinished)
            return unfinished;
        var issues = SetupIssues(next);
        if (issues.Count > 0)
            return new MoveResult(state, Array.Empty<GameEvent>(), issues.Select(issue => new Issue(Severity.Error, issue)).ToList());

        if (state.Round > plan.EntryRound)
        {
            return MoveResult.Ok(BeginOrdering(next, events, out var warnings), events, warnings);
        }
        var entry = plan.EntryPlayer ?? state.ActivePlayer!.Value;
        var acted = plan.Acted.Contains(entry);
        next = next with
        {
            ActivePlayer = entry,
            ActionTaken = plan.EntryActionTaken || acted,
            ActionsOwed = acted ? 0 : plan.EntryActionsOwed,
            SkipUsed = false,
        };
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Setup: the faction the tools are for. Free: the faction that acts, starting afresh (as if its turn began; the one
    // before must have finished its action, and gives up what a turn's end gives up).
    private static MoveResult ApplySelectFaction(GameState state, SelectFaction move)
    {
        if (!state.IsInGame(move.Faction))
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are not in the game.");
        if (state.Phase != Phase.SettingUp && !state.IsPlanning)
            return MoveResult.Rejected(state, "A faction is selected only while setting up a game in progress, or in Free play.");
        if (state.ActivePlayer == move.Faction)
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are already selected.");
        var events = new List<GameEvent>();
        if (state.Phase == Phase.SettingUp)
            return MoveResult.Ok(state with { ActivePlayer = move.Faction }, events, MoveResult.NoIssues);
        if (state.Phase != Phase.Playing)
            return MoveResult.Rejected(state, "Finish spending the round-end spades first.");

        var next = state;
        if (state.ActivePlayer is Faction current)
        {
            if (MidAction(state, current, endingTurn: false) is { } unfinished)
                return unfinished;
            next = next.WithPlayer(next[current] with { PriestExchangesLeft = 0 });
            next = ReturnPriestsFromReturningSlots(next, events);
        }
        next = next with
        {
            ActivePlayer = move.Faction,
            ActionTaken = false,
            ActionsOwed = 0,
            SkipUsed = false,
            Planning = next.Planning! with { Selected = move.Faction },
        };
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Free play: the faction stays selected; once everyone has passed, the round ends.
    private static MoveResult EndFreeTurn(GameState state, Faction player)
    {
        if (MidAction(state, player, endingTurn: true) is { } unfinished)
            return unfinished;
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        state = ReleaseHeldPower(state, player, events);
        var ended = state.WithPlayer(state[player] with { PriestExchangesLeft = 0 }) with { ActionTaken = false, ActionsOwed = 0 };
        var next = ReturnPriestsFromReturningSlots(ended, events);
        if (next.PassOrder.Count == next.Players.Count)
            next = EndRound(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    private static MoveResult ApplyCancelPriestDebt(GameState state, CancelPriestDebt move)
    {
        if (state.Phase != Phase.SettingUp && !state.IsPlanning)
            return MoveResult.Rejected(state, "A priest owed is cancelled only while setting up a game in progress, or in Free play.");
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        var player = state[move.Player];
        if (player.PriestDebt == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} owe no priest.");
        return MoveResult.Ok(state.WithPlayer(player with { PriestDebt = player.PriestDebt - 1 }),
            new PriestDebtPaid(move.Player, player.PriestDebt - 1));
    }

    // ---------- The turn-order step ----------

    // After Setup's Done, or leaving Free once a round was advanced: the factions still in the round are clicked into
    // turn order. If everyone has passed, there is nothing to order: the round ends.
    private static GameState BeginOrdering(GameState state, List<GameEvent> events, out List<Issue> warnings)
    {
        warnings = new List<Issue>();
        var next = state with { ActivePlayer = null, ActionTaken = false, ActionsOwed = 0, SkipUsed = false, SpadedHexes = ValueList<int>.Empty };
        if (next.PassOrder.Count == next.Players.Count)
            return EndRound(next with { Phase = Phase.Playing }, events, warnings);
        // Calculator: the one faction plays on at once, in Free.
        if (next.Options.Calculator)
        {
            var only = next.Players[0].Faction;
            return BeginPlanningIfFree(next with { Phase = Phase.Playing, ActivePlayer = only, TurnOrder = ValueList.Of(only) }, events);
        }
        events.Add(new TurnOrderStepBegan());
        return next with { Phase = Phase.OrderingTurns, StartOrder = ValueList<Faction>.Empty };
    }

    // Play goes on in Free mode with no Free play under way yet (after Setup in Free, i.e. Calculator): it begins now,
    // from the active player's turn.
    private static GameState BeginPlanningIfFree(GameState state, List<GameEvent> events)
    {
        if (state.Mode != PlayMode.Free || state.Planning is not null || state.Phase != Phase.Playing)
            return state;
        events.Add(new PlanningStarted(state.ActivePlayer));
        return state with { Planning = new FreePlan(state.ActivePlayer, state.ActionTaken, state.ActionsOwed, state.Round, ValueList<Faction>.Empty, state.ActivePlayer) };
    }

    // The faction takes the next place among those still in the round. With the last of them, play goes on: the first
    // clicked begins their turn, the passed factions after them in the order they passed.
    private static MoveResult OrderTurn(GameState state, Faction faction)
    {
        if (!state.IsInGame(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} are not in the game.");
        if (state.HasPassed(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} have passed: they keep their place in the pass order.");
        var order = state.StartOrder ?? ValueList<Faction>.Empty;
        if (order.Contains(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} already have a place in the turn order.");
        order = order.Add(faction);
        var events = new List<GameEvent> { new TurnOrderChosen(faction, order.Count) };
        if (order.Count < state.Players.Count - state.PassOrder.Count)
            return MoveResult.Ok(state with { StartOrder = order }, events, MoveResult.NoIssues);

        var next = state with
        {
            Phase = Phase.Playing,
            TurnOrder = new ValueList<Faction>(order.Concat(state.PassOrder)),
            StartOrder = null,
            ActivePlayer = order[0],
            ActionTaken = false,
        };
        return MoveResult.Ok(BeginPlanningIfFree(next, events), events, MoveResult.NoIssues);
    }

    // That faction loses its place, and so does every faction clicked after it.
    private static MoveResult UnorderTurn(GameState state, Faction faction)
    {
        var order = state.StartOrder ?? ValueList<Faction>.Empty;
        var position = order.IndexOf(faction);
        if (position < 0)
            return MoveResult.Rejected(state, $"The {Name(faction)} do not have a place in the turn order yet.");
        return MoveResult.Ok(state with { StartOrder = new ValueList<Faction>(order.Take(position)) });
    }
}
