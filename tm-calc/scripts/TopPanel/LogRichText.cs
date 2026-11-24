using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace TMCalc;

// A piece of log text in a colour, with a stroke round its letters (Main.LogSegments: the faction's colour, made
// Readable, with a black stroke; the power purple, made Readable, for what is nobody's and the " · " between parts).
// Glow: a blurred stroke in this colour round the letters instead (the black faction's text: a soft grey, as a hard
// stroke of any colour round near-black text didn't look good), drawn by layers behind the label (SetText).
public readonly record struct LogSegment(string Text, Color Fill, Color Stroke, Color? Glow = null);

// Log text in colour, on a RichTextLabel: the segments' plain text is broken into lines as LogBreaks breaks it (or as
// given), and each character keeps its segment's colours. Used by the full log's rows, the notes and the top panel's
// log line (and its hovered whole).
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
    // glow: for a glow layer (SetText), that layer's opacity: only segments with a Glow are drawn, letters and strokes
    // in its colour at that opacity.
    public static string BbCode(IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines, LogSegment extra, float? glow = null)
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
                Color fill = look.Fill, stroke = look.Stroke;
                if (glow is float layer)
                    fill = stroke = look.Glow is Color soft ? soft with { A = soft.A * layer } : Colors.Transparent;
                code.Append($"[color=#{fill.ToHtml(true)}][outline_color=#{stroke.ToHtml(true)}]")
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

    // The blurred stroke: copies of the text behind the label, each with only the Glow segments, wider and fainter
    // outwards (outline size, opacity), so together they fade off softly. The sizes are beyond the label's own stroke
    // (the black one every faction has).
    public static readonly (int Outline, float Opacity)[] GlowLayers = { (8, 0.8f), (12, 0.5f), (14, 0.35f), (16, 0.2f) }; // Log Font Lab, 2026-11-24

    // Shows the text on the label, with its glow layers (made the first time a glow is wanted, hidden when none is).
    public static void SetText(RichTextLabel label, IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines, LogSegment extra)
    {
        label.Text = BbCode(segments, lines, extra);
        bool glows = segments.Any(segment => segment.Glow is not null);
        for (var i = 0; i < GlowLayers.Length; i++)
        {
            var layer = label.GetNodeOrNull<RichTextLabel>($"Glow{i}");
            if (!glows)
            {
                if (layer is not null)
                    layer.Visible = false;
                continue;
            }
            if (layer is null)
            {
                layer = Make($"Glow{i}");
                layer.MouseFilter = Control.MouseFilterEnum.Ignore;
                layer.ShowBehindParent = true; // under the label's own text
                layer.FitContent = false;
                layer.ClipContents = false;   // the glow reaches past the lines' box
                label.ClipContents = false;   // (a clipping label clips its children too)
                label.AddChild(layer, false, Node.InternalMode.Front);
                layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            }
            // Laid out as the label is, so the letters fall in the same places.
            Style(layer, label.GetThemeFont("normal_font"), label.GetThemeFontSize("normal_font_size"),
                label.GetThemeConstant("outline_size") + GlowLayers[i].Outline);
            layer.AddThemeStyleboxOverride("normal", label.GetThemeStylebox("normal")); // the same margins
            layer.AutowrapMode = label.AutowrapMode;
            layer.VerticalAlignment = label.VerticalAlignment;
            layer.Text = BbCode(segments, lines, extra, GlowLayers[i].Opacity);
            layer.Visible = true;
        }
    }

    // One piece of text, all in one look (e.g. a plain string shown in the log's own colour).
    public static IReadOnlyList<LogSegment> Single(string text, Color fill, Color stroke) =>
        new[] { new LogSegment(text ?? "", fill, stroke) };
}
