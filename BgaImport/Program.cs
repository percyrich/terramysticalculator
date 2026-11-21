using BgaImport;
using Core;

// Usage: BgaImport <log.json> [<gamedatas.json>] [<output folder>]
//        BgaImport <folder> [""] [<output folder>]   every .json file in the folder, with summary.txt of them all
//   log.json       BGA's move log: the "logs" of logs.html (or the whole response, or one file holding logs and gamedatas)
//   gamedatas.json the replay page's gameui.gamedatas (leave out, or pass "", if log.json holds it)
//   output folder  where bga-<table>.tmcalc, -report.txt and -trace.txt go (default: beside log.json)
// Options meant for dotnet (e.g. --nologo) that reach the program are ignored.
args = args.Where(a => !a.StartsWith("--")).ToArray();
// A folder passed as "folder\" makes Windows read \" as a quote, so the arguments arrive run together
// ('folder" " BgaImport\out'): split them again at the quotes.
args = args.SelectMany(a => a.Contains('"') ? a.Split('"').Select(p => p.Trim()) : new[] { a }).ToArray();
if (args.Length < 1)
{
    Console.WriteLine("Usage: BgaImport <log.json | folder> [<gamedatas.json>] [<output folder>]");
    return 1;
}
var input = args[0];
var dataPath = args.Length > 1 && args[1].Length > 0 ? args[1] : null;
var outDir = args.Length > 2 ? args[2]
    : Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
Directory.CreateDirectory(outDir);

if (!Directory.Exists(input))
    return ConvertGame(input, dataPath, verbose: true).Code;

// A folder: every game in it, one line each, also written to summary.txt.
var files = Directory.GetFiles(input, "*.json").OrderBy(f => f).ToList();
var lines = new List<string>();
var complete = 0;
foreach (var file in files)
{
    var (code, line) = ConvertGame(file, null, verbose: false);
    complete += code == 0 ? 1 : 0;
    lines.Add(line);
    Console.WriteLine(line);
}
lines.Add($"{complete} of {files.Count} games converted to the end.");
Console.WriteLine(lines[^1]);
File.WriteAllLines(Path.Combine(outDir, "summary.txt"), lines);
return complete == files.Count ? 0 : 3;

(int Code, string Line) ConvertGame(string logPath, string? data, bool verbose)
{
    var name = Path.GetFileName(logPath);
    BgaGame game;
    try
    {
        game = BgaGame.Load(logPath, data);
    }
    catch (Exception e)
    {
        var text = $"Could not read the BGA files: {e.Message}";
        if (verbose)
            Console.WriteLine(text);
        return (2, $"FAIL {name}: {text}");
    }

    ConversionResult result;
    try
    {
        result = new Converter(game).Run();
    }
    catch (Exception e)
    {
        if (verbose)
            Console.WriteLine($"The converter crashed: {e}");
        return (4, $"FAIL {name}: the converter crashed: {e.Message}");
    }
    var stem = $"bga-{game.TableId}";
    File.WriteAllText(Path.Combine(outDir, stem + ".tmcalc"), SaveGame.ToJson(result.History));
    File.WriteAllText(Path.Combine(outDir, stem + "-report.txt"), result.Report);
    File.WriteAllText(Path.Combine(outDir, stem + "-trace.txt"), result.Trace);

    // The save must load: replay it as the app would.
    var check = SaveGame.FromJson(File.ReadAllText(Path.Combine(outDir, stem + ".tmcalc")));
    var loads = check.Error is not null ? $"The save does NOT load: {check.Error}"
        : check.Problem is not null ? $"The save loads but stops at a refused move: {check.Problem.Reason}"
        : "The save loads back cleanly.";
    if (verbose)
    {
        Console.Write(result.Summary);
        Console.WriteLine($"Written to {Path.GetFullPath(outDir)}: {stem}.tmcalc, {stem}-report.txt, {stem}-trace.txt");
        Console.WriteLine(loads);
    }
    var summary = string.Join(" | ", result.Summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    var loadNote = check.Error is null && check.Problem is null ? "" : " | " + loads;
    return (result.Completed ? 0 : 3, $"{(result.Completed ? "OK  " : "STOP")} {summary}{loadNote}");
}
