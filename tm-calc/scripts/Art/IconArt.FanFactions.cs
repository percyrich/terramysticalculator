using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// IconArt, continued: the fan factions' icons.
public static partial class IconArt
{
    // ---------- Fan factions ----------

    // Numbers and signs drawn as stroked paths (no fonts, so they look the same at every size and on every machine): each glyph is a centre
    // line in a box 1 high (y from -0.5 to 0.5) and Width wide, drawn wide in the edge colour, then narrower in the fill.
    private static readonly Dictionary<char, (float Width, string Path)> Glyphs = new()
    {
        ['0'] = (0.62f, "M0 -0.42C0.32 -0.42 0.32 0.42 0 0.42C-0.32 0.42 -0.32 -0.42 0 -0.42"),
        ['1'] = (0.5f, "M-0.13 -0.28L0.04 -0.43L0.04 0.43"),
        ['2'] = (0.62f, "M-0.23 -0.24C-0.18 -0.5 0.26 -0.5 0.24 -0.2C0.22 -0.02 -0.1 0.15 -0.25 0.43L0.26 0.43"),
        ['3'] = (0.62f, "M-0.24 -0.3C-0.16 -0.5 0.27 -0.5 0.23 -0.26C0.21 -0.11 0.08 -0.07 -0.02 -0.06C0.13 -0.06 0.28 0.03 0.26 0.18C0.24 0.47 -0.17 0.5 -0.27 0.28"),
        ['4'] = (0.64f, "M0.11 0.43L0.11 -0.43L-0.27 0.17L0.3 0.17"),
        ['5'] = (0.62f, "M0.23 -0.43L-0.16 -0.43L-0.21 -0.04C0.1 -0.18 0.29 -0.04 0.27 0.15C0.25 0.48 -0.17 0.5 -0.27 0.3"),
        ['+'] = (0.56f, "M-0.2 0.02L0.2 0.02M0 -0.18L0 0.22"),
        ['-'] = (0.5f, "M-0.17 0.02L0.17 0.02"),
        ['?'] = (0.56f, "M-0.2 -0.22C-0.18 -0.48 0.24 -0.48 0.22 -0.22C0.2 -0.04 0 -0.04 0 0.12M0 0.38L0 0.381"),
        ['!'] = (0.34f, "M0 -0.42L0 0.1M0 0.38L0 0.381"),
    };

    // Text (from Glyphs) centred on (x, y), h tall: white edged in the outline colour unless given. All the edges go
    // down first, then all the fills, so neighbouring glyphs don't cut into each other.
    private static void GlyphText(Svg svg, string text, float x, float y, float h, Color? fill = null, Color? edge = null)
    {
        float t = h * 0.16f, w = Mathf.Max(0.8f, h * 0.075f), left = x - text.Sum(c => Glyphs[c].Width) * h / 2;
        var placed = new List<(float X, string Path)>();
        foreach (char c in text)
        {
            placed.Add((left + Glyphs[c].Width * h / 2, Glyphs[c].Path));
            left += Glyphs[c].Width * h;
        }
        foreach (var (colour, width) in new[] { (edge ?? Out, t + 2 * w), (fill ?? Colors.White, t) })
            foreach (var (cx, path) in placed)
            {
                svg.Save();
                svg.Translate(cx, y);
                svg.Scale(h, h);
                svg.Stroke(new SvgPath().Data(path), colour, width / h, "round", "round");
                svg.Restore();
            }
    }

    private static readonly Color MapLake = HexArt.TerrainColour(Terrain.Lake), MapVolcano = HexArt.TerrainColour(Terrain.Volcano),
        MapIce = HexArt.TerrainColour(Terrain.Ice), MapWasteland = HexArt.TerrainColour(Terrain.Wasteland), MapRiver = new("9ccbe6");
    private static readonly Color SlashColour = new("2a2420");

    // A black arrow (CurveArrow) in the fan icons' usual proportions, k times as thick.
    private static void BlackArrow(Svg svg, float s, Vector2 p0, Vector2 c, Vector2 p1, float k = 1) =>
        CurveArrow(svg, p0, c, p1, ArrowBlack, ArrowEdge, s * 0.08f * k, s * 0.12f * k, s * 0.09f * k, Ow(s) * 0.5f);

    // A dark slash ("or"); edged: with a light edge round it.
    private static void Slash(Svg svg, float s, Vector2 a, Vector2 b, bool edged = true)
    {
        SvgPath line = new SvgPath().MoveTo(a).LineTo(b);
        if (edged)
            Band(svg, line, s * 0.035f, SlashColour, s * 0.5f, "round", ArrowEdge);
        else
            svg.Stroke(line, SlashColour, s * 0.035f, "round", "round");
    }

    // A power bowl as on the player panel (152 side, 26 corner, 6 edge in the bowl's own purple, its fill that purple
    // 0.85 towards white; as FactionBoardArt's bowls), `side` across, centred on `at`, its numeral (I, II or III) in its
    // purple: n stems 16 wide, 14 apart, under one serif bar along the top and one along the bottom.
    private static readonly Color[] BowlPurples = { new("5b3a6d"), new("83417b"), new("b0308f") };
    private static void PowerBowl(Svg svg, Vector2 at, float side, int n)
    {
        Color edge = BowlPurples[n - 1];
        float k = side / 152, span = n * 16 + (n - 1) * 14, x0 = 76 - span / 2;
        svg.Save();
        svg.Translate(at.X - side / 2, at.Y - side / 2);
        svg.Scale(k, k);
        svg.Fill(new SvgPath().RoundRect(3, 3, 146, 146, 23), edge.Lerp(Colors.White, 0.85f));
        svg.Stroke(new SvgPath().RoundRect(3, 3, 146, 146, 23), edge, 6);
        svg.Stroke(new SvgPath().RoundRect(0, 0, 152, 152, 26), Out, 2);
        svg.Fill(new SvgPath().Rect(x0 - 12, 36, span + 24, 10), edge);
        svg.Fill(new SvgPath().Rect(x0 - 12, 106, span + 24, 10), edge);
        for (var i = 0; i < n; i++)
            svg.Fill(new SvgPath().Rect(x0 + i * 30, 46, 16, 60), edge);
        svg.Restore();
    }

    // Where a number goes on a power token drawn at size u (the middle of its top), below its centre, of u.
    private const float TokenTop = -0.131f;

    // A power token at size u centred on `at`, a number on it (none if text is null).
    private static void NumberedToken(Svg svg, Vector2 at, float u, string text = null)
    {
        svg.Save();
        svg.Translate(at.X, at.Y);
        Solid(svg, PieceArt.PictureFor(PieceArt.Prop.PowerToken), u, Purple);
        svg.Restore();
        if (text is not null)
            GlyphText(svg, text, at.X, at.Y + TokenTop * u, u * 0.5f);
    }

    // A burned power token (removed for the rest of the game) at size u centred on `at`: TokenRemoved.
    private static void BurnedToken(Svg svg, Vector2 at, float u)
    {
        svg.Save();
        svg.Translate(at.X, at.Y);
        TokenRemoved(svg, u / 0.9f);
        svg.Restore();
    }

    // Power gained or spent, drawn at size u (the token 0.56 u wide, 0.27 u below (0, 0), the arrow over
    // it): Godot's PowerGain and PowerSpend, sized and placed so the token matches. Returns the token's centre.
    private static Vector2 LabPower(Svg svg, float u, bool spend)
    {
        var token = new Vector2(0, u * 0.27f);
        float k = u * 0.56f / TokenWidth;
        svg.Save();
        svg.Translate(token.X, token.Y);
        if (spend)
            PowerSpend(svg, k);
        else
            PowerGain(svg, k);
        svg.Restore();
        return token;
    }

    // Map hexes on the map's grid, centred on `centre`, hex radius r: each cell's column and row (rows alternate half a
    // hex across), its fill (null: a river, blue with no outline) and a white building on it (or none). unit: a
    // dwelling's length (r × 0.62 if 0); outlineWidth and edgeMix: as Solid's, for the buildings. Returns where each
    // cell's centre is.
    private static Func<int, int, Vector2> HexGrid(Svg svg, float s, Vector2 centre, float r,
        (int Column, int Row, Color? Fill, PieceKind? Building)[] cells, float unit = 0, float? outlineWidth = null, float edgeMix = 0.3f,
        Color? buildings = null)
    {
        Vector2 Centre(int c, int row) => new(Mathf.Sqrt(3) * (c + 0.5f * (row & 1)), 1.5f * row); // in hex radii
        List<Vector2> centres = cells.Select(c => Centre(c.Column, c.Row)).ToList();
        var middle = new Vector2((centres.Min(p => p.X) + centres.Max(p => p.X)) / 2, (centres.Min(p => p.Y) + centres.Max(p => p.Y)) / 2);
        Vector2 Place(int c, int row) => centre + (Centre(c, row) - middle) * r;
        foreach (var cell in cells.Where(c => c.Fill is null))
            svg.Fill(SvgPath.Polygon(HexCorners(Place(cell.Column, cell.Row), r + 0.5f)), MapRiver);
        foreach (var cell in cells.Where(c => c.Fill is not null))
            MapHex(svg, s, Place(cell.Column, cell.Row), r * 0.97f, cell.Fill.Value);
        foreach (var cell in cells.Where(c => c.Building is not null))
        {
            Vector2 p = Place(cell.Column, cell.Row);
            svg.Save();
            svg.Translate(p.X, p.Y + r * 0.04f);
            Solid(svg, PieceArt.PictureFor(cell.Building.Value), 0, buildings ?? White, shadow: false, unit: unit > 0 ? unit : r * 0.62f,
                outlineWidth: outlineWidth, edgeMix: edgeMix);
            svg.Restore();
        }
        return Place;
    }

    // A small town key token centred on `at` (corner radius r): the gold frame round grey stone, a white exclamation
    // mark on it (a warning: the town).
    private static void KeyWarning(Svg svg, float s, Vector2 at, float r)
    {
        Outlined(svg, SvgPath.Polygon(HexCorners(at, r)), Paint.Linear(at.X - r, at.Y - r, at.X + r, at.Y + r,
            (0, new Color("f0d27a")), (0.5f, new Color("c9a248")), (1, new Color("8f6c25"))), s, Out, Ow(s) * 0.7f);
        Outlined(svg, SvgPath.Polygon(HexCorners(at, r * 0.76f)), new Color("8e979e"), s, new Color("4e3a12"), Ow(s) * 0.4f);
        GlyphText(svg, "!", at.X, at.Y, r * 1.15f);
    }

    // A bridge piece (PieceArt.Prop.Bridge) centred on `at`, `unit` pixels to a dwelling's length.
    private static void BridgePiece(Svg svg, Vector2 at, float unit, Color colour)
    {
        svg.Save();
        svg.Translate(at.X, at.Y);
        Solid(svg, PieceArt.PictureFor(PieceArt.Prop.Bridge), 0, colour, shadow: false, unit: unit);
        svg.Restore();
    }

    // Architects' stronghold: move a bridge. The bridge where it was (faded, a town key token with a warning over it),
    // a black arrow over to the bridge where it goes (in the wasteland red).
    private static void ArchitectsMoveBridge(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        svg.Save();
        svg.Opacity(0.45f);
        BridgePiece(svg, new Vector2(-s * 0.04f, -s * 0.06f), s * 0.27f, MapWasteland);
        svg.Restore();
        BridgePiece(svg, new Vector2(s * 0.1f, s * 0.17f), s * 0.27f, MapWasteland);
        BlackArrow(svg, s, new Vector2(s * 0.11f, -s * 0.1f), new Vector2(s * 0.26f, -s * 0.12f), new Vector2(s * 0.21f, s * 0.06f), 0.75f);
        KeyWarning(svg, s, new Vector2(-s * 0.17f, -s * 0.15f), s * 0.16f);
    });

    // Archivists' stronghold: when passing, take two pass tiles. On pass, then two pass tiles, a black arrow out of
    // them down to the left. Twice as wide as tall.
    private static void ArchivistsTwoPassTiles(Svg svg, float s)
    {
        svg.Save();
        svg.Translate(-s * 0.42f, 0);
        OnPass(svg, s * 0.9f);
        svg.Restore();
        float w = s * 0.25f, h = w * 375 / 120;
        foreach (float x in new[] { 0.32f, 0.6f })
        {
            svg.Save();
            svg.Translate(x * s - w / 2, -h / 2);
            PassTile(svg, w, h);
            svg.Restore();
        }
        BlackArrow(svg, s, new Vector2(s * 0.26f, -s * 0.04f), new Vector2(s * 0.04f, -s * 0.06f), new Vector2(0, s * 0.36f), 1.2f);
    }

    // Time Travellers' stronghold: up to four power tokens from bowl I to bowl III. Bowl I lower left, bowl III upper
    // right, a black arrow from one to the other; a token marked 4 in the corner below.
    private static void TimeTravellersBowls(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        PowerBowl(svg, new Vector2(-s * 0.19f, s * 0.17f), s * 0.28f, 1);
        PowerBowl(svg, new Vector2(s * 0.19f, -s * 0.16f), s * 0.28f, 3);
        BlackArrow(svg, s, new Vector2(-s * 0.19f, 0), new Vector2(-s * 0.21f, -s * 0.18f), new Vector2(s * 0.04f, -s * 0.17f));
        NumberedToken(svg, new Vector2(s * 0.13f, s * 0.17f), s * 0.22f, "4");
    });

    // Prospectors' resource: the spade, its blade gold, turned to point from lower left to upper right.
    private static void GoldenSpade(Svg svg, float s)
    {
        svg.Save();
        svg.Scale(-1, 1);
        Spade(svg, s, gold: true);
        svg.Restore();
    }

    // Prospectors' stronghold: a coin per opponents' trading post. The opponents symbol (the Dragonlords') above a
    // trading post on a grey hex, the brace, a coin marked 1. The hex's outline is thin and the trading post's heavy
    // (the usual widths swapped), so the building stands out.
    private static void ProspectorsCoins(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        svg.Save();
        svg.Translate(-s * 0.14f, -s * 0.15f);
        Opponents(svg, s, s * 0.36f);
        svg.Restore();
        float r = s * 0.16f, thin = Mathf.Max(1.1f, s * 0.11f * 1.3f * 0.035f), heavy = Mathf.Max(1, r * 0.09f);
        var hex = new Vector2(-s * 0.13f, s * 0.14f);
        Outlined(svg, SvgPath.Polygon(HexCorners(hex, r)), MapGrey, s, Out, thin);
        svg.Save();
        svg.Translate(hex.X, hex.Y);
        Solid(svg, PieceArt.PictureFor(PieceKind.TradingPost), 0, White, shadow: false, unit: s * 0.11f, outlineWidth: heavy);
        svg.Restore();
        svg.Save();
        svg.Translate(s * 0.05f, s * 0.01f);
        Brace(svg, s * 0.62f);
        svg.Restore();
        svg.Save();
        svg.Translate(s * 0.24f, s * 0.01f);
        Coin(svg, s * 0.28f);
        svg.Restore();
        GlyphText(svg, "1", s * 0.24f, s * 0.01f, s * 0.14f, Out, new Color("fbe08a"));
    });

    // Children of the Wyrm's stronghold: every burned power token back into bowl I. Three burned tokens in a heap, a
    // black arrow from them down into bowl I.
    private static void WyrmTokensToBowl(Svg svg, float s)
    {
        PowerBowl(svg, new Vector2(s * 0.22f, s * 0.26f), s * 0.4f, 1);
        BlackArrow(svg, s, new Vector2(s * 0.06f, -s * 0.2f), new Vector2(s * 0.22f, -s * 0.2f), new Vector2(s * 0.22f, s * 0.09f), 1.1f);
        foreach (var (x, y) in new[] { (-0.3f, -0.24f), (-0.05f, -0.27f), (-0.18f, -0.06f) })
            BurnedToken(svg, new Vector2(x * s, y * s), s * 0.3f);
    }

    // Children of the Wyrm's action: burn up to two power and put them on river spaces. Two burned tokens, the grey
    // arrow, then a patch of map with two tokens dropping onto its river hexes.
    private static void WyrmTokensToRiver(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        BurnedToken(svg, new Vector2(-s * 0.27f, -s * 0.09f), s * 0.19f);
        BurnedToken(svg, new Vector2(-s * 0.27f, s * 0.11f), s * 0.19f);
        svg.Save();
        svg.Translate(-s * 0.1f, s * 0.01f);
        TriggerArrow(svg, s * 0.3f);
        svg.Restore();
        Func<int, int, Vector2> at = HexGrid(svg, s, new Vector2(s * 0.17f, s * 0.04f), s * 0.1f, new (int, int, Color?, PieceKind?)[]
        {
            (0, 0, MapGrey, null), (1, 0, null, null), (0, 1, null, null), (1, 1, MapGrey, null), (0, 2, MapGrey, null), (1, 2, null, null), (0, 3, null, null),
        });
        foreach (var (c, r) in new[] { (1, 0), (1, 2) })
        {
            Vector2 p = at(c, r);
            NumberedToken(svg, p + new Vector2(0, s * 0.01f), s * 0.12f);
            BlackArrow(svg, s, p + new Vector2(-s * 0.13f, -s * 0.16f), p + new Vector2(-s * 0.03f, -s * 0.18f), p + new Vector2(-s * 0.01f, -s * 0.06f), 0.6f);
        }
    });

    // Atlanteans' stronghold: eight lake hexes in a line, zigzagging (four up, four down). Twice as wide as tall.
    private static void AtlanteansHexes(Svg svg, float s) =>
        HexGrid(svg, s, Vector2.Zero, s * 0.235f, Enumerable.Range(0, 4)
            .SelectMany(c => new (int, int, Color?, PieceKind?)[] { (c, 0, MapLake, null), (c, 1, MapLake, null) }).ToArray());

    // Chash Dallah's stronghold: pay for a power action with power or with coins. Power spent over a coin, split by a
    // slash; the grey arrow; a power action. Twice as wide as tall.
    private static void ChashPowerAction(Svg svg, float s)
    {
        svg.Save();
        svg.Translate(-s * 0.72f, -s * 0.16f);
        LabPower(svg, s * 0.5f, spend: true);
        svg.Restore();
        svg.Save();
        svg.Translate(-s * 0.46f, s * 0.22f);
        Coin(svg, s * 0.4f);
        svg.Restore();
        Slash(svg, s, new Vector2(-s * 0.695f, s * 0.18f), new Vector2(-s * 0.485f, -s * 0.18f));
        svg.Save();
        svg.Translate(-s * 0.08f, 0);
        TriggerArrow(svg, s * 0.5f);
        svg.Restore();
        float w = s * 0.86f, h = w * 127 / 247;
        svg.Save();
        svg.Translate(s * 0.5f - w / 2, -h / 2);
        svg.Scale(w / 247, h / 127);
        PowerAction(svg, 1.4f * 247 / w);
        svg.Restore();
    }

    // Chash Dallah's income track: the book action, its panel's flat top and bottom WideBookExtra (of s) longer at each
    // end, blank: room for a worker and a coin on one income hand (the worker 0.27 s and the coin 0.3 s
    // across, 0.13 s each side of the middle and 0.11 s up; the hand at 0.6 s, 0.03 s up). Its texture
    // is WideBookCanvas times as wide as tall.
    public const float WideBookExtra = 0.13f, WideBookCanvas = 1.3f;

    // A black arrow along a curve with a head at each end (from p0, bent towards c, to p1).
    private static void DoubleArrow(Svg svg, float s, Vector2 p0, Vector2 c, Vector2 p1, float w, float headLength, float headHalf)
    {
        Vector2 At(float t) => (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * c + t * t * p1;
        Vector2 Tangent(float t) => (2 * (1 - t) * (c - p0) + 2 * t * (p1 - c)).Normalized();
        float length = 0;
        for (var i = 1; i <= 40; i++)
            length += At(i / 40f).DistanceTo(At((i - 1) / 40f));
        float t0 = headLength / length, t1 = 1 - headLength / length;
        const int steps = 20;
        var left = new List<Vector2>();
        var right = new List<Vector2>();
        for (var i = 0; i <= steps; i++)
        {
            float t = t0 + (t1 - t0) * i / steps;
            Vector2 d = Tangent(t), across = new Vector2(-d.Y, d.X) * w / 2;
            left.Add(At(t) + across);
            right.Add(At(t) - across);
        }
        Vector2[] Head(float t, Vector2 tip)
        {
            Vector2 b = At(t), e = Tangent(t), side = new Vector2(-e.Y, e.X) * headHalf;
            return new[] { b + side, tip, b - side };
        }
        Vector2[] start = Head(t0, p0);
        IEnumerable<Vector2> outline = new[] { start[2], p0, start[0] }.Concat(left).Concat(Head(t1, p1)).Concat(Enumerable.Reverse(right));
        Outlined(svg, SvgPath.Polygon(outline), ArrowBlack, s, ArrowEdge, Ow(s) * 0.5f);
    }

    // Conspirators' stronghold: swap a favour, and no power for cult steps lost. Two favours with a two-headed arrow
    // over them; below, a cult step taken away (the any-cult disc with a minus) and a power gain of 0.
    private static void ConspiratorsSwap(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        float fw = s * 0.27f, fh = s * 0.15f;
        foreach (float x in new[] { -0.17f, 0.17f })
        {
            svg.Save();
            svg.Translate(x * s - fw / 2, -s * 0.07f - fh / 2);
            Favour(svg, fw, fh);
            svg.Restore();
        }
        DoubleArrow(svg, s, new Vector2(-s * 0.2f, -s * 0.17f), new Vector2(0, -s * 0.38f), new Vector2(s * 0.2f, -s * 0.17f), s * 0.065f, s * 0.1f, s * 0.075f);
        svg.Save();
        svg.Translate(-s * 0.14f, s * 0.2f);
        AnyCult(svg, s * 0.22f);
        svg.Restore();
        GlyphText(svg, "-", -s * 0.14f, s * 0.2f, s * 0.2f);
        float u = s * 0.26f;
        svg.Save();
        svg.Translate(s * 0.14f, s * 0.17f);
        Vector2 token = LabPower(svg, u, spend: false);
        GlyphText(svg, "0", token.X, token.Y + TokenTop * u * 0.56f, u * 0.3f);
        svg.Restore();
    });

    // Selkies' stronghold: a spade, at one more shipping. The ship with +1 on its sail, the spade.
    private static void SelkiesSpade(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        svg.Save();
        svg.Translate(-s * 0.1f, s * 0.08f);
        Ship(svg, s * 0.5f);
        svg.Restore();
        GlyphText(svg, "+1", -s * 0.1f, 0, s * 0.17f, Out, new Color("ece6d6"));
        svg.Save();
        svg.Translate(s * 0.19f, -s * 0.13f);
        Spade(svg, s * 0.4f);
        svg.Restore();
    });

    // Snow Shamans' stronghold: a dwelling on every mass of ice. Ice hexes in three separate masses among grey ones, a
    // black arrow dropping a dwelling (heavy outline, dark creases, to stand out on the ice) onto each mass.
    private static void SnowShamansIce(Svg svg, float s)
    {
        Color i = MapIce, g = MapGrey;
        Func<int, int, Vector2> at = HexGrid(svg, s, new Vector2(0, s * 0.04f), s * 0.15f, new (int, int, Color?, PieceKind?)[]
        {
            (0, 0, i, PieceKind.Dwelling), (1, 0, g, null), (2, 0, i, null), (0, 1, g, null), (1, 1, i, PieceKind.Dwelling), (2, 1, g, null),
            (0, 2, i, PieceKind.Dwelling), (1, 2, g, null), (2, 2, g, null),
        }, s * 0.1f, Mathf.Max(1.6f, s * 0.02f), 0.75f);
        foreach (var (c, r) in new[] { (0, 0), (1, 1), (0, 2) })
        {
            Vector2 p = at(c, r);
            BlackArrow(svg, s, p + new Vector2(-s * 0.16f, -s * 0.2f), p + new Vector2(-s * 0.05f, -s * 0.22f), p + new Vector2(-s * 0.02f, -s * 0.08f), 0.7f);
        }
    }

    // Enlightened's stronghold: conversions give twice as much. Three rows: power spent (5, 3, 1), the grey arrow, two
    // priests, two workers, two coins.
    private static void EnlightenedConversions(Svg svg, float s)
    {
        var rows = new[] { ("5", 0), ("3", 1), ("1", 2) };
        foreach (var (n, what) in rows)
        {
            float y = (what - 1) * s * 0.32f, u = s * 0.3f;
            svg.Save();
            svg.Translate(-s * 0.3f, y - u * 0.12f);
            Vector2 token = LabPower(svg, u, spend: true);
            GlyphText(svg, n, token.X, token.Y + TokenTop * u * 0.56f, u * 0.3f);
            svg.Restore();
            svg.Save();
            svg.Translate(-s * 0.04f, y);
            TriggerArrow(svg, s * 0.24f);
            svg.Restore();
            foreach (float dx in new[] { 0.17f, 0.3f })
            {
                svg.Save();
                svg.Translate(dx * s, y);
                if (what == 0)
                    Priest(svg, s * 0.26f, White);
                else if (what == 1)
                    Solid(svg, PieceArt.PictureFor(PieceArt.Prop.Worker), s * 0.2f, new Color("f3f3f1"), outline: 2f);
                else
                    Coin(svg, s * 0.26f);
                svg.Restore();
            }
        }
    }

    // Firewalkers' stronghold: when passing, a point per neighbourhood (shipping not counted). On pass; the ship struck
    // through; three neighbourhoods of white hexes with buildings in the volcano colour; the brace; points marked 1.
    // Twice as wide as tall.
    private static void FirewalkersNeighbourhoods(Svg svg, float s)
    {
        svg.Save();
        svg.Translate(-s * 0.76f, 0);
        OnPass(svg, s * 0.66f);
        svg.Restore();
        svg.Save();
        svg.Translate(-s * 0.38f, -s * 0.22f);
        Ship(svg, s * 0.34f);
        svg.Restore();
        Slash(svg, s, new Vector2(-s * 0.5f, -s * 0.04f), new Vector2(-s * 0.26f, -s * 0.4f), edged: false);
        float r = s * 0.105f, h = Mathf.Sqrt(3) * r;
        void Hex(float x, float y, PieceKind building)
        {
            MapHex(svg, s, new Vector2(x, y), r, White);
            PlacedBuilding(svg, building, x, y + r * 0.04f, r * 0.6f, MapVolcano);
        }
        Hex(-s * 0.08f, -s * 0.3f, PieceKind.Dwelling);
        Hex(-s * 0.08f + h, -s * 0.3f, PieceKind.TradingPost);
        Hex(-s * 0.4f, s * 0.2f, PieceKind.Dwelling);
        Hex(-s * 0.08f, s * 0.02f, PieceKind.Stronghold);
        Hex(-s * 0.08f + h / 2, s * 0.02f + 1.5f * r, PieceKind.TradingPost);
        Hex(-s * 0.08f + h * 1.5f, s * 0.02f + 1.5f * r, PieceKind.Temple);
        svg.Save();
        svg.Translate(s * 0.48f, 0);
        Brace(svg, s * 0.9f);
        svg.Restore();
        svg.Save();
        svg.Translate(s * 0.74f, 0);
        Points(svg, s * 0.4f);
        svg.Restore();
        GlyphText(svg, "1", s * 0.74f, 0, s * 0.2f);
    }

    // Kingdom of Ember's lava token: a plain disc in the volcano hex's colour (its side darker), as wide as a landscape
    // token on its hex (LavaDiameter of the hex's corner radius), LavaThickness of that thick, seen from LavaView above
    // the ground (nearly from above, so it lies almost flat) with its side still showing, so a stack can be counted.
    // On Icon.LavaToken the disc is LavaIconDiameter of the drawing's size (6/7 of the texture's size).
    public const float LavaDiameter = 1.64f, LavaThickness = 0.24f, LavaIconDiameter = 0.86f;
    public static readonly float LavaView = 65f * Mathf.Pi / 180f;

    // How much higher each token in a stack of lava tokens `diameter` across sits than the one under it.
    public static float LavaLift(float diameter) => diameter * LavaThickness * Mathf.Cos(LavaView);

    // A lava token `d` across, the middle of its bottom face at (x, y). Returns the y of its top face's middle.
    private static float LavaDisc(Svg svg, float s, float x, float y, float d)
    {
        float rx = d / 2, ry = rx * Mathf.Sin(LavaView), top = y - LavaLift(d), o = Mathf.Max(1, d * 0.04f);
        SvgPath side = EllipseArc(new SvgPath().MoveTo(x - rx, top).LineTo(x - rx, y), x, y, rx, ry, Mathf.Pi, 0, anticlockwise: true)
            .LineTo(x + rx, top).Close();
        Outlined(svg, side, Shade(MapVolcano, -0.35f), s, Out, o);
        Outlined(svg, new SvgPath().Ellipse(x, top, rx, ry), MapVolcano, s, Out, o);
        return top;
    }

    // Any building: every building squashed together into one shape (one flat colour, one outline round the whole), a
    // question mark on it, centred on (x, y), `unit` pixels to a dwelling's length. Each part: the building, where it is
    // (of unit) and how big (the dwelling bigger and higher, so its roof tops the shape). Every part's faces are filled
    // in the outline colour with a line twice the outline's width round them, then all of them in the colour.
    private static readonly (PieceKind Kind, float X, float Y, float Size)[] AnyBuildingParts =
    {
        (PieceKind.Stronghold, -0.5f, -0.2f, 1), (PieceKind.Temple, 0.55f, -0.15f, 1), (PieceKind.Dwelling, 0, -0.85f, 1.2f),
        (PieceKind.Sanctuary, -0.1f, 0.25f, 1), (PieceKind.TradingPost, 0.45f, 0.25f, 1),
    };

    private static void AnyBuilding(Svg svg, float x, float y, float unit, Color colour)
    {
        var faces = AnyBuildingParts.SelectMany(part =>
        {
            PieceArt.Picture picture = PieceArt.PictureFor(part.Kind);
            Vector2 middle = picture.Bounds.GetCenter(), at = new(x + part.X * unit, y + part.Y * unit);
            float scale = unit * part.Size / PieceArt.Unit;
            return picture.Faces.Select(f => SvgPath.Polygon(f.Points.Select(p => at + (p - middle) * scale)));
        }).ToList();
        float w = Mathf.Max(1.4f, unit * 0.07f);
        foreach (SvgPath face in faces)
        {
            svg.Fill(face, Out);
            svg.Stroke(face, Out, w * 2);
        }
        foreach (SvgPath face in faces)
        {
            svg.Fill(face, colour);
            svg.Stroke(face, colour, 0.6f);
        }
        GlyphText(svg, "?", x + unit * 0.02f, y - unit * 0.2f, unit * 1.2f, Out, colour);
    }

    // Kingdom of Ember's stronghold: a lava token under any building. A lava token; a black arrow curving under, to any
    // building (pieces: its colour; the volcano pieces' orange usually) standing on a lava token covering most of its hex.
    private static void EmberLavaUnder(Svg svg, float s, Color pieces)
    {
        float r = s * 0.28f;
        var hex = new Vector2(s * 0.16f, s * 0.1f);
        MapHex(svg, s, hex, r, MapGrey);
        float top = LavaDisc(svg, s, hex.X, hex.Y + s * 0.02f, r * LavaDiameter * 0.95f);
        AnyBuilding(svg, hex.X, top - s * 0.06f, s * 0.12f, pieces);
        LavaDisc(svg, s, -s * 0.28f, -s * 0.2f, s * 0.34f);
        BlackArrow(svg, s, new Vector2(-s * 0.3f, -s * 0.06f), new Vector2(-s * 0.26f, s * 0.3f), new Vector2(hex.X - r * 0.8f, hex.Y + s * 0.12f));
    }

    // Geologists' glowing rock: a glowing rock set in a metal socket, in a grey hex (centre `at`, corner radius r). On
    // Icon.GlowingRock the hex's corner radius is RockIconRadius of the drawing's size (6/7 of the texture's size).
    public const float RockIconRadius = 0.48f;
    private static readonly Vector2[] RockShape =
    {
        new(-0.62f, -0.1f), new(-0.35f, -0.55f), new(0.12f, -0.62f), new(0.55f, -0.32f), new(0.62f, 0.15f), new(0.3f, 0.55f), new(-0.2f, 0.6f), new(-0.58f, 0.3f),
    };

    private static void RockToken(Svg svg, Vector2 at, float r)
    {
        MapHex(svg, 0, at, r, MapGrey);
        float ro = r * 0.66f, ri = r * 0.5f;
        Outlined(svg, SvgPath.Circle(at.X, at.Y, ro), Paint.Linear(at.X - ro, at.Y - ro, at.X + ro, at.Y + ro,
            (0, new Color("eef1f4")), (0.5f, new Color("9aa3ad")), (1, new Color("4e555b"))), 0, Out, Mathf.Max(0.8f, r * 0.07f));        // the socket
        Outlined(svg, SvgPath.Circle(at.X, at.Y, ri), Paint.Radial(at.X, at.Y, at.X, at.Y, ri,
            (0, new Color("ffcf6a")), (0.6f, new Color("e8701e")), (1, new Color("5a2410"))), 0, new Color("2a1a10"), Mathf.Max(0.6f, r * 0.04f)); // its glow
        Outlined(svg, SvgPath.Polygon(RockShape.Select(p => at + p * ri * 1.6f)), Paint.Radial(at.X - ri * 0.3f, at.Y - ri * 0.4f, at.X, at.Y, ri * 1.6f,
            (0, new Color("fff3c0")), (0.45f, new Color("ffb340")), (1, new Color("c4521a"))), 0, new Color("5a2410"), Mathf.Max(0.6f, r * 0.04f)); // the rock
        Vector2 P(float x, float y) => at + new Vector2(x, y) * ri;
        svg.Stroke(new SvgPath().MoveTo(P(-0.56f, -0.88f)).LineTo(P(0.1f, -0.1f)).LineTo(P(1f, 0.24f)).MoveTo(P(0.1f, -0.1f)).LineTo(P(-0.2f, 0.96f)),
            new Color(150 / 255f, 60 / 255f, 20 / 255f, 0.6f), Mathf.Max(0.5f, r * 0.03f));                                            // its facets
    }

    // Geologists' stronghold: put a glowing rock on a hex, or take one off. Left: a rock token with a black arrow up onto
    // an empty grey hex; a slash; right: a rock token on a grey hex with a black arrow down off it to a rock token.
    private static void GeologistsRock(Svg svg, float s) => ActionWith(svg, s, () =>
    {
        MapHex(svg, s, new Vector2(-s * 0.17f, -s * 0.15f), s * 0.15f, MapGrey);
        MapHex(svg, s, new Vector2(s * 0.17f, -s * 0.15f), s * 0.15f, MapGrey);
        RockToken(svg, new Vector2(s * 0.17f, -s * 0.15f), s * 0.1f);
        RockToken(svg, new Vector2(-s * 0.17f, s * 0.2f), s * 0.11f);
        RockToken(svg, new Vector2(s * 0.17f, s * 0.2f), s * 0.11f);
        BlackArrow(svg, s, new Vector2(-s * 0.3f, s * 0.13f), new Vector2(-s * 0.34f, -s * 0.05f), new Vector2(-s * 0.25f, -s * 0.1f), 0.75f);
        BlackArrow(svg, s, new Vector2(s * 0.3f, -s * 0.12f), new Vector2(s * 0.36f, s * 0.02f), new Vector2(s * 0.28f, s * 0.12f), 0.75f);
        Slash(svg, s, new Vector2(-s * 0.05f, s * 0.3f), new Vector2(s * 0.05f, s * 0.06f));
    });

    // Goblins' treasure: an X token (an octagon, as the action markers) in black, with a glowing orange X (wide faint
    // strokes under narrow bright ones).
    private static void GoblinsTreasure(Svg svg, float s)
    {
        float r = s * 0.47f, d = s * 0.17f;
        Outlined(svg, OctagonPath(r), new Color("25211e"), s);
        svg.Stroke(OctagonPath(r * 0.86f), new Color("4a423b"), Ow(s) * 0.6f);
        SvgPath x = new SvgPath().MoveTo(-d, -d).LineTo(d, d).MoveTo(d, -d).LineTo(-d, d);
        foreach (var (w, colour) in new[]
        {
            (0.3f, new Color(1, 120 / 255f, 20 / 255f, 0.1f)), (0.22f, new Color(1, 120 / 255f, 20 / 255f, 0.18f)), (0.15f, new Color(1, 140 / 255f, 30 / 255f, 0.35f)),
            (0.085f, new Color("ff8a1e")), (0.04f, new Color("ffe0a0")),
        })
            svg.Stroke(x, colour, s * w, "round", "round");
    }
}
