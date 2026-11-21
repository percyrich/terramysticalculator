using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// IconArt, continued: the everyday icons: pieces, cults, resources, power, actions and arrows.
public static partial class IconArt
{
    // ---------- Pieces (from PieceArt) ----------

    // A piece centred on (0, 0): `size` is how wide or tall its outline is at most, or,
    // if `unit` is given, a dwelling's length in pixels.
    // outline: how many times the usual thickness the outside line is. outlineWidth: the outside line this wide
    // instead (the crease lines keep their usual width). edgeMix: how far the crease lines go from the piece's colour
    // towards the outline's (0.3 usually; higher reads darker).
    internal static void Solid(Svg svg, PieceArt.Picture picture, float size, Color colour, bool shadow = true, float unit = 0f,
        float outline = 1f, float? outlineWidth = null, float edgeMix = 0.3f)
    {
        float longest = Mathf.Max(picture.Bounds.Size.X, picture.Bounds.Size.Y);
        float scale = unit > 0 ? unit / PieceArt.Unit : size / longest, extent = longest * scale, usual = Mathf.Max(1.1f, extent * 0.035f) * outline;
        Piece(svg, picture, scale, colour, outlineWidth ?? usual, shadow ? new Vector2(0.04f, 0.03f) * extent : null,
            Mathf.Max(0.5f, usual * 0.35f), edgeMix);
    }

    // The priest: the figure (not counting its thickness) 0.82 of s tall. Its outside line
    // is twice the usual thickness, as the worker's (both are 0.82 of s, so the lines match).
    internal static void Priest(Svg svg, float s, Color colour, bool shadow = true)
    {
        float height = s * 0.82f;
        Piece(svg, PieceArt.PictureFor(PieceKind.Priest), height / PieceArt.PriestHeight, colour,
            Mathf.Max(1.1f, height * 0.035f) * 2f, shadow ? new Vector2(0.04f, 0.03f) * height : null);
    }

    // A piece's picture centred on (0, 0) at `scale`: its shadow (moved by shadowShift; none if null), then the
    // outline (every face filled in the outline colour with a line twice the outline's width round it, so half of
    // that shows round the outside of the whole piece), then the faces with their thin crease lines.
    private static void Piece(Svg svg, PieceArt.Picture picture, float scale, Color colour, float outlineWidth, Vector2? shadowShift,
        float? creaseWidth = null, float edgeMix = 0.3f)
    {
        Vector2 middle = picture.Bounds.GetCenter();
        Vector2 Place(Vector2 p) => (p - middle) * scale;
        if (shadowShift is Vector2 shift)
            svg.Fill(SvgPath.Polygon(picture.Base.Select(p => Place(p) + shift)), new Color(0, 0, 0, 0.3f));

        var faces = picture.Faces.Select(f => (Path: SvgPath.Polygon(f.Points.Select(Place)), f.Points, f.Lit, f.Hard)).ToList();
        foreach (var face in faces)
        {
            svg.Fill(face.Path, Out);
            svg.Stroke(face.Path, Out, outlineWidth * 2);
        }
        Color edge = Out.Lerp(colour, 1 - edgeMix); // the crease lines: close to the piece's own colour
        foreach (var face in faces)
        {
            Color fill = PieceArt.FaceColour(colour, face.Lit);
            svg.Fill(face.Path, fill);
            svg.Stroke(face.Path, fill, 1.2f); // softens the face's edge, so no gaps show between faces
            var creases = new SvgPath();
            bool any = false;
            for (var i = 0; i < face.Points.Length; i++)
                if (face.Hard[i])
                {
                    creases.MoveTo(Place(face.Points[i])).LineTo(Place(face.Points[(i + 1) % face.Points.Length]));
                    any = true;
                }
            if (any)
                svg.Stroke(creases, edge, creaseWidth ?? Mathf.Max(0.5f, outlineWidth * 0.35f), "round", "round");
        }
    }

    // The worker's outside line is twice the usual thickness (white on white otherwise reads weakly).
    private static void Worker(Svg svg, float s) =>
        Solid(svg, PieceArt.PictureFor(PieceArt.Prop.Worker), s * 0.82f, new Color("f3f3f1"), outline: 2f);

    // ---------- Cults ----------

    private static readonly Dictionary<Track, (Color Ring, Color Core, Color Detail)> Cults = new()
    {
        [Track.Fire] = (new Color("a83a1f"), new Color("e8772a"), new Color("f0a050")),
        [Track.Water] = (new Color("1f5aa6"), new Color("3d8ad6"), new Color("9fd0ff")),
        [Track.Earth] = (new Color("5e3f1d"), new Color("8c6436"), new Color("33220f")),
        [Track.Air] = (new Color("8193a6"), new Color("c8d5e1"), new Color("f4f8fb")),
    };

    // A disc in the track's colours: a patterned ring round a softly lit middle.
    private static void CultDisc(Svg svg, float s, Track cult)
    {
        var (ring, core, detail) = Cults[cult];
        float r = s * 0.47f, rc = r * 0.68f, middle = (r + rc) / 2; // outer radius, the middle's, halfway between
        svg.Fill(SvgPath.Circle(0, 0, r), ring);
        // the ring's pattern
        if (cult == Track.Fire) // chevrons pointing round
            for (var i = 0; i < 12; i++)
            {
                float d = r * 0.09f;
                svg.Save();
                svg.Rotate(i / 12f * Tau);
                svg.Stroke(new SvgPath().MoveTo(middle - d, -d * 1.1f).LineTo(middle + d * 0.6f, 0).LineTo(middle - d, d * 1.1f), detail, r * 0.07f, "round", "round");
                svg.Restore();
            }
        if (cult is Track.Water or Track.Air) // dots
        {
            int k = cult == Track.Water ? 10 : 12;
            for (var i = 0; i < k; i++)
                svg.Fill(SvgPath.Circle(Mathf.Cos(i * Tau / k) * middle, Mathf.Sin(i * Tau / k) * middle, r * 0.065f), detail);
        }
        if (cult == Track.Earth) // small blocks
            for (var i = 0; i < 8; i++)
            {
                svg.Save();
                svg.Rotate(i / 8f * Tau + Mathf.Pi / 8);
                svg.Fill(new SvgPath().Rect(middle - r * 0.1f, -r * 0.06f, r * 0.2f, r * 0.12f), detail);
                svg.Restore();
            }
        svg.Fill(SvgPath.Circle(0, 0, rc), Paint.Radial(-rc * 0.3f, -rc * 0.35f, 0, 0, rc, (0, Shade(core, 0.14f)), (1, Shade(core, -0.08f))));
        svg.Stroke(SvgPath.Circle(0, 0, rc), new Color(0, 0, 0, 0.45f), Ow(s) * 0.6f);
        svg.Stroke(SvgPath.Circle(0, 0, r), Out, Ow(s));
    }

    // A step on every track: the four discs bunched together (as on the town keys): air at the back, water in front.
    private static void EveryCult(Svg svg, float s)
    {
        foreach (var (cult, x, y) in new[] { (Track.Air, 0f, -0.2f), (Track.Fire, -0.25f, 0.02f), (Track.Earth, 0.25f, 0.02f), (Track.Water, 0f, 0.22f) })
        {
            svg.Save();
            svg.Translate(x * s, y * s);
            CultDisc(svg, s * 0.52f, cult);
            svg.Restore();
        }
    }

    // A step on any track: one disc in four quarters, a track's colours in each.
    private static void AnyCult(Svg svg, float s)
    {
        float r = s * 0.47f;
        foreach (var (cult, from, to) in new[] { (Track.Fire, Mathf.Pi, 1.5f * Mathf.Pi), (Track.Air, 1.5f * Mathf.Pi, Tau), (Track.Earth, 0f, 0.5f * Mathf.Pi), (Track.Water, 0.5f * Mathf.Pi, Mathf.Pi) })
        {
            svg.Save();
            svg.Clip(new SvgPath().MoveTo(0, 0).Arc(0, 0, r + 2, from, to).Close());
            CultDisc(svg, s, cult);
            svg.Restore();
        }
        svg.Stroke(new SvgPath().MoveTo(-r, 0).LineTo(r, 0).MoveTo(0, -r).LineTo(0, r), Out, Ow(s) * 0.5f);
        svg.Stroke(SvgPath.Circle(0, 0, r), Out, Ow(s));
    }

    // ---------- Resources ----------

    private static void Coin(Svg svg, float s)
    {
        float r = s * 0.44f;
        Outlined(svg, SvgPath.Circle(0, 0, r), new Color("b8852c"), s);
        svg.Fill(SvgPath.Circle(0, 0, r * 0.82f), Paint.Radial(-r * 0.3f, -r * 0.35f, 0, 0, r * 0.84f, (0, new Color("fbe08a")), (1, new Color("d9a33a"))));
        svg.Stroke(SvgPath.Circle(0, 0, r * 0.82f), new Color("8a5f1c"), Ow(s) * 0.5f);
        for (var i = 0; i < 16; i++) // a sunburst
        {
            float a = i / 16f * Tau;
            svg.Fill(new SvgPath().MoveTo(At(a - 0.11f, r * 0.42f)).LineTo(At(a, r * 0.72f)).LineTo(At(a + 0.11f, r * 0.42f)).Close(), new Color("c48b2c"));
        }
        Outlined(svg, SvgPath.Circle(0, 0, r * 0.38f), new Color("f6d36a"), s, new Color("8a5f1c"), Ow(s) * 0.5f);
    }

    // The key is drawn at 80% (it looked too big beside the others); on the town icon, a further 70% of that.
    private const float KeyScale = 0.8f, KeyOnTown = 0.7f;

    // A town: the hexes and buildings, with a smaller key over the middle, read as one symbol.
    private static void TownWithKey(Svg svg, float s)
    {
        Town(svg, s);
        Key(svg, s * KeyScale * KeyOnTown);
    }

    // "threehextown": a smaller town key token with three gold map hexes on it, one over two, as for a town of
    // three hexes; drawn in the key token's 150 × 172 units, scaled to fit s. As KeyToken, but with the gold frame
    // twice as wide and the stone one flat colour. The hexes meet at the token's centre, so the gaps between them
    // run straight to the token's corners.
    private static void ThreeHexTown(Svg svg, float s)
    {
        float k = s * 0.98f / 172, r = Mathf.Min(150 / Mathf.Sqrt(3), 86) - 2;
        var centre = new Vector2(75, 86);
        SvgPath Hex(Vector2 at, float radius) => SvgPath.Polygon(HexCorners(at, radius));
        svg.Save();
        svg.Scale(k, k);
        svg.Translate(-centre.X, -centre.Y);
        var line = new Color("4e3a12");
        svg.Fill(Hex(centre, r), Paint.Linear(centre.X - r, centre.Y - r, centre.X + r, centre.Y + r,
            (0, new Color("f0d27a")), (0.5f, new Color("c9a248")), (1, new Color("8f6c25"))));
        svg.Stroke(Hex(centre, r), line, 1.5f, "miter");
        svg.Stroke(Hex(centre, r * 0.94f), new Color("fff0be", 0.6f), 1, "miter"); // the bevel's highlight
        svg.Fill(Hex(centre, r * 0.76f), new Color("8e979e"));                       // KeyToken's stone is at 0.88
        svg.Stroke(Hex(centre, r * 0.76f), line, 1.5f, "miter");
        const float step = 26.7f, tile = 22.6f; // between the hexes' centres; each hex's corner radius
        Vector2[] tiles =
        {
            centre + new Vector2(0, -step),
            centre + new Vector2(-step * Mathf.Sqrt(3) / 2, step / 2),
            centre + new Vector2(step * Mathf.Sqrt(3) / 2, step / 2),
        };
        foreach (Vector2 at in tiles)
            Outlined(svg, Hex(at, tile), Paint.Linear(at.X - tile, at.Y - tile, at.X + tile, at.Y + tile,
                (0, new Color("c4a24c")), (1, new Color("93752e"))), s, Out, Mathf.Max(1.7f, Ow(s) / k / 2));
        svg.Restore();
    }

    private static void Key(Svg svg, float s)
    {
        svg.Save();
        svg.Rotate(-Mathf.Pi / 4);
        var gold = new Color("cfae55");
        float w = s * 0.09f, y = w / 2;
        // The bow: a ring (the inner circle runs the other way round, so filling leaves a hole).
        float bx = -s * 0.3f, ro = s * 0.13f + y, ri = s * 0.13f - y;
        SvgPath ring = new SvgPath().Arc(bx, 0, ro, 0, Tau).Close().MoveTo(bx + ri, 0).Arc(bx, 0, ri, 0, -Tau, anticlockwise: true).Close();
        // The shaft and the bit as one outline: two teeth with a notch between them.
        SvgPath body = SvgPath.Polygon(new Vector2[]
        {
            new(-s * 0.16f, -y), new(s * 0.46f, -y), new(s * 0.46f, y + s * 0.13f), new(s * 0.4f, y + s * 0.13f), new(s * 0.4f, y + s * 0.05f),
            new(s * 0.31f, y + s * 0.05f), new(s * 0.31f, y + s * 0.1f), new(s * 0.25f, y + s * 0.1f), new(s * 0.25f, y), new(-s * 0.16f, y),
        });
        // Outline first, twice as wide as wanted (half of it ends up outside), then the gold over everything: the
        // outline is the same width all the way round and there is no line where the ring meets the shaft.
        svg.Stroke(ring, Out, Ow(s) * 2, "miter");
        svg.Stroke(body, Out, Ow(s) * 2, "miter");
        svg.Fill(ring, gold);
        svg.Fill(body, gold);
        svg.Restore();
    }

    private static void Points(Svg svg, float s)
    {
        float h = s * 0.44f;
        Outlined(svg, new SvgPath().RoundRect(-h, -h, 2 * h, 2 * h, s * 0.05f), Paint.Linear(-h, -h, h, h, (0, new Color("c98b48")), (1, new Color("87501f"))), s);
        svg.Stroke(new SvgPath().Rect(-h * 0.84f, -h * 0.84f, h * 1.68f, h * 1.68f), new Color("ffe1b4", 0.35f), Ow(s) * 0.6f);
        // a gold burst in the middle, where the number goes
        svg.Fill(new SvgPath().Rect(-h, -h, 2 * h, 2 * h), Paint.Radial(0, 0, 0, 0, h * 0.7f, (0, new Color("ffe28c", 0.95f)), (1, new Color("ffc85a", 0f))));
        for (var i = 0; i < 12; i++)
        {
            float a = i / 12f * Tau;
            svg.Fill(new SvgPath().MoveTo(At(a - 0.12f, h * 0.18f)).LineTo(At(a, h * 0.52f)).LineTo(At(a + 0.12f, h * 0.18f)).Close(), new Color("f4cf6a"));
        }
        // laurel: leaves up both sides
        float leafLine = Mathf.Max(0.5f, Ow(s) * 0.35f);
        foreach (int side in new[] { -1, 1 })
            for (var i = 0; i < 6; i++)
            {
                float a = Mathf.Pi / 2 + side * (0.42f + i * 0.36f), rr = h * 0.72f;
                svg.Save();
                svg.Translate(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
                svg.Rotate(a + side * Mathf.Pi / 2 - side * 0.5f);
                SvgPath leaf = new SvgPath().Ellipse(0, 0, h * 0.17f, h * 0.07f);
                svg.Fill(leaf, new Color("4a5866"));
                svg.Stroke(leaf, new Color("1e252c"), leafLine);
                svg.Restore();
            }
    }

    private static void Spade(Svg svg, float s) => Spade(svg, s, gold: false);

    // gold: the blade and its socket gold (the Prospectors' spade).
    private static void Spade(Svg svg, float s, bool gold)
    {
        svg.Save();
        svg.Rotate(-Mathf.Pi / 4);
        svg.Translate(0, s * 0.06f); // handle up and to the left, blade down and to the right
        var wood = new Color("8b5a2b");
        float top = -s * 0.6f;
        Band(svg, new SvgPath().MoveTo(-s * 0.09f, top).LineTo(s * 0.09f, top), s * 0.07f, wood, s); // the grip
        Band(svg, new SvgPath().MoveTo(0, top).LineTo(0, s * 0.02f), s * 0.07f, wood, s, "butt");     // the shaft
        float hw = s * 0.187f;
        SvgPath blade = new SvgPath().MoveTo(-hw, s * 0.02f).LineTo(hw, s * 0.02f)
            .CubicTo(hw * 1.07f, s * 0.24f, hw * 0.64f, s * 0.38f, 0, s * 0.48f)
            .CubicTo(-hw * 0.64f, s * 0.38f, -hw * 1.07f, s * 0.24f, -hw, s * 0.02f).Close();
        Outlined(svg, blade, Paint.Linear(-hw, 0, hw, 0, (0, new Color(gold ? "fbe08a" : "dfe4ea")), (1, new Color(gold ? "b8852c" : "8b95a1"))), s);
        SvgPath socket = SvgPath.Polygon(new Vector2[] { new(-s * 0.06f, -s * 0.04f), new(s * 0.06f, -s * 0.04f), new(s * 0.06f, s * 0.05f), new(-s * 0.06f, s * 0.05f) });
        Outlined(svg, socket, new Color(gold ? "d9a33a" : "9aa3ad"), s, Out, Ow(s) * 0.7f);
        svg.Stroke(new SvgPath().MoveTo(-s * 0.04f, s * 0.1f).LineTo(-s * 0.02f, s * 0.36f), new Color(1, 1, 1, 0.6f), Ow(s) * 0.6f); // a glint
        svg.Restore();
    }

    private static void Ship(Svg svg, float s)
    {
        var water = new SvgPath().MoveTo(-s * 0.48f, s * 0.3f);
        for (var i = 0; i < 4; i++) // waves
        {
            float x = -s * 0.48f + i * s * 0.24f;
            water.QuadTo(x + s * 0.06f, s * 0.25f, x + s * 0.12f, s * 0.3f).QuadTo(x + s * 0.18f, s * 0.35f, x + s * 0.24f, s * 0.3f);
        }
        water.LineTo(s * 0.48f, s * 0.42f).LineTo(-s * 0.48f, s * 0.42f).Close();
        Band(svg, new SvgPath().MoveTo(-s * 0.02f, s * 0.2f).LineTo(-s * 0.02f, -s * 0.46f), s * 0.04f, new Color("5a3d24"), s, "butt"); // the mast
        SvgPath sail = new SvgPath().MoveTo(-s * 0.26f, -s * 0.38f).LineTo(s * 0.22f, -s * 0.38f).QuadTo(s * 0.28f, -s * 0.12f, s * 0.22f, s * 0.08f)
            .LineTo(-s * 0.26f, s * 0.08f).QuadTo(-s * 0.2f, -s * 0.15f, -s * 0.26f, -s * 0.38f).Close();
        Outlined(svg, sail, new Color("ece6d6"), s);
        svg.Stroke(new SvgPath().MoveTo(-s * 0.02f, -s * 0.38f).LineTo(-s * 0.02f, s * 0.08f), new Color(0, 0, 0, 0.25f), Ow(s) * 0.5f);
        SvgPath hull = new SvgPath().MoveTo(-s * 0.42f, s * 0.12f).LineTo(s * 0.3f, s * 0.12f).QuadTo(s * 0.42f, s * 0.08f, s * 0.46f, -s * 0.06f)
            .QuadTo(s * 0.44f, s * 0.24f, s * 0.26f, s * 0.32f).LineTo(-s * 0.3f, s * 0.32f).QuadTo(-s * 0.4f, s * 0.24f, -s * 0.42f, s * 0.12f).Close();
        Outlined(svg, hull, new Color("6e4a2c"), s);
        svg.Stroke(new SvgPath().MoveTo(-s * 0.36f, s * 0.2f).LineTo(s * 0.34f, s * 0.2f), new Color("ffdcb4", 0.35f), Ow(s) * 0.5f);
        Outlined(svg, water, new Color("6fb1dd"), s);
    }

    // ---------- Power ----------

    // A curved arrow as one closed shape (band and head together), so its outline runs all the way round it.
    // Angles as on a canvas (increasing is clockwise); the head is at `to`.
    // scale: the band and head that much bigger (the outline stays the icon's).
    private static void ArcArrow(Svg svg, float s, float cx, float cy, float r, float from, float to, Color colour, Color line, float scale = 1f)
    {
        float w = s * 0.13f * scale, headLength = s * 0.24f * scale, headHalf = s * 0.15f * scale;
        int dir = to > from ? 1 : -1;
        float end = to - dir * headLength / r, ro = r + w / 2, ri = r - w / 2; // where the head starts; outer and inner radii
        Vector2 P(float a, float radius) => new(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius);
        SvgPath arrow = new SvgPath().MoveTo(P(from, ro)).Arc(cx, cy, ro, from, end, dir < 0) // outer edge to the head
            .LineTo(P(end, r + headHalf)).LineTo(P(to, r)).LineTo(P(end, r - headHalf))     // the head
            .LineTo(P(end, ri)).Arc(cx, cy, ri, end, from, dir > 0)                          // inner edge back to the tail
            .Close();
        Outlined(svg, arrow, colour, s, line);
    }

    private static void PowerToken(Svg svg, float s) => Solid(svg, PieceArt.PictureFor(PieceArt.Prop.PowerToken), s * 0.9f, Purple);

    // Power gained or spent: a token in the middle, a curved arrow over it that may reach outside the icon's square
    // (Canvas gives it room). Arrow and token are drawn ArrowScale times bigger, the token TokenScale times more (the
    // icon's size unchanged; income icons too).
    private const float ArrowScale = 1.2f;
    private const float TokenScale = 1.2f;
    private const float TokenWidth = 0.56f * ArrowScale * TokenScale; // of s
    private const float ArrowCentreY = (0.12f - 0.27f) * ArrowScale, ArrowRadius = 0.36f * ArrowScale; // of s

    private static void SmallToken(Svg svg, float s) => Solid(svg, PieceArt.PictureFor(PieceArt.Prop.PowerToken), s * TokenWidth, Purple);

    private static void PowerGain(Svg svg, float s)
    {
        SmallToken(svg, s);
        ArcArrow(svg, s, 0, s * ArrowCentreY, s * ArrowRadius, Mathf.Pi, Mathf.Pi * 1.98f, new Color("d9dedb"), Out, ArrowScale);
    }

    private static void PowerSpend(Svg svg, float s)
    {
        SmallToken(svg, s);
        ArcArrow(svg, s, 0, s * ArrowCentreY, s * ArrowRadius, Mathf.Pi * 2, Mathf.Pi * 1.02f, new Color("27302f"), new Color("e9ece9"), ArrowScale);
    }

    // ---------- Actions and arrows ----------

    // An octagon pulled out sideways: its right half moved e right, its left half e left (top and bottom 2e longer).
    private static SvgPath WideOctagonPath(float r, float e) => SvgPath.Polygon(Enumerable.Range(0, 8)
        .Select(i => At(Mathf.Pi / 8 + i * Mathf.Pi / 4, r)).Select(p => new Vector2(p.X + Mathf.Sign(p.X) * e, p.Y)));

    private static SvgPath OctagonPath(float r) => SvgPath.Polygon(Enumerable.Range(0, 8).Select(i => At(Mathf.Pi / 8 + i * Mathf.Pi / 4, r)));

    // An action's octagon; used: duller, with a cross over it.
    private static void Octagon(Svg svg, float s, bool used)
    {
        float r = s * 0.48f;
        Outlined(svg, OctagonPath(r), new Color(used ? "d8b64c" : "f2a14a"), s);
        svg.Stroke(OctagonPath(r * 0.86f), new Color(used ? "a5862d" : "b26f2a"), Ow(s) * 0.6f);
        if (!used)
            return;
        float d = s * 0.17f;
        svg.Stroke(new SvgPath().MoveTo(-d, -d).LineTo(d, d).MoveTo(d, -d).LineTo(-d, d), new Color("1d1d1d"), s * 0.05f, "round", "round");
    }

    // The tail flares out to the head's height and reaches the head directly.
    private static void TriggerArrow(Svg svg, float s)
    {
        float h = s * 0.26f, shaft = s * 0.08f, tail = -s * 0.2f, head = s * 0.04f;
        SvgPath arrow = new SvgPath().MoveTo(tail, -h).QuadTo(tail + s * 0.16f, -shaft, head, -shaft)
            .LineTo(head, -h).LineTo(s * 0.36f, 0).LineTo(head, h).LineTo(head, shaft)
            .QuadTo(tail + s * 0.16f, shaft, tail, h).Close();
        svg.Save();
        svg.Translate(-s * 0.08f, 0);
        Outlined(svg, arrow, new Color("8a8f8b"), s);
        svg.Restore();
    }

    // An arrow along the curve from p0 (bent towards c) to p1, as one closed shape: flat tail, head at p1.
    private static void CurveArrow(Svg svg, Vector2 p0, Vector2 c, Vector2 p1, Color colour, Color line, float w, float headLength, float headHalf, float lineWidth)
    {
        Vector2 At(float t) => (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p1;
        Vector2 Tangent(float t) => (2 * (1 - t) * (c - p0) + 2 * t * (p1 - c)).Normalized();
        float length = 0;
        for (var i = 1; i <= 40; i++)
            length += At(i / 40f).DistanceTo(At((i - 1) / 40f));
        float tEnd = Mathf.Max(0.05f, 1 - headLength / length); // where the head starts
        const int steps = 20;
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i <= steps; i++)
        {
            float t = tEnd * i / steps;
            Vector2 d = Tangent(t), across = new Vector2(-d.Y, d.X) * w / 2;
            left.Add(At(t) + across);
            right.Add(At(t) - across);
        }
        Vector2 b = At(tEnd), e = Tangent(tEnd), side = new Vector2(-e.Y, e.X) * headHalf;
        IEnumerable<Vector2> outline = left.Append(b + side).Append(p1).Append(b - side).Concat(Enumerable.Reverse(right));
        Outlined(svg, SvgPath.Polygon(outline), colour, 0, line, lineWidth);
    }
}
