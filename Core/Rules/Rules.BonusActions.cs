namespace Core;

// Bonus actions (the octagons): each is the turn's action, once per round. Unlocked by a stronghold, a pass tile or a
// favour (BonusActionId). The fan factions' stronghold actions are in Rules.FanFactions.cs.
public static partial class Rules
{
    // One step (two for the Auren's stronghold) up the chosen track.
    private static MoveResult ApplyBonusCultStep(GameState state, BonusCultStep move)
    {
        if (CheckAction(state, move.Player) is { } actionRefusal)
            return actionRefusal;
        var bonus = state[move.Player].BonusActions
            .Where(unlocked => unlocked.Source == move.Source && unlocked.Action is BonusAction.CultStep or BonusAction.TwoCultSteps)
            .Select(unlocked => (BonusActionId?)unlocked)
            .FirstOrDefault() ?? new BonusActionId(move.Source, BonusAction.CultStep);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;

        var steps = bonus.Action == BonusAction.TwoCultSteps ? 2 : 1;
        var before = state[move.Player].TrackSteps[(int)move.Track];
        var events = new List<GameEvent>();
        var next = ClimbTrack(state with { ActionTaken = true }, move.Player, move.Track, steps, events, out var after);
        var warnings = after < before + steps
            ? new[] { new Issue(Severity.Note, $"The {Name(move.Player)} cannot go further up {move.Track} (the top step needs a key, and only one player may be on it), so {(after == before ? "the step is" : "a step is")} lost.") }
            : MoveResult.NoIssues;
        events.Insert(0, new TrackAdvanced(move.Player, move.Track, before, after));
        next = TrackRewards(next, move.Player, before, after, events);
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, warnings);
    }

    // The bonus action gives its free spades, which must then be spent at once (SpendSpades), before
    // anything else but free actions. It is the turn's action.
    private static MoveResult ApplyBonusSpades(GameState state, BonusSpades move)
    {
        if (CheckAction(state, move.Player) is { } actionRefusal)
            return actionRefusal;
        var unlocked = state[move.Player].BonusActions
            .Where(bonus => bonus.Source == move.Source && BonusActions.Spades(bonus.Action) > 0)
            .Select(bonus => (BonusActionId?)bonus)
            .FirstOrDefault();
        if (unlocked is not BonusActionId spadeBonus)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no spade bonus action from their {move.Source}.");
        if (CheckBonusAction(state, move.Player, spadeBonus) is { } refusal)
            return refusal;

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = GainSpades(state with { ActionTaken = true, SpadedHexes = ValueList<int>.Empty }, move.Player,
            BonusActions.Spades(spadeBonus.Action), events, warnings);
        if (spadeBonus.Action == BonusAction.SelkiesSpade) // shipping +1 until these spades are done (FinishSpading)
            next = next with { ShippingBoost = true };
        next = MarkBonusActionUsed(next, move.Player, spadeBonus);
        return MoveResult.Ok(next, events, warnings);
    }

    // A free dwelling on any empty hex of home terrain: range does not matter.
    private static MoveResult ApplyBonusBuild(GameState state, BonusBuild move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.BuildOnHomeTerrain);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        if (state[move.Player].NextBuilding(PieceKind.Dwelling) is not Piece dwelling)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no dwellings left to build.");
        if (!state.Layout.Contains(move.Hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.IsOccupied(move.Hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        if (!state[move.Player].CanBuildOn(state.HexTerrain[move.Hex]))
            return MoveResult.Rejected(state, $"This bonus action builds only on {state[move.Player].BuildTerrain}.");

        var events = new List<GameEvent>();
        var next = PlaceDwelling(state, dwelling, move.Hex, default, events);
        next = MarkBonusActionUsed(next, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // A free upgrade to a trading post: everything else is as for a normal upgrade.
    private static MoveResult ApplyBonusTradingPost(GameState state, BonusTradingPost move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.FreeTradingPost);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        if (state[move.Player].NextBuilding(PieceKind.TradingPost) is not Piece tradingPost)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no trading posts left.");
        return Upgrade(state, tradingPost, move.Hex, bonus);
    }

    // Chaos Magicians: this is the turn's action, and two more follow (see Apply).
    private static MoveResult ApplyBonusDoubleAction(GameState state, BonusDoubleAction move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.DoubleAction);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        var events = new List<GameEvent>();
        // In Free play, where actions are not counted, the octagon is only marked used.
        var next = state.IsPlanning
            ? MarkBonusActionUsed(state with { ActionTaken = true }, move.Player, bonus)
            : MarkBonusActionUsed(state with { ActionsOwed = 2 }, move.Player, bonus);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Nomads: a hex next to one of their buildings (their bridges count; shipping does not) becomes Desert. No
    // spades are spent, so nothing scores for spades. A dwelling may then be built there, paid (BuildOnSpadedHex),
    // or Done gives that up.
    private static MoveResult ApplyBonusSandstorm(GameState state, BonusSandstorm move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.Sandstorm);
        if (CheckBonusAction(state, move.Player, bonus) is { } refusal)
            return refusal;
        if (!state.Layout.Contains(move.Hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.HexTerrain[move.Hex] == Terrain.River)
            return MoveResult.Rejected(state, "Rivers cannot be built on or transformed.");
        if (state.IsOccupied(move.Hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        var home = state[move.Player].Home!.Value;
        if (CheckTransformTo(state, move.Player, move.Hex, home) is { } terrainRefusal)
            return terrainRefusal;
        if (!state.HexesBuiltOnBy(move.Player).Any(built => AdjacentHexes(state, move.Player, built).Contains(move.Hex)))
            return MoveResult.Rejected(state, $"The sandstorm only reaches hexes next to the {Name(move.Player)}' buildings.");

        var events = new List<GameEvent>();
        var next = ChangeTerrain(state, move.Hex, home, events) with { ActionTaken = true, SpadedHexes = ValueList<int>.Empty };
        next = MarkBonusActionUsed(next, move.Player, bonus);
        var buildable = next with { SpadedHexes = ValueList.Of(move.Hex) };
        if (SpadingOpen(buildable, move.Player))
            next = buildable;
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Shapeshifters' stronghold: their home terrain moves to another basic terrain that is nobody's home, for 5 power
    // (burning first if needed) or 5 power tokens removed for the game (lowest bowls first). The turn's action, as often
    // as they like once unlocked: not once a round (as on BGA).
    private static MoveResult ApplyBonusShapeshift(GameState state, BonusShapeshift move)
    {
        var bonus = new BonusActionId(move.Source, BonusAction.Shapeshift);
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (!state[move.Player].HasUnlocked(bonus))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have not unlocked that bonus action.");
        if (!Terrains.IsBasic(move.To))
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' home must be one of the seven basic terrains, not {move.To}.");
        if (state.Players.FirstOrDefault(player => player.Home == move.To) is PlayerState owner)
            return MoveResult.Rejected(state, owner.Faction == move.Player
                ? $"{move.To} already is the {Name(move.Player)}' home terrain."
                : $"{move.To} is the {Name(owner.Faction)}' home terrain.");

        var events = new List<GameEvent>();
        GameState next;
        if (move.PayWithTokens)
        {
            if (state[move.Player].Power.Total < Config.ShapeshiftCost)
                return MoveResult.Rejected(state, $"The {Name(move.Player)} need {Config.ShapeshiftCost} power tokens to remove.");
            next = RemoveTokens(state, move.Player, Config.ShapeshiftCost, events);
        }
        else if (BurnAndSpend(state, move.Player, Config.ShapeshiftCost, events) is GameState paid)
            next = paid;
        else
            return MoveResult.Rejected(state, $"The {Name(move.Player)} need {Config.ShapeshiftCost} power in bowl 3 (burning included).");

        next = next.WithPlayer(next[move.Player] with { ShapeshifterHome = move.To }) with { ActionTaken = true };
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // For a bonus action: it must be the player's turn and their action not yet taken (it IS the action),
    // and the bonus action must be unlocked and not used yet this round.
    private static MoveResult? CheckBonusAction(GameState state, Faction player, BonusActionId bonus)
    {
        if (CheckAction(state, player) is { } refusal)
            return refusal;
        if (!state[player].HasUnlocked(bonus))
            return MoveResult.Rejected(state, $"The {Name(player)} have not unlocked that bonus action.");
        if (state[player].BonusActionsUsed.Contains(bonus))
            return MoveResult.Rejected(state, $"The {Name(player)} have already used that bonus action this round.");
        return null;
    }

    private static GameState MarkBonusActionUsed(GameState state, Faction faction, BonusActionId bonus)
    {
        var player = state[faction];
        return state.WithPlayer(player with { BonusActionsUsed = player.BonusActionsUsed.Add(bonus) });
    }
}
