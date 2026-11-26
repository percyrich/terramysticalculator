using System.Linq;
using System.Collections.Generic;
using Godot;

namespace TMCalc;

// The panel across the top of the map: the player panels' framed stone
// backing; at the left the game button (a PlayerButton, power purple, in a round sunken socket); a sunken text
// section with the message about what to do next over the latest log entry, and at its right end the log key, which
// opens the whole log under the bar (TopPanelLog); then a sunken section of four stone keys: undo, redo, bookmark,
// bookmark map. Past the bar's right end, from its top down to its midline, the rules tab (one outline with the bar):
// a three-notch slider, Strict, Lenient, Free (TopPanelSlider). Made entirely in code: add a TopPanel anywhere (as a
// Control) and set it up from code.
//
// Size: built in panel pixels (the player panels' own) and scaled by PanelScale (0.6), so it matches the panels and buttons on screen. ShownWidth is its width in
// its parent's pixels: set it to the map's width there (the tab then sticks out TopPanelArt.TabReach, scaled, past it, to the right).
// ShownSize is the bar's size in the parent's pixels, without the tab (the log hangs below it when open).
//
// Use:
//   panel.Message = "Witches: take an action";        the top line (cut short with … if too long)
//   panel.LogEntry = "Giants take the 2-spade power action";
//   panel.GameButton.Show(PlayerButton.Mode.Waiting, panel.GameButtonColour, animate: true);
//   panel.CanUndo / CanRedo = …;                       the key is dimmed and can't be pressed when false
//   panel.Bookmarked = true;                           this position has a bookmark: gold ribbon, key disabled
//   panel.MapOpen = true;                              lights the map key (the bookmark map is open)
//   panel.ShowLog(entries, played, bookmarked);        what the whole log lists (see TopPanelLog.ShowEntries)
//   panel.UndoPressed += …; RedoPressed, BookmarkPressed, MapPressed, GamePressed (the game button's Pressed);
//   panel.LogToggled += open => …;                     the log key opens and closes the log itself (LogOpen)
//   panel.Mode = state.Mode;                           the rules slider's knob glides there (shown at once the first time)
//   panel.ModeChosen += mode => …;                     (int)PlayMode the knob was let go on, if it changed
// The PlayerButton keeps its own rules: Disabled unless shown as Confirm.
[GlobalClass]
public partial class TopPanel : Control
{
    [Signal] public delegate void GamePressedEventHandler();
    [Signal] public delegate void UndoPressedEventHandler();
    [Signal] public delegate void RedoPressedEventHandler();
    [Signal] public delegate void BookmarkPressedEventHandler();
    [Signal] public delegate void MapPressedEventHandler();
    [Signal] public delegate void LogToggledEventHandler(bool open);
    [Signal] public delegate void ModeChosenEventHandler(int mode); // a Core.PlayMode

    [ExportGroup("Size")]
    [Export] public float ShownWidth { get => _shownWidth; set { _shownWidth = value; Relayout(); } }
    [Export] public float PanelScale { get => _panelScale; set { _panelScale = Mathf.Max(0.05f, value); Relayout(); } }
    [ExportGroup("Look")]
    [Export] public Color FrameColour { get => _frame; set { _frame = value; Redress(); } }
    [Export] public Color GameButtonColour { get; set; } = IconArt.PowerPurple;
    [Export] public Color MessageColour { get => _messageColour; set { _messageColour = value; Restyle(); } }
    [Export] public Color LogColour { get => _logColour; set { _logColour = value; Restyle(); } }
    [ExportGroup("")]

    public PlayerButton GameButton { get; } = new() { Name = "GameButton" };
    public TopPanelLog Log { get; } = new() { Name = "Log", Visible = false };
    public TopPanelSlider Rules { get; } = new()
    {
        Name = "Rules",
        TooltipText = "How strictly the rules apply", // the label under the pointer gives its own (TopPanelSlider.Tooltips)
    };
    // The rules slider's setting (no signal); ModeChosen fires when the player moves it.
    public Core.PlayMode Mode { get => (Core.PlayMode)Rules.Value; set => Rules.Value = (int)value; }

    public string Message { get => _message.Text; set { _message.Text = value ?? ""; RefreshExpanded(); } }
    // The message in italics (a passing note or a prompt, not the step's own message).
    public bool MessageItalic { get => _messageItalic; set { _messageItalic = value; Restyle(); } }
    public string LogEntry { get => _logText; set { _logText = value ?? ""; _logSegments = null; RefreshLogLine(); RefreshExpanded(); } }

    // The log line in colour (Main.LogSegments): each part in its faction's colour.
    public void ShowLogEntry(IReadOnlyList<LogSegment> segments)
    {
        _logSegments = segments;
        _logText = segments is null ? "" : LogRichText.Plain(segments);
        RefreshLogLine();
        RefreshExpanded();
    }
    public bool CanUndo { get => !Key(TopPanelArt.KeyKind.Undo).Disabled; set => Key(TopPanelArt.KeyKind.Undo).Disabled = !value; }
    public bool CanRedo { get => !Key(TopPanelArt.KeyKind.Redo).Disabled; set => Key(TopPanelArt.KeyKind.Redo).Disabled = !value; }
    public bool Bookmarked
    {
        get => Key(TopPanelArt.KeyKind.Bookmark).Filled;
        set { Key(TopPanelArt.KeyKind.Bookmark).Filled = value; Key(TopPanelArt.KeyKind.Bookmark).Disabled = value; }
    }
    public bool MapOpen { get => Key(TopPanelArt.KeyKind.Map).On; set => Key(TopPanelArt.KeyKind.Map).On = value; }
    public bool LogOpen
    {
        get => Log.Visible;
        set { Log.Visible = value; Key(TopPanelArt.KeyKind.Log).On = value; }
    }

    // The bar's size in the parent's pixels (without the log or the tab); how far the tab sticks out past it.
    public Vector2 ShownSize => new(_shownWidth, TopPanelArt.Height * _panelScale);

    public TopPanelKey Key(TopPanelArt.KeyKind kind) => _keys[kind];

    private float _shownWidth = 880, _panelScale = 0.6f;
    private const float LogHeight = 600; // panel pixels (doubled 2026-11-24: more of the log at once)
    private Color _frame = IconArt.PowerPurple, _messageColour = TopPanelArt.MessageColour, _logColour = TopPanelArt.LogColour;
    private const int MessageFontSize = 25, LogFontSize = 26; // (the log line: 22 → 24 → 26 with the coloured text)
    private const float MessageEmbolden = 0f, LogEmbolden = 0f; // the default font is semibold (the log as it is)
    private bool _messageItalic;
    // A line cut short (…) shown whole while the pointer is over it: a copy of it on a dark box of its own, as wide as the
    // text needs, over whatever is to its right.
    private readonly Label _expanded = new() { Name = "Expanded", Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10 };
    private Label _hovered;
    private string _logText = ""; // LogEntry whole; the line shows its first line (LogBreaks.FirstLine)
    private IReadOnlyList<LogSegment> _logSegments; // its colours (null: all in LogColour)
    // The log line and its hovered whole in colour, over the plain Label (kept, its text clear, for the hover and the
    // measuring).
    private readonly LogTextView _logRich = new() { Name = "LogEntryColoured", CentreVertically = true },
        _expandedRich = new() { Name = "ExpandedColoured" };
    private readonly Panel _expandedBack = new() { Name = "ExpandedColouredBack" };
    // IgnoreSize: otherwise a TextureRect can't be smaller than its texture, so after a narrower relayout it kept the old
    // width and stretched the new picture (the sections drifted away from the keys).
    private readonly TextureRect _backing = new()
    {
        Name = "Backing", MouseFilter = MouseFilterEnum.Stop, StretchMode = TextureRect.StretchModeEnum.Scale,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
    };
    private readonly Label _message = new() { Name = "Message" }, _logLine = new() { Name = "LogEntry" };
    private readonly Dictionary<TopPanelArt.KeyKind, TopPanelKey> _keys = new();

    private static readonly (TopPanelArt.KeyKind Kind, string Tip)[] KeyTips =
    {
        (TopPanelArt.KeyKind.Log, "The whole log"),
        (TopPanelArt.KeyKind.Undo, "Back one move (Ctrl+Z)"),
        (TopPanelArt.KeyKind.Redo, "Forward one move (Ctrl+Y)"),
        (TopPanelArt.KeyKind.Bookmark, "Bookmark this position (B)"),
        (TopPanelArt.KeyKind.Map, "Bookmark map (M)"),
    };

    public TopPanel()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_backing);
        foreach (Label label in new[] { _message, _logLine })
        {
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.ClipText = true;
            label.MouseFilter = MouseFilterEnum.Pass;
            AddChild(label);
            Label line = label;
            label.MouseEntered += () => { _hovered = line; RefreshExpanded(); };
            label.MouseExited += () => { if (_hovered == line) _hovered = null; RefreshExpanded(); };
        }
        GameButton.Diameter = TopPanelArt.ButtonDiameter;
        GameButton.RingWidth = TopPanelArt.ButtonRing;
        AddChild(GameButton);
        GameButton.Pressed += () => EmitSignal(SignalName.GamePressed);
        foreach ((TopPanelArt.KeyKind kind, string tip) in KeyTips)
        {
            var key = new TopPanelKey { Name = kind + "Key", Kind = kind, TooltipText = tip };
            _keys[kind] = key;
            AddChild(key);
        }
        Key(TopPanelArt.KeyKind.Log).Pressed += () =>
        {
            LogOpen = !LogOpen;
            EmitSignal(SignalName.LogToggled, LogOpen);
        };
        Key(TopPanelArt.KeyKind.Undo).Pressed += () => EmitSignal(SignalName.UndoPressed);
        Key(TopPanelArt.KeyKind.Redo).Pressed += () => EmitSignal(SignalName.RedoPressed);
        Key(TopPanelArt.KeyKind.Bookmark).Pressed += () => EmitSignal(SignalName.BookmarkPressed);
        Key(TopPanelArt.KeyKind.Map).Pressed += () => EmitSignal(SignalName.MapPressed);
        AddChild(Rules);
        Rules.ValueChosen += value => EmitSignal(SignalName.ModeChosen, value);
        AddChild(Log);
        AddChild(_expanded);
        _logRich.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_logRich);
        _expandedRich.MouseFilter = MouseFilterEnum.Ignore;
        _expandedRich.Visible = false;
        _expandedRich.ZIndex = 10;
        // Its box on a panel of its own behind it: a box of the label's own would cover the glow layers behind its text.
        _expandedBack.MouseFilter = MouseFilterEnum.Ignore;
        _expandedBack.Visible = false;
        _expandedBack.ZIndex = 10;
        AddChild(_expandedBack);
        AddChild(_expandedRich);
        _expandedRich.VisibilityChanged += () => _expandedBack.Visible = _expandedRich.Visible;
        Restyle();
        Relayout();
    }

    public override void _Ready()
    {
        if (GameButton.Shown == PlayerButton.Mode.Hidden)
            GameButton.Show(PlayerButton.Mode.Blank, GameButtonColour, animate: false);
    }

    // What the whole log lists; see TopPanelLog.ShowEntries.
    public void ShowLog(IReadOnlyList<string> entries, int played, IEnumerable<int> bookmarked = null, IEnumerable<int> locked = null) =>
        Log.ShowEntries(entries, played, bookmarked, locked);

    // ---------- Layout ----------

    private float PanelWidth => _shownWidth / _panelScale;

    private void Relayout()
    {
        Scale = Vector2.One * _panelScale;
        TopPanelArt.BarLayout l = TopPanelArt.Layout(PanelWidth);
        var full = new Vector2(l.FullWidth, l.Size.Y); // the bar and the tab
        CustomMinimumSize = full; // first: Size can't go below the old minimum
        Size = full;
        Redress(); // the new picture before sizing it
        _backing.Position = Vector2.Zero;
        _backing.Size = full;
        Rules.Position = l.Rules.Position;
        Rules.Size = l.Rules.Size;
        GameButton.Position = l.Socket - GameButton.Size / 2;
        _message.Position = l.MessageLine.Position;
        _message.Size = l.MessageLine.Size;
        _logLine.Position = l.LogLine.Position;
        _logLine.Size = l.LogLine.Size;
        _logRich.Position = l.LogLine.Position;
        _logRich.Size = l.LogLine.Size;
        RefreshLogLine();
        Key(TopPanelArt.KeyKind.Log).Position = l.LogKey.Position;
        var history = new[] { TopPanelArt.KeyKind.Undo, TopPanelArt.KeyKind.Redo, TopPanelArt.KeyKind.Bookmark, TopPanelArt.KeyKind.Map };
        for (var i = 0; i < history.Length; i++)
            Key(history[i]).Position = l.Keys[i].Position;
        Log.Position = new Vector2(0, l.Size.Y + TopPanelArt.LogGap);
        Log.Size = new Vector2(l.Size.X, LogHeight);
        Redress();
    }

    // The pictures in the frame colour.
    private void Redress()
    {
        _backing.Texture = TopPanelArt.Backing(PanelWidth, _frame);
        foreach (TopPanelKey key in _keys.Values)
            key.Frame = _frame;
        Rules.Frame = _frame;
        Log.Frame = _frame;
    }

    private void Restyle()
    {
        _message.LabelSettings = Text(MessageFontSize, MessageEmbolden, _messageColour, _messageItalic);
        _logLine.LabelSettings = Text(LogFontSize, LogEmbolden, Colors.Transparent, false); // drawn by _logRich
        _logRich.Style(_logLine.LabelSettings.Font, LogFontSize, LogOutline);
        _expandedRich.Style(_logLine.LabelSettings.Font, LogFontSize, LogOutline);
        RefreshLogLine();
        RefreshExpanded();
    }

    private static LabelSettings Text(int size, float embolden, Color colour, bool italic) => new()
    {
        Font = new FontVariation
        {
            BaseFont = ThemeDB.FallbackFont,
            VariationEmbolden = embolden,
            VariationTransform = italic ? new Transform2D(1, 0.2f, 0, 1, 0, 0) : Transform2D.Identity, // slanted
        },
        FontSize = size,
        FontColor = colour,
    };

    // The log line: the first line of the record, "…" after it if there is more.
    private void RefreshLogLine()
    {
        LabelSettings settings = _logLine.LabelSettings;
        _logLine.Text = settings is null ? _logText : LogBreaks.FirstLine(_logText, settings.Font, settings.FontSize, _logLine.Size.X);
        _logRich.Show(LogSegments, new[] { _logLine.Text }, MoreLook);
    }

    private const int LogOutline = 4;
    private IReadOnlyList<LogSegment> LogSegments => _logSegments ?? LogRichText.Single(_logText, _logColour, new Color(0, 0, 0, 0.85f));
    // The " …" after a cut-short line: lavender, glowing as the text does.
    private LogSegment MoreLook => new(LogBreaks.More, LogRichText.Readable(IconArt.PowerPurple), LogRichText.Stroke,
        _logSegments?.Select(s => s.Glow).FirstOrDefault(g => g is not null));

    // The hovered line whole, if it is cut short (nothing otherwise): the message on one longer line; the log record
    // in the log line's width, broken onto more lines (LogBreaks.Lines), the first where the log line is.
    private void RefreshExpanded()
    {
        Label line = _hovered;
        LabelSettings settings = line?.LabelSettings;
        string whole = line == _logLine ? _logText : line?.Text;
        _expandedRich.Visible = false;
        if (settings is null || string.IsNullOrEmpty(whole))
        {
            _expanded.Visible = false;
            return;
        }
        float Width(string s) => settings.Font.GetStringSize(s, HorizontalAlignment.Left, -1, settings.FontSize).X;
        List<string> lines = line == _logLine ? LogBreaks.Lines(whole, settings.Font, settings.FontSize, line.Size.X) : new List<string> { whole };
        float width = 0;
        foreach (string text in lines)
            width = Mathf.Max(width, Width(text));
        if (lines.Count == 1 && width <= line.Size.X)
        {
            _expanded.Visible = false;
            return;
        }
        const float padX = 10, padY = 2;
        float padTop = padY + Mathf.Max(0, (line.Size.Y - settings.Font.GetHeight(settings.FontSize)) / 2); // the first line on the line
        var box = new StyleBoxFlat
        {
            BgColor = TopPanelArt.SectionColour, BorderColor = _frame,
            ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padTop, ContentMarginBottom = padTop,
        };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(8);
        if (line == _logLine)
        {
            // The log record in colour.
            _expanded.Visible = false;
            _expandedBack.AddThemeStyleboxOverride("panel", box);
            _expandedRich.Show(LogSegments, lines, MoreLook);
            float textHeight = lines.Count * _expandedRich.LineHeight;
            _expandedBack.Position = line.Position - new Vector2(padX, padY);
            _expandedBack.Size = new Vector2(width + 2 * padX + 4 + LogOutline, textHeight + 2 * padTop);
            _expandedRich.Position = _expandedBack.Position + new Vector2(padX, padTop); // its first line where the log line's is
            _expandedRich.Size = new Vector2(width + 4 + LogOutline, textHeight);
            _expandedRich.Visible = true;
            return;
        }
        _expanded.AddThemeStyleboxOverride("normal", box);
        _expanded.LabelSettings = settings;
        _expanded.Text = string.Join("\n", lines);
        _expanded.VerticalAlignment = VerticalAlignment.Top;
        _expanded.Position = line.Position - new Vector2(padX, padY);
        _expanded.Size = new Vector2(width + 2 * padX + 4, 0); // as tall as its lines
        _expanded.Visible = true;
    }
}
