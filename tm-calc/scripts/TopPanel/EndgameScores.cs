using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The end of the game's scores and statistics (Core.GameStats), in the top panel's style: over a dimmed screen, the top
// panel's frame and stone; three tab keys at the top left (VPs: the VP icon; Actions: the action octagon; Resources:
// coin, worker, priest and power), the selected one pressed in and ringed in the frame colour. Under them, one column
// per faction (the first round's turn order): the VP icon with the final VP and the place ("1st", "2nd (tie)"), and
// the portrait. Under those, in a sunken section that scrolls, the chosen tab's table: section headings, then a row
// per line (its name at the left, a value under each faction, "–" where the row can't apply), alternate rows faintly
// lit. A Close key along the bottom; Esc (Main) or a click on the dimmed screen closes it too (Closed). Sized in panel
// pixels and scaled like the top panel, smaller if the screen is too small for it.
[GlobalClass]
public partial class EndgameScores : Control
{
    [ExportGroup("Layout")]
    [Export] public float Padding { get; set; } = 26f;           // panel pixels, inside the frame
    [Export] public float NameWidth { get; set; } = 330f;        // the rows' names
    [Export] public float ColumnWidth { get; set; } = 250f;      // a faction's column
    [Export] public float TableHeight { get; set; } = 640f;      // the sunken section
    [Export] public float RowHeight { get; set; } = 42f;
    [Export] public int RowFontSize { get; set; } = 25;
    [Export] public int SectionFontSize { get; set; } = 27;
    [Export] public Vector2 TabSize { get; set; } = new(104, 66);
    [Export] public int TabIconSize { get; set; } = 46;
    [Export] public float PortraitSize { get; set; } = 120f;
    [Export] public int PointsIconSize { get; set; } = 62;
    [Export] public int PointsFontSize { get; set; } = 26;
    [Export] public int PlaceFontSize { get; set; } = 24;
    [Export] public Vector2 CloseSize { get; set; } = new(220, 56);
    [Export] public float Gap { get; set; } = 14f;
    [Export] public float ScreenMargin { get; set; } = 0.94f;
    [ExportGroup("Colours")]
    [Export] public Color DimColour { get; set; } = new(0, 0, 0, 0.55f);
    [Export] public Color TextColour { get; set; } = new("e4ebf1");
    [Export] public Color SectionColour { get; set; } = new("f7fafc");
    [Export] public Color DashColour { get; set; } = new(0.89f, 0.92f, 0.95f, 0.45f);
    [Export] public Color StripeColour { get; set; } = new(1, 1, 1, 0.05f);

    public bool IsOpen => Visible;
    public event Action Closed;

    private const float Frame = TopPanelArt.Frame;
    private readonly Control _drawer = new() { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
    private readonly TextureRect _backing = new()
    {
        Name = "Backing", MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
    };
    private readonly Control _header = new() { Name = "Header", MouseFilter = MouseFilterEnum.Ignore };
    private readonly ScrollContainer _scroll = new() { Name = "Table", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer _rows = new() { Name = "Rows" };
    private readonly TopPanelTextKey _close = new() { Name = "Close", Text = "Close", FontSize = 24, TooltipText = "Close (Esc)" };
    private readonly List<EndgameTab> _tabs = new();
    private GameStats _stats;
    private Color _frame = IconArt.PowerPurple;
    private float _scale = 0.6f;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the dimmed screen takes the clicks
        var dim = new ColorRect { Name = "Dim", Color = DimColour, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_drawer);
        _drawer.AddChild(_backing);
        _drawer.AddChild(_header);
        _drawer.AddChild(_scroll);
        _scroll.AddChild(_rows);
        _rows.AddThemeConstantOverride("separation", 0);
        _drawer.AddChild(_close);
        _close.Pressed += () => Callable.From((Action)Close).CallDeferred();
        string[] tips = { "Victory points", "Actions", "Resources" };
        for (var i = 0; i < 3; i++)
        {
            var tab = new EndgameTab { Name = $"Tab{i}", Kind = i, TooltipText = tips[i] };
            int index = i;
            tab.Pressed += () => ShowTab(index);
            _tabs.Add(tab);
            _drawer.AddChild(tab);
        }
        Visible = false;
    }

    // Shows these statistics (the VPs tab first). frame and scale: the top panel's.
    public void Open(GameStats stats, Color frame, float scale)
    {
        _stats = stats;
        _frame = frame;
        _scale = scale;
        Visible = true;
        Place();
        ShowTab(0);
    }

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        Closed?.Invoke();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle })
        {
            AcceptEvent();
            Callable.From((Action)Close).CallDeferred();
        }
    }

    // ---------- Layout ----------

    private float ColumnsLeft => Frame + Padding + NameWidth;

    private void Place()
    {
        int n = _stats?.Factions.Count ?? 0;
        float x0 = Frame + Padding, width = ColumnsLeft + n * ColumnWidth + 22 + Padding + Frame; // 22: the scroll bar
        float y = Frame + Padding * 0.7f;
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Size = TabSize;
            _tabs[i].Position = new Vector2(x0 + i * (TabSize.X + Gap * 0.6f), y);
            _tabs[i].IconSize = TabIconSize;
            _tabs[i].Frame = _frame;
        }
        y += TabSize.Y + Gap;

        float headerHeight = Mathf.Max(PortraitSize, PointsIconSize + PlaceFontSize * 2.6f);
        _header.Position = new Vector2(0, y);
        _header.Size = new Vector2(width, headerHeight);
        BuildHeader(headerHeight);
        y += headerHeight + Gap;

        var table = new Rect2(x0, y, width - 2 * x0, TableHeight);
        _scroll.Position = table.Position + new Vector2(6, 6);
        _scroll.Size = table.Size - new Vector2(12, 12);
        _rows.CustomMinimumSize = new Vector2(_scroll.Size.X - 20, 0); // (room for the scroll bar)
        y += TableHeight + Gap;

        _close.Size = CloseSize;
        _close.Position = new Vector2((width - CloseSize.X) / 2, y);
        y += CloseSize.Y + Padding * 0.8f + Frame;

        var size = new Vector2(width, y);
        _drawer.Size = size;
        _backing.Size = size;
        _backing.Texture = TopPanelArt.DrawerBacking(size, _frame, (table, 12));
        Vector2 screen = GetViewportRect().Size;
        float scale = Mathf.Min(_scale, Mathf.Min(screen.X * ScreenMargin / size.X, screen.Y * ScreenMargin / size.Y));
        _drawer.Scale = Vector2.One * scale;
        _drawer.Position = ((screen - size * scale) / 2).Round();
    }

    // Each faction's column: the VP icon with the final VP and the place under it, then the portrait.
    private void BuildHeader(float height)
    {
        foreach (Node old in _header.GetChildren())
        {
            _header.RemoveChild(old);
            old.QueueFree();
        }
        if (_stats is null)
            return;
        for (var c = 0; c < _stats.Factions.Count; c++)
        {
            float left = ColumnsLeft + c * ColumnWidth, groupWidth = PointsIconSize + Gap + PortraitSize;
            float x = left + (ColumnWidth - groupWidth) / 2;
            var points = new TextureRect
            {
                Texture = Art.ResourceIcon(ResourceKind.VictoryPoint, size: PointsIconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Size = Vector2.One * PointsIconSize, Position = new Vector2(x, (height - PointsIconSize - PlaceFontSize * 2.6f) / 2),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _header.AddChild(points);
            var total = new Label
            {
                Text = _stats.Totals[c].ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Size = points.Size, Position = Vector2.Zero, MouseFilter = MouseFilterEnum.Ignore,
                LabelSettings = new LabelSettings
                {
                    Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.5f },
                    FontSize = PointsFontSize, FontColor = Colors.White, OutlineColor = Colors.Black, OutlineSize = 6,
                },
            };
            points.AddChild(total);
            var place = new Label
            {
                Text = Ordinal(_stats.Places[c]) + (_stats.Tied[c] ? "\n(tie)" : ""),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Size = new Vector2(PointsIconSize + 2 * Gap, PlaceFontSize * 2.6f),
                Position = new Vector2(x - Gap, points.Position.Y + PointsIconSize + 2), MouseFilter = MouseFilterEnum.Ignore,
                LabelSettings = Style(PlaceFontSize, SectionColour, 0.5f),
            };
            _header.AddChild(place);
            var portrait = new TextureRect
            {
                Texture = Art.PortraitTexture(_stats.Factions[c]),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Size = Vector2.One * PortraitSize, Position = new Vector2(x + PointsIconSize + Gap, (height - PortraitSize) / 2),
                TooltipText = Factions.Name(_stats.Factions[c]), MouseFilter = MouseFilterEnum.Pass,
            };
            _header.AddChild(portrait);
        }
    }

    private static string Ordinal(int place) => place switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        _ => $"{place}th",
    };

    // ---------- The table ----------

    private void ShowTab(int index)
    {
        for (var i = 0; i < _tabs.Count; i++)
            _tabs[i].Selected = i == index;
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }
        _scroll.ScrollVertical = 0;
        if (_stats is null)
            return;
        StatTable table = index switch { 0 => _stats.VictoryPoints, 1 => _stats.Actions, _ => _stats.Resources };
        var stripe = 0;
        foreach (StatRow row in table.Rows)
        {
            var line = new Control
            {
                CustomMinimumSize = new Vector2(0, row.IsSection ? RowHeight * 1.15f : RowHeight),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            if (row.IsSection)
                stripe = 0;
            else if (stripe++ % 2 == 1)
            {
                var lit = new ColorRect { Color = StripeColour, MouseFilter = MouseFilterEnum.Ignore };
                line.AddChild(lit);
                lit.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            }
            float left = Padding * 0.4f;
            line.AddChild(new Label
            {
                Text = row.Label, VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(row.IsSection ? left : left + 18, 0),
                Size = new Vector2(NameWidth, row.IsSection ? RowHeight * 1.15f : RowHeight),
                LabelSettings = row.IsSection ? Style(SectionFontSize, SectionColour, 0.5f) : Style(RowFontSize, TextColour, 0f),
                MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
            });
            if (!row.IsSection)
                for (var c = 0; c < row.Values!.Count; c++)
                {
                    int? value = row.Values[c];
                    line.AddChild(new Label
                    {
                        Text = value is int number ? (number < 0 ? $"−{-number}" : number.ToString()) : "–",
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Position = new Vector2(ColumnsLeft - Frame - Padding - 6 + c * ColumnWidth, 0),
                        Size = new Vector2(ColumnWidth, RowHeight),
                        LabelSettings = Style(RowFontSize, value is null ? DashColour : TextColour, row.Label == "Total" ? 0.5f : 0f),
                        MouseFilter = MouseFilterEnum.Ignore,
                    });
                }
            _rows.AddChild(line);
        }
    }

    private static LabelSettings Style(int size, Color colour, float embolden) => new()
    {
        Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = embolden },
        FontSize = size, FontColor = colour,
        ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 2, ShadowOffset = new Vector2(0, 1),
    };
}
