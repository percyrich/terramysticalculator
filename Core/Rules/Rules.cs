namespace Core;

// The rules: a pure function from (state, move) to a result.
// Static, holds no data of its own, never modifies its inputs.
//
// Rules is one class written across the files of this folder, one file per topic:
//   Rules.cs               every move enters here (Apply) and is handed to its own method (ApplyMove)
//   Rules.Checks.cs        the checks most moves begin with: whose turn, one action a turn, nothing left unfinished
//   Rules.Queries.cs       what the view asks: where a piece can go, what something costs, what is in range
//   Rules.GameSetup.cs     before round 1: game, factions, turn order, map, tiles, starting dwellings
//   Rules.Building.cs      dwellings, upgrades, strongholds, shipping and spade upgrades
//   Rules.Terrain.cs       transforming, spades, Volcano
//   Rules.Cults.cs         priests, the four tracks, favours
//   Rules.Power.cs         gaining power, power offered by neighbours' builds, the power actions
//   Rules.FreeActions.cs   conversions, burning, and the choices a faction owes
//   Rules.Towns.cs         adjacency, bridges, towns
//   Rules.BonusActions.cs  the octagons: stronghold, pass tile and favour actions
//   Rules.Rounds.cs        ending a turn, passing, pass tiles, the end and start of a round, income
//   Rules.Modes.cs         Strict, Lenient, Free play, and the turn-order step
//   Rules.Calculator.cs    Calculator: the neutral opponent, the +1 panel
//   Rules.EditPosition.cs  setting up a game in progress: every value set directly (the Edit moves)
//   Rules.PositionChecks.cs what a set-up position must satisfy before play goes on
//   Rules.Landscapes.cs    the Special Landscapes expansion
//   Rules.FanFactions.cs   the fan factions, one section each
public static partial class Rules
{
    // Every move goes through here. Moves are applied one by one (ApplyMove); after the Chaos Magicians' double
    // action, the turn's action is taken twice: the first action leaves ActionTaken false (one action still owed),
    // the second sets it; anything else in between (spending spades, bridges, favours) leaves it as it was.
    // Strict refuses any move that leaves somebody in debt (coins, workers, priests); below 0 VP only warns, as in
    // Lenient and Free (an auction bid can leave a player there). In Free, every
    // faction that takes an action is remembered (FreePlan.Acted).
    public static MoveResult Apply(GameState state, Move move)
    {
        // Treasurers: anything they may put in their Safe is settled (StoreInTreasury) before anything else happens
        // (with their stronghold, their conversions may come first: TreasurersConverting).
        if (state.TreasuryOffer is not null && move is not (StoreInTreasury or ChooseForSafe or SetMode or SelectFaction or SetRound)
            && !IsEdit(move) // (Setup mode's edits are never held)
            && !TreasurersConverting(state, move))
            return MoveResult.Rejected(state, $"Waiting for the {Name(Faction.Treasurers)} to choose what goes into their Safe.");
        var result = ApplyCountingActions(state, move);
        if (!result.Accepted)
            return result;
        // Atlanteans: their town's power thresholds pay out as soon as they are reached (Rules.FanFactions.cs).
        var rewards = new List<GameEvent>();
        var rewardNotes = new List<Issue>();
        var rewarded = AtlanteansTownRewards(result.State, rewards, rewardNotes);
        if (rewards.Count > 0)
            result = result with { State = rewarded, Events = result.Events.Concat(rewards).ToList(), Issues = result.Issues.Concat(rewardNotes).ToList() };
        // Firewalkers: their VP never behind their pyro token (Free: only a warning).
        if (PyroOvertaken(state, result.State) is string pyro)
        {
            if (state.Mode != PlayMode.Free && result.State.Mode != PlayMode.Free || IsCalculatorStrict(state))
                return MoveResult.Rejected(state, pyro);
            result = result with { Issues = result.Issues.Append(new Issue(Severity.Warning, pyro)).ToList() };
        }
        var next = DropHeldTopSteps(state, NoteTownMakers(state, result.State));
        next = TreasuryOfferAfter(state, move, next, result.Events); // what the Treasurers may put in their Safe now
        // Power held for the end of an action (the Alchemists' stronghold) comes once the action is complete and every
        // power offer from it has been answered (as on BGA).
        var releasedPower = new List<GameEvent>();
        next = ReleaseHeldPowerWhenDone(next, releasedPower);
        if (releasedPower.Count > 0)
            result = result with { Events = result.Events.Concat(releasedPower).ToList() };
        if (state.Planning is not null && next.Planning is FreePlan plan && (IsAction(state, move) || move is BonusDoubleAction)
            && state.ActivePlayer is Faction actor && !plan.Acted.Contains(actor))
            next = next with { Planning = plan with { Acted = plan.Acted.Add(actor) } };
        // Before the starting dwellings VictoryPoints holds the starting VP's offset from 20 (an auction may go below):
        // no debt then.
        if ((state.Mode == PlayMode.Strict && next.Mode == PlayMode.Strict || IsCalculatorStrict(state))
            && next.Phase != Phase.SettingUp && next.Phase >= Phase.PlacingDwellings && next.Phase != Phase.Finished && next.AnyoneInDebt)
            return new MoveResult(state, Array.Empty<GameEvent>(), new[] { new Issue(Severity.Error, $"Strict: {DebtText(next, strict: true)}") });
        // Anyone left deeper in debt (or lower below 0 VP) with no warning about it yet gets one. (Not for the move ending
        // the game: final scoring ignores priest debt; it stays shown on the board.)
        if (next.Phase != Phase.SettingUp && next.Phase >= Phase.PlacingDwellings && next.Phase != Phase.Finished && !result.Issues.Any(issue => issue.Severity == Severity.Warning))
        {
            var deeper = next.Players.Where(after => state.IsInGame(after.Faction) && DeeperInDebt(state[after.Faction], after)).ToList();
            if (deeper.Count > 0)
            {
                var shown = next with { Players = new ValueList<PlayerState>(deeper) };
                var issues = result.Issues.Append(new Issue(Severity.Warning, Capitalised(DebtText(shown, would: false)))).ToList();
                return result with { State = next, Issues = issues };
            }
        }
        return result with { State = next };
    }

    private static MoveResult ApplyCountingActions(GameState state, Move move)
    {
        var original = state;
        // Calculator: the turn never needs ending (only after passing), so a new action after a finished one first does
        // what End turn would: priests in the returning slots go back, the Darklings' exchanges not made are lost.
        List<GameEvent>? turnEnd = null;
        if (state.Options.Calculator && state.IsPlanning && state.ActionTaken && IsAction(state, move)
            && state.ActivePlayer is Faction actor && !state.HasPassed(actor) && MidAction(state, actor, endingTurn: false) is null)
        {
            turnEnd = new List<GameEvent>();
            state = ReturnPriestsFromReturningSlots(
                state.WithPlayer(state[actor] with { PriestExchangesLeft = 0 }) with { ActionTaken = false, ActionsOwed = 0 }, turnEnd);
        }
        // A new action: its one carpet flight or tunnel is still to use.
        if (IsAction(state, move) && state.SkipUsed)
            state = state with { SkipUsed = false };
        var result = ApplyMove(state, move);
        if (!result.Accepted)
            return result with { State = original };
        if (turnEnd is { Count: > 0 })
            result = result with { Events = turnEnd.Concat(result.Events).ToList() };
        if (state.ActionsOwed == 0 || move is BonusDoubleAction)
            return result;
        var next = result.State;
        if (IsAction(state, move))
        {
            var owed = state.ActionsOwed - 1;
            next = next with { ActionsOwed = owed, ActionTaken = owed == 0 };
        }
        else if (next.ActionsOwed > 0)
            next = next with { ActionTaken = state.ActionTaken };
        return result with { State = next };
    }

    // The moves that are a turn's one action. A special landscape is one unless it is placed with free spades, on a hex
    // just transformed or as the Chaos Magicians' portal; the Alchemists' upgrades it offers are not.
    internal static bool IsAction(GameState state, Move move) => move switch
    {
        PlaceLandscape m => !state.IsInGame(m.Player)
            || !(state[m.Player].LandscapeOffers.HasFlag(LandscapeOffer.Portal) || (state.SpadesPending && state.ActivePlayer == m.Player)),
        // The Snow Shamans' free steps after passing are never an action.
        UpgradeShipping m => !(state.IsInGame(m.Player) && (state[m.Player].LandscapeOffers.HasFlag(LandscapeOffer.Shipping) || m.Player == Faction.SnowShamans)),
        UpgradeSpades m => !(state.IsInGame(m.Player) && (state[m.Player].LandscapeOffers.HasFlag(LandscapeOffer.Spades) || m.Player == Faction.SnowShamans)),
        _ => move is BuildDwelling or UpgradeBuilding or TransformTerrain or PlacePriest or Pass
            or BonusCultStep or BonusSpades or BonusBuild or BonusTradingPost or BonusSandstorm
            or TakePowerAction or BuildBridge or BonusShapeshift
            or SwapTracks or SpendTreasure or AdvanceIncomeTrack or BonusMoveBridge or BonusTokensToBowlThree
            or BonusCoinsForTradingPosts or BonusFourPower or BonusSwapFavour
            or LetLavaFlow or BonusRiverTokens or BonusMoveRock or BonusSelkiesBuild,
    };

    // The moves that set a value directly while a game in progress is set up (Rules.EditPosition.cs): recognised by
    // their name, so every such move's name must begin with Edit.
    internal static bool IsEdit(Move move) => move.GetType().Name.StartsWith("Edit");

    private static MoveResult ApplyMove(GameState state, Move move) => move switch
    {
        ChooseGame m            => ApplyChooseGame(state, m),
        AddPlayer m             => ApplyAddPlayer(state, m),
        RemovePlayer m          => ApplyRemovePlayer(state, m),
        ChooseStartingTerrain m => ApplyChooseStartingTerrain(state, m),
        ConfirmFactions         => ApplyConfirmFactions(state),
        ChooseTurnOrder m       => ApplyChooseTurnOrder(state, m),
        UnchooseTurnOrder m     => ApplyUnchooseTurnOrder(state, m),
        ConfirmTurnOrder        => ApplyConfirmTurnOrder(state),
        AdjustStartingVictoryPoints m => ApplyAdjustStartingVictoryPoints(state, m),
        ChooseMap m             => ApplyChooseMap(state, m),
        ChooseCustomMap m       => ApplyChooseCustomMap(state, m),
        ChooseRoundTile m       => ApplyChooseRoundTile(state, m),
        UnchooseRoundTile m     => ApplyUnchooseRoundTile(state, m),
        ConfirmRoundTiles       => ApplyConfirmRoundTiles(state),
        AddPassTile m           => ApplyAddPassTile(state, m),
        RemovePassTile m        => ApplyRemovePassTile(state, m),
        StartGame               => ApplyStartGame(state),
        PlaceStartingDwelling m => ApplyPlaceStartingDwelling(state, m),
        PickPassTile m          => ApplyPickPassTile(state, m),
        BuildDwelling m         => ApplyBuildDwelling(state, m),
        UpgradeBuilding m       => ApplyUpgradeBuilding(state, m),
        TransformTerrain m      => ApplyTransformTerrain(state, m),
        PlacePriest m           => ApplyPlacePriest(state, m),
        Pass m                  => ApplyPass(state, m),
        ConvertPowerToCoin m    => ApplyConvertPowerToCoin(state, m),
        ConvertPowerToWorker m  => ApplyConvertPowerToWorker(state, m),
        ConvertPriestToWorker m => ApplyConvertPriestToWorker(state, m),
        ConvertWorkerToCoin m   => ApplyConvertWorkerToCoin(state, m),
        ConvertPowerToPriest m  => ApplyConvertPowerToPriest(state, m),
        BurnPower m             => ApplyBurnPower(state, m),
        TakeFavour m            => ApplyTakeFavour(state, m),
        BonusCultStep m         => ApplyBonusCultStep(state, m),
        BonusSpades m           => ApplyBonusSpades(state, m),
        TakePowerAction m       => ApplyTakePowerAction(state, m),
        PlaceBridge m           => ApplyPlaceBridge(state, m),
        FormTown m              => ApplyFormTown(state, m),
        AcceptPower m           => ApplyAnswerPower(state, m.Player, accept: true),
        DeclinePower m          => ApplyAnswerPower(state, m.Player, accept: false),
        UpgradeShipping m       => ApplyUpgradeShipping(state, m),
        UpgradeSpades m         => ApplyUpgradeSpades(state, m),
        SpendSpades m           => ApplySpendSpades(state, m),
        BuildOnSpadedHex m      => ApplyBuildOnSpadedHex(state, m),
        FinishSpades m          => ApplyFinishSpades(state, m),
        BonusBuild m            => ApplyBonusBuild(state, m),
        BonusTradingPost m      => ApplyBonusTradingPost(state, m),
        BonusDoubleAction m     => ApplyBonusDoubleAction(state, m),
        BonusSandstorm m        => ApplyBonusSandstorm(state, m),
        BuildBridge m           => ApplyBuildBridge(state, m),
        ConvertVictoryPointToCoin m  => ApplyConvertVictoryPointToCoin(state, m),
        ConvertCoinsToVictoryPoint m => ApplyConvertCoinsToVictoryPoint(state, m),
        TakeCultStep m          => ApplyTakeCultStep(state, m),
        ExchangeWorkersForPriests m  => ApplyExchangeWorkersForPriests(state, m),
        FinishPriestExchange m  => ApplyFinishPriestExchange(state, m),
        BonusShapeshift m       => ApplyBonusShapeshift(state, m),
        SkipBridges m           => ApplySkipBridges(state, m),
        PayCultSteps m          => ApplyPayCultSteps(state, m),
        AnswerTokenOffer m      => ApplyAnswerTokenOffer(state, m),
        ChoosePriest m          => ApplyChoosePriest(state, m),
        PlaceLandscape m        => ApplyPlaceLandscape(state, m),
        ChooseTownBonus m       => ApplyChooseTownBonus(state, m),
        SwapTracks m            => ApplySwapTracks(state, m),
        SpendTreasure m         => ApplySpendTreasure(state, m),
        AdvanceIncomeTrack m    => ApplyAdvanceIncomeTrack(state, m),
        BonusMoveBridge m       => ApplyBonusMoveBridge(state, m),
        BonusTokensToBowlThree m => ApplyBonusTokensToBowlThree(state, m),
        BonusCoinsForTradingPosts m => ApplyBonusCoinsForTradingPosts(state, m),
        BonusFourPower m        => ApplyBonusFourPower(state, m),
        BonusSwapFavour m       => ApplyBonusSwapFavour(state, m),
        BuyPowerToken m         => ApplyBuyPowerToken(state, m),
        StoreInTreasury m       => ApplyStoreInTreasury(state, m),
        ChooseForSafe m         => ApplyChooseForSafe(state, m),
        TakeStartingSteps m     => ApplyTakeStartingSteps(state, m),
        PlaceFreeDwelling m     => ApplyPlaceFreeDwelling(state, m),
        ChooseChangelingTerrains m => ApplyChooseChangelingTerrains(state, m),
        SkipFreeDwelling m      => ApplySkipFreeDwelling(state, m),
        PlaceLava m             => ApplyPlaceLava(state, m),
        LetLavaFlow m           => ApplyLetLavaFlow(state, m),
        AdvancePyro m           => ApplyAdvancePyro(state, m),
        MoveRock m              => ApplyMoveRock(state, m),
        BonusMoveRock m         => ApplyBonusMoveRock(state, m),
        BonusRiverTokens m      => ApplyBonusRiverTokens(state, m),
        BonusSelkiesBuild m     => ApplyBonusSelkiesBuild(state, m),
        EditLava m              => ApplyEditLava(state, m),
        EditWyrmToken m         => ApplyEditWyrmToken(state, m),
        EditRock m              => ApplyEditRock(state, m),
        DeclineLandscapeBonus m => ApplyDeclineLandscapeBonus(state, m),
        LandscapeShapeshift m   => ApplyLandscapeShapeshift(state, m),
        TakeCoinInstead m       => ApplyTakeCoinInstead(state, m),
        EditLandscape m         => ApplyEditLandscape(state, m),
        EndTurn m               => ApplyEndTurn(state, m),
        SetMode m               => ApplySetMode(state, m),
        PlaceNeutralDwelling m  => ApplyNeutral(state, m),
        RemoveNeutralDwelling m => ApplyNeutral(state, m),
        PlaceNeutralBridge m    => ApplyNeutral(state, m),
        RemoveNeutralBridge m   => ApplyNeutral(state, m),
        PlaceNeutralPriest m    => ApplyNeutral(state, m),
        RemoveNeutralPriest m   => ApplyNeutral(state, m),
        CalculatorGain m        => ApplyCalculatorGain(state, m),
        TakePassTileCoin m      => ApplyTakePassTileCoin(state, m),
        SelectFaction m         => ApplySelectFaction(state, m),
        CancelPriestDebt m      => ApplyCancelPriestDebt(state, m),
        EditResource m          => ApplyEditResource(state, m),
        EditPowerToken m        => ApplyEditPowerToken(state, m),
        EditGainPriest m        => ApplyEditGainPriest(state, m),
        EditSafe m              => ApplyEditSafe(state, m),
        EditReturnPriest m      => ApplyEditReturnPriest(state, m),
        EditPlacePriest m       => ApplyEditPlacePriest(state, m),
        EditTrack m             => ApplyEditTrack(state, m),
        EditFavour m            => ApplyEditFavour(state, m),
        EditLevel m             => ApplyEditLevel(state, m),
        EditBridge m            => ApplyEditBridge(state, m),
        EditBuilding m          => ApplyEditBuilding(state, m),
        EditTerrain m           => ApplyEditTerrain(state, m),
        EditPlaceKey m          => ApplyEditPlaceKey(state, m),
        EditRemoveKey m         => ApplyEditRemoveKey(state, m),
        SetRound m              => ApplySetRound(state, m),
        EditPassTile m          => ApplyEditPassTile(state, m),
        EditPassTileCoins m     => ApplyEditPassTileCoins(state, m),
        MarkPassed m            => ApplyMarkPassed(state, m),
        TogglePowerAction m     => ApplyTogglePowerAction(state, m),
        ToggleBonusAction m     => ApplyToggleBonusAction(state, m),
        EditWheel m             => ApplyEditWheel(state, m),
        EditHome m              => ApplyEditHome(state, m),
        FinishSetup             => ApplyFinishSetup(state),
        _ => throw new ArgumentOutOfRangeException(nameof(move), move, "Unknown move type."),
    };

    // ---------- After every move: towns made formable, debt ----------

    // Towns that a building or bridge just made formable remember it (TownMaker), so that forming the town later in
    // the turn shows its points set off from there. Towns made formable otherwise (a favour lowering the power
    // needed) have none. Forgotten once the turn passes to someone else.
    private static GameState NoteTownMakers(GameState before, GameState after)
    {
        var makers = after.Makers.Where(maker => maker.Faction == after.ActivePlayer).ToList();
        if (after.Phase == Phase.Playing && (before.Map != after.Map || before.Bridges != after.Bridges))
        {
            var placed = new List<(Faction Owner, PointsArea Trigger, int[] Hexes)>();
            for (var hex = 0; hex < after.Map.Count; hex++)
                if (after.Map[hex] is Piece piece && (hex >= before.Map.Count || before.Map[hex] != piece))
                    placed.Add((piece.Owner, new PointsArea.OnHex(hex), new[] { hex }));
            foreach (var bridge in after.Bridges.Where(bridge => !before.Bridges.Contains(bridge)))
                placed.Add((bridge.Owner, new PointsArea.OnBridge(bridge), new[] { bridge.HexA, bridge.HexB }));
            foreach (var faction in placed.Select(p => p.Owner).Distinct().Where(after.IsInGame))
            {
                var formableBefore = FormableTowns(before, faction);
                foreach (var town in FormableTowns(after, faction))
                {
                    if (formableBefore.Any(old => old.Any(town.Contains))
                        || makers.Any(maker => maker.Faction == faction && maker.Hexes.Any(town.Contains)))
                        continue; // already formable: made by something earlier
                    var by = placed.FirstOrDefault(p => p.Owner == faction && p.Hexes.Any(town.Contains));
                    if (by.Trigger is not null)
                        makers.Add(new TownMaker(faction, town, by.Trigger));
                }
            }
        }
        var kept = new ValueList<TownMaker>(makers);
        return kept == after.Makers ? after : after with { TownMakers = makers.Count == 0 ? null : kept };
    }

    private static bool DeeperInDebt(PlayerState before, PlayerState after) =>
        (after.Coins < 0 && after.Coins < before.Coins) || (after.Workers < 0 && after.Workers < before.Workers)
        || after.PriestDebt > before.PriestDebt || (after.VictoryPoints < 0 && after.VictoryPoints < before.VictoryPoints);

    // Who is in debt or below 0 VP, and by how much ("the Giants would be in debt: -2 coins, 1 priest"). strict: only
    // what Strict refuses (VP left out).
    private static string DebtText(GameState state, bool would = true, bool strict = false)
    {
        var parts = new List<string>();
        foreach (var player in state.Players.Where(player => strict ? player.OwesResources : player.InDebt))
        {
            var debts = new List<string>();
            if (player.Coins < 0) debts.Add($"{player.Coins} coins");
            if (player.Workers < 0) debts.Add($"{player.Workers} workers");
            if (player.PriestDebt > 0) debts.Add(player.PriestDebt == 1 ? "1 priest" : $"{player.PriestDebt} priests");
            if (player.VictoryPoints < 0 && !strict) debts.Add($"{player.VictoryPoints} VP");
            parts.Add($"the {Name(player.Faction)} {(would ? "would be" : "are")} in debt ({string.Join(", ", debts)})");
        }
        return string.Join("; ", parts) + ".";
    }

    // A warning naming every resource below zero, or no warning at all.
    private static IReadOnlyList<Issue> DebtWarning(PlayerState player)
    {
        var debts = new List<string>();
        if (player.Coins < 0) debts.Add($"{player.Coins} coins");
        if (player.Workers < 0) debts.Add($"{player.Workers} workers");
        if (player.PriestDebt > 0) debts.Add(player.PriestDebt == 1 ? "1 priest" : $"{player.PriestDebt} priests");
        return debts.Count == 0
            ? MoveResult.NoIssues
            : new[] { new Issue(Severity.Warning, $"The {Name(player.Faction)} are in debt: {string.Join(", ", debts)}.") };
    }

    // ---------- Victory points ----------

    // source and trigger: what scored them and what set that off, for the view (VictoryPointsGained); none: not shown.
    private static GameState GainVictoryPoints(GameState state, Faction faction, int amount, List<GameEvent> events,
        PointsArea? source = null, PointsArea? trigger = null)
    {
        if (amount == 0)
            return state;
        var player = state[faction];
        var gained = player with { VictoryPoints = player.VictoryPoints + amount };
        events.Add(new VictoryPointsGained(faction, amount, gained.VictoryPoints, source, trigger));
        return state.WithPlayer(gained);
    }

    // A lasting favour effect's VP (per dwelling, per trading post) for building on the hex: each favour held from
    // itself, set off from the hex.
    private static GameState FavourPoints(GameState state, Faction faction, Func<FavourDefinition, int> effect, int hex,
        List<GameEvent> events)
    {
        foreach (var favour in state[faction].FavourTiles)
            state = GainVictoryPoints(state, faction, effect(Favours.All[favour.Id]), events,
                new PointsArea.Favour(favour.Id), new PointsArea.OnHex(hex));
        return state;
    }

    // ---------- Wording ----------

    // A faction's name for messages ("Chaos Magicians").
    private static string Name(Faction? faction) => faction is Faction f ? Factions.Name(f) : "nobody";

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
