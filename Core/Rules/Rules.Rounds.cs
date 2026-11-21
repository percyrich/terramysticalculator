namespace Core;

// Turn and round flow: ending a turn, passing and pass tiles, the end of a round and the start of the next (income).
public static partial class Rules
{
    // ---------- Ending a turn ----------

    private static MoveResult ApplyEndTurn(GameState state, EndTurn move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (state.IsPlanning)
            return EndFreeTurn(state, move.Player);
        // After the first of the Chaos Magicians' two actions the turn may end, giving up the second.
        var secondGivenUp = !state.ActionTaken && state.ActionsOwed == 1;
        if (!state.ActionTaken && !secondGivenUp)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} must take an action (or pass) before ending their turn.");
        if (MidAction(state, move.Player, endingTurn: true) is { } unfinished)
            return unfinished;

        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        if (secondGivenUp)
            warnings.Add(new Issue(Severity.Note, $"The {Name(move.Player)} end their turn without taking their second action."));
        state = ReleaseHeldPower(state, move.Player, events);
        // Priest exchanges the Darklings did not make are lost with the turn.
        var ended = state.WithPlayer(state[move.Player] with { PriestExchangesLeft = 0 }) with { ActionTaken = false, ActionsOwed = 0 };
        var next = ReturnPriestsFromReturningSlots(ended, events);

        if (next.PassOrder.Count == next.Players.Count)
            next = EndRound(next, events, warnings); // everyone has passed
        else
        {
            var following = NextPlayer(next);
            next = next with { ActivePlayer = following };
        }
        return MoveResult.Ok(next, events, warnings);
    }

    // Held power and tokens come once no power offer from the build is still waiting, as on BGA: before a town still to
    // form if the offers were answered first (the Children of the Wyrm in 717052298), after it if not (the Alchemists in
    // 241070597); with no offer, at once.
    private static GameState ReleaseHeldPowerWhenDone(GameState state, List<GameEvent> events)
    {
        foreach (var player in state.Players.Where(p => p.PowerHeld > 0 || p.TokensHeld > 0).ToList())
            if (!state.PowerOffers.Any(offer => offer.Builder == player.Faction))
                state = ReleaseHeldPower(state, player.Faction, events);
        return state;
    }

    // Power the action gave for its end (PlayerState.PowerHeld), gained once it is done (ReleaseHeldPowerWhenDone), at
    // the latest as the turn ends.
    // New tokens held (PlayerState.TokensHeld) go into bowl 1 first.
    private static GameState ReleaseHeldPower(GameState state, Faction faction, List<GameEvent> events)
    {
        var player = state[faction];
        if (player.PowerHeld == 0 && player.TokensHeld == 0)
            return state;
        var next = state.WithPlayer(player with { PowerHeld = 0, TokensHeld = 0 });
        if (player.TokensHeld > 0)
            next = AddTokens(next, faction, 1, player.TokensHeld, events);
        if (player.PowerHeld > 0)
            next = GainPower(next, faction, player.PowerHeld, PowerSource.Stronghold, events);
        return next;
    }

    // Who acts after the active player: the next one still in the round (second in TurnQueue),
    // or the active player again if they are the only one still in.
    private static Faction NextPlayer(GameState state)
    {
        var queue = state.TurnQueue;
        if (queue.Count > 1)
            return queue[1];
        var active = state.ActivePlayer!.Value;
        if (!state.HasPassed(active))
            return active;
        throw new InvalidOperationException("No player is left to take a turn."); // a rules bug, not a user error
    }

    // ---------- Passing and pass tiles ----------

    // Passing is the action. The player is out of the round from now on, but it stays their turn
    // (they may still convert) until they end it.
    // In rounds 1-5 the player also swaps their pass tile for one from the bank (taking its coins);
    // in the last round no tile is taken, and the tiles they hold go back to the bank once they have scored (less
    // clutter on the boards at the game's end).
    private static MoveResult ApplyPass(GameState state, Pass move)
    {
        if (CheckAction(state, move.Player) is { } refusal)
            return refusal;
        if (state.ActionsOwed == 2)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} take their first action before passing (passing can be the second).");

        var lastRound = state.Round >= Config.Rounds;
        if (lastRound && move.PassTile is not null)
            return MoveResult.Rejected(state, "No pass tiles are taken in the last round.");
        if (move.PassTile is int wanted && state.PassTileBank.IndexOf(wanted) < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");

        var warnings = new List<Issue>();
        var owesTile = !lastRound && !move.KeepTile && (move.PassTile is null || (move.Player == Faction.Archivists && state[move.Player].StrongholdBuilt));
        if (!lastRound && move.KeepTile)
            warnings.Add(new Issue(Severity.Note, $"The {Name(move.Player)} passed without taking a pass tile, so they keep the one they have."));

        var next = state with { PassOrder = state.PassOrder.Add(move.Player), ActionTaken = true };
        if (owesTile)
            next = next.WithPlayer(next[move.Player] with { PassTileOwed = true });
        var events = new List<GameEvent> { new Passed(move.Player) };
        var player = state[move.Player];
        var heldTiles = new ValueList<int>(new[] { player.PassTile, player.ExtraPassTile }.OfType<int>()); // the Archivists may hold two
        // Snow Shamans: a free step on their shipping or spade track, before the turn ends; the pass scores after it
        // (as on BGA: 819403314 #65, the shipping pass tile counting the new level).
        if (move.Player == Faction.SnowShamans && (player.Shipping < Factions.MaxShipping(move.Player)
                || player.SpadeUpgrades < Factions.MaxSpadeUpgrades(move.Player)))
        {
            next = next.WithPlayer(next[move.Player] with { FreeUpgradeOwed = true, PassScoringHeld = heldTiles });
            events.Add(new FreeUpgradeOwed(move.Player));
        }
        else
            next = ScorePass(next, move.Player, heldTiles, events);

        // Archivists with their stronghold: two new tiles, each swapped for one they hold (the first, then the second);
        // the tiles they held when passing can't be taken back this time (HeldPassTiles).
        if (owesTile && move.Player == Faction.Archivists && player.StrongholdBuilt)
            next = next.WithPlayer(next[move.Player] with
            {
                HeldPassTiles = heldTiles,
                ExtraPassTileOwed = true,
            });
        else if (move.PassTile is int taken)
            next = TakePassTile(next, move.Player, taken, events);
        if (move.PassTile is int first && next[move.Player].HeldPassTiles is not null)
            next = TakeArchivistsTile(next, move.Player, first, events);
        if (lastRound)
            next = ReturnPassTiles(next, move.Player, events);

        return MoveResult.Ok(next, events, warnings);
    }

    // Victory points for passing, with the faction as it is in this state (the Snow Shamans: after their free step):
    // favours that score trading posts, the pass tiles held when passing (heldTiles: given up by now if swapped in the
    // pass), and the stronghold's. Each scored separately, from what scores it (and what it counts), for the view.
    private static GameState ScorePass(GameState state, Faction faction, IReadOnlyList<int> heldTiles, List<GameEvent> events)
    {
        var next = state;
        var player = state[faction];
        var tradingPosts = state.Map.Count(piece => piece is { Kind: PieceKind.TradingPost } tp && tp.Owner == faction);
        foreach (var favour in player.FavourTiles)
            next = GainVictoryPoints(next, faction, Favours.All[favour.Id].PassVictoryPointsFor(tradingPosts), events,
                new PointsArea.Favour(favour.Id), new PointsArea.Favour(favour.Id));
        foreach (var held in heldTiles)
        {
            // As PassTiles.PassVictoryPoints, a building at a time.
            var tile = PassTiles.All[held];
            for (var hex = 0; hex < state.Map.Count; hex++)
                if (state.Map[hex] is { } piece && piece.Owner == faction && tile.PassScoredBuildings.Contains(Landscapes.CountsAs(piece)))
                    next = GainVictoryPoints(next, faction, tile.PassVictoryPointsPerBuilding, events,
                        new PointsArea.PassTile(held), new PointsArea.OnHex(hex));
            next = GainVictoryPoints(next, faction, player.Shipping * tile.PassVictoryPointsPerShipping, events,
                new PointsArea.PassTile(held), new PointsArea.Shipping());
        }
        if (player.StrongholdBuilt)
        {
            // From the stronghold's hex (the portrait if it isn't on the map, as in some tests).
            PointsArea stronghold = StrongholdHex(state, faction) is int at ? new PointsArea.OnHex(at) : new PointsArea.Portrait();
            if (faction == Faction.Engineers)
                foreach (var bridge in state.Bridges.Where(bridge => bridge.Owner == faction
                    && OwnsBuildingAt(state, faction, bridge.HexA) && OwnsBuildingAt(state, faction, bridge.HexB)))
                    next = GainVictoryPoints(next, faction, Config.EngineersVictoryPointsPerBridge, events,
                        stronghold, new PointsArea.OnBridge(bridge));
            if (faction == Faction.Djinni) // 1 VP per priest of theirs on the tracks
                foreach (var track in Enum.GetValues<Track>())
                    for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
                        if (state.PriestZones[(int)track][slot] is { } priest && priest.Owner == faction)
                            next = GainVictoryPoints(next, faction, Config.DjinniVictoryPointsPerPriest, events, stronghold, stronghold);
            if (faction == Faction.Firewalkers) // 1 VP per group of their structures (touching or bridged)
                foreach (var group in BuildingGroups(state, faction))
                    next = GainVictoryPoints(next, faction, Config.FirewalkersVictoryPointsPerGroup, events, stronghold, new PointsArea.OnHex(group[0]));
            if (faction == Faction.IceMaidens)
                for (var hex = 0; hex < state.Map.Count; hex++)
                    if (state.Map[hex] is { Kind: PieceKind.Temple } temple && temple.Owner == faction)
                        next = GainVictoryPoints(next, faction, Config.IceMaidensVictoryPointsPerTemple, events,
                            stronghold, new PointsArea.OnHex(hex));
        }

        return next;
    }

    // After passing: the new tile from the bank, swapped for the player's own. Conversions are still allowed
    // afterwards, until End turn.
    private static MoveResult PickAfterPassing(GameState state, PickPassTile move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (!state[move.Player].PassTileOwed && !state[move.Player].ExtraPassTileOwed)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} pick a new pass tile after passing: click their own pass tile to pass.");
        if (state.PassTileBank.IndexOf(move.Tile) < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");

        if (state[move.Player].HeldPassTiles is { } given && given.Contains(move.Tile))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} can't take back a tile they held when passing.");
        var events = new List<GameEvent>();
        if (state[move.Player].HeldPassTiles is not null) // the Archivists taking two
            return MoveResult.Ok(TakeArchivistsTile(state, move.Player, move.Tile, events), events, MoveResult.NoIssues);
        var next = TakePassTile(state, move.Player, move.Tile, events);
        next = next.WithPlayer(next[move.Player] with { PassTileOwed = false });
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // The player takes this tile from the bank, with the coins on it. The tiles left close up to the left, so
    // the bank has no gaps; the tile they held (if any) goes back at the right-hand end, after the others.
    private static GameState TakePassTile(GameState state, Faction faction, int tile, List<GameEvent> events)
    {
        var slot = state.PassTileBank.IndexOf(tile);
        var player = state[faction];
        var coins = state.PassTileCoins[tile];

        var left = state.PassTileBank.Where(t => t is int other && other != tile).ToList();
        events.Add(new PassTileTaken(faction, tile, slot, coins));
        if (player.PassTile is int old)
            left.Add(old);
        var bank = left.Concat(Enumerable.Repeat((int?)null, state.PassTileBank.Count - left.Count));

        var next = state.WithPlayer(player with { PassTile = tile, Coins = player.Coins + coins }) with
        {
            PassTileBank = new ValueList<int?>(bank),
            PassTileCoins = state.PassTileCoins.SetItem(tile, 0),
        };
        return ArchivistsPower(next, faction, coins, events);
    }

    // The player's pass tiles (the Archivists may hold two) back into the bank's free slots, after the tiles there.
    private static GameState ReturnPassTiles(GameState state, Faction faction, List<GameEvent> events)
    {
        var player = state[faction];
        var held = new[] { player.PassTile, player.ExtraPassTile }.OfType<int>().ToList();
        if (held.Count == 0)
            return state;
        var left = state.PassTileBank.OfType<int>().ToList();
        foreach (var tile in held)
        {
            events.Add(new PassTileReturned(faction, tile, left.Count));
            left.Add(tile);
        }
        var bank = left.Select(tile => (int?)tile).Concat(Enumerable.Repeat((int?)null, Math.Max(0, state.PassTileBank.Count - left.Count)));
        return state.WithPlayer(player with { PassTile = null, ExtraPassTile = null }) with { PassTileBank = new ValueList<int?>(bank) };
    }

    private static GameState AddCoinsToBankPassTiles(GameState state)
    {
        var coins = state.PassTileCoins;
        foreach (var tile in state.PassTileBank)
            if (tile is int t)
                coins = coins.SetItem(t, coins[t] + Config.PassTileCoinsPerRound);
        return state with { PassTileCoins = coins };
    }

    // ---------- The end of a round ----------

    // Everyone has passed. Either the game ends, or the pass order becomes the next round's turn order.
    private static GameState EndRound(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        events.Add(new RoundEnded(state.Round));

        // After the last round: final scoring instead of the round tile's reward.
        if (state.Round >= Config.Rounds)
            return EndGame.Finish(state with { ActivePlayer = null }, events);

        // The pass order becomes the next round's turn order (without variable turn order, the first player moves to the
        // end instead). Then this round's tile rewards positions on its track, the bank's pass tiles gain coins, and any
        // round-end transforms are made.
        var order = state.Options.VariableTurnOrder
            ? state.PassOrder
            : new ValueList<Faction>(state.TurnOrder.Skip(1).Append(state.TurnOrder[0]));
        var next = state with { TurnOrder = order, PassOrder = ValueList<Faction>.Empty, ActivePlayer = null };
        next = RoundEndRewards(next, events, warnings);
        next = AddCoinsToBankPassTiles(next);
        // The Treasurers with their stronghold may put the cult reward's coins, workers and priests into their Safe
        // before it opens, so doubled at once (as on BGA): the round waits for their choice
        // (TreasuryOfferAfter offers it; ApplyStoreInTreasury goes on).
        if (state.IsInGame(Faction.Treasurers) && state[Faction.Treasurers].StrongholdBuilt)
        {
            PlayerState was = state[Faction.Treasurers], now = next[Faction.Treasurers];
            if (now.Coins > was.Coins || now.Workers > was.Workers || now.Priests.Count > was.Priests.Count)
                return next with { SafeOpeningHeld = true };
        }
        return ContinueAfterRoundEnd(next, events, warnings);
    }

    // What this round's tile will give the faction at the round's end as things stand (coins, workers, priests,
    // power; not spades): nothing in the last round, outside the round's play (it is paid once the round ends), or for
    // the Archivists. (The panel's income.)
    public static Income RoundEndIncome(GameState state, Faction faction) =>
        state.Phase != Phase.Playing || state.Round < 1 || state.Round >= Config.Rounds || state.Round > state.ChosenRoundTiles.Count
            || faction == Faction.Archivists || !state.IsInGame(faction)
            ? default
            : RoundEndIncome(state, faction, RoundTiles.All[state.ChosenRoundTiles[state.Round - 1]]);

    private static int RoundEndTimes(PlayerState player, RoundTileDefinition tile) =>
        tile.Track is Track track ? player.TrackSteps[(int)track] / tile.StepsPerReward : 0;

    private static Income RoundEndIncome(GameState state, Faction faction, RoundTileDefinition tile)
    {
        var priestsOnTracks = state.PriestZones.Sum(zone => zone.Count(slot => slot is { } priest && priest.Owner == faction));
        return tile.Reward * RoundEndTimes(state[faction], tile) + new Income(tile.CoinsPerPriestOnTracks * priestsOnTracks, 0, 0, 0);
    }

    // This round's tile rewards every player once for each full StepsPerReward steps on its track
    // (and with coins per priest on the tracks, for tiles that count those).
    private static GameState RoundEndRewards(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        if (state.Round < 1 || state.Round > state.ChosenRoundTiles.Count)
            return state;
        var tile = RoundTiles.All[state.ChosenRoundTiles[state.Round - 1]];

        foreach (var faction in state.TurnOrder.Where(faction => faction != Faction.Archivists)) // they get no cult rewards
        {
            var income = RoundEndIncome(state, faction, tile);
            var spades = tile.SpadesPerReward * RoundEndTimes(state[faction], tile);
            if (income == default && spades == 0)
                continue;

            events.Add(new RoundEndRewardReceived(faction, income, spades));
            state = Receive(state, faction, income, PowerSource.RoundEnd, events, warnings);
            // The Giants need exactly 2 spades for any transform, and spades between rounds cannot be topped up.
            if (faction == Faction.Giants && spades % Config.GiantsSpadesPerTransform != 0)
            {
                var lost = spades % Config.GiantsSpadesPerTransform;
                warnings.Add(new Issue(Severity.Note, $"The Giants need {Config.GiantsSpadesPerTransform} spades for every transform, so {lost} spade{(lost == 1 ? " is" : "s are")} lost."));
                events.Add(new SpadesForfeited(faction, lost));
                spades -= lost;
            }
            state = GainSpades(state, faction, spades, events, warnings);
        }
        return state;
    }

    // The first player (in the next round's turn order) still holding round-end spades spends them (the Acolytes:
    // still owing the cult steps their spades became, takes them); once nobody owes any, the next round begins.
    private static GameState ContinueAfterRoundEnd(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        var owed = state.TurnOrder.Where(faction => OwesBetweenRounds(state[faction]))
            .Select(faction => (Faction?)faction).FirstOrDefault();
        if (owed is not Faction faction)
            return BeginRound(state, events, warnings);

        return state with { Phase = Phase.RoundEndSpades, ActivePlayer = faction, ActionTaken = false, SpadedHexes = ValueList<int>.Empty, SkipUsed = false };
    }

    // ---------- The start of a round: income ----------

    // Starts the next round: everyone receives income, nobody has passed, first in turn order acts.
    private static GameState BeginRound(GameState state, List<GameEvent> events, List<Issue> warnings)
    {
        var round = state.Round + 1;
        var next = state with
        {
            Phase = Phase.Playing,
            Round = round,
            PassOrder = ValueList<Faction>.Empty,
            ActivePlayer = state.TurnOrder[0],
            ActionTaken = false,
            PowerActionsTaken = ValueList<int>.Empty, // every power action is available again
        };
        events.Add(new RoundStarted(round));
        // Dragonlords with their Lava Pond: a new token in bowl 3.
        if (next.IsInGame(Faction.Dragonlords) && next.HasLandscape(Faction.Dragonlords))
            next = AddTokens(next, Faction.Dragonlords, 3, Landscapes.DragonlordsTokensPerRound, events);

        next = OpenSafe(next, events, warnings); // the Treasurers' Safe, doubled, before income (Rules.FanFactions.cs)
        foreach (var player in state.Players)
        {
            var income = player.Income;
            if (player.Faction == Faction.Treasurers && round < Config.Rounds) // what may go into their Safe now
                next = next with { TreasuryOffer = new Income(income.Coins, income.Workers, income.Priests, 0) };
            events.Add(new IncomeReceived(player.Faction, income));
            // Every bonus action is available again.
            next = next.WithPlayer(next[player.Faction] with { BonusActionsUsed = ValueList<BonusActionId>.Empty });
            next = Receive(next, player.Faction, income, PowerSource.Income, events, warnings);
        }

        // Free play: begins with round 1 if Free was chosen before it; the faction selected stays selected.
        if (next.Mode == PlayMode.Free && next.Planning is null)
        {
            next = next with { Planning = new FreePlan(state.TurnOrder[0], false, 0, round, ValueList<Faction>.Empty, state.TurnOrder[0]) };
            events.Add(new PlanningStarted(state.TurnOrder[0]));
        }
        if (next.Planning is { Selected: Faction selected })
            next = next with { ActivePlayer = selected };
        return next;
    }

    // Coins and workers are added; priests are gained one at a time (warning if the supply runs out);
    // power is gained into the bowls.
    private static GameState Receive(GameState state, Faction faction, Income income, PowerSource source,
        List<GameEvent> events, List<Issue> warnings)
    {
        var player = state[faction];
        var next = state.WithPlayer(player with
        {
            Coins = player.Coins + income.Coins,
            Workers = player.Workers + income.Workers,
        });
        for (var i = 0; i < income.Priests; i++)
            next = GainPriest(next, faction, events, warnings);
        if (income.Power > 0)
            next = GainPower(next, faction, income.Power, source, events);
        if (income.VictoryPoints != 0)
            next = GainVictoryPoints(next, faction, income.VictoryPoints, events, new PointsArea.Portrait(), new PointsArea.Portrait());
        return next;
    }
}
