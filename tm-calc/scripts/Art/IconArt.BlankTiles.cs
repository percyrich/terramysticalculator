using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// IconArt, continued: the blank tiles everything is printed on.
public static partial class IconArt
{
    // ---------- Blank tiles (origin top left, w × h pixels) ----------

    internal static readonly Color StoneTop = new("6f8aa4"), StoneBottom = new("4c647d"), StoneEdge = new("22303b");

    // Stone: a plain, gentle gradient.
    internal static void Stone(Svg svg, SvgPath path, float w, float h, Color top, Color bottom) =>
        svg.Fill(path, Paint.Linear(0, 0, w * 0.3f, h, (0, top), (1, bottom)));

    // A sunken panel: darker inside, a shadow along its top and left, a highlight along its bottom and right.
    private static void Sunken(Svg svg, float x, float y, float w, float h, float r, Color fill)
    {
        SvgPath panel = new SvgPath().RoundRect(x, y, w, h, r);
        svg.Fill(panel, fill);
        svg.Save();
        svg.Clip(panel);
        svg.Stroke(new SvgPath().RoundRect(x + 1.5f, y + 1.5f, w + 4, h + 4, r), new Color(0, 0, 0, 0.35f), 4);
        svg.Stroke(new SvgPath().RoundRect(x - 4, y - 4, w + 2.5f, h + 2.5f, r), new Color(1, 1, 1, 0.14f), 4);
        svg.Restore();
        svg.Stroke(panel, new Color("0f1923", 0.6f), 1);
    }

    // Round tile: stone, with two equal square panels: what scores during the round (left) and the end-of-round
    // reward (right).
    private static void RoundTile(Svg svg, float w, float h)
    {
        SvgPath outer = new SvgPath().RoundRect(1, 1, w - 2, h - 2, h * 0.09f);
        Stone(svg, outer, w, h, StoneTop, StoneBottom);
        svg.Stroke(outer, StoneEdge, 2);
        Rect2 left = RoundTilePanel(0), right = RoundTilePanel(1);
        Sunken(svg, left.Position.X, left.Position.Y, left.Size.X, left.Size.Y, RoundTilePanelRadius, new Color("3f5567"));
        Sunken(svg, right.Position.X, right.Position.Y, right.Size.X, right.Size.Y, RoundTilePanelRadius, new Color("566f86"));
    }

    // A round tile's right-hand panel on its own (the blue square the last round's symbol sits in), `side` pixels
    // tall and widthShare of that across (1: the square; less: a narrower rectangle, the same corners): the game
    // screen's choices and the final scoring tile use it.
    public static Texture2D RoundTilePanelTexture(float side, float widthShare = 1f)
    {
        Rect2 panel = RoundTilePanel(1);
        return Cached(("round tile panel", side, widthShare), () =>
        {
            float width = panel.Size.X * widthShare;
            var svg = new Svg(width, panel.Size.Y);
            Sunken(svg, 0, 0, width, panel.Size.Y, RoundTilePanelRadius, new Color("566f86"));
            var pixels = new Vector2I(Mathf.Max(1, Mathf.RoundToInt(side * widthShare)), Mathf.Max(1, Mathf.RoundToInt(side)));
            return Rasterise(svg.Text(), pixels, side / panel.Size.X);
        });
    }

    // The panels' corner radius, in the tile's natural pixels.
    public static float RoundTilePanelRadius => NaturalSize(BlankTile.Round).Y * 0.06f;

    // A round tile's square panels (0: the left, 1: the right), in its natural pixels from its top left.
    public static Rect2 RoundTilePanel(int panel)
    {
        Vector2 size = NaturalSize(BlankTile.Round);
        float m = size.Y * 0.08f, side = size.Y - 2 * m, gap = (size.X - 2 * side) / 3;
        return new Rect2(gap + panel * (gap + side), m, side, side);
    }

    // The scroll's rolled top: a rounded bar with a spiral at one end.
    private static void ScrollRoll(Svg svg, float x, float y, float w, float h, bool curlRight)
    {
        SvgPath roll = new SvgPath().RoundRect(x, y, w, h, h / 2);
        svg.Fill(roll, Paint.Linear(0, y, 0, y + h, (0, new Color("f6ebc4")), (0.55f, new Color("e3cf94")), (1, new Color("b49a5e"))));
        svg.Stroke(roll, new Color("6e5a2e"), 1.2f);
        float cx = curlRight ? x + w - h * 0.55f : x + h * 0.55f, cy = y + h / 2;
        var spiral = new SvgPath();
        for (var i = 0; i <= 30; i++)
        {
            float t = i / 30f * 2.6f * Mathf.Pi, r = h * 0.06f + h * 0.06f * t;
            spiral.LineTo(cx + Mathf.Cos(t) * r * 0.8f, cy + Mathf.Sin(t) * r * 0.8f);
        }
        svg.Stroke(spiral, new Color("6e5a2e", 0.85f), 1);
    }

    // Pass tile: a tall stone tile almost covered by a scroll, rolled up at the top, cut straight across the bottom.
    private static void PassTile(Svg svg, float w, float h)
    {
        SvgPath outer = new SvgPath().RoundRect(1, 1, w - 2, h - 2, w * 0.1f);
        Stone(svg, outer, w, h, StoneTop, StoneBottom);
        svg.Stroke(outer, StoneEdge, 2);
        float x0 = w * 0.13f, x1 = w * 0.87f, y0 = h * 0.06f, y1 = h * 0.95f, rollHeight = h * 0.055f;
        SvgPath body = new SvgPath().Rect(x0, y0 + rollHeight * 0.5f, x1 - x0, y1 - y0 - rollHeight * 0.5f); // square corners at the bottom
        svg.Fill(body, Paint.Linear(x0, 0, x1, 0, (0, new Color("e8d8a2")), (0.5f, new Color("f6ecc6")), (1, new Color("e2cf96"))));
        svg.Stroke(body, new Color("7d683a"), 1.2f, "miter");
        ScrollRoll(svg, x0 - w * 0.03f, y0, x1 - x0 + w * 0.06f, rollHeight, true);
    }

    // Town key token: a pointy-topped hexagon with a bevelled gold frame round grey stone.
    private static void KeyToken(Svg svg, float w, float h)
    {
        float cx = w / 2, cy = h / 2, r = Mathf.Min(w / Mathf.Sqrt(3), h / 2) - 2;
        SvgPath Hex(float radius, float dx = 0, float dy = 0) => SvgPath.Polygon(HexCorners(new Vector2(cx + dx, cy + dy), radius));
        svg.Fill(Hex(r), Paint.Linear(cx - r, cy - r, cx + r, cy + r, (0, new Color("f0d27a")), (0.5f, new Color("c9a248")), (1, new Color("8f6c25"))));
        svg.Stroke(Hex(r), new Color("4e3a12"), 1.5f, "miter");
        svg.Stroke(Hex(r * 0.94f), new Color("fff0be", 0.6f), 1, "miter"); // the bevel's highlight
        SvgPath inner = Hex(r * 0.88f);
        Stone(svg, inner, w, h, new Color("a3acb2"), new Color("78838b"));
        svg.Save();
        svg.Clip(inner);
        svg.Stroke(Hex(r * 0.88f, 1.5f, 2), new Color(0, 0, 0, 0.25f), 5, "miter"); // a shadow inside the frame
        svg.Restore();
        svg.Stroke(inner, new Color("4e3a12"), 1.5f, "miter");
    }

    // Power action, from an SVG drawing (247 × 127 units), tidied and made symmetric about its middle (x = 124): a
    // back flap at each end, rolled up behind the banner; the banner; and the curled lip at each top corner.
    private static readonly Vector2[] FlapPoints = // its inner edge hides behind the banner
    {
        new(1, 15), new(10, 15), new(23, 9), new(30.5f, 5), new(36, 0.8f), new(40, 0.5f), new(43, 2.5f), new(43, 21),
        new(30, 30), new(30, 110), new(13, 109.5f), new(7.5f, 111), new(2.5f, 111), new(0.5f, 109.5f),
    };
    private static readonly Vector2[] LipPoints =
    {
        new(43, 7.5f), new(41, 11.5f), new(36, 14), new(30.5f, 15), new(25.5f, 17.5f), new(23, 20.5f), new(27, 23), new(34, 23),
        new(41, 21.5f), new(42.5f, 20.7f),
    };
    private static readonly Vector2[] TopPoints = // left half, outwards in
    {
        new(23, 20.5f), new(27, 23), new(34, 23), new(41, 21.5f), new(48.5f, 17.5f), new(55, 14), new(62, 11.5f), new(70, 10),
        new(85, 9.6f), new(104, 9.2f), new(124, 9),
    };
    private static readonly Vector2[] BottomPoints =
    {
        new(21, 116.5f), new(23, 119), new(29.5f, 121.5f), new(34, 123.5f), new(42, 124), new(48.5f, 123.5f), new(52, 122),
        new(69.5f, 120.5f), new(95, 120), new(124, 119.6f),
    };

    private static Vector2[] MirrorX(IEnumerable<Vector2> points) => points.Select(p => new Vector2(248 - p.X, p.Y)).ToArray();

    // A smooth curve through points, added to a path that is already at points[0] (Catmull-Rom, as Bézier pieces).
    private static SvgPath Through(SvgPath path, IReadOnlyList<Vector2> points)
    {
        Vector2 P(int i) => points[Mathf.Clamp(i, 0, points.Count - 1)];
        for (var i = 0; i < points.Count - 1; i++)
            path.CubicTo(P(i) + (P(i + 1) - P(i - 1)) / 6, P(i + 1) - (P(i + 2) - P(i)) / 6, P(i + 1));
        return path;
    }

    // The same curve as Through, as points: `steps` along each piece.
    private static Vector2[] ThroughPoints(IReadOnlyList<Vector2> points, int steps = 6)
    {
        Vector2 P(int i) => points[Mathf.Clamp(i, 0, points.Count - 1)];
        var result = new List<Vector2> { points[0] };
        for (var i = 0; i < points.Count - 1; i++)
            for (var k = 1; k <= steps; k++)
                result.Add(P(i).BezierInterpolate(P(i) + (P(i + 1) - P(i - 1)) / 6, P(i + 1) - (P(i + 2) - P(i)) / 6, P(i + 1), (float)k / steps));
        return result.ToArray();
    }

    // The power action's silhouette, for EdgeHighlight, in its 247 × 127 units: the left flap, the banner's top edge
    // between the flaps, the right flap, then the banner's bottom edge. The lips lie inside the flaps.
    internal static Vector2[] PowerActionOutline()
    {
        Vector2[] flapTop = ThroughPoints(FlapPoints.Take(8).ToArray()); // (1, 15) round to (43, 2.5)
        Vector2[] top = ThroughPoints(TopPoints.Concat(Enumerable.Reverse(MirrorX(TopPoints)).Skip(1)).ToArray());
        Vector2[] bottom = ThroughPoints(BottomPoints.Concat(Enumerable.Reverse(MirrorX(BottomPoints)).Skip(1)).ToArray());
        int i = Array.FindIndex(top, p => p.X >= 43);                    // where the flap's inner edge meets the banner
        float meet = Mathf.Lerp(top[i - 1].Y, top[i].Y, (43 - top[i - 1].X) / (top[i].X - top[i - 1].X));
        Vector2[] flapBottom = { new(21, 109.8f), new(13, 109.5f), new(7.5f, 111), new(2.5f, 111), new(0.5f, 109.5f) }; // from the banner outwards
        return flapTop.Append(new Vector2(43, meet))
            .Concat(top.Where(p => p.X > 43 && p.X < 205)).Append(new Vector2(205, meet))
            .Concat(Enumerable.Reverse(MirrorX(flapTop)))
            .Concat(MirrorX(Enumerable.Reverse(flapBottom)))
            .Concat(Enumerable.Reverse(bottom))
            .Concat(flapBottom).ToArray();
    }

    // Drawn in the SVG's own units: the tile is always made at its natural size (Texture scales it).
    private static void PowerAction(Svg svg, float w, float h) => PowerAction(svg, 1.4f);

    // In its own 247 × 127 units; lineWidth in those units (1.4 at full size).
    private static void PowerAction(Svg svg, float lineWidth)
    {
        var line = new Color("7a5a1c");
        // Shading like a strip of paper: the flaps (rolled away behind) and the curled lips are in shade; the
        // banner is lit, darkening a little towards its ends where it starts to curve into the rolls.
        foreach (Vector2[] points in new[] { FlapPoints, MirrorX(FlapPoints) })
        {
            SvgPath flap = Through(new SvgPath().MoveTo(points[0]), points.Take(8).ToArray());
            foreach (Vector2 q in points.Skip(8))
                flap.LineTo(q);
            flap.Close();
            svg.Fill(flap, Paint.Linear(0, 0, 0, 127, (0, new Color("b48c33")), (1, new Color("94701f"))));
            svg.Stroke(flap, line, lineWidth);
        }
        // the banner: top edge across, down the right, bottom edge back, up the left
        Vector2[] top = TopPoints.Concat(Enumerable.Reverse(MirrorX(TopPoints)).Skip(1)).ToArray();
        Vector2[] bottom = BottomPoints.Concat(Enumerable.Reverse(MirrorX(BottomPoints)).Skip(1)).ToArray();
        SvgPath banner = Through(new SvgPath().MoveTo(top[0]), top).LineTo(227, 117.5f).LineTo(bottom[^1]);
        Through(banner, Enumerable.Reverse(bottom).ToArray()).LineTo(21, 110).Close();
        svg.Fill(banner, Paint.Linear(21, 0, 227, 0, (0, new Color("cfa947")), (0.1f, new Color("efd57f")), (0.5f, new Color("f6e294")),
            (0.9f, new Color("efd57f")), (1, new Color("cfa947"))));
        svg.Stroke(banner, line, lineWidth);
        foreach (Vector2[] points in new[] { LipPoints, MirrorX(LipPoints) })
        {
            SvgPath lip = new SvgPath().MoveTo(points[0]);
            Through(lip, points.Take(6).ToArray());
            Through(lip, points.Skip(5).ToArray());
            lip.Close();
            svg.Fill(lip, Paint.Linear(0, 0, 0, 24, (0, new Color("c49c3f")), (1, new Color("a5802c"))));
            svg.Stroke(lip, line, lineWidth);
        }
    }

    // Favour, as in the game: a glowing orange oval, near white at the centre, with soft rays outwards.
    private static void Favour(Svg svg, float w, float h)
    {
        const float edge = 3f; // the outline's width (inside the tile's box)
        SvgPath oval = new SvgPath().Ellipse(w / 2, h / 2, w / 2 - edge / 2, h / 2 - edge / 2);
        svg.Save();
        svg.Clip(oval);
        svg.Translate(w / 2, h / 2);
        svg.Scale(1, h / w); // the glow's circles squashed to the oval
        svg.Fill(new SvgPath().Rect(-w / 2, -w / 2, w, w), Paint.Radial(0, 0, 0, 0, w / 2,
            (0, new Color("fffbe0")), (0.18f, new Color("ffe27a")), (0.6f, new Color("fbb83a")), (1, new Color("ef9a22"))));
        for (var i = 0; i < 16; i++) // rays
        {
            float a = i / 16f * Tau + 0.1f, spread = 0.07f;
            svg.Fill(new SvgPath().MoveTo(0, 0).LineTo(At(a - spread, w)).LineTo(At(a + spread, w)).Close(), new Color("fff5c8", 0.22f));
        }
        svg.Restore();
        svg.Stroke(oval, new Color("5a3510", 0.9f), edge); // dark brown
    }
}
