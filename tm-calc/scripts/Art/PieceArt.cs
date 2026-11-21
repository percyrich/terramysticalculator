using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The five buildings and the priest, drawn in code instead of from image files. Each building is a simple solid
// (a gabled block, a cylinder, a rounded oblong, ...) seen from 30° above the side and turned 45°, each face shaded
// by how much it faces the light, with a thick outline round the whole piece and thin lines where faces meet. The
// priest is a flat figure seen almost from the front (see PriestPicture).
//
// TextureFor() returns a DrawnPieceTexture: a Texture2D that holds no pixels and instead draws the piece's
// polygons whenever something draws it. So it goes anywhere a PNG went (Sprite2D, the icons ResourceBundle
// makes), and stays sharp at any scale. IconArt draws the same pictures into its icons.
public static class PieceArt
{
    // ---------- The view and the look ----------

    private static readonly float Elevation = 30f * MathF.PI / 180f; // how far above the side the piece is seen from
    private static readonly float Turn = -45f * MathF.PI / 180f;     // how far the piece is turned about the vertical
    // One unit (a dwelling's length) in texture pixels: 0.64 of a hex's radius, which is 74 in the 128×148 hex art.
    internal const float Unit = 0.64f * 74f;
    internal const float PriestHeight = 0.9f * Unit; // the priest figure's height, not counting its thickness
    internal const float OutlineWidth = 3.3f; // the thick line round the whole piece, in texture pixels
    internal const float EdgeWidth = 1.2f;    // the thin lines where two faces meet
    internal const float SmoothWidth = 1f;    // a soft line round each face, so polygon edges aren't jagged
    internal static readonly Color ShadowColour = new(0f, 0f, 0f, 0.33f);
    private static readonly Vector2 ShadowOffset = new(0.07f * Unit, 0.05f * Unit); // the ground shadow: down and right
    // Where the light comes from, in the turned piece's frame: the right, the front and above.
    private static readonly Vector3 Light = new Vector3(0.45f, -0.55f, 0.75f).Normalized();

    // How strongly faces darken away from the light (1 = fully). The flat-sided pieces are toned down by half.
    private static float ShadeStrength(PieceKind kind) =>
        kind is PieceKind.Dwelling or PieceKind.TradingPost or PieceKind.Priest ? 0.5f : 1f;

    // Solids that aren't game pieces but are drawn the same way, for icons (IconArt): a worker cube, a power token,
    // the hexagonal plate the "when built" icons stand a building on, and a bridge (the Architects' stronghold icon).
    internal enum Prop { Worker, PowerToken, Plate, Bridge }

    // The texture for one kind of building in one colour. Every piece has the same view; only colours differ.
    // At run time each texture is made once and reused. In the editor (ResourceBundle runs there too) a new one
    // is made each time: Godot objects kept in a static field can stop the editor reloading changed scripts.
    public static Texture2D TextureFor(PieceKind kind, Color colour, Color outline)
    {
        bool cache = !Engine.IsEditorHint();
        if (cache && Textures.TryGetValue((kind, colour, outline), out DrawnPieceTexture known))
            return known;
        var texture = new DrawnPieceTexture();
        texture.Show(PictureFor(kind), colour, outline);
        if (cache)
            Textures[(kind, colour, outline)] = texture;
        return texture;
    }

    private static readonly Dictionary<(PieceKind, Color, Color), DrawnPieceTexture> Textures = new();

    // A face's colour: the piece colour darkened towards 55% in full shade, lifted 16% towards white in full light.
    internal static Color FaceColour(Color piece, float lit)
    {
        float Channel(float c) => c * 0.55f + (c + (1f - c) * 0.16f - c * 0.55f) * lit;
        return new Color(Channel(piece.R), Channel(piece.G), Channel(piece.B));
    }

    // ---------- The solids ----------
    // Units: 1 = a dwelling's length. x runs along a piece's length, y across it, z up; the base sits at z = 0.

    private sealed class Face
    {
        public Vector3[] Corners; // in order round the face, counter-clockwise seen from outside the solid
        public bool[] Hard;       // Hard[i]: draw a thin line along the edge from corner i to corner i + 1
        public Vector3 Normal;    // pointing out of the solid
        public Vector3 Centre;
    }

    private sealed class Solid
    {
        public List<Face> Faces;
        public Vector2[] Footprint; // the base seen from above, for the ground shadow
        public bool IsWalls;        // a footprint pushed straight up: Faces[0] is the top, the rest are walls
    }

    private static Solid SolidFor(PieceKind kind) => kind switch
    {
        PieceKind.Dwelling => Dwelling(),
        PieceKind.TradingPost => TradingPost(),
        PieceKind.Temple => Walls(Circle(0.665f), 0.5f),  // as wide on screen as the trading post
        PieceKind.Sanctuary => Walls(Stadium(1.5f, 0.5f), 1.15f),
        PieceKind.Stronghold => Walls(Lobed(tipRadius: 0.22f, reach: 0.86f, waist: 0.55f, phase: MathF.PI / 4), 1.15f),
        _ => throw new ArgumentException($"{kind} has no solid"),
    };

    private static Solid SolidFor(Prop prop) => prop switch
    {
        Prop.Worker => Walls(Rectangle(-0.5f, 0.5f, 0.5f), 1f),
        Prop.PowerToken => Walls(Circle(0.5f, 40), 0.36f),
        Prop.Plate => Walls(Hexagon(0.5f), 0.06f),
        // a long low bar, turned BridgeTurn more than the pieces, so it runs from lower left to upper right on screen
        Prop.Bridge => Walls(Rectangle(-0.9f, 0.9f, 0.13f).Select(p => p.Rotated(BridgeTurn)).ToArray(), 0.2f),
        _ => throw new ArgumentException($"{prop} has no solid"),
    };

    private static readonly float BridgeTurn = 80f * MathF.PI / 180f; // with the view's -45°: 35°, as the bridge in the icons

    // A long block with a ridged roof along its length; the pointed gable ends face along x.
    private static Solid Dwelling()
    {
        const float x0 = -0.5f, x1 = 0.5f, w = 0.3f, wall = 0.42f, ridge = 0.68f;
        Vector3[] Gable(float x) => new[] { V(x, -w, 0), V(x, w, 0), V(x, w, wall), V(x, 0, ridge), V(x, -w, wall) };
        Vector3[] Side(float y) => new[] { V(x0, y, 0), V(x1, y, 0), V(x1, y, wall), V(x0, y, wall) };
        Vector3[] Slope(float y) => new[] { V(x0, y, wall), V(x1, y, wall), V(x1, 0, ridge), V(x0, 0, ridge) };
        return FromFaces(new[]
        {
            (Side(-w), V(0, -1, 0)), (Side(w), V(0, 1, 0)),
            (Slope(-w), V(0, -1, 1)), (Slope(w), V(0, 1, 1)),
            (Gable(x0), V(-1, 0, 0)), (Gable(x1), V(1, 0, 0)),
        }, Rectangle(x0, x1, w));
    }

    // A raised block with a pointed roof at the back, and a lower block in front of it. The roof's ridge runs
    // across the piece, so its pointed gables are on the long sides, each of which is one L-shaped face.
    private static Solid TradingPost()
    {
        const float x0 = -0.62f, xm = 0f, x1 = 0.62f, w = 0.32f, low = 0.42f, wall = 0.72f, ridge = 1f;
        const float xr = (x0 + xm) / 2;
        Vector3[] Side(float y) => new[] { V(x0, y, 0), V(x1, y, 0), V(x1, y, low), V(xm, y, low), V(xm, y, wall), V(xr, y, ridge), V(x0, y, wall) };
        return FromFaces(new[]
        {
            (Side(-w), V(0, -1, 0)), (Side(w), V(0, 1, 0)),
            (new[] { V(x0, -w, wall), V(xr, -w, ridge), V(xr, w, ridge), V(x0, w, wall) }, V(-1, 0, 1)), // roof, back slope
            (new[] { V(xr, -w, ridge), V(xm, -w, wall), V(xm, w, wall), V(xr, w, ridge) }, V(1, 0, 1)),   // roof, front slope
            (new[] { V(x0, -w, 0), V(x0, w, 0), V(x0, w, wall), V(x0, -w, wall) }, V(-1, 0, 0)),          // back wall
            (new[] { V(xm, -w, low), V(xm, w, low), V(xm, w, wall), V(xm, -w, wall) }, V(1, 0, 0)),       // wall above the low block
            (new[] { V(xm, -w, low), V(x1, -w, low), V(x1, w, low), V(xm, w, low) }, V(0, 0, 1)),         // low block, top
            (new[] { V(x1, -w, 0), V(x1, w, 0), V(x1, w, low), V(x1, -w, low) }, V(1, 0, 0)),             // low block, front
        }, Rectangle(x0, x1, w));
    }

    // Faces listed directly, each with a rough outward direction: a face whose corners run the wrong way round
    // is reversed. All their edges are drawn (every edge here is a crease between flat faces).
    private static Solid FromFaces((Vector3[] Corners, Vector3 Outward)[] faces, Vector2[] footprint) => new()
    {
        Faces = faces.Select(f => MakeFace(NormalOf(f.Corners).Dot(f.Outward) < 0 ? f.Corners.Reverse().ToArray() : f.Corners)).ToList(),
        Footprint = footprint,
    };

    // A footprint (counter-clockwise seen from above) pushed straight up to height h. A line runs down the wall
    // only at corners sharper than 25°, so curved walls stay smooth.
    private static Solid Walls(Vector2[] footprint, float h)
    {
        int n = footprint.Length;
        var sharp = new bool[n];
        for (var i = 0; i < n; i++)
        {
            Vector2 before = footprint[i] - footprint[(i - 1 + n) % n], after = footprint[(i + 1) % n] - footprint[i];
            sharp[i] = Mathf.Abs(before.AngleTo(after)) > 25f * MathF.PI / 180f;
        }
        var faces = new List<Face> { MakeFace(footprint.Select(p => V(p.X, p.Y, h)).ToArray()) };
        for (var i = 0; i < n; i++)
        {
            Vector2 a = footprint[i], b = footprint[(i + 1) % n];
            faces.Add(MakeFace(new[] { V(a.X, a.Y, 0), V(b.X, b.Y, 0), V(b.X, b.Y, h), V(a.X, a.Y, h) },
                new[] { false, sharp[(i + 1) % n], true, sharp[i] })); // bottom, right, top, left
        }
        return new Solid { Faces = faces, Footprint = footprint, IsWalls = true };
    }

    private static Face MakeFace(Vector3[] corners, bool[] hard = null) => new()
    {
        Corners = corners,
        Hard = hard ?? corners.Select(_ => true).ToArray(),
        Normal = NormalOf(corners),
        Centre = corners.Aggregate(Vector3.Zero, (sum, c) => sum + c) / corners.Length,
    };

    // Newell's method: a flat polygon's normal, following its corner order (counter-clockwise gives outward).
    private static Vector3 NormalOf(Vector3[] c)
    {
        Vector3 n = Vector3.Zero;
        for (var i = 0; i < c.Length; i++)
        {
            Vector3 a = c[i], b = c[(i + 1) % c.Length];
            n += V((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return n.Normalized();
    }

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    // ---------- Footprints (seen from above, counter-clockwise) ----------

    private static Vector2[] Rectangle(float x0, float x1, float halfWidth) =>
        new[] { new Vector2(x0, -halfWidth), new Vector2(x1, -halfWidth), new Vector2(x1, halfWidth), new Vector2(x0, halfWidth) };

    private static Vector2[] Circle(float radius, int steps = 48) =>
        Enumerable.Range(0, steps).Select(i => Vector2.FromAngle(i * 2 * MathF.PI / steps) * radius).ToArray();

    // Six corners `radius` from the middle, the first 30° round from x.
    private static Vector2[] Hexagon(float radius) =>
        Enumerable.Range(0, 6).Select(i => Vector2.FromAngle(MathF.PI / 6 + i * MathF.PI / 3) * radius).ToArray();

    // A rectangle with a half circle on each end: `length` long overall, `radius` half as wide.
    private static Vector2[] Stadium(float length, float radius, int stepsPerEnd = 24)
    {
        float centre = length / 2 - radius; // from the middle to the centre of each end's half circle
        var points = new List<Vector2>();
        for (var i = 0; i <= stepsPerEnd; i++)
            points.Add(new Vector2(centre, 0) + Vector2.FromAngle(-MathF.PI / 2 + i * MathF.PI / stepsPerEnd) * radius);
        for (var i = 0; i <= stepsPerEnd; i++)
            points.Add(new Vector2(-centre, 0) + Vector2.FromAngle(MathF.PI / 2 + i * MathF.PI / stepsPerEnd) * radius);
        return points.ToArray();
    }

    // The stronghold: four round tips joined by single inward curves. Each side is an arc of a circle sitting
    // outside the shape on a diagonal, just touching both neighbouring tips, so it sweeps smoothly from tip to
    // tip. tipRadius: how round the tips are; reach: how far they extend from the centre; waist: how far from
    // the centre the middle of each side is; phase: the direction of the first tip.
    private static Vector2[] Lobed(float tipRadius, float reach, float waist, float phase, int stepsPerTip = 14, int stepsPerSide = 22)
    {
        float d = reach - tipRadius, k = waist - tipRadius;
        // The side circle's centre is `far` from the middle and its radius `sideRadius`: chosen so it touches
        // both tip circles and passes `waist` from the middle.
        float far = (k * k - d * d) / (2 * k - MathF.Sqrt(2) * d), sideRadius = far - waist;
        Vector2 Tip(int i) => Vector2.FromAngle(phase + i * MathF.PI / 2) * d;
        Vector2 Side(int i) => Vector2.FromAngle(phase + i * MathF.PI / 2 + MathF.PI / 4) * far;
        Vector2 Touch(Vector2 tip, Vector2 side) => side + (tip - side).Normalized() * sideRadius; // where they meet

        var points = new List<Vector2>();
        void Arc(Vector2 centre, float radius, Vector2 from, Vector2 to, int steps)
        {
            float start = (from - centre).Angle();
            float sweep = Mathf.Wrap((to - centre).Angle() - start, -MathF.PI, MathF.PI); // the short way round
            for (var j = 0; j < steps; j++)
                points.Add(centre + Vector2.FromAngle(start + sweep * j / steps) * radius);
        }
        for (var i = 0; i < 4; i++)
        {
            Arc(Tip(i), tipRadius, Touch(Tip(i), Side(i - 1)), Touch(Tip(i), Side(i)), stepsPerTip);        // round tip
            Arc(Side(i), sideRadius, Touch(Tip(i), Side(i)), Touch(Tip(i + 1), Side(i)), stepsPerSide);     // inward curve
        }
        return points.ToArray();
    }

    // ---------- The priest ----------
    // A flat figure standing upright, seen almost from the front as on the tiles. Its front is the outline from
    // an SVG drawing; its back is the same outline pushed up and to the left, so its left
    // side and top show. Worked out directly on screen (there is no solid to turn).

    // The SVG's outline in fractions of its height, centred (x right, y down).
    private static readonly Vector2[] PriestOutline =
    {
        new(-0.2996f, -0.0358f), new(-0.3023f, -0.0701f), new(-0.2979f, -0.0993f), new(-0.2864f, -0.1265f),
        new(-0.2686f, -0.1501f), new(-0.2443f, -0.1709f), new(-0.2122f, -0.1892f), new(-0.1729f, -0.204f),
        new(-0.132f, -0.213f), new(-0.1318f, -0.2867f), new(-0.1234f, -0.3528f), new(-0.1075f, -0.4071f),
        new(-0.0839f, -0.4503f), new(-0.0581f, -0.4767f), new(-0.0278f, -0.493f), new(0.0084f, -0.5f),
        new(0.0424f, -0.496f), new(0.0599f, -0.4869f), new(0.0742f, -0.4734f), new(0.0849f, -0.4568f),
        new(0.094f, -0.434f), new(0.1031f, -0.3781f), new(0.1008f, -0.3075f), new(0.157f, -0.343f),
        new(0.1822f, -0.3532f), new(0.2039f, -0.3578f), new(0.2211f, -0.3572f), new(0.2371f, -0.3511f),
        new(0.2494f, -0.3392f), new(0.2573f, -0.3239f), new(0.2657f, -0.293f), new(0.2675f, -0.2635f),
        new(0.2633f, -0.2317f), new(0.2536f, -0.2012f), new(0.2372f, -0.1681f), new(0.2151f, -0.1354f),
        new(0.1877f, -0.1037f), new(0.1571f, -0.0751f), new(0.3023f, 0.1763f), new(-0.2555f, 0.5f),
        new(-0.1321f, 0.0663f), new(-0.1639f, 0.0697f), new(-0.1935f, 0.0681f), new(-0.2199f, 0.0621f),
        new(-0.2446f, 0.051f), new(-0.2641f, 0.036f), new(-0.2797f, 0.0169f), new(-0.2918f, -0.0073f),
    };

    private static Picture PriestPicture()
    {
        float h = PriestHeight;
        Vector2 back = new Vector2(-0.21f, -0.12f) * h; // where the back face sits, relative to the front
        Vector2 direction = back.Normalized();
        // The front, moved half the thickness down and right, so front and back together are centred.
        Vector2[] front = PriestOutline.Select(p => p * h - back / 2).ToArray();
        int n = front.Length;
        Vector2 Corner(int i) => front[(i % n + n) % n];

        // Which way round the outline runs on screen (y is down; 1 is clockwise), so each edge's outward side is known.
        float area = 0f;
        for (var i = 0; i < n; i++)
            area += Corner(i).Cross(Corner(i + 1));
        float turn = MathF.Sign(area);
        Vector2 Outward(int i)
        {
            Vector2 along = (Corner(i + 1) - Corner(i)).Normalized();
            return new Vector2(along.Y, -along.X) * turn;
        }
        // A line down the side only at outward corners sharper than 25°: where the outline turns inward, or bends
        // gently, the side stays smooth.
        bool Sharp(int i)
        {
            Vector2 before = Corner(i) - Corner(i - 1), after = Corner(i + 1) - Corner(i);
            return before.Cross(after) * turn > 0 && Mathf.Abs(before.AngleTo(after)) > 25f * MathF.PI / 180f;
        }

        // The sides that face the viewer (their outward side towards the back): each edge swept back, lit by
        // which way it faces (brightest facing up).
        var sides = new List<(Vector2[] Points, float Lit, bool[] Hard)>();
        for (var i = 0; i < n; i++)
        {
            Vector2 outward = Outward(i);
            if (outward.Dot(direction) <= 0.02f)
                continue;
            Vector2 a = Corner(i), b = Corner(i + 1);
            float lit = 0.35f + 0.45f * Mathf.Max(0f, outward.X * 0.55f - outward.Y * 0.8f);
            sides.Add((new[] { a, b, b + back, a + back }, lit, new[] { false, Sharp(i + 1), true, Sharp(i) })); // front, right, back rim, left
        }

        // Their order. Measured across the back direction and along it: where two sides overlap across it, the
        // one whose front edge is further along it is nearer the viewer there (it needs pushing back less to
        // reach that spot), so it is drawn after the other.
        Vector2 Flat(Vector2 p) => new(direction.Cross(p), -direction.Dot(p)); // across, and how far away
        var spans = sides.Select(side =>
        {
            Vector2 a = Flat(side.Points[0]), b = Flat(side.Points[1]);
            return a.X < b.X ? (a, b) : (b, a);
        }).ToList();
        float strength = ShadeStrength(PieceKind.Priest);
        var faces = FarToNear(spans).Select(i => sides[i])
            .Append((Points: front, Lit: 0.95f, Hard: front.Select(_ => true).ToArray())) // the front last, over them all
            .Select(f => (f.Points, 1f - (1f - f.Lit) * strength, f.Hard));

        // The shadow: under the base (the two lowest corners), as deep as the figure is thick.
        Vector2[] lowest = front.OrderByDescending(p => p.Y).Take(2).ToArray();
        Vector2[] ground = { lowest[0], lowest[1], lowest[1] + back, lowest[0] + back };
        return Compose(front.Concat(front.Select(p => p + back)), ground, faces);
    }

    // ---------- From the solid to a flat picture ----------

    // What one kind looks like from the fixed view: the ground shadow, then the faces in the order to draw them,
    // as flat polygons in texture pixels. Worked out once per kind; only colours differ between players.
    internal sealed class Picture
    {
        public Vector2 Size;     // the texture's size: the piece's outline is centred in it
        public Rect2 Bounds;     // the piece's outline (without its shadow or the outline's width)
        public Vector2[] Base;   // the piece's footprint on the ground; the shadow is this moved down and right
        public Vector2[] Shadow; // null if it couldn't be filled
        public readonly List<(Vector2[] Points, float Lit, bool[] Hard)> Faces = new(); // Lit: 0 shaded .. 1 lit
    }

    private static readonly Dictionary<object, Picture> Pictures = new(); // by PieceKind or Prop

    internal static Picture PictureFor(PieceKind kind) =>
        Cached(kind, () => kind == PieceKind.Priest ? PriestPicture() : PictureOf(SolidFor(kind), ShadeStrength(kind)));

    internal static Picture PictureFor(Prop prop) =>
        Cached(prop, () => PictureOf(SolidFor(prop), prop == Prop.PowerToken ? 1f : 0.5f));

    private static Picture Cached(object key, Func<Picture> make)
    {
        if (!Pictures.TryGetValue(key, out Picture picture))
            Pictures[key] = picture = make();
        return picture;
    }

    // A solid seen from the view. strength: how strongly its faces darken away from the light.
    private static Picture PictureOf(Solid solid, float strength)
    {
        float ct = MathF.Cos(Turn), st = MathF.Sin(Turn), ce = MathF.Cos(Elevation), se = MathF.Sin(Elevation);
        // Turn about the vertical axis; then tilt: screen x is the turned x, screen y goes up with height and
        // with distance from the viewer (the turned y). Depth: bigger is further away.
        Vector3 Turned(Vector3 v) => V(v.X * ct - v.Y * st, v.X * st + v.Y * ct, v.Z);
        Vector2 Project(Vector3 v) { Vector3 t = Turned(v); return new Vector2(t.X, -(v.Z * ce + t.Y * se)) * Unit; }
        float Depth(Vector3 v) => Turned(v).Y * ce - v.Z * se;
        // Faces turned away (or nearly edge-on) aren't drawn.
        bool Facing(Face f) { Vector3 n = Turned(f.Normal); return -n.Y * ce + n.Z * se > 0.02f; }

        // Far to near, so nearer faces cover further ones. That's enough for the convex shapes and the trading
        // post; footprint solids need the more careful wall order, and their top always goes last.
        List<Face> order = solid.IsWalls
            ? WallOrder(solid, Facing, ct, st).Append(solid.Faces[0]).ToList()
            : solid.Faces.Where(Facing).OrderByDescending(f => Depth(f.Centre)).ToList();

        return Compose(
            solid.Faces.SelectMany(f => f.Corners).Select(Project),
            solid.Footprint.Select(p => Project(V(p.X, p.Y, 0))).ToArray(),
            order.Select(face =>
            {
                float lit = 0.15f + 0.85f * Mathf.Max(0f, Turned(face.Normal).Dot(Light));
                return (face.Corners.Select(Project).ToArray(), 1f - (1f - lit) * strength, face.Hard);
            }));
    }

    // Puts a picture together from what is seen on screen: the piece's outline (any points round it), its
    // footprint on the ground, and its faces in drawing order. Centres the outline in a texture big enough for
    // the shadow and the outline's width, and leaves out faces that can't be filled.
    private static Picture Compose(IEnumerable<Vector2> outline, Vector2[] ground, IEnumerable<(Vector2[] Points, float Lit, bool[] Hard)> faces)
    {
        List<Vector2> corners = outline.ToList();
        float minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X), minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
        var middle = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
        Vector2[] shadow = ground.Select(p => p + ShadowOffset).ToArray();
        float halfX = corners.Concat(shadow).Max(p => Mathf.Abs(p.X - middle.X)) + OutlineWidth + 2;
        float halfY = corners.Concat(shadow).Max(p => Mathf.Abs(p.Y - middle.Y)) + OutlineWidth + 2;
        var size = new Vector2(MathF.Ceiling(halfX * 2), MathF.Ceiling(halfY * 2));
        Vector2 shift = size / 2 - middle;
        Vector2[] Placed(IEnumerable<Vector2> points) => points.Select(p => p + shift).ToArray();

        var picture = new Picture
        {
            Size = size,
            Bounds = new Rect2(minX + shift.X, minY + shift.Y, maxX - minX, maxY - minY),
            Base = Placed(ground),
        };
        Vector2[] placedShadow = Placed(shadow);
        picture.Shadow = Fillable(placedShadow) ? placedShadow : null;
        foreach (var face in faces)
        {
            Vector2[] points = Placed(face.Points);
            if (Fillable(points)) // a sliver too thin to fill is left out; the outline covers it
                picture.Faces.Add((points, face.Lit, face.Hard));
        }
        return picture;
    }

    // Godot fills a polygon by cutting it into triangles; one it can't cut (too thin, or crossing itself) would
    // only print an error, so those are left out.
    private static bool Fillable(Vector2[] points) => Geometry2D.TriangulatePolygon(points).Length > 0;

    // The order to draw a footprint solid's walls in (just those facing the viewer): each wall's bottom edge,
    // turned and seen from above (x across the screen, y away from the viewer), put in order by FarToNear.
    private static List<Face> WallOrder(Solid solid, Func<Face, bool> facing, float ct, float st)
    {
        List<Face> walls = solid.Faces.Skip(1).Where(facing).ToList();
        Vector2 Flat(Vector3 v) => new(v.X * ct - v.Y * st, v.X * st + v.Y * ct);
        var spans = walls.Select(f =>
        {
            Vector2 a = Flat(f.Corners[0]), b = Flat(f.Corners[1]);
            return a.X < b.X ? (a, b) : (b, a);
        }).ToList();
        return FarToNear(spans).Select(i => walls[i]).ToList();
    }

    // The order to draw walls in, far to near. Each wall is a span across the screen (Left.X < Right.X) whose y
    // is how far from the viewer it is. Sorting by each span's middle goes wrong where an outline curves inwards,
    // so instead: whenever two spans overlap left to right, the one further away at that spot goes first.
    private static List<int> FarToNear(IReadOnlyList<(Vector2 Left, Vector2 Right)> spans)
    {
        float DistanceAt((Vector2 Left, Vector2 Right) s, float x) =>
            s.Left.Y + (s.Right.Y - s.Left.Y) * (x - s.Left.X) / Mathf.Max(s.Right.X - s.Left.X, 1e-6f);

        int m = spans.Count;
        var drawnAfter = Enumerable.Range(0, m).Select(_ => new List<int>()).ToArray(); // drawnAfter[i]: walls that go after i
        var waitingFor = new int[m];                                                    // how many must go before each
        for (var i = 0; i < m; i++)
            for (var j = i + 1; j < m; j++)
            {
                float left = Mathf.Max(spans[i].Left.X, spans[j].Left.X), right = Mathf.Min(spans[i].Right.X, spans[j].Right.X);
                if (right - left < 1e-6f)
                    continue; // side by side (or just touching): either order is fine
                float x = (left + right) / 2;
                (int far, int near) = DistanceAt(spans[i], x) > DistanceAt(spans[j], x) ? (i, j) : (j, i);
                drawnAfter[far].Add(near);
                waitingFor[near]++;
            }

        // Draw each wall once everything that must go before it has been drawn.
        var ready = new Queue<int>(Enumerable.Range(0, m).Where(i => waitingFor[i] == 0));
        var result = new List<int>();
        while (ready.Count > 0)
        {
            int i = ready.Dequeue();
            result.Add(i);
            foreach (int j in drawnAfter[i])
                if (--waitingFor[j] == 0)
                    ready.Enqueue(j);
        }
        result.AddRange(Enumerable.Range(0, m).Except(result)); // safety net: a loop shouldn't happen with these shapes
        return result;
    }
}
