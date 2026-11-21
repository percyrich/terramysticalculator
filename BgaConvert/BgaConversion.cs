using System.Text.Json;

namespace BgaImport;

// The app's way in: the text of a TM Export file → its conversion. Error (and no Result) when the text isn't JSON or
// isn't a Terra Mystica export; otherwise the Result, complete or stopped part-way (ConversionResult.Stop).
public static class BgaConversion
{
    public static (ConversionResult? Result, string? Error) Convert(string json)
    {
        BgaGame game;
        try
        {
            game = BgaGame.Parse(json);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            return (null, e.Message);
        }
        if (game.Notes.Count == 0)
            return (null, "The file holds no moves.");
        return (new Converter(game).Run(), null);
    }
}
