using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The controller: the only bridge between Godot (nodes on screen) and Core (rules, history).
// It never changes game data itself. It turns clicks and drags into Moves, hands them to History,
// and redraws the screen from whatever state History says is current.
public partial class Main : Control
{
    private const float MoveSeconds = 0.3f;
    private const float SnapRadius = 40f;                               // screen pixels: how near a drop must land
    private static readonly Vector2 PriestStackOffset = new(14, 0);     // art pixels between stacked priests (side by side)
    private static readonly Vector2 PriestSupplyOffset = new(12, 0);    // art pixels between priests in the supply (in a row)
    private static readonly Color SupplyTint = PlayerPanel.SupplyTint; // priests not yet gained
    private static readonly Color HighlightColour = new(1f, 1f, 0.45f); // tint on legal drop targets
    private static readonly Color UnchosenTint = new(0.4f, 0.4f, 0.4f); // factions left out, during turn order
    // Favour tiles: offsets are in the pixels of the marker they sit on, so scaling the marker in the
    // editor scales the tiles and their spacing together.
    private static readonly Vector2 FavourStackOffset = new(6, -6);  // between copies in one bank stack
    private static readonly Color PastRoundTint = new(0.45f, 0.45f, 0.45f); // round tiles already played
    private static readonly Track[] AllTracks = Enum.GetValues<Track>();
    private static readonly Faction[] AllFactions = Enum.GetValues<Faction>();

    private History _history = new(GameState.Initial); // replaced by Load (MainSaveLoad.cs)

    // One sprite per piece in the game, and one per (faction, track) tracker.
    private readonly Dictionary<Piece, Sprite2D> _pieceSprites = new();
    private readonly Dictionary<(Faction, Track), Sprite2D> _trackerSprites = new();
    private readonly Dictionary<FavourTile, Node2D> _favourSprites = new(); // FavourTileViews
    // Favour tiles still in the bank: the only ones that can be clicked.
    private readonly HashSet<FavourTile> _bankFavours = new();
    // The slide currently moving each node, so a newer one can cancel it.
    private readonly Dictionary<Node2D, Tween> _tweens = new();
    private readonly Dictionary<Node2D, Tween> _scaleTweens = new();
    // Where each visible panel is heading this render (Board coordinates). Pieces on a panel aim here too.
    private readonly Dictionary<PlayerPanel, Vector2> _panelTargets = new();

    private Node2D _factionSelect;
    private Node2D _board;
    private PlayerPanel[] _panels;
    // Where the panels go, top first: where PlayerPanel0, 1, 2, ... are placed in the editor (read before anything
    // moves them). The panel at the top of the turn stack goes to the first, and so on.
    private Vector2[] _panelSlots;
    private MapView _map;
    private ZoomableZone _priestZones; // small until a priest is picked up or the zones are clicked
    private Node2D _tracks;
    private Node2D _pieceLayer;
    private Node2D _favourBank;

    // Setup screens, one per setup phase (the faction screen also does turn order).
    private CanvasItem _gameSelect;
    private CanvasItem _mapSelect;
    private Node2D _roundTileSelect; // Tile0..Tile8, each a tile_choice.tscn
    private Node2D _passTileSelect;  // Tile0..Tile9
    // On the board: the six round tiles beside the map, and the pass tile bank.
    private Node2D _roundTileRow;    // Round1..Round6
    private Node2D _passTileBank;    // Slot0..Slot9, and Highlight
    private readonly Dictionary<int, Node2D> _passTileSprites = new(); // PassTileViews
    private readonly HashSet<int> _bankPassTiles = new(); // pass tiles in the bank: the only clickable ones
    private readonly Node2D[] _roundTileSprites = new Node2D[Config.Rounds]; // RoundTileViews (a blank tile for a blank round)
    private readonly int?[] _roundTileShown = new int?[Config.Rounds]; // which tile each of those shows

    // Drag state: the piece being dragged (null when none), where it may go, and the grab offset.
    private Piece? _dragged;
    private PieceOptions _dragOptions;
    private Vector2 _grabOffset;

    // The hex-click menus: the colour wheel (what the hex becomes, with its cost), then "Build dwelling?" (Yes / No).
    private ColourWheel _wheel;                       // %ColourWheel if the scene has one, else made in code
    private ValueList<TransformChoice> _wheelChoices; // what the wheel's hex can become now
    private Faction _wheelPlayer;

    private int? _pendingHex;        // the hex the colour wheel is about

    // A worker being dragged from a worker count to a coin count (a picture only; not a game piece).
    private Sprite2D _draggedWorker;
    private PlayerPanel _workerDragPanel;

    // Whether the piece being dragged is a priest that could also be dropped on the worker count.
    private bool _dragCanBecomeWorker;

    // A bonus action whose octagon has been clicked, waiting for a target; null when none.
    private (Faction Faction, BonusActionId Bonus)? _bonus;
    private PieceOptions _bonusOptions;

    // The building currently drawn see-through because the mouse is over it.
    private Sprite2D _hoveredBuilding;

    // Pieces currently sitting on a panel (bank or priest stack): the only ones that can be picked up.
    private readonly HashSet<Piece> _panelPieces = new();
    private readonly Fader _fader = new();

    // Another player's panel shown in full for a look, in place (null: none).
    private PlayerPanel _inspected;
    // The panel drawn above the others: the one inspected. It stays on top after shrinking back (so it
    // never drops under its neighbours on the way) until the game state next changes.
    private PlayerPanel _raised;
    private GameState _raisedState;
    private const int RaisedPanelZ = 50; // above every piece on the board...
    private const int RaisedPieceZ = 60; // ...and its own pieces above it (still under a dragged piece, at 100)
    // Every open (full-size) panel lies over the collapsed ones and their pieces, its own pieces over it.
    private const int OpenPanelZ = 30, OpenPieceZ = 35;

    public override void _Ready()
    {
        ReadyBackground(); // the background rectangle behind everything, and its colour picker (MainBackground.cs)
        _factionSelect = GetNode<Node2D>("%FactionSelect");
        _board = GetNode<Node2D>("Board");
        _wheel = GetNodeOrNull<ColourWheel>("%ColourWheel");
        if (_wheel is null)
        {
            _wheel = new ColourWheel { Name = "ColourWheel", ZIndex = ColourWheelZIndex };
            _board.AddChild(_wheel);
        }
        _wheel.Visible = false;
        _wheel.TerrainChosen += OnWheelTerrainChosen;
        _panels = new[]
        {
            GetNode<PlayerPanel>("%PlayerPanel0"),
            GetNode<PlayerPanel>("%PlayerPanel1"),
            GetNode<PlayerPanel>("%PlayerPanel2"),
            GetNode<PlayerPanel>("%PlayerPanel3"), // for a 4th player
            GetNode<PlayerPanel>("%PlayerPanel4"), // for a 5th player
        };
        _panelSlots = _panels.Select(panel => panel.Position).ToArray();
        _map = GetNode<MapView>("%Map");
        _priestZones = GetNode<ZoomableZone>("%PriestZones");
        _tracks = GetNode<Node2D>("%Tracks");
        _pieceLayer = GetNode<Node2D>("%Pieces");
        _favourBank = GetNode<Node2D>("%FavourBank");
        _gameSelect = GetNode<CanvasItem>("%GameSelect");
        _mapSelect = GetNode<CanvasItem>("%MapSelect");
        _roundTileSelect = GetNode<Node2D>("%RoundTileSelect");
        _passTileSelect = GetNode<Node2D>("%PassTileSelect");
        _roundTileRow = GetNode<Node2D>("%RoundTiles");
        _passTileBank = GetNode<Node2D>("%PassTileBank");

        // The tile choices always show the same tiles, so their views are made once.
        for (var tile = 0; tile < RoundTiles.Count; tile++)
            SetUpTileChoice(TileChoice(_roundTileSelect, tile), RoundTileView.Create(tile));
        for (var tile = 0; tile < PassTiles.Count; tile++)
            SetUpTileChoice(TileChoice(_passTileSelect, tile), PassTileView.Create(tile));

        // The large setup portraits always show the same factions, so they are made once (MainFactions.cs).
        BuildFactionSelect();

        ReadyTopPanel(); // the bar over the map: messages, log, history keys (MainTopPanel.cs)
        ReadyStepButton(); // the top panel's game button (MainStepButton.cs)
        ReadyNeutralBank(); // Calculator's neutral opponent (MainNeutral.cs)
        ReadyGameOptions(); // the game's square and the checkboxes under it; the step button's tick chooses the game
        ReadyMapChoices(); // the miniature maps on the map screen (MainMaps.cs)

        // Each panel's player button ends the turn of whichever faction that panel is showing.
        foreach (PlayerPanel panel in _panels)
            panel.PlayerButton.Pressed += () =>
            {
                if (panel.ShownFaction is not Faction faction)
                    return;
                if (DeclineLandscapeOffers(faction)) // what a special landscape offers, not taken (MainLandscapes.cs)
                    return;
                if (OnFanFactionsTick(faction)) // the Treasurers' Safe, the Wisps' free dwelling (MainFanFactions.cs)
                    return;
                Submit(new EndTurn(faction));
            };
        // A lit terrain on a panel's terrain wheel (Fire & Ice): the Riverwalkers' priest choice, the Shapeshifters' shift.
        foreach (PlayerPanel panel in _panels)
            if (panel.TerrainWheel is ColourWheel terrainWheel)
                terrainWheel.TerrainChosen += terrain => OnPanelTerrainChosen(panel, terrain);
        ReadyHistory(); // the arrows, the bookmark button and the bookmark map (MainHistory.cs)
        ReadyNotes();   // the map's Open notes and Quit keys (MainNotes.cs)
        ReadySaveLoad(); // saving, loading, the game line: after the bookmark map (MainSaveLoad.cs)
        ReadyBgaImport(); // the bookmarklet and Load BGA JSON: after saving and loading (MainBgaImport.cs)

        _map.Build(_history.Current.Layout, _history.Current.ChosenMap); // built again when the map is chosen (Render)
        DressCults(); // the tracks' and priest zones' pictures, and the padlocks (MainCults.cs)
        LabelPriestSlots();
        ReadyTownsAndPower(); // key tokens, power actions, bridges, power offers (MainTownsAndPower.cs)
        Render(animate: false);
    }

    // ---------- Moves ----------

    private void Submit(Move move)
    {
        if (InterceptFanMove(move)) // the Changelings' returning dwelling: where it goes, first (MainFanFactionsEmberGeologistsChangelings.cs)
            return;
        EndBonus(); // whatever was being chosen no longer applies to the new state
        CancelPanelSteps(); // a turn's panels still changing: straight to the end
        GameState before = _history.Current;
        MoveResult result = _history.Apply(move);
        ShowIssues(result.Issues); // errors for refused moves, warnings (e.g. debt) for accepted ones
        List<Gain> gains = TakeGainsInFlight(result, before); // the counts wait for their lights (MainPoints.cs)
        Render(animate: true);
        QueueGains(gains);
        FlyRocks(result); // the Geologists' rock onto their wheel (MainFanFactionsEmberGeologistsChangelings.cs)
    }

    private void ShowIssues(IEnumerable<Issue> issues) =>
        ShowNote(string.Join("\n", issues.Select(issue => issue.Message)));

    // The step button's tick on a setup screen (once called Proceed) confirms whatever it chooses.
    private void OnProceed()
    {
        switch (_history.Current.Phase)
        {
            case Phase.ChoosingGame when _gameChosen: Submit(new ChooseGame(GameKind.TerraMystica, ChosenOptions())); break;
            case Phase.ChoosingFactions: Submit(new ConfirmFactions()); break;
            case Phase.ChoosingTurnOrder: Submit(new ConfirmTurnOrder()); break;
            case Phase.ChoosingRoundTiles: Submit(new ConfirmRoundTiles()); break;
        }
    }

    // The move a click on a setup screen makes, if it lands on something clickable:
    //   choosing factions: toggle the faction in or out;
    //   choosing turn order: give it the next place, or take its place away (and every later one);
    //   choosing round tiles: the same as turn order, for rounds;
    //   choosing pass tiles: toggle the tile in or out.
    private Move SetupMoveAt(Vector2 point)
    {
        GameState state = _history.Current;
        if (_setupWheel.Visible && _setupWheel.IsOver(point))
            return null; // its lit terrains take their own clicks; the rest of it does nothing
        switch (state.Phase)
        {
            case Phase.ChoosingFactions when _favourOverlay.Visible:
                return OverlayFavourUnder(point) is int favour && state.Players.FirstOrDefault(p => p.FavoursOwed > 0) is PlayerState owing
                    ? new TakeFavour(owing.Faction, favour)
                    : null;
            case Phase.ChoosingFactions when ChoiceUnder(point) is Faction faction:
                return state.IsInGame(faction) ? new RemovePlayer(faction) : new AddPlayer(faction);
            case Phase.ChoosingTurnOrder when ChoiceUnder(point) is Faction faction:
                return state.TurnOrder.Contains(faction) ? new UnchooseTurnOrder(faction) : new ChooseTurnOrder(faction);
            case Phase.ChoosingRoundTiles when TileChoiceUnder(_roundTileSelect, RoundTiles.Count, point) is int tile:
                return state.ChosenRoundTiles.Contains(tile) ? new UnchooseRoundTile(tile) : new ChooseRoundTile(tile);
            case Phase.ChoosingPassTiles when TileChoiceUnder(_passTileSelect, PassTiles.Count, point) is int tile:
                return state.ChosenPassTiles.Contains(tile) ? new RemovePassTile(tile) : new AddPassTile(tile);
            case Phase.ChoosingMap when _mapChoices.MapAt(point) is MapId map:
                return new ChooseMap(map);
            default:
                return null;
        }
    }

    // What a faction's player button shows (see PlayerButton): blank at the start of the turn; underway (Waiting) once
    // the action has begun (a move made, or a bonus action's octagon clicked and waiting for its target); the tick once
    // the turn can end. choosing: a bonus action of this faction's is waiting for its target.
    private static PlayerButton.Mode PlayerButtonMode(GameState state, Faction faction, bool choosing)
    {
        // Fan factions: the Treasurers' Safe (everyone waits), the Wisps' free dwelling (MainFanFactions.cs).
        if (state.Options.FanFactions && FanPlayerButtonMode(state, faction) is PlayerButton.Mode fan)
            return fan;
        // A special landscape's offers waiting (and nothing else): the tick gives them up (MainLandscapes.cs).
        if (!choosing && OnlyLandscapeOffersWaiting(state, faction))
            return PlayerButton.Mode.Confirm;
        // Calculator: it is always their turn, so the tick (End turn) only shows once they have passed; a new action
        // after a finished one does the rest of End turn's work (Core).
        if (state.Options.Calculator && state.Phase == Phase.Playing && state.ActivePlayer == faction && !state.HasPassed(faction))
            return choosing || Rules.IsMidAction(state, faction) ? PlayerButton.Mode.Waiting : PlayerButton.Mode.Blank;
        // The turn-order step: dots on the boards still to be placed, a tick on those placed (MainModes.cs).
        if (state.Phase == Phase.OrderingTurns)
            return state.HasPassed(faction) ? PlayerButton.Mode.Hidden
                : state.StartOrder?.Contains(faction) ?? false ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting;
        if (state.Phase != Phase.Playing || state.ActivePlayer != faction)
            return PlayerButton.Mode.Hidden;
        if (Rules.Apply(state, new EndTurn(faction)).Accepted && !choosing)
            return PlayerButton.Mode.Confirm;
        return state.ActionTaken || choosing || Rules.IsMidAction(state, faction) ? PlayerButton.Mode.Waiting : PlayerButton.Mode.Blank;
    }

    // ---------- Drawing ----------

    // While the priest zones grow or shrink, the priests in them follow every frame (instead of sliding),
    // and once more on the frame after, so they end exactly where the zones end.
    private bool _zonesWereAnimating;

    public override void _Process(double delta)
    {
        UpdatePointLights(); // victory points on their way (MainPoints.cs)
        bool animating = _priestZones.IsAnimating;
        if (!animating && !_zonesWereAnimating)
            return;
        _zonesWereAnimating = animating;
        GameState state = _history.Current;
        foreach (Track track in AllTracks)
        {
            ValueList<Piece?> zone = state.PriestZones[(int)track];
            for (var slot = 0; slot < zone.Count; slot++)
            {
                if (zone[slot] is not Piece priest || !_pieceSprites.TryGetValue(priest, out Sprite2D sprite))
                    continue;
                StopSlide(sprite);
                StopResize(sprite);
                sprite.Position = _pieceLayer.ToLocal(PriestSlotNode(track, slot).GlobalPosition);
                sprite.Scale = Vector2.One; // full size, however big the zones are
                sprite.ZIndex = PriestInZoneZ;
            }
        }
        // Calculator's neutral priests follow their slots too (MainNeutral.cs).
        foreach (var ((track, slot), sprite) in _neutralPriests)
            sprite.GlobalPosition = PriestSlotNode(track, slot).GlobalPosition;
    }

    // Redraws everything from the current state. animate: true slides things to their new places;
    // false places them instantly (for future bookmark jumps).
    private void Render(bool animate)
    {
        GameState state = _history.Current;

        // Before Start: one setup screen per phase. From Start on: the board.
        bool boardShown = state.Phase >= Phase.PlacingDwellings;
        // The board's first showing: everything in place at once (the active board already open, not opening).
        if (boardShown && !_board.Visible)
            animate = false;
        _gameSelect.Visible = state.Phase == Phase.ChoosingGame;
        _factionSelect.Visible = state.Phase is Phase.ChoosingFactions or Phase.ChoosingVolcanoTerrain or Phase.ChoosingTurnOrder;
        _mapSelect.Visible = state.Phase == Phase.ChoosingMap;
        RenderCustomMap(state); // the custom map editor or the six maps (MainCustomMap.cs)
        _roundTileSelect.Visible = state.Phase == Phase.ChoosingRoundTiles;
        _passTileSelect.Visible = state.Phase == Phase.ChoosingPassTiles;
        _board.Visible = boardShown;

        RenderControls(state);
        if (!_map.IsBuiltFor(state.Layout, state.ChosenMap))
            _map.Build(state.Layout, state.ChosenMap); // another map chosen (or unchosen by undo or a jump)
        // The Kingdom of Ember's lava flow while its path is chosen: the map, its lava and the pieces as the flow so far
        // leaves them (MainFanFactionsEmberGeologistsChangelings.cs); everything else from the game as it is.
        GameState shown = FlowShown(state);
        _flowStep = NewFlowStep(shown, animate); // a step of the flow just clicked or taken back: animated its own way
        // A transformed hex turns over (MapView's transform animation); the flow's hexes change as its stack lands.
        _map.ShowTerrain(shown.HexTerrain, animate, _flowStep?.Switched, SlideSeconds);
        RenderFactionSelect(state);
        RenderGameSelect(); // MainGameSelect.cs
        RenderFinalTile(state);
        RenderTileSelects(state);
        RenderRoundTiles(state);
        RenderReach(state);
        RenderPanels(state, animate); // before pieces: pieces on a panel need to know where it is going
        RenderPieces(shown, animate);
        AnimateFlowStep(shown); // the flowing structure: carried on its stack, then dropped (MainFanFactionsEmberGeologistsChangelings.cs)
        RenderFanMap(shown, animate); // lava, the Wyrm's tokens, the pyro (MainFanFactionsOnMap.cs)
        RenderFavours(state, animate);
        RenderTownsAndPower(state, animate);
        RenderFactionAbilities(state);
        RenderFanFactions(state); // MainFanFactions.cs
        RenderBuildOffer(state); // after the pieces: it lights the leftmost dwelling's sprite
        RenderLandscapes(state); // after the build offer: the landscape token's light, the Witches' choice (MainLandscapes.cs)
        RenderPassTiles(state, animate);
        RenderTrackers(state, animate);
        LabelPriestSlots(state);
        RenderHistory(); // the history buttons, and the bookmark map if open (MainHistory.cs)
        RenderConversions(state); // the conversions under bowl III, if open: which can be made now (MainPower.cs)
        RenderSafeEditor(state);  // Setup: the Treasurers' Safe editor, if open (MainSafeEditor.cs)
        UpdatePowerHover(GetGlobalMousePosition()); // what is clickable may have changed
    }

    private void RenderControls(GameState state)
    {
        SetInstruction(WithGameLine(TopLine(state))); // the top panel's top line (MainTopPanel.cs; Setup and Free: MainModes.cs; game line: MainSaveLoad.cs)

        RenderStepButton(state); // MainStepButton.cs
    }

    // What has to happen next, and who does it: "<Faction> must …" when something is owed before the game can go on,
    // "<Faction> may …" when it can be left (the step button's confirm, or a click elsewhere, leaves it). At the start of
    // a turn: "Round X of 6: Take an action (or click pass tile)"; once the turn can end: "Click confirm to end turn".
    private string Instruction(GameState state)
    {
        string Who(Faction? faction) => NameOf(faction);
        PlayerState Owing(Func<PlayerState, bool> owes) => state.Players.FirstOrDefault(owes);
        PlayerState me = state.ActivePlayer is Faction active ? state[active] : null;
        string round = $"Round {state.Round} of {Config.Rounds}";
        if (state.Options.FanFactions && FanInstruction(state) is string fan) // MainFanFactionsEmberGeologistsChangelings.cs
            return fan;
        switch (state.Phase)
        {
            case Phase.ChoosingGame:
                return _gameChosen ? "Click confirm to start with these options" : "Choose a game";
            case Phase.ChoosingFactions when TerrainChooser(state) is PlayerState choosing:
                return $"{Who(choosing.Faction)} must choose their starting terrain: click one on the wheel";
            case Phase.ChoosingFactions when Owing(p => p.FavoursOwed > 0) is PlayerState owing:
                return $"{Who(owing.Faction)} must choose a favour: click one";
            case Phase.ChoosingFactions:
                return Rules.Apply(state, new ConfirmFactions()).Accepted ? "Choose more factions, or click confirm" : "Choose factions";
            case Phase.ChoosingVolcanoTerrain:
                return $"{Who(TerrainChooser(state)?.Faction)} must choose their starting terrain: click one on the wheel";
            case Phase.ChoosingTurnOrder:
                return state.IsTurnOrderComplete ? "Turn order set: click confirm" : "Click the factions in turn order";
            case Phase.ChoosingMap:
                return _customMapOpen ? CustomMapInstruction() : "Choose a map";
            case Phase.ChoosingRoundTiles:
                return state.ChosenRoundTiles.Count == Config.Rounds
                    ? "Round tiles set: click confirm"
                    : $"Click the round tiles in round order ({state.ChosenRoundTiles.Count} of {Config.Rounds})"
                      + (state.Options.Calculator && state.ChosenRoundTiles.Count == 0 ? " - or proceed without" : "");
            case Phase.ChoosingPassTiles:
                return state.ChosenPassTiles.Count >= state.PassTilesNeeded
                    ? $"Click more pass tiles, or click confirm to start ({state.ChosenPassTiles.Count} chosen)"
                    : $"Click the pass tiles in this game ({state.ChosenPassTiles.Count} chosen, at least {state.PassTilesNeeded})"
                      + (state.Options.Calculator && state.ChosenPassTiles.Count == 0 ? " - or proceed with all" : "");
            case Phase.PlacingDwellings:
                return $"{Who(state.ActivePlayer)} must place a starting dwelling";
            case Phase.PickingPassTiles:
                return $"{Who(state.ActivePlayer)} must pick a pass tile";
            case Phase.Finished:
                return $"Game over. {WinnerText(state.Winners)}";
            case Phase.Playing or Phase.RoundEndSpades when Owing(p => p.PriestChoicesOwed > 0) is PlayerState walkers:
                return $"{Who(walkers.Faction)} must unlock a terrain (click a lit one on their wheel) or take the priest (click anywhere else)";
            case Phase.RoundEndSpades when me is not null && me.CultStepsOwed > 0:
                return $"End of round {state.Round}: {Who(me.Faction)} must take {me.CultStepsOwed} cult step(s): click a track";
            case Phase.RoundEndSpades when me is not null:
                return $"End of round {state.Round}: {Who(me.Faction)} may use {me.SpadesOwed} free spade(s): click a hex (or click confirm to stop)";
            case Phase.Playing when me is not null:
                break;
            default:
                return "";
        }

        // Playing: whatever is owed first, then the turn itself.
        if (Owing(p => p.TokenOfferOwed) is PlayerState shifters)
            return $"{Who(shifters.Faction)} may pay 1 VP for a new power token in bowl III: click the lit bowl to accept, anywhere else to decline";
        if (Owing(p => p.FavoursOwed > 0) is PlayerState favoured)
            return $"{Who(favoured.Faction)} must choose a favour";
        if (me.CultStepsToPay > 0)
            return $"{Who(me.Faction)} must pay {me.CultStepsToPay} steps for the Volcano: click a lit track";
        if (LandscapeInstruction(state, me) is string landscape) // MainLandscapes.cs
            return landscape;
        if (state.SpadesPending && me.SpadesOwed > 0)
            return $"{Who(me.Faction)} may use {me.SpadesOwed} free spade(s): click a hex (or click confirm to stop)";
        if (state.SpadesPending)
            return $"{Who(me.Faction)} may build a dwelling: click a highlighted hex, or drag their dwelling onto one (anywhere else: no)";
        if (me.BridgesOwed > 0 && me.BridgesOptional)
            return $"{Who(me.Faction)} may place {me.BridgesOwed} free bridge(s): drag one from their supply to a highlighted spot (or click confirm to stop)";
        if (me.BridgesOwed > 0)
            return $"{Who(me.Faction)} must place their bridge: drag it from their supply to a highlighted spot";
        if (Rules.FormableTowns(state, me.Faction).Count > 0)
            return $"{Who(me.Faction)} must form a town: drag a key token onto a highlighted hex";
        if (state.PowerOffers.Count > 0)
            return $"{Who(state.PowerOffers[0].Player)} may gain power: click the chip to accept, anywhere else on the map to decline";
        if (Owing(p => p.CultStepsOwed > 0) is PlayerState stepper)
            return $"{Who(stepper.Faction)} must take a cult step: click a track";
        if (me.PassTileOwed)
            return $"{Who(me.Faction)} must pick a new pass tile from the bank";
        if (me.PriestExchangesLeft > 0)
            return $"{Who(me.Faction)} may exchange up to {me.PriestExchangesLeft} more workers for priests: drag workers onto their priests (or click confirm when done)";
        if (state.ActionsOwed == 2)
            return $"{round}: {Who(me.Faction)} take two actions: take the first (or click pass tile)";
        if (state.ActionsOwed == 1 && !state.ActionTaken)
            return $"{Who(me.Faction)} may take their second action (or click confirm to end turn)";
        if (RiverTownsNow(state).Count > 0)
            return $"{(state.ActionTaken ? "" : round + ": ")}{Who(me.Faction)} may form a river town: drag a key token onto a highlighted river hex";
        if (state.ActionTaken && Rules.Apply(state, new EndTurn(me.Faction)).Accepted)
            return state.Options.Calculator && !state.HasPassed(me.Faction)
                ? $"{round}: Take an action (or click pass tile)" // Calculator: no turn to end until passing
                : "Click confirm to end turn";
        if (state.ActionTaken)
            return $"{Who(me.Faction)} must finish their action";
        return $"{round}: Take an action (or click pass tile)";
    }

    private static string WinnerText(ValueList<Faction> winners) =>
        winners.Count == 1
            ? $"The {NameOf(winners[0])} win!"
            : $"Tie between the {string.Join(" and the ", winners.Select(winner => NameOf(winner)))}.";
}
