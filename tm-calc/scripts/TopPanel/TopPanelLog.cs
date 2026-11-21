using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// The whole log, opened under the top panel by its log key (TopPanel.LogOpen): the panel's frame and stone round a
// sunken list (TopPanelArt.LogBacking), one record a row, the most recent at the top, scrolling. Each row: a bookmark
// button (gold when the record is bookmarked, an outline when not: BookmarkToggled), the jump arrow (JumpRequested: go
// to the game as it was just after that record), then the text. The latest record played is lit and bolder; records
// after it (undone, can be redone) dimmed and slanted. Panel pixels.
//   log.ShowEntries(entries, played: 8, bookmarked: new[] { 0, 5 }, locked: new[] { 0 })
//     entries[0..7] played (entries[7] is where the game is), entries[8..] can be redone; locked: bookmarks that can't
//     be removed (Session start), their buttons disabled.
//   ShowRows (the notes window, Ascending): rows in the order given (oldest first when Ascending), each with its own
//     state (played, where the game is, can be redone, on another line: dimmed, upright), its indent (Depth ×
//     IndentWidth) and "(Alternate) " before the text of a row that opens a line. Signals give the row's index.
public partial class TopPanelLog : Control
{
    public enum RowState { Played, Here, Later, Other }

    // One row: its text, its number (none: no "n. "), how far in, whether it opens a line ("(Alternate)"), its state,
    // whether it is bookmarked and whether that bookmark is locked (Session start).
    public sealed record Row(string Text, int? Number, int Depth, bool Opens, RowState State, bool Bookmarked, bool Locked);

    [Signal] public delegate void BookmarkToggledEventHandler(int index);
    [Signal] public delegate void JumpRequestedEventHandler(int index);

    public float RowHeight { get; set; } = 34;
    public int FontSize { get; set; } = 22;
    public int ButtonSize { get; set; } = 26;
    public float Embolden { get; set; } = -0.3f;       // the rows' weight (the default font is semibold)
    public float CurrentEmbolden { get; set; } = 0.3f; // the latest record played
    public Color TextColour { get; set; } = new("e4ebf1");
    public Color RedoColour { get; set; } = new(0.86f, 0.89f, 0.92f, 0.4f);
    public Color OtherColour { get; set; } = new(0.86f, 0.89f, 0.92f, 0.55f); // a row on another line (the notes)
    public float IndentWidth { get; set; } = 30;
    public bool Ascending { get; set; }                                       // oldest at the top (the notes)
    public Color CurrentFill { get; set; } = new(1, 1, 1, 0.1f);
    public Color Frame { get => _frame; set { _frame = value; Redress(); } }

    private Color _frame = IconArt.PowerPurple;
    private readonly TextureRect _backing = new()
    {
        MouseFilter = MouseFilterEnum.Stop, StretchMode = TextureRect.StretchModeEnum.Scale,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, // can be smaller than an old texture
    };
    private readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer _rows = new();
    private IReadOnlyList<Row> _shown = new List<Row>();
    private LabelSettings _plain, _current, _redo, _other;
    private Control _scrollTo;

    public TopPanelLog()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_backing);
        AddChild(_scroll);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(_rows);
        Resized += Redress;
        SetProcess(false);
    }

    // entries: every record, oldest first (shown newest first); played: how many have been played (the rest can be
    // redone); bookmarked: the indices (into entries) with a bookmark; locked: those whose bookmark can't be removed.
    public void ShowEntries(IReadOnlyList<string> entries, int played, IEnumerable<int> bookmarked = null, IEnumerable<int> locked = null)
    {
        entries ??= new List<string>();
        played = Mathf.Clamp(played, 0, entries.Count);
        var marked = bookmarked is null ? new HashSet<int>() : new HashSet<int>(bookmarked);
        var fixedMarks = locked is null ? new HashSet<int>() : new HashSet<int>(locked);
        ShowRows(entries.Select((text, i) => new Row(text, i == 0 ? null : i, 0, false,
            i == played - 1 ? RowState.Here : i >= played ? RowState.Later : RowState.Played,
            marked.Contains(i), fixedMarks.Contains(i))).ToList());
    }

    public void ShowRows(IReadOnlyList<Row> rows)
    {
        _shown = rows ?? new List<Row>();
        Rebuild();
    }

    private void Redress()
    {
        if (Size.X < 1 || Size.Y < 1)
            return;
        _backing.Texture = TopPanelArt.LogBacking(Size, _frame);
        _backing.Size = Size;
        Rect2 list = TopPanelArt.LogList(Size);
        _scroll.Position = list.Position + new Vector2(4, 4);
        _scroll.Size = list.Size - new Vector2(8, 8);
    }

    private void Rebuild()
    {
        foreach (Node row in _rows.GetChildren())
        {
            _rows.RemoveChild(row);
            row.QueueFree();
        }
        Font font = ThemeDB.FallbackFont;
        LabelSettings Settings(float embolden, bool slanted, Color colour) => new()
        {
            Font = new FontVariation
            {
                BaseFont = font,
                VariationEmbolden = embolden,
                VariationTransform = slanted ? new Transform2D(1, 0.2f, 0, 1, 0, 0) : Transform2D.Identity,
            },
            FontSize = FontSize,
            FontColor = colour,
        };
        _plain ??= Settings(Embolden, false, TextColour);
        _current ??= Settings(CurrentEmbolden, false, TextColour);
        _redo ??= Settings(Embolden, true, RedoColour);
        _other ??= Settings(Embolden, false, OtherColour);
        Control current = null;
        int count = _shown.Count;
        for (var n = 0; n < count; n++) // the most recent first, unless Ascending
        {
            int i = Ascending ? n : count - 1 - n;
            Row shown = _shown[i];
            bool here = shown.State == RowState.Here;
            var style = new StyleBoxFlat { BgColor = here ? CurrentFill : Colors.Transparent, ContentMarginLeft = 4, ContentMarginRight = 8 };
            style.SetCornerRadiusAll(6);
            var row = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeStyleboxOverride("panel", style);
            var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            line.AddThemeConstantOverride("separation", 6);
            row.AddChild(line);
            if (shown.Depth > 0)
                line.AddChild(new Control { CustomMinimumSize = new Vector2(shown.Depth * IndentWidth, 0), MouseFilter = MouseFilterEnum.Pass });

            int index = i;
            bool marked = shown.Bookmarked;
            TextureButton bookmark = RowButton(TopPanelArt.RowIcon(TopPanelArt.RowIconKind.Bookmark, marked, ButtonSize),
                shown.Locked ? "Session start: always bookmarked" : marked ? "Remove this bookmark" : "Bookmark this point");
            bookmark.Disabled = shown.Locked;
            bookmark.Pressed += () => EmitSignal(SignalName.BookmarkToggled, index);
            line.AddChild(bookmark);
            TextureButton jump = RowButton(TopPanelArt.RowIcon(TopPanelArt.RowIconKind.Jump, false, ButtonSize),
                here ? "The game is here" : shown.Opens && shown.State == RowState.Other ? "Take this line" : "Go to this point");
            jump.Disabled = here;
            if (here)
                jump.Modulate = new Color(1, 1, 1, 0.35f);
            jump.Pressed += () => EmitSignal(SignalName.JumpRequested, index);
            line.AddChild(jump);

            string whole = (shown.Opens ? "(Alternate) " : "") + (shown.Number is int number ? $"{number}. {shown.Text}" : shown.Text);
            var text = new Label
            {
                Text = whole,
                LabelSettings = shown.State switch
                {
                    RowState.Here => _current,
                    RowState.Later => _redo,
                    RowState.Other => _other,
                    _ => _plain,
                },
                VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, // only a word too long for a line
                ClipText = true,
                TooltipText = whole,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Pass,
            };
            // Too long for the row: a new line before a " · " (or between words), the row growing to hold them.
            text.Resized += () =>
            {
                text.Text = string.Join("\n", LogBreaks.Lines(whole, text.LabelSettings.Font, text.LabelSettings.FontSize, text.Size.X));
                text.TooltipText = text.Text;
            };
            line.AddChild(text);
            _rows.AddChild(row);
            if (here)
                current = row;
        }
        _scrollTo = current; // once the rows are laid out (next frame)
        SetProcess(current is not null);
    }

    // A small icon button (TextureButton): the pointing hand, a little lighter under the pointer.
    private TextureButton RowButton(Texture2D icon, string tip)
    {
        var button = new TextureButton
        {
            TextureNormal = icon,
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(ButtonSize, ButtonSize),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = tip,
        };
        button.MouseEntered += () => { if (!button.Disabled) button.SelfModulate = new Color(1.25f, 1.25f, 1.25f); };
        button.MouseExited += () => button.SelfModulate = Colors.White;
        return button;
    }

    public override void _Process(double delta)
    {
        if (_scrollTo is not null && IsInstanceValid(_scrollTo))
            _scroll.EnsureControlVisible(_scrollTo);
        _scrollTo = null;
        SetProcess(false);
    }
}
