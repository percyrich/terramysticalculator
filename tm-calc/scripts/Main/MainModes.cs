using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;
using Resource = Core.Resource; // not Godot.Resource

namespace TMCalc;

// Setting up a game in progress (Phase.SettingUp), the turn-order step after it (Phase.OrderingTurns), Free play, and
// priest debt. Core: Rules.Modes.cs, Rules.EditPosition.cs, Rules.PositionChecks.cs; see the Game modes doc.
//   Layout: during Setup, the turn-order step and Free, the boards keep their places in turn order (no shuffling): the
//     selected faction's board is open, the others compact, and the boards below the open one move up to fit
//     (FixedSlot). Clicking another board selects that faction (SelectFaction).
//   Setup clicks (the selected faction; Shift: ×10 for VP, coins, workers and keys):
//     own board: VP, coins, workers: left +1, right −1 (keys only come from key tokens placed); a power bowl: click
//       adds a token, a token dragged to another bowl moves there, dragged off the bowls leaves the game, right-click
//       removes one; the bridge supply: drag a bridge to a lit spot; shipping and spades icons: left +1,
//       right −1; an octagon: used or not; a supply priest: gain it; a priest in hand: right-click back to the supply,
//       drag to a priest slot; a favour: right-click returns it; the pass tile: right-click marks passed or not;
//       a building: drag to a hex.
//     board: a bank favour takes it; a bank pass tile swaps it in (right-click: +1 coin, Shift-right-click −1); a round
//       tile makes it the current round; a power action: taken or not; a key token: drag onto a hex (right-click a
//       hex: its key back); a priest in a zone: right-click back to the supply; an empty slot: the top priest in hand
//       (else the next in the supply) goes there; a cult track: left +1, right −1; a building on the map: drag it
//       (onto the board: back to the board), click it for the other building kinds its board still has (replacing it),
//       right-click back to the board; a bridge: drag it to another lit spot, or off them to take it away (right-click:
//       taken away); an empty land hex: the colour wheel (any terrain).
//   Favours owed (Core keeps FavoursOwed at Rules.FavoursToClaim in Setup) show as blank favours after the faction's
//     own (RenderBlankFavours), in play too.
//     The game button is Done: dots while a check fails (each click shows the next: "… (1 of 3)"), tick once all pass.
//   The turn-order step: boards still in the round show dots; clicking one gives it the next place (clicking a placed
//     one takes it and every later one back); the last one starts play.
//   Priest debt: red priest outlines over the supply's next priests (beyond the supply: outlines alone); in Setup and
//     Free a click on one cancels a priest owed.
public partial class Main
{
    private int _setupIssue;          // which failing check the next Done click shows
    private Piece? _setupDragged;     // a building or priest held during Setup
    private Vector2 _setupGrab;
    private int? _setupKey;           // a key token (its kind) held during Setup
    private Node2D _setupKeySprite;
    private int? _setupBowl;          // the bowl a power token press began in (Setup)
    private bool _setupTokenLifted;   // a token was picked up there
    private Vector2 _setupPressPoint;  // where the press began (a press that hardly moves is a click)
    private Sprite2D _setupBridgeSprite; // a bridge being dragged (from the supply, or lifted off the map)
    private Bridge? _setupBridgeLifted;
    private readonly List<((int HexA, int HexB) Edge, Sprite2D Sprite)> _setupBridgeSpots = new();
    private readonly List<Sprite2D> _blankFavours = new();
    private const float ClickSlop = 6f; // screen pixels a click may move and still be a click
    private readonly List<Sprite2D> _debtSprites = new();
    private readonly List<Faction> _debtOwners = new();

    private static bool IsFixedStack(GameState state) =>
        state.Phase is Phase.SettingUp or Phase.OrderingTurns || state.IsPlanning;

    // ---------- Layout and top line ----------

    // Where a board sits in the fixed stack: under the boards before it in turn order, each open (the selected one) or
    // compact, with the scene's gap between compact boards.
    private Vector2 FixedSlot(GameState state, int place, PlayerPanel panel)
    {
        float full = FactionBoardArt.BackingLayout(false).Size.Y * panel.Scale.Y;
        float small = FactionBoardArt.BackingLayout(true).Size.Y * panel.Scale.Y;
        float gap = _panelSlots.Length > 2 ? _panelSlots[2].Y - _panelSlots[1].Y - small : 0f;
        float y = _panelSlots[0].Y;
        for (var j = 0; j < place && j < state.TurnOrder.Count; j++)
            y += (state.TurnOrder[j] == state.ActivePlayer ? full : small) + gap;
        return new Vector2(_panelSlots[Mathf.Min(place, _panelSlots.Length - 1)].X, y);
    }

    private string TopLine(GameState state)
    {
        switch (state.Phase)
        {
            case Phase.SettingUp:
                return $"Setting up: {NameOf(state.ActivePlayer)} selected";
            case Phase.OrderingTurns:
                return $"Round {state.Round} of {Config.Rounds}: click the boards in turn order, first to act first";
        }
        string line = Instruction(state);
        if (!state.IsPlanning || state.Phase != Phase.Playing || state.Options.Calculator) // Calculator: always Free, so unsaid
            return line;
        return line.StartsWith("Round ") ? $"Free: {NameOf(state.ActivePlayer)} selected" : $"Free: {line}";
    }

    // The game button in Setup: Done once every check passes; otherwise the next failing check, with its count.
    private void OnSetupDone()
    {
        ValueList<string> issues = Rules.SetupIssues(_history.Current);
        if (issues.Count == 0)
        {
            _setupIssue = 0;
            Submit(new FinishSetup());
            return;
        }
        int at = _setupIssue % issues.Count;
        _setupIssue = at + 1;
        ShowNote($"{issues[at]} ({at + 1} of {issues.Count})");
    }

    // ---------- Priest debt ----------

    private void RenderPriestDebt(GameState state, bool animate)
    {
        _debtOwners.Clear();
        var used = 0;
        for (var p = 0; p < state.Players.Count && p < _panels.Length; p++)
        {
            PlayerState player = state.Players[p];
            PlayerPanel panel = _panels[p];
            if (player.PriestDebt == 0 || !panel.Visible || !panel.IsShownInLayout(panel.PriestSupply))
                continue;
            int supply = player.PriestSupply.Count;
            for (var k = 0; k < player.PriestDebt; k++)
            {
                var placement = new Placement(panel.PriestSupply, PriestSupplyOffset * (supply - 1 - k), PriestDebtZIndex, panel);
                if (used == _debtSprites.Count)
                {
                    var made = new Sprite2D { Name = "PriestDebt" };
                    _pieceLayer.AddChild(made);
                    _debtSprites.Add(made);
                }
                Sprite2D sprite = _debtSprites[used++];
                sprite.Texture = PieceArt.TextureFor(PieceKind.Priest, new Color(0, 0, 0, 0), PriestDebtColour);
                sprite.ZIndex = PriestDebtZIndex + RaiseFor(panel);
                sprite.Visible = true;
                Resize(sprite, PieceSize(placement), animate);
                Slide(sprite, TargetFor(placement), animate);
                _debtOwners.Add(player.Faction);
            }
        }
        for (var i = used; i < _debtSprites.Count; i++)
            _debtSprites[i].Visible = false;
    }

    private Faction? DebtMarkerUnder(Vector2 point)
    {
        for (var i = 0; i < _debtOwners.Count; i++)
            if (_debtSprites[i].Visible && IsUnder(_debtSprites[i], point))
                return _debtOwners[i];
        return null;
    }

    // ---------- Input ----------

    private PlayerPanel PanelUnder(Vector2 point) => _panels.FirstOrDefault(panel => panel.Visible && panel.IsOver(point));

    // Clicks and drags of Setup, the turn-order step, and priest debt in Free. True if the event was taken.
    private bool OnModeInput(InputEvent e)
    {
        GameState state = _history.Current;
        Vector2 mouse = GetGlobalMousePosition();
        if (OnPassTileCoinInput(e, state)) // Calculator: right-click a bank pass tile for a coin off it (MainCalculatorGain.cs)
            return true;
        if (OnCalculatorGainInput(e, state)) // Calculator's +1 panel (MainCalculatorGain.cs)
            return true;
        if (OnNeutralInput(e, state, mouse)) // Calculator's neutral pieces (MainNeutral.cs)
            return true;
        if (e is InputEventMouseMotion)
        {
            if (_setupDragged is Piece held && _pieceSprites.TryGetValue(held, out Sprite2D heldSprite))
                heldSprite.GlobalPosition = mouse + _setupGrab;
            else if (_setupKey is not null && _setupKeySprite is not null)
                _setupKeySprite.GlobalPosition = mouse;
            else if (_setupBridgeSprite is not null)
                _setupBridgeSprite.GlobalPosition = mouse;
            else if (_setupBowl is not null && _setupTokenLifted && mouse.DistanceTo(_setupPressPoint) > ClickSlop
                && state.ActivePlayer is Faction lifter)
                PanelFor(lifter)?.PowerBank.DragLifted(mouse);
            return _setupDragged is not null || _setupKey is not null || _setupBridgeSprite is not null || _setupBowl is not null;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button)
            return false;
        if (!button.Pressed)
        {
            if (button.ButtonIndex != MouseButton.Left
                || (_setupDragged is null && _setupKey is null && _setupBowl is null && _setupBridgeSprite is null))
                return false;
            GetViewport().SetInputAsHandled();
            EndSetupDrag(state, mouse);
            return true;
        }

        bool right = button.ButtonIndex == MouseButton.Right;
        if (OnSafeEditorPress(mouse, right)) // Setup: the Treasurers' Safe editor, if open (MainSafeEditor.cs)
            return true;
        // The turn order screen: a faction's starting VP (MainFactions.cs).
        if (state.Phase == Phase.ChoosingTurnOrder && StartingVictoryPointsUnder(mouse) is Faction starter)
        {
            GetViewport().SetInputAsHandled();
            Submit(new AdjustStartingVictoryPoints(starter, (right ? -1 : 1) * (Input.IsKeyPressed(Key.Shift) ? 10 : 1)));
            return true;
        }
        bool setup = state.Phase == Phase.SettingUp;
        if (!setup && state.Phase != Phase.OrderingTurns && !state.IsPlanning)
            return false;
        if (IsOverTopPanel() || ScoreBadgeUnder(mouse) is not null || _wheel.Visible || _upgradeChoice is not null || AnyMenuOpen)
            return false; // the top panel, score badges and the wheel work as usual (OnPress)

        if (!right && (setup || state.IsPlanning) && DebtMarkerUnder(mouse) is Faction owing)
        {
            GetViewport().SetInputAsHandled();
            Submit(new CancelPriestDebt(owing));
            return true;
        }
        if (state.Phase == Phase.OrderingTurns)
        {
            if (right || PanelUnder(mouse)?.ShownFaction is not Faction clicked)
                return false;
            GetViewport().SetInputAsHandled();
            bool placed = state.StartOrder?.Contains(clicked) ?? false;
            Submit(placed ? new UnchooseTurnOrder(clicked) : new ChooseTurnOrder(clicked));
            return true;
        }
        if (!setup)
            return false; // Free: normal play (a compact board selects: OnPress)

        GetViewport().SetInputAsHandled();
        OnSetupPress(state, mouse, right, Input.IsKeyPressed(Key.Shift));
        return true;
    }

    private void OnSetupPress(GameState state, Vector2 mouse, bool right, bool shift)
    {
        if (state.ActivePlayer is not Faction f)
            return;
        int sign = right ? -1 : 1, step = shift ? 10 : 1;

        // The priest zones: a click on them while small only makes them full size.
        bool overZones = _priestZones.IsOver(mouse);
        if (!_priestZones.IsExpanded && overZones && !right)
        {
            _priestZones.Expand();
            return;
        }
        if (_priestZones.IsExpanded && !overZones)
            _priestZones.Shrink();

        PlayerPanel panel = PanelUnder(mouse);
        if (panel?.ShownFaction is Faction other && other != f)
        {
            Submit(new SelectFaction(other));
            return;
        }
        if (panel is not null)
        {
            OnSetupPanelPress(state, panel, f, mouse, right, sign, step);
            return;
        }

        if (!right && FavourUnder(mouse) is int bankFavour)
            Submit(new EditFavour(f, bankFavour, Take: true));
        else if (PassTileUnder(mouse) is int tile)
        {
            if (state.PassTileBank.Contains(tile))
                Submit(right ? new EditPassTileCoins(tile, shift ? -1 : 1) : new EditPassTile(f, tile));
            else if (right)
                Submit(new MarkPassed(f, !state.HasPassed(f)));
        }
        else if (!right && RoundTileUnder(mouse) is int round)
            Submit(new SetRound(round));
        else if (!right && PowerActionUnder(mouse) is int action)
            Submit(new TogglePowerAction(action));
        else if (!right && KeyUnder(mouse) is int token)
            StartSetupKeyDrag(token);
        else if (PieceUnder(mouse) is Piece piece)
        {
            bool onMap = state.Map.IndexOf(piece) >= 0;
            if (right && (onMap ? piece.Owner == f : piece.Kind == PieceKind.Priest))
                Submit(!onMap ? new EditReturnPriest(piece)
                    : piece.Kind == PieceKind.Landscape ? new EditLandscape(f, null) : new EditBuilding(piece, null));
            else if (!right && onMap && piece.Owner == f)
                StartSetupDrag(piece, mouse);
            else if (onMap && piece.Owner != f)
                ShowNote($"Select the {NameOf(piece.Owner)} to change their buildings.");
        }
        else if (!right && PriestSlotAt(mouse) is { } slot)
        {
            PlayerState player = state[f];
            if ((player.TopPriest ?? (player.PriestSupply.Count > 0 ? (Piece?)player.PriestSupply[0] : null)) is Piece priest)
                Submit(new EditPlacePriest(priest, slot.Track, slot.Slot));
        }
        else if (OwnBridgeNear(state, f, mouse) is Bridge bridge)
        {
            if (right)
                Submit(new EditBridge(f, bridge.HexA, bridge.HexB, Place: false));
            else
                StartSetupBridgeDrag(state, f, bridge, mouse);
        }
        else if (TrackAt(mouse) is Track track)
            Submit(new EditTrack(f, track, sign));
        else if (_map.HexAt(mouse) is int hex)
        {
            if (right)
            {
                if (state.PlacedKeys.Any(key => key.Hex == hex))
                    Submit(new EditRemoveKey(hex));
            }
            else if (state.Map[hex] is null && state.HexTerrain[hex] != Terrain.River)
                OpenSetupWheel(state, f, hex);
        }
    }

    private void OnSetupPanelPress(GameState state, PlayerPanel panel, Faction f, Vector2 mouse, bool right, int sign, int step)
    {
        foreach (var (kind, resource) in new[]
        {
            (ResourceKind.VictoryPoint, Resource.VictoryPoints), (ResourceKind.Coin, Resource.Coins),
            (ResourceKind.Worker, Resource.Workers),
        })
            if (panel.IsOverHolding(kind, mouse))
            {
                Submit(new EditResource(f, resource, sign * step));
                return;
            }
        for (var bowl = 1; bowl <= 3; bowl++)
            if (panel.PowerBank.IsOverBowl(bowl, mouse))
            {
                if (right)
                    Submit(new EditPowerToken(f, bowl, null));
                else
                {
                    _setupBowl = bowl; // added, moved or removed on release (EndSetupDrag)
                    _setupPressPoint = mouse;
                    _setupTokenLifted = panel.PowerBank.LiftToken(bowl);
                }
                return;
            }
        if (!right && panel.IsOverBridgeSupply(mouse))
        {
            if (state.BridgesLeft(f) > 0)
                StartSetupBridgeDrag(state, f, null, mouse);
            return;
        }
        if (panel.IsOverShippingIcon(mouse))
            Submit(new EditLevel(f, Level.Shipping, sign));
        else if (panel.IsOverSpadeIcon(mouse))
            Submit(new EditLevel(f, Level.Spades, sign));
        else if (!right && f == Faction.Treasurers && panel.IsOverSafe(mouse))
            OpenSafeEditor(panel); // its contents, as the conversions' menu (MainSafeEditor.cs)
        else if (!right && panel.BonusActionAt(mouse) is BonusActionId bonus)
            Submit(new ToggleBonusAction(f, bonus));
        else if (right && OwnFavourUnder(state, f, mouse) is int favour)
            Submit(new EditFavour(f, favour, Take: false));
        else if (right && PassTileUnder(mouse) is int tile && state[f].PassTile == tile)
            Submit(new MarkPassed(f, !state.HasPassed(f)));
        else if (PieceUnder(mouse) is Piece piece && piece.Owner == f)
        {
            PlayerState player = state[f];
            if (piece.Kind != PieceKind.Priest)
            {
                if (!right)
                    StartSetupDrag(piece, mouse);
            }
            else if (player.PriestSupply.Contains(piece))
            {
                if (!right)
                    Submit(new EditGainPriest(f));
            }
            else if (right)
                Submit(new EditReturnPriest(piece));
            else
                StartSetupDrag(piece, mouse);
        }
    }

    private void StartSetupDrag(Piece piece, Vector2 mouse)
    {
        if (!_pieceSprites.TryGetValue(piece, out Sprite2D sprite))
            return;
        _setupDragged = piece;
        _setupGrab = sprite.GlobalPosition - mouse;
        _setupPressPoint = mouse;
        StopSlide(sprite);
        sprite.ZIndex = PriestDebtZIndex + 10;
        if (piece.Kind == PieceKind.Priest)
            _priestZones.Expand();
    }

    private void StartSetupKeyDrag(int token)
    {
        _setupKey = token;
        _setupKeySprite = _keySprites.Where(pair => _bankKeys.Contains(pair.Key) && pair.Key.Id == token)
            .OrderByDescending(pair => pair.Value.ZIndex).Select(pair => pair.Value).FirstOrDefault();
        if (_setupKeySprite is not null)
            StopSlide(_setupKeySprite);
    }

    private void EndSetupDrag(GameState state, Vector2 mouse)
    {
        Faction? selected = state.ActivePlayer;
        if (_setupBowl is int from && selected is Faction owner)
        {
            _setupBowl = null;
            PowerBowlsView bowls = PanelFor(owner)?.PowerBank;
            int? to = Enumerable.Range(1, 3).Where(bowl => bowls?.IsOverBowl(bowl, mouse) ?? false).Select(b => (int?)b).FirstOrDefault();
            // Fan factions: the Children of the Wyrm's tokens dropped on a river hex.
            if (owner == Faction.ChildrenOfTheWyrm && to is null && _map.HexAt(mouse) is int river
                && state.HexTerrain[river] == Terrain.River)
            {
                bowls?.ReleaseLifted();
                _setupTokenLifted = false;
                Submit(new EditWyrmToken(owner, from, river));
                return;
            }
            if (mouse.DistanceTo(_setupPressPoint) <= ClickSlop || to == from)
            {
                bowls?.ReleaseLifted();
                Submit(new EditPowerToken(owner, null, from)); // a click: a new token there
            }
            else
            {
                if (bowls is not null && _setupTokenLifted)
                    bowls.DirectNextChange = true; // the lifted token goes straight there (or leaves)
                Submit(new EditPowerToken(owner, from, to));
                bowls?.ReleaseLifted();
            }
            _setupTokenLifted = false;
            return;
        }
        if (_setupBridgeSprite is not null)
        {
            EndSetupBridgeDrag(state, mouse);
            return;
        }
        if (_setupKey is int token)
        {
            _setupKey = null;
            _setupKeySprite = null;
            if (selected is Faction keyOwner && _map.HexAt(mouse) is int keyHex)
                Submit(new EditPlaceKey(keyOwner, token, keyHex));
            else
                Render(animate: true);
            return;
        }
        if (_setupDragged is not Piece piece)
            return;
        _setupDragged = null;
        if (piece.Kind == PieceKind.Priest)
        {
            _priestZones.Shrink();
            if (PriestSlotAt(mouse) is { } slot)
            {
                Submit(new EditPlacePriest(piece, slot.Track, slot.Slot));
                return;
            }
        }
        else if (piece.Kind != PieceKind.Landscape && state.Map.IndexOf(piece) is var at && at >= 0
            && mouse.DistanceTo(_setupPressPoint) <= ClickSlop)
        {
            Render(animate: true); // a click, not a drag: back in place, and the kinds it could become
            OfferSetupBuildings(state, piece, at);
            return;
        }
        else if (_map.HexAt(mouse) is int hex)
        {
            Submit(piece.Kind == PieceKind.Landscape ? new EditLandscape(piece.Owner, hex) : new EditBuilding(piece, hex));
            return;
        }
        else if (state.Map.IndexOf(piece) >= 0 && (PanelFor(piece.Owner)?.IsOver(mouse) ?? false))
        {
            Submit(piece.Kind == PieceKind.Landscape ? new EditLandscape(piece.Owner, null) : new EditBuilding(piece, null));
            return;
        }
        Render(animate: true); // dropped elsewhere: back where it was
    }

    // ---------- Hit tests ----------

    private int? RoundTileUnder(Vector2 point)
    {
        for (var i = 0; i < _roundTileSprites.Length; i++)
            if (_roundTileSprites[i] is Node2D tile && tile.IsVisibleInTree() && Hit.IsOver(tile, point))
                return i + 1;
        return null;
    }

    private int? OwnFavourUnder(GameState state, Faction faction, Vector2 point) =>
        _favourSprites
            .Where(pair => !_bankFavours.Contains(pair.Key) && state[faction].FavourTiles.Contains(pair.Key)
                && pair.Value.IsVisibleInTree() && Hit.IsOver(pair.Value, point))
            .Select(pair => (int?)pair.Key.Id)
            .FirstOrDefault();

    // ---------- Bridges and building kinds (Setup) ----------

    private Bridge? OwnBridgeNear(GameState state, Faction faction, Vector2 point) =>
        _bridgeSprites
            .Where(pair => pair.Key.Owner == faction && pair.Value.Visible && pair.Value.GlobalPosition.DistanceTo(point) <= SnapRadius)
            .OrderBy(pair => pair.Value.GlobalPosition.DistanceTo(point))
            .Select(pair => (Bridge?)pair.Key)
            .FirstOrDefault();

    // A bridge picked up from the supply (lifted null) or off the map: it follows the mouse, and the free spots next to
    // the faction's buildings light up.
    private void StartSetupBridgeDrag(GameState state, Faction faction, Bridge? lifted, Vector2 mouse)
    {
        _setupBridgeLifted = lifted;
        _setupBridgeSprite = new Sprite2D { Texture = Art.BridgeIconTexture(state[faction].Colour), ZIndex = 100, Scale = _pieceLayer.GlobalScale };
        AddChild(_setupBridgeSprite);
        _setupBridgeSprite.GlobalPosition = mouse;
        if (lifted is Bridge onMap && _bridgeSprites.TryGetValue(onMap, out Sprite2D placed))
            placed.Visible = false;
        foreach ((int HexA, int HexB) edge in Rules.BridgeSpots(state, faction))
        {
            var spot = new Sprite2D { Texture = Art.BridgeSpotTexture(), ZIndex = MapTopDepth(state) + 1 };
            _pieceLayer.AddChild(spot);
            PlaceAcross(spot, edge.HexA, edge.HexB);
            _setupBridgeSpots.Add((edge, spot));
        }
    }

    // Dropped on a lit spot: placed (moved there, if lifted off the map). A lifted bridge dropped anywhere else but its
    // own place is taken away.
    private void EndSetupBridgeDrag(GameState state, Vector2 mouse)
    {
        (int HexA, int HexB)? target = _setupBridgeSpots
            .Where(spot => spot.Sprite.GlobalPosition.DistanceTo(mouse) <= SnapRadius)
            .OrderBy(spot => spot.Sprite.GlobalPosition.DistanceTo(mouse))
            .Select(spot => ((int HexA, int HexB)?)spot.Edge)
            .FirstOrDefault();
        foreach (var (_, spot) in _setupBridgeSpots)
            spot.QueueFree();
        _setupBridgeSpots.Clear();
        _setupBridgeSprite.QueueFree();
        _setupBridgeSprite = null;
        Bridge? lifted = _setupBridgeLifted;
        _setupBridgeLifted = null;
        if (lifted is Bridge shown && _bridgeSprites.TryGetValue(shown, out Sprite2D liftedSprite))
            liftedSprite.Visible = true;
        if (state.ActivePlayer is not Faction f)
            return;
        if (lifted is Bridge old)
        {
            Vector2 home = (_map.Hex(old.HexA).GlobalPosition + _map.Hex(old.HexB).GlobalPosition) / 2;
            if (target is null && home.DistanceTo(mouse) <= SnapRadius)
            {
                Render(animate: false); // back in its place
                return;
            }
            Submit(new EditBridge(f, old.HexA, old.HexB, Place: false));
        }
        if (target is (int a, int b))
            Submit(new EditBridge(f, a, b, Place: true));
        else if (lifted is null)
            Render(animate: false);
    }

    // A click on one of the selected faction's buildings: the other kinds its board still has, any of which replaces it.
    private void OfferSetupBuildings(GameState state, Piece building, int hex)
    {
        PlayerState player = state[building.Owner];
        var choices = Buildings.All
            .Where(kind => kind != building.Kind && player.NextBuilding(kind) is Piece)
            .Select(kind => (Kind: kind, Move: (Move)new EditBuilding(player.NextBuilding(kind)!.Value, hex)))
            .ToList();
        // Fan factions: an Ember dwelling also offers a lava tile under it, on the left; a click adds one and leaves the
        // choices open for more.
        bool lava = building.Owner == Faction.KingdomOfEmber && building.Kind == PieceKind.Dwelling;
        if (choices.Count == 0 && !lava)
            ShowNote($"The {NameOf(building.Owner)} have no other kind of building left on their board.");
        else
            ShowBuildingChoices(hex, player.Colour, choices, lava ? new EditLava(hex) : null,
                () => OfferSetupBuildings(_history.Current, building, hex));
    }

    // ---------- Favours owed ----------

    // A blank favour for each favour owed, after the faction's own tiles on its board, dimmed.
    private void RenderBlankFavours(GameState state, bool animate)
    {
        var used = 0;
        Vector2 tileSize = FavourTileSize();
        for (var p = 0; p < state.Players.Count && p < _panels.Length; p++)
        {
            PlayerState player = state.Players[p];
            PlayerPanel panel = _panels[p];
            int held = player.FavourTiles.Count, total = held + player.FavoursOwed;
            for (int i = held; i < total; i++)
            {
                var placement = new Placement(panel.FavourRow, panel.FavourOffset(i, total, tileSize), i, panel);
                if (used == _blankFavours.Count)
                {
                    var made = new Sprite2D { Name = "BlankFavour", Texture = Art.FavourTexture() };
                    _pieceLayer.AddChild(made);
                    _blankFavours.Add(made);
                }
                Sprite2D blank = _blankFavours[used++];
                blank.Visible = true;
                blank.ZIndex = i + RaiseFor(panel);
                bool shown = panel.Visible && panel.IsShownInLayout(panel.FavourRow);
                TintAndFade(blank, Colors.White, shown ? BlankFavourAlpha : 0f, animate);
                Resize(blank, ScaleFor(placement) * (tileSize / blank.Texture.GetSize()), animate);
                Slide(blank, TargetFor(placement), animate);
            }
        }
        for (var i = used; i < _blankFavours.Count; i++)
            _blankFavours[i].Visible = false;
    }

    private const float BlankFavourAlpha = 0.6f;

    // The colour wheel on an empty land hex: any basic terrain, and Ice or Volcano with such a faction in the game.
    private void OpenSetupWheel(GameState state, Faction selected, int hex)
    {
        bool ice = state.Players.Any(p => p.Kind == FactionKind.Ice), volcano = state.Players.Any(p => p.Kind == FactionKind.Volcano);
        IEnumerable<Terrain> allowed = Terrains.Wheel
            .Concat(ice ? new[] { Terrain.Ice } : System.Array.Empty<Terrain>())
            .Concat(volcano ? new[] { Terrain.Volcano } : System.Array.Empty<Terrain>())
            .Where(t => t != state.HexTerrain[hex]);
        var choices = new ValueList<TransformChoice>(allowed.Select(t => new TransformChoice(t, 0, 0, default, new EditTerrain(hex, t))));
        OpenColourWheel(state, selected, hex, choices);
        _wheel.CentreOnly = false;
        _wheel.Centre = volcano ? Terrain.Volcano : null;
        if (!ice)
            _wheel.Ring = null;
    }
}
