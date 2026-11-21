using System.Collections.Generic;
using Godot;

namespace TMCalc;

// A log text broken into lines that fit a width: its results (the " · " parts, GameLog.Describe) packed onto a line
// while they fit, the next going onto a new line that starts "· "; a part too long for a line by itself broken
// between words. Text that fits (or no width yet) is one line, unchanged. Used by the full log's rows (all the lines)
// and the top panel's log line (the first line, then "…"; all the lines when hovered).
//   LogBreaks.Lines("Goblins upgrade … (2w 3c) · Kingdom of Ember offered 1 power", font, 22, 400)
//     → ["Goblins upgrade … (2w 3c)", "· Kingdom of Ember offered 1 power"]
public static class LogBreaks
{
    public const string Separator = " · ";
    public const string More = " …"; // after a first line that has more

    public static List<string> Lines(string text, Font font, int fontSize, float width)
    {
        text ??= "";
        float Width(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, fontSize).X;
        if (font is null || width <= 0 || Width(text) <= width)
            return new List<string> { text };
        var lines = new List<string>();
        string line = null;
        foreach (string part in text.Split(Separator))
        {
            if (line is not null && Width(line + Separator + part) <= width)
            {
                line += Separator + part;
                continue;
            }
            if (line is not null)
                lines.Add(line);
            List<string> wrapped = Wrap(line is null ? part : "· " + part, Width, width);
            lines.AddRange(wrapped.GetRange(0, wrapped.Count - 1));
            line = wrapped[^1];
        }
        lines.Add(line);
        return lines;
    }

    // One line's worth: the first line (with " …" after it if there is more), fitting the width.
    public static string FirstLine(string text, Font font, int fontSize, float width)
    {
        if (font is null || width <= 0)
            return text ?? "";
        List<string> whole = Lines(text, font, fontSize, width);
        if (whole.Count == 1)
            return whole[0];
        float more = font.GetStringSize(More, HorizontalAlignment.Left, -1, fontSize).X;
        return Lines(text, font, fontSize, width - more)[0] + More;
    }

    // Between words, as many as fit on each line (a word too long for a line by itself stays whole).
    private static List<string> Wrap(string text, System.Func<string, float> measure, float width)
    {
        var lines = new List<string>();
        string line = "";
        foreach (string word in text.Split(' '))
        {
            string longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length == 0 || measure(longer) <= width)
                line = longer;
            else
            {
                lines.Add(line);
                line = word;
            }
        }
        lines.Add(line);
        return lines;
    }
}
