using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The colour wheel. The seven basic terrains as circles evenly spaced round a thick black
// ring, in the transformation cycle's order (Terrains.Wheel: red, yellow, brown, black, blue, green, grey), clockwise,
// turned so a chosen terrain (the player's home) is at the top. Nothing else is drawn, so the map shows through
// (unless BackingColour is set). Centred on this node. Three ways of showing it:
//   wheel.ShowFor(top, clicked, costOf)          what a clicked hex becomes: the hex's terrain gets a white
//                                                EdgeHighlight (ClickedGlints); costOf gives each terrain's cost
//   wheel.ShowFor(top, clicked, workersPerSpade) the same, costing spades × workersPerSpade workers
//   wheel.ShowChoices(top, choosable, colourOf)  a choice: those terrains lit (purple unless colourOf says); only they react
//   wheel.ShowStill(top)                         just the picture: no highlights, no clicks
//   wheel.ShowPicker(top, selected)              a palette (the custom map editor): every circle reacts, the middle
//                                                one (Centre) too; the selected one lit white (ClickedGlints)
// Hovering a terrain that reacts highlights it white too (HoverGlints) and shows, at CostOffset, what it costs as
// a ResourceBundle (nothing when it's free); a cost the player can't pay or make is written in the bundle's
// WarningColour. Clicking it emits TerrainChosen (and takes the click). Changing the top while a hex is shown turns
// the ring smoothly, keeping the clicked terrain's circle still while it turns.
// Extras, kept until changed:
//   Ring / RingIcon / RingChooses   a ring (IconArt.IceRing, VariableRing) over one terrain's circle, RingScale times
//                                   its size, moved by RingOffset; clicking the ring itself (not its hole) chooses
//                                   RingChooses (Ice for the ice factions), if set. RingOutside: beside the circle
//                                   instead, outward from the wheel's centre and touching it (above it at the top),
//                                   and then all of it (the hole too) chooses
//   Centre                          one more circle in the middle (at CentreOffset), e.g. Volcano; its cost is
//                                   shown at CentreCostOffset
//   SetDisabled(terrains)           those circles dimmed (DisabledColour)
//   SetPriests(terrains, colour)    a priest standing on those circles (PriestScale of a circle's height)
//   CentreOnly                      only the middle circle (Centre), where the top circle would be: no ring, no other
//                                   circles; its cost always shown at CostOffset (a volcano faction's one choice)
//   Spread                          0 to 1: how far the circles stand from the middle; at 0 only Focus (the top, if
//                                   not set) shows, as a single circle in the middle (a badge). Animate(spread,
//                                   scale, seconds) changes it and this node's scale together.
[GlobalClass]
public partial class ColourWheel : Node2D
{
    [Signal] public delegate void TerrainChosenEventHandler(Terrain terrain);

    [ExportGroup("Wheel")]
    [Export] public float RingRadius { get; set; } = 130f;   // to the circles' centres
    [Export] public float CircleRadius { get; set; } = 42f;
    [Export] public float RingWidth { get; set; } = 9f;
    [Export] public bool Anticlockwise { get; set; }          // false: the faction boards' direction
    [Export] public int ClickedGlints { get; set; }           // on the clicked terrain's highlight (0: none)
    [Export] public int HoverGlints { get; set; } = 1;        // on the hovered terrain's

    [ExportGroup("Cost")]
    [Export] public int CostIconSize { get; set; } = 46;
    [Export] public int CostFontSize { get; set; } = 34;
    [Export] public Vector2 CostOffset { get; set; }        // from the centre (the clicked hex's centre)
    [Export] public Vector2 CentreCostOffset { get; set; } = new(0, 70); // for the middle circle's cost

    [ExportGroup("Extras")]
    [Export] public float RingScale { get; set; } = 1f;       // the ring's size, of its circle's
    [Export] public Vector2 RingOffset { get; set; }          // from its circle's centre
    [Export] public Vector2 CentreOffset { get; set; }      // the middle circle (e.g. Volcano), from the centre
    [Export] public Color DisabledColour { get; set; } = new(0.42f, 0.42f, 0.42f, 0.8f);
    [Export] public float PriestScale { get; set; } = 0.9f;   // a priest's height, of a circle's diameter
    [Export] public Color BackingColour { get; set; } = new(0, 0, 0, 0); // a disc behind it all (clear: none)
    [Export] public float BackingMargin { get; set; } = 14f;
    // Fan factions: the Geologists' glowing rocks, one on each covered terrain's circle (RockScale of its diameter).
    [Export] public float RockScale { get; set; } = 0.7f;
    [ExportGroup("")]

    private static readonly Color RingColour = new("161a18"), EdgeColour = new("161a18");
    private const float EdgeWidth = 4f, TurnSpeed = 10f;      // how quickly the ring settles after turning
    private const int RingSlot = 7, CentreSlot = 8, Slots = 9; // after the seven terrains: the ring, the middle circle

    private int _home = 3, _from = -1, _hover = -1;            // slots (0-6: indexes into Terrains.Wheel)
    private bool _choosing;
    private Func<Terrain, (Amounts Cost, bool Allowed)> _costOf;
    private readonly Dictionary<int, HighlightArt.Colour> _lit = new(); // ShowChoices' slots, and their colours
    private readonly HashSet<int> _disabled = new();
    private readonly HashSet<int> _priests = new();
    private Texture2D _priest;
    private float _turn;                                       // extra turn (radians), easing back to 0
    private float _spread = 1f;
    private Tween _tween;
    private readonly EdgeHighlight[] _highlights = new EdgeHighlight[Slots];
    private ResourceBundle _cost;

    // The ring over one terrain's circle, and what clicking it chooses (null: the circle underneath).
    public Terrain? Ring { get => _ring; set { _ring = value; Changed(); } }
    public Icon RingIcon { get => _ringIcon; set { _ringIcon = value; Changed(); } }
    public Terrain? RingChooses { get; set; }
    // Points (global) where clicks and hovers are left to something over the wheel (e.g. chips), or null.
    public Func<Vector2, bool> IgnoreClick { get; set; }
    public bool RingOutside { get => _ringOutside; set { _ringOutside = value; Changed(); } }
    private bool _ringOutside;
    // One more circle in the middle (e.g. Volcano), or null.
    public Terrain? Centre { get => _centre; set { _centre = value; Changed(); } }
    // Only that circle, at the top circle's place (see the notes at the top).
    public bool CentreOnly { get => _centreOnly; set { _centreOnly = value; Refresh(); } }
    private bool _centreOnly;
    // The terrain shown alone while Spread is 0 (null: the top one).
    public Terrain? Focus { get => _focus; set { _focus = value; Changed(); } }
    public float Spread
    {
        get => _spread;
        set
        {
            bool wasOut = _spread > 0.01f;
            _spread = Mathf.Clamp(value, 0f, 1f);
            if (wasOut != _spread > 0.01f)
                Refresh(); // circles shown or gathered away: their highlights with them
            else
                PlaceHighlights();
            QueueRedraw();
        }
    }
    private Terrain? _ring, _centre, _focus;
    private Icon _ringIcon = Icon.IceRing;

    public override void _Ready()
    {
        for (var i = 0; i < Slots; i++)
        {
            _highlights[i] = new EdgeHighlight { Name = $"Highlight{i}", Visible = false };
            AddChild(_highlights[i]);
        }
        _cost = new ResourceBundle
        {
            Name = "Cost", Visible = false, Centred = true, ShowOnes = true,
            IconSize = CostIconSize, FontSize = CostFontSize, Position = CostOffset, ZIndex = 1,
            FontColour = Colors.Black, OutlineColour = Colors.White, // over the map: black with a white edge, as the hexes' costs
        };
        AddChild(_cost);
    }

    // The cost's icon and font sizes from now on (Main matches them to the map's hex costs).
    public void SetCostSize(int iconSize, int fontSize)
    {
        CostIconSize = iconSize;
        CostFontSize = fontSize;
        if (_cost is not null)
        {
            _cost.IconSize = iconSize;
            _cost.FontSize = fontSize;
        }
    }

    // ---------- What it shows ----------

    // top: the terrain at the top (the player's home); clicked: the clicked hex's terrain (highlighted white);
    // costOf: what turning the hex into a terrain costs, and whether the player can do it now.
    public void ShowFor(Terrain top, Terrain clicked, Func<Terrain, (Amounts Cost, bool Allowed)> costOf)
    {
        int newHome = Terrains.Wheel.IndexOf(top), from = Terrains.Wheel.IndexOf(clicked);
        if (newHome < 0 || from < 0)
            return;
        if (newHome != _home && _from >= 0)
        {
            // Turn so the clicked terrain's circle starts where it was, then ease round.
            float before = Angle(_from), after;
            _home = newHome;
            after = Angle(_from);
            _turn += Mathf.Wrap(before - after, -Mathf.Pi, Mathf.Pi);
        }
        _home = newHome;
        _from = from;
        _choosing = false;
        _lit.Clear();
        _costOf = costOf;
        _hover = SlotAt(GetLocalMousePosition());
        Refresh();
    }

    // Each terrain costs its spades (the shorter way round) at workersPerSpade workers each.
    public void ShowFor(Terrain top, Terrain clicked, int workersPerSpade) =>
        ShowFor(top, clicked, to => Terrains.Spades(clicked, to) is int spades
            ? (Amounts.Of(ResourceKind.Worker, spades * workersPerSpade), true)
            : (Amounts.None, false));

    // A choice between these terrains (Ice or the middle circle's terrain included, if shown): lit in the colour
    // colourOf gives (purple if null); only they can be hovered and clicked. costOf: what each costs (null: nothing shown).
    public void ShowChoices(Terrain top, IEnumerable<Terrain> choosable, Func<Terrain, HighlightArt.Colour> colourOf = null,
        Func<Terrain, (Amounts Cost, bool Allowed)> costOf = null)
    {
        SetTop(top);
        _from = -1;
        _choosing = true;
        _costOf = costOf;
        Light(choosable, colourOf);
        _hover = SlotAt(GetLocalMousePosition());
        Refresh();
    }

    // A palette: every circle (the middle one too, if shown) can be clicked; `selected` lit white (ClickedGlints).
    public void ShowPicker(Terrain top, Terrain selected)
    {
        SetTop(top);
        _from = SlotsOf(selected).DefaultIfEmpty(-1).First();
        _choosing = false;
        _costOf = null;
        _lit.Clear();
        _hover = SlotAt(GetLocalMousePosition());
        Refresh();
    }

    // The picture alone: nothing lit, nothing reacts.
    public void ShowStill(Terrain top)
    {
        SetTop(top);
        _from = -1;
        _choosing = false;
        _costOf = null;
        _lit.Clear();
        _hover = -1;
        Refresh();
    }

    private void Light(IEnumerable<Terrain> terrains, Func<Terrain, HighlightArt.Colour> colourOf)
    {
        _lit.Clear();
        foreach (Terrain terrain in terrains)
            foreach (int slot in SlotsOf(terrain))
                _lit[slot] = colourOf?.Invoke(terrain) ?? HighlightArt.Colour.Purple;
    }

    public void SetDisabled(IEnumerable<Terrain> terrains)
    {
        _disabled.Clear();
        _disabled.UnionWith(terrains.Select(t => Terrains.Wheel.IndexOf(t)).Where(i => i >= 0));
        QueueRedraw();
    }

    private readonly HashSet<int> _rocks = new();
    private Texture2D _rock;

    public void SetRocks(IEnumerable<Terrain> terrains)
    {
        var slots = terrains.Select(t => Terrains.Wheel.IndexOf(t)).Where(i => i >= 0).ToList();
        if (_rocks.SetEquals(slots))
            return;
        _rocks.Clear();
        _rocks.UnionWith(slots);
        _rock ??= IconArt.Texture(Icon.GlowingRock, 96);
        QueueRedraw();
    }

    public void SetPriests(IEnumerable<Terrain> terrains, Color colour, Color outline)
    {
        _priests.Clear();
        _priests.UnionWith(terrains.Select(t => Terrains.Wheel.IndexOf(t)).Where(i => i >= 0));
        _priest = _priests.Count > 0 ? PieceArt.TextureFor(PieceKind.Priest, colour, outline) : null;
        QueueRedraw();
    }

    // Changes Spread and this node's scale together, over `seconds` (0: at once).
    public void Animate(float spread, Vector2 scale, float seconds)
    {
        if (_tween is not null && _tween.IsValid())
            _tween.Kill();
        _tween = null;
        if (seconds <= 0 || !IsInsideTree() || (Mathf.IsEqualApprox(spread, _spread) && scale.IsEqualApprox(Scale)))
        {
            Spread = spread;
            Scale = scale;
            return;
        }
        _tween = CreateTween().SetParallel();
        _tween.TweenMethod(Callable.From<float>(value => Spread = value), _spread, spread, seconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(this, "scale", scale, seconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    // How far it reaches from its centre fully spread, in its own units (a ring beside its circle and the backing
    // disc included): what must stay on screen.
    public float Reach
    {
        get
        {
            if (_centreOnly)
                return RingRadius + CircleRadius;
            float reach = RingRadius + CircleRadius + (BackingColour.A > 0 ? BackingMargin : 0);
            if (_ring is Terrain r && Terrains.Wheel.IndexOf(r) is int i and >= 0)
                reach = Mathf.Max(reach, RingCentre(Vector2.FromAngle(Angle(i)) * RingRadius).Length() + RingOuter);
            return reach;
        }
    }

    // Is the point over the wheel (anywhere within its outermost circles)?
    public bool IsOver(Vector2 globalPoint) =>
        IsVisibleInTree() && (_centreOnly
            ? ToLocal(globalPoint).DistanceTo(SlotCentre(CentreSlot)) <= CircleRadius
            : ToLocal(globalPoint).Length() <= RingRadius * _spread + CircleRadius);

    // Where a terrain's circle is now, in global coordinates (null for a terrain not on the wheel).
    public Vector2? CircleGlobalPosition(Terrain terrain)
    {
        int i = Terrains.Wheel.IndexOf(terrain);
        return i < 0 ? null : ToGlobal(SlotCentre(i));
    }

    // ---------- Hover and click ----------

    private bool Interactive => _from >= 0 || _choosing;

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || !Interactive || IgnoreClick?.Invoke(GetGlobalMousePosition()) == true)
            return;
        if (input is InputEventMouseMotion)
        {
            int hover = SlotAt(GetLocalMousePosition());
            if (hover != _hover)
            {
                _hover = hover;
                Refresh();
            }
        }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            int i = SlotAt(GetLocalMousePosition());
            if (i < 0 || TerrainOf(i) is not Terrain terrain)
                return;
            EmitSignal(SignalName.TerrainChosen, Variant.From(terrain));
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (_turn == 0)
            return;
        _turn = Mathf.Abs(_turn) < 0.001f ? 0 : _turn * MathF.Exp(-TurnSpeed * (float)delta);
        PlaceHighlights();
        QueueRedraw();
    }

    // ---------- Where things are ----------

    private void SetTop(Terrain top)
    {
        int i = Terrains.Wheel.IndexOf(top);
        if (i >= 0)
            _home = i;
    }

    private void Changed()
    {
        PlaceHighlights();
        QueueRedraw();
    }

    // Where terrain i sits: its place round from the top (home is 0), as an angle, plus any turn still easing out.
    private float Angle(int i) =>
        -Mathf.Pi / 2 + (Anticlockwise ? -1 : 1) * Mathf.PosMod(i - _home, 7) * Mathf.Tau / 7 + _turn;

    private int FocusSlot => _focus is Terrain f && Terrains.Wheel.IndexOf(f) is int i and >= 0 ? i : _home;

    // A slot's centre: a terrain's circle (drawn in from the ring as Spread falls), the ring, or the middle circle.
    private Vector2 SlotCentre(int slot) => slot switch
    {
        RingSlot => _ring is Terrain r && Terrains.Wheel.IndexOf(r) is int i and >= 0 ? RingCentre(SlotCentre(i)) : CentreOffset,
        CentreSlot => _centreOnly ? Vector2.Up * RingRadius : CentreOffset,
        _ => Vector2.FromAngle(Angle(slot)) * RingRadius * _spread,
    };

    private float RingOuter => CircleRadius * RingScale;

    // Over its circle, or (RingOutside) beside it, outward from the middle (straight up for a circle in the middle).
    private Vector2 RingCentre(Vector2 circle)
    {
        if (!_ringOutside)
            return circle + RingOffset;
        Vector2 outward = circle.LengthSquared() > 0.01f ? circle.Normalized() : Vector2.Up;
        return circle + outward * (CircleRadius + RingOuter) + RingOffset;
    }

    // The slots that choose a terrain: its circle, and the ring if it chooses it, the middle circle if it is it.
    private IEnumerable<int> SlotsOf(Terrain terrain)
    {
        int i = Terrains.Wheel.IndexOf(terrain);
        if (i >= 0)
            yield return i;
        if (_ring is not null && RingChooses == terrain)
            yield return RingSlot;
        if (_centre == terrain)
            yield return CentreSlot;
    }

    private Terrain? TerrainOf(int slot) => slot switch
    {
        < 0 => null,
        RingSlot => RingChooses,
        CentreSlot => _centre,
        _ => Terrains.Wheel[slot],
    };

    // Whether a slot is drawn now (only the focus while gathered in the middle).
    private bool IsShown(int slot) => _centreOnly ? slot == CentreSlot && _centre is not null : slot switch
    {
        RingSlot => _ring is Terrain r && (_spread > 0.01f || Terrains.Wheel.IndexOf(r) == FocusSlot),
        CentreSlot => _centre is not null && _spread > 0.01f,
        _ => _spread > 0.01f || slot == FocusSlot,
    };

    // The slot under a point (the ring, outside its hole, before the circles; in a choice, only the lit ones).
    private int SlotAt(Vector2 local)
    {
        IEnumerable<int> order = new[] { RingSlot, CentreSlot }.Concat(Enumerable.Range(0, 7));
        foreach (int slot in order)
        {
            if (!IsShown(slot) || (slot == RingSlot && RingChooses is null))
                continue;
            float d = local.DistanceTo(SlotCentre(slot));
            bool over = slot == RingSlot ? d <= RingOuter && (_ringOutside || d >= RingOuter * IconArt.RingHole) : d <= CircleRadius;
            if (!over)
                continue;
            if (_choosing && !_lit.ContainsKey(slot))
                return -1;
            return slot;
        }
        return -1;
    }

    // ---------- Drawing ----------

    public override void _Draw()
    {
        float alpha = Mathf.Clamp(_spread, 0f, 1f);
        if (BackingColour.A > 0 && !_centreOnly)
            DrawCircle(Vector2.Zero, RingRadius * _spread + CircleRadius + BackingMargin, BackingColour);
        if (_spread > 0.01f && !_centreOnly)
            DrawArc(Vector2.Zero, RingRadius * _spread, 0, Mathf.Tau, 128, new Color(RingColour, alpha), RingWidth, true);
        int focus = FocusSlot;
        foreach (int i in Enumerable.Range(0, 7).Where(i => i != focus).Append(focus))
        {
            if (!IsShown(i))
                continue;
            float a = i == focus ? 1f : alpha;
            DrawTerrainCircle(SlotCentre(i), Terrains.Wheel[i], _disabled.Contains(i), a);
        }
        if (IsShown(CentreSlot) && _centre is Terrain centre)
            DrawTerrainCircle(SlotCentre(CentreSlot), centre, false, alpha);
        if (IsShown(RingSlot) && IconArt.Texture(_ringIcon, IconArt.RingIconSize) is Texture2D ring)
        {
            // The ring's texture has its outside at 42 px of its 102 (IconArt.RingIconSize).
            float k = RingOuter / (0.48f * IconArt.RingIconSize * 6f / 7f);
            DrawSetTransform(SlotCentre(RingSlot), 0, Vector2.One * k);
            DrawTexture(ring, -ring.GetSize() / 2);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        if (_rock is not null)
            foreach (int i in _rocks.Where(IsShown))
            {
                float k = CircleRadius * 2 * RockScale / _rock.GetSize().Y;
                DrawSetTransform(SlotCentre(i), 0, Vector2.One * k);
                DrawTexture(_rock, -_rock.GetSize() / 2);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        if (_priest is not null)
            foreach (int i in _priests.Where(IsShown))
            {
                float k = CircleRadius * 2 * PriestScale / _priest.GetSize().Y;
                DrawSetTransform(SlotCentre(i), 0, Vector2.One * k);
                DrawTexture(_priest, -_priest.GetSize() / 2, new Color(1, 1, 1, i == focus ? 1f : alpha));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
    }

    private void DrawTerrainCircle(Vector2 at, Terrain terrain, bool disabled, float alpha)
    {
        Color fill = HexArt.TerrainColour(terrain);
        if (disabled)
            fill = fill.Lerp(new Color(DisabledColour, 1f), DisabledColour.A);
        DrawCircle(at, CircleRadius, new Color(fill, alpha));
        DrawArc(at, CircleRadius - EdgeWidth / 2, 0, Mathf.Tau, 64, new Color(EdgeColour, alpha), EdgeWidth, true);
    }

    // The highlights (white for the clicked and the hovered terrain, the lit colour for the others lit), and the cost.
    private void Refresh()
    {
        if (_highlights[0] is null)
            return; // not ready yet
        for (var i = 0; i < Slots; i++)
        {
            bool white = i == _from || i == _hover;
            HighlightArt.Colour colour = white ? HighlightArt.Colour.White : _lit.GetValueOrDefault(i, HighlightArt.Colour.Purple);
            _highlights[i].Visible = IsShown(i) && (white || _lit.ContainsKey(i));
            int glints = i == _hover ? HoverGlints : i == _from ? ClickedGlints : 0;
            float radius = i == RingSlot ? RingOuter : CircleRadius;
            if (_highlights[i].Visible && !_highlights[i].IsShown(colour, glints))
                _highlights[i].Show(new[] { HighlightArt.Ellipse(Vector2.Zero, new Vector2(radius, radius), 64) }, colour, glints);
        }
        PlaceHighlights();

        Terrain? hovered = _centreOnly ? _centre : _hover >= 0 && _hover != _from ? TerrainOf(_hover) : null;
        (Amounts Cost, bool Allowed) cost = hovered is Terrain to && _costOf is not null
            ? _costOf(to)
            : (Amounts.None, true);
        _cost.Visible = !cost.Cost.IsEmpty;
        _cost.Position = _hover == CentreSlot && !_centreOnly ? CentreCostOffset : CostOffset;
        if (_cost.Visible)
            _cost.ShowAmounts(cost.Cost, cost.Allowed ? null : cost.Cost.Parts.Select(part => part.Kind));
        QueueRedraw();
    }

    // Highlights follow their circles; one whose circle is gathered away is hidden.
    private void PlaceHighlights()
    {
        if (_highlights[0] is null)
            return;
        for (var i = 0; i < Slots; i++)
        {
            _highlights[i].Position = SlotCentre(i);
            if (_highlights[i].Visible && !IsShown(i))
                _highlights[i].Visible = false;
        }
    }
}
