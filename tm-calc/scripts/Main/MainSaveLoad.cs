using System;
using System.IO;
using Core;
using Godot;

namespace TMCalc;

// Saving and loading the whole history (Core: SaveGame, a .tmcalc JSON file), and the game line.
//   Save (Ctrl+S): to the file last saved to or loaded, else as Save as. Save as (Ctrl+Shift+S), Load (Ctrl+O): a file
//     dialog. Also the bookmark map's buttons (HistoryOverlay).
//   Autosave: AutosaveSeconds after the history last changed, to user://autosave.tmcalc (the app's data folder, where
//     the dialogs start when nothing has been saved yet). Not while nothing has been played.
//   Loading replays the saved moves; a move the rules now refuse stops that line there, and the note says which.
//   The game line: "Mark as game line" on the bookmark map; the top line starts "Off the game line (N moves) · " while
//     the player is off it; "Back to game line" returns to where it was left.
public partial class Main
{
    private const string AutosavePath = "user://autosave." + SaveGame.Extension;
    private string _savePath;      // where Save writes (the file last saved to or loaded)
    private int _autosaveSequence;
    private FileDialog _fileDialog;

    // After ReadyHistory (the bookmark map).
    private void ReadySaveLoad()
    {
        _historyOverlay.SaveRequested += Save;
        _historyOverlay.SaveAsRequested += SaveAs;
        _historyOverlay.LoadRequested += OpenLoad;
        _historyOverlay.MarkGameLineRequested += () =>
        {
            _history.MarkGameLine();
            ShowNote("This line is now the game line");
            RenderHistory();
            RenderControls(_history.Current);
        };
        _historyOverlay.ReturnToGameLineRequested += () =>
        {
            if (!_history.ReturnToGameLine())
            {
                ShowNote("There is no game line yet: mark one on the bookmark map");
                return;
            }
            _historyOverlay.Close();
            CancelPanelSteps();
            EndBonus();
            Render(animate: false);
        };
    }

    // Ctrl+S, Ctrl+Shift+S, Ctrl+O. True if taken.
    private bool OnSaveLoadInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true } key)
            return false;
        bool map = CustomMapShown; // the custom map editor's own (MainCustomMap.cs)
        Action action = key.Keycode switch
        {
            Key.S when key.ShiftPressed => map ? SaveCustomMapAs : SaveAs,
            Key.S => map ? SaveCustomMap : Save,
            Key.O => map ? OpenCustomMapLoad : OpenLoad,
            _ => null,
        };
        if (action is null)
            return false;
        GetViewport().SetInputAsHandled();
        action();
        return true;
    }

    private string WithGameLine(string line) =>
        _history.GameLineStatus is { OnLine: false } off
            ? $"Off the game line ({off.Moves} move{(off.Moves == 1 ? "" : "s")}) · {line}"
            : line;

    private void Save()
    {
        if (_savePath is null)
            SaveAs();
        else
            WriteTo(_savePath, quiet: false);
    }

    private void SaveAs() => ShowFileDialog(FileDialog.FileModeEnum.SaveFile, path =>
    {
        if (!path.EndsWith("." + SaveGame.Extension, StringComparison.OrdinalIgnoreCase))
            path += "." + SaveGame.Extension;
        if (WriteTo(path, quiet: false))
            _savePath = path;
    });

    private void OpenLoad() => ShowFileDialog(FileDialog.FileModeEnum.OpenFile, Load);

    private bool WriteTo(string path, bool quiet)
    {
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), SaveGame.ToJson(_history));
            if (!quiet)
                ShowNote($"Saved {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!quiet)
                ShowNote($"Couldn't save {Path.GetFileName(path)}: {e.Message}");
            return false;
        }
    }

    private void Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(ProjectSettings.GlobalizePath(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNote($"Couldn't read {Path.GetFileName(path)}: {e.Message}");
            return;
        }
        LoadResult loaded = SaveGame.FromJson(json);
        if (loaded.History is null)
        {
            ShowNote($"Couldn't load {Path.GetFileName(path)}: {loaded.Error}");
            return;
        }
        ReplaceHistory(loaded.History, path == AutosavePath ? null : path);
        ShowNote(loaded.Problem is { } problem
            ? $"Loaded {Path.GetFileName(path)}, but stopped at a move the rules refuse: " +
              $"{GameLog.Describe(problem.Before, problem.Move, Array.Empty<GameEvent>(), problem.Before)} ({problem.Reason})"
            : $"Loaded {Path.GetFileName(path)}");
    }

    // The history on screen becomes this one (Load, Load BGA JSON): the map and log closed, anything half done
    // dropped, drawn at once. savePath: where Save writes from now on (null: Save asks).
    private void ReplaceHistory(History history, string savePath)
    {
        _historyOverlay.Close();
        SetLogOpen(false);
        CancelPanelSteps();
        EndBonus();
        StopPointLights();
        _history = history;
        _logTexts.Clear(); // position numbers belong to the old history
        _savePath = savePath;
        Render(animate: false);
    }

    // filters and folder: the saves' own unless given (Load BGA JSON: *.json, starting in Downloads).
    private void ShowFileDialog(FileDialog.FileModeEnum mode, Action<string> chosen, string[] filters = null, string folder = null)
    {
        _fileDialog?.QueueFree();
        _fileDialog = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = filters ?? new[] { $"*.{SaveGame.Extension} ; Terra Mystica Calculator saves" },
            UseNativeDialog = true,
            CurrentDir = folder ?? (_savePath is not null ? Path.GetDirectoryName(ProjectSettings.GlobalizePath(_savePath)) : OS.GetUserDataDir()),
        };
        _fileDialog.FileSelected += path => chosen(path);
        AddChild(_fileDialog);
        _fileDialog.PopupCentered(new Vector2I(900, 600));
    }

    // A quiet save to user://autosave.tmcalc, AutosaveSeconds after the last change (each change restarts the wait).
    private void ScheduleAutosave()
    {
        if (!_history.CanUndo && !_history.CanRedo)
            return; // nothing played yet: keep the last autosave
        int sequence = ++_autosaveSequence;
        GetTree().CreateTimer(AutosaveSeconds).Timeout += () =>
        {
            if (sequence == _autosaveSequence)
                WriteTo(AutosavePath, quiet: true);
        };
    }
}
