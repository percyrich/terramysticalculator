using System.Reflection;

namespace BgaImport;

// The TM Export bookmarklet (BgaImport/bookmarklet.min.txt, built into this library): the one line a user pastes as a
// bookmark's URL. On a BGA game in progress or a replay it downloads bga-<table>.json, which BgaConversion converts.
public static class Bookmarklet
{
    public static string Text { get; } = Read();

    private static string Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("bookmarklet.min.txt")
            ?? throw new InvalidOperationException("The bookmarklet is missing from BgaConvert.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
