using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BgaImport;
using Core;
using Godot;

namespace TMCalc;

// Games from Board Game Arena, through the bookmark map's two keys:
//   BGA Bookmarklet: a message box (TopPanelModal) saying how to make the TM Export bookmark, with the bookmarklet's
//     text to select and a "Copy to Clipboard" key. Closing it leaves the bookmark map open underneath.
//   Load BGA JSON: a file dialog for the .json the bookmarklet downloads (starting in Downloads, then wherever the last
//     one was). BgaConvert turns it into a history (on a worker thread, so the screen keeps drawing), which is saved
//     beside the .json as bga-<table>.tmcalc (overwriting one of that name; it becomes Save's file) and replaces the
//     history on screen, as Load does, at the game's last move: the end of the game (final scoring) or, for a game in
//     progress, its latest move.
//     A file that isn't a TM Export file: "Converter failed: incorrect JSON". A conversion that stopped part-way: the
//     game up to there is loaded (and saved), and a message box says where and why, with a key copying the converter's
//     report. A game converted to its end whose final scores differ from BGA's, or played with another version of a
//     faction: a message box saying so. Otherwise a note on the top line.
// The message boxes are on their own CanvasLayer (ModalLayer) above the top panel and the map. While one is open,
// _Input leaves everything to it: Esc closes it; keys go to its text box (Ctrl+A, Ctrl+C).
public partial class Main
{
    private TopPanelModal _modal;
    private string _bgaFolder;      // where the last BGA file was chosen
    private bool _bgaConverting;
    private int _copiedSequence;

    // After ReadySaveLoad (the bookmark map, the file dialog).
    private void ReadyBgaImport()
    {
        var layer = new CanvasLayer { Name = "ModalLayer", Layer = ModalLayer };
        AddChild(layer);
        layer.AddChild(_modal = new TopPanelModal { Name = "Modal" });
        _historyOverlay.BookmarkletRequested += ShowBookmarklet;
        _historyOverlay.LoadBgaRequested += OpenBgaLoad;
    }

    // While a message box is open: Esc closes it, and nothing else here sees the event (its keys and text box do).
    private bool OnModalInput(InputEvent @event)
    {
        if (_modal is null || !_modal.IsOpen)
            return false;
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            _modal.Close();
        }
        return true;
    }

    private void ShowModal(string title, string body, string code = null, string keyText = null, Action keyPressed = null) =>
        _modal.Open(title, body, _topPanel.FrameColour, _topPanel.PanelScale, code, keyText, keyPressed);

    // ---------- The bookmarklet ----------

    private void ShowBookmarklet()
    {
        string text = Bookmarklet.Text;
        ShowModal("BGA Bookmarklet",
            "Use this bookmarklet to download a file that you can load in here. You must either be viewing a game in " +
            "progress, or a game replay, for it to work.\n\n" +
            "To make a bookmarklet, create a new bookmark on any page in your browser, rename it to \"TM Export\", and " +
            "paste the contents of the bookmarklet into the URL field.",
            text, "Copy to Clipboard", () => CopyFromModal(text, "Copy to Clipboard"));
    }

    // Copies the text; the key says "Copied" for a moment.
    private void CopyFromModal(string text, string keyText)
    {
        DisplayServer.ClipboardSet(text);
        _modal.SetKeyText("Copied");
        int sequence = ++_copiedSequence;
        GetTree().CreateTimer(1.5).Timeout += () =>
        {
            if (sequence == _copiedSequence)
                _modal.SetKeyText(keyText);
        };
    }

    // ---------- Load BGA JSON ----------

    private void OpenBgaLoad()
    {
        if (_bgaConverting)
            return;
        ShowFileDialog(FileDialog.FileModeEnum.OpenFile, path => LoadBga(path),
            new[] { "*.json ; Board Game Arena exports (TM Export)" },
            _bgaFolder ?? OS.GetSystemDir(OS.SystemDir.Downloads));
    }

    private async void LoadBga(string path)
    {
        string full = ProjectSettings.GlobalizePath(path), name = Path.GetFileName(full);
        _bgaFolder = Path.GetDirectoryName(full);
        string json;
        try
        {
            json = File.ReadAllText(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowModal("Converter failed", $"Couldn't read {name}: {e.Message}");
            return;
        }

        _bgaConverting = true;
        ShowNote($"Converting {name}…");
        (ConversionResult Result, string Error) outcome;
        try
        {
            outcome = await Task.Run(() => BgaConversion.Convert(json));
        }
        catch (Exception e)
        {
            outcome = (null, e.Message);
        }
        finally
        {
            _bgaConverting = false;
        }
        if (outcome.Result is not ConversionResult result)
        {
            ShowNote("");
            ShowModal("Converter failed",
                "Converter failed: incorrect JSON.\n\n" +
                $"{name} isn't a Terra Mystica game exported with the TM Export bookmarklet (see BGA Bookmarklet on the " +
                "bookmark map).");
            return;
        }

        ReplaceHistory(result.History, null);
        string savePath = Path.Combine(_bgaFolder ?? OS.GetUserDataDir(), $"bga-{SafeFileName(result.TableId)}.{SaveGame.Extension}");
        string saved;
        if (!_history.CanUndo)
            saved = "Nothing could be converted, so nothing was saved.";
        else if (WriteTo(savePath, quiet: true))
        {
            _savePath = savePath;
            saved = $"Saved as {Path.GetFileName(savePath)}, beside {name}.";
        }
        else
            saved = $"It couldn't be saved beside {name}: use Save as to keep it.";
        ReportBga(result, saved);
    }

    private void ReportBga(ConversionResult result, string saved)
    {
        string table = $"BGA table {result.TableId}";
        var diffs = result.ScoreDifferences ?? Array.Empty<ScoreDifference>();
        var versions = result.VersionDifferences ?? Array.Empty<string>();
        Action copyReport = () => CopyFromModal(result.Report, "Copy report");

        if (result.Stop is ConversionStop stop)
        {
            ShowNote($"{table}: converted part of the way");
            var body = new StringBuilder();
            if (stop.BgaMove is null)
                body.Append("The game couldn't be set up from this file.\n\n");
            else
                body.Append($"The game was converted up to BGA's move {stop.BgaMove}, where the calculator couldn't follow it. " +
                    "You're at the last position it reached; the moves after it weren't converted.\n\n");
            if (stop.BgaLine is not null)
                body.Append($"BGA's move {stop.BgaMove}: {stop.BgaLine}\n");
            body.Append(stop.RefusedMove is not null
                ? $"The calculator's rules refuse: {stop.RefusedMove}. {stop.Reason}\n\n"
                : $"Why: {stop.Reason}\n\n");
            body.Append(saved);
            body.Append("\nTo report it, copy the converter's report and send it with the BGA file.");
            ShowModal("Conversion stopped", body.ToString(), keyText: "Copy report", keyPressed: copyReport);
            return;
        }

        string where = result.InProgress
            ? $"game in progress, {(_history.Current.ActivePlayer is Faction active ? Factions.Name(active) : "nobody")} to act"
            : "the whole game, to its final scoring";
        if (diffs.Count == 0 && versions.Count == 0)
        {
            ShowNote($"Loaded {table}: {where} · {saved}");
            return;
        }
        ShowNote($"Loaded {table}: {where}");
        var text = new StringBuilder($"{table} was converted ({where}), but it doesn't match BGA exactly.\n");
        if (diffs.Count > 0)
        {
            text.Append("\nFinal scores that differ from BGA's:\n");
            foreach (ScoreDifference d in diffs)
                text.Append($"•  {Factions.Name(d.Faction)} ({d.Player}): {d.Core} VP here, {d.Bga} on BGA\n");
        }
        if (versions.Count > 0)
        {
            text.Append("\nBGA's game used another version of some factions than the calculator does:\n");
            foreach (string v in versions)
                text.Append($"•  {v}\n");
        }
        text.Append($"\n{saved}");
        ShowModal("Converted, with differences", text.ToString(), keyText: "Copy report", keyPressed: copyReport);
    }

    private static string SafeFileName(string name)
    {
        string safe = new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return safe.Length > 0 ? safe : "game";
    }
}
