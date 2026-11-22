using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the top panel (TopPanel), across the top of the map. Also every tooltip's look (StyleTooltips).
//   Game button: the step button (MainStepButton.cs).
//   Top line: what the player has to do next (RenderControls' instruction). A note (a refusal, a rule's warning, a
//     bookmark made) takes its place in italics for NoteSeconds, then it comes back. A prompt
//     (instructions for a mode that is open: a bonus action's target, a town or bridge to place, the shapeshift, the
//     Alchemists' coin) shows in italics for as long as the mode is open (ShowPrompt; cleared with the mode).
//   Second line: the game log's line for the current position (GameLog.Lines), in colour (LogSegments: each part in
//     its faction's colour; the full log and the notes too).
//   Log key: the whole log under the bar, newest first (TopPanelLog), modal like the bookmark map: the board behind it
//     dimmed and ignoring the mouse; Esc, the log key, L or a click on the dimmed board closes it. Each record's
//     bookmark button adds or removes a bookmark there (History.ToggleBookmark); its arrow goes there
//     (History.JumpToPosition: the position left is bookmarked first if it isn't already; undo and redo work on from
//     there), drawn at once.
//   Keys: undo, redo, bookmark, bookmark map (MainHistory.cs).
//   Rules tab (past the bar's right end, beyond the map): Strict, Lenient, Free. On the game screen the knob only
//     chooses the mode to start in (GameOptions.StartingMode, sent with ChooseGame; _startingMode); after that,
//     letting the knob go on another notch submits SetMode, and the knob follows the state's Mode on every render
//     (so a refused change glides back).
// Placed by %TopPanelPlace if the scene has it (any Control, e.g. a ReferenceRect, which only shows in the editor): the
// bar fills its rectangle, its height setting the scale (TopPanelArt.Height 135.6 panel pixels: 81.4 high = the usual
// 0.6). Otherwise over the map: as wide as the map's hexes, TopPanelTop down from the top of the screen.
public partial class Main
{
    private CanvasLayer _historyLayer; // the top panel, the log's dimmed screen and the bookmark map (MainHistory.cs)
    private TopPanel _topPanel;
    private ColorRect _logDim;
    private string _instruction = "", _prompt, _note;
    private int _noteNumber;           // which note a timer belongs to (a newer note keeps its own time)
    private readonly Dictionary<int, IReadOnlyList<GameLog.LogPart>> _logTexts = new(); // GameLog parts by position (positions never change)

    private void ReadyTopPanel()
    {
        StyleTooltips();
        _historyLayer = new CanvasLayer { Name = "HistoryLayer", Layer = HistoryLayer };
        AddChild(_historyLayer);
        _logDim = new ColorRect { Name = "LogDim", Color = LogDimColour, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _logDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _logDim.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: true })
                SetLogOpen(false);
        };
        _historyLayer.AddChild(_logDim);
        _topPanel = new TopPanel { Name = "TopPanel" };
        _historyLayer.AddChild(_topPanel);
        _topPanel.GamePressed += OnStep;
        _topPanel.UndoPressed += StepBack;
        _topPanel.RedoPressed += StepForward;
        _topPanel.BookmarkPressed += BookmarkHere;
        _topPanel.MapPressed += ToggleHistoryMap;
        _topPanel.ModeChosen += OnModeChosen;
        _topPanel.LogToggled += open => SetLogOpen(open);
        _topPanel.Log.BookmarkToggled += ToggleLogBookmark;
        _topPanel.Log.JumpRequested += JumpToLogEntry;
        PlaceTopPanel();
    }

    // In %TopPanelPlace's rectangle; or as wide as the map's hexes, over them (centred on the screen before the map is
    // made).
    private void PlaceTopPanel()
    {
        if (_topPanel is null)
            return;
        if (GetNodeOrNull<Control>("%TopPanelPlace") is Control place)
        {
            place.MouseFilter = Control.MouseFilterEnum.Ignore;
            place.Visible = false; // a ReferenceRect only shows in the editor anyway; anything else is hidden here
            Rect2 box = place.GetGlobalRect();
            SetTopPanel(box.Position, box.Size.X, box.Size.Y / TopPanelArt.Height);
            return;
        }
        Rect2 map = _map?.ShownBounds() ?? default;
        if (map.Size.X > 1)
            SetTopPanel(new Vector2(map.Position.X, TopPanelTop), map.Size.X, _topPanel.PanelScale);
        else
            SetTopPanel(new Vector2((GetViewportRect().Size.X - _topPanel.ShownWidth) / 2, TopPanelTop), _topPanel.ShownWidth, _topPanel.PanelScale);
    }

    // Only what changed (each change lays the bar out again).
    private void SetTopPanel(Vector2 position, float width, float scale)
    {
        if (scale > 0 && !Mathf.IsEqualApprox(_topPanel.PanelScale, scale))
            _topPanel.PanelScale = scale;
        if (width > 1 && !Mathf.IsEqualApprox(_topPanel.ShownWidth, width))
            _topPanel.ShownWidth = width;
        _topPanel.Position = position;
    }

    // Whether the pointer is over the top panel (its log and expanded lines included), or on the background picker or
    // its open popup (MainBackground.cs): the board leaves such clicks to them.
    private bool IsOverTopPanel()
    {
        Control hovered = GetViewport().GuiGetHoveredControl();
        if (IsOverBackgroundPicker(hovered))
            return true;
        return hovered is not null && (hovered == _topPanel || _topPanel.IsAncestorOf(hovered));
    }

    // ---------- The top line ----------

    // The step's own message (RenderControls).
    private void SetInstruction(string text)
    {
        _instruction = text ?? "";
        ShowTopLine();
    }

    // Instructions for a mode that is open, until the mode ends (`ShowNote("")`, or another prompt).
    private void ShowPrompt(string text)
    {
        _prompt = string.IsNullOrEmpty(text) ? null : text;
        _note = null;
        ShowTopLine();
    }

    // A note for NoteSeconds; "" clears the note and any prompt.
    private void ShowNote(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _note = _prompt = null;
            ShowTopLine();
            return;
        }
        _note = text.Replace("\n", " · ");
        int number = ++_noteNumber;
        GetTree().CreateTimer(NoteSeconds).Timeout += () =>
        {
            if (number != _noteNumber)
                return;
            _note = null;
            ShowTopLine();
        };
        ShowTopLine();
    }

    private void ShowTopLine()
    {
        if (_topPanel is null)
            return;
        string shown = _note ?? _prompt;
        _topPanel.Message = shown ?? _instruction;
        _topPanel.MessageItalic = shown is not null;
    }

    // ---------- The log ----------

    private IReadOnlyList<GameLog.LogPart> LogText(LogEntry entry)
    {
        if (!_logTexts.TryGetValue(entry.Position, out IReadOnlyList<GameLog.LogPart> parts))
            _logTexts[entry.Position] = parts = GameLog.Parts(entry);
        return parts;
    }

    // A log line in colour: each part in the colour of the faction it is about, with a stroke round its letters (black
    // round light colours and brown, white round dark ones); what is nobody's, and the " · " between parts, in the power
    // purple.
    private IReadOnlyList<LogSegment> LogSegments(IReadOnlyList<GameLog.LogPart> parts)
    {
        GameState state = _history.Current;
        LogSegment Look(string text, Faction? faction)
        {
            if (faction is not Faction f)
                return new LogSegment(text, IconArt.PowerPurple, LogStroke(IconArt.PowerPurple, null));
            PlayerColour colour = state.IsInGame(f) ? state[f].Colour : Factions.ColourOf(f);
            Color fill = Art.PieceColour(colour);
            return new LogSegment(text, fill, LogStroke(fill, colour));
        }
        var segments = new List<LogSegment>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
                segments.Add(Look(GameLog.Separator, null));
            segments.Add(Look(parts[i].Text, parts[i].Faction));
        }
        return segments;
    }

    private static Color LogStroke(Color fill, PlayerColour? colour) =>
        colour == PlayerColour.Brown || fill.Luminance > 0.5f ? Colors.Black : Colors.White;

    // The keys, the latest record, and the whole log while it is open (from RenderHistory).
    private void RenderTopPanel()
    {
        if (_topPanel is null)
            return;
        PlaceTopPanel();
        _topPanel.CanUndo = _history.CanUndo || CustomMapShown && _customMap.CanUndo;
        _topPanel.CanRedo = _history.CanRedo || CustomMapShown && _customMap.CanRedo;
        _topPanel.Bookmarked = !_history.CanBookmark;
        _topPanel.MapOpen = _historyOverlay?.IsOpen ?? false;
        if (!_topPanel.Rules.Dragging)
            _topPanel.Mode = _history.Current.Phase == Phase.ChoosingGame ? _startingMode : _history.Current.Mode;
        // Calculator: always Free, no slider (on the game screen: while its box is ticked).
        _topPanel.Rules.Visible = !(_history.Current.Phase == Phase.ChoosingGame
            ? _calculator?.ButtonPressed ?? false
            : _history.Current.Options.Calculator);
        ValueList<LogEntry> log = _history.Log();
        int at = _history.LogIndex;
        // One row per line: a run of conversions shares one (GameLog.Lines), so rows map to records through _logRows.
        List<GameLog.LogLine> lines = GameLog.Lines(log, at + 1, LogText);
        _logRows.Clear();
        _logRows.AddRange(lines.Select(line => line.Index));
        _topPanel.ShowLogEntry(at >= 0 ? LogSegments(lines.FirstOrDefault(line => line.Index == at)?.Parts ?? LogText(log[at])) : null);
        if (_topPanel.LogOpen)
        {
            int played = lines.Count(line => line.Index <= at);
            _topPanel.Log.ShowRows(lines.Select((line, row) => new TopPanelLog.Row(line.Text, row == 0 ? null : row, 0, false,
                row == played - 1 ? TopPanelLog.RowState.Here : row >= played ? TopPanelLog.RowState.Later : TopPanelLog.RowState.Played,
                log[line.Index].Bookmark is not null, row == 0, LogSegments(line.Parts))).ToList());
        }
    }

    private readonly List<int> _logRows = new(); // each log row's last record (its index in History.Log)

    private int RecordOfRow(int row) => row >= 0 && row < _logRows.Count ? _logRows[row] : row;

    // The mode to start in, chosen on the slider while the game is being chosen (Core refuses SetMode before ChooseGame).
    private PlayMode _startingMode = PlayMode.Lenient;

    private void OnModeChosen(int mode)
    {
        if (_history.Current.Phase == Phase.ChoosingGame)
        {
            _startingMode = (PlayMode)mode;
            RenderTopPanel();
        }
        else
            Submit(new SetMode((PlayMode)mode));
    }

    private void SetLogOpen(bool open)
    {
        if (open)
        {
            if (_historyOverlay?.IsOpen ?? false)
                _historyOverlay.Close();
            // Whatever was half done on the board is put down first (as for the map).
            EndBonus();
            CloseWheel();
        }
        _topPanel.LogOpen = open;
        _logDim.Visible = open;
        RenderTopPanel();
    }

    private void ToggleLog() => SetLogOpen(!_topPanel.LogOpen);

    private void ToggleLogBookmark(int row)
    {
        int index = RecordOfRow(row);
        ValueList<LogEntry> log = _history.Log();
        if (index < 0 || index >= log.Count)
            return;
        if (_history.ToggleBookmark(log[index].Position))
            ShowNote(_history.Log()[index].Bookmark is Bookmark added ? $"Bookmarked: {added.Name}" : "Bookmark removed");
        RenderHistory();
    }

    // Straight there (no animation: a jump can change the whole board); the log stays open.
    private void JumpToLogEntry(int row)
    {
        int index = RecordOfRow(row);
        ValueList<LogEntry> log = _history.Log();
        if (index < 0 || index >= log.Count)
            return;
        EndBonus();
        StopPointLights();
        CancelPanelSteps();
        if (_history.JumpToPosition(log[index].Position))
            ShowNote($"At record {index}");
        Render(animate: false);
    }

    // Every tooltip (the default theme's TooltipPanel, used by every control): TooltipColour, nearly opaque, so the text
    // reads over whatever is under it, with a thin TooltipEdgeColour edge.
    private void StyleTooltips()
    {
        var panel = new StyleBoxFlat
        {
            BgColor = TooltipColour, BorderColor = TooltipEdgeColour,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(4);
        ThemeDB.GetDefaultTheme().SetStylebox("panel", "TooltipPanel", panel);
    }
}
