using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// IconArt, continued: endgame scoring, the symbols on tiles, and the income hand.
public static partial class IconArt
{
    // ---------- Endgame scoring ----------

    // Cult scoring: the four cult discs in a square, fire and water above, earth and air below.
    private static void CultScoring(Svg svg, float s)
    {
        foreach (var (cult, x, y) in new[] { (Track.Fire, -1, -1), (Track.Water, 1, -1), (Track.Earth, -1, 1), (Track.Air, 1, 1) })
        {
            svg.Save();
            svg.Translate(x * s * 0.235f, y * s * 0.235f);
            CultDisc(svg, s * 0.46f, cult);
            svg.Restore();
        }
    }

    // Network: a stronghold, a trading post, a sanctuary and a dwelling joined across a river (blue hexes, no outline),
    // on the map's grid fitted to the icon as the town icon is. The big two buildings are drawn smaller, to fit.
    private static void Network(Svg svg, float s)
    {
        var cells = new (int Column, int Row, PieceKind? Kind)[]
        {
            (0, 0, PieceKind.Stronghold), (1, 0, null), (0, 1, null), (1, 1, PieceKind.TradingPost), (0, 2, null), (1, 2, PieceKind.Sanctuary), (1, 3, PieceKind.Dwelling),
        }; // null: river
        Vector2 Centre(int c, int r) => new(Mathf.Sqrt(3) * (c + 0.5f * (r & 1)), 1.5f * r); // in hex radii
        List<Vector2> centres = cells.Select(c => Centre(c.Column, c.Row)).ToList();
        float minX = centres.Min(p => p.X), maxX = centres.Max(p => p.X), minY = centres.Min(p => p.Y), maxY = centres.Max(p => p.Y);
        float radius = s * 0.94f / Mathf.Max(maxX - minX + Mathf.Sqrt(3), maxY - minY + 2);
        var middle = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        Vector2 Place(int c, int r) => (Centre(c, r) - middle) * radius;
        foreach (var cell in cells.Where(c => c.Kind is null))
            svg.Fill(SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius + 0.5f)), new Color("9ccbe6"));
        foreach (var cell in cells.Where(c => c.Kind is not null))
            Outlined(svg, SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius * 0.97f)), MapGrey, s, Out, Mathf.Max(1, radius * 0.09f));
        foreach (var cell in cells.Where(c => c.Kind is not null))
        {
            Vector2 p = Place(cell.Column, cell.Row);
            bool big = cell.Kind is PieceKind.Stronghold or PieceKind.Sanctuary;
            PlacedBuilding(svg, cell.Kind.Value, p.X, p.Y, radius * (big ? 0.6f : 0.75f), White);
        }
    }

    // The scoring icons' outline width (only round the outside of each group of hexes).
    private static float ScoringLine(float s) => Mathf.Max(1, s * 0.0104f);

    // Hexes that touch, drawn as one grey shape outlined only round its outside (no edges between them): every hex
    // outlined twice `line` wide first, then all filled over that (a shade bigger, to close the seams), so only the
    // outline's outer half shows.
    private static void HexGroup(Svg svg, IEnumerable<(Vector2 Centre, float Radius)> hexes, float line)
    {
        var list = hexes.ToList();
        foreach (var (centre, radius) in list)
            svg.Stroke(SvgPath.Polygon(HexCorners(centre, radius)), Out, 2 * line);
        foreach (var (centre, radius) in list)
            svg.Fill(SvgPath.Polygon(HexCorners(centre, radius + 0.3f)), MapGrey);
    }

    // A chain of hexes bending out to the left, as on the distance scoring tiles: from a hex above (centre a, centre to
    // corner ra), n hexes (radius r) run down-left flat side to flat side to a middle hex cut across by a wavy black
    // line (however far), then n more run down-right to the same size of hex below, mirrored. All one HexGroup, the
    // hexes at both ends included. Returns that lower hex's centre; the caller draws what stands on the ends.
    private static Vector2 BrokenChain(Svg svg, float s, Vector2 a, float ra, float r, int n)
    {
        float Apothem(float k) => k * Mathf.Sqrt(3) / 2;
        var d = new Vector2(-0.5f, Mathf.Sqrt(3) / 2);
        float reach = Apothem(ra) + Apothem(r) + n * 2 * Apothem(r);
        Vector2 middle = a + d * reach, b = new(a.X, a.Y + 2 * d.Y * reach);
        var hexes = new List<(Vector2, float)> { (a, ra), (b, ra), (middle, r) };
        for (var i = 0; i < n; i++)
        {
            float k = Apothem(ra) + Apothem(r) + i * 2 * Apothem(r);
            hexes.Add((a + d * k, r));
            hexes.Add((new Vector2(b.X + d.X * k, b.Y - d.Y * k), r));
        }
        HexGroup(svg, hexes, ScoringLine(s));
        // the break: a wave right across the middle hex (its left and right sides are both on the outside), cut off at
        // its edge
        float w = Apothem(r) * 2, x0 = middle.X - w / 2, y = middle.Y, amp = r * 0.22f;
        SvgPath wave = new SvgPath().MoveTo(x0 - r * 0.2f, y).CubicTo(x0 + w * 0.2f, y - 2 * amp, x0 + w * 0.3f, y + 2 * amp, x0 + w * 0.5f, y)
            .CubicTo(x0 + w * 0.7f, y - 2 * amp, x0 + w * 0.8f, y + 2 * amp, x0 + w + r * 0.2f, y);
        svg.Save();
        svg.Clip(SvgPath.Polygon(HexCorners(middle, r)));
        svg.Stroke(wave, Out, Mathf.Max(1.2f, r * 0.2f));
        svg.Restore();
        return b;
    }

    // Every building in a slanting heap, the dwelling at the front (lower left), the stronghold at the back (upper
    // right), a dwelling's length `unit`.
    private static void BuildingHeap(Svg svg, float x, float y, float unit)
    {
        PieceKind[] order = { PieceKind.Stronghold, PieceKind.Sanctuary, PieceKind.Temple, PieceKind.TradingPost, PieceKind.Dwelling };
        for (var i = 0; i < order.Length; i++)
        {
            int j = 2 - i;
            PlacedBuilding(svg, order[i], x + j * unit * 0.62f, y - j * unit * 0.36f, unit, White);
        }
    }

    // The four scoring icons are drawn where convenient and then centred on what they draw (CentredOnDrawing).

    // Fire & Ice: the distance between the stronghold and the sanctuary (one hex each side of the broken middle).
    private static void ScoringStrongholdSanctuary(Svg svg, float s)
    {
        float big = s * 0.13f;
        var a = new Vector2(s * 0.14f, -s * 0.323f);
        Vector2 b = BrokenChain(svg, s, a, big, s * 0.1f, 1);
        PlacedBuilding(svg, PieceKind.Sanctuary, a.X, a.Y, big * 0.55f, White);
        PlacedBuilding(svg, PieceKind.Stronghold, b.X, b.Y, big * 0.55f, White);
    }

    // Fire & Ice: buildings at the edge of the map (every building, inside a frame of hexes round three sides).
    private static void ScoringEdge(Svg svg, float s)
    {
        float r = s * 0.058f, step = Mathf.Sqrt(3) * r, right = s * 0.36f, top = -s * 0.39f, bottom = s * 0.39f;
        var cells = new List<(Vector2, float)>();
        for (var k = 0; k < 6; k++)
        {
            float x = right - step / 2 - k * step;
            cells.Add((new Vector2(x, top), r));
            cells.Add((new Vector2(x, bottom), r));
        }
        var row = 1;
        for (float y = top + 1.5f * r; y < bottom - 0.5f * r; row++, y += 1.5f * r)
            cells.Add((new Vector2(right - (row % 2 == 1 ? 0 : step / 2), y), r));
        HexGroup(svg, cells, ScoringLine(s));
        BuildingHeap(svg, -s * 0.06f, s * 0.03f, s * 0.14f);
    }

    // Fire & Ice: clusters (separate groups of buildings): a pair, one alone, and a chain of three.
    private static void ScoringClusters(Svg svg, float s)
    {
        float r = s * 0.115f, h = Mathf.Sqrt(3) * r, u = r * 0.6f;
        Vector2 pair = new(-s * 0.02f, -s * 0.36f), alone = new(-s * 0.25f, 0), chain = new(s * 0.17f, -s * 0.04f);
        (Vector2 At, PieceKind Kind)[] groupOfTwo = { (pair, PieceKind.TradingPost), (pair + new Vector2(h, 0), PieceKind.Dwelling) };
        (Vector2 At, PieceKind Kind)[] groupOfOne = { (alone, PieceKind.Dwelling) };
        (Vector2 At, PieceKind Kind)[] groupOfThree =
        {
            (chain, PieceKind.Stronghold), (chain + new Vector2(h / 2, 1.5f * r), PieceKind.TradingPost),
            (chain + new Vector2(0, 3 * r), PieceKind.Temple),
        };
        var groups = new[] { groupOfTwo, groupOfOne, groupOfThree };
        foreach (var group in groups)
            HexGroup(svg, group.Select(cell => (cell.At, r)), ScoringLine(s));
        foreach (var cell in groups.SelectMany(group => group))
            PlacedBuilding(svg, cell.Kind, cell.At.X, cell.At.Y, u, White);
    }

    // Fire & Ice: the greatest distance between two buildings: a heap of buildings at each end of a broken chain (one
    // hex each side of the broken middle; the ends' hexes the same size).
    private static void ScoringDistance(Svg svg, float s)
    {
        float r = s * 0.0975f;
        var a = new Vector2(s * 0.16f, -s * 0.315f);
        Vector2 b = BrokenChain(svg, s, a, r, r, 1);
        BuildingHeap(svg, a.X - s * 0.05f, a.Y - s * 0.02f, s * 0.1f);
        BuildingHeap(svg, b.X - s * 0.05f, b.Y - s * 0.02f, s * 0.1f);
    }

    // ---------- Tile symbols ----------

    // "When passing": two rolled scrolls tied together, then a colon.
    private static void OnPass(Svg svg, float s)
    {
        svg.Save();
        svg.Translate(s * 0.06f, 0);
        foreach (float x in new[] { -0.31f, -0.15f })
        {
            float cx = x * s, w = s * 0.16f, top = -s * 0.3f, bottom = s * 0.32f;
            Outlined(svg, new SvgPath().RoundRect(cx - w / 2, top, w, bottom - top, w * 0.3f),
                Paint.Linear(cx - w / 2, 0, cx + w / 2, 0, (0, new Color("c9b07a")), (0.45f, new Color("efdfb0")), (1, new Color("b0955e"))), s);
            Outlined(svg, new SvgPath().Ellipse(cx, top + w * 0.25f, w / 2, w * 0.28f), new Color("e6d4a0"), s, Out, Ow(s) * 0.7f); // the scroll's end
            var spiral = new SvgPath();
            for (var i = 0; i <= 24; i++)
            {
                float t = i / 24f * 3 * Mathf.Pi, rr = w * 0.05f + w * 0.045f * t;
                spiral.LineTo(cx + Mathf.Cos(t) * rr, top + w * 0.25f + Mathf.Sin(t) * rr * 0.55f);
            }
            svg.Stroke(spiral, new Color("8a7444"), Mathf.Max(0.6f, Ow(s) * 0.45f));
        }
        Band(svg, new SvgPath().MoveTo(-s * 0.4f, s * 0.05f).LineTo(-s * 0.06f, s * 0.05f), s * 0.05f, new Color("9b3b22"), s, "butt"); // the cord
        Outlined(svg, SvgPath.Circle(-s * 0.23f, s * 0.05f, s * 0.045f), new Color("e2b84a"), s, Out, Ow(s) * 0.7f);                     // its knot
        foreach (float y in new[] { -0.1f, 0.12f }) // the colon
            Outlined(svg, SvgPath.Circle(s * 0.09f, y * s, s * 0.055f), new Color("2a2a2a"), s, new Color("f3efe4"), Ow(s) * 0.6f);
        svg.Restore();
    }

    // How high each building sits in a "when built" icon, so it covers the arrow's tail.
    private static readonly Dictionary<PieceKind, float> BuiltAt = new()
    {
        [PieceKind.Dwelling] = -0.11f, [PieceKind.TradingPost] = -0.15f, [PieceKind.Temple] = -0.1f,
        [PieceKind.Sanctuary] = -0.2f, [PieceKind.Stronghold] = -0.21f,
    };

    // "When this is built": the building above, a black arrow curving down onto a ground plate. kind null: the two
    // big buildings (sanctuary behind, stronghold in front). pieces: the buildings' and the plate's colour (e.g. the
    // Witches' green on their stronghold action); null: white buildings on a sand plate.
    private static void WhenBuilt(Svg svg, float s, PieceKind? kind, Color? pieces = null)
    {
        float u = s * 0.3f; // a dwelling's length
        svg.Save();
        svg.Translate(s * 0.02f, s * 0.34f);
        Solid(svg, PieceArt.PictureFor(PieceArt.Prop.Plate), 0, pieces ?? new Color("e3d3a5"), shadow: false, unit: s * 0.62f);
        svg.Restore();
        // the arrow goes underneath: from behind the building, almost straight down onto the plate
        CurveArrow(svg, new Vector2(-s * 0.04f, -s * 0.06f), new Vector2(s * 0.04f, s * 0.1f), new Vector2(s * 0.05f, s * 0.3f),
            new Color("1d1d1d"), new Color("f3efe4"), s * 0.11f, s * 0.17f, s * 0.12f, Ow(s) * 0.5f);
        void Building(PieceKind building, float x, float y, float unit)
        {
            svg.Save();
            svg.Translate(x, y);
            Solid(svg, PieceArt.PictureFor(building), 0, pieces ?? White, shadow: false, unit: unit);
            svg.Restore();
        }
        if (kind is PieceKind one)
            Building(one, -s * 0.06f, BuiltAt[one] * s, u);
        else
        {
            Building(PieceKind.Sanctuary, s * 0.08f, -s * 0.2f, u * 0.72f);
            Building(PieceKind.Stronghold, -s * 0.12f, -s * 0.08f, u * 0.72f);
        }
    }

    // A pointy-topped hexagon's corners.
    private static IEnumerable<Vector2> HexCorners(Vector2 centre, float radius) =>
        Enumerable.Range(0, 6).Select(i => centre + At(-Mathf.Pi / 2 + i * Mathf.Pi / 3, radius));

    // A town (for the fire favour): grey hexes, white buildings. A dwelling, a bridge over the river to a trading
    // post, then another trading post below-left of it and a dwelling below-right of that. Hexes in rows, every
    // other row shifted half a hex.
    private static void Town(Svg svg, float s)
    {
        var cells = new (int Column, int Row, PieceKind? Kind)[]
        {
            (0, 0, PieceKind.Dwelling), (1, 0, null), (0, 1, null), (1, 1, PieceKind.TradingPost), (1, 2, PieceKind.TradingPost), (1, 3, PieceKind.Dwelling),
        }; // null: river
        Vector2 Centre(int c, int r) => new(Mathf.Sqrt(3) * (c + 0.5f * (r & 1)), 1.5f * r); // in hex radii
        List<Vector2> centres = cells.Select(c => Centre(c.Column, c.Row)).ToList();
        float minX = centres.Min(p => p.X), maxX = centres.Max(p => p.X), minY = centres.Min(p => p.Y), maxY = centres.Max(p => p.Y);
        float radius = s * 0.94f / Mathf.Max(maxX - minX + Mathf.Sqrt(3), maxY - minY + 2);
        var middle = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        Vector2 Place(int c, int r) => (Centre(c, r) - middle) * radius;
        float line = Mathf.Max(1, radius * 0.09f);

        foreach (var cell in cells.Where(c => c.Kind is null))
            svg.Fill(SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius + 0.5f)), new Color("9ccbe6"));
        foreach (var cell in cells.Where(c => c.Kind is not null))
            Outlined(svg, SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), radius * 0.97f)), new Color("a1a8ae"), s, Out, line);
        // the bridge: from the first dwelling's corner to the trading post's facing corner, a little short of each
        Vector2 a = Place(0, 0), b = Place(1, 1);
        Vector2 Nearest(Vector2 centre, Vector2 towards) => HexCorners(centre, radius).OrderBy(p => p.DistanceTo(towards)).First();
        Vector2 p0 = Nearest(a, b), p1 = Nearest(b, a), q0 = p0.Lerp(p1, 0.14f), q1 = p1.Lerp(p0, 0.14f);
        Vector2 along = (q1 - q0).Normalized(), across = new Vector2(-along.Y, along.X) * radius * 0.13f;
        Outlined(svg, SvgPath.Polygon(new[] { q0 + across, q1 + across, q1 - across, q0 - across }), new Color("e9e7e1"), s, Out, line);
        foreach (var cell in cells.Where(c => c.Kind is not null))
        {
            Vector2 p = Place(cell.Column, cell.Row);
            svg.Save();
            svg.Translate(p.X, p.Y);
            Solid(svg, PieceArt.PictureFor(cell.Kind.Value), 0, White, shadow: false, unit: radius * 0.62f);
            svg.Restore();
        }
    }

    // The tall brace on round tiles, between what is counted and the reward.
    private static void Brace(Svg svg, float s)
    {
        float h = s * 0.47f, w = s * 0.12f;
        SvgPath brace = new SvgPath().MoveTo(-w, -h).CubicTo(0, -h, 0, -h * 0.9f, 0, -h * 0.6f).LineTo(0, -h * 0.15f).CubicTo(0, -h * 0.03f, w * 0.4f, 0, w, 0)
            .CubicTo(w * 0.4f, 0, 0, h * 0.03f, 0, h * 0.15f).LineTo(0, h * 0.6f).CubicTo(0, h * 0.9f, 0, h, -w, h);
        Band(svg, brace, s * 0.035f, new Color("d3dbe0"), s * 0.6f);
    }

    // The thin rule across a pass tile, between its bonus action and its income, ending in diamonds.
    // upright: turned 90°. white: white, with a very thin grey line round it.
    private static void PassRule(Svg svg, float s, bool upright, bool white)
    {
        float x = s * 0.44f, t = Mathf.Max(1, s * 0.02f) / 2, d = Mathf.Max(2, s * 0.045f); // half length, half thickness, diamond size
        var shapes = new List<SvgPath> { SvgPath.Polygon(new Vector2[] { new(-x, -t), new(x, -t), new(x, t), new(-x, t) }) };
        foreach (float px in new[] { -x, x }) // each end: a small square turned 45°
            shapes.Add(SvgPath.Polygon(new Vector2[] { new(px - d, 0), new(px, -d), new(px + d, 0), new(px, d) }));
        svg.Save();
        if (upright)
            svg.Rotate(Mathf.Pi / 2);
        if (white) // half of this line ends up outside
            foreach (SvgPath shape in shapes)
                svg.Stroke(shape, new Color("8c9298"), Mathf.Max(0.6f, s * 0.012f) * 2);
        foreach (SvgPath shape in shapes)
            svg.Fill(shape, white ? Colors.White : new Color("9c875a"));
        svg.Restore();
    }

    // ---------- Income: an open hand, palm up, with the icon resting on it ----------
    // The hand, from an SVG drawing (342 × 125): HandLines is its path, every line in it;
    // HandOutline is the outside edge of those lines, worked out once from them, used to fill the hand.

    private const string HandLines = "M153 85.556L222 69.056L236 67.556L243 65.556L249 59.556L252 52.556L251.25 49.056M251.25 49.056L250.5 45.556L246 42.178H236M251.25 49.056L293.5 13.056M293.5 13.056C293.5 13.056 295.524 11.3576 297 10.556C300.668 8.56402 303.326 8.05597 307.5 8.05593C309.5 8.05591 314.723 7.4753 317.5 10.056C319.548 11.9591 320 14.5559 320 17.056C320 19.5561 259.5 77.056 259.5 77.056L203 117.056H29.5L25.5 115.056L22 106.056V91.556L24 82.056L29.5 74.056L57.5 69.056L103 42.556L123.5 40.056L165 45.056L195 43.5203M293.5 13.056C293.5 13.056 286.952 10.6795 283 10.556C278.68 10.421 272.5 14.056 272.5 14.056L265 19.556M236 42.178L255 27.556L265 19.556M236 42.178L219 42.556M265 19.556C265 19.556 253.252 17.058 248.5 19.056C243.748 21.054 236.5 27.056 236.5 27.056M219 42.556L236.5 27.056M219 42.556L195 43.5203M236.5 27.056C236.5 27.056 225.914 25.9193 218.5 28.056C212.222 29.8655 195 43.5203 195 43.5203";

    private static readonly Vector2[] HandOutline =
    {
        new(202.88f, 117.56f), new(29.5f, 117.56f), new(25.06f, 115.38f), new(21.44f, 106.25f), new(21.44f, 91.25f), new(23.44f, 81.88f),
        new(29.12f, 73.56f), new(57.25f, 68.56f), new(103.0f, 41.94f), new(124.5f, 39.56f), new(164.0f, 44.44f), new(194.25f, 43.06f),
        new(207.25f, 33.69f), new(216.38f, 28.19f), new(219.88f, 27.06f), new(225.12f, 26.31f), new(236.38f, 26.44f), new(243.38f, 21.19f),
        new(249.12f, 18.19f), new(254.88f, 17.69f), new(264.88f, 18.94f), new(272.75f, 13.19f), new(281.12f, 10.06f), new(285.25f, 10.19f),
        new(293.38f, 12.44f), new(296.62f, 10.06f), new(301.0f, 8.19f), new(304.5f, 7.56f), new(311.88f, 7.44f), new(315.75f, 8.31f),
        new(318.31f, 10.12f), new(319.69f, 12.38f), new(320.44f, 15.75f), new(320.44f, 17.12f), new(318.69f, 19.5f), new(297.12f, 41.44f),
        new(259.75f, 77.44f),
    };

    // The hand, HandWidth × s across, centred `y` below the middle. Its lines are as thick as they always were
    // (when the hand was drawn 0.94 of a bigger icon).
    private const float HandWidth = 0.94f / 0.66f;
    // Under the resource the hand's top is at most this far above its centre (the fingers, on the right), of s.
    private const float HandTop = 0.149f * HandWidth;

    private static void Hand(Svg svg, float s, float y)
    {
        float k = HandWidth * s / 299; // the hand is 299 units wide in the SVG
        float lines = s / 0.66f;      // the size its lines were worked out for
        svg.Save();
        svg.Translate(0, y);
        svg.Scale(k, k);
        svg.Translate(-171, -62.5f);
        SvgPath outline = SvgPath.Polygon(HandOutline);
        svg.Fill(outline, Paint.Linear(0, 8, 0, 118, (0, new Color("f2dcae")), (1, new Color("c2995f"))));
        // inner lines (thumb, fingers) thin and soft; then the outside edge over them, full width
        svg.Stroke(new SvgPath().Data(HandLines), new Color("5f3e1e", 0.8f), Ow(lines) * 0.55f / k, "round", "round");
        svg.Stroke(outline, Out, Ow(lines) / k, "round", "round");
        svg.Restore();
    }

    // An income icon: the resource at full size in the middle, the hand below it, HandGap (of s) clear of where a
    // resource's bottom reaches (ResourceBottom). The hand is in the same place on every income icon, so hands line
    // up in a row (the power token, smaller than most, sits a little higher above its hand).
    private const float HandGap = 0.1f;
    private const float ResourceBottom = 0.44f;

    private static Action<Svg, float> Income(Action<Svg, float> draw) => (svg, s) =>
    {
        draw(svg, s);
        Hand(svg, s, s * HandBelow);
    };

    private const float HandBelow = ResourceBottom + HandGap + HandTop; // the hand's centre below the resource's, of s

    // Where the hand's centre is below a resource's centre on an income icon `size` pixels (its resource's
    // size), in pixels: where to put Icon.IncomeHand to stand in for the hands of a whole row.
    public static float IncomeHandBelow(int size) => size * 6f / 7f * HandBelow;
}
