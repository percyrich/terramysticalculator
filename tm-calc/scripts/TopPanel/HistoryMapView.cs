using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The bookmark map (Core's History.Map), in the top panel's style; it sits on the
// drop-down's sunken map section (HistoryOverlay draws that), in panel pixels.
//   Lines: every line a thin dark rim (RimWidth) round a core (CoreWidth): pale on the active line (ActiveColour), stone
//     grey off it (OtherColour). The game line has a wider gold stroke under that (GoldWidth beyond the rim each side),
//     so it shows whether or not it is the active line.
//   Ribbons: bookmarks gold with a dark outline (as the log's bookmark buttons); the unbookmarked end of the active line
//     an outline ribbon. Hovered (here or on the list): ringed and glowing in the frame colour, its name above it on a
//     dark box edged in the frame colour.
//   Where the player is: a small round stone knob with a jewel in the frame colour (the rules slider's, smaller), on its
//     ribbon or halfway along the active line.
//   A key to the lines in the bottom left corner (ShowLegend).
// Layout: every edge is EdgeLength across; each branch gets a row of its own RowGap apart, off the first line
// alternately above and below, further out on the same side for branches of branches. Each branch and everything
// branching off it take a band of rows; on each side of a line, the later a branch leaves it the nearer its band, so
// an edge never crosses or runs over another line (an earlier branch's edge passes the nearer bands before any of
// their lines start). Opening centres the whole map if
// it fits, otherwise where the player is.
// Mouse: drag to pan; the wheel pans up and down (with Shift, across); hovering a ribbon lights it; clicking it raises
// BookmarkClicked. HoverChanged tells the list which one is lit.
[GlobalClass]
public partial class HistoryMapView : Control
{
    [ExportGroup("Layout")]
    [Export] public float EdgeLength { get; set; } = 200f;
    [Export] public float RowGap { get; set; } = 110f;
    [Export] public float CoreWidth { get; set; } = 10f;
    [Export] public float RimWidth { get; set; } = 3f;
    [Export] public float GoldWidth { get; set; } = 4f;
    [Export] public int RibbonSize { get; set; } = 54;
    [Export] public float KnobRadius { get; set; } = 12f;
    [ExportGroup("Text")]
    [Export] public int NameFontSize { get; set; } = 18;
    [Export] public int LegendFontSize { get; set; } = 17;
    [Export] public bool ShowLegend { get; set; } = true;
    [ExportGroup("Mouse")]
    [Export] public float DragThreshold { get; set; } = 6f;  // how far the mouse moves before a press becomes a drag
    [Export] public float WheelStep { get; set; } = 40f;
    [ExportGroup("Colours")]
    [Export] public Color ActiveColour { get; set; } = TopPanelArt.MapActive;
    [Export] public Color OtherColour { get; set; } = TopPanelArt.MapOther;
    [Export] public Color RimColour { get; set; } = TopPanelArt.MapRim;
    [Export] public Color GameLineColour { get; set; } = TopPanelArt.MapGold;
    [Export] public Color TextColour { get; set; } = new("f2f6f9");
    [Export] public Color LegendColour { get; set; } = new("a9b9c8");

    // The top panel's frame colour (HistoryOverlay sets it): the knob's jewel, a lit ribbon, the name box's edge.
    public Color Frame { get => _frame; set { _frame = value; QueueRedraw(); } }

    public event Action<Bookmark> BookmarkClicked;
    public event Action<int?> HoverChanged;

    private Color _frame = IconArt.PowerPurple;
    private HistoryMap _map;
    private readonly Dictionary<int, Vector2> _points = new(); // each vertex, Session start at (0, 0)
    private Vector2 _origin;                                    // where (0, 0) is on this control
    private bool _centrePending;
    private int? _hovered, _highlighted;
    private Vector2? _pressedAt;
    private bool _dragging;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => Hover(null);
    }

    public void ShowMap(HistoryMap map)
    {
        _map = map;
        Layout();
        if (_hovered is int hovered && !_points.ContainsKey(hovered))
            Hover(null);
        QueueRedraw();
    }

    // Lit from outside (a name hovered in the list); null: none.
    public void Highlight(int? position)
    {
        _highlighted = position;
        QueueRedraw();
    }

    // Centres the whole map if it fits, otherwise where the player is (at the next draw, once this control has its size).
    public void CentreOnCurrent()
    {
        _centrePending = true;
        QueueRedraw();
    }

    // ---------- Layout ----------

    private void Layout()
    {
        _points.Clear();
        if (_map is null)
            return;
        // The lines: a vertex carries its edge's line on (same indent) or starts a branch (one step further in). Each
        // line is known by its first vertex; each records the branches leaving it, with the column they leave from.
        var columns = new Dictionary<int, int>();
        var indents = new Dictionary<int, int>();
        var lineOf = new Dictionary<int, int>();
        var branches = new Dictionary<int, List<(int Line, int Column)>>();
        int? root = null;
        foreach (MapVertex vertex in _map.Vertices) // each after the vertex its edge comes from
        {
            int p = vertex.Position;
            indents[p] = vertex.Indent;
            if (vertex.From is not int from || !columns.ContainsKey(from))
            {
                if (root is not null)
                    continue; // (only Session start has no edge)
                root = p;
                columns[p] = 0;
                lineOf[p] = p;
                branches[p] = new List<(int, int)>();
                continue;
            }
            columns[p] = columns[from] + 1;
            if (vertex.Indent == indents[from])
                lineOf[p] = lineOf[from];
            else
            {
                lineOf[p] = p;
                branches[p] = new List<(int, int)>();
                branches[lineOf[from]].Add((p, columns[from]));
            }
        }
        if (root is not int first)
            return;

        var rows = new Dictionary<int, int> { [first] = 0 };
        // A line's branches on one side (dir: -1 above, 1 below), the latest nearest; each band outside the last.
        // Returns the outermost row used.
        int Stack(IEnumerable<(int Line, int Column)> side, int row, int dir)
        {
            int outer = row;
            foreach (var (line, _) in side.OrderByDescending(b => b.Column))
            {
                rows[line] = outer + dir;
                outer = Stack(branches[line], outer + dir, dir); // its own branches further out, before the next band
            }
            return outer;
        }
        // Off the first line: alternately above and below, in the order they leave it.
        List<(int Line, int Column)> off = branches[first];
        Stack(off.Where((_, i) => i % 2 == 0), 0, -1);
        Stack(off.Where((_, i) => i % 2 == 1), 0, 1);

        foreach (var (position, column) in columns)
            _points[position] = new Vector2(column * EdgeLength, rows.GetValueOrDefault(lineOf[position]) * RowGap);
    }

    private Vector2 Current => _map.Towards is int towards ? (_points[_map.At] + _points[towards]) / 2 : _points[_map.At];

    private Vector2 At(int position) => _origin + _points[position];

    private void Centre()
    {
        var min = new Vector2(_points.Values.Min(p => p.X), _points.Values.Min(p => p.Y));
        var max = new Vector2(_points.Values.Max(p => p.X), _points.Values.Max(p => p.Y));
        float pad = RibbonSize, legend = ShowLegend ? 40 : 0;
        bool fits = max.X - min.X + 2 * pad <= Size.X && max.Y - min.Y + 2 * pad <= Size.Y - legend;
        _origin = fits ? new Vector2(Size.X / 2, (Size.Y - legend) / 2) - (min + max) / 2 : Size / 2 - Current;
    }

    // ---------- Drawing ----------

    public override void _Draw()
    {
        if (_map is null || _points.Count == 0)
            return;
        if (_centrePending && Size.X > 0 && Size.Y > 0)
        {
            Centre();
            _centrePending = false;
        }

        // The lines: the others first, the active line over them.
        var onGameLine = _map.Vertices.Where(v => v.OnGameLine).Select(v => v.Position).ToHashSet();
        foreach (bool active in new[] { false, true })
            foreach (MapVertex vertex in _map.Vertices)
            {
                if (vertex.From is not int from || vertex.Active != active)
                    continue;
                DrawMapLine(At(from), At(vertex.Position), active, vertex.OnGameLine && onGameLine.Contains(from), 1f);
            }

        foreach (MapVertex vertex in _map.Vertices)
        {
            bool lit = vertex.Bookmark is not null && (vertex.Position == _hovered || vertex.Position == _highlighted);
            Texture2D ribbon = TopPanelArt.Ribbon(vertex.Bookmark is not null, lit, _frame, RibbonSize);
            DrawTexture(ribbon, At(vertex.Position) - ribbon.GetSize() / 2);
        }
        Vector2 here = _origin + Current - (_map.Towards is null ? new Vector2(0, 4) : Vector2.Zero);
        Texture2D knob = TopPanelArt.HereKnob(KnobRadius, _frame);
        DrawTexture(knob, here - knob.GetSize() / 2);

        if (_hovered is int hovered && _map.Vertices.FirstOrDefault(v => v.Position == hovered)?.Bookmark is Bookmark bookmark)
            DrawName(At(hovered), bookmark.Name);
        if (ShowLegend)
            DrawLegend();
    }

    // A line: the gold under it on the game line, the dark rim, the core; round ends.
    private void DrawMapLine(Vector2 a, Vector2 b, bool active, bool game, float scale)
    {
        void Stroke(Color colour, float width)
        {
            DrawLine(a, b, colour, width, true);
            DrawCircle(a, width / 2, colour, true, -1, true);
            DrawCircle(b, width / 2, colour, true, -1, true);
        }
        float core = CoreWidth * scale, rim = RimWidth * scale, gold = GoldWidth * scale;
        if (game)
            Stroke(GameLineColour, core + 2 * rim + 2 * gold);
        Stroke(RimColour, core + 2 * rim);
        Stroke(active ? ActiveColour : OtherColour, core);
    }

    // The name above the ribbon, on a dark box edged in the frame colour (as a cut-short top panel line shown whole).
    private void DrawName(Vector2 centre, string name)
    {
        Font font = ThemeDB.FallbackFont;
        Vector2 size = font.GetStringSize(name, HorizontalAlignment.Left, -1, NameFontSize);
        float w = size.X + 20, h = 30;
        var box = new Rect2(centre + new Vector2(-w / 2, -RibbonSize * 0.5f - h - 6), new Vector2(w, h));
        var style = new StyleBoxFlat { BgColor = TopPanelArt.SectionColour, BorderColor = _frame };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        DrawStyleBox(style, box);
        float baseline = box.Position.Y + (h - font.GetHeight(NameFontSize)) / 2 + font.GetAscent(NameFontSize);
        DrawString(font, new Vector2(box.Position.X, baseline), name, HorizontalAlignment.Center, w, NameFontSize, TextColour);
    }

    // The key, bottom left: active line, other line, game line, where you are.
    private void DrawLegend()
    {
        Font font = ThemeDB.FallbackFont;
        float x = 18, y = Size.Y - 22, baseline = y - font.GetHeight(LegendFontSize) / 2 + font.GetAscent(LegendFontSize);
        void Item(Action<float> draw, string text)
        {
            draw(x);
            DrawString(font, new Vector2(x + 46, baseline), text, HorizontalAlignment.Left, -1, LegendFontSize, LegendColour);
            x += 46 + font.GetStringSize(text, HorizontalAlignment.Left, -1, LegendFontSize).X + 22;
        }
        Item(x0 => DrawMapLine(new Vector2(x0 + 4, y), new Vector2(x0 + 34, y), true, false, 0.8f), "Active line");
        Item(x0 => DrawMapLine(new Vector2(x0 + 4, y), new Vector2(x0 + 34, y), false, false, 0.8f), "Other line");
        Item(x0 => DrawMapLine(new Vector2(x0 + 4, y), new Vector2(x0 + 34, y), true, true, 0.8f), "Game line");
        Item(x0 =>
        {
            Texture2D knob = TopPanelArt.HereKnob(9, _frame);
            DrawTexture(knob, new Vector2(x0 + 18, y) - knob.GetSize() / 2);
        }, "You are here");
    }

    // ---------- Mouse ----------

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _pressedAt = button.Position;
                    _dragging = false;
                }
                else
                {
                    if (!_dragging && BookmarkAt(button.Position) is Bookmark bookmark)
                        BookmarkClicked?.Invoke(bookmark);
                    _pressedAt = null;
                    _dragging = false;
                    MouseDefaultCursorShape = _hovered is null ? CursorShape.Arrow : CursorShape.PointingHand;
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true } wheel when wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
                or MouseButton.WheelLeft or MouseButton.WheelRight:
                Vector2 step = wheel.ButtonIndex switch
                {
                    MouseButton.WheelUp => new Vector2(0, WheelStep),
                    MouseButton.WheelDown => new Vector2(0, -WheelStep),
                    MouseButton.WheelLeft => new Vector2(WheelStep, 0),
                    _ => new Vector2(-WheelStep, 0),
                };
                _origin += wheel.ShiftPressed ? new Vector2(step.Y, step.X) : step;
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_pressedAt is Vector2 pressedAt && (_dragging || motion.Position.DistanceTo(pressedAt) > DragThreshold))
                {
                    _origin += _dragging ? motion.Relative : motion.Position - pressedAt; // the whole way so far, the first time
                    _dragging = true;
                    QueueRedraw();
                }
                Hover(_dragging ? null : VertexAt(motion.Position));
                MouseDefaultCursorShape = _dragging ? CursorShape.Drag : _hovered is null ? CursorShape.Arrow : CursorShape.PointingHand;
                break;
        }
    }

    private void Hover(int? position)
    {
        if (position == _hovered)
            return;
        _hovered = position;
        QueueRedraw();
        HoverChanged?.Invoke(position);
    }

    // The bookmark whose ribbon is under the point (in this control's pixels), if any.
    private int? VertexAt(Vector2 point)
    {
        if (_map is null)
            return null;
        var size = new Vector2(RibbonSize * 0.6f, RibbonSize * 0.94f);
        foreach (MapVertex vertex in _map.Vertices)
            if (vertex.Bookmark is not null && new Rect2(At(vertex.Position) - size / 2, size).Grow(4).HasPoint(point))
                return vertex.Position;
        return null;
    }

    private Bookmark BookmarkAt(Vector2 point) =>
        VertexAt(point) is int position ? _map.Vertices.First(v => v.Position == position).Bookmark : null;
}
