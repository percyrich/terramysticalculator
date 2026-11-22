namespace Core;

// Priests, the four tracks, and favours.
public static partial class Rules
{
    // ---------- Priests ----------

    // The next priest in the supply becomes ready to place. With the supply empty, nothing is gained
    // and a warning says so (the move still goes ahead).
    // Riverwalkers with priests still on their wheel choose instead, each time (ChoosePriest): the priest, or a terrain.
    private static GameState GainPriest(GameState state, Faction faction, List<GameEvent> events, List<Issue> warnings)
    {
        var owner = state[faction];
        if (faction == Faction.Riverwalkers && owner.WheelPriests.Count > 0)
            return state.WithPlayer(owner with { PriestChoicesOwed = owner.PriestChoicesOwed + 1 });
        return TakePriestFromSupply(state, faction, events, warnings);
    }

    private static GameState TakePriestFromSupply(GameState state, Faction faction, List<GameEvent> events, List<Issue> warnings)
    {
        var player = state[faction];
        // A priest owed is paid back first, by the priest gained, whether or not the supply has one.
        if (player.PriestDebt > 0)
        {
            events.Add(new PriestDebtPaid(faction, player.PriestDebt - 1));
            return state.WithPlayer(player with { PriestDebt = player.PriestDebt - 1 });
        }
        if (player.PriestSupply.Count == 0)
        {
            warnings.Add(new Issue(Severity.Note, $"The {Name(faction)} have no priests left in their supply, so gain none."));
            return state;
        }

        var priest = player.PriestSupply[0];
        events.Add(new PriestGained(priest));
        return state.WithPlayer(player with
        {
            PriestSupply = player.PriestSupply.RemoveAt(0),
            Priests = player.Priests.Add(priest),
        });
    }

    // A priest paid: the top ready priest goes back to the front of the supply. With none ready, the faction owes it
    // (PriestDebt, a debt: DebtWarning); the next priest gained pays it back.
    private static GameState PayPriest(GameState state, Faction faction, List<GameEvent> events, List<Issue> warnings)
    {
        var player = state[faction];
        if (player.TopPriest is not Piece priest)
        {
            events.Add(new PriestDebtIncurred(faction, player.PriestDebt + 1));
            return state.WithPlayer(player with { PriestDebt = player.PriestDebt + 1 });
        }
        return state.WithPlayer(player with
        {
            Priests = player.Priests.RemoveAt(player.Priests.Count - 1),
            PriestSupply = player.PriestSupply.Insert(0, priest),
        });
    }

    // How many steps a priest sent to a track in this slot moves this faction: the slot's number, 1 more for the
    // Acolytes with their stronghold.
    public static int PriestSlotSteps(GameState state, Faction faction, int slot) =>
        Config.PriestSlots[slot].Steps
        + (faction == Faction.Acolytes && state.IsInGame(faction) && state[faction].StrongholdBuilt ? Config.AcolytesPriestBonusSteps : 0);

    private static MoveResult ApplyPlacePriest(GameState state, PlacePriest move)
    {
        var priest = move.Priest;
        if (priest.Kind != PieceKind.Priest)
            return MoveResult.Rejected(state, "That piece is not a priest.");
        if (!state.IsInGame(priest.Owner))
            return MoveResult.Rejected(state, $"The {Name(priest.Owner)} are not in the game.");

        var player = state[priest.Owner];
        if (player.PriestSupply.Contains(priest))
            return MoveResult.Rejected(state, $"That priest is still in the {Name(priest.Owner)}' supply. A priest must be gained before it can be placed.");
        if (CheckAction(state, priest.Owner) is { } refusal)
            return refusal;
        if (!player.Priests.Contains(priest))
            return MoveResult.Rejected(state, "That priest is not ready to place.");
        if (priest != player.TopPriest)
            return MoveResult.Rejected(state, "Only the top priest can be placed.");
        if (move.Slot < 0 || move.Slot >= Config.PriestSlotsPerZone)
            return MoveResult.Rejected(state, "There is no such slot.");

        var track = (int)move.Track;
        var zone = state.PriestZones[track];
        if (state.IsSlotTaken(move.Track, move.Slot))
            return MoveResult.Rejected(state, "That slot is already taken.");

        var before = player.TrackSteps[track];
        var placed = player with { Priests = player.Priests.RemoveAt(player.Priests.Count - 1) };
        var next = state.WithPlayer(placed) with
        {
            PriestZones = state.PriestZones.SetItem(track, zone.SetItem(move.Slot, priest)),
            ActionTaken = true,
        };

        var events = new List<GameEvent>();
        next = ClimbTrack(next, priest.Owner, move.Track, PriestSlotSteps(state, priest.Owner, move.Slot), events, out var after);
        events.Insert(0, new PriestPlaced(priest, move.Track, move.Slot, before, after));
        next = TrackRewards(next, priest.Owner, before, after, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Priests in returning slots go back to the front of their owners' supplies.
    private static GameState ReturnPriestsFromReturningSlots(GameState state, List<GameEvent> events)
    {
        foreach (var track in Enum.GetValues<Track>())
        {
            for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
            {
                if (!Config.PriestSlots[slot].ReturnsAtEndOfTurn)
                    continue;
                if (state.PriestZones[(int)track][slot] is not Piece priest)
                    continue;

                var owner = state[priest.Owner];
                state = state.WithPlayer(owner with { PriestSupply = owner.PriestSupply.Insert(0, priest) });
                state = state with
                {
                    PriestZones = state.PriestZones.SetItem((int)track, state.PriestZones[(int)track].SetItem(slot, null)),
                };
            }
        }
        return state;
    }

    // ---------- Tracks ----------

    // Moves the player `steps` up a track (steps never go down). The top step needs a key, which is
    // spent, and only one player may be on it; without both, the climb stops one short (TM).
    // Steps beyond the top are lost. Track power rewards are separate (TrackRewards).
    // Without a key, in the player's own action, with a town still to be formed: the key that town brings is used at
    // once (KeysBorrowed; Andrew 2026-11-22, as BGA: "is founding a Town this turn and can use the key immediately",
    // 854471936 #404), so the top step's power comes now. Not during a town's own rewards (mayBorrow false): one town
    // is finished before the next begins.
    private static GameState ClimbTrack(GameState state, Faction faction, Track track, int steps, List<GameEvent> events, out int after,
        bool mayBorrow = true)
    {
        var player = state[faction];
        var before = player.TrackSteps[(int)track];
        after = Math.Min(Config.TrackMax, before + steps);
        var spendsKey = false;
        var borrows = false;
        var held = player.TopStepsHeld;
        if (after == Config.TrackMax && before < Config.TrackMax)
        {
            var topTaken = state.Players.Any(other => other.Faction != faction && other.TrackSteps[(int)track] == Config.TrackMax);
            var ownAction = state.Phase == Phase.Playing && state.ActivePlayer == faction;
            if (!topTaken && player.Keys == 0 && mayBorrow && ownAction && FormableTowns(state, faction).Count > player.KeysBorrowed)
                borrows = true;
            else if (topTaken || player.Keys == 0)
            {
                after = Math.Max(before, Config.TrackMax - 1);
                // Without a key and no town to form yet, in the player's own action: the step onto 10 waits in case a
                // town later in the action brings one (the designer's ruling: a step may be taken after the town it
                // helped to form), e.g. the Acolytes' steps for a spade before the dwelling that makes the town.
                if (!topTaken && ownAction && state.ActionTaken && (held is null || !held.Value.Contains(track)))
                    held = (held ?? ValueList<Track>.Empty).Add(track);
            }
            else
                spendsKey = true;
        }
        if (spendsKey || borrows)
            events.Add(new KeySpent(faction, track));
        var next = state.WithPlayer(player with
        {
            TrackSteps = player.TrackSteps.SetItem((int)track, after),
            Keys = player.Keys - (spendsKey ? 1 : 0),
            KeysBorrowed = player.KeysBorrowed + (borrows ? 1 : 0),
            TopStepsHeld = held,
        });
        // A faction that moves arrives last on its new step.
        if (after != before)
        {
            var order = next.TrackArrivals[(int)track];
            order = order.RemoveAt(order.IndexOf(faction)).Add(faction);
            next = next with { TrackArrivals = next.TrackArrivals.SetItem((int)track, order) };
        }
        return next;
    }

    // The steps onto 10 held this action for want of a key (TopStepsHeld), taken now that a town has brought one: one
    // key each, in the order held; those still without a key keep waiting.
    // With fewer keys than such tracks the player chooses instead (TopStepChoiceOwed, then TakeTopStep).
    private static GameState ReleaseHeldTopSteps(GameState state, Faction faction, List<GameEvent> events)
    {
        if (state[faction].TopStepsHeld is not { Count: > 0 } held)
            return state;
        var keys = state[faction].Keys;
        if (keys > 0 && held.Count(track => TopStepOpen(state, faction, track)) > keys)
            return state.WithPlayer(state[faction] with { TopStepChoiceOwed = true });
        var next = state.WithPlayer(state[faction] with { TopStepsHeld = null });
        var waiting = new List<Track>();
        foreach (var track in held)
        {
            var before = next[faction].TrackSteps[(int)track];
            if (before != Config.TrackMax - 1)
                continue;
            if (next[faction].Keys == 0)
            {
                waiting.Add(track);
                continue;
            }
            next = ClimbTrack(next, faction, track, 1, events, out var after);
            events.Add(new TrackAdvanced(faction, track, before, after));
            next = TrackRewards(next, faction, before, after, events);
        }
        return waiting.Count == 0 ? next : next.WithPlayer(next[faction] with { TopStepsHeld = new ValueList<Track>(waiting) });
    }

    // When the action ends (the turn moves on, a new action begins, the phase changes), held steps are dropped.
    private static GameState DropHeldTopSteps(GameState before, GameState after)
    {
        if (!after.Players.Any(p => p.TopStepsHeld is not null))
            return after;
        if (after.Phase == Phase.Playing && after.ActivePlayer == before.ActivePlayer && !(before.ActionTaken && !after.ActionTaken))
            return after;
        foreach (var player in after.Players.Where(p => p.TopStepsHeld is not null).ToList())
            after = after.WithPlayer(player with { TopStepsHeld = null, TopStepChoiceOwed = false });
        return after;
    }

    // One step from 9 onto the top of this track is open to the faction: they are on 9 and nobody else is on 10.
    private static bool TopStepOpen(GameState state, Faction faction, Track track) =>
        state[faction].TrackSteps[(int)track] == Config.TrackMax - 1
        && !state.Players.Any(other => other.Faction != faction && other.TrackSteps[(int)track] == Config.TrackMax);

    // The key goes on this track: the step onto 10 held there is taken. The steps still held then wait for the keys
    // left (another choice if those are still too few).
    private static MoveResult ApplyTakeTopStep(GameState state, TakeTopStep move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (!player.TopStepChoiceOwed || player.TopStepsHeld is not { } held || player.Keys == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no key to place on a track.");
        if (!held.Contains(move.Track) || !TopStepOpen(state, move.Player, move.Track))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} cannot take their key to the top of {move.Track}: choose "
                + string.Join(" or ", held.Where(track => TopStepOpen(state, move.Player, track))) + ".");

        var events = new List<GameEvent>();
        var rest = held.RemoveAt(held.IndexOf(move.Track));
        var next = state.WithPlayer(player with { TopStepChoiceOwed = false, TopStepsHeld = rest.Count == 0 ? null : rest });
        var before = player.TrackSteps[(int)move.Track];
        next = ClimbTrack(next, move.Player, move.Track, 1, events, out var after);
        events.Insert(0, new TrackAdvanced(move.Player, move.Track, before, after));
        next = TrackRewards(next, move.Player, before, after, events);
        next = ReleaseHeldTopSteps(next, move.Player, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Power for every reward step passed on the way up a track, in order (each is a separate gain).
    private static GameState TrackRewards(GameState state, Faction faction, int before, int after, List<GameEvent> events)
    {
        foreach (var (step, power) in Config.TrackPowerRewards)
            if (before < step && step <= after)
                state = GainPower(state, faction, power, PowerSource.Track, events);
        // Auren with their Tree of Serenity: VP for reaching the top.
        if (faction == Faction.Auren && before < Config.TrackMax && after >= Config.TrackMax && state.LandscapeHex(faction) is int tree)
            state = GainVictoryPoints(state, faction, Landscapes.AurenVictoryPoints, events,
                new PointsArea.Landscape(tree), new PointsArea.Landscape(tree));
        return state;
    }

    // Moves the player down a track (the Acolytes paying for Volcano). No power is gained or lost on the way. Leaving the
    // top step gives back the key spent to reach it, and frees the step for someone else.
    private static GameState DescendTrack(GameState state, Faction faction, Track track, int steps, List<GameEvent> events)
    {
        var player = state[faction];
        var before = player.TrackSteps[(int)track];
        var after = Math.Max(0, before - steps);
        var keyBack = before == Config.TrackMax && after < Config.TrackMax;
        events.Add(new TrackDescended(faction, track, before, after));
        if (keyBack)
            events.Add(new KeyReturned(faction, track));
        var next = state.WithPlayer(player with
        {
            TrackSteps = player.TrackSteps.SetItem((int)track, after),
            Keys = player.Keys + (keyBack ? 1 : 0),
        });
        if (after != before) // arrives last on its new step
        {
            var order = next.TrackArrivals[(int)track];
            order = order.RemoveAt(order.IndexOf(faction)).Add(faction);
            next = next with { TrackArrivals = next.TrackArrivals.SetItem((int)track, order) };
        }
        return next;
    }

    // One step up the chosen track, owed: the Cultists' because an opponent took the power from their build; the
    // Acolytes' for a spade (between rounds too, when it is their turn to spend round-end spades).
    private static MoveResult ApplyTakeCultStep(GameState state, TakeCultStep move)
    {
        var betweenRounds = state.Phase == Phase.RoundEndSpades && move.Player == state.ActivePlayer;
        if (!betweenRounds && CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (state[move.Player].CultStepsOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no cult step to take.");

        var player = state[move.Player];
        var before = player.TrackSteps[(int)move.Track];
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state.WithPlayer(player with { CultStepsOwed = player.CultStepsOwed - 1 });
        next = ClimbTrack(next, move.Player, move.Track, 1, events, out var after);
        events.Insert(0, new TrackAdvanced(move.Player, move.Track, before, after));
        next = TrackRewards(next, move.Player, before, after, events);
        if (after == before)
            warnings.Add(new Issue(Severity.Note, $"The {Name(move.Player)} cannot go further up {move.Track}, so the step is lost."));
        if (betweenRounds && next[move.Player].CultStepsOwed == 0 && next[move.Player].SpadesOwed == 0)
            next = ContinueAfterRoundEnd(next, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // The Cultists give up the step an opponent's power brought them.
    private static MoveResult ApplyDeclineCultStep(GameState state, DeclineCultStep move)
    {
        if (move.Player != Faction.Cultists)
            return MoveResult.Rejected(state, $"Only the Cultists may give up their cult step.");
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (state[move.Player].CultStepsOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no cult step to give up.");
        var next = state.WithPlayer(state[move.Player] with { CultStepsOwed = state[move.Player].CultStepsOwed - 1 });
        return MoveResult.Ok(next, new List<GameEvent>(), MoveResult.NoIssues);
    }

    // Acolytes: the steps owed for a hex just made Volcano, paid down one track that has them all.
    private static MoveResult ApplyPayCultSteps(GameState state, PayCultSteps move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        var steps = player.CultStepsToPay;
        if (steps == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no cult steps to pay.");
        if (player.TrackSteps[(int)move.Track] < steps)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have only {player.TrackSteps[(int)move.Track]} steps on {move.Track}: {steps} are owed, all from one track.");

        var events = new List<GameEvent>();
        var before = player.TrackSteps[(int)move.Track];
        var next = state.WithPlayer(player with
        {
            CultStepsToPay = 0,
            AltarStepsToPay = false,
            VolcanoStepsPaid = player.AltarStepsToPay ? null : (move.Track, steps),
        });
        next = DescendTrack(next, move.Player, move.Track, steps, events);
        // Acolytes with their Altar of Sacrifice: power for the reward steps left (not when paying for the Altar).
        if (move.Player == Faction.Acolytes && !player.AltarStepsToPay && next.HasLandscape(move.Player))
            next = DescentRewards(next, move.Player, before, next[move.Player].TrackSteps[(int)move.Track], events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Favours ----------

    private static MoveResult ApplyTakeFavour(GameState state, TakeFavour move)
    {
        // The Ice Maidens take theirs during faction choice, straight after their starting terrain.
        var setup = state.Phase == Phase.ChoosingFactions && state.IsInGame(move.Player);
        if (!setup && (CheckTurn(state, move.Player) ?? CheckOffersAnswered(state)) is { } refusal)
            return refusal;

        var player = state[move.Player];
        if (player.FavoursOwed == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no favour to take.");
        if (move.Favour < 0 || move.Favour >= Favours.Count)
            return MoveResult.Rejected(state, "There is no such favour.");
        if (player.HoldsFavour(move.Favour))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} already have that favour.");
        var stack = state.FavourBank[move.Favour];
        if (stack.Count == 0)
            return MoveResult.Rejected(state, "That favour has run out.");

        var events = new List<GameEvent>();
        var next = state.WithPlayer(player with { FavoursOwed = player.FavoursOwed - 1 });
        next = TakeFavourTile(next, move.Player, move.Favour, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // The top tile of that favour to the player (who doesn't hold one): its steps and the power they bring; the
    // Conspirators' 2 coins.
    private static GameState TakeFavourTile(GameState state, Faction faction, int id, List<GameEvent> events)
    {
        var player = state[faction];
        var stack = state.FavourBank[id];
        var tile = stack[stack.Count - 1]; // the top tile
        var favour = Favours.All[id];
        var before = player.TrackSteps[(int)favour.Track];
        var next = state.WithPlayer(player with { FavourTiles = player.FavourTiles.Add(tile) }) with
        {
            FavourBank = state.FavourBank.SetItem(id, stack.RemoveAt(stack.Count - 1)),
        };
        next = ClimbTrack(next, faction, favour.Track, favour.Steps, events, out var after);
        next = TrackRewards(next, faction, before, after, events);
        if (faction == Faction.Conspirators) // 2 coins with every favour
        {
            var coins = next[faction].Coins + Config.ConspiratorsCoinsPerFavour;
            next = next.WithPlayer(next[faction] with { Coins = coins });
            events.Add(new CoinGained(faction, coins));
        }
        return next;
    }

    // The player is owed favours, to be chosen before their turn ends. A favour is only owed if there is
    // still one the player could take; otherwise it is lost, with a warning (the move still goes ahead).
    private static GameState EarnFavours(GameState state, Faction faction, int count, List<GameEvent> events, List<Issue> warnings)
    {
        if (count == 0)
            return state;

        var available = Enumerable.Range(0, Favours.Count).Count(id => CanTakeFavour(state, faction, id));
        var player = state[faction];
        var earned = Math.Min(count, available - player.FavoursOwed);
        if (earned < count)
            warnings.Add(new Issue(Severity.Note, $"There are not enough favours left that the {Name(faction)} can take, so they gain {Math.Max(earned, 0)} instead of {count}."));
        if (earned <= 0)
            return state;

        events.Add(new FavoursEarned(faction, earned));
        return state.WithPlayer(player with { FavoursOwed = player.FavoursOwed + earned });
    }
}
