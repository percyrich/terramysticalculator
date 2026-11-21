using System;
using System.Collections.Generic;
using Core;
using Godot;

namespace TMCalc;

// The history (Core's History) through the top panel's keys (MainTopPanel.cs): back and forward one move, bookmark
// this position, the bookmark map (HistoryOverlay), and the whole log. The map sits on the top panel's CanvasLayer
// under the bar, so the keys still work while it is open.
// Shortcuts: Ctrl+Z back, Ctrl+Y (or Ctrl+Shift+Z) forward, B bookmark, M map, L log; Esc closes the map or the log.
public partial class Main
{
    private HistoryOverlay _historyOverlay;

    // After ReadyTopPanel (its layer).
    private void ReadyHistory()
    {
        _historyLayer.AddChild(_historyOverlay = new HistoryOverlay { Name = "HistoryOverlay" });
        _historyLayer.MoveChild(_historyOverlay, 0); // under the log's dimmed screen and the top panel
        _historyOverlay.Under = _topPanel;           // the map's drop-down hangs from the bar
        _historyOverlay.Visible = false;
        _historyOverlay.JumpRequested += JumpToBookmark;
        _historyOverlay.DeleteRequested += DeleteBookmark;
        _historyOverlay.RenameRequested += (bookmark, name) =>
        {
            if (_history.RenameBookmark(bookmark, name) is Bookmark renamed)
                ShowNote($"Renamed: {renamed.Name}");
            RenderHistory();
        };
        _historyOverlay.VisibilityChanged += () => _topPanel.MapOpen = _historyOverlay.Visible;
    }

    // The keys and the log; the map follows every change while it is open (the red dot moves with the arrows).
    private void RenderHistory()
    {
        RenderTopPanel();
        RenderNotes();      // MainNotes.cs, if its window is shown
        ScheduleAutosave(); // MainSaveLoad.cs
        if (!_historyOverlay.IsOpen)
            return;
        ShowGameLineButtons();
        _historyOverlay.ShowMap(_history.Map());
    }

    // Mark when the active line isn't already the game line; Back when there is one and the player has left it.
    private void ShowGameLineButtons()
    {
        _historyOverlay.CanMarkGameLine = !_history.ActiveLineIsGameLine;
        _historyOverlay.CanReturnToGameLine = _history.GameLineStatus is { OnLine: false };
    }

    // ---------- Input ----------

    // The shortcuts, and while the map or the log is open: Esc closes it, and the mouse is left to it (the board
    // ignores it). True if the event is used up here.
    private bool OnHistoryInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key && HistoryShortcut(key) is Action action)
        {
            GetViewport().SetInputAsHandled();
            action();
            return true;
        }
        if (_topPanel.LogOpen)
        {
            if (@event.IsActionPressed("ui_cancel"))
            {
                GetViewport().SetInputAsHandled();
                SetLogOpen(false);
            }
            return true;
        }
        if (!_historyOverlay.IsOpen)
            return false;
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            _historyOverlay.Close();
        }
        return true;
    }

    private Action HistoryShortcut(InputEventKey key)
    {
        if (_dragged is not null || _draggedWorker is not null || _draggedCoin is not null)
            return null; // not while something is held
        bool command = key.CtrlPressed || key.MetaPressed;
        return key.Keycode switch
        {
            Key.Z when command && key.ShiftPressed => StepForward,
            Key.Z when command => StepBack,
            Key.Y when command => StepForward,
            Key.B when !command && !key.Echo => BookmarkHere,
            Key.M when !command && !key.Echo => ToggleHistoryMap,
            Key.L when !command && !key.Echo => ToggleLog,
            _ => null,
        };
    }

    // ---------- What the keys do ----------

    private void StepBack()
    {
        if (UndoCustomMap()) // the custom map editor's own strokes first (MainCustomMap.cs)
            return;
        if (!_history.CanUndo)
            return;
        EndBonus();
        StopPointLights(); // points still on their way go at once (MainPoints.cs)
        CancelPanelSteps();
        GameState was = _history.Current;
        _history.Undo();
        ReopenCustomMap(was); // back before a custom map was chosen: its editor again
        ShowNote("");
        Render(animate: true);
    }

    private void StepForward()
    {
        if (RedoCustomMap())
            return;
        if (!_history.CanRedo)
            return;
        EndBonus();
        CancelPanelSteps();
        // Shown as if the move were made here (Submit): its points and rewards fly, the Geologists' rock too; lights
        // still on their way carry on.
        GameState before = _history.Current;
        _history.Redo();
        var redone = new MoveResult(_history.Current, _history.CurrentEvents, MoveResult.NoIssues);
        ShowNote("");
        List<Gain> gains = TakeGainsInFlight(redone, before); // the counts wait for their lights (MainPoints.cs)
        Render(animate: true);
        QueueGains(gains);
        FlyRocks(redone);
    }

    private void BookmarkHere()
    {
        if (_history.AddBookmark() is not Bookmark added)
            return;
        ShowNote($"Bookmarked: {added.Name}");
        RenderHistory();
    }

    private void ToggleHistoryMap()
    {
        if (_historyOverlay.IsOpen)
        {
            _historyOverlay.Close();
            return;
        }
        if (_topPanel.LogOpen)
            SetLogOpen(false);
        // Whatever was half done on the board is put down first.
        EndBonus();
        CloseWheel();
        ShowGameLineButtons();
        _historyOverlay.Open(_history.Map());
    }

    // Straight there (no animation: a jump can change the whole board). Leaving the active line bookmarks its end
    // first (Core).
    private void JumpToBookmark(Bookmark bookmark)
    {
        _historyOverlay.Close();
        EndBonus();
        StopPointLights();
        CancelPanelSteps();
        if (_history.JumpTo(bookmark))
            ShowNote($"At {bookmark.Name}");
        Render(animate: false);
    }

    private void DeleteBookmark(Bookmark bookmark)
    {
        _history.DeleteBookmark(bookmark);
        RenderHistory();
    }
}
