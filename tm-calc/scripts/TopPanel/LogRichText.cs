using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace TMCalc;

// A piece of log text in a colour, with a stroke round its letters (Main.LogSegments: the faction's colour, black
// round light colours and brown, white round dark ones; the power purple for what is nobody's and for the " · "
// between parts).
public readonly record struct LogSegment(string Text, Color Fill, Color Stroke);

// Log text in colour, on a RichTextLabel: the segments' plain text is broken into lines as LogBreaks breaks it (or as
// given), and each character keeps its segment's colours. Used by the full log's rows, the notes and the top panel's
// log line (and its hovered whole).
public static class LogRichText
{
    public const int OutlineSize = 4;

    public static string Plain(IReadOnlyList<LogSegment> segments) => string.Concat(segments.Select(s => s.Text));

    // A label for log text: BBCode, as tall as its lines; the lines are broken here (Word wrapping only keeps its width
    // from following the text).
    public static RichTextLabel Make(string name = null) => new()
    {
        Name = name ?? "Text", BbcodeEnabled = true, FitContent = true, ScrollActive = false,
        AutowrapMode = TextServer.AutowrapMode.Word, MouseFilter = Control.MouseFilterEnum.Pass,
    };

    public static void Style(RichTextLabel label, Font font, int fontSize, int outline = OutlineSize)
    {
        label.AddThemeFontOverride("normal_font", font);
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", outline);
        label.AddThemeConstantOverride("line_separation", 0);
    }

    // The BBCode for these lines (the segments' plain text broken, words' spaces dropped at the breaks, perhaps with
    // more on the end such as " …"). Each character takes its segment's colours; anything not in the text, `extra`'s.
    public static string BbCode(IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines, LogSegment extra)
    {
        string plain = Plain(segments);
        var owner = new int[plain.Length];
        for (int s = 0, at = 0; s < segments.Count; s++)
            for (var k = 0; k < segments[s].Text.Length; k++)
                owner[at++] = s;
        var code = new StringBuilder();
        var p = 0;
        for (var l = 0; l < lines.Count; l++)
        {
            if (l > 0)
                code.Append('\n');
            int run = -2; // the segment of the open run (-1: extra)
            var text = new StringBuilder();
            void Flush()
            {
                if (text.Length == 0)
                    return;
                LogSegment look = run >= 0 ? segments[run] : extra;
                code.Append($"[color=#{look.Fill.ToHtml(true)}][outline_color=#{look.Stroke.ToHtml(true)}]")
                    .Append(text.ToString().Replace("[", "[lb]")).Append("[/outline_color][/color]");
                text.Clear();
            }
            foreach (char ch in lines[l])
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
        }
        return code.ToString();
    }

    // One piece of text, all in one look (e.g. a plain string shown in the log's own colour).
    public static IReadOnlyList<LogSegment> Single(string text, Color fill, Color stroke) =>
        new[] { new LogSegment(text ?? "", fill, stroke) };
}
