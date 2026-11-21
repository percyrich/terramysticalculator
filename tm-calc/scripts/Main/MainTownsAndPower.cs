using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: key tokens and towns, special power actions, bridges, and power offers.
// A second file of the same class (partial), only to keep Main.cs a manageable size.
public partial class Main
{
    private static readonly Vector2 KeyStackOffset = new(6, -6);   // between tokens in one bank stack (marker pixels)
    // A placed token lies under its building, centred on the hex and almost filling it (a 125 px token in a
    // 128 px wide hex).
    private const float KeyOnMapScale = 0.94f;
    private const float ClickTolerance = 8f;                       // screen pixels: a press and release this close is a click

    private bool _finalScoresClosed;   // the end-of-game scores closed by the player; shown again if undone and re-finished
    private Node2D _keyBank;      // %KeyBank: Stack0..Stack7
    // %PowerActions: Action0..Action5. Each is a PowerActionView (power_action.tscn): From is the power it
    // costs, To an ActionToken around what it gives (crossed out once taken this round).
    private Node2D _powerActions;
    private readonly Dictionary<KeyTile, Node2D> _keySprites = new(); // KeyTileViews
    private readonly HashSet<KeyTile> _bankKeys = new();          // tokens still in the bank: the clickable ones
    private readonly Dictionary<Bridge, Sprite2D> _bridgeSprites = new();
    private readonly List<((int HexA, int HexB) Edge, Sprite2D Sprite)> _bridgeSpots = new();

    // A key token being dragged (or chosen by a click, waiting for a hex).
    private int? _draggedKey;
    private Vector2 _keyPressPoint;
    private int? _selectedKey;
    // A bridge being dragged from the active player's supply (a picture only), or chosen by a click.
    private Sprite2D _draggedBridge;
    private Vector2 _bridgePressPoint;
    private bool _bridgeSelected;

    private void ReadyTownsAndPower()
    {
        _keyBank = GetNode<Node2D>("%KeyBank");
        _powerActions = GetNode<Node2D>("%PowerActions");

        // Each power action shows its cost and what it gives.
        for (var action = 0; action < PowerActions.Count; action++)
            PowerActionNode(action).ShowAction(action);
    }

    private PowerActionView PowerActionNode(int action) => _powerActions.GetNode<PowerActionView>($"Action{action}");

    // ---------- Clicks ----------

    // Handles a press on a power action, a key token, a bridge in the supply, or a hex/spot while a token
    // or bridge is chosen. Returns false if the press was on none of these.
    private bool OnPressTownsAndPower(Vector2 mouse)
    {
        GameState state = _history.Current;

        if (PowerActionUnder(mouse) is int action)
        {
            if (state.ActivePlayer is Faction active)
                Submit(new TakePowerAction(active, action)); // refused with a reason if not possible
            return true;
        }

        if (KeyUnder(mouse) is int token)
        {
            _draggedKey = token;
            _keyPressPoint = mouse;
            _selectedKey = null;
            return true;
        }

        if (_selectedKey is int chosen && _map.HexAt(mouse) is int keyHex && state.ActivePlayer is Faction former)
        {
            _selectedKey = null;
            Submit(new FormTown(former, chosen, keyHex)); // a hex outside the town is refused with a reason
            return true;
        }

        if (state.ActivePlayer is Faction owner && PanelFor(owner) is PlayerPanel panel
            && panel.IsOverBridgeSupply(mouse) && state[owner].BridgesOwed > 0)
        {
            _draggedBridge = new Sprite2D
            {
                Texture = Art.BridgeIconTexture(state[owner].Colour),
                ZIndex = 100,
                Scale = _pieceLayer.GlobalScale,
            };
            AddChild(_draggedBridge);
            _draggedBridge.GlobalPosition = mouse;
            _bridgePressPoint = mouse;
            _bridgeSelected = false;
            return true;
        }

        if (BridgeSpotAt(mouse) is { } spot && state.ActivePlayer is Faction builder)
        {
            _bridgeSelected = false;
            Submit(new PlaceBridge(builder, spot.HexA, spot.HexB));
            return true;
        }

        return false;
    }

    private int? PowerActionUnder(Vector2 point) =>
        Enumerable.Range(0, PowerActions.Count)
            .Where(action => Hit.IsOver(PowerActionNode(action), point))
            .Select(action => (int?)action)
            .FirstOrDefault();

    // The kind of the topmost bank token under the point, if any.
    private int? KeyUnder(Vector2 point) =>
        _keySprites
            .Where(pair => _bankKeys.Contains(pair.Key) && Hit.IsOver(pair.Value, point))
            .OrderByDescending(pair => pair.Value.ZIndex)
            .Select(pair => (int?)pair.Key.Id)
            .FirstOrDefault();

    private (int HexA, int HexB)? BridgeSpotAt(Vector2 point) =>
        _bridgeSpots
            .Where(spot => spot.Sprite.GlobalPosition.DistanceTo(point) <= SnapRadius)
            .OrderBy(spot => spot.Sprite.GlobalPosition.DistanceTo(point))
            .Select(spot => ((int HexA, int HexB)?)spot.Edge)
            .FirstOrDefault();

    // ---------- Dragging ----------

    // Returns true while a key token or a bridge is being dragged (it follows the mouse).
    private bool MoveKeyOrBridgeDrag(Vector2 mouse)
    {
        if (MoveFanDrag(mouse)) // the Architects' bridge being moved (MainFanFactions.cs)
            return true;
        if (_draggedKey is int token)
        {
            KeyTile top = _history.Current.KeyBank[token][^1];
            if (_keySprites.TryGetValue(top, out Node2D sprite))
            {
                sprite.ZIndex = 100;
                sprite.GlobalPosition = mouse;
            }
            return true;
        }
        if (_draggedBridge is not null)
        {
            _draggedBridge.GlobalPosition = mouse;
            return true;
        }
        return false;
    }

    // A drop on a hex forms the town (or a spot places the bridge). A release where it was pressed is a
    // click: the token or bridge is chosen, and the next click on a hex or spot uses it.
    private void EndKeyOrBridgeDrag()
    {
        if (EndFanDrag()) // the Architects' bridge being moved (MainFanFactions.cs)
            return;
        Vector2 mouse = GetGlobalMousePosition();
        GameState state = _history.Current;

        if (_draggedKey is int token)
        {
            _draggedKey = null;
            bool clicked = mouse.DistanceTo(_keyPressPoint) <= ClickTolerance;
            if (clicked)
            {
                _selectedKey = token;
                ShowPrompt("Click a highlighted hex to form the town there (Esc to cancel).");
                Render(animate: true);
            }
            else if (_map.HexAt(mouse) is int hex && state.ActivePlayer is Faction active)
                Submit(new FormTown(active, token, hex)); // refused with a reason off the town; slides back
            else
                Render(animate: true);
            return;
        }

        if (_draggedBridge is not null)
        {
            _draggedBridge.QueueFree();
            _draggedBridge = null;
            if (mouse.DistanceTo(_bridgePressPoint) <= ClickTolerance)
            {
                _bridgeSelected = true;
                ShowPrompt("Click a highlighted spot to place the bridge (Esc to cancel).");
            }
            else if (BridgeSpotAt(mouse) is { } spot && state.ActivePlayer is Faction active)
                Submit(new PlaceBridge(active, spot.HexA, spot.HexB));
        }
    }

    private bool CancelKeyOrBridgeSelection()
    {
        if (_selectedKey is null && !_bridgeSelected)
            return false;
        _selectedKey = null;
        _bridgeSelected = false;
        ShowNote("");
        return true;
    }

    // ---------- Drawing ----------

    private void RenderTownsAndPower(GameState state, bool animate)
    {
        RenderKeys(state, animate);
        RenderPowerActions(state);
        RenderBridges(state);
        RenderTownRings(state);
        RenderPowerOffers(state);
        RenderFinalScores(state);
    }

    // A ring round every town: each group of a player's buildings (joined by adjacency or their bridges) that
    // holds a building of one of their towns. A building added next to a town joins its group, so the ring grows.
    // A town the active player could form now (waiting for its key) has the ring too, with a bright band drifting
    // across it (no sparkles); once the key is placed it is a town like the others: its ring steady.
    private void RenderTownRings(GameState state)
    {
        var towns = new List<(IReadOnlyCollection<int>, bool)>();
        if (state.Phase == Phase.Playing && state.ActivePlayer is Faction forming)
            foreach (ValueList<int> formable in Rules.FormableTowns(state, forming))
                towns.Add((formable.ToList(), true));
        foreach (PlayerState player in state.Players)
        {
            var built = new HashSet<int>(state.HexesBuiltOnBy(player.Faction));
            var seen = new HashSet<int>();
            foreach (int start in player.TownHexes)
            {
                if (!built.Contains(start) || !seen.Add(start))
                    continue;
                var group = new List<int> { start };
                for (var i = 0; i < group.Count; i++)
                    foreach (int n in Rules.AdjacentHexes(state, player.Faction, group[i]))
                        if (built.Contains(n) && seen.Add(n))
                            group.Add(n);
                towns.Add((group, false));
            }
        }
        _map.ShowTowns(towns);
    }

    // Every waiting power offer lights the edges between the hex built on and the offered player's buildings next
    // to it, in their colour; the first (the one to answer now) also shows its chip with the power it would bring
    // (as much as fits in the player's bowls). Answered by clicking (see OnPress).
    private void RenderPowerOffers(GameState state)
    {
        var offers = new List<(Color, int, IReadOnlyList<int>, int)>();
        foreach (PowerOffer offer in state.PowerOffers)
        {
            IReadOnlyList<int> neighbours = Rules.AdjacentHexes(state, offer.Player, offer.Hex)
                .Where(n => state.Map[n] is { } piece && piece.Owner == offer.Player)
                .Distinct()
                .ToList();
            int gain = Math.Min(Math.Min(offer.Power, state[offer.Player].Power.GainCapacity),
                Rules.AffordableGain(state, offer.Player)); // the Firewalkers' pyro caps it (fan factions)
            offers.Add((Art.PieceColour(state[offer.Player].Colour), offer.Hex, neighbours, gain));
        }
        // The Nomads may take a coin instead, for a build next to their Trading Camp: its chip beside the power's.
        bool coin = state.PowerOffers.Count > 0 && Rules.Apply(state, new TakeCoinInstead(state.PowerOffers[0].Player)).Accepted;
        _map.ShowPowerOffers(offers, coin);
    }

    // The end of the game: the scores and statistics (EndgameScores, MainEndgame.cs) open once the game is finished,
    // until closed.
    private void RenderFinalScores(GameState state)
    {
        if (state.Phase != Phase.Finished)
        {
            CloseEndgame(); // (an undo back from the end; its Closed marks it closed, so this comes first)
            _finalScoresClosed = false;
            return;
        }
        if (!_finalScoresClosed && _endgame?.IsOpen != true)
            OpenEndgame();
    }

    // Key tokens: stacked in the bank, or on the hex where they formed a town. The bank glows while the
    // active player has a town to form.
    private void RenderKeys(GameState state, bool animate)
    {
        var placements = new Dictionary<KeyTile, (Placement Placement, float Scale)>();
        var onTop = new HashSet<KeyTile>(); // the top token of each bank stack
        bool townToForm = state.Phase == Phase.Playing && state.ActivePlayer is Faction active
            && (Rules.FormableTowns(state, active).Count > 0 || RiverTownsNow(state).Count > 0)
            || state.IsInGame(Faction.Atlanteans) && state[Faction.Atlanteans].StartingTownOwed; // their starting town (fan factions)
        for (var id = 0; id < state.KeyBank.Count; id++)
        {
            ValueList<KeyTile> stack = state.KeyBank[id];
            Node2D anchor = _keyBank.GetNode<Node2D>($"Stack{id}");
            for (var i = 0; i < stack.Count; i++)
                placements[stack[i]] = (new Placement(anchor, KeyStackOffset * i, i, null), 1f);
            if (stack.Count > 0)
                onTop.Add(stack[^1]);
        }
        foreach (PlacedKey placed in state.PlacedKeys)
            placements[placed.Tile] = (new Placement(_map.Hex(placed.Hex), Vector2.Zero, 0, null), KeyOnMapScale);
        var onMap = new HashSet<KeyTile>(state.PlacedKeys.Select(placed => placed.Tile));

        _bankKeys.Clear();
        _bankKeys.UnionWith(state.KeyBank.SelectMany(stack => stack));

        foreach (KeyTile gone in _keySprites.Keys.Except(placements.Keys).ToList())
        {
            RemoveSprite(_keySprites[gone]);
            _keySprites.Remove(gone);
        }

        foreach (var (tile, (placement, extraScale)) in placements)
        {
            Vector2 scale = placement.Anchor.GlobalScale / _pieceLayer.GlobalScale * extraScale;
            Vector2 target = TargetFor(placement with { Offset = placement.Offset * scale });
            if (!_keySprites.TryGetValue(tile, out Node2D sprite))
            {
                sprite = KeyTileView.Create(tile.Id);
                sprite.Position = target;
                sprite.Scale = scale;
                _pieceLayer.AddChild(sprite);
                _keySprites[tile] = sprite;
            }
            sprite.ZIndex = placement.Depth;
            if (onMap.Contains(tile))
                _pieceLayer.MoveChild(sprite, 0); // drawn before (under) every piece on the map
            sprite.Modulate = _selectedKey == tile.Id && _bankKeys.Contains(tile) ? HighlightColour : Colors.White;
            EdgeHighlight.Set(sprite, townToForm && onTop.Contains(tile), HighlightArt.Colour.Yellow, glints: 1);
            Slide(sprite, target, animate);
            Resize(sprite, scale, animate);
        }
    }

    // Each power action: crossed out once taken this round, with a yellow edge highlight when the active player
    // could take it.
    // What each power action's cost shows now (the Yetis pay less while it is their turn).
    private readonly Dictionary<int, int> _powerActionCostShown = new();

    private void RenderPowerActions(GameState state)
    {
        for (var action = 0; action < PowerActions.Count; action++)
        {
            PowerActionView node = PowerActionNode(action);
            int cost = state.Phase == Phase.Playing && state.ActivePlayer is Faction payer
                ? Rules.PowerActionCost(state, payer, action)
                : PowerActions.All[action].Cost;
            if (_powerActionCostShown.GetValueOrDefault(action, PowerActions.All[action].Cost) != cost)
                node.ShowConversion(Amounts.Of(ResourceKind.PowerSpend, cost), PowerActionView.Gain(action));
            _powerActionCostShown[action] = cost;
            if (node.To is ActionToken token)
                token.IsUsed = state.PowerActionsTaken.Contains(action);
            bool available = state.ActivePlayer is Faction active
                && Rules.Apply(state, new TakePowerAction(active, action)).Accepted;
            EdgeHighlight.Set(node, available, HighlightArt.Colour.Yellow, glints: 0);
        }
    }

    // Placed bridges lie across the edge they span, from one hex centre towards the other. While the active
    // player must place one, every spot where it could go is marked.
    private void RenderBridges(GameState state)
    {
        foreach (Bridge gone in _bridgeSprites.Keys.Except(state.Bridges).ToList())
        {
            RemoveSprite(_bridgeSprites[gone]);
            _bridgeSprites.Remove(gone);
        }
        foreach (Bridge bridge in state.Bridges)
        {
            if (!_bridgeSprites.TryGetValue(bridge, out Sprite2D sprite))
            {
                sprite = new Sprite2D();
                _pieceLayer.AddChild(sprite);
                _bridgeSprites[bridge] = sprite;
            }
            sprite.ZIndex = MapTopDepth(state); // over the structures (MainPieces.MapDepth)
            sprite.Texture = Art.BridgeTexture(state[bridge.Owner].Colour); // the Shapeshifters' colour can change
            PlaceAcross(sprite, bridge.HexA, bridge.HexB);
        }

        foreach (var (_, spotSprite) in _bridgeSpots)
            spotSprite.QueueFree();
        _bridgeSpots.Clear();
        if (state.Phase == Phase.Playing && state.ActivePlayer is Faction active && state[active].BridgesOwed > 0)
        {
            foreach ((int HexA, int HexB) edge in Rules.BridgeSpots(state, active))
            {
                var spot = new Sprite2D { Texture = Art.BridgeSpotTexture(), ZIndex = MapTopDepth(state) + 1 };
                _pieceLayer.AddChild(spot);
                PlaceAcross(spot, edge.HexA, edge.HexB);
                _bridgeSpots.Add((edge, spot));
            }
        }
    }

    private void PlaceAcross(Sprite2D sprite, int hexA, int hexB)
    {
        Vector2 a = _map.Hex(hexA).GlobalPosition;
        Vector2 b = _map.Hex(hexB).GlobalPosition;
        sprite.GlobalPosition = (a + b) / 2;
        sprite.GlobalRotation = (b - a).Angle();
    }
}
