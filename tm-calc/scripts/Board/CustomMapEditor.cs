using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The custom map editor (on the map screen, from its "Custom map" key; Main: MainCustomMap.cs). The colour wheel on
// the left as a palette (ColourWheel.ShowPicker): the seven terrains round the ring and River in the middle, the one
// chosen lit white (River at first). A map on the right, river hexes only at first (MapView, MapId.Custom): a click on
// a hex paints it with the chosen terrain, and dragging with the button held paints every hex passed over (River
// paints a hex back to river); the hex under the pointer is outlined white. Over the map, a bar in the top panel's
// style (its frame and stone, TopPanelArt.DrawerBacking): Back (BackPressed) and the 13-top / 12-top switch
// (TopPanelSwitch: which rows are long, Maps.CustomLayout; switching clears the map) on the left; Save, Save as and Load
// (SaveRequested, SaveAsRequested, LoadRequested: Main writes and reads the .tmmap file, Core.MapFile; a map loaded
// comes in with Replace) in the middle; Proceed (ProceedPressed with the map as text, Maps.Rows) on the right, disabled
// until every terrain has Config.CustomMapMinimumHexes hexes (its tooltip says what is missing). Undo and Redo (Main's Ctrl+Z / Ctrl+Y and the top panel's keys, while open) take back and redo
// one stroke or switch at a time; Changed fires after anything changes. Its own pixels are the map screen's.
[GlobalClass]
public partial class CustomMapEditor : Node2D
{
    [Signal] public delegate void ProceedPressedEventHandler(string rows);
    [Signal] public delegate void BackPressedEventHandler();
    [Signal] public delegate void ChangedEventHandler();
    [Signal] public delegate void SaveRequestedEventHandler();
    [Signal] public delegate void SaveAsRequestedEventHandler();
    [Signal] public delegate void LoadRequestedEventHandler();

    [ExportGroup("Wheel")]
    [Export] public Vector2 WheelCentre { get; set; } = new(400, 600);
    [Export] public float WheelScale { get; set; } = 1.3f;
    [ExportGroup("Map")]
    [Export] public Vector2 MapCentre { get; set; } = new(1240, 640);   // the middle of the hexes
    [Export] public float MapScale { get; set; } = 0.5f;                // of the board's hex art (a 128 px wide hex)
    [ExportGroup("Bar")]
    [Export] public float BarScale { get; set; } = 0.6f;                // as the top panel's PanelScale
    [Export] public float BarHeight { get; set; } = 110f;               // panel pixels; as wide as the map
    [Export] public float BarGap { get; set; } = 18f;                   // screen pixels, from the bar to the map's top
    [Export] public Vector2 KeySize { get; set; } = new(180, 66);       // Back and Proceed, panel pixels
    [Export] public Vector2 FileKeySize { get; set; } = new(150, 66);   // Save, Save as, Load
    [Export] public float KeyGap { get; set; } = 14f;                   // between the bar's keys (and the switch)
    [Export] public int KeyFontSize { get; set; } = 26;
    [Export] public Vector2 SwitchSize { get; set; } = new(300, 76);    // panel pixels
    [Export] public int SwitchFontSize { get; set; } = 22;
    [Export] public Color FrameColour { get; set; } = IconArt.PowerPurple;
    [ExportGroup("")]

    public bool TwelveTop => _twelveTop;
    public MapLayout Layout => Maps.CustomLayout(_twelveTop);
    public IReadOnlyList<Terrain> Hexes => _hexes;
    public List<(Terrain Terrain, int Missing)> Shortfall => Maps.Shortfall(_hexes);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private sealed record Snapshot(bool TwelveTop, Terrain[] Hexes);

    private bool _twelveTop;
    private Terrain[] _hexes = System.Array.Empty<Terrain>();
    private Terrain _brush = Terrain.River;
    private readonly Stack<Snapshot> _undo = new(), _redo = new();
    private Snapshot _strokeStart; // while painting with the button held (null: not)
    private int? _hovered;

    private ColourWheel _wheel;
    private MapView _map;
    private Control _bar;
    private TextureRect _barBacking;
    private TopPanelTextKey _back, _proceed, _save, _saveAs, _load;
    private TopPanelSwitch _switch;

    // Shown (Main): made the first time, a blank 13-top map.
    public void Open()
    {
        if (_map is null)
            Make();
        Refresh();
    }

    // Shows this map (e.g. the custom map an undo has just taken back), unless it is the one already here (whose undo
    // steps are kept then); otherwise the undo steps start again.
    public void Load(bool twelveTop, IReadOnlyList<Terrain> hexes)
    {
        if (_map is null)
            Make();
        if (twelveTop == _twelveTop && hexes.SequenceEqual(_hexes))
            return;
        _undo.Clear();
        _redo.Clear();
        Restore(new Snapshot(twelveTop, hexes.ToArray()));
    }

    // A map loaded from a file: it replaces this one as one undo step (Undo brings this one back).
    public void Replace(bool twelveTop, IReadOnlyList<Terrain> hexes)
    {
        if (_map is null)
            Make();
        if (twelveTop == _twelveTop && hexes.SequenceEqual(_hexes))
            return;
        _undo.Push(Now());
        _redo.Clear();
        Restore(new Snapshot(twelveTop, hexes.ToArray()));
    }

    public bool Undo() => Step(_undo, _redo);
    public bool Redo() => Step(_redo, _undo);

    private bool Step(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (from.Count == 0 || _strokeStart is not null)
            return false;
        to.Push(Now());
        Restore(from.Pop());
        return true;
    }

    private Snapshot Now() => new(_twelveTop, _hexes.ToArray());

    // ---------- Making it ----------

    private void Make()
    {
        _wheel = new ColourWheel { Name = "Wheel", Position = WheelCentre, Scale = Vector2.One * WheelScale, Centre = Terrain.River,
            ClickedGlints = 0, HoverGlints = 1 };
        AddChild(_wheel);
        _wheel.TerrainChosen += terrain =>
        {
            _brush = terrain;
            _wheel.ShowPicker(Terrains.Wheel[0], _brush);
        };

        _map = new MapView { Name = "Map", Scale = Vector2.One * MapScale };
        AddChild(_map);

        _bar = new Control { Name = "Bar", Scale = Vector2.One * BarScale, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_bar);
        _barBacking = new TextureRect
        {
            Name = "Backing", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _bar.AddChild(_barBacking);
        _back = new TopPanelTextKey { Name = "Back", Text = "Back", FontSize = KeyFontSize, TooltipText = "Back to the six maps (this map is kept)" };
        _back.Pressed += () => EmitSignal(SignalName.BackPressed);
        _bar.AddChild(_back);
        _switch = new TopPanelSwitch
        {
            Name = "Rows", Labels = new[] { "13-top", "12-top" }, LabelSize = SwitchFontSize, Frame = FrameColour,
            Tooltips = new[]
            {
                "13-top: the top row has 13 hexes, as the original map (113 hexes). Switching clears the map.",
                "12-top: the top row has 12 hexes, as Fire & Ice (112 hexes). Switching clears the map.",
            },
        };
        _switch.ValueChosen += side => Switch(side == 1);
        _bar.AddChild(_switch);
        _save = new TopPanelTextKey { Name = "Save", Text = "Save", FontSize = KeyFontSize, TooltipText = "Save this map (Ctrl+S)" };
        _save.Pressed += () => EmitSignal(SignalName.SaveRequested);
        _bar.AddChild(_save);
        _saveAs = new TopPanelTextKey { Name = "SaveAs", Text = "Save as", FontSize = KeyFontSize, TooltipText = "Save this map under a new name (Ctrl+Shift+S)" };
        _saveAs.Pressed += () => EmitSignal(SignalName.SaveAsRequested);
        _bar.AddChild(_saveAs);
        _load = new TopPanelTextKey { Name = "Load", Text = "Load", FontSize = KeyFontSize, TooltipText = "Load a saved map in place of this one (Ctrl+O; Ctrl+Z takes it back)" };
        _load.Pressed += () => EmitSignal(SignalName.LoadRequested);
        _bar.AddChild(_load);
        _proceed = new TopPanelTextKey { Name = "Proceed", Text = "Proceed", FontSize = KeyFontSize };
        _proceed.Pressed += () =>
        {
            if (Shortfall.Count == 0)
                EmitSignal(SignalName.ProceedPressed, Maps.Rows(Layout, _hexes));
        };
        _bar.AddChild(_proceed);

        _twelveTop = false;
        _hexes = Enumerable.Repeat(Terrain.River, Layout.HexCount).ToArray();
        _switch.Value = 0;
        Rebuild();
    }

    // The map's hexes for the layout, placed (its middle at MapCentre) with the bar over it.
    private void Rebuild()
    {
        _hovered = null;
        _map.Position = Vector2.Zero;
        _map.Build(Layout, MapId.Custom);
        _map.ShowTerrain(new ValueList<Terrain>(_hexes));
        Rect2 bounds = LocalBounds();
        _map.Position += MapCentre - bounds.GetCenter();
        bounds = LocalBounds();

        var size = new Vector2(bounds.Size.X / BarScale, BarHeight);
        _bar.Size = size;
        _bar.Position = new Vector2(bounds.Position.X, bounds.Position.Y - BarGap - BarHeight * BarScale);
        _barBacking.Size = size;
        _barBacking.Texture = TopPanelArt.DrawerBacking(size, FrameColour);
        float margin = TopPanelArt.Frame + TopPanelArt.Margin;
        _back.Size = KeySize;
        _back.Position = new Vector2(margin, (size.Y - KeySize.Y) / 2);
        _proceed.Size = KeySize;
        _proceed.Position = new Vector2(size.X - margin - KeySize.X, (size.Y - KeySize.Y) / 2);
        // Back, then the switch, on the left; Proceed on the right; Save, Save as, Load centred between them.
        _switch.Size = SwitchSize;
        _switch.Position = new Vector2(_back.Position.X + KeySize.X + KeyGap, (size.Y - SwitchSize.Y) / 2);
        float left = _switch.Position.X + SwitchSize.X + KeyGap, right = _proceed.Position.X - KeyGap;
        float files = 3 * FileKeySize.X + 2 * KeyGap, x = left + Mathf.Max(0, (right - left - files) / 2);
        foreach (TopPanelTextKey key in new[] { _save, _saveAs, _load })
        {
            key.Size = FileKeySize;
            key.Position = new Vector2(x, (size.Y - FileKeySize.Y) / 2);
            x += FileKeySize.X + KeyGap;
        }
    }

    // The hexes' outline box in this node's pixels.
    private Rect2 LocalBounds()
    {
        Rect2 shown = _map.ShownBounds();
        Vector2 from = ToLocal(shown.Position), to = ToLocal(shown.End);
        return new Rect2(from, to - from);
    }

    // The wheel, the switch and Proceed as things now are; Changed.
    private void Refresh()
    {
        _wheel.ShowPicker(Terrains.Wheel[0], _brush);
        _switch.Value = _twelveTop ? 1 : 0;
        List<(Terrain Terrain, int Missing)> missing = Shortfall;
        _proceed.Disabled = missing.Count > 0;
        _proceed.TooltipText = missing.Count == 0
            ? "Play on this map: on to the round tiles"
            : $"Every terrain needs at least {Config.CustomMapMinimumHexes} hexes. Still needed: "
              + string.Join(", ", missing.Select(part => $"{part.Missing} {part.Terrain}"));
        EmitSignal(SignalName.Changed);
    }

    // ---------- Changes ----------

    // The other rows long: a blank map of that shape (an undo step, so it can be taken back).
    private void Switch(bool twelveTop)
    {
        if (twelveTop == _twelveTop)
            return;
        _undo.Push(Now());
        _redo.Clear();
        Restore(new Snapshot(twelveTop, Enumerable.Repeat(Terrain.River, Maps.CustomLayout(twelveTop).HexCount).ToArray()));
    }

    private void Restore(Snapshot snapshot)
    {
        bool reshaped = snapshot.TwelveTop != _twelveTop || snapshot.Hexes.Length != _hexes.Length;
        _twelveTop = snapshot.TwelveTop;
        _hexes = snapshot.Hexes.ToArray();
        if (reshaped)
            Rebuild();
        else
            _map.ShowTerrain(new ValueList<Terrain>(_hexes));
        Refresh();
    }

    private void Paint(int hex)
    {
        if (hex < 0 || hex >= _hexes.Length || _hexes[hex] == _brush)
            return;
        _hexes[hex] = _brush;
        _map.ShowTerrain(new ValueList<Terrain>(_hexes));
        Refresh();
    }

    // ---------- Input ----------

    public override void _Input(InputEvent input)
    {
        if (_map is null || !IsVisibleInTree())
            return;
        Vector2 mouse = GetGlobalMousePosition();
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                if (_map.HexAt(mouse) is int hex && !_wheel.IsOver(mouse))
                {
                    _strokeStart = Now();
                    Paint(hex);
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_strokeStart is Snapshot start)
                {
                    _strokeStart = null;
                    if (!start.Hexes.SequenceEqual(_hexes)) // one stroke, one undo step
                    {
                        _undo.Push(start);
                        _redo.Clear();
                    }
                    Refresh();
                }
                break;
            case InputEventMouseMotion:
                int? over = _map.HexAt(mouse);
                if (over != _hovered)
                {
                    if (_hovered is int was)
                        _map.ShowHexHighlight(was, false);
                    _hovered = over;
                    if (over is int now)
                        _map.ShowHexHighlight(now, true);
                }
                if (_strokeStart is not null && over is int under)
                    Paint(under);
                break;
        }
    }
}
