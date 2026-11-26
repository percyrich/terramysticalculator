using System;
using System.Collections.Generic;
using Godot;

namespace TMCalc;

// Log text in colour (LogSegment), drawn straight onto the control with the font (DrawStringOutline, DrawString) in
// the order a RichTextLabel draws: the glow layers behind (LogRichText.GlowLayers, each run's outline then its letters,
// in its Glow colour), then every run's outline, then every run's letters. 
//   Lines: given (Show(segments, lines): the top panel's line, its hovered whole), or broken to fit the width with
//   LogBreaks (Show(segments): the log's rows), the control as tall as its lines.
public partial class LogTextView : Control
{
    // Centred in its height (the top panel's line); otherwise from the top, as tall as its lines.
    public bool CentreVertically { get; set; }

    private IReadOnlyList<LogSegment> _segments = Array.Empty<LogSegment>();
    private IReadOnlyList<string> _given;   // null: broken to fit the width
    private LogSegment _extra;              // the look of text not in the segments (" …")
    private List<List<(string Text, LogSegment Look)>> _runs = new();
    private Font _font = ThemeDB.FallbackFont;
    private int _size = 16, _outline;
    private float _laidOut = -1;

    public LogTextView()
    {
        MouseFilter = MouseFilterEnum.Pass;
        Resized += () => Relayout(false);
    }

    public float LineHeight => _font.GetHeight(_size);

    public void Style(Font font, int fontSize, int outline)
    {
        _font = font ?? ThemeDB.FallbackFont;
        _size = fontSize;
        _outline = outline;
        Relayout(true);
    }

    public void Show(IReadOnlyList<LogSegment> segments, IReadOnlyList<string> lines = null, LogSegment extra = default)
    {
        _segments = segments ?? Array.Empty<LogSegment>();
        _given = lines;
        _extra = extra;
        Relayout(true);
    }

    private void Relayout(bool force)
    {
        IReadOnlyList<string> lines = _given;
        if (lines is null)
        {
            float width = Size.X - _outline; // room for the outline at the ends
            if (!force && Mathf.IsEqualApprox(width, _laidOut))
                return;
            _laidOut = width;
            string plain = LogRichText.Plain(_segments);
            lines = width > 0 ? LogBreaks.Lines(plain, _font, _size, width) : new List<string> { plain };
        }
        _runs = LogRichText.Runs(_segments, lines, _extra);
        float height = _runs.Count * LineHeight;
        if (!CentreVertically && !Mathf.IsEqualApprox(CustomMinimumSize.Y, height))
            CustomMinimumSize = new Vector2(0, height);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float lineHeight = LineHeight, ascent = _font.GetAscent(_size);
        float top = CentreVertically ? (Size.Y - _runs.Count * lineHeight) / 2 : 0;
        var placed = new List<(Vector2 At, string Text, LogSegment Look)>();
        for (var l = 0; l < _runs.Count; l++)
        {
            float x = _outline / 4f, y = top + l * lineHeight + ascent;
            foreach (var (text, look) in _runs[l])
            {
                placed.Add((new Vector2(x, y), text, look));
                x += _font.GetStringSize(text, HorizontalAlignment.Left, -1, _size).X;
            }
        }
        foreach (var (extra, opacity) in LogRichText.GlowLayers)
            foreach (var (at, text, look) in placed)
                if (look.Glow is Color glow)
                {
                    Color colour = glow with { A = glow.A * opacity };
                    DrawStringOutline(_font, at, text, HorizontalAlignment.Left, -1, _size, _outline + extra, colour);
                    DrawString(_font, at, text, HorizontalAlignment.Left, -1, _size, colour);
                }
        if (_outline > 0)
            foreach (var (at, text, look) in placed)
                if (look.Stroke.A > 0)
                    DrawStringOutline(_font, at, text, HorizontalAlignment.Left, -1, _size, _outline, look.Stroke);
        foreach (var (at, text, look) in placed)
            DrawString(_font, at, text, HorizontalAlignment.Left, -1, _size, look.Fill);
    }
}
