using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BgaImport;

// One BGA notification: an entry of a packet's "data" list. Index is its place in BgaGame.Notes; Packet is its packet's
// place in the log; MoveId is BGA's move number (empty for packets without one).
public sealed record Note(int Index, int Packet, string MoveId, string Type, string Log, JsonObject Args)
{
    public string? Str(string key) => J.Str(Args[key]);
    public int? Int(string key) => J.Int(Args[key]);
    public int Amount(string key) => J.Amount(Args[key]);

    // The hex name BGA shows in brackets after a log line ("[H5]"), without the brackets; "" if none.
    public string Shown => (Str("extra") ?? "").Trim().TrimStart('[').TrimEnd(']').Trim();

    public override string ToString() => $"#{MoveId} {Type}: {J.Plain(Log, Args)}";
}

// What BGA's replay page gives for one table: the move log (logs.html → data.logs) and the game data at the start of the
// game (gameui.gamedatas on the replay page).
public sealed class BgaGame
{
    public required string TableId { get; init; }
    public required IReadOnlyList<Note> Notes { get; init; }
    public required JsonObject GameData { get; init; }

    // logPath: the move log as copied from the Network tab (an array of packets), the whole logs.html response
    // ({ status, data: { logs } }), or one file holding both ({ logs, gamedatas }, as a bookmarklet might save it).
    // dataPath: the game data (gameui.gamedatas, or an object with "gamedatas"); null if logPath holds it.
    public static BgaGame Load(string logPath, string? dataPath) =>
        Parse(File.ReadAllText(logPath), string.IsNullOrWhiteSpace(dataPath) ? null : File.ReadAllText(dataPath), logPath, dataPath);

    // The same from the files' text (the app reads the bookmarklet's file itself). Throws JsonException for text that
    // isn't JSON and InvalidDataException for JSON that isn't a Terra Mystica export.
    public static BgaGame Parse(string logJson, string? dataJson = null, string logName = "The file", string? dataName = null)
    {
        var logPath = logName;
        var dataPath = dataName ?? "The game data file";
        var root = JsonNode.Parse(logJson) ?? throw new InvalidDataException($"{logPath} is empty.");
        JsonArray? logs = null;
        JsonObject? data = null;
        string? table = null;
        switch (root)
        {
            case JsonArray array:
                logs = array;
                break;
            case JsonObject obj:
                logs = (obj["logs"] ?? obj["data"]?["logs"]) as JsonArray;
                data = obj["gamedatas"] as JsonObject;
                table = J.Str(obj["table"]) ?? J.Str(obj["table_id"]);
                break;
        }
        if (logs is null)
            throw new InvalidDataException($"{logPath} holds no move log (expected an array of packets, or an object with \"logs\" or \"data.logs\").");
        if (dataJson is not null)
        {
            var node = JsonNode.Parse(dataJson) as JsonObject
                ?? throw new InvalidDataException($"{dataPath} is not a JSON object.");
            data = node["gamedatas"] as JsonObject ?? node;
        }
        if (data is null)
            throw new InvalidDataException("No game data: give the gamedatas file as the second argument.");
        if (data["board_terrain_spaces"] is null || data["scoring_tiles"] is null)
            throw new InvalidDataException("The game data has no board_terrain_spaces or scoring_tiles: is it Terra Mystica's gameui.gamedatas?");

        var notes = new List<Note>();
        for (var p = 0; p < logs.Count; p++)
        {
            if (logs[p] is not JsonObject packet || packet["data"] is not JsonArray items)
                continue;
            table ??= J.Str(packet["table_id"]);
            var moveId = J.Str(packet["move_id"]) ?? "";
            foreach (var item in items)
            {
                if (item is not JsonObject n)
                    continue;
                var args = n["args"] as JsonObject ?? new JsonObject();
                notes.Add(new Note(notes.Count, p, moveId, J.Str(n["type"]) ?? "", J.Str(n["log"]) ?? "", args));
            }
        }
        return new BgaGame { TableId = table ?? "unknown", Notes = notes, GameData = data };
    }
}

// Reading BGA's JSON, which writes the same value as a string in one place and a number in another.
public static class J
{
    public static string? Str(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(),
    };

    public static int? Int(JsonNode? node) => int.TryParse(Str(node), out var number) ? number : null;

    // The number inside one of BGA's icon snippets ("<div class='tmlogs_icon' title='Power'><div class='power_amount'>3</div></div>"),
    // or a plain number; 0 if there is none.
    public static int Amount(JsonNode? node)
    {
        var text = Str(node) ?? "";
        if (int.TryParse(text.Trim(), out var plain))
            return plain;
        var match = Regex.Match(text, @">\s*(-?\d+)\s*<");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    // A log line with its ${…} filled in and the icon snippets reduced to "3 Power".
    public static string Plain(string log, JsonObject args)
    {
        var text = Regex.Replace(log, @"\$\{(\w+)\}", m => Str(args[m.Groups[1].Value]) ?? "");
        text = Regex.Replace(text, @"<div class='tmlogs_icon' title='([^']*)'><div class='[^']*'>([^<]*)</div></div>",
            m => m.Groups[2].Value.Replace("&nbsp;", "").Trim() is { Length: > 0 } amount ? $"{amount} {m.Groups[1].Value}" : m.Groups[1].Value);
        text = Regex.Replace(text, "<[^>]+>", "").Replace("&nbsp;", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
