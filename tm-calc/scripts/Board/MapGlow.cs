using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// Glowing edges on the map (drawn in MapView's coordinates, under the pieces):
//   Towns: a gold ring along each town's outer edge (three strokes, widest and faintest first), steady; a town
//     waiting for its key (Sparkles) also has a bright band drifting across its ring, and
//     no sparkles. For a couple of seconds after a ring first appears the glow is stronger. A town
//     that grows keeps its ring.
//   Power offers: the edges (or bridges) between the hex just built on and the buildings of a player offered
//     power, in that player's colour with a white core, a glint running along each edge and the odd sparkle.
//     Each fades in over 0.45 s when it first appears. The first waiting offer also gets a chip (PowerOfferChip)
//     over its middle edge, showing the power on offer; MapView puts it there.
// MapView works out the edges and passes them in; this only draws. Redrawn every frame while anything is shown.
public partial class MapGlow : Node2D
{
    public readonly record struct Segment(Vector2 A, Vector2 B);
    public readonly record struct Offer(string Key, Color Colour, IReadOnlyList<Segment> Segments);

    private float _radius = 74f;
    private Node2D _townLines, _powerLines;
    private Painter _live;

    private sealed class Town
    {
        public HashSet<int> Hexes;
        public List<Segment> Segments;
        public List<(Vector2 At, float Phase)> Corners;
        public float Founded;
        public bool Sparkles;
        public List<Line2D> Glows = new();
    }

    private sealed class ShownOffer
    {
        public Offer Offer;
        public float Start;
        public int Salt;
        public Node2D Lines;
    }

    private readonly List<Town> _towns = new();
    private readonly List<ShownOffer> _offers = new();
    private readonly Dictionary<string, float> _offerStarts = new();
    private bool _townsShownBefore;

    private static float Now => Time.GetTicksMsec() / 1000f;

    public override void _Ready()
    {
        _townLines = new Node2D { Name = "TownLines" };
        _powerLines = new Node2D { Name = "PowerLines" };
        _live = new Painter { Name = "Live", Paint = PaintLive };
        AddChild(_townLines);
        AddChild(_powerLines);
        AddChild(_live);
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        float now = Now;
        foreach (Town town in _towns)
            foreach (Line2D glow in town.Glows)
                glow.DefaultColor = new Color(1f, 0.745f, 0.235f, 0.18f + 0.22f * Burst(town, now));
        foreach (ShownOffer offer in _offers)
            offer.Lines.Modulate = new Color(1, 1, 1, Intro(offer, now));
        _live.QueueRedraw();
    }

    // ---------- Towns ----------

    // Every town now: its hexes and outer edges. radius: the hexes' corner radius.
    // sparkles: a town waiting for its key: the drifting band over its ring; without, the ring alone, steady.
    public void ShowTowns(IReadOnlyList<(HashSet<int> Hexes, List<Segment> Segments, bool Sparkles)> towns, float radius)
    {
        _radius = radius;
        float now = Now;
        var next = new List<Town>();
        foreach (var (hexes, segments, sparkles) in towns)
        {
            Town before = _towns.FirstOrDefault(t => t.Hexes.Overlaps(hexes));
            next.Add(new Town
            {
                Hexes = hexes,
                Segments = segments,
                Corners = CornersOf(segments),
                Sparkles = sparkles,
                // Founded now, unless it was already there (perhaps smaller), or this is the first showing.
                Founded = before?.Founded ?? (_townsShownBefore ? now : -1000f),
            });
        }
        _townsShownBefore = true;
        _towns.Clear();
        _towns.AddRange(next);

        Clear(_townLines);
        foreach (Town town in _towns)
        {
            foreach (var (points, closed) in Chains(town.Segments))
            {
                town.Glows.Add(AddLine(_townLines, points, closed, _radius * 0.2f, new Color(1f, 0.745f, 0.235f, 0.18f)));
                AddLine(_townLines, points, closed, _radius * 0.1f, new Color(1f, 0.784f, 0.314f, 0.6f));
                AddLine(_townLines, points, closed, Mathf.Max(1.5f, _radius * 0.04f), new Color("ffe39a"));
            }
        }
        UpdateProcessing();
    }

    private static float Burst(Town town, float now)
    {
        float age = now - town.Founded;
        return age >= 0 && age < 2.2f ? 1 - age / 2.2f : 0f;
    }

    // ---------- Power offers ----------

    // Every waiting offer, first to answer first. Offers already shown keep their start (no second fade-in).
    public void ShowOffers(IReadOnlyList<Offer> offers, float radius)
    {
        _radius = radius;
        float now = Now;
        foreach (string gone in _offerStarts.Keys.Where(key => offers.All(o => o.Key != key)).ToList())
            _offerStarts.Remove(gone);
        Clear(_powerLines);
        _offers.Clear();
        for (var i = 0; i < offers.Count; i++)
        {
            Offer offer = offers[i];
            if (!_offerStarts.TryGetValue(offer.Key, out float start))
                _offerStarts[offer.Key] = start = now;
            var lines = new Node2D();
            _powerLines.AddChild(lines);
            foreach (var (points, closed) in Chains(offer.Segments.ToList()))
            {
                AddLine(lines, points, closed, _radius * 0.24f, new Color(offer.Colour, 0.38f));
                AddLine(lines, points, closed, _radius * 0.12f, new Color(offer.Colour, 0.9f));
                AddLine(lines, points, closed, Mathf.Max(1.5f, _radius * 0.045f), Colors.White);
            }
            _offers.Add(new ShownOffer { Offer = offer, Start = start, Salt = i, Lines = lines });
        }
        UpdateProcessing();
    }

    private static float Intro(ShownOffer offer, float now)
    {
        float t = Mathf.Clamp((now - offer.Start) / 0.45f, 0f, 1f);
        return 1 - MathF.Pow(1 - t, 3);
    }

    // ---------- Drawing every frame: the town bands and sparkles, the power glints ----------

    private void PaintLive(Painter g)
    {
        float now = Now, r = _radius;
        foreach (Town town in _towns)
        {
            if (!town.Sparkles)
                continue; // a town formed: its ring alone, steady
            // A bright band drifting across the ring: each edge in six pieces, each lit by where it is.
            foreach (Segment s in town.Segments)
                for (var j = 0; j < 6; j++)
                {
                    Vector2 p0 = s.A.Lerp(s.B, j / 6f), p1 = s.A.Lerp(s.B, (j + 1) / 6f), mid = (p0 + p1) / 2;
                    float at = (mid.X * 0.8f + mid.Y * 0.6f) / (r * 7) - now * 0.3f;
                    float band = MathF.Pow(MathF.Max(0f, MathF.Cos(Mathf.Tau * at)), 18f);
                    if (band >= 0.03f)
                        g.DrawLine(p0, p1, new Color(1, 1, 1, band * 0.9f), r * 0.07f, true);
                }
        }
        foreach (ShownOffer offer in _offers)
        {
            float alpha = Intro(offer, now);
            IReadOnlyList<Segment> segments = offer.Offer.Segments;
            for (var i = 0; i < segments.Count; i++)
            {
                Segment s = segments[i];
                float u = Frac(now * 0.6f + i * 0.37f + offer.Salt * 0.21f);
                Glint(g, s.A.Lerp(s.B, u), r * 0.2f, 0.9f * MathF.Sin(MathF.PI * u) * alpha);
                float cycle = Frac(now * 0.4f + i * 0.53f + offer.Salt * 0.17f);
                if (cycle < 0.1f)
                {
                    float e = MathF.Sin(MathF.PI * cycle / 0.1f);
                    Sparkle(g, (s.A + s.B) / 2, r * 0.2f * e, e * alpha, true);
                }
            }
        }
    }

    // A soft white spot: circles one over another, fading out towards `radius`.
    internal static void Glint(CanvasItem g, Vector2 at, float radius, float alpha)
    {
        if (alpha <= 0.01f)
            return;
        const int rings = 6;
        for (var k = rings; k >= 1; k--)
            g.DrawCircle(at, radius * k / rings, new Color(1, 1, 1, alpha / rings));
    }

    // A four-pointed star with curved sides, `size` from the centre to a point, over a warm glow.
    internal static void Sparkle(CanvasItem g, Vector2 at, float size, float alpha, bool glow)
    {
        if (size <= 0.2f || alpha <= 0f)
            return;
        alpha = MathF.Min(1f, alpha);
        if (glow)
        {
            const int rings = 6;
            for (var k = rings; k >= 1; k--)
                g.DrawCircle(at, size * 1.5f * k / rings, new Color(1f, 0.925f, 0.667f, 0.75f * alpha / rings));
        }
        float k2 = size * 0.08f;
        Vector2[] tips = { new(0, -size), new(size, 0), new(0, size), new(-size, 0) };
        Vector2[] bends = { new(k2, -k2), new(k2, k2), new(-k2, k2), new(-k2, -k2) };
        var points = new List<Vector2>();
        for (var i = 0; i < 4; i++)
        {
            Vector2 p0 = tips[i], c = bends[i], p1 = tips[(i + 1) % 4];
            for (var t = 0; t < 6; t++)
            {
                float u = t / 6f;
                points.Add(at + (1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * c + u * u * p1);
            }
        }
        g.DrawColoredPolygon(points.ToArray(), new Color(1, 1, 1, alpha));
    }

    // ---------- Helpers ----------

    private void UpdateProcessing()
    {
        bool any = _towns.Count > 0 || _offers.Count > 0;
        SetProcess(any);
        _live.QueueRedraw();
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Line2D AddLine(Node parent, Vector2[] points, bool closed, float width, Color colour)
    {
        var line = new Line2D
        {
            Points = points,
            Closed = closed,
            Width = width,
            DefaultColor = colour,
            JointMode = Line2D.LineJointMode.Round,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
            Antialiased = true,
        };
        parent.AddChild(line);
        return line;
    }

    private static (int, int) Key(Vector2 p) => (Mathf.RoundToInt(p.X * 2), Mathf.RoundToInt(p.Y * 2));

    // The corners along the edges, once each, with a steady pseudo-random phase (so sparkles take turns).
    private static List<(Vector2, float)> CornersOf(List<Segment> segments)
    {
        var corners = new Dictionary<(int, int), Vector2>();
        foreach (Segment s in segments)
        {
            corners.TryAdd(Key(s.A), s.A);
            corners.TryAdd(Key(s.B), s.B);
        }
        return corners.Select(pair => (pair.Value, Hash01(pair.Key))).ToList();
    }

    private static float Hash01((int X, int Y) key)
    {
        unchecked
        {
            uint h = 2166136261;
            h = (h ^ (uint)key.X) * 16777619;
            h = (h ^ (uint)key.Y) * 16777619;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // Edges joined end to end into as few lines as possible (closed where a line comes back to its start), so each
    // stroke is drawn once, with round joins, and see-through strokes don't double up at the corners.
    private static List<(Vector2[] Points, bool Closed)> Chains(List<Segment> segments)
    {
        var at = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            foreach (Vector2 end in new[] { segments[i].A, segments[i].B })
            {
                if (!at.TryGetValue(Key(end), out List<int> list))
                    at[Key(end)] = list = new List<int>();
                list.Add(i);
            }
        }
        var used = new bool[segments.Count];
        var chains = new List<(Vector2[], bool)>();
        for (var first = 0; first < segments.Count; first++)
        {
            if (used[first])
                continue;
            used[first] = true;
            var points = new LinkedList<Vector2>(new[] { segments[first].A, segments[first].B });
            // Onwards from the last point, then back from the first.
            for (var direction = 0; direction < 2; direction++)
            {
                while (true)
                {
                    Vector2 end = direction == 0 ? points.Last!.Value : points.First!.Value;
                    int next = at[Key(end)].FirstOrDefault(i => !used[i], -1);
                    if (next < 0)
                        break;
                    used[next] = true;
                    Vector2 other = Key(segments[next].A) == Key(end) ? segments[next].B : segments[next].A;
                    if (direction == 0)
                        points.AddLast(other);
                    else
                        points.AddFirst(other);
                }
            }
            bool closed = points.Count > 3 && Key(points.First!.Value) == Key(points.Last!.Value);
            if (closed)
                points.RemoveLast();
            chains.Add((points.ToArray(), closed));
        }
        return chains;
    }
}
