using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: where the panels and every piece go, and sliding them there. A file of the same class (partial).
public partial class Main
{
    // Priests in the zones: just over the zones, which lie over the pass tiles while big (ZoomableZone.ExpandedZIndex).
    private int PriestInZoneZ => _priestZones.ZIndex + 1;

    private static bool IsInPriestZone(Piece piece, Placement placement) =>
        piece.Kind == PieceKind.Priest && placement.Panel is null;

    // Panels stack from the top: players still in this round, active player first (TurnQueue),
    // then players who have passed, in the order they passed (PassOrder). The places are where the panels are put in
    // the editor (_panelSlots). Panels with no player (beyond the player count) are hidden.
    // When a turn passes (an animated render with another active player): the panels change in three steps (MoveSeconds,
    // PanelShuffleSeconds, MoveSeconds), by rendering again: the old active player's panel collapses where it is (Collapse: every panel
    // compact, the stack as it was), then the panels move to their new places (Move: still all compact), then the new
    // active player's panel opens (None: as usual). Anything else rendered in between (a move, undo) cancels the rest.
    private enum PanelStep { None, Collapse, Move }
    private const float PanelShuffleSeconds = 2 * MoveSeconds; // the Move step: panels (and the pieces on them) sliding
    private float SlideSeconds => _panelStep == PanelStep.Move ? PanelShuffleSeconds : MoveSeconds;
    // The Move step's easing: slow at both ends (Sine, InOut), so the panels and what is on them gather speed and
    // settle instead of leaping off; every other slide keeps its quick start (Cubic, Out).
    private Tween.TransitionType SlideTransition => _panelStep == PanelStep.Move ? ShuffleTransition : Tween.TransitionType.Cubic;
    private Tween.EaseType SlideEase => _panelStep == PanelStep.Move ? ShuffleEase : Tween.EaseType.Out;
    private PanelStep _panelStep;
    private int _panelSteps;                      // counts sequences, so a cancelled one's later steps do nothing
    private ValueList<Faction> _shownStack = ValueList<Faction>.Empty;
    private Faction? _shownActive;

    private void StartPanelSteps()
    {
        _panelStep = PanelStep.Collapse;
        int sequence = ++_panelSteps;
        GameState at = _history.Current;
        void Next(PanelStep step)
        {
            if (sequence != _panelSteps || !ReferenceEquals(_history.Current, at))
                return;
            _panelStep = step;
            Render(animate: true);
        }
        GetTree().CreateTimer(MoveSeconds).Timeout += () => Next(PanelStep.Move);
        GetTree().CreateTimer(MoveSeconds + PanelShuffleSeconds).Timeout += () => Next(PanelStep.None);
    }

    // Any render not made by the steps themselves: the steps still to come are dropped.
    private void CancelPanelSteps()
    {
        _panelSteps++;
        _panelStep = PanelStep.None;
    }

    private void RenderPanels(GameState state, bool animate)
    {
        if (animate && _panelStep == PanelStep.None && _shownActive is Faction was && state.ActivePlayer is Faction now
            && now != was && state.Phase == Phase.Playing && !IsFixedStack(state))
            StartPanelSteps();
        _panelTargets.Clear();
        if (_inspected is null && !ReferenceEquals(state, _raisedState))
            _raised = null;
        // The active player may already have passed; they stay on top (once) until their turn ends.
        // Without variable turn order the panels keep the turn order, cycling from the active player, passed players
        // included (they are skipped, not moved).
        // Setup, the turn-order step and Free: the boards keep the turn order (MainModes.cs).
        ValueList<Faction> stack = IsFixedStack(state) ? state.TurnOrder
            : state.Options.VariableTurnOrder || state.ActivePlayer is not Faction active
            ? new(state.TurnQueue.Concat(state.PassOrder.Where(f => !state.TurnQueue.Contains(f))))
            : new(Enumerable.Range(0, state.TurnOrder.Count)
                .Select(i => state.TurnOrder[(state.TurnOrder.IndexOf(active) + i) % state.TurnOrder.Count]));
        if (_panelStep == PanelStep.Collapse && _shownStack.Count == stack.Count)
            stack = _shownStack; // collapsing where they are: they move in the next step
        else
            _shownStack = stack;
        _shownActive = state.ActivePlayer;

        List<ScorePreview> scores = EndGame.Preview(state); // the score badges (MainScorePreview.cs)
        for (var i = 0; i < _panels.Length; i++)
        {
            PlayerPanel panel = _panels[i];
            if (i >= state.Players.Count)
            {
                panel.ShowEmpty();
                if (panel == _inspected)
                    _inspected = null;
                continue;
            }

            PlayerState player = state.Players[i];
            if (panel.Board is BuildingBoard landscapeBoard)
                landscapeBoard.Landscapes = state.Options.Landscapes; // the dwelling row's well for the landscape
            ShowInFlightOn(panel, player.Faction); // points, coins and workers still to land (MainPoints.cs)
            // No income shown in the last round (or once the game is over): there is no round after it.
            panel.ShowPlayer(player, ShownPassed(state, player.Faction), animate ? MoveSeconds : 0f, PredictedIncome(state, player),
                showIncome: state.Round < Config.Rounds);
            panel.ShowScores(scores[i], player.VictoryPoints, Art.PieceColour(player.Colour));
            // The turn-order step: the boards clicked so far outlined in white.
            panel.ShowBoardHighlight(state.Phase == Phase.OrderingTurns && (state.StartOrder?.Contains(player.Faction) ?? false));
            // The player button (on the active player's panel): a tick once the rules let the turn end (action taken,
            // favours, spades, bridge, towns, power offers settled), dots while the action is under way, else blank.
            panel.ShowPlayerButton(_bonus is { } rivers && rivers.Bonus.Action == BonusAction.RiverTokens && rivers.Faction == player.Faction
                    || _flowFrom is not null && player.Faction == Faction.KingdomOfEmber // Done: the lava's path
                    ? PlayerButton.Mode.Confirm // Done: one token, or none (MainFanFactionsOnMap.cs)
                    : PlayerButtonMode(state, player.Faction, _bonus?.Faction == player.Faction),
                Art.PieceColour(player.Colour), animate);
            panel.ShowBridgeSupply(state.BridgesLeft(player.Faction), player.Colour, player.BridgesOwed > 0);
            BonusActionId? selected = _bonus is { } bonus && bonus.Faction == player.Faction ? bonus.Bonus : null;
            panel.ShowBonusActions(player.BonusActions, player.BonusActionsUsed, selected, FanBook(state, player));
            panel.ShowSafeOffer(SafeLeft(state, player)); // the Treasurers (MainFanFactions.cs)
            panel.ShowPriestExchange(state.Phase == Phase.Playing && state.ActivePlayer == player.Faction && player.PriestExchangesLeft > 0);
            ShowUpgrades(panel, state, player);
            // The Riverwalkers choosing what a priest does: full size and on top, even out of turn.
            // (Fan factions: the Treasurers choosing what goes into their Safe too.)
            bool choosingPriest = player.PriestChoicesOwed > 0
                || (player.Faction == Faction.Treasurers && state.TreasuryOffer is not null);
            if (choosingPriest)
            {
                _raised = panel;
                _raisedState = state;
            }
            ShowTerrainChoices(panel, state, player, animate); // the terrain wheel, the Shapeshifters' chips (MainFireAndIce.cs)

            // Full size for the player whose turn it is and for one being inspected; compact for the rest (and for
            // everyone while the panels collapse and move at the end of a turn).
            bool isActive = state.ActivePlayer == player.Faction && _panelStep == PanelStep.None;
            if (isActive && panel == _inspected)
                _inspected = null; // their turn now: full size anyway
            bool compact = !isActive && panel != _inspected && !choosingPriest;
            panel.SetLayout(compact, animate ? MoveSeconds : 0f);
            panel.ZIndex = panel == _raised ? RaisedPanelZ : compact ? 0 : OpenPanelZ;

            // Outside play the stack is empty; then panels simply sit in player order.
            int place = stack.IndexOf(player.Faction);
            Vector2 target = state.Phase == Phase.Finished ? GameOverSlot(place >= 0 ? place : i, panel)
                : IsFixedStack(state) && place >= 0 ? FixedSlot(state, place, panel) : _panelSlots[place >= 0 ? place : i];
            target.Y += CalculatorGainShift(state, player.Faction); // Calculator's +1 panel above it (MainCalculatorGain.cs)
            // A compact panel opened to full size grows downwards from its top; if that would run off the bottom of the
            // screen (the last of five), it opens upwards instead: its bottom stays where the compact panel's was.
            if (!compact && !isActive)
            {
                float full = FactionBoardArt.BackingLayout(false).Size.Y * panel.Scale.Y;
                float small = FactionBoardArt.BackingLayout(true).Size.Y * panel.Scale.Y;
                if (_board.ToGlobal(target + new Vector2(0, full)).Y > GetViewportRect().End.Y)
                    target.Y -= full - small;
            }
            _panelTargets[panel] = target;
            Slide(panel, target, animate);
        }
    }

    // A passed player's panel (and what is on it) is greyed out, but not once the game is over.
    private static bool ShownPassed(GameState state, Faction faction) =>
        state.Phase != Phase.Finished && state.HasPassed(faction);

    // Once the game is over every board is compact (unless inspected), stacked from the first slot with no gaps.
    private Vector2 GameOverSlot(int place, PlayerPanel panel)
    {
        float small = FactionBoardArt.BackingLayout(true).Size.Y * panel.Scale.Y;
        return new Vector2(_panelSlots[Mathf.Min(place, _panelSlots.Length - 1)].X, _panelSlots[0].Y + place * small);
    }

    // Where one piece should be drawn: next to which node, nudged by how much, at what drawing depth,
    // and on which panel (null if it is on the map or a priest zone).
    // Offset: in the Pieces layer's pixels; on a panel, in the anchor's own pixels (so it shrinks with it).
    private readonly record struct Placement(Node2D Anchor, Vector2 Offset, int Depth, PlayerPanel Panel);

    // Drawing depths on the map, row by row, so what stands on a hex in front (lower on the screen) is drawn over what
    // stands behind it: a hex's lava stack (and the Wyrm's tokens) at MapGroundDepth, its structure just over that
    // (MapDepth), both over everything on the rows behind; bridges and their spots over all of it (MapTopDepth). All
    // under the open panels (OpenPanelZ).
    private static int MapGroundDepth(GameState state, int hex) => 2 * state.Layout.Position(hex).Row;
    private static int MapDepth(GameState state, int hex) => MapGroundDepth(state, hex) + 1;
    private static int MapTopDepth(GameState state) => 2 * state.Layout.RowCount + 1;

    // The compact layout's building row, from the left.
    private static readonly PieceKind[] CompactRowOrder =
        { PieceKind.Stronghold, PieceKind.TradingPost, PieceKind.Dwelling, PieceKind.Temple, PieceKind.Sanctuary };

    // Reads the state and answers: where does every piece belong on screen?
    private Dictionary<Piece, Placement> PlaceEveryPiece(GameState state)
    {
        var result = new Dictionary<Piece, Placement>();

        for (var p = 0; p < state.Players.Count; p++)
        {
            PlayerState player = state.Players[p];
            PlayerPanel panel = _panels[p];

            // Compact (with a building row): the buildings still in the banks side by side, each drawn over
            // the one before. Full size: each in its bank slot.
            Node2D row = panel.IsCompact ? panel.CompactBuildingRow : null;
            float dwellingsCentre = 0; // compact: the middle of the dwellings' stretch (the special landscape's place)
            if (row is not null)
            {
                // Each building CompactBuildingSpacing after the one before; the first of each kind
                // CompactGroupSpacing after the last of the kind before.
                var place = 0;
                var x = 0f;
                foreach (PieceKind kind in CompactRowOrder)
                {
                    var first = true;
                    float? from = null;
                    foreach (Piece? inBank in player.Bank(kind))
                        if (inBank is Piece building)
                        {
                            if (place > 0)
                                x += first ? panel.CompactGroupSpacing : panel.CompactBuildingSpacing;
                            result[building] = new Placement(row, new Vector2(x, 0), place + 1, panel); // over the landscape (0)
                            from ??= x;
                            place++;
                            first = false;
                        }
                    if (kind == PieceKind.Dwelling) // none left: where they would start
                        dwellingsCentre = from is float start ? (start + x) / 2 : place > 0 ? x + panel.CompactGroupSpacing / 2 : 0;
                }
            }
            else
            {
                foreach (PieceKind kind in Buildings.All)
                {
                    ValueList<Piece?> bank = player.Bank(kind);
                    for (var slot = 0; slot < bank.Count; slot++)
                        if (bank[slot] is Piece building)
                            result[building] = new Placement(panel.BankSlot(kind, slot), Vector2.Zero, 0, panel);
                }
            }
            // Fan factions: the Changelings' 9th dwelling beside their stronghold until built (compact: after the row).
            if (player.ExtraDwelling is Piece extra)
                result[extra] = row is not null
                    ? new Placement(row, new Vector2(result.Values.Where(at => at.Anchor == row).Select(at => at.Offset.X).DefaultIfEmpty(0).Max()
                        + panel.CompactBuildingSpacing, 0), result.Values.Count(at => at.Anchor == row) + 1, panel) // last (kept under open boards)
                    : new Placement(panel.BankSlot(PieceKind.Dwelling, Buildings.PerPlayer(PieceKind.Dwelling)), Vector2.Zero, 0, panel);

            // The special landscape in its well until it is placed (Landscapes expansion).
            // Collapsed: centred on the dwellings' stretch of the row, behind the buildings there (depth 0: above the
            // compact panel itself, which is at 0 too; the row's buildings from 1).
            if (state.Options.Landscapes && !state.HasLandscape(player.Faction))
            {
                if (row is not null)
                    result[LandscapePiece(player.Faction)] = new Placement(row, new Vector2(dwellingsCentre, 0), 0, panel);
                else if (panel.Board?.LandscapeSlot is Node2D well)
                    result[LandscapePiece(player.Faction)] = new Placement(well, Vector2.Zero, 0, panel);
            }

            // Stacked priests (in the resources, in both layouts): each one a little up and to the right,
            // later ones drawn on top.
            for (var i = 0; i < player.Priests.Count; i++)
                result[player.Priests[i]] = new Placement(panel.PriestStack, PriestStackOffset * i, i, panel);

            // The supply under the portrait: the next priest to be gained (index 0) is drawn on top.
            int supplyCount = player.PriestSupply.Count;
            for (var i = 0; i < supplyCount; i++)
                result[player.PriestSupply[i]] = new Placement(
                    panel.PriestSupply, PriestSupplyOffset * (supplyCount - 1 - i), supplyCount - i, panel);
        }

        for (var hex = 0; hex < state.Map.Count; hex++)
            if (state.Map[hex] is Piece building) // (on top of any lava: the Kingdom of Ember's)
                result[building] = new Placement(_map.Hex(hex), new Vector2(0, -LavaRaise(state, hex)), MapDepth(state, hex), null);

        foreach (Track track in AllTracks)
        {
            ValueList<Piece?> zone = state.PriestZones[(int)track];
            for (var slot = 0; slot < zone.Count; slot++)
                if (zone[slot] is Piece priest)
                    result[priest] = new Placement(PriestSlotNode(track, slot), Vector2.Zero, 0, null);
        }

        return result;
    }

    // The position (in the Pieces layer) a piece should end up at.
    // A panel may be sliding to a new place in the stack, and changing layout; pieces on it aim for where
    // their anchor WILL be when both have finished, so they travel with the panel instead of to where it was.
    private Vector2 TargetFor(Placement placement)
    {
        if (placement.Panel is not PlayerPanel panel)
            return _pieceLayer.ToLocal(placement.Anchor.GlobalPosition) + placement.Offset;

        Vector2 panelPosition = _panelTargets.TryGetValue(panel, out Vector2 target) ? target : panel.Position;
        Vector2 withinPanel = panel.FinalTransform(placement.Anchor) * placement.Offset * panel.Scale;
        return _pieceLayer.ToLocal(_board.ToGlobal(panelPosition + withinPanel));
    }

    // The size (in the Pieces layer) that matches the anchor's: its final size, on a panel changing layout.
    private Vector2 ScaleFor(Placement placement) =>
        placement.Panel is PlayerPanel panel
            ? panel.GlobalScale * panel.FinalTransform(placement.Anchor).Scale / _pieceLayer.GlobalScale
            : placement.Anchor.GlobalScale / _pieceLayer.GlobalScale;

    // A piece's size (its sprite's scale in the Pieces layer): map size, except on a panel, where it shrinks and
    // grows with the part it sits on, and in a building board's slot, where it is the board's BuildingSize.
    private Vector2 PieceSize(Placement placement)
    {
        if (placement.Panel is not PlayerPanel panel)
            return Vector2.One;
        if (panel.Board is BuildingBoard well && placement.Anchor == well.LandscapeSlot) // a landscape token in its well
            return ScaleFor(placement) * (well.LandscapeTokenSize / Mathf.Max(1, LandscapeTokenPixels));
        if (panel.Board is BuildingBoard board && board.IsSlot(placement.Anchor))
            return ScaleFor(placement) * board.PieceScale;
        return panel.ScaleFactor(placement.Anchor);
    }

    // How far above the rest a panel's pieces and tiles are drawn: only the raised panel's are.
    private int RaiseFor(PlayerPanel panel) =>
        panel is null ? 0 : panel == _raised ? RaisedPieceZ : panel.IsCompact ? 0 : OpenPieceZ;

    // Tints an item and fades it to `alpha`, gradually or (animate false) at once. The tint never resets the fade.
    private void TintAndFade(CanvasItem item, Color tint, float alpha, bool animate)
    {
        item.Modulate = new Color(tint, item.Modulate.A);
        _fader.To(item, alpha, animate ? MoveSeconds : 0f);
    }

    private void RenderPieces(GameState state, bool animate)
    {
        Dictionary<Piece, Placement> placements = PlaceEveryPiece(state);

        _panelPieces.Clear();
        _panelPieces.UnionWith(placements.Where(pair => pair.Value.Panel is not null).Select(pair => pair.Key));

        // Pieces that no longer exist (e.g. a faction removed, a purchase undone) are removed.
        foreach (Piece gone in _pieceSprites.Keys.Except(placements.Keys).ToList())
        {
            RemoveSprite(_pieceSprites[gone]);
            _pieceSprites.Remove(gone);
        }

        foreach (var (piece, placement) in placements)
        {
            Vector2 target = TargetFor(placement);
            // A priest just taken off the Riverwalkers' wheel starts where it stood there.
            Vector2 start = _wheelPriestSpots.TryGetValue(piece, out Vector2 spot) ? _pieceLayer.ToLocal(spot) : target;
            Sprite2D sprite = SpriteFor(_pieceSprites, piece, start);
            sprite.Texture = piece.Kind == PieceKind.Landscape
                ? LandscapeArt.Token(piece.Owner, LandscapeTokenPixels, marks: placement.Panel is not null) // as big as a map hex
                                                                                                             // takes it (MainLandscapes.cs);
                                                                                                             // placed: without what it needs
                : Art.PieceTexture(piece.Kind, state[piece.Owner].Colour);
            if (piece.Owner == Faction.Changelings && piece.Kind is not (PieceKind.Landscape or PieceKind.Priest))
                ShowChangelingPiece(sprite, piece, state[piece.Owner]); // its letter's colour, an A halo (MainFanFactionsEmberGeologistsChangelings.cs)
            sprite.ZIndex = placement.Depth + RaiseFor(placement.Panel);
            // Priests still in the supply are greyed out; so is everything on a passed player's panel (both,
            // for the supply of a passed player).
            bool inSupply = piece.Kind == PieceKind.Priest && state[piece.Owner].PriestSupply.Contains(piece);
            bool onPassedPanel = placement.Panel is not null && ShownPassed(state, piece.Owner);
            Color tint = (inSupply ? SupplyTint : Colors.White) * (onPassedPanel ? PlayerPanel.PassedTint : Colors.White);
            // On a panel, a piece fades with the part it sits on (if that part is hidden in the compact layout).
            bool shown = placement.Panel?.IsShownInLayout(placement.Anchor) ?? true;
            TintAndFade(sprite, tint, shown ? 1f : 0f, animate);
            // Priests in the priest zones are always full size (the zones shrink, they don't); pieces on a panel
            // shrink and grow with the part they sit on (see PieceSize).
            if (IsInPriestZone(piece, placement))
            {
                StopResize(sprite);
                sprite.Scale = Vector2.One;
                sprite.ZIndex = PriestInZoneZ;
            }
            else
                Resize(sprite, PieceSize(placement), animate);
            if (animate && placement.Panel is null && _map.FlipSecondsLeft(placement.Anchor) is not null)
                HoldThenPlace(sprite, target, placement.Anchor);
            else
                Slide(sprite, target, animate);
        }
        RememberWheelPriests(state);
        RenderPriestDebt(state, animate); // red outlines for priests owed (MainModes.cs)
        RenderNeutral(state); // Calculator's neutral pieces and bank (MainNeutral.cs)
        RenderCalculatorGain(state); // Calculator's +1 panel (MainCalculatorGain.cs)
    }

    // A piece put on a hex that is being transformed: it moves (at DwellingSpeed) to a point HoldHeight above the
    // hex; the hex waits for it, then rises, turns over and sinks back (MapView); once the hex has landed, the piece
    // drops onto it (at DwellingSpeed). Rendering again part-way through carries on from where the piece is.
    private void HoldThenPlace(Node2D sprite, Vector2 target, Node2D hex)
    {
        if (Going(sprite, target, held: true))
            return; // already on its way down onto this hex: it carries on
        StopSlide(sprite);
        float toPieces = _map.GlobalScale.Y / _pieceLayer.GlobalScale.Y; // map pixels to piece-layer pixels
        float height = HoldHeight * toPieces, speed = Mathf.Max(1, DwellingSpeed * toPieces);
        Vector2 held = target - new Vector2(0, height);
        // From afar (a starting dwelling clicked onto its hex from the panel) it travels as any other piece does (Slide).
        float glide = sprite.Position.DistanceTo(held) / speed, drop = height / speed;
        bool far = glide > MoveSeconds;
        glide = Mathf.Min(glide, MoveSeconds);
        _map.DelayFlip(hex, glide);
        float wait = Mathf.Max(0, (_map.FlipSecondsLeft(hex) ?? 0) - glide);
        Tween tween = sprite.CreateTween();
        if (glide > 0.001f)
            tween.TweenProperty(sprite, "position", held, glide)
                .SetTrans(far ? Tween.TransitionType.Cubic : Tween.TransitionType.Quad)
                .SetEase(far ? Tween.EaseType.Out : Tween.EaseType.InOut);
        if (wait > 0)
            tween.TweenInterval(wait);
        tween.TweenProperty(sprite, "position", target, Mathf.Max(0.001f, drop)).SetTrans(Tween.TransitionType.Linear);
        _tweens[sprite] = tween;
        _tweenTargets[sprite] = (target, true);
    }

    // Where each running slide (_tweens) is going, and whether it is a hold-then-place.
    private readonly Dictionary<Node2D, (Vector2 Target, bool Held)> _tweenTargets = new();
    private readonly Dictionary<Node2D, Vector2> _scaleTargets = new();

    // A slide already running to this target (rendering again part-way through, e.g. a redo, lets it carry on).
    private bool Going(Node2D node, Vector2 target, bool held) =>
        _tweens.TryGetValue(node, out Tween running) && running.IsValid() && running.IsRunning()
        && _tweenTargets.TryGetValue(node, out var going) && going == (target, held);

    // The existing sprite for this key, or a new one created directly at its target.
    private Sprite2D SpriteFor<TKey>(Dictionary<TKey, Sprite2D> sprites, TKey key, Vector2 target)
    {
        if (!sprites.TryGetValue(key, out Sprite2D sprite))
        {
            sprite = new Sprite2D { Position = target };
            _pieceLayer.AddChild(sprite);
            sprites[key] = sprite;
        }
        return sprite;
    }

    private void RemoveSprite(Node2D sprite)
    {
        StopSlide(sprite);
        StopResize(sprite);
        _fader.Forget(sprite);
        sprite.QueueFree();
    }

    // Like Slide, for size: a favour shrinks or grows as it flies between the bank and a panel.
    private void Resize(Node2D node, Vector2 scale, bool animate)
    {
        if (animate && _scaleTweens.TryGetValue(node, out Tween going) && going.IsValid() && going.IsRunning()
            && _scaleTargets.TryGetValue(node, out Vector2 to) && to == scale)
            return; // already growing or shrinking to this: it carries on
        StopResize(node);
        if (!animate || node.Scale == scale)
        {
            node.Scale = scale;
            return;
        }

        Tween tween = node.CreateTween();
        tween.TweenProperty(node, "scale", scale, SlideSeconds)
             .SetTrans(SlideTransition)
             .SetEase(SlideEase);
        _scaleTweens[node] = tween;
        _scaleTargets[node] = scale;
    }

    private void Slide(Node2D node, Vector2 target, bool animate)
    {
        if (animate && Going(node, target, held: false))
            return; // already on its way there: it carries on
        StopSlide(node);
        if (!animate || node.Position == target)
        {
            node.Position = target;
            return;
        }

        Tween tween = node.CreateTween();
        tween.TweenProperty(node, "position", target, SlideSeconds)
             .SetTrans(SlideTransition)
             .SetEase(SlideEase);
        _tweens[node] = tween;
        _tweenTargets[node] = (target, false);
    }

    private void StopSlide(Node2D node)
    {
        _tweenTargets.Remove(node);
        if (_tweens.Remove(node, out Tween running))
            running.Kill();
    }

    private void StopResize(Node2D node)
    {
        _scaleTargets.Remove(node);
        if (_scaleTweens.Remove(node, out Tween running))
            running.Kill();
    }
}
