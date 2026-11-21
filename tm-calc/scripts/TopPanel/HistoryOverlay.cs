using System;
using System.Collections.Generic;
using Core;
using Godot;

namespace TMCalc;

// The bookmark map, opened by the top panel's map key: a drop-down out of the top panel, as the log is. The screen behind is dimmed (DimColour, the log's) and takes the clicks, so nothing else can be
// used; a click there closes it. Main keeps it on the top panel's layer, under the panel, so the keys still work.
// The drop-down (panel pixels, scaled by the top panel's PanelScale): the top panel's frame and stone, as wide as the
// bar, hanging from its bottom edge (Under), Height tall. Inside:
//   left: a sunken list of the bookmarks in tree order, branches indented (Core's History.Map). Each row: where the
//     player is (the knob, on that row only; the row lit and bold), a gold ribbon, the name, a pencil and a bin (neither
//     for Session start). Hovering a row lights its ribbon on the map (and hovering a ribbon lights its row); pressing
//     the name jumps there. The pencil puts a text box over the name, all of it selected: Enter or a click elsewhere
//     keeps the new name (RenameRequested), Esc puts the old one back; an empty name is not kept.
//     Below: stone keys with words: Save, Save as, Load; BGA Bookmarklet, Load BGA JSON; Mark as game line, Back to
//     game line; Open notes (NotesRequested: Main's notes window), Fullscreen (a toggle, FullscreenToggled: Main puts
//     the game's window into fullscreen on the screen it is on, or back), Quit (QuitRequested: Main quits at once).
//   right: the map (HistoryMapView) on a sunken section.
// Built in code. Main sets Under (the TopPanel) and calls Open, ShowMap (after every change while open) and Close, and
// hears JumpRequested, DeleteRequested, RenameRequested and the key events (raised after the click has been handled,
// so the list can be rebuilt safely). CanMarkGameLine and CanReturnToGameLine dim those keys.
[GlobalClass]
public partial class HistoryOverlay : Control
{
    [ExportGroup("Layout")]
    [Export] public float Height { get; set; } = 620f;          // panel pixels (four rows of keys)
    [Export] public float ListWidth { get; set; } = 430f;
    [Export] public float RowHeight { get; set; } = 34f;
    [Export] public int FontSize { get; set; } = 22;
    [Export] public int RowIconSize { get; set; } = 24;
    [Export] public float IndentWidth { get; set; } = 24f;
    [Export] public float KeyHeight { get; set; } = 44f;
    [Export] public float KeyGap { get; set; } = 6f;
    [ExportGroup("Colours")]
    [Export] public Color DimColour { get; set; } = new(0, 0, 0, 0.45f); // as Main.LogDimColour
    [Export] public Color TextColour { get; set; } = new("e4ebf1");
    [Export] public Color HeadingColour { get; set; } = new("a9b9c8");
    [Export] public Color CurrentRowColour { get; set; } = new(1, 1, 1, 0.1f);
    [Export] public Color HoverRowColour { get; set; } = new(1, 1, 1, 0.06f);
    [ExportGroup("Fallback placing")]
    // Without Under: where the drop-down goes (screen pixels), its width there and its scale.
    [Export] public Vector2 FallbackPosition { get; set; } = new(100, 100);
    [Export] public float FallbackWidth { get; set; } = 880f;
    [Export] public float FallbackScale { get; set; } = 0.6f;

    // The top panel the drop-down hangs from: its position, width, scale and frame colour are followed on every Open
    // and ShowMap.
    public TopPanel Under { get; set; }
    public bool CanMarkGameLine { get => !_mark.Disabled; set => _mark.Disabled = !value; }
    public bool CanReturnToGameLine { get => !_back.Disabled; set => _back.Disabled = !value; }

    public event Action<Bookmark> JumpRequested;
    public event Action<Bookmark> DeleteRequested;
    public event Action<Bookmark, string> RenameRequested;
    public event Action SaveRequested, SaveAsRequested, LoadRequested, MarkGameLineRequested, ReturnToGameLineRequested;
    public event Action BookmarkletRequested, LoadBgaRequested, NotesRequested, QuitRequested;
    public event Action<bool> FullscreenToggled;
    // The Fullscreen key's state (set without raising FullscreenToggled).
    public bool Fullscreen { get => _fullscreen.ButtonPressed; set => _fullscreen.SetPressedNoSignal(value); }

    public bool IsOpen => Visible;

    private const float Frame = TopPanelArt.Frame, Inset = 10;   // the frame; the space inside it and between sections
    private readonly Control _drawer = new() { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
    private readonly TextureRect _backing = new()
    {
        Name = "Backing", MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
    };
    private readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer _rows = new();
    private readonly Label _heading = new() { Text = "Bookmarks", MouseFilter = MouseFilterEnum.Ignore };
    private readonly HistoryMapView _map = new() { Name = "Map" };
    private readonly TopPanelTextKey _save = new() { Text = "Save", TooltipText = "Save the whole history (Ctrl+S)" };
    private readonly TopPanelTextKey _saveAs = new() { Text = "Save as", TooltipText = "Save under a new name (Ctrl+Shift+S)" };
    private readonly TopPanelTextKey _load = new() { Text = "Load", TooltipText = "Load a saved history (Ctrl+O)" };
    private readonly TopPanelTextKey _bookmarklet = new() { Text = "BGA Bookmarklet", TooltipText = "The bookmarklet that exports a game from Board Game Arena" };
    private readonly TopPanelTextKey _loadBga = new() { Text = "Load BGA JSON", TooltipText = "Convert a game exported from Board Game Arena and load it" };
    private readonly TopPanelTextKey _mark = new() { Text = "Mark as game line", TooltipText = "The line you are on becomes the game being followed" };
    private readonly TopPanelTextKey _back = new() { Text = "Back to game line", TooltipText = "Back to where you left the game line" };
    private readonly TopPanelTextKey _notes = new() { Text = "Open notes", TooltipText = "The whole log, with every line, in a window of its own" };
    private readonly TopPanelTextKey _fullscreen = new()
    {
        Text = "Fullscreen", ToggleMode = true,
        TooltipText = "Fill the screen the game's window is on (move the window to another screen first), or back to a window",
    };
    private readonly TopPanelTextKey _quit = new() { Text = "Quit", TooltipText = "Close the program now (nothing is asked)" };
    private readonly Dictionary<int, BookmarkRow> _rowsByPosition = new();
    private HistoryMap _shown;
    private int? _listHovered, _mapHovered;
    private Color _frame = IconArt.PowerPurple;
    private BookmarkRow _editing;
    internal Color FrameColour => _frame;
    // While a name is being typed: Main's key handling (_Input, which comes first) must leave keys alone then, so that
    // typing B or M doesn't bookmark or close, and Esc puts the old name back instead of closing the map.
    public bool IsRenaming => _editing is not null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the dimmed screen takes the clicks meant for the board
        var dim = new ColorRect { Name = "Dim", Color = DimColour, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        AddChild(_drawer);
        _drawer.AddChild(_backing);
        _heading.LabelSettings = new LabelSettings { Font = ThemeDB.FallbackFont, FontSize = 18, FontColor = HeadingColour };
        _drawer.AddChild(_heading);
        _drawer.AddChild(_scroll);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(_rows);
        foreach (TopPanelTextKey key in new[] { _save, _saveAs, _load, _bookmarklet, _loadBga, _mark, _back, _notes, _fullscreen, _quit })
            _drawer.AddChild(key);
        _save.Pressed += () => Later(() => SaveRequested?.Invoke());
        _saveAs.Pressed += () => Later(() => SaveAsRequested?.Invoke());
        _load.Pressed += () => Later(() => LoadRequested?.Invoke());
        _bookmarklet.Pressed += () => Later(() => BookmarkletRequested?.Invoke());
        _loadBga.Pressed += () => Later(() => LoadBgaRequested?.Invoke());
        _mark.Pressed += () => Later(() => MarkGameLineRequested?.Invoke());
        _back.Pressed += () => Later(() => ReturnToGameLineRequested?.Invoke());
        _notes.Pressed += () => Later(() => NotesRequested?.Invoke());
        _quit.Pressed += () => Later(() => QuitRequested?.Invoke());
        _fullscreen.Toggled += on => Later(() => FullscreenToggled?.Invoke(on));
        _drawer.AddChild(_map);
        _map.BookmarkClicked += bookmark => Later(() => JumpRequested?.Invoke(bookmark));
        _map.HoverChanged += position =>
        {
            _mapHovered = position;
            LightRows();
        };
        Visible = false;
    }

    public void Open(HistoryMap map)
    {
        Visible = true;
        _shown = null;
        ShowMap(map);
        _map.CentreOnCurrent();
    }

    // A click on the dimmed screen, outside the drop-down, closes it (the drop-down stops clicks inside it). The wheel
    // doesn't.
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle })
        {
            AcceptEvent();
            Close();
        }
    }

    public void Close()
    {
        if (_editing is not null)
            EndRename(keep: true);
        Visible = false;
        _listHovered = _mapHovered = null;
        _map.Highlight(null);
    }

    // Shows this map (the list is rebuilt only if it has changed); follows the top panel's place and colour.
    public void ShowMap(HistoryMap map)
    {
        Place();
        if (map == _shown)
            return;
        if (_editing is not null)
            EndRename(keep: true);
        _shown = map;
        ShowRows(map);
        _map.ShowMap(map);
    }

    // ---------- Placing ----------

    private void Place()
    {
        Vector2 position = Under is null ? FallbackPosition : Under.Position + new Vector2(0, Under.ShownSize.Y);
        float scale = Under?.PanelScale ?? FallbackScale, width = (Under?.ShownWidth ?? FallbackWidth) / scale;
        Color frame = Under?.FrameColour ?? IconArt.PowerPurple;
        var size = new Vector2(width, Height);
        bool changed = !_drawer.Size.IsEqualApprox(size) || frame != _frame || _backing.Texture is null;
        _drawer.Position = position;
        _drawer.Scale = Vector2.One * scale;
        _frame = frame;
        _map.Frame = frame;
        if (!changed)
            return;

        float x0 = Frame + Inset, y0 = Frame + Inset, keysH = 4 * KeyHeight + 3 * KeyGap;
        var list = new Rect2(x0, y0, ListWidth, Height - 2 * y0 - keysH - Inset);
        float mapX = x0 + ListWidth + Inset;
        var mapArea = new Rect2(mapX, y0, width - mapX - x0, Height - 2 * y0);
        _drawer.CustomMinimumSize = size;
        _drawer.Size = size;
        _backing.Texture = TopPanelArt.DrawerBacking(size, frame, (list, 12), (mapArea, 14));
        _backing.Size = size;

        _heading.Position = list.Position + new Vector2(14, 8);
        _heading.Size = new Vector2(list.Size.X - 28, 28);
        _scroll.Position = list.Position + new Vector2(4, 40);
        _scroll.Size = list.Size - new Vector2(8, 44);

        float ky = list.End.Y + Inset;
        void Keys(float y, params TopPanelTextKey[] keys)
        {
            float w = (ListWidth - (keys.Length - 1) * KeyGap) / keys.Length;
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i].Position = new Vector2(x0 + i * (w + KeyGap), y);
                keys[i].Size = new Vector2(w, KeyHeight);
            }
        }
        Keys(ky, _save, _saveAs, _load);
        Keys(ky + KeyHeight + KeyGap, _bookmarklet, _loadBga);
        Keys(ky + 2 * (KeyHeight + KeyGap), _mark, _back);
        Keys(ky + 3 * (KeyHeight + KeyGap), _notes, _fullscreen, _quit);

        _map.Position = mapArea.Position + new Vector2(4, 4); // a little inside the section, so lines keep off its corners
        _map.Size = mapArea.Size - new Vector2(8, 8);
    }

    // ---------- The list ----------

    private void ShowRows(HistoryMap map)
    {
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }
        _rowsByPosition.Clear();
        _listHovered = null;
        _map.Highlight(null);
        int? here = map.Towards is null ? map.At : null;
        foreach (MapVertex vertex in map.Vertices)
        {
            if (vertex.Bookmark is not Bookmark bookmark)
                continue; // the end of the active line, not bookmarked: on the map only
            var row = new BookmarkRow();
            row.Setup(this, bookmark, vertex.Indent, vertex.Position == here, vertex.From is null);
            _rows.AddChild(row);
            _rowsByPosition[vertex.Position] = row;
        }
        LightRows();
    }

    internal void HoverRow(int? position)
    {
        _listHovered = position;
        _map.Highlight(position);
        LightRows();
    }

    // The row hovered here or whose ribbon is hovered on the map is lit.
    private void LightRows()
    {
        foreach (var (position, row) in _rowsByPosition)
            row.Lit = position == _listHovered || position == _mapHovered;
    }

    internal void StartRename(BookmarkRow row)
    {
        if (_editing is not null && _editing != row)
            EndRename(keep: true);
        _editing = row;
        row.StartEditing();
    }

    internal void EndRename(bool keep)
    {
        BookmarkRow row = _editing;
        _editing = null;
        if (row is null)
            return;
        string name = row.StopEditing(keep);
        if (name is not null)
            Later(() => RenameRequested?.Invoke(row.Bookmark, name));
    }

    internal bool IsEditing(BookmarkRow row) => _editing == row;
    internal bool IsListHovered(int position) => _listHovered == position;
    internal void RequestJump(Bookmark bookmark) => Later(() => JumpRequested?.Invoke(bookmark));
    internal void RequestDelete(Bookmark bookmark) => Later(() => DeleteRequested?.Invoke(bookmark));

    // After the click that asked for it has finished (the list may be rebuilt).
    internal static void Later(Action action) => Callable.From(action).CallDeferred();
}
