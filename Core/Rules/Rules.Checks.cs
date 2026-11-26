namespace Core;

// The checks most moves begin with. Each returns the refusal, or null when the move may go ahead.
public static partial class Rules
{
    // For anything a player does during a round. Returns a rejection, or null if they may proceed.
    private static MoveResult? CheckTurn(GameState state, Faction player)
    {
        if (state.Phase < Phase.PlacingDwellings)
            return MoveResult.Rejected(state, "The game has not started yet.");
        if (state.Phase == Phase.PlacingDwellings)
            return MoveResult.Rejected(state, "Starting dwellings are still being placed.");
        if (state.Phase == Phase.PickingPassTiles)
            return MoveResult.Rejected(state, "Pass tiles are still being picked.");
        if (state.Phase == Phase.SettingUp)
            return MoveResult.Rejected(state, "The game in progress is still being set up: press Done first.");
        if (state.Phase == Phase.OrderingTurns)
            return MoveResult.Rejected(state, "Click the factions still in the round in turn order first.");
        if (state.Phase == Phase.RoundEndSpades)
            return MoveResult.Rejected(state, "The round has ended: round-end spades are being spent.");
        if (state.Phase == Phase.Finished)
            return MoveResult.Rejected(state, "The game is over.");
        if (player != state.ActivePlayer)
            return MoveResult.Rejected(state, $"It is not the {Name(player)}' turn.");
        return null;
    }

    // Rewards after the action (a favour, a town, the stronghold's held power) wait for the power offers from it: those
    // are part of the action.
    private static MoveResult? CheckOffersAnswered(GameState state) =>
        state.PowerOffers.Count > 0
            ? MoveResult.Rejected(state, $"Waiting for the {Name(state.PowerOffers[0].Player)} to accept or decline power.")
            : null;

    // For the one action per turn: it must be your turn, and you must not have acted yet.
    private static MoveResult? CheckAction(GameState state, Faction player)
    {
        if (CheckTurn(state, player) is { } refusal)
            return refusal;
        // Free play: any number of actions, each once the one before is finished, until the faction passes.
        if (state.IsPlanning && state.HasPassed(player))
            return MoveResult.Rejected(state, $"The {Name(player)} have passed this round.");
        if (state.ActionTaken && state.IsPlanning)
        {
            if (MidAction(state, player, endingTurn: false) is { } unfinished)
                return unfinished;
        }
        else if (state.ActionTaken)
            return MoveResult.Rejected(state, $"The {Name(player)} have already taken their action this turn. Press End turn.");
        // Only between the Chaos Magicians' two actions can these still be waiting.
        if (state.SpadesPending)
            return MoveResult.Rejected(state, $"The {Name(player)} must finish spending their free spades first (or press Done).");
        if (state[player].BridgesOwed > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must place their bridge first.");
        if (state.PowerOffers.Count > 0)
            return MoveResult.Rejected(state, $"Waiting for the {Name(state.PowerOffers[0].Player)} to accept or decline power.");
        // The Riverwalkers decide about priests gained (e.g. as income) before anyone acts.
        if (state.Players.FirstOrDefault(other => other.PriestChoicesOwed > 0) is PlayerState choosing)
            return MoveResult.Rejected(state, $"Waiting for the {Name(choosing.Faction)} to choose what to do with their priest.");
        if (state[player].CultStepsToPay > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must pay their cult steps first.");
        if (state[player].TokenOfferOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must answer the power token offer first.");
        if (state[player].LandscapeOffers != LandscapeOffer.None)
            return MoveResult.Rejected(state, $"The {Name(player)} must take or decline what their {Landscapes.Name(player)} offers first.");
        if (state[player].WitchesTownBonusHex is not null)
            return MoveResult.Rejected(state, $"The {Name(player)} must choose their town bonus first: 5 VP or 3 cult steps.");
        return null;
    }

    // For free actions (conversions, burning): any time during your own turn, before or after your action, but not in
    // the middle of it (e.g. between a transform and the dwelling built there) nor while power offers from it wait. The
    // rewards after the action don't stop them, before, between or after them (MidAction's rewardsAllowed; 
    // as on BGA: 931629591 #359, 4 power to coins between the favour and the town).
    private static MoveResult? CheckFreeAction(GameState state, Faction player)
    {
        if (CheckTurn(state, player) is { } refusal)
            return refusal;
        if (MidAction(state, player, endingTurn: false, rewardsAllowed: true) is { } unfinished)
            return unfinished;
        if (state.Players.FirstOrDefault(other => other.PriestChoicesOwed > 0) is PlayerState choosing)
            return MoveResult.Rejected(state, $"Waiting for the {Name(choosing.Faction)} to choose what to do with their priest.");
        return null;
    }

    // Whether the player is part-way through an action (see MidAction): for the view (the player button's dots).
    public static bool IsMidAction(GameState state, Faction player) => MidAction(state, player, endingTurn: false) is not null;

    // Something of the player's action still to finish (a favour to take, spades to spend or a dwelling to build on the
    // hex just transformed, a bridge, a town, a priest choice, power offers to be answered, ...): a rejection naming it,
    // or null if the action is complete (or none has begun). endingTurn: worded for End turn.
    // rewardsAllowed: the rewards after the action don't count (free actions): a favour still to take, a town still to be
    // formed, the Halflings' stronghold spades (RewardSpades) and the dwelling after them, the Riverwalkers' free bridges
    // (BridgesOptional). Power offers from the action still do: they are part of it.
    private static MoveResult? MidAction(GameState state, Faction player, bool endingTurn, bool rewardsAllowed = false)
    {
        var p = state[player];
        if (!rewardsAllowed && p.FavoursOwed > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must take their favour {(endingTurn ? "before ending their turn" : "first")}.");
        if (state.SpadesPending && !(rewardsAllowed && state.RewardSpades))
            return MoveResult.Rejected(state, $"The {Name(player)} must finish spending their free spades first (or press Done).");
        if (p.BridgesOwed > 0 && !(rewardsAllowed && p.BridgesOptional))
            return MoveResult.Rejected(state, p.BridgesOptional
                ? $"The {Name(player)} must place their bridges first (or skip them)."
                : $"The {Name(player)} must place their bridge first.");
        if (p.PriestChoicesOwed > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must choose what to do with their priest first.");
        if (p.CultStepsToPay > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must pay their cult steps first.");
        if (p.TokenOfferOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must answer the power token offer first.");
        if (p.LandscapeOffers != LandscapeOffer.None)
            return MoveResult.Rejected(state, $"The {Name(player)} must take or decline what their {Landscapes.Name(player)} offers first.");
        if (p.WitchesTownBonusHex is not null)
            return MoveResult.Rejected(state, $"The {Name(player)} must choose their town bonus first: 5 VP or 3 cult steps.");
        if (p.PassTileOwed || p.ExtraPassTileOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must pick a new pass tile from the bank first.");
        if (p.LakeDwellingOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must place their free dwelling on a Lake hex first.");
        if (p.IceMassesOwed is { Count: > 0 })
            return MoveResult.Rejected(state, $"The {Name(player)} must place their free dwellings on the masses of Ice first.");
        if (p.LavaOwed > 0 || p.LavaOwedAny > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must place their lava first.");
        if (p.RockMovesOwed > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must place or remove a glowing rock first (or press Done).");
        if (p.FreeUpgradeOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must take their free shipping or spade step first.");
        if (!rewardsAllowed && FormableTowns(state, player).Count > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must form their town first (place a key token on it).");
        if (state.PowerOffers.Count > 0)
            return MoveResult.Rejected(state, $"Waiting for the {Name(state.PowerOffers[0].Player)} to accept or decline power.");
        if (p.CultStepsOwed > 0)
            return MoveResult.Rejected(state, $"The {Name(player)} must choose a track for their cult step first.");
        if (p.TopStepChoiceOwed)
            return MoveResult.Rejected(state, $"The {Name(player)} must choose which track their key takes to the top first.");
        return null;
    }

    // The piece must still be in its bank, and must be the leftmost one there.
    private static MoveResult? CheckLeftmostInBank(GameState state, ValueList<Piece?> bank, Piece piece, string noun)
    {
        var slot = bank.IndexOf(piece);
        if (slot < 0)
            return MoveResult.Rejected(state, $"That {noun} has already been built.");
        for (var left = 0; left < slot; left++)
            if (bank[left] is not null)
                return MoveResult.Rejected(state, $"Only the leftmost {noun} can be built.");
        return null;
    }

    // Can this faction transform or build on this hex? Checked in the order a player would want to hear it.
    // reach: how the hex is reached (a carpet flight or tunnel is paid for by the caller, PayForReach).
    // allowSkip: whether carpets and tunnels may be used.
    private static MoveResult? CheckHexTarget(GameState state, Faction faction, int hex, bool allowSkip, out Reach reach)
    {
        reach = Reach.None;
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.HexTerrain[hex] == Terrain.River)
            return MoveResult.Rejected(state, "Rivers cannot be built on or transformed.");
        if (state.IsOccupied(hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        // A carpet flight or tunnel only once per action (e.g. two free spades: only one may go on a hex reached so).
        reach = ReachOf(state, faction, hex, allowSkip && !state.SkipUsed);
        if (reach == Reach.None && allowSkip && state.SkipUsed && ReachOf(state, faction, hex, allowSkip: true) is Reach.Carpet or Reach.Tunnel)
            return MoveResult.Rejected(state, faction == Faction.Dwarves
                ? $"The {Name(faction)} have already tunnelled in this action: only one hex per action can be reached that way."
                : $"The {Name(faction)} have already used their carpet flight in this action: only one hex per action can be reached that way.");
        if (reach == Reach.None)
            return MoveResult.Rejected(state, $"That hex is out of range of the {Name(faction)}' buildings.");
        return null;
    }
}
