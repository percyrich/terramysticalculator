using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core;

// A saved history: every position as the moves that made it (Session start first), bookmarks, where redo goes, the
// current position and the game line (History.Export / Import).
public sealed record PositionData(int Id, int? Parent, Move[] Moves, string? Bookmark, int? Next);
public sealed record HistoryData(List<PositionData> Positions, int Current, int? GameEnd, Dictionary<string, int> Used);

// The first saved move the rules refused while loading: at that position, from that state, why.
public sealed record LoadProblem(int Position, Move Move, GameState Before, string Reason);

public sealed record LoadResult(History? History, string? Error, LoadProblem? Problem);

// The save file (.tmcalc): readable JSON. { "Format": "tmcalc", "Version": 1, "History": { ... } }. Moves are written as
// objects with "$type" (the move's record name) and their fields; enums by name. Loading replays the moves through the
// rules, so a save holds no game state of its own: a rule change can make an old save stop part-way (LoadProblem).
public static class SaveGame
{
    public const string Format = "tmcalc";
    public const int Version = 1;
    public const string Extension = "tmcalc";

    private sealed record SaveFile(string Format, int Version, HistoryData History);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new MoveConverter() },
    };

    public static string ToJson(History history) => JsonSerializer.Serialize(new SaveFile(Format, Version, history.Export()), Options);

    public static LoadResult FromJson(string json)
    {
        SaveFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SaveFile>(json, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return new LoadResult(null, $"Not a readable save: {e.Message}", null);
        }
        if (file is null || file.Format != Format || file.History is null)
            return new LoadResult(null, "Not a Terra Mystica Calculator save.", null);
        if (file.Version > Version)
            return new LoadResult(null, $"Saved by a newer version (format {file.Version}; this one reads up to {Version}).", null);
        var history = History.Import(file.History, GameState.Initial, out LoadProblem? problem);
        return new LoadResult(history, null, problem);
    }

    // A move as { "$type": "BuildDwelling", ...its fields }.
    private sealed class MoveConverter : JsonConverter<Move>
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Move); // the records themselves: as usual

        public override Move? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty("$type", out JsonElement name) || name.GetString() is not string typeName)
                throw new JsonException("A move without its \"$type\".");
            Type? type = typeof(Move).Assembly.GetType($"{typeof(Move).Namespace}.{typeName}");
            if (type is null || !typeof(Move).IsAssignableFrom(type) || type.IsAbstract)
                throw new JsonException($"Unknown move \"{typeName}\".");
            return (Move?)document.RootElement.Deserialize(type, options);
        }

        public override void Write(Utf8JsonWriter writer, Move value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("$type", value.GetType().Name);
            using JsonDocument fields = JsonSerializer.SerializeToDocument(value, value.GetType(), options);
            foreach (JsonProperty field in fields.RootElement.EnumerateObject())
                field.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
