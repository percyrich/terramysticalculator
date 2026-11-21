using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the mouse. Presses and releases, hovering, dragging pieces and workers, inspecting another
// player's panel, and where things are on screen. A file of the same class (partial).
public partial class Main
{
    // ---------- Mouse input ----------

    // _Input sees every input event before the buttons do. It only claims a press that lands on a
    // setup portrait, a power token, a piece or a hex; anything else carries on to the buttons as normal.
    public override void _Input(InputEvent @event)
    {
        if (_historyOverlay?.IsRenaming == true) // typing a bookmark's name: every key belongs to its text box
            return;
        if (OnModalInput(@event)) // a message box is open: Esc closes it, everything else is its own (MainBgaImport.cs)
            return;
        if (OnEndgameInput(@event)) // the end of the game's scores: the same (MainEndgame.cs)
            return;
        if (OnSaveLoadInput(@event)) // Ctrl+S, Ctrl+Shift+S, Ctrl+O (MainSaveLoad.cs)
            return;
        if (OnHistoryInput(@event)) // shortcuts; while the bookmark map is open, nothing below (MainHistory.cs)
            return;
        if ((_djinniSwapping || _goblinsChoosing || _flowFrom is not null) && @event.IsActionPressed("ui_cancel")) // Esc: the book's choice, the flow
        {
            GetViewport().SetInputAsHandled();
            EndFanChoices();
            ShowPrompt("");
            Render(animate: true);
            return;
        }
        if (_bonus is not null && @event.IsActionPressed("ui_cancel")) // Esc
        {
            GetViewport().SetInputAsHandled();
            CancelBonus();
            return;
        }
        if (_inspected is not null && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            EndInspection();
            return;
        }
        if (_wheel.Visible && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            CloseWheel();
            return;
        }
        if (_upgradeChoice is not null && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            CloseUpgradeChoice();
            return;
        }
        if (_safeEditPanel is not null && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            CloseSafeEditor();
            return;
        }
        if (_conversionPanel is not null && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            CloseConversions();
            return;
        }
        if (@event.IsActionPressed("ui_cancel") && CancelKeyOrBridgeSelection())
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        // The power conversions open: a press outside the menu closes it and then does what it would have done anyway
        // (bowl III's own press only closes it).
        if (_conversionPanel is PlayerPanel converting && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            && !converting.PowerBank.IsOverConversions(GetGlobalMousePosition()))
        {
            bool onBowl = converting.PowerBank.IsOverBowl(3, GetGlobalMousePosition());
            CloseConversions();
            if (onBowl)
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (OnModeInput(@event)) // Setup, the turn-order step, priest debt (MainModes.cs)
            return;
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed)
                OnPress();
            else if (_dragged is Piece released)
                EndDrag(released);
            else if (_draggedWorker is not null)
                EndWorkerDrag();
            else if (_draggedCoin is not null)
                EndCoinDrag();
            else
                EndKeyOrBridgeDrag();
        }
        else if (@event is InputEventMouseMotion)
        {
            if (_dragged is Piece dragged)
                _pieceSprites[dragged].GlobalPosition = GetGlobalMousePosition() + _grabOffset;
            else if (_draggedWorker is not null)
                _draggedWorker.GlobalPosition = GetGlobalMousePosition();
            else if (_draggedCoin is not null)
                _draggedCoin.GlobalPosition = GetGlobalMousePosition();
            else if (MoveKeyOrBridgeDrag(GetGlobalMousePosition()))
            {
            }
            else
            {
                UpdatePowerHover(GetGlobalMousePosition());
                UpdateHover(GetGlobalMousePosition());
            }
        }
    }

    private void OnPress()
    {
        // Score badges (MainScorePreview.cs): a click anywhere but on one closes every score preview first.
        PlayerPanel badge = ScoreBadgeUnder(GetGlobalMousePosition());
        if (badge is null)
            CloseScorePreviews();
        if (IsOverTopPanel())
            return; // the top panel's keys, game button and log work through their own signals (MainTopPanel.cs)
        // The colour wheel takes clicks on its circles itself (before this); any other click closes it.
        if (_wheel.Visible)
        {
            CloseWheel();
            return;
        }
        // The buildings a clicked building could become: one clicked upgrades to it; any other click closes them.
        if (_upgradeChoice is not null)
        {
            OnPressUpgradeChoice(GetGlobalMousePosition());
            return;
        }
        // The power conversions under bowl III: one that can be made converts (the menu stays); any other click closes it.
        if (_conversionPanel is not null)
        {
            OnPressConversions(GetGlobalMousePosition());
            return;
        }
        if (AnyMenuOpen)
            return; // an open menu handles its own clicks

        Vector2 mouse = GetGlobalMousePosition();

        // A score badge: opens (or closes) its score preview, and nothing else.
        if (badge is not null)
        {
            GetViewport().SetInputAsHandled();
            ToggleScorePreview(badge);
            return;
        }

        if (!_board.Visible)
        {
            if (_gameSelect.Visible && OnPressGameSelect(mouse)) // the game and the final scoring tile (MainGameSelect.cs)
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            if (SetupMoveAt(mouse) is Move setupMove)
            {
                GetViewport().SetInputAsHandled();
                Submit(setupMove);
            }
            return; // buttons (the step button) work through their own signals
        }

        if (OnPressBuildOffer(mouse)) // a dwelling on a hex just made home terrain, or not
            return;
        if (OnPressOwedChoice(mouse)) // the Riverwalkers' priest, the Shapeshifters' token (MainFireAndIce.cs)
            return;
        if (OnPressTerrainWheel(mouse)) // opening the Riverwalkers' wheel for a look, or closing it
            return;
        if (OnPressLandscapes(mouse)) // the Witches' town bonus, the Chaos Portal (MainLandscapes.cs)
            return;
        if (OnPressFanFactions(mouse)) // the Safe, the Djinni's tracks, the Goblins' rows, the Wisps' Lake (MainFanFactions.cs)
            return;

        // An inspected panel: clicks on it only look (its buttons still work); a click anywhere else
        // shrinks it back, and is then handled as usual.
        if (_inspected is not null)
        {
            if (_inspected.IsOver(mouse))
                return;
            EndInspection();
        }

        // The priest zones: a click on them while small only makes them full size; a click anywhere else
        // shrinks them, and is then handled as usual.
        bool overZones = _priestZones.IsOver(mouse);
        if (!_priestZones.IsExpanded && overZones)
        {
            GetViewport().SetInputAsHandled();
            _priestZones.Expand();
            return;
        }
        if (_priestZones.IsExpanded && !overZones)
            _priestZones.Shrink();

        // A power offer waiting on the map: its chip accepts it (gaining the power, paying the VP); a click anywhere
        // else on the map declines it. Either way the next offer (if any) then shows.
        if (_history.Current.PowerOffers.Count > 0)
        {
            Faction offered = _history.Current.PowerOffers[0].Player;
            if (_map.IsOverCoinChip(mouse)) // the Nomads' coin instead (their Trading Camp)
            {
                GetViewport().SetInputAsHandled();
                Submit(new TakeCoinInstead(offered));
                return;
            }
            if (_map.IsOverOfferChip(mouse))
            {
                GetViewport().SetInputAsHandled();
                Submit(new AcceptPower(offered));
                return;
            }
            if (_map.HexAt(mouse) is int)
            {
                GetViewport().SetInputAsHandled();
                Submit(new DeclinePower(offered));
                return;
            }
        }

        // Another player's compact panel: a click opens it for a look (not while a bonus action waits for
        // its target).
        if (_bonus is null && InspectablePanelUnder(mouse) is PlayerPanel compactPanel)
        {
            GetViewport().SetInputAsHandled();
            // In Free play a board's faction is selected instead (MainModes.cs).
            if (_history.Current.IsPlanning && compactPanel.ShownFaction is Faction chosen)
                Submit(new SelectFaction(chosen));
            else
                Inspect(compactPanel);
            return;
        }

        if (OnPressFactionAbilities(mouse)) // book actions, cult steps, the Alchemists' VP and coins (MainFactions.cs)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (BonusActionUnder(mouse) is { } octagon)
        {
            GetViewport().SetInputAsHandled();
            OnBonusActionClicked(octagon.Faction, octagon.Bonus);
            return;
        }
        if (_bonus is not null)
        {
            OnBonusTargetClicked(mouse); // while choosing a target, nothing else on the board responds
            return;
        }
        if (OnPressTownsAndPower(mouse)) // key tokens, power actions, bridges
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (OnPressPowerBowls(mouse)) // bowl III: the conversions; bowl II: burn (MainPower.cs)
        {
            GetViewport().SetInputAsHandled();
        }
        else if (FavourUnder(mouse) is int favour)
        {
            GetViewport().SetInputAsHandled();
            OnFavourClicked(favour);
        }
        else if (PassTileUnder(mouse) is int passTile)
        {
            GetViewport().SetInputAsHandled();
            OnPassTileClicked(passTile);
        }
        else if (PieceUnder(mouse) is Piece piece)
        {
            GetViewport().SetInputAsHandled();
            TryStartDrag(piece, mouse);
        }
        else if (UpgradeMoveAt(mouse) is Move upgrade)
        {
            GetViewport().SetInputAsHandled();
            Submit(upgrade); // refused with a reason if not possible now
        }
        else if (WorkerZoneUnder(mouse) is PlayerPanel workerPanel)
        {
            GetViewport().SetInputAsHandled();
            TryStartWorkerDrag(workerPanel, mouse);
        }
        else if (PriestSlotAt(mouse) is { } priestSlot)
        {
            GetViewport().SetInputAsHandled();
            OnPriestSlotClicked(priestSlot.Track, priestSlot.Slot);
        }
        else if (_map.HexAt(mouse) is int hex)
        {
            GetViewport().SetInputAsHandled();
            OnHexClicked(hex);
        }
    }

    // The priest slot under the point (within snapping distance of its centre), if any.
    private (Track Track, int Slot)? PriestSlotAt(Vector2 point)
    {
        foreach (Track track in AllTracks)
            for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
                if (PriestSlotNode(track, slot).GlobalPosition.DistanceTo(point) <= SnapRadius)
                    return (track, slot);
        return null;
    }

    // Clicking a priest slot sends the active player's top priest there, as if it had been dragged.
    private void OnPriestSlotClicked(Track track, int slot)
    {
        GameState state = _history.Current;
        if (state.ActivePlayer is not Faction active)
        {
            ShowNote("Nobody is taking a turn right now.");
            return;
        }
        if (state.PriestZones[(int)track][slot] is not null)
        {
            ShowNote("That slot is already taken.");
            return;
        }
        if (state[active].TopPriest is not Piece priest)
        {
            ShowNote($"The {NameOf(active)} have no priest ready to place.");
            return;
        }
        _priestZones.Shrink();
        Submit(new PlacePriest(priest, track, slot)); // Render slides the priest into the slot
    }

    // The topmost piece under the point (highest ZIndex wins, so the top of a priest stack).
    private Piece? PieceUnder(Vector2 point) =>
        _pieceSprites
            .Where(pair => _panelPieces.Contains(pair.Key) && IsUnder(pair.Value, point))
            .OrderByDescending(pair => pair.Value.ZIndex)
            .Select(pair => (Piece?)pair.Key)
            .FirstOrDefault();

    private static bool IsUnder(Sprite2D sprite, Vector2 globalPoint) =>
        sprite.Texture is not null && sprite.GetRect().HasPoint(sprite.ToLocal(globalPoint));

    // ---------- Inspecting: click another player's compact panel to see it in full ----------

    // Panels whose parts respond to clicks and hovers: full size, and not just being inspected.
    private bool IsInteractive(PlayerPanel panel) => panel.Visible && !panel.IsCompact && panel != _inspected;

    // A compact panel under the point (but not its power offer, whose buttons work as usual).
    private PlayerPanel InspectablePanelUnder(Vector2 point) =>
        _panels.FirstOrDefault(panel =>
            panel.Visible && panel.IsCompact && panel.IsOver(point));

    // Grows the panel to full size where it is, on top of the others.
    private void Inspect(PlayerPanel panel)
    {
        _inspected = panel;
        _raised = panel;
        _raisedState = _history.Current;
        Render(animate: true);
    }

    // Shrinks the inspected panel back.
    private void EndInspection()
    {
        if (_inspected is null)
            return;
        _inspected = null;
        _raisedState = _history.Current;
        Render(animate: true);
    }

    // ---------- Hovering: buildings and X markers become see-through ----------

    private void UpdateHover(Vector2 mouse)
    {
        Sprite2D over = _board.Visible ? BuildingSpriteUnder(mouse) : null;
        if (over != _hoveredBuilding)
        {
            if (_hoveredBuilding is not null && IsInstanceValid(_hoveredBuilding))
                _hoveredBuilding.SelfModulate = Colors.White;
            _hoveredBuilding = over;
            if (over is not null)
                over.SelfModulate = new Color(1f, 1f, 1f, HoverOpacity);
        }

        UpdateFavourHover(mouse);

        // Every used action token (panel octagons, power actions, pass tiles) goes see-through under the mouse.
        foreach (ActionToken token in GetTree().GetNodesInGroup(ActionToken.Group).OfType<ActionToken>())
            token.HoverUsed(_board.Visible ? mouse : null, HoverOpacity);
    }

    // The topmost building in a bank of a full-size panel under the point, if any (buildings on the map,
    // and in a compact panel's building row, have nothing underneath worth seeing).
    private Sprite2D BuildingSpriteUnder(Vector2 point) =>
        _pieceSprites
            .Where(pair => Buildings.All.Contains(pair.Key.Kind) && _panelPieces.Contains(pair.Key)
                && PanelFor(pair.Key.Owner) is { IsCompact: false } && IsUnder(pair.Value, point))
            .OrderByDescending(pair => pair.Value.ZIndex)
            .Select(pair => pair.Value)
            .FirstOrDefault();

    // Something open that takes the next click itself (see OnPress).
    private bool AnyMenuOpen =>
        _wheel.Visible || _upgradeChoice is not null || _conversionPanel is not null;

    // ---------- Dragging ----------

    // options and moveFor: where it may go and the move a drop makes, if not the usual (the Selkies' stronghold).
    private void TryStartDrag(Piece piece, Vector2 mouse, PieceOptions given = null, Func<Destination, Move> moveFor = null)
    {
        GameState state = _history.Current;
        PieceOptions options = given ?? Rules.LegalDestinations(state, piece);
        _dragMove = moveFor;

        // A ready priest can also be dropped on its owner's worker count, to become a worker.
        // That is a free action, so it may be possible even when no priest slot is (e.g. after the action).
        bool canBecomeWorker = piece.Kind == PieceKind.Priest
            && state[piece.Owner].TopPriest == piece
            && Rules.Apply(state, new ConvertPriestToWorker(piece.Owner)).Accepted;

        if (!options.CanMove && !canBecomeWorker)
        {
            ShowNote(options.Reason); // e.g. "Only the leftmost dwelling can be built."
            return;
        }

        _dragged = piece;
        _dragOptions = options;
        _dragCanBecomeWorker = canBecomeWorker;
        if (piece.Kind == PieceKind.Priest)
            _priestZones.Expand(); // full size while a priest is held
        if (canBecomeWorker)
            PanelFor(piece.Owner)?.HighlightWorkerZone(true);
        Sprite2D sprite = _pieceSprites[piece];
        EdgeHighlight.SetOnArea(sprite, false, default); // the build offer's light (RenderBuildOffer), while held
        sprite.SelfModulate = Colors.White; // no longer see-through once picked up
        if (_hoveredBuilding == sprite)
            _hoveredBuilding = null;
        _grabOffset = sprite.GlobalPosition - mouse;
        sprite.ZIndex = 100; // above everything while dragging
        ShowNote("");
        // A dwelling shows where it can go with the reach highlight; other pieces light up their targets. The build
        // offer's dwelling (after a transform) has its hexes lit already (RenderBuildOffer): no reach over that glow.
        bool offered = BuildOfferHexes(state).Count > 0 && NextDwelling(state, piece.Owner) == piece;
        if (piece.Kind == PieceKind.Dwelling && offered)
            _map.ShowReach(Enumerable.Empty<int>());
        else if (piece.Kind == PieceKind.Dwelling || state.Phase == Phase.PlacingDwellings) // (the Atlanteans' stronghold as a dwelling)
            _map.ShowReach(HexesOf(options.Destinations));
        else
            SetHighlights(options.Destinations, on: true);
        ShowTransformCosts(piece, options.Destinations);
    }

    private static IEnumerable<int> HexesOf(IEnumerable<Destination> destinations) =>
        destinations.OfType<HexDestination>().Select(destination => destination.Hex);

    // The reach highlight outside a drag (the white hexagon inside each hex):
    //   during starting-dwelling placement, every hex where the active player's next dwelling could go;
    //   while free spades are being spent (a spade bonus action or power action, or a cult reward between rounds),
    //   every hex they could go on (the hexes a dwelling could then go on are lit by RenderBuildOffer);
    //   otherwise none. (A town waiting for its key is shown by its sparkling ring instead.)
    private void RenderReach(GameState state)
    {
        if (_dragged is { Kind: PieceKind.Dwelling } || state.Phase == Phase.PlacingDwellings && _dragged is { Kind: PieceKind.Stronghold })
            return; // the drag shows its own
        IEnumerable<int> hexes = Enumerable.Empty<int>();
        if (state.Phase == Phase.PlacingDwellings && state.ActivePlayer is Faction active
            && StartingPiece(state, active) is Piece dwelling) // (the Atlanteans' stronghold too: MainHexes.cs)
            hexes = HexesOf(Rules.LegalDestinations(state, dwelling).Destinations);
        else if (state.SpadesPending && state.ActivePlayer is Faction spender)
            hexes = Rules.SpadeTargets(state, spender);
        _map.ShowReach(hexes);
    }

    private void EndDrag(Piece piece)
    {
        SetHighlights(_dragOptions.Destinations, on: false);
        _map.ShowReach(Enumerable.Empty<int>()); // the next render puts back any that still applies
        _map.HideAllCosts();
        Vector2 mouse = GetGlobalMousePosition();
        Destination target = NearestDestination(_dragOptions.Destinations, mouse);
        PlayerPanel ownPanel = PanelFor(piece.Owner);
        bool onWorkerZone = _dragCanBecomeWorker && ownPanel is not null && ownPanel.IsOverWorkerZone(mouse);
        ownPanel?.HighlightWorkerZone(false);
        _dragged = null;
        _dragOptions = null;
        _dragCanBecomeWorker = false;
        if (piece.Kind == PieceKind.Priest)
            _priestZones.Shrink(); // wherever the priest was dropped

        if (onWorkerZone)
            Submit(new ConvertPriestToWorker(piece.Owner)); // the priest slides back to the supply
        else if (target is not null)
            Submit(_dragMove?.Invoke(target) ?? Rules.MoveTo(_history.Current, piece, target)); // Render then slides it into place
        else
            Render(animate: true);               // not near a legal spot: slides back
    }

    // ---------- Dragging a worker onto the coin count ----------

    private PlayerPanel WorkerZoneUnder(Vector2 point) =>
        _panels.FirstOrDefault(panel => IsInteractive(panel) && panel.IsOverWorkerZone(point));

    // Pressing on a worker count picks up a worker (even with none: the conversion may go into debt).
    // Its drop targets are the same panel's coin count and, while the Darklings may exchange workers for priests,
    // their worker and priest section.
    private void TryStartWorkerDrag(PlayerPanel panel, Vector2 mouse)
    {
        if (panel.ShownFaction is not Faction faction)
            return;
        MoveResult trial = Rules.Apply(_history.Current, new ConvertWorkerToCoin(faction));
        if (!trial.Accepted)
        {
            ShowIssues(trial.Issues); // e.g. "It is not the Giants' turn."
            return;
        }

        _draggedWorker = new Sprite2D
        {
            Texture = Art.WorkerTexture(),
            ZIndex = 100,
            Scale = _pieceLayer.GlobalScale, // the same size as pieces on the board
        };
        AddChild(_draggedWorker);
        _draggedWorker.GlobalPosition = mouse;
        _workerDragPanel = panel;
        panel.HighlightCoinZone(true);
        ShowNote("");
    }

    private void EndWorkerDrag()
    {
        PlayerPanel panel = _workerDragPanel;
        bool onCoinZone = panel.IsOverCoinZone(GetGlobalMousePosition());

        panel.HighlightCoinZone(false);
        _draggedWorker.QueueFree();
        _draggedWorker = null;
        _workerDragPanel = null;

        if (panel.ShownFaction is not Faction faction)
            return;
        if (_history.Current[faction].PriestExchangesLeft > 0 && panel.IsOverPriestExchange(GetGlobalMousePosition()))
            Submit(new ExchangeWorkersForPriests(faction, 1));
        else if (onCoinZone)
            Submit(new ConvertWorkerToCoin(faction)); // dropped anywhere else: nothing happens
    }

    private PlayerPanel PanelFor(Faction faction) =>
        _panels.FirstOrDefault(panel => panel.Visible && panel.ShownFaction == faction);

    // The legal destination nearest the point, if any is within SnapRadius; otherwise null.
    private Destination NearestDestination(IEnumerable<Destination> destinations, Vector2 point) =>
        destinations
            .Select(destination => (destination, distance: DestinationNode(destination).GlobalPosition.DistanceTo(point)))
            .Where(candidate => candidate.distance <= SnapRadius)
            .OrderBy(candidate => candidate.distance)
            .Select(candidate => candidate.destination)
            .FirstOrDefault();

    // Legal destinations: a hex gets a white edge highlight, a cult track a yellow one; a priest slot is tinted.
    private void SetHighlights(IEnumerable<Destination> destinations, bool on)
    {
        foreach (Destination destination in destinations)
        {
            switch (destination)
            {
                case HexDestination hex:
                    _map.ShowHexHighlight(hex.Hex, on);
                    break;
                case TrackDestination: // a step on a cult of the player's choice: yellow, one glint
                    EdgeHighlight.SetOnCultTrack(DestinationNode(destination), on, glints: 1);
                    break;
                default:
                    DestinationNode(destination).Modulate = on ? HighlightColour : Colors.White;
                    break;
            }
        }
    }

    // ---------- Where things are on screen ----------

    private Node2D DestinationNode(Destination destination) => destination switch
    {
        HexDestination d        => _map.Hex(d.Hex),
        PriestSlotDestination d => PriestSlotNode(d.Track, d.Slot),
        TrackDestination d      => _tracks.GetNode<Node2D>(d.Track.ToString()),
        _ => throw new ArgumentOutOfRangeException(nameof(destination)),
    };

    private Node2D PriestSlotNode(Track track, int slot) => _priestZones.GetNode<Node2D>($"{track}/Slot{slot}");

    private Node2D TrackStepNode(Track track, int step) => _tracks.GetNode<Node2D>($"{track}/Step{step}");

    // Each priest slot shows how many steps it gives, read from Core so the two can never disagree: for the player
    // whose turn it is (1 more for the Acolytes with their stronghold).
    private void LabelPriestSlots(GameState state = null)
    {
        Faction? active = state is { Phase: Phase.Playing, ActivePlayer: Faction playing } ? playing : null;
        foreach (Track track in AllTracks)
            for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
            {
                Label label = PriestSlotNode(track, slot).GetNode<Label>("StepLabel");
                string text = (active is Faction faction ? Rules.PriestSlotSteps(state, faction, slot) : Config.PriestSlots[slot].Steps).ToString();
                if (label.Text != text)
                    label.Text = text;
            }
    }
}
