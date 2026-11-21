using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// Calculator: the neutral opponent (Core: Rules.Calculator.cs, GameState.Neutral). Its bank (NeutralBank) sits where the
// other players' boards would be. At any time once the game has started:
//   drag its priest to a free priest slot (no steps), its bridge to a free bridge spot (lit while dragging), its
//     dwelling to any empty land hex (the hex shows in the power purple);
//   drag a neutral piece on the board to another place, or back onto the bank to take it away; right-click takes it away.
public partial class Main
{
    private NeutralBank _neutralBank;
    private readonly Dictionary<int, Sprite2D> _neutralDwellings = new();
    private readonly Dictionary<(int HexA, int HexB), Sprite2D> _neutralBridges = new();
    private readonly Dictionary<(Track Track, int Slot), Sprite2D> _neutralPriests = new();
    private NeutralPiece? _neutralDragged;       // the kind being dragged
    private Sprite2D _neutralDragSprite;
    private object _neutralLifted;               // where it was lifted from (hex, edge or slot); null: from the bank
    private readonly List<((int HexA, int HexB) Edge, Sprite2D Sprite)> _neutralSpots = new();

    // %NeutralBank if the scene has one; otherwise made at the second board's place.
    private void ReadyNeutralBank()
    {
        _neutralBank = GetNodeOrNull<NeutralBank>("%NeutralBank");
        if (_neutralBank is null)
        {
            _neutralBank = new NeutralBank { Name = "NeutralBank", Scale = _panels[0].Scale };
            _board.AddChild(_neutralBank);
            Vector2 slot = _panelSlots.Length > 1 ? _panelSlots[1] : _panelSlots[0];
            _neutralBank.Position = slot + _neutralBank.BankSize / 2 * _neutralBank.Scale + NeutralBankOffset;
        }
        _neutralBank.Visible = false;
    }

    private void RenderNeutral(GameState state)
    {
        if (_neutralBank is null)
            return;
        bool calculator = state.Options.Calculator && _board.Visible;
        _neutralBank.Visible = calculator;
        if (calculator)
            _map.ShowNeutral(state.Neutrals.Hexes, NeutralBank.Purple);

        Sync<int>(_neutralDwellings, calculator ? state.Neutrals.Hexes : Enumerable.Empty<int>(), NeutralPiece.Dwelling, (sprite, hex) =>
        {
            sprite.GlobalPosition = _map.Hex(hex).GlobalPosition;
            sprite.ZIndex = MapDepth(state, hex); // row by row, as the structures (MainPieces)
        });
        Sync<(int HexA, int HexB)>(_neutralBridges, calculator ? state.Neutrals.Bridges : Enumerable.Empty<(int HexA, int HexB)>(), NeutralPiece.Bridge, (sprite, edge) =>
        {
            sprite.ZIndex = MapTopDepth(state);
            sprite.Texture = HexArt.Bridge(NeutralBank.Purple, NeutralBank.PieceOutline);
            PlaceAcross(sprite, edge.HexA, edge.HexB);
        });
        Sync<(Track Track, int Slot)>(_neutralPriests, calculator ? state.Neutrals.Priests : Enumerable.Empty<(Track Track, int Slot)>(), NeutralPiece.Priest, (sprite, slot) =>
        {
            sprite.GlobalPosition = PriestSlotNode(slot.Track, slot.Slot).GlobalPosition;
            sprite.ZIndex = PriestInZoneZ;
        });
    }

    private void Sync<TKey>(Dictionary<TKey, Sprite2D> sprites, IEnumerable<TKey> wanted, NeutralPiece kind, System.Action<Sprite2D, TKey> place)
    {
        var keep = wanted.ToHashSet();
        foreach (TKey gone in sprites.Keys.Where(key => !keep.Contains(key)).ToList())
        {
            sprites[gone].QueueFree();
            sprites.Remove(gone);
        }
        foreach (TKey key in keep)
        {
            if (!sprites.TryGetValue(key, out Sprite2D sprite))
            {
                sprites[key] = sprite = new Sprite2D { Texture = NeutralBank.Texture(kind), ZIndex = 1 };
                _pieceLayer.AddChild(sprite);
            }
            sprite.Visible = true;
            place(sprite, key);
        }
    }

    // Presses, drags and releases for the neutral pieces (from OnModeInput). True if the event was taken.
    private bool OnNeutralInput(InputEvent e, GameState state, Vector2 mouse)
    {
        if (!state.Options.Calculator || !_board.Visible)
            return false;
        if (e is InputEventMouseMotion)
        {
            if (_neutralDragSprite is null)
                return false;
            _neutralDragSprite.GlobalPosition = mouse;
            return true;
        }
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button)
            return false;
        if (!button.Pressed)
        {
            if (_neutralDragSprite is null || button.ButtonIndex != MouseButton.Left)
                return false;
            GetViewport().SetInputAsHandled();
            EndNeutralDrag(mouse);
            return true;
        }
        if (IsOverTopPanel() || _wheel.Visible || _upgradeChoice is not null || AnyMenuOpen)
            return false;
        bool right = button.ButtonIndex == MouseButton.Right;

        Move remove = null;
        NeutralPiece? lift = null;
        object from = null;
        if (!right && _neutralBank.PieceAt(mouse) is NeutralPiece fromBank)
            lift = fromBank;
        else if (_neutralPriests.Where(pair => IsUnder(pair.Value, mouse)).Select(pair => ((Track Track, int Slot)?)pair.Key).FirstOrDefault() is { } slot)
            (lift, from, remove) = (NeutralPiece.Priest, slot, new RemoveNeutralPriest(slot.Track, slot.Slot));
        else if (_neutralBridges.Where(pair => pair.Value.GlobalPosition.DistanceTo(mouse) <= SnapRadius).Select(pair => ((int, int)?)pair.Key).FirstOrDefault() is (int a, int b))
            (lift, from, remove) = (NeutralPiece.Bridge, (a, b), new RemoveNeutralBridge(a, b));
        else if (_map.HexAt(mouse) is int hex && state.Neutrals.Hexes.Contains(hex))
            (lift, from, remove) = (NeutralPiece.Dwelling, hex, new RemoveNeutralDwelling(hex));
        if (lift is null)
            return false;

        GetViewport().SetInputAsHandled();
        if (right)
        {
            if (remove is not null)
                Submit(remove);
            return true;
        }
        StartNeutralDrag(state, lift.Value, from, mouse);
        return true;
    }

    private void StartNeutralDrag(GameState state, NeutralPiece piece, object from, Vector2 mouse)
    {
        _neutralDragged = piece;
        _neutralLifted = from;
        _neutralDragSprite = new Sprite2D { Texture = NeutralBank.Texture(piece), ZIndex = 100, Scale = _pieceLayer.GlobalScale };
        AddChild(_neutralDragSprite);
        _neutralDragSprite.GlobalPosition = mouse;
        Sprite2D lifted = from switch
        {
            int hex => _neutralDwellings.GetValueOrDefault(hex),
            (int a, int b) => _neutralBridges.GetValueOrDefault((a, b)),
            (Track track, int slot) => _neutralPriests.GetValueOrDefault((track, slot)),
            _ => null,
        };
        if (lifted is not null)
            lifted.Visible = false;
        if (piece == NeutralPiece.Priest)
            _priestZones.Expand();
        if (piece == NeutralPiece.Bridge)
            foreach ((int HexA, int HexB) edge in Rules.BridgeEdges(state).Where(edge => !state.IsBridged(edge.HexA, edge.HexB)))
            {
                var spot = new Sprite2D { Texture = Art.BridgeSpotTexture(), ZIndex = 2 };
                _pieceLayer.AddChild(spot);
                PlaceAcross(spot, edge.HexA, edge.HexB);
                _neutralSpots.Add((edge, spot));
            }
    }

    // Onto the bank: taken away (if it came off the board). Onto a free place of its kind: placed there (moved, if it
    // came off the board). Anywhere else: nothing changes.
    private void EndNeutralDrag(Vector2 mouse)
    {
        NeutralPiece piece = _neutralDragged!.Value;
        object from = _neutralLifted;
        (int HexA, int HexB)? spot = _neutralSpots
            .Where(s => s.Sprite.GlobalPosition.DistanceTo(mouse) <= SnapRadius)
            .OrderBy(s => s.Sprite.GlobalPosition.DistanceTo(mouse))
            .Select(s => ((int HexA, int HexB)?)s.Edge).FirstOrDefault();
        foreach (var (_, sprite) in _neutralSpots)
            sprite.QueueFree();
        _neutralSpots.Clear();
        _neutralDragSprite.QueueFree();
        _neutralDragSprite = null;
        _neutralDragged = null;
        _neutralLifted = null;
        if (piece == NeutralPiece.Priest)
            _priestZones.Shrink();

        Move remove = from switch
        {
            int hex => new RemoveNeutralDwelling(hex),
            (int a, int b) => new RemoveNeutralBridge(a, b),
            (Track track, int slot) => new RemoveNeutralPriest(track, slot),
            _ => null,
        };
        Move place = piece switch
        {
            NeutralPiece.Priest when PriestSlotAt(mouse) is { } slot => new PlaceNeutralPriest(slot.Track, slot.Slot),
            NeutralPiece.Bridge when spot is (int a, int b) => new PlaceNeutralBridge(a, b),
            NeutralPiece.Dwelling when _map.HexAt(mouse) is int hex && !(from is int same && same == hex) => new PlaceNeutralDwelling(hex),
            _ => null,
        };
        bool onBank = _neutralBank.IsOver(mouse);
        if (remove is not null && (onBank || place is not null))
            Submit(remove);
        if (place is not null && !onBank)
            Submit(place);
        else if (remove is null || !onBank)
            Render(animate: false); // back where it was
    }
}
