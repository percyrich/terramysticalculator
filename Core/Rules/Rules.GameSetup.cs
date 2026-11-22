namespace Core;

// Before round 1: the game and its options, the factions, the turn order, the map, the round and pass tiles, then
// Start: starting resources, starting dwellings and the first pass tiles (Phase.ChoosingGame to PickingPassTiles).
// (Recording a game already in progress is Rules.EditPosition.cs.)
public static partial class Rules
{
    // ---------- Game and factions ----------

    private static MoveResult ApplyChooseGame(GameState state, ChooseGame move)
    {
        if (state.Phase != Phase.ChoosingGame)
            return MoveResult.Rejected(state, "The game has already been chosen.");
        var options = move.Options ?? GameOptions.All;
        if (options.FinalScoring is not null && !options.FireAndIce)
            return MoveResult.Rejected(state, "The final scoring tiles are from Fire & Ice, which is not in this game.");
        if (options.FanFactions && options.Landscapes)
            return MoveResult.Rejected(state, "The fan factions and the special landscapes can't be played together.");
        if (options.SetUpInProgress && options.StartingMode == PlayMode.Free && !options.Calculator)
            return MoveResult.Rejected(state, "A game in progress is set up for Strict or Lenient play, not Free.");
        return MoveResult.Ok(state with
        {
            Game = move.Game, Phase = Phase.ChoosingFactions, ChosenOptions = options,
            Mode = options.Calculator ? PlayMode.Free : options.StartingMode, // Calculator: always Free
            KeyBank = KeyTiles.FullBank(options.MiniExpansion),
        });
    }

    private static MoveResult ApplyAddPlayer(GameState state, AddPlayer move)
    {
        if (state.Phase != Phase.ChoosingFactions)
            return MoveResult.Rejected(state, "Factions can only be chosen during setup.");
        if (state.IsInGame(move.Faction))
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are already in the game.");
        if (state.Players.Count >= Config.MaxPlayers)
            return MoveResult.Rejected(state, $"At most {Config.MaxPlayers} factions can play.");
        if (state.Options.Calculator && state.Players.Count >= 1)
            return MoveResult.Rejected(state, "Calculator plays one faction.");
        if (SetupChoiceWaiting(state) is { } waiting)
            return waiting;
        // One faction per colour: one ice faction, one volcano faction, and each basic terrain once (a base faction's
        // home, or a starting terrain chosen by an ice or variable faction).
        if (Factions.IsFanFaction(move.Faction) && !state.Options.FanFactions)
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are a fan faction, and the fan factions are not in this game.");
        var kind = Factions.Kind(move.Faction);
        if (kind != FactionKind.Base && !state.Options.FireAndIce)
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are from Fire & Ice, which is not in this game.");
        // One faction of each Fire & Ice kind: one ice, one volcano, one variable (Riverwalkers or Shapeshifters).
        if (kind != FactionKind.Base
            && state.Players.FirstOrDefault(player => player.Kind == kind) is PlayerState sameKind)
        {
            var kindName = kind switch { FactionKind.Ice => "ice", FactionKind.Volcano => "volcano", _ => "variable" };
            return MoveResult.Rejected(state, $"The {Name(sameKind.Faction)} are already in the game: only one {kindName} faction can play.");
        }
        if (kind == FactionKind.Base)
        {
            var home = Factions.HomeTerrain(move.Faction);
            if (state.Players.FirstOrDefault(player => (player.Kind == FactionKind.Base ? player.Home : player.StartingTerrain) == home) is PlayerState sameColour)
                return MoveResult.Rejected(state, sameColour.Kind == FactionKind.Base
                    ? $"The {Name(sameColour.Faction)} already have {home} as their home terrain: only one faction of each colour can play."
                    : $"The {Name(sameColour.Faction)} have chosen {home} as their starting terrain: only one faction of each colour can play.");
        }
        if (kind is FactionKind.Ice or FactionKind.Variable && !Terrains.Wheel.Except(state.TakenTerrains()).Any())
            return MoveResult.Rejected(state, $"Every terrain is taken: the {Name(move.Faction)} would have no starting terrain to choose.");

        var next = state with
        {
            Players = state.Players.Add(PlayerState.New(move.Faction)),
            TrackArrivals = new ValueList<ValueList<Faction>>(state.TrackArrivals.Select(order => order.Add(move.Faction))),
        };
        return MoveResult.Ok(next);
    }

    private static MoveResult ApplyRemovePlayer(GameState state, RemovePlayer move)
    {
        if (state.Phase != Phase.ChoosingFactions)
            return MoveResult.Rejected(state, "Factions can only be removed during setup.");
        if (!state.IsInGame(move.Faction))
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are not in the game.");

        var remaining = new ValueList<PlayerState>(state.Players.Where(p => p.Faction != move.Faction));
        var arrivals = new ValueList<ValueList<Faction>>(state.TrackArrivals.Select(order =>
            new ValueList<Faction>(order.Where(faction => faction != move.Faction))));
        // Favours taken during setup (the Ice Maidens') go back on top of their stacks.
        var bank = state.FavourBank;
        foreach (var tile in state[move.Faction].FavourTiles)
            bank = bank.SetItem(tile.Id, bank[tile.Id].Add(tile));
        return MoveResult.Ok(state with { Players = remaining, TrackArrivals = arrivals, FavourBank = bank });
    }

    // During faction choice, a faction just added may still owe its starting terrain (ice, variable) and then, for the
    // Ice Maidens, a favour: nothing else happens until it has chosen. Null if nothing is owed.
    private static MoveResult? SetupChoiceWaiting(GameState state)
    {
        foreach (var player in state.Players)
        {
            if (player.Kind is FactionKind.Ice or FactionKind.Variable && player.StartingTerrain is null)
                return MoveResult.Rejected(state, $"The {Name(player.Faction)} choose their starting terrain first.");
            if (player.FavoursOwed > 0)
                return MoveResult.Rejected(state, $"The {Name(player.Faction)} choose their favour first.");
        }
        return null;
    }

    // A basic terrain no other faction in the game has taken (as its home or its starting terrain).
    private static MoveResult? CheckStartingTerrain(GameState state, Faction faction, Terrain terrain)
    {
        if (!Terrains.IsBasic(terrain))
            return MoveResult.Rejected(state, $"The starting terrain must be one of the seven basic terrains, not {terrain}.");
        if (state.Players.FirstOrDefault(player => player.Faction != faction
                && (player.Kind == FactionKind.Base ? player.Home : player.StartingTerrain) == terrain) is PlayerState taken)
            return MoveResult.Rejected(state, $"{terrain} is the {(taken.Kind == FactionKind.Base ? "home" : "starting")} terrain of the {Name(taken.Faction)}: choose another.");
        return null;
    }

    // Ice and variable factions choose as soon as they are added; volcano factions once every faction is chosen.
    // The choice sets up what goes with it: the Shapeshifters' home, the Riverwalkers' locked terrains (a priest on
    // each of the other six), the Ice Maidens' favour.
    private static MoveResult ApplyChooseStartingTerrain(GameState state, ChooseStartingTerrain move)
    {
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        var player = state[move.Player];
        if (player.Kind == FactionKind.Base)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} start on their home terrain: they don't choose one.");
        if (player.StartingTerrain is not null)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have already chosen {player.StartingTerrain}.");
        var volcano = player.Kind == FactionKind.Volcano;
        if (volcano && state.Phase != Phase.ChoosingVolcanoTerrain)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} choose their starting terrain once every faction has been chosen.");
        if (volcano && LateTerrainChooser(state) is Faction first && first != move.Player)
            return MoveResult.Rejected(state, $"The {Name(first)} choose first (in the order the factions were chosen).");
        if (volcano && state.IsInGame(Faction.Changelings)
            && (state[Faction.Changelings].TerrainB == move.Terrain || state[Faction.Changelings].TerrainC == move.Terrain))
            return MoveResult.Rejected(state, $"{move.Terrain} is one of the {Name(Faction.Changelings)}' terrains.");
        if (!volcano && state.Phase != Phase.ChoosingFactions)
            return MoveResult.Rejected(state, "Starting terrains are chosen during faction choice.");
        if (CheckStartingTerrain(state, move.Player, move.Terrain) is { } refusal)
            return refusal;

        var chosen = player with { StartingTerrain = move.Terrain };
        if (move.Player == Faction.Shapeshifters)
            chosen = chosen with { ShapeshifterHome = move.Terrain };
        if (move.Player == Faction.Riverwalkers)
        {
            // One priest stays in the supply; the other six go on the wheel, one on every other terrain.
            var priests = player.PriestSupply;
            var locked = Terrains.Wheel.Where(terrain => terrain != move.Terrain)
                .Select((terrain, i) => new WheelPriest(terrain, priests[i + 1]));
            chosen = chosen with { PriestSupply = ValueList.Of(priests[0]), WheelPriests = new ValueList<WheelPriest>(locked) };
        }
        if (move.Player == Faction.IceMaidens)
            chosen = chosen with { FavoursOwed = chosen.FavoursOwed + 1 };
        if (move.Player == Faction.Geologists) // a glowing rock on it from the start
            chosen = chosen with { Rocks = ValueList.Of(move.Terrain) };

        var events = new List<GameEvent>();
        if (move.Player == Faction.IceMaidens)
            events.Add(new FavoursEarned(move.Player, 1));
        var next = state.WithPlayer(chosen);
        if (volcano)
            next = AfterLateTerrains(next, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // Once the factions are confirmed, the volcano faction's starting terrain and the Changelings' B and C, one after
    // the other in the order the factions were chosen: who chooses now (null: nobody left).
    public static Faction? LateTerrainChooser(GameState state) =>
        state.Phase != Phase.ChoosingVolcanoTerrain ? null
            : state.Players.Where(p => p.Kind == FactionKind.Volcano && p.StartingTerrain is null
                    || p.Faction == Faction.Changelings && p.TerrainB is null)
                .Select(p => (Faction?)p.Faction).FirstOrDefault();

    // After a late choice: with nobody left to choose, on to the turn order (Calculator: the map).
    private static GameState AfterLateTerrains(GameState state, List<GameEvent> events)
    {
        if (LateTerrainChooser(state) is not null)
            return state;
        if (state.Options.Calculator)
            return state with { Phase = Phase.ChoosingMap };
        return PlaceLastFactionIfOnlyOneLeft(state with { Phase = Phase.ChoosingTurnOrder }, events);
    }

    private static MoveResult ApplyConfirmFactions(GameState state)
    {
        if (state.Phase == Phase.ChoosingGame)
            return MoveResult.Rejected(state, "Choose the game first.");
        if (state.Phase != Phase.ChoosingFactions)
            return MoveResult.Rejected(state, "The factions have already been confirmed.");
        if (state.Players.Count == 0)
            return MoveResult.Rejected(state, "Choose at least one faction first.");
        if (SetupChoiceWaiting(state) is { } waiting)
            return waiting;
        if (state.Players.Count < 2 && !state.Options.Calculator)
            return MoveResult.Rejected(state, "Choose at least two factions (one faction alone plays in Calculator mode).");

        var events = new List<GameEvent>();
        // Setting up a game in progress: no volcano starting terrain and no turn order (the boards stay in the order
        // the factions were chosen until the turn-order step); the map is next.
        if (state.Options.SetUpInProgress)
            return MoveResult.Ok(state with
            {
                Phase = Phase.ChoosingMap,
                TurnOrder = new ValueList<Faction>(state.Players.Select(player => player.Faction)),
            }, events, MoveResult.NoIssues);
        // A volcano faction now chooses its starting terrain, from those left; then the turn order (Calculator: none,
        // straight to the map).
        if (state.Options.Calculator)
            state = state with { TurnOrder = ValueList.Of(state.Players[0].Faction) };
        if (state.Players.Any(player => player.Kind == FactionKind.Volcano || player.Faction == Faction.Changelings))
            return MoveResult.Ok(state with { Phase = Phase.ChoosingVolcanoTerrain }, events, MoveResult.NoIssues);
        if (state.Options.Calculator)
            return MoveResult.Ok(state with { Phase = Phase.ChoosingMap }, events, MoveResult.NoIssues);
        var next = state with { Phase = Phase.ChoosingTurnOrder };
        next = PlaceLastFactionIfOnlyOneLeft(next, events); // solo: the order is complete at once
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // ---------- Turn order ----------

    private static MoveResult ApplyChooseTurnOrder(GameState state, ChooseTurnOrder move)
    {
        if (state.Phase == Phase.OrderingTurns)
            return OrderTurn(state, move.Faction);
        if (state.Phase != Phase.ChoosingTurnOrder)
            return MoveResult.Rejected(state, "Turn order is not being chosen now.");
        if (!state.IsInGame(move.Faction))
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} are not in the game.");
        if (state.TurnOrder.Contains(move.Faction))
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} already have a place in the turn order.");

        var next = state with { TurnOrder = state.TurnOrder.Add(move.Faction) };
        var events = new List<GameEvent> { new TurnOrderChosen(move.Faction, next.TurnOrder.Count) };
        next = PlaceLastFactionIfOnlyOneLeft(next, events);
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    private static MoveResult ApplyUnchooseTurnOrder(GameState state, UnchooseTurnOrder move)
    {
        if (state.Phase == Phase.OrderingTurns)
            return UnorderTurn(state, move.Faction);
        if (state.Phase != Phase.ChoosingTurnOrder)
            return MoveResult.Rejected(state, "Turn order is not being chosen now.");

        var position = state.TurnOrder.IndexOf(move.Faction);
        if (position < 0)
            return MoveResult.Rejected(state, $"The {Name(move.Faction)} do not have a place in the turn order yet.");
        // Solo: the only place is filled automatically, so there is no order to change.
        if (state.Players.Count == 1)
            return MoveResult.Rejected(state, "With only one faction there is no turn order to choose. Press Proceed to continue, or Undo to change factions.");
        // Removing the last place would leave one faction unplaced, which is immediately placed again.
        // Refuse instead of recording a move that changes nothing.
        if (position == state.Players.Count - 1)
            return MoveResult.Rejected(state, "The last place is filled automatically. Click an earlier faction to change the order.");

        var kept = new ValueList<Faction>(state.TurnOrder.Take(position));
        return MoveResult.Ok(state with { TurnOrder = kept });
    }

    // The VP a faction will start with: Config.StartingVictoryPoints, adjusted while the turn order is chosen.
    public static int StartingVictoryPoints(PlayerState player) => Config.StartingVictoryPoints + player.VictoryPoints;

    private static MoveResult ApplyAdjustStartingVictoryPoints(GameState state, AdjustStartingVictoryPoints move)
    {
        if (state.Phase != Phase.ChoosingTurnOrder)
            return MoveResult.Rejected(state, "Starting VP are set while the turn order is chosen.");
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        var player = state[move.Player];
        var before = StartingVictoryPoints(player);
        var after = before + move.Delta; // a high bid can take it below 0 (Andrew 2026-11-22)
        if (after == before)
            return MoveResult.Rejected(state, "Nothing to change.");
        return MoveResult.Ok(state.WithPlayer(player with { VictoryPoints = after - Config.StartingVictoryPoints }));
    }

    private static MoveResult ApplyConfirmTurnOrder(GameState state)
    {
        if (state.Phase != Phase.ChoosingTurnOrder)
            return MoveResult.Rejected(state, "Turn order is not being chosen now.");
        if (!state.IsTurnOrderComplete)
            return MoveResult.Rejected(state, "Finish choosing the turn order first.");
        return MoveResult.Ok(state with { Phase = Phase.ChoosingMap });
    }

    // ---------- Map, round tiles, pass tiles ----------

    private static MoveResult ApplyChooseMap(GameState state, ChooseMap move)
    {
        if (state.Phase != Phase.ChoosingMap)
            return MoveResult.Rejected(state, "The map is not being chosen now.");

        if (move.Map == MapId.Custom)
            return MoveResult.Rejected(state, "A custom map is made in the custom map editor.");
        return MoveResult.Ok(WithMap(state, move.Map, Maps.ById(move.Map)));
    }

    private static MoveResult ApplyChooseCustomMap(GameState state, ChooseCustomMap move)
    {
        if (state.Phase != Phase.ChoosingMap)
            return MoveResult.Rejected(state, "The map is not being chosen now.");
        if (Maps.FromRows(move.Rows) is not MapDefinition map)
            return MoveResult.Rejected(state, "That is not a map.");
        if (!map.Layout.Equals(Maps.CustomLayout(twelveTop: true)) && !map.Layout.Equals(Maps.CustomLayout(twelveTop: false)))
            return MoveResult.Rejected(state, "A custom map has nine rows of 13 and 12 hexes in turn.");
        if (map.HexTerrain.Any(terrain => terrain != Terrain.River && !Terrains.IsBasic(terrain)))
            return MoveResult.Rejected(state, "A custom map has river and the seven terrains of the wheel only.");
        if (Maps.Shortfall(map.HexTerrain) is { Count: > 0 } missing)
            return MoveResult.Rejected(state, $"Every terrain needs at least {Config.CustomMapMinimumHexes} hexes: "
                + string.Join(", ", missing.Select(part => $"{part.Missing} more {part.Terrain}")) + ".");
        return MoveResult.Ok(WithMap(state, MapId.Custom, map));
    }

    private static GameState WithMap(GameState state, MapId id, MapDefinition map) => state with
    {
        Phase = Phase.ChoosingRoundTiles,
        ChosenMap = id,
        Layout = map.Layout,
        HexTerrain = map.HexTerrain,
        Map = ValueList<Piece?>.Repeat(null, map.Layout.HexCount),
    };

    private static MoveResult ApplyChooseRoundTile(GameState state, ChooseRoundTile move)
    {
        if (state.Phase != Phase.ChoosingRoundTiles)
            return MoveResult.Rejected(state, "Round tiles are not being chosen now.");
        if (move.Tile < 0 || move.Tile >= RoundTiles.Count)
            return MoveResult.Rejected(state, "There is no such round tile.");
        if (state.ChosenRoundTiles.Contains(move.Tile))
            return MoveResult.Rejected(state, "That round tile has already been chosen.");
        if (RoundTiles.All[move.Tile].MiniExpansion && !state.Options.MiniExpansion)
            return MoveResult.Rejected(state, "That round tile is from the mini-expansion, which is not in this game.");
        if (state.ChosenRoundTiles.Count == Config.Rounds)
            return MoveResult.Rejected(state, $"All {Config.Rounds} round tiles have been chosen. Click a numbered tile to change the order.");

        var next = state with { ChosenRoundTiles = state.ChosenRoundTiles.Add(move.Tile) };
        return MoveResult.Ok(next, new RoundTileChosen(move.Tile, next.ChosenRoundTiles.Count));
    }

    private static MoveResult ApplyUnchooseRoundTile(GameState state, UnchooseRoundTile move)
    {
        if (state.Phase != Phase.ChoosingRoundTiles)
            return MoveResult.Rejected(state, "Round tiles are not being chosen now.");
        var position = state.ChosenRoundTiles.IndexOf(move.Tile);
        if (position < 0)
            return MoveResult.Rejected(state, "That round tile has not been chosen.");

        var kept = new ValueList<int>(state.ChosenRoundTiles.Take(position));
        return MoveResult.Ok(state with { ChosenRoundTiles = kept });
    }

    private static MoveResult ApplyConfirmRoundTiles(GameState state)
    {
        if (state.Phase != Phase.ChoosingRoundTiles)
            return MoveResult.Rejected(state, "Round tiles are not being chosen now.");
        if (state.ChosenRoundTiles.Count != Config.Rounds && !state.Options.Calculator) // Calculator: the rest blank
            return MoveResult.Rejected(state, $"Choose a round tile for each of the {Config.Rounds} rounds first.");
        return MoveResult.Ok(state with { Phase = Phase.ChoosingPassTiles });
    }

    private static MoveResult ApplyAddPassTile(GameState state, AddPassTile move)
    {
        if (state.Phase != Phase.ChoosingPassTiles)
            return MoveResult.Rejected(state, "Pass tiles are not being chosen now.");
        if (move.Tile < 0 || move.Tile >= PassTiles.Count)
            return MoveResult.Rejected(state, "There is no such pass tile.");
        if (state.ChosenPassTiles.Contains(move.Tile))
            return MoveResult.Rejected(state, "That pass tile is already in the game.");
        if (PassTiles.All[move.Tile].MiniExpansion && !state.Options.MiniExpansion)
            return MoveResult.Rejected(state, "That pass tile is from the mini-expansion, which is not in this game.");
        // Exactly the rules' number of pass tiles (Calculator: any number).
        if (!state.Options.Calculator && state.ChosenPassTiles.Count >= state.PassTilesNeeded)
            return MoveResult.Rejected(state,
                $"A game with {state.Players.Count} factions has {state.PassTilesNeeded} pass tiles ({Config.ExtraPassTiles} more than the number of factions).");
        return MoveResult.Ok(state with { ChosenPassTiles = state.ChosenPassTiles.Add(move.Tile) });
    }

    private static MoveResult ApplyRemovePassTile(GameState state, RemovePassTile move)
    {
        if (state.Phase != Phase.ChoosingPassTiles)
            return MoveResult.Rejected(state, "Pass tiles are not being chosen now.");
        var position = state.ChosenPassTiles.IndexOf(move.Tile);
        if (position < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the game.");
        return MoveResult.Ok(state with { ChosenPassTiles = state.ChosenPassTiles.RemoveAt(position) });
    }

    // ---------- Start: resources, starting dwellings, first pass tiles ----------

    private static MoveResult ApplyStartGame(GameState state)
    {
        if (state.Phase < Phase.ChoosingPassTiles)
            return MoveResult.Rejected(state, "Finish the setup first.");
        if (state.Phase > Phase.ChoosingPassTiles)
            return MoveResult.Rejected(state, "The game has already started.");
        if (state.Players.Any(player => player.Faction == Faction.Changelings && player.TerrainB is null))
            return MoveResult.Rejected(state, $"The {Name(Faction.Changelings)} choose their B and C terrains first.");
        // Calculator with no pass tiles chosen: every pass tile in this game.
        if (state.Options.Calculator && state.ChosenPassTiles.Count == 0)
            state = state with
            {
                ChosenPassTiles = new ValueList<int>(Enumerable.Range(0, PassTiles.Count)
                    .Where(tile => state.Options.MiniExpansion || !PassTiles.All[tile].MiniExpansion)),
            };
        if (state.Options.Calculator && state.ChosenPassTiles.Count < state.PassTilesNeeded)
            return MoveResult.Rejected(state, $"Choose at least {state.PassTilesNeeded} pass tiles, or none for all of them.");
        if (!state.Options.Calculator && state.ChosenPassTiles.Count != state.PassTilesNeeded)
            return MoveResult.Rejected(state,
                $"Choose {state.PassTilesNeeded} pass tiles ({Config.ExtraPassTiles} more than the number of factions).");

        var events = new List<GameEvent>();
        // The bank: one slot per pass tile in the game, in tile order.
        var next = state with
        {
            PassTileBank = new ValueList<int?>(state.ChosenPassTiles.OrderBy(tile => tile).Select(tile => (int?)tile)),
        };

        // Starting resources (and priests: the Darklings start with one).
        var warnings = new List<Issue>();
        foreach (var player in state.Players)
        {
            var paid = player with
            {
                Coins = player.Coins + Factions.StartingCoins(player.Faction),
                Workers = player.Workers + Factions.StartingWorkers(player.Faction),
                VictoryPoints = player.VictoryPoints + Config.StartingVictoryPoints,
            };
            if (player.Faction == Faction.Firewalkers) // the pyro token 4 behind (never below 0)
                paid = paid with { Pyro = Math.Max(0, paid.VictoryPoints - Config.FirewalkersPyroBehind) };
            next = next.WithPlayer(paid);
            for (var i = 0; i < Factions.StartingPriests(player.Faction); i++)
                next = GainPriest(next, player.Faction, events, warnings);
            if (player.Faction == Faction.DynionGeifr)
                next = DynionStartingFavour(next, player.Faction, events);
            if (state.Options.SetUpInProgress && player.StartingStepsOwed > 0) // set directly instead
                next = next.WithPlayer(next[player.Faction] with { StartingStepsOwed = 0 });
        }

        // Setting up a game in progress: the board opens for editing, the first faction selected.
        if (state.Options.SetUpInProgress)
        {
            next = next with { Phase = Phase.SettingUp, ActivePlayer = state.TurnOrder[0], Round = 1 };
            return MoveResult.Ok(next, events, warnings);
        }

        // Starting dwellings: each player in turn order, then each in reverse turn order; then the Nomads' third;
        // then, in turn order, the Chaos Magicians' only dwelling, the Atlanteans' stronghold and the Djinni's
        // starting steps (TakeStartingSteps).
        var twice = state.TurnOrder.Where(faction => Factions.StartingDwellings(faction) >= 2).ToList();
        var placements = new ValueList<Faction>(twice
            .Concat(Enumerable.Reverse(twice))
            .Concat(state.TurnOrder.Where(faction => Factions.StartingDwellings(faction) >= 3))
            .Concat(state.TurnOrder.Where(faction => Factions.StartingDwellings(faction) == 1 || faction == Faction.Djinni)));
        next = next with
        {
            Phase = Phase.PlacingDwellings,
            PlacementQueue = placements,
            ActivePlayer = placements[0],
        };
        return MoveResult.Ok(next, events, warnings);
    }

    // If exactly one faction has no place yet, it can only go last, so place it.
    private static GameState PlaceLastFactionIfOnlyOneLeft(GameState state, List<GameEvent> events)
    {
        var unplaced = state.Players.Select(p => p.Faction).Where(f => !state.TurnOrder.Contains(f)).ToList();
        if (unplaced.Count != 1)
            return state;

        var next = state with { TurnOrder = state.TurnOrder.Add(unplaced[0]) };
        events.Add(new TurnOrderChosen(unplaced[0], next.TurnOrder.Count));
        return next;
    }

    private static MoveResult ApplyPlaceStartingDwelling(GameState state, PlaceStartingDwelling move)
    {
        var dwelling = move.Dwelling;
        if (state.Phase != Phase.PlacingDwellings)
            return MoveResult.Rejected(state, "Starting dwellings are only placed before round 1.");
        var atlanteans = dwelling.Owner == Faction.Atlanteans; // their stronghold instead
        if (dwelling.Kind != (atlanteans ? PieceKind.Stronghold : PieceKind.Dwelling))
            return MoveResult.Rejected(state, atlanteans ? "The Atlanteans start with their stronghold instead of dwellings." : "That piece is not a dwelling.");
        if (dwelling.Owner != state.ActivePlayer)
            return MoveResult.Rejected(state, $"It is not the {Name(dwelling.Owner)}' turn.");
        if (IsStartingStepsTurn(state))
            return MoveResult.Rejected(state, $"The {Name(dwelling.Owner)} choose the track for their starting steps now.");

        var player = state[dwelling.Owner];
        var bank = player.Bank(dwelling.Kind);
        if (CheckLeftmostInBank(state, bank, dwelling, Buildings.Noun(dwelling.Kind)) is { } bankRefusal)
            return bankRefusal;
        if (!state.Layout.Contains(move.Hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.IsOccupied(move.Hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");

        // Changelings: their A and B dwellings, each on its own terrain.
        var starting = dwelling.Owner == Faction.Changelings ? ChangelingTerrain(player, dwelling)!.Value : player.StartingTerrain ?? player.Home!.Value;
        if (state.HexTerrain[move.Hex] != starting)
            return MoveResult.Rejected(state, $"The {Name(dwelling.Owner)}' starting dwellings must be placed on {starting}.");
        if (dwelling.Owner == Faction.Riverwalkers && !state.Layout.Neighbours(move.Hex).Any(n => state.HexTerrain[n] == Terrain.River))
            return MoveResult.Rejected(state, $"The {Name(dwelling.Owner)} build only next to a river.");

        var fromSlot = bank.IndexOf(dwelling);
        var events = new List<GameEvent>();
        var placed = player.WithBank(dwelling.Kind, bank.SetItem(fromSlot, null));
        if (atlanteans) // their starting town's key token comes once every first structure is down
        {
            placed = placed with { StartingTownOwed = true };
            events.Add(new StartingTownOwed(dwelling.Owner));
        }
        var next = state.WithPlayer(placed);
        // The ice and volcano factions' starting terrain turns into Ice or Volcano under the dwelling.
        if (player.Kind is FactionKind.Ice or FactionKind.Volcano)
            next = ChangeTerrain(next, move.Hex, player.BuildTerrain!.Value, events);
        next = next with
        {
            Map = next.Map.SetItem(move.Hex, dwelling),
            PlacementQueue = next.PlacementQueue.RemoveAt(0),
        };

        return MoveResult.Ok(NextPlacement(next), events, MoveResult.NoIssues);
    }

    // Next placement at once, or, once every starting dwelling is down (and the Djinni's steps taken), the pass tile
    // picks: each player once, in reverse turn order.
    private static GameState NextPlacement(GameState next)
    {
        if (next.PlacementQueue.Count == 0)
            next = next with { Phase = Phase.PickingPassTiles, PlacementQueue = new ValueList<Faction>(next.TurnOrder.Reverse()) };
        return next with { ActivePlayer = next.PlacementQueue[0] };
    }

    // The Djinni's turn in the last placements (their dwellings down): their starting steps, not a dwelling.
    public static bool IsStartingStepsTurn(GameState state) =>
        state.Phase == Phase.PlacingDwellings && state.ActivePlayer is Faction active && state.IsInGame(active)
        && state[active].StartingStepsOwed > 0
        && state.HexesBuiltOnBy(active).Count() >= Factions.StartingDwellings(active);

    // (TakeStartingSteps moves the placements on: Rules.FanFactions.cs.)
    private static GameState AfterStartingSteps(GameState next) =>
        NextPlacement(next with { PlacementQueue = next.PlacementQueue.RemoveAt(0) });

    // After the last pick, coins go on the tiles left in the bank and round 1 begins (with the
    // picked tiles' income).
    private static MoveResult ApplyPickPassTile(GameState state, PickPassTile move)
    {
        if (state.Phase == Phase.Playing)
            return PickAfterPassing(state, move);
        if (state.Phase != Phase.PickingPassTiles)
            return MoveResult.Rejected(state, "Pass tiles are only picked after the starting dwellings, or after passing.");
        if (state.Players.FirstOrDefault(player => player.StartingStepsOwed > 0) is PlayerState stepping)
            return MoveResult.Rejected(state, $"The {Name(stepping.Faction)} choose the track for their starting steps first.");
        if (state.Players.FirstOrDefault(player => player.StartingTownOwed) is PlayerState founding)
            return MoveResult.Rejected(state, $"The {Name(founding.Faction)} take the key token for their starting town first.");
        if (move.Player != state.ActivePlayer)
            return MoveResult.Rejected(state, $"It is the {Name(state.ActivePlayer)}' turn to pick a pass tile.");
        if (state.PassTileBank.IndexOf(move.Tile) < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");

        var events = new List<GameEvent>();
        var next = TakePassTile(state, move.Player, move.Tile, events);
        next = next with { PlacementQueue = next.PlacementQueue.RemoveAt(0) };

        var warnings = new List<Issue>();
        if (next.PlacementQueue.Count == 0)
        {
            next = AddCoinsToBankPassTiles(next);
            next = BeginRound(next, events, warnings);
        }
        else
        {
            next = next with { ActivePlayer = next.PlacementQueue[0] };
        }
        return MoveResult.Ok(next, events, warnings);
    }
}
