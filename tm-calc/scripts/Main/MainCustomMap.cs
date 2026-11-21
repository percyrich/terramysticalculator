using System;
using System.IO;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the custom map editor (CustomMapEditor) on the map screen. A "Custom map" key under the six
// miniatures (CustomMapButtonOffset from the grid's middle) opens it in place of them; its Back shows them again (the
// map painted is kept for next time); its Proceed chooses the map (ChooseCustomMap) and goes on to the round tiles.
// While it is open, undo and redo (Ctrl+Z / Ctrl+Y, the top panel's keys) take back its own strokes first; with none
// left, they go on to the game's history. Undoing the custom map's choice reopens the editor with that map. The top
// line says what is still needed. Its Save, Save as and Load (and Ctrl+S, Ctrl+Shift+S, Ctrl+O while it is open)
// write and read the map alone as a .tmmap file (Core.MapFile), as the bookmark map's do the whole history: Save to the
// map file last saved or loaded, else as Save as. A file of the same class (partial), only to keep Main.cs a
// manageable size.
public partial class Main
{
    private CustomMapEditor _customMap;
    private TopPanelTextKey _customMapButton;
    private bool _customMapOpen;
    private string _mapSavePath; // where the editor's Save writes (the map file last saved to or loaded)

    private bool CustomMapShown => _customMapOpen && _history.Current.Phase == Phase.ChoosingMap;

    private void ReadyCustomMap()
    {
        _customMapButton = new TopPanelTextKey
        {
            Name = "CustomMapButton", Text = "Custom map", FontSize = CustomMapButtonFontSize, Size = CustomMapButtonSize,
            Position = _mapChoices.Position + CustomMapButtonOffset - CustomMapButtonSize / 2,
            TooltipText = "Make a map of your own: paint its terrain hex by hex",
        };
        _mapSelect.AddChild(_customMapButton);
        _customMapButton.Pressed += () =>
        {
            _customMapOpen = true;
            _customMap.Open();
            Render(animate: false);
        };

        _customMap = GetNodeOrNull<CustomMapEditor>("%CustomMap");
        if (_customMap is null)
        {
            _customMap = new CustomMapEditor { Name = "CustomMap" };
            _mapSelect.AddChild(_customMap);
        }
        _customMap.Visible = false;
        _customMap.BackPressed += () =>
        {
            _customMapOpen = false;
            Render(animate: false);
        };
        _customMap.ProceedPressed += rows => Submit(new ChooseCustomMap(rows));
        _customMap.SaveRequested += SaveCustomMap;
        _customMap.SaveAsRequested += SaveCustomMapAs;
        _customMap.LoadRequested += OpenCustomMapLoad;
        _customMap.Changed += () =>
        {
            if (!CustomMapShown)
                return;
            RenderControls(_history.Current);
            RenderTopPanel();
        };
    }

    // The editor or the miniatures (from Render). Leaving the map screen closes the editor.
    private void RenderCustomMap(GameState state)
    {
        if (_customMap is null)
            return;
        if (state.Phase != Phase.ChoosingMap)
            _customMapOpen = false;
        _customMap.Visible = _customMapOpen;
        _mapChoices.Visible = !_customMapOpen;
        _customMapButton.Visible = !_customMapOpen;
    }

    // The top line while the editor is open.
    private string CustomMapInstruction()
    {
        var missing = _customMap.Shortfall;
        string still = missing.Count == 0 ? "ready: click Proceed"
            : "still needed: " + string.Join(", ", missing.Select(part => $"{part.Missing} {part.Terrain}"));
        return $"Custom map: choose a terrain on the wheel, then click or drag over hexes · {still}";
    }

    // ---------- Saving and loading the map ----------

    private void SaveCustomMap()
    {
        if (_mapSavePath is null)
            SaveCustomMapAs();
        else
            WriteCustomMap(_mapSavePath);
    }

    private void SaveCustomMapAs() => ShowFileDialog(FileDialog.FileModeEnum.SaveFile, path =>
    {
        if (!path.EndsWith("." + MapFile.Extension, StringComparison.OrdinalIgnoreCase))
            path += "." + MapFile.Extension;
        if (WriteCustomMap(path))
            _mapSavePath = path;
    }, MapFilters, MapFolder);

    private void OpenCustomMapLoad() => ShowFileDialog(FileDialog.FileModeEnum.OpenFile, LoadCustomMap, MapFilters, MapFolder);

    private static readonly string[] MapFilters = { $"*.{MapFile.Extension} ; Terra Mystica Calculator maps" };

    // Where the map dialogs open: the last map file's folder, else the last save's, else the app's data folder.
    private string MapFolder => Path.GetDirectoryName(ProjectSettings.GlobalizePath(_mapSavePath ?? _savePath ?? "user://x"));

    private bool WriteCustomMap(string path)
    {
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(path), MapFile.ToJson(_customMap.Layout, _customMap.Hexes));
            ShowNote($"Saved {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNote($"Couldn't save {Path.GetFileName(path)}: {e.Message}");
            return false;
        }
    }

    private void LoadCustomMap(string path)
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
        MapLoadResult loaded = MapFile.FromJson(json);
        if (loaded.Map is not MapDefinition map)
        {
            ShowNote($"Couldn't load {Path.GetFileName(path)}: {loaded.Error}");
            return;
        }
        _mapSavePath = path;
        _customMap.Replace(loaded.TwelveTop, map.HexTerrain);
        ShowNote($"Loaded {Path.GetFileName(path)}");
    }

    // Undo within the editor (true: done there).
    private bool UndoCustomMap() => CustomMapShown && _customMap.Undo();
    private bool RedoCustomMap() => CustomMapShown && _customMap.Redo();

    // After an undo of the game's history: back before a custom map was chosen, the editor opens again with it.
    private void ReopenCustomMap(GameState was)
    {
        GameState now = _history.Current;
        if (was.ChosenMap != MapId.Custom || now.Phase != Phase.ChoosingMap || now.ChosenMap is not null || _customMap is null)
            return;
        _customMap.Load(was.Layout.RowLengths[0] == 12, was.HexTerrain);
        _customMapOpen = true;
    }
}
