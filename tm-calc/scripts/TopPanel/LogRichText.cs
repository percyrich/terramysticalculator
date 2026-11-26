using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace TMCalc;

// A piece of log text in a colour, with a stroke round its letters (Main.LogSegments: the faction's colour, a black
// stroke; lavender for what is nobody's and the " · " between parts). Glow: a soft glow in this colour behind the
// letters and their stroke (LogTextView draws it: GlowLayers).
public readonly record struct LogSegment(string Text, Color Fill, Color Stroke, Color? Glow = null);

// Log text in colour: the segments' plain text broken into lines as LogBreaks breaks it (or as given), each character
// keeping its segment's look (Runs; LogTextView draws them: the full log's rows, the notes, the top panel's log line
// and its hovered whole). BbCode gives the same as RichTextLabel BBCode (no longer used by the game).
public static class LogRichText
{
    public const int OutlineSize = 4;

    // Colours at least this light are shown as they are (green, the darkest that reads well on the dark log with a
    // black stroke, is about 0.52); darker ones are mixed towards white, keeping their hue, until this light.
    public const float ReadableLuminance = 0.52f, TintedLuminance = 0.6f;

    // The colour as the log shows it: light enough to read with a black stroke (red → salmon, blue → sky blue,
    // brown → tan, black → grey, purple → lavender).
    // tinted: how light a darker colour is made (TintedLuminance unless told otherwise).
    public static Color Readable(Color colour, float tinted = TintedLuminance)
    {
        float l = colour.Luminance; // linear in the channels, white 1: mixing a part t of white adds t × (1 − l)
        if (l >= ReadableLuminance)
            return colour;
        return colour.Lerp(Colors.White, Mathf.Clamp((tinted - l) / (1 - l), 0f, 1f)) with { A = colour.A };
    }

    public static readonly Color Stroke = Colors.Black;

    public static string Plain(IReadOnlyList<LogSegment> segments) => string.Concat(segments.Select(s => s.Text));

    // The BBCode for these lines (the segments' plain text broken, words' spaces dropped at the breaks, perhaps with
    // more on the end such as " …"). Each character takes its segment's colours; anything not in the text, `extra`'s.
    // glow: for a glow layer, that layer's opacity: only segments with a Glow are drawn, letters and strokes in its
    // colour at that opacity.
    public static string BbCode(IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines, LogSegment extra, float? glow = null)
    {
        var code = new StringBuilder();
        List<List<(string Text, LogSegment Look)>> runs = Runs(segments, lines, extra);
        for (var l = 0; l < runs.Count; l++)
        {
            if (l > 0)
                code.Append('\n');
            foreach (var (text, look) in runs[l])
            {
                Color fill = look.Fill, stroke = look.Stroke;
                if (glow is float layer)
                    fill = stroke = look.Glow is Color soft ? soft with { A = soft.A * layer } : Colors.Transparent;
                code.Append($"[color=#{fill.ToHtml(true)}][outline_color=#{stroke.ToHtml(true)}]")
                    .Append(text.Replace("[", "[lb]")).Append("[/outline_color][/color]");
            }
        }
        return code.ToString();
    }

    // These lines (the segments' plain text broken, words' spaces dropped at the breaks, perhaps with more on the end
    // such as " …") as runs of one look each: each character takes its segment's look; anything not in the text,
    // `extra`'s.
    public static List<List<(string Text, LogSegment Look)>> Runs(IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines, LogSegment extra)
    {
        string plain = Plain(segments);
        var owner = new int[plain.Length];
        for (int s = 0, at = 0; s < segments.Count; s++)
            for (var k = 0; k < segments[s].Text.Length; k++)
                owner[at++] = s;
        var result = new List<List<(string, LogSegment)>>();
        var p = 0;
        foreach (string line in lines)
        {
            var runs = new List<(string, LogSegment)>();
            int run = -2; // the segment of the open run (-1: extra)
            var text = new StringBuilder();
            void Flush()
            {
                if (text.Length > 0)
                    runs.Add((text.ToString(), run >= 0 ? segments[run] : extra));
                text.Clear();
            }
            foreach (char ch in line)
            {
                // The next character of the text that is this one, past any spaces dropped at a break.
                int j = p;
                while (j < plain.Length && plain[j] != ch && plain[j] == ' ')
                    j++;
                int who = -1;
                if (j < plain.Length && plain[j] == ch)
                {
                    who = owner[j];
                    p = j + 1;
                }
                if (who != run)
                {
                    Flush();
                    run = who;
                }
                text.Append(ch);
            }
            Flush();
            result.Add(runs);
        }
        return result;
    }

    // The glow: the Glow segment drawn again behind the text.
    public static readonly (int Outline, float Opacity)[] GlowLayers = { (8, 0.8f) };

    // One piece of text, all in one look (e.g. a plain string shown in the log's own colour).
    public static IReadOnlyList<LogSegment> Single(string text, Color fill, Color stroke) =>
        new[] { new LogSegment(text ?? "", fill, stroke) };
}
