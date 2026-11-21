using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The map's hexes and the bridges, drawn in code:
//   land   a flat fill in the terrain's colour with a thick dark outline (9% of the hex radius);
//   river  a flat fill with sand banks along every edge that faces land: one band per run of such edges, a
//          curved sand fillet where two banks meet inside the hex, and a rounded sand corner where a land point
//          juts between two river hexes. One rule covers all 64 patterns of land and river neighbours.
//   bridge a plain rectangle in the owner's colour, outlined inside, as long as the gap between the two land
//          hexes' facing points less 0.12 of a radius at each end.
// Hexes are pointy-topped. Edge d runs from corner d to corner d + 1; corner i is at (60i − 30)° (y down), so
// edges 0..5 face E, SE, SW, W, NW, NE. Textures are made once and reused.
public static class HexArt
{
    // The hex radius in texture pixels: 74 for the 128 × 148 hex art (128 = √3 × 74).
    public const float Radius = 74f;

    private static readonly Dictionary<Terrain, Color> TerrainColours = new()
    {
        [Terrain.Plains] = new Color("7d5230"),
        [Terrain.Swamp] = new Color("33302e"),
        [Terrain.Lake] = new Color("2f72b8"),
        [Terrain.Forest] = new Color("2f7d3b"),
        [Terrain.Mountain] = new Color("8c8f93"),
        [Terrain.Wasteland] = new Color("b8382f"),
        [Terrain.Desert] = new Color("e2bf47"),
        [Terrain.Ice] = new Color("e4edf2"),
        [Terrain.Volcano] = new Color("e2702c"),
        [Terrain.River] = new Color("9ccbe6"),
    };

    // A terrain's flat colour, as the hexes are filled (the colour wheel uses it too).
    public static Color TerrainColour(Terrain terrain) => TerrainColours[terrain];
    private static readonly Color Outline = new("161a18");
    public static Color OutlineColour => Outline;
    // River banks, as opaque colours (the page's translucent ones laid over the river and the sand).
    private static readonly Color Sand = Color.Color8(226, 204, 146);
    private static readonly Color SandHaze = Color.Color8(174, 202, 205); // faint sand reaching further into the water
    private static readonly Color Shore = Color.Color8(122, 137, 80);     // the thin line where the land meets the sand

    private static float OutlineWidth(float r) => Mathf.Max(2f, r * 0.09f);
    private static float Pad(float r) => OutlineWidth(r) / 2 + 2f;
    private static Vector2 SizeFor(float r) => new(MathF.Ceiling(MathF.Sqrt(3) * r + 2 * Pad(r)), MathF.Ceiling(2 * r + 2 * Pad(r)));

    private static readonly Dictionary<(Terrain, float), VectorTexture> LandTextures = new();
    private static readonly Dictionary<(int, float), VectorTexture> RiverTextures = new();

    // ---------- Land ----------

    public static Texture2D Land(Terrain terrain, float r = Radius)
    {
        if (LandTextures.TryGetValue((terrain, r), out VectorTexture known))
            return known;
        var texture = LandFilled(TerrainColours[terrain], r);
        LandTextures[(terrain, r)] = texture;
        return texture;
    }

    private static readonly Dictionary<(Color, float), VectorTexture> FilledTextures = new();

    // A land hex in any colour (Calculator: a neutral dwelling's hex in the power purple).
    public static Texture2D LandIn(Color fill, float r = Radius)
    {
        if (!FilledTextures.TryGetValue((fill, r), out VectorTexture texture))
            FilledTextures[(fill, r)] = texture = LandFilled(fill, r);
        return texture;
    }

    private static VectorTexture LandFilled(Color fill, float r)
    {
        Vector2 size = SizeFor(r), c = size / 2;
        float w = OutlineWidth(r);
        var shapes = new List<VectorTexture.Shape> { new(Corners(c, r), fill) };
        // The outline: a band of width w centred on the edge, as one quad per edge (mitred corners, no gaps).
        float slant = MathF.Cos(MathF.PI / 6); // corner distance = edge distance / cos 30°
        Vector2[] outer = Corners(c, r + w / 2 / slant), inner = Corners(c, r - w / 2 / slant);
        for (var d = 0; d < 6; d++)
            shapes.Add(new(new[] { outer[d], outer[(d + 1) % 6], inner[(d + 1) % 6], inner[d] }, Outline));
        var texture = new VectorTexture();
        texture.Show(size, shapes);
        return texture;
    }

    // ---------- River ----------

    // land[d]: the neighbour across edge d is land (not river, not off the map).
    public static Texture2D River(bool[] land, float r = Radius)
    {
        int mask = Enumerable.Range(0, 6).Sum(d => land[d] ? 1 << d : 0);
        if (RiverTextures.TryGetValue((mask, r), out VectorTexture known))
            return known;

        Vector2 size = SizeFor(r), c = size / 2;
        Vector2[] p = Corners(c, r);
        var shapes = new List<VectorTexture.Shape> { new(Corners(c, r + 0.5f), TerrainColours[Terrain.River]) };
        List<List<int>> runs = BankRuns(land, out bool closed);
        float h = r * 0.18f; // how far the sand reaches into the river hex

        Vector2 Inward(int d) => (c - (p[d] + p[(d + 1) % 6]) / 2).Normalized();
        // Corner v moved in towards the centre so that it sits `depth` inside both edges that meet there.
        Vector2 Inset(int v, float depth) => c + (p[v] - c) * ((r - depth / MathF.Cos(MathF.PI / 6)) / r);

        // A band `depth` deep along every land-facing edge. Where two such edges meet, the bands join at the
        // inset corner; where a run ends, the band ends square to its edge.
        void Bands(float depth, Color colour)
        {
            foreach (List<int> run in runs)
                for (var i = 0; i < run.Count; i++)
                {
                    int d = run[i], from = d, to = (d + 1) % 6;
                    bool joinedBefore = closed || i > 0, joinedAfter = closed || i < run.Count - 1;
                    Vector2 fromInner = joinedBefore ? Inset(from, depth) : p[from] + Inward(d) * depth;
                    Vector2 toInner = joinedAfter ? Inset(to, depth) : p[to] + Inward(d) * depth;
                    shapes.Add(new(new[] { p[from], p[to], toInner, fromInner }, colour));
                }
        }

        Bands(r * 0.28f, SandHaze);
        Bands(h, Sand);

        // Where two banks meet inside the hex, the water would have a sharp corner: fill it with a curved fillet.
        void Fillet(int v)
        {
            Vector2 at = p[v], a = p[(v + 5) % 6], b = p[(v + 1) % 6];
            Vector2 t1 = (a - at).Normalized(), t2 = (b - at).Normalized();
            Vector2 n1 = (c - (at + a) / 2).Normalized(), n2 = (c - (at + b) / 2).Normalized();
            float s0 = h / MathF.Tan(MathF.PI / 3), s1 = s0 + h * 1.4f; // where the sand edges meet; where the fillet starts
            Vector2 control = at + t1 * s0 + n1 * h;
            Vector2 start = at + t1 * s1 + n1 * h, end = at + t2 * s1 + n2 * h;
            var points = new List<Vector2> { at, at + t1 * s1 };
            const int steps = 12;
            for (var k = 0; k <= steps; k++)
            {
                float t = (float)k / steps, u = 1 - t;
                points.Add(start * u * u + control * 2 * u * t + end * t * t); // quadratic curve
            }
            points.Add(at + t2 * s1);
            shapes.Add(new(points.ToArray(), Sand));
        }

        // Where a bank stops at a corner, the land point carries on between two river hexes: round it off.
        void RoundedPoint(int v)
        {
            Vector2 at = p[v];
            float from = (p[(v + 1) % 6] - at).Angle(), to = (p[(v + 5) % 6] - at).Angle();
            float sweep = Mathf.Wrap(to - from, -MathF.PI, MathF.PI); // the 120° inside the hex
            var points = new List<Vector2> { at };
            const int steps = 12;
            for (var k = 0; k <= steps; k++)
                points.Add(at + Vector2.FromAngle(from + sweep * k / steps) * h);
            shapes.Add(new(points.ToArray(), Sand));
        }

        foreach (List<int> run in runs)
        {
            for (var i = 0; i < run.Count - (closed ? 0 : 1); i++)
                Fillet((run[i] + 1) % 6);
            if (!closed)
            {
                RoundedPoint(run[0]);
                RoundedPoint((run[^1] + 1) % 6);
            }
        }

        Bands(r * 0.035f, Shore);

        var texture = new VectorTexture();
        texture.Show(size, shapes);
        RiverTextures[(mask, r)] = texture;
        return texture;
    }

    // Runs of land-facing edges next to each other, in order round the hex, so a bank bends round a corner as
    // one piece. closed: every edge faces land (one run all the way round).
    private static List<List<int>> BankRuns(bool[] land, out bool closed)
    {
        closed = land.All(l => l);
        if (closed)
            return new List<List<int>> { Enumerable.Range(0, 6).ToList() };
        var runs = new List<List<int>>();
        int start = Array.IndexOf(land, false);
        List<int> current = null;
        for (var k = 1; k <= 6; k++)
        {
            int d = (start + k) % 6;
            if (!land[d])
            {
                current = null;
                continue;
            }
            if (current is null)
            {
                current = new List<int>();
                runs.Add(current);
            }
            current.Add(d);
        }
        return runs;
    }

    // Which edge of a hex faces a neighbour that is `offset` away (centre to centre): 0 = E, 1 = SE, ... 5 = NE.
    public static int EdgeTowards(Vector2 offset) =>
        ((int)MathF.Round(offset.Angle() / (MathF.PI / 3)) % 6 + 6) % 6;

    public static Vector2[] Corners(Vector2 c, float r) =>
        Enumerable.Range(0, 6).Select(i => c + Vector2.FromAngle((60 * i - 30) * MathF.PI / 180) * r).ToArray();

    // ---------- Bridges ----------

    private static readonly Dictionary<(Color, Color), VectorTexture> BridgeTextures = new();

    // A bridge lying left to right, centred, at the icons' size and outline (BridgeLength × BridgeWidth): across the
    // gap between the facing points of two land hexes with one river edge between them (one radius), with room at
    // each end.
    public static Texture2D Bridge(Color colour, Color outline, float r = Radius)
    {
        if (BridgeTextures.TryGetValue((colour, outline), out VectorTexture known) && r == Radius)
            return known;
        float length = r * BridgeLength, width = r * BridgeWidth, o = Mathf.Max(1.2f, r * 0.032f);
        var size = new Vector2(MathF.Ceiling(length + 2), MathF.Ceiling(width + 2));
        Vector2 c = size / 2;
        Vector2[] Rect(float l, float w) => new[]
        {
            c + new Vector2(-l / 2, -w / 2), c + new Vector2(l / 2, -w / 2), c + new Vector2(l / 2, w / 2), c + new Vector2(-l / 2, w / 2),
        };
        var texture = new VectorTexture();
        // The outline is drawn inside the rectangle, so it never spills onto the hexes.
        texture.Show(size, new VectorTexture.Shape[] { new(Rect(length, width), outline), new(Rect(length - 2 * o, width - 2 * o), colour) });
        if (r == Radius && !Engine.IsEditorHint())
            BridgeTextures[(colour, outline)] = texture;
        return texture;
    }

    private static readonly Dictionary<(Color, Color), VectorTexture> BridgeIcons = new();

    // Every bridge's size, in hex radii, on the map and as an icon alike (the icon is the map's bridge turned): 0.8 of
    // the old map bridge's length (0.76) and 0.48 of its width (0.28), so pieces and icons match.
    private const float BridgeLength = 0.76f * 0.8f, BridgeWidth = 0.28f * 0.8f * 0.6f;

    // A bridge as an icon (resource bundles, a player's bridge supply): the map's bridge turned 45° anticlockwise
    // (lower left to upper right), centred in a square texture. Its outline is as thick as the worker's and priest's
    // on a 48 px icon (about 2.4 px).
    public static Texture2D BridgeIcon(Color colour, Color outline, float r = Radius)
    {
        if (BridgeIcons.TryGetValue((colour, outline), out VectorTexture known) && r == Radius)
            return known;
        float length = r * BridgeLength, width = r * BridgeWidth, o = Mathf.Max(1.2f, r * 0.032f);
        float side = MathF.Ceiling((length + width) / MathF.Sqrt(2) + 2);
        var c = new Vector2(side / 2, side / 2);
        Vector2 along = Vector2.FromAngle(-MathF.PI / 4), across = along.Orthogonal(); // up and to the right
        Vector2[] Rect(float l, float w) => new[]
        {
            c - along * l / 2 - across * w / 2, c + along * l / 2 - across * w / 2,
            c + along * l / 2 + across * w / 2, c - along * l / 2 + across * w / 2,
        };
        var texture = new VectorTexture();
        texture.Show(new Vector2(side, side), new VectorTexture.Shape[] { new(Rect(length, width), outline), new(Rect(length - 2 * o, width - 2 * o), colour) });
        if (r == Radius && !Engine.IsEditorHint())
            BridgeIcons[(colour, outline)] = texture;
        return texture;
    }
}
