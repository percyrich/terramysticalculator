using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace TMCalc;

// A picture written as SVG text through calls like a 2D canvas's: paths filled and stroked, gradients,
// transforms and clips. It reads like canvas drawing code (save, translate, fill, restore); Godot then turns the
// text into pixels (see IconArt.Rasterise).
// Every Translate, Rotate, Scale or Clip opens an SVG group that lasts until the matching Restore, just as a
// canvas's transform and clip last until restore().
internal sealed class Svg
{
    private readonly float _width, _height;
    private readonly StringBuilder _defs = new(), _body = new();
    private readonly Stack<int> _saved = new(); // for each Save: how many groups were open before it
    private int _open;                          // groups opened since the last Save
    private int _ids;                           // for naming gradients and clips

    // A picture `width` × `height` units, one unit to a pixel (before any scaling when it's turned into pixels).
    public Svg(float width, float height)
    {
        _width = width;
        _height = height;
    }

    public void Save()
    {
        _saved.Push(_open);
        _open = 0;
    }

    public void Restore()
    {
        for (; _open > 0; _open--)
            _body.Append("</g>");
        _open = _saved.Pop();
    }

    public void Translate(float x, float y) => Open($"transform=\"translate({N(x)} {N(y)})\"");
    public void Rotate(float radians) => Open($"transform=\"rotate({N(Mathf.RadToDeg(radians))})\"");
    public void Scale(float x, float y) => Open($"transform=\"scale({N(x)} {N(y)})\"");
    // What is drawn next (until Restore) is laid down as one layer at this opacity (as a canvas drawing into its own
    // layer, then putting that down with globalAlpha), so overlapping parts fade as one.
    public void Opacity(float alpha) => Open($"opacity=\"{N(alpha)}\"");
    // Any further transform, as a canvas's transform(a, b, c, d, e, f): x' = a x + c y + e, y' = b x + d y + f
    // (e.g. a slant: drawing on a face of a block seen corner on).
    public void Transform(float a, float b, float c, float d, float e, float f) =>
        Open($"transform=\"matrix({N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)})\"");

    // SVG markup drawn as it is, in the current transform and clip (e.g. a whole drawing made elsewhere: the landscapes'
    // art). Its ids must not clash with this picture's own (c0, c1, …, g0, g1, …).
    public void Raw(string markup) => _body.Append(markup);

    // Only what is inside `path` shows of what is drawn next (until Restore).
    public void Clip(SvgPath path)
    {
        string id = $"c{_ids++}";
        _defs.Append($"<clipPath id=\"{id}\"><path d=\"{path}\"/></clipPath>");
        Open($"clip-path=\"url(#{id})\"");
    }

    public void Fill(SvgPath path, Paint paint) =>
        _body.Append($"<path d=\"{path}\" {Colour("fill", paint)} stroke=\"none\"/>");

    // A line along the path, `width` wide, centred on it. join: "round", "miter" or "bevel"; cap: "butt", "round"
    // or "square" (the ends of open paths). dash: dashes of that length with gaps of that length (as a canvas's
    // setLineDash), or a solid line.
    public void Stroke(SvgPath path, Color colour, float width, string join = "round", string cap = "butt", (float On, float Off)? dash = null) =>
        _body.Append($"<path d=\"{path}\" fill=\"none\" {Colour("stroke", colour)} stroke-width=\"{N(width)}\" " +
                     $"stroke-linejoin=\"{join}\" stroke-linecap=\"{cap}\" stroke-miterlimit=\"10\"" +
                     (dash is { } d ? $" stroke-dasharray=\"{N(d.On)} {N(d.Off)}\"" : "") + "/>");

    // The whole picture as SVG text.
    public string Text()
    {
        var text = new StringBuilder();
        text.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(_width)}\" height=\"{N(_height)}\" viewBox=\"0 0 {N(_width)} {N(_height)}\">");
        text.Append("<defs>").Append(_defs).Append("</defs>").Append(_body);
        text.Append(string.Concat(Enumerable.Repeat("</g>", _open + _saved.Sum()))); // anything left open
        return text.Append("</svg>").ToString();
    }

    private void Open(string attributes)
    {
        _body.Append($"<g {attributes}>");
        _open++;
    }

    // What to fill or stroke with, as SVG attributes: a colour (with its opacity), or a gradient, defined once.
    private string Colour(string what, Paint paint)
    {
        if (paint.Stops is null)
            return $"{what}=\"#{paint.Colour.ToHtml(false)}\" {what}-opacity=\"{N(paint.Colour.A)}\"";
        string id = $"g{_ids++}";
        string stops = string.Concat(paint.Stops.Select(s =>
            $"<stop offset=\"{N(s.At)}\" stop-color=\"#{s.Colour.ToHtml(false)}\" stop-opacity=\"{N(s.Colour.A)}\"/>"));
        string kind = paint.IsRadial ? "radialGradient" : "linearGradient";
        _defs.Append($"<{kind} id=\"{id}\" gradientUnits=\"userSpaceOnUse\" {paint.Geometry}>{stops}</{kind}>");
        return $"{what}=\"url(#{id})\"";
    }

    // A number for SVG: dot for the decimal point whatever the computer's language, at most three decimals.
    internal static string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

// What to fill with: a colour, or a gradient whose coordinates are in the space the path is drawn in (as on a
// canvas). A Color can be given wherever a Paint is wanted.
internal sealed class Paint
{
    public Color Colour { get; private init; }
    public (float At, Color Colour)[] Stops { get; private init; } // null for a plain colour
    public bool IsRadial { get; private init; }
    public string Geometry { get; private init; }                  // the gradient's position, as SVG attributes

    public static implicit operator Paint(Color colour) => new() { Colour = colour };

    // From (x0, y0) to (x1, y1), like a canvas's createLinearGradient.
    public static Paint Linear(float x0, float y0, float x1, float y1, params (float At, Color Colour)[] stops) => new()
    {
        Stops = stops,
        Geometry = $"x1=\"{Svg.N(x0)}\" y1=\"{Svg.N(y0)}\" x2=\"{Svg.N(x1)}\" y2=\"{Svg.N(y1)}\"",
    };

    // Rings round (cx, cy) out to radius r, starting from (fx, fy): like a canvas's createRadialGradient with an
    // inner circle of no size at (fx, fy).
    public static Paint Radial(float fx, float fy, float cx, float cy, float r, params (float At, Color Colour)[] stops) => new()
    {
        Stops = stops,
        IsRadial = true,
        Geometry = $"fx=\"{Svg.N(fx)}\" fy=\"{Svg.N(fy)}\" cx=\"{Svg.N(cx)}\" cy=\"{Svg.N(cy)}\" r=\"{Svg.N(r)}\"",
    };
}

// A path's outline, written as SVG path data, built with the same steps as a canvas path.
internal sealed class SvgPath
{
    private readonly StringBuilder _data = new();
    private bool _hasPoint; // whether there is a current point (an arc then joins it with a straight line)

    public SvgPath MoveTo(float x, float y) => Add($"M{N(x)} {N(y)}");
    public SvgPath MoveTo(Vector2 p) => MoveTo(p.X, p.Y);
    public SvgPath LineTo(float x, float y) => _hasPoint ? Add($"L{N(x)} {N(y)}") : MoveTo(x, y);
    public SvgPath LineTo(Vector2 p) => LineTo(p.X, p.Y);
    public SvgPath QuadTo(float cx, float cy, float x, float y) => Add($"Q{N(cx)} {N(cy)} {N(x)} {N(y)}");
    public SvgPath CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) =>
        Add($"C{N(c1x)} {N(c1y)} {N(c2x)} {N(c2y)} {N(x)} {N(y)}");
    public SvgPath CubicTo(Vector2 c1, Vector2 c2, Vector2 p) => CubicTo(c1.X, c1.Y, c2.X, c2.Y, p.X, p.Y);
    public SvgPath Close() => Add("Z");

    // Raw SVG path data, e.g. from an SVG file.
    public SvgPath Data(string data) => Add(data);

    // Part of a circle, as a canvas's arc(): from angle `from` to `to` (radians; increasing is clockwise on
    // screen), the other way round if anticlockwise. A whole turn or more draws the full circle.
    public SvgPath Arc(float cx, float cy, float r, float from, float to, bool anticlockwise = false)
    {
        float sweep = to - from, turn = 2 * Mathf.Pi;
        if (!anticlockwise)
            sweep = sweep >= turn ? turn : Mathf.PosMod(sweep, turn);
        else
            sweep = sweep <= -turn ? -turn : -Mathf.PosMod(-sweep, turn);
        LineTo(cx + r * Mathf.Cos(from), cy + r * Mathf.Sin(from));
        // In pieces of at most a quarter turn, so each SVG arc is unambiguous.
        int pieces = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / (Mathf.Pi / 2) - 1e-4f));
        for (var i = 1; i <= pieces && sweep != 0; i++)
        {
            float a = from + sweep * i / pieces;
            Add($"A{N(r)} {N(r)} 0 0 {(sweep > 0 ? 1 : 0)} {N(cx + r * Mathf.Cos(a))} {N(cy + r * Mathf.Sin(a))}");
        }
        return this;
    }

    // A whole ellipse, upright, as its own closed shape.
    public SvgPath Ellipse(float cx, float cy, float rx, float ry) =>
        MoveTo(cx + rx, cy).Add($"A{N(rx)} {N(ry)} 0 0 1 {N(cx - rx)} {N(cy)}A{N(rx)} {N(ry)} 0 0 1 {N(cx + rx)} {N(cy)}Z");

    public SvgPath Rect(float x, float y, float w, float h) =>
        MoveTo(x, y).LineTo(x + w, y).LineTo(x + w, y + h).LineTo(x, y + h).Close();

    // A rectangle with its corners rounded to radius r (at most half its width or height).
    public SvgPath RoundRect(float x, float y, float w, float h, float r)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) / 2);
        string Corner(float px, float py) => $"A{N(r)} {N(r)} 0 0 1 {N(px)} {N(py)}";
        MoveTo(x + r, y).LineTo(x + w - r, y).Add(Corner(x + w, y + r));
        LineTo(x + w, y + h - r).Add(Corner(x + w - r, y + h));
        LineTo(x + r, y + h).Add(Corner(x, y + h - r));
        return LineTo(x, y + r).Add(Corner(x + r, y)).Close();
    }

    public static SvgPath Circle(float x, float y, float r) => new SvgPath().Arc(x, y, r, 0, 2 * Mathf.Pi).Close();

    public static SvgPath Polygon(IEnumerable<Vector2> points)
    {
        var path = new SvgPath();
        foreach (Vector2 p in points)
            path.LineTo(p);
        return path.Close();
    }

    public override string ToString() => _data.ToString();

    private SvgPath Add(string step)
    {
        _data.Append(step);
        _hasPoint = true; // (after Close, the current point is where that part of the path started)
        return this;
    }

    private static string N(float value) => Svg.N(value);
}
