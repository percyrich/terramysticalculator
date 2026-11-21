using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The notes window (NotesWindow, from the bookmark map's "Open notes"): Core's History.Notes, oldest first, conversions
// run together along a line (GameLog.NoteLines). Rows: played, where the game is, to be redone (the active line), or
// on another line (dimmed). The buttons are the log's: the bookmark adds or removes one there (History.ToggleBookmark),
// the arrow goes there (History.JumpToPosition: onto another line too, its line becoming the active one). Kept up to
// date from RenderHistory while it is shown. Quit (the map's key) closes the program at once, nothing asked.
public partial class Main
{
    private NotesWindow _notesWindow;
    private readonly List<NoteEntry> _notes = new();
    private readonly List<GameLog.NoteLine> _noteLines = new();

    // After ReadyHistory (the map's keys).
    private void ReadyNotes()
    {
        _historyOverlay.NotesRequested += OpenNotes;
        _historyOverlay.QuitRequested += () => GetTree().Quit();
        _historyOverlay.FullscreenToggled += SetFullscreen;
        _historyOverlay.VisibilityChanged += () => _historyOverlay.Fullscreen = IsFullscreen; // as it is now
    }

    private bool IsFullscreen => GetWindow().Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    // The game's window filling the screen it is on (or back to a window, where it was).
    private void SetFullscreen(bool on)
    {
        if (Engine.IsEmbeddedInEditor())
        {
            ShowNote("Fullscreen needs the game in a window of its own: turn off \"Embed Game on Next Play\" in the editor's Game tab, or run the exported game");
            _historyOverlay.Fullscreen = IsFullscreen;
            return;
        }
        Window window = GetWindow();
        int screen = window.CurrentScreen; // the screen it was moved to
        window.Mode = on ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
        if (on && window.CurrentScreen != screen)
            window.CurrentScreen = screen;
        _historyOverlay.Fullscreen = IsFullscreen;
    }

    private void OpenNotes()
    {
        if (_notesWindow is null || !IsInstanceValid(_notesWindow))
        {
            _notesWindow = new NotesWindow();
            AddChild(_notesWindow);
            _notesWindow.Log.BookmarkToggled += ToggleNoteBookmark;
            _notesWindow.Log.JumpRequested += JumpToNote;
            _notesWindow.VisibilityChanged += () =>
            {
                if (_notesWindow.Visible)
                    RenderNotes();
            };
        }
        _notesWindow.Open();
        if (_notesWindow.Inside)
            ShowNote(Engine.IsEmbeddedInEditor()
                ? "The notes open in a window of their own only when the game is in one: turn off \"Embed Game on Next Play\" in the editor's Game tab, or run the exported game"
                : "The notes couldn't have a window of their own here, so they are kept inside the game's window");
        RenderNotes();
    }

    // From RenderHistory: the rows again, while the window is shown.
    private void RenderNotes()
    {
        if (_notesWindow is null || !IsInstanceValid(_notesWindow) || !_notesWindow.Visible)
            return;
        ValueList<LogEntry> log = _history.Log();
        int at = _history.LogIndex;
        var onActive = new Dictionary<int, int>(); // position → its index in the log
        for (var i = 0; i < log.Count; i++)
            onActive[log[i].Position] = i;
        int current = at >= 0 ? log[at].Position : -1;
        _notes.Clear();
        _notes.AddRange(_history.Notes());
        _noteLines.Clear();
        _noteLines.AddRange(GameLog.NoteLines(_notes, current, LogText));
        var rows = _noteLines.Select(line =>
        {
            NoteEntry note = _notes[line.Index];
            int position = note.Entry.Position;
            TopPanelLog.RowState state = position == current ? TopPanelLog.RowState.Here
                : !onActive.TryGetValue(position, out int index) ? TopPanelLog.RowState.Other
                : index < at ? TopPanelLog.RowState.Played
                : TopPanelLog.RowState.Later;
            bool start = note.Entry.Move is null; // Session start
            return new TopPanelLog.Row(line.Text, start ? null : note.Number, line.Depth, line.Opens, state,
                note.Entry.Bookmark is not null, start);
        }).ToList();
        _notesWindow.Log.Frame = _topPanel.FrameColour;
        _notesWindow.Log.ShowRows(rows);
    }

    private NoteEntry NoteOfRow(int row) =>
        row >= 0 && row < _noteLines.Count && _noteLines[row].Index < _notes.Count ? _notes[_noteLines[row].Index] : null;

    private void ToggleNoteBookmark(int row)
    {
        if (NoteOfRow(row) is not NoteEntry note)
            return;
        if (_history.ToggleBookmark(note.Entry.Position))
            ShowNote(note.Entry.Bookmark is null ? "Bookmarked" : "Bookmark removed");
        RenderHistory();
    }

    // As the log's arrow (no animation: a jump can change the whole board).
    private void JumpToNote(int row)
    {
        if (NoteOfRow(row) is not NoteEntry note)
            return;
        EndBonus();
        StopPointLights();
        CancelPanelSteps();
        if (_history.JumpToPosition(note.Entry.Position))
            ShowNote($"At record {note.Number}");
        Render(animate: false);
    }
}
