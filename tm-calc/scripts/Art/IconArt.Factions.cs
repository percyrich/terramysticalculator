using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// IconArt, continued: the published factions' actions and abilities.
public static partial class IconArt
{
    // ---------- Faction actions and abilities ----------

    private static readonly Color MapDesert = new("e2bf47"), MapGrey = new("a1a8ae"), MapMountain = new("8c8f93");
    private static readonly Color ArrowBlack = new("1d1d1d"), ArrowEdge = new("f3efe4");

    // Book action (repeatable faction actions, e.g. the Engineers' bridge): a closed leather book seen from above,
    // pages showing along its bottom edge, brass corners, and an octagon panel on the cover where the action's icon
    // goes. The panel is BookOctagon of s from centre to corner, centred on (0, 0); so a book icon BookScale times the
    // size of an action octagon has a panel exactly as big as that octagon (e.g. Texture(Icon.BookAction, 107) for
    // Art.OctagonSize 80, with the action's icon on top as on a bonus octagon).
    public const float BookOctagon = 0.36f, BookScale = 0.48f / BookOctagon;

    private static void BookAction(Svg svg, float s) => BookAction(svg, s, 0);

    // e: the book and its panel's flat top and bottom e longer at each end (WideBook, Chash Dallah's income track).
    private static void BookAction(Svg svg, float s, float e)
    {
        float l = -s * 0.46f - e, r = s * 0.46f + e, t = -s * 0.43f, b = s * 0.39f, o = Ow(s);
        // pages: a cream block under the cover, showing below it
        Outlined(svg, new SvgPath().RoundRect(l + s * 0.03f, t + s * 0.06f, r - l - s * 0.05f, b - t + s * 0.03f, s * 0.03f), new Color("ece2c6"), s, Out, o * 0.8f);
        foreach (float k in new[] { 0.3f, 0.65f })
        {
            float y = b + k * s * 0.08f;
            svg.Stroke(new SvgPath().MoveTo(l + s * 0.06f, y).LineTo(r - s * 0.03f, y), new Color(new Color("786038"), 0.6f), Mathf.Max(0.5f, o * 0.35f));
        }
        // the cover: dark leather, a stitched border
        Outlined(svg, new SvgPath().RoundRect(l, t, r - l, b - t, s * 0.04f),
            Paint.Linear(l, t, r, b, (0, new Color("74492a")), (1, new Color("43281a"))), s);
        svg.Stroke(new SvgPath().RoundRect(l + s * 0.045f, t + s * 0.045f, r - l - s * 0.09f, b - t - s * 0.09f, s * 0.025f),
            new Color(new Color("d6aa70"), 0.55f), Mathf.Max(0.6f, o * 0.4f), dash: (s * 0.035f, s * 0.025f));
        // brass corners: a small triangle over each corner of the cover
        float c = s * 0.12f;
        Paint brass = Paint.Linear(l, t, r, b, (0, new Color("f0d27a")), (1, new Color("a8812f")));
        foreach (var (x, y, dx, dy) in new[] { (l, t, 1, 1), (r, t, -1, 1), (r, b, -1, -1), (l, b, 1, -1) })
            Outlined(svg, SvgPath.Polygon(new[] { new Vector2(x, y), new Vector2(x + dx * c, y), new Vector2(x, y + dy * c) }), brass, s, Out, o * 0.6f);
        // the panel: light brown, sunk into the cover (a shadow along its top and left)
        float rr = s * BookOctagon;
        SvgPath panel = WideOctagonPath(rr, e);
        svg.Fill(panel, Paint.Linear(0, -rr, 0, rr, (0, new Color("dcae7c")), (1, new Color("c9935f"))));
        svg.Save();
        svg.Clip(panel);
        svg.Translate(rr * 0.05f, rr * 0.07f);
        svg.Stroke(WideOctagonPath(rr * 1.04f, e), new Color(new Color("3c200c"), 0.35f), rr * 0.16f);
        svg.Restore();
        svg.Stroke(panel, new Color("2b190d"), o * 0.8f);
    }

    // A plain action octagon at size s round a picture (kept just inside the octagon's inner line).
    private static void ActionWith(Svg svg, float s, Action inside)
    {
        Octagon(svg, s, used: false);
        svg.Save();
        svg.Clip(OctagonPath(s * 0.48f * 0.86f));
        inside();
        svg.Restore();
    }

    // A map hex for icons: pointy-topped, with an outline radius × 0.09 wide.
    private static void MapHex(Svg svg, float s, Vector2 centre, float radius, Color fill) =>
        Outlined(svg, SvgPath.Polygon(HexCorners(centre, radius)), fill, s, Out, Mathf.Max(1, radius * 0.09f));

    // A building centred on (x, y), `unit` pixels to a dwelling's length, no shadow.
    private static void PlacedBuilding(Svg svg, PieceKind kind, float x, float y, float unit, Color colour)
    {
        svg.Save();
        svg.Translate(x, y);
        Solid(svg, PieceArt.PictureFor(kind), 0, colour, shadow: false, unit: unit);
        svg.Restore();
    }

    // Chaos Magicians' stronghold action (two actions in a row): two action octagons inside one, the second down and
    // to the left of the first, in front of it.
    private static void DoubleAction(Svg svg, float s)
    {
        Octagon(svg, s, used: false);
        foreach (var (x, y) in new[] { (0.085f, -0.085f), (-0.085f, 0.085f) })
        {
            svg.Save();
            svg.Translate(x * s, y * s);
            Octagon(svg, s * 0.56f, used: false);
            svg.Restore();
        }
    }

    // Nomads' stronghold action (sandstorm): four map hexes, two side by side meeting in the middle, one above and one
    // below. Left: desert with a dwelling. Right: grey, with a desert hex hovering over it and its shadow on the grey.
    // An arrow from near the top middle of the octagon down onto the hovering hex, moved down SandstormArrowDrop of s
    // from where it would otherwise sit so it stays inside the octagon's inner line.
    private const float SandstormArrowDrop = 0.09f;

    private static void Sandstorm(Svg svg, float s)
    {
        float r = s * 0.2f, w = r * Mathf.Sqrt(3) / 2, lift = s * 0.075f;
        ActionWith(svg, s, () =>
        {
            MapHex(svg, s, new Vector2(0, -1.5f * r), r, MapGrey);
            MapHex(svg, s, new Vector2(0, 1.5f * r), r, MapGrey);
            MapHex(svg, s, new Vector2(-w, 0), r, MapDesert);
            MapHex(svg, s, new Vector2(w, 0), r, MapGrey);
            svg.Fill(SvgPath.Polygon(HexCorners(new Vector2(w + s * 0.025f, s * 0.012f), r * 0.86f)), new Color(0, 0, 0, 0.32f)); // the hovering hex's shadow
            PlacedBuilding(svg, PieceKind.Dwelling, -w, r * 0.05f, r * 0.62f, White);
        });
        MapHex(svg, s, new Vector2(w, -lift), r * 0.92f, MapDesert);
        var drop = new Vector2(0, s * SandstormArrowDrop);
        CurveArrow(svg, new Vector2(-s * 0.02f, -s * 0.37f) + drop, new Vector2(w + s * 0.01f, -s * 0.38f) + drop,
            new Vector2(w, -lift - r * 0.62f) + drop, ArrowBlack, ArrowEdge, s * 0.09f, s * 0.14f, s * 0.1f, Ow(s) * 0.5f);
    }

    // Swarmlings' stronghold action: a dwelling becomes a trading post (an arrow curving up from one to the other).
    private static void DwellingToTradingPost(Svg svg, float s, Color pieces) => ActionWith(svg, s, () =>
    {
        PlacedBuilding(svg, PieceKind.Dwelling, -s * 0.17f, s * 0.15f, s * 0.23f, pieces);
        PlacedBuilding(svg, PieceKind.TradingPost, s * 0.16f, -s * 0.07f, s * 0.23f, pieces);
        CurveArrow(svg, new Vector2(-s * 0.19f, 0), new Vector2(-s * 0.17f, -s * 0.2f), new Vector2(-s * 0.01f, -s * 0.21f),
            ArrowBlack, ArrowEdge, s * 0.08f, s * 0.12f, s * 0.09f, Ow(s) * 0.5f);
    });

    // Dwarves' tunnelling: two mountain hexes with grey dwellings, one hex apart; the two hexes between them (sharing
    // an edge) only as dashed outlines; an arrow from one dwelling over the gap to the other.
    private static void Tunnel(Svg svg, float s)
    {
        var cells = new (int Column, int Row, bool Built)[] { (0, 0, true), (1, 0, false), (0, 1, false), (1, 1, true) };
        Vector2 Centre(int c, int r) => new(Mathf.Sqrt(3) * (c + 0.5f * (r & 1)), 1.5f * r); // in hex radii
        List<Vector2> centres = cells.Select(c => Centre(c.Column, c.Row)).ToList();
        float minX = centres.Min(p => p.X), maxX = centres.Max(p => p.X), minY = centres.Min(p => p.Y), maxY = centres.Max(p => p.Y);
        float radius = s * 0.96f / Mathf.Max(maxX - minX + Mathf.Sqrt(3), maxY - minY + 2);
        var middle = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        Vector2 Place(int c, int r) => (Centre(c, r) - middle) * radius;
        foreach (var cell in cells.Where(c => !c.Built))
            svg.Stroke(SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius * 0.97f)), new Color("8c9298"),
                Mathf.Max(1, radius * 0.06f), dash: (radius * 0.16f, radius * 0.12f));
        foreach (var cell in cells.Where(c => c.Built))
            MapHex(svg, s, Place(cell.Column, cell.Row), radius * 0.97f, MapMountain);
        Vector2 a = Place(0, 0), b = Place(1, 1);
        foreach (Vector2 p in new[] { a, b })
            PlacedBuilding(svg, PieceKind.Dwelling, p.X, p.Y + radius * 0.05f, radius * 0.62f, new Color("b4b9be"));
        CurveArrow(svg, a + new Vector2(radius * 0.35f, -radius * 0.35f), (a + b) / 2 + new Vector2(radius * 0.55f, -radius * 1.25f),
            b + new Vector2(radius * 0.05f, -radius * 0.42f), ArrowBlack, ArrowEdge, s * 0.075f, s * 0.12f, s * 0.085f, Ow(s) * 0.5f);
    }

    // Fakirs' carpet flight (in place of shipping): a figure sitting cross-legged on a flying carpet.
    private static void CarpetFlight(Svg svg, float s)
    {
        float o = Ow(s);
        // speed lines behind the carpet
        foreach (var (x0, x1, y) in new[] { (-0.48f, -0.36f, 0.17f), (-0.46f, -0.3f, 0.27f) })
            svg.Stroke(new SvgPath().MoveTo(x0 * s, y * s).LineTo(x1 * s, y * s), new Color(200 / 255f, 210 / 255f, 220 / 255f, 0.85f), Mathf.Max(1, s * 0.03f), "round", "round");
        // the carpet: seen from a little above, rippling, with a gold border inside its edge and tassels at the ends
        SvgPath carpet = new SvgPath().MoveTo(-s * 0.34f, s * 0.12f).CubicTo(-s * 0.12f, s * 0.06f, s * 0.1f, s * 0.16f, s * 0.4f, s * 0.1f)
            .LineTo(s * 0.46f, s * 0.27f).CubicTo(s * 0.16f, s * 0.34f, -s * 0.06f, s * 0.24f, -s * 0.38f, s * 0.31f).Close();
        Outlined(svg, carpet, Paint.Linear(0, s * 0.06f, 0, s * 0.34f, (0, new Color("d0493b")), (1, new Color("962a22"))), s);
        Color gold = new("e8c15a");
        svg.Save();
        svg.Clip(carpet);
        svg.Stroke(new SvgPath().MoveTo(-s * 0.3f, s * 0.15f).CubicTo(-s * 0.1f, s * 0.1f, s * 0.1f, s * 0.19f, s * 0.37f, s * 0.14f)
            .LineTo(s * 0.41f, s * 0.25f).CubicTo(s * 0.15f, s * 0.3f, -s * 0.06f, s * 0.21f, -s * 0.33f, s * 0.27f).Close(), gold, s * 0.025f);
        svg.Restore();
        foreach (var (x, y, dx) in new[] { (-0.34f, 0.14f, -1), (-0.36f, 0.22f, -1), (-0.38f, 0.29f, -1), (0.41f, 0.13f, 1), (0.43f, 0.19f, 1), (0.45f, 0.25f, 1) })
            svg.Stroke(new SvgPath().MoveTo(x * s, y * s).LineTo((x + dx * 0.05f) * s, (y + 0.005f) * s), gold, Mathf.Max(1, s * 0.022f), "round", "round");
        // the figure: knees out to the sides, robe, arms bent with the hands on the knees, head, turban with a jewel
        Color fold = new("c9d0d6"), skin = new("8c5a38"); // a deep tan, as in the portrait
        float cx = s * 0.03f;
        Outlined(svg, new SvgPath().Ellipse(cx, s * 0.12f, s * 0.24f, s * 0.07f), fold, s);
        Outlined(svg, SvgPath.Polygon(new[] { new Vector2(cx - s * 0.1f, s * 0.12f), new Vector2(cx + s * 0.1f, s * 0.12f), new Vector2(cx + s * 0.075f, -s * 0.11f), new Vector2(cx - s * 0.075f, -s * 0.11f) }), White, s);
        foreach (int side in new[] { -1, 1 })
        {
            Band(svg, new SvgPath().MoveTo(cx + side * s * 0.07f, -s * 0.08f).LineTo(cx + side * s * 0.16f, 0).LineTo(cx + side * s * 0.17f, s * 0.09f), s * 0.05f, White, s * 0.7f);
            Outlined(svg, SvgPath.Circle(cx + side * s * 0.17f, s * 0.1f, s * 0.035f), skin, s, Out, o * 0.6f);
        }
        Outlined(svg, SvgPath.Circle(cx, -s * 0.18f, s * 0.075f), skin, s);
        float ty = -s * 0.215f, rx = s * 0.095f, ry = s * 0.075f; // the turban: the top half of an ellipse
        Outlined(svg, new SvgPath().MoveTo(cx - rx, ty).Data($"A{Svg.N(rx)} {Svg.N(ry)} 0 0 1 {Svg.N(cx + rx)} {Svg.N(ty)}").Close(), new Color("f1ece0"), s);
        Outlined(svg, SvgPath.Circle(cx, -s * 0.235f, s * 0.024f), new Color("3fa04d"), s, Out, o * 0.5f);
    }

    // Engineers (when passing, VP per bridge between their buildings): the tunnelling layout, with a grey trading post
    // and a grey dwelling one hex apart and a grey bridge across the edge between the two hexes in between (along that
    // edge, 0.12 R short of each end, 0.28 R wide, as on the map).
    private static void BridgeBetween(Svg svg, float s)
    {
        var cells = new (int Column, int Row, bool Built)[] { (0, 0, true), (1, 0, false), (0, 1, false), (1, 1, true) };
        Vector2 Centre(int c, int r) => new(Mathf.Sqrt(3) * (c + 0.5f * (r & 1)), 1.5f * r); // in hex radii
        List<Vector2> centres = cells.Select(c => Centre(c.Column, c.Row)).ToList();
        float minX = centres.Min(p => p.X), maxX = centres.Max(p => p.X), minY = centres.Min(p => p.Y), maxY = centres.Max(p => p.Y);
        float radius = s * 0.96f / Mathf.Max(maxX - minX + Mathf.Sqrt(3), maxY - minY + 2);
        var middle = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        Vector2 Place(int c, int r) => (Centre(c, r) - middle) * radius;
        foreach (var cell in cells.Where(c => !c.Built))
            svg.Stroke(SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius * 0.97f)), new Color("8c9298"),
                Mathf.Max(1, radius * 0.06f), dash: (radius * 0.16f, radius * 0.12f));
        foreach (var cell in cells.Where(c => c.Built))
            MapHex(svg, s, Place(cell.Column, cell.Row), radius * 0.97f, MapMountain);
        Vector2 a = Place(0, 0), b = Place(1, 1), corner = new Vector2(Mathf.Cos(Mathf.Pi / 6), 0.5f) * radius;
        Vector2 p0 = a + corner, p1 = b - corner, along = (p1 - p0).Normalized(), across = new Vector2(-along.Y, along.X) * radius * 0.14f;
        Vector2 q0 = p0 + along * radius * 0.12f, q1 = p1 - along * radius * 0.12f;
        Color grey = new("b4b9be");
        Outlined(svg, SvgPath.Polygon(new[] { q0 + across, q1 + across, q1 - across, q0 - across }), grey, s, Out, Mathf.Max(1, radius * 0.09f));
        PlacedBuilding(svg, PieceKind.TradingPost, a.X, a.Y + radius * 0.02f, radius * 0.62f, grey);
        PlacedBuilding(svg, PieceKind.Dwelling, b.X, b.Y + radius * 0.05f, radius * 0.62f, grey);
    }

    // A white cross, to lay over a power icon (e.g. power that is lost).
    private static void PowerCross(Svg svg, float s)
    {
        float d = s * 0.32f;
        Band(svg, new SvgPath().MoveTo(-d, -d).LineTo(d, d).MoveTo(d, -d).LineTo(-d, d), s * 0.12f, Colors.White, s);
    }

    // Rings for the two terrains outside the colour wheel, the same size and edge as the wheel's circles: the outside
    // 0.48 of s (so ColourWheel's 42 px circles match Texture(icon, RingIconSize)), a dark edge RingEdge of that inside
    // it and round the hole, the hole RingHole of the outside.
    public const int RingIconSize = 102;
    public const float RingHole = 0.55f;
    private const float RingEdge = 4f / 42f;
    private static SvgPath RingPath(float r) =>
        new SvgPath().Arc(0, 0, r, 0, Tau).Close().MoveTo(r * RingHole, 0).Arc(0, 0, r * RingHole, 0, -Tau, anticlockwise: true).Close();

    private static void RingEdges(Svg svg, float r)
    {
        float w = r * RingEdge;
        Color edge = new("161a18");
        svg.Stroke(SvgPath.Circle(0, 0, r - w / 2), edge, w);
        svg.Stroke(SvgPath.Circle(0, 0, r * RingHole + w / 2), edge, w);
    }

    // Ice: pale blue, lighter towards the top left, with a few lighter facets and thin white cracks.
    private static void IceRing(Svg svg, float s)
    {
        float r = s * 0.48f, ri = r * RingHole;
        SvgPath ring = RingPath(r);
        svg.Fill(ring, Paint.Radial(-r * 0.35f, -r * 0.4f, 0, 0, r * 1.05f, (0, new Color("f4f9fc")), (1, new Color("9fc8de"))));
        svg.Save();
        svg.Clip(RingPath(r));
        for (var i = 0; i < 6; i++)
        {
            float a = i / 6f * Tau + 0.35f, b = a + 0.42f, m = (a + b) / 2;
            svg.Fill(SvgPath.Polygon(new[] { At(a, ri), At(m + 0.12f, r), At(b, ri * 1.15f) }), new Color(1, 1, 1, 0.35f));
        }
        for (var i = 0; i < 7; i++)
        {
            float a = i / 7f * Tau + 0.1f, k = a + 0.18f;
            svg.Stroke(new SvgPath().MoveTo(At(a, ri)).LineTo(At(k, (ri + r) / 2)).LineTo(At(a + 0.05f, r)), new Color(1, 1, 1, 0.85f),
                Mathf.Max(0.6f, r * 0.025f), "round", "round");
        }
        svg.Restore();
        RingEdges(svg, r);
    }

    // Variable (the shapeshifters' and riverwalkers' choice): dark slate with light blades twisted round it, like an iris.
    private static void VariableRing(Svg svg, float s)
    {
        float r = s * 0.48f, ri = r * RingHole, w = Tau / 16 * 0.5f, twist = 0.32f;
        svg.Fill(RingPath(r), new Color("272b3d"));
        svg.Save();
        svg.Clip(RingPath(r));
        Paint blade = Paint.Radial(0, 0, 0, 0, r, (RingHole, new Color("6d7690")), (RingHole + (1 - RingHole) * 0.55f, new Color("c3cad8")), (1, new Color("7c859e")));
        for (var i = 0; i < 16; i++)
        {
            float a = i / 16f * Tau;
            svg.Fill(SvgPath.Polygon(new[] { At(a, ri * 0.95f), At(a + twist, r * 1.02f), At(a + twist + w, r * 1.02f), At(a + w * 0.6f, ri * 0.95f) }), blade);
        }
        svg.Restore();
        RingEdges(svg, r);
    }

    // Part of an upright ellipse round (cx, cy), from angle `from` to `to` (radians, clockwise on screen), drawn on from
    // where the path is; the path must already be at its start.
    private static SvgPath EllipseArc(SvgPath path, float cx, float cy, float rx, float ry, float from, float to, bool anticlockwise = false)
    {
        float sweep = to - from;
        bool large = Mathf.Abs(sweep) > Mathf.Pi;
        float x = cx + rx * Mathf.Cos(to), y = cy + ry * Mathf.Sin(to);
        return path.Data($"A{Svg.N(rx)} {Svg.N(ry)} 0 {(large ? 1 : 0)} {(anticlockwise ? 0 : 1)} {Svg.N(x)} {Svg.N(y)}");
    }

    // Djinni's lamp: a brass oil lamp, long spout to the left, curled handle to the right, domed lid, a puff of smoke.
    private static readonly Color[] Brass = { new("f6dc8c"), new("c99a3e"), new("8a6420") };
    private static void Lamp(Svg svg, float s)
    {
        float o = Ow(s);
        Paint brass = Paint.Linear(0, -s * 0.2f, 0, s * 0.28f, (0, Brass[0]), (0.5f, Brass[1]), (1, Brass[2]));
        Band(svg, new SvgPath().MoveTo(s * 0.2f, 0).CubicTo(s * 0.46f, -s * 0.08f, s * 0.46f, s * 0.2f, s * 0.22f, s * 0.15f), s * 0.055f, Brass[1], s);
        Outlined(svg, new SvgPath().MoveTo(-s * 0.14f, 0).QuadTo(-s * 0.32f, -s * 0.02f, -s * 0.47f, -s * 0.13f).LineTo(-s * 0.45f, -s * 0.07f)
            .QuadTo(-s * 0.32f, s * 0.1f, -s * 0.16f, s * 0.15f).Close(), brass, s);                                           // the spout
        Outlined(svg, SvgPath.Polygon(new[] { new Vector2(-s * 0.12f, s * 0.19f), new Vector2(s * 0.12f, s * 0.19f), new Vector2(s * 0.17f, s * 0.27f), new Vector2(-s * 0.17f, s * 0.27f) }), brass, s); // the foot
        Outlined(svg, new SvgPath().Ellipse(0, s * 0.07f, s * 0.27f, s * 0.14f), brass, s);                                     // the body
        Outlined(svg, EllipseArc(new SvgPath().MoveTo(-s * 0.13f, -s * 0.05f), 0, -s * 0.05f, s * 0.13f, s * 0.085f, Mathf.Pi, Tau).Close(), brass, s); // the lid
        Outlined(svg, SvgPath.Circle(0, -s * 0.165f, s * 0.035f), Brass[0], s, Out, o * 0.8f);                                  // its knob
        float rx = s * 0.2f, ry = s * 0.08f, cy = s * 0.07f, start = Mathf.Pi * 1.08f;                                            // a shine on the body
        svg.Stroke(EllipseArc(new SvgPath().MoveTo(rx * Mathf.Cos(start), cy + ry * Mathf.Sin(start)), 0, cy, rx, ry, start, Mathf.Pi * 1.45f),
            new Color(new Color("fff8dc"), 0.75f), Mathf.Max(1, s * 0.025f), "round", "round");
        SvgPath puff = new SvgPath().Arc(-s * 0.42f, -s * 0.27f, s * 0.06f, 0, Tau).Close()
            .MoveTo(-s * 0.235f, -s * 0.33f).Arc(-s * 0.31f, -s * 0.33f, s * 0.075f, 0, Tau).Close();
        svg.Stroke(puff, Out, o * 2);                                                                                            // two round puffs, outlined as one
        svg.Fill(puff, new Color("eef1f4"));
    }

    // Treasurers' safe: a steel box with brass corners, a round dial, a handle and hinges, on two feet. Twice as wide as
    // tall (Canvas), with the dial and handle faded to SafeFade so resources can go on top.
    public const float SafeFade = 0.2f;
    private static void Safe(Svg svg, float s)
    {
        float o = Ow(s), l = -s * 0.84f, r = s * 0.84f, t = -s * 0.42f, b = s * 0.34f;
        foreach (float x in new[] { -0.68f, 0.6f })
            Outlined(svg, new SvgPath().RoundRect(x * s, b - s * 0.02f, s * 0.08f, s * 0.08f, s * 0.015f), new Color("4a5056"), s, Out, o * 0.7f); // feet
        Outlined(svg, new SvgPath().RoundRect(l, t, r - l, b - t, s * 0.05f), Paint.Linear(l, t, r, b, (0, new Color("c9cfd4")), (1, new Color("7b838a"))), s);
        SvgPath Door() => new SvgPath().RoundRect(l + s * 0.08f, t + s * 0.08f, r - l - s * 0.16f, b - t - s * 0.16f, s * 0.03f);
        svg.Save();
        svg.Translate(o * 0.4f, o * 0.4f);
        svg.Stroke(Door(), new Color(1, 1, 1, 0.5f), o * 0.6f);
        svg.Restore();
        svg.Stroke(Door(), new Color("4e555b"), o * 0.6f);
        foreach (float y in new[] { -0.24f, 0.16f })
            Outlined(svg, new SvgPath().RoundRect(l + s * 0.045f, y * s, s * 0.05f, s * 0.09f, s * 0.01f), new Color("4e555b"), s, Out, o * 0.5f); // hinges
        float c = s * 0.11f;
        Paint brass = Paint.Linear(l, t, r, b, (0, new Color("f0d27a")), (1, new Color("a8812f")));
        foreach (var (x, y, dx, dy) in new[] { (l, t, 1, 1), (r, t, -1, 1), (r, b, -1, -1), (l, b, 1, -1) })
            Outlined(svg, SvgPath.Polygon(new[] { new Vector2(x, y), new Vector2(x + dx * c, y), new Vector2(x, y + dy * c) }), brass, s, Out, o * 0.6f);
        svg.Save();
        svg.Opacity(SafeFade);
        float cx = -s * 0.03f, cy = -s * 0.04f, dr = s * 0.15f;
        Outlined(svg, SvgPath.Circle(cx, cy, dr), Paint.Radial(cx - dr * 0.3f, cy - dr * 0.35f, cx, cy, dr, (0, new Color("f2f4f6")), (1, new Color("8e969d"))), s);
        var ticks = new SvgPath();
        for (var i = 0; i < 12; i++)
        {
            float a = i / 12f * Tau;
            ticks.MoveTo(new Vector2(cx, cy) + At(a, dr * 0.72f)).LineTo(new Vector2(cx, cy) + At(a, dr * 0.9f));
        }
        svg.Stroke(ticks, new Color("3a4046"), Mathf.Max(0.6f, o * 0.4f));
        Outlined(svg, SvgPath.Circle(cx, cy, dr * 0.3f), new Color("4e555b"), s, Out, o * 0.6f);
        Band(svg, new SvgPath().MoveTo(s * 0.22f, -s * 0.13f).LineTo(s * 0.22f, s * 0.05f), s * 0.05f, new Color("d9b45a"), s);   // the handle
        svg.Restore();
    }

    // Firewalkers' flame piece: an orange cylinder (as the pieces, seen from 30° up) with a flame sitting down in its top:
    // the flame's bottom edge is the front of the cylinder's rim, so the top itself is hidden.
    private static void FlamePiece(Svg svg, float s)
    {
        float rx = s * 0.19f, ry = rx * 0.5f, top = s * 0.04f, bottom = s * 0.36f;
        SvgPath body = EllipseArc(new SvgPath().MoveTo(-rx, top).LineTo(-rx, bottom), 0, bottom, rx, ry, Mathf.Pi, 0, anticlockwise: true)
            .LineTo(rx, top).Close();
        Outlined(svg, body, Paint.Linear(-rx, 0, rx, 0, (0, new Color("f9b062")), (0.45f, new Color("e9792e")), (1, new Color("b4521b"))), s);
        // the flame: a side tongue to the right, the main flame over it (down to the rim), a pale core
        Paint flame = Paint.Radial(0, top + s * 0.02f, 0, top - s * 0.14f, s * 0.36f, (0, new Color("fff8d8")), (0.45f, new Color("ffd34d")), (1, new Color("f28c1c")));
        Outlined(svg, new SvgPath().MoveTo(s * 0.25f, top - s * 0.31f).CubicTo(s * 0.28f, top - s * 0.18f, s * 0.25f, top - s * 0.06f, s * 0.16f, top + s * 0.01f)
            .LineTo(s * 0.1f, top - s * 0.12f).CubicTo(s * 0.17f, top - s * 0.15f, s * 0.23f, top - s * 0.22f, s * 0.25f, top - s * 0.31f).Close(), flame, s);
        SvgPath main = new SvgPath().MoveTo(-s * 0.02f, top - s * 0.46f).CubicTo(s * 0.08f, top - s * 0.32f, rx * 1.15f, top - s * 0.16f, rx, top);
        EllipseArc(main, 0, top, rx, ry, 0, Mathf.Pi);                                                       // the rim's front, round the bottom
        main.CubicTo(-rx * 1.2f, top - s * 0.14f, -s * 0.1f, top - s * 0.28f, -s * 0.02f, top - s * 0.46f).Close();
        Outlined(svg, main, flame, s);
        svg.Fill(new SvgPath().MoveTo(0, top - s * 0.2f).CubicTo(s * 0.07f, top - s * 0.1f, s * 0.09f, top - s * 0.01f, 0, top + s * 0.05f)
            .CubicTo(-s * 0.09f, top - s * 0.01f, -s * 0.06f, top - s * 0.1f, 0, top - s * 0.2f).Close(), new Color(new Color("fffceb"), 0.9f));
    }

    // The Terra Mystica symbol, for round tiles: gold with a thin dark edge (a third of the usual outline, as the symbol
    // is fine work), no background. A slate orb with a gold rim and 16 short spokes (the four diagonal ones longer), a
    // ball on top, a small ring below, and a crescent at each end, its round side towards the middle.
    public const float TerraMysticaSpoke = 0.165f, TerraMysticaLongSpoke = 0.2f; // how far the spokes reach, of s (from 0.125)
    private static void TerraMysticaSymbol(Svg svg, float s)
    {
        Color gold = new("d9b452");
        float o = Mathf.Max(0.75f, Ow(s) / 3);
        void Line(SvgPath path, float w)
        {
            svg.Stroke(path, Out, w + 2 * o, "round", "round");
            svg.Stroke(path, gold, w, "round", "round");
        }
        var spokes = new SvgPath();
        for (var i = 0; i < 16; i++)
        {
            float a = i / 16f * Tau, reach = i % 4 == 2 ? TerraMysticaLongSpoke : TerraMysticaSpoke; // i = 2, 6, 10, 14: 45°, 135°, ...
            spokes.MoveTo(At(a, s * 0.125f)).LineTo(At(a, s * reach));
        }
        Line(spokes, s * 0.026f);
        svg.Fill(SvgPath.Circle(0, 0, s * 0.08f), Paint.Radial(-s * 0.03f, -s * 0.035f, 0, 0, s * 0.09f, (0, new Color("6f879b")), (1, new Color("2f4053"))));
        Line(SvgPath.Circle(0, 0, s * 0.085f), s * 0.032f);
        Outlined(svg, SvgPath.Circle(0, -s * 0.215f, s * 0.042f), gold, s, Out, o);
        Line(SvgPath.Circle(0, s * 0.205f, s * 0.025f), s * 0.02f);
        // a crescent: a circle with an overlapping one cut away, the two arcs meeting at their crossing points; drawn
        // mirrored about its own circle's centre, so its horns point outwards
        float c1 = -s * 0.36f, r1 = s * 0.15f, c2 = -s * 0.3f, r2 = s * 0.125f, d = c2 - c1, a1 = (d * d + r1 * r1 - r2 * r2) / (2 * d), h = Mathf.Sqrt(r1 * r1 - a1 * a1);
        float angle1 = Mathf.Atan2(h, a1), angle2 = Mathf.Atan2(h, a1 - d);
        Paint moonGold = Paint.Linear(0, -r1, 0, r1, (0, new Color("f2d887")), (1, new Color("b38a2e")));
        foreach (int k in new[] { 1, -1 })
        {
            svg.Save();
            svg.Scale(k, 1);
            svg.Translate(c1, 0);
            svg.Scale(-1, 1);
            svg.Translate(-c1, 0);
            Outlined(svg, new SvgPath().Arc(c1, 0, r1, -angle1, angle1, anticlockwise: true).Arc(c2, 0, r2, angle2, -angle2).Close(), moonGold, s, Out, o);
            svg.Restore();
        }
    }

    // A rectangle with only its top corners rounded.
    private static SvgPath TopRoundRect(float x, float y, float w, float h, float r) =>
        new SvgPath().MoveTo(x, y + h).LineTo(x, y + r).Arc(x + r, y + r, r, Mathf.Pi, Mathf.Pi * 1.5f)
            .LineTo(x + w - r, y).Arc(x + w - r, y + r, r, Mathf.Pi * 1.5f, Tau).LineTo(x + w, y + h).Close();

    // Dragonlords' stronghold (power tokens to bowl I, one per player): a large power token with two figures seated at a
    // table over the whole token (the number of players), a black arrow from it down into a power bowl marked I. The
    // figures are drawn PlayersScale times their first size; the table's top is TableTop thick, its leg TableLeg wide,
    // from TableTopY down to TableBottomY (all of the token's size).
    public const float PlayersScale = 1.5f, TableTop = 0.07f, TableLeg = 0.1f, TableTopY = -0.02f, TableBottomY = 0.15f;
    private static void PlayersToBowl(Svg svg, float s)
    {
        // the bowl: as on the player panel (152 side, 26 corner, 6 edge in bowl I's purple, its light fill), scaled down
        float side = s * 0.4f, bx = s * 0.22f, by = s * 0.26f, k = side / 152;
        svg.Save();
        svg.Translate(bx - side / 2, by - side / 2);
        svg.Scale(k, k);
        svg.Fill(new SvgPath().RoundRect(3, 3, 146, 146, 23), new Color("e6e1e9"));
        svg.Stroke(new SvgPath().RoundRect(3, 3, 146, 146, 23), new Color("5b3a6d"), 6);
        svg.Stroke(new SvgPath().RoundRect(0, 0, 152, 152, 26), Out, 2);
        svg.Fill(SvgPath.Polygon(new Vector2[] { new(56, 36), new(96, 36), new(96, 46), new(84, 46), new(84, 106), new(96, 106), new(96, 116),
            new(56, 116), new(56, 106), new(68, 106), new(68, 46), new(56, 46) }), new Color("5b3a6d"));              // the numeral I, with serifs
        svg.Restore();
        // the token, the arrow from its right side into the bowl, the players over the token
        float u = s * 0.6f, tx = -s * 0.14f, ty = -s * 0.18f;
        svg.Save();
        svg.Translate(tx, ty);
        Solid(svg, PieceArt.PictureFor(PieceArt.Prop.PowerToken), u, Purple);
        svg.Restore();
        CurveArrow(svg, new Vector2(s * 0.1f, -s * 0.14f), new Vector2(s * 0.24f, -s * 0.14f), new Vector2(bx, by - side / 2 + s * 0.07f),
            ArrowBlack, ArrowEdge, s * 0.09f, s * 0.13f, s * 0.1f, Ow(s) * 0.5f);
        svg.Save();
        svg.Translate(tx, ty - u * 0.04f);
        Opponents(svg, s, u);
        svg.Restore();
    }

    // The opponents symbol: two figures seated at a table, centred on (0, 0), sized for a power token of size u
    // (the Dragonlords' stronghold; the Prospectors' too).
    private static void Opponents(Svg svg, float s, float u)
    {
        svg.Save();
        svg.Scale(PlayersScale, PlayersScale);
        float o = Mathf.Max(0.75f, Ow(s) * 0.55f) / PlayersScale;
        foreach (int dir in new[] { -1, 1 })
        {
            Outlined(svg, SvgPath.Circle(dir * u * 0.21f, -u * 0.1f, u * 0.07f), White, s, Out, o);                          // head
            Outlined(svg, TopRoundRect(dir > 0 ? u * 0.13f : -u * 0.29f, -u * 0.01f, u * 0.16f, u * 0.13f, u * 0.06f), White, s, Out, o); // body
        }
        float leg = TableLeg / 2;
        Outlined(svg, SvgPath.Polygon(new[] { new Vector2(-0.1f, TableTopY), new Vector2(0.1f, TableTopY), new Vector2(0.1f, TableTopY + TableTop),
            new Vector2(leg, TableTopY + TableTop), new Vector2(leg, TableBottomY), new Vector2(-leg, TableBottomY), new Vector2(-leg, TableTopY + TableTop),
            new Vector2(-0.1f, TableTopY + TableTop) }.Select(p => p * u)), White, s, Out, o);                                // the table
        svg.Restore();
    }

    // Acolytes' stronghold: a priest, and a black arrow into the middle of a priest zone (neutral grey, no numbers),
    // drawn as CultArt draws the zones (214 × 282 units): dark, the slots tinted towards the grey (CultArt.DarkCell).
    private static void PriestToZone(Svg svg, float s)
    {
        const float pad = 12, slot = 92, gap = 6, small = 72, smallGap = -4, radius = 22, line = 6, slotLine = 3, w = 214, h = 282;
        Color edge = CultArt.Neutral;
        float k = s * 0.8f / h, zx = s * 0.16f, zy = s * 0.08f;
        svg.Save();
        svg.Translate(zx - w * k / 2, zy - h * k / 2);
        svg.Scale(k, k);
        Color slotFill = CultArt.SlotFill(edge);
        CultArt.DarkCell(svg, new Rect2(0, 0, w, h), radius, edge, line, CultArt.Base);
        for (var i = 0; i < 4; i++)
            CultArt.DarkCell(svg, new Rect2(pad + i % 2 * (slot + gap), pad + i / 2 * (slot + gap), slot, slot), slot / 2, edge, slotLine, slotFill);
        CultArt.DarkCell(svg, new Rect2(w / 2 - small / 2, pad + 2 * slot + gap + smallGap, small, small), small / 2, edge, slotLine, slotFill);
        svg.Stroke(new SvgPath().RoundRect(0, 0, w, h, radius), Out, 3);
        svg.Restore();
        CurveArrow(svg, new Vector2(-s * 0.2f, -s * 0.12f), new Vector2(-s * 0.08f, -s * 0.06f), new Vector2(zx, zy - s * 0.05f),
            ArrowBlack, ArrowEdge, s * 0.09f, s * 0.13f, s * 0.1f, Ow(s) * 0.5f);
        svg.Save();
        svg.Translate(-s * 0.3f, -s * 0.25f);
        Priest(svg, s * 0.5f, White);
        svg.Restore();
    }

    // Shapeshifters' shift: an action octagon whose middle (inside its inner line) is the colour wheel as a smooth sweep
    // of the seven terrain colours, clockwise from grey at the top (the wheel's order), mixing towards the middle.
    // WheelWedges thin wedges, each overlapping the next a little so no seams show.
    private const int WheelWedges = 168;
    private static void ShiftAction(Svg svg, float s)
    {
        Octagon(svg, s, used: false);
        float r = s * 0.48f * 0.86f;
        Color[] colours = Enumerable.Range(0, 7).Select(i => HexArt.TerrainColour(Terrains.Wheel[(i + 6) % 7])).ToArray(); // grey first
        Color At01(float t)
        {
            float f = t * 7;
            int i = Mathf.FloorToInt(f) % 7;
            return colours[i].Lerp(colours[(i + 1) % 7], f - Mathf.Floor(f));
        }
        svg.Save();
        svg.Clip(OctagonPath(r));
        for (var i = 0; i < WheelWedges; i++)
        {
            float a0 = -Mathf.Pi / 2 + (i - 0.5f) / WheelWedges * Tau - Tau / 14, a1 = a0 + 1.6f / WheelWedges * Tau;
            svg.Fill(new SvgPath().MoveTo(0, 0).Arc(0, 0, r * 1.2f, a0, a1).Close(), At01((i + 0.5f) / WheelWedges));
        }
        var mix = new Color(120 / 255f, 118 / 255f, 112 / 255f);
        svg.Fill(new SvgPath().Rect(-r, -r, 2 * r, 2 * r), Paint.Radial(0, 0, 0, 0, r * 0.6f, (0, new Color(mix, 0.75f)), (1, new Color(mix, 0))));
        svg.Restore();
        svg.Stroke(OctagonPath(r), new Color("b26f2a"), Ow(s) * 0.8f);
    }

    // Yetis' stronghold (their power actions can be taken again): a small power action tile, an infinity sign below,
    // black outlined in white like the building board's text.
    private static void PowerActionForever(Svg svg, float s)
    {
        float w = s * 0.78f, h = w * 127 / 247;
        svg.Save();
        svg.Translate(-w / 2, -s * 0.2f - h / 2);
        svg.Scale(w / 247, h / 127);
        PowerAction(svg, 1.4f * 247 / w); // its lines 1.4 px wide on screen, as on the full-size tile
        svg.Restore();
        float a = s * 0.36f, cy = s * 0.24f;
        var infinity = new SvgPath();
        for (var i = 0; i <= 96; i++)
        {
            float t = i / 96f * Tau, d = 1 + Mathf.Sin(t) * Mathf.Sin(t);
            infinity.LineTo(a * Mathf.Cos(t) / d, cy + a * Mathf.Sin(t) * Mathf.Cos(t) / d * 1.25f);
        }
        Band(svg, infinity.Close(), s * 0.06f, new Color("141414"), s, line: Colors.White);
    }

    // Ice Maidens' stronghold (when passing, VP per temple on the map): a map hex in the ice colour with a temple on it,
    // in their colour (pieces) or white.
    private static void TempleOnIce(Svg svg, float s, Color pieces)
    {
        float r = s * 0.5f;
        MapHex(svg, s, Vector2.Zero, r * 0.97f, HexArt.TerrainColour(Terrain.Ice));
        PlacedBuilding(svg, PieceKind.Temple, 0, r * 0.04f, r * 0.62f, pieces);
    }

    // A power token removed for the rest of the game (the Dragonlords' price for Volcano): the token with the white
    // cross over it.
    private static void TokenRemoved(Svg svg, float s)
    {
        PowerToken(svg, s);
        PowerCross(svg, s);
    }
}
