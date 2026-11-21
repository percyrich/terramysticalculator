using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// The shape light gathers in: a rectangle with rounded corners, an ellipse, or a map hex (pointy-topped, as on the board).
public enum LightShape { RoundedRect, Ellipse, Hex }

// An area for a GatheringLight, in the coordinates of the layer the animation is drawn in.
public readonly record struct LightArea(LightShape Shape, Vector2 Centre, Vector2 Size, float CornerRadius = 0)
{
    public static LightArea RoundedRect(Rect2 rect, float cornerRadius = 0) => new(LightShape.RoundedRect, rect.GetCenter(), rect.Size, cornerRadius);
    public static LightArea Ellipse(Vector2 centre, Vector2 size) => new(LightShape.Ellipse, centre, size);
    // radius: centre to corner (a map hex: HexArt.Radius, 74, times the map's scale); 1.73 radii wide, 2 tall.
    public static LightArea Hex(Vector2 centre, float radius) => new(LightShape.Hex, centre, new Vector2(Mathf.Sqrt(3) * radius, 2 * radius));
    // A hex of the map, for an animation drawn in `layer`.
    public static LightArea MapHex(MapView map, int hex, Node2D layer)
    {
        Node2D node = map.Hex(hex);
        return Hex(layer.ToLocal(node.GlobalPosition), HexArt.Radius * node.GlobalScale.X / layer.GlobalScale.X);
    }

    public float HexRadius => Size.X / Mathf.Sqrt(3);
}

// The gathering light: on each of two areas (the source and the trigger) a ring of white
// light builds up at the edge, then shrinks to the middle (the area's outline scaled down about its centre), leaving a
// soft glow behind it, and turns into a point of light. Once both points are made they rise straight up, follow one arc
// (a cubic from one risen point to the other, its control points above them) towards each other, meet halfway along it,
// merge with a flash, and the merged point arcs onto the target, ending in a burst ring. Then it frees itself.
//   var light = GatheringLight.Play(layer, LightArea.RoundedRect(rect, 12), LightArea.MapHex(map, hex, layer), targetPoint);
//   light.Landed += () => ...;   // the point reaches the target (the burst starts)
//   light.Finished += () => ...; // the burst is over (the node is freed after this, unless FreeWhenDone is false)
// Everything is drawn added to what is under it (additive light). Settings: GatheringLightSettings (Default unless given).
public partial class GatheringLight : Node2D
{
    public event Action Landed, Finished;

    public GatheringLightSettings Settings { get; private set; }
    public LightArea Source { get; set; }
    public LightArea Trigger { get; set; }
    public Vector2 Target { get; set; }
    public double Elapsed { get; set; }      // seconds since the start (can be set, e.g. to scrub through it)
    public bool Paused { get; set; }
    public bool FreeWhenDone { get; set; } = true;

    private GatheringLightRing _sourceGlow, _triggerGlow;
    private Paths _paths;
    private bool _landed, _finished;

    // Starts the animation in `layer` (positions in its coordinates), drawn over it at Settings.ZIndex.
    public static GatheringLight Play(Node2D layer, LightArea source, LightArea trigger, Vector2 target, GatheringLightSettings settings = null)
    {
        var light = new GatheringLight { Name = "GatheringLight", Source = source, Trigger = trigger, Target = target, Settings = settings ?? GatheringLightSettings.Default };
        layer.AddChild(light);
        return light;
    }

    public override void _Ready()
    {
        Settings ??= GatheringLightSettings.Default;
        ZIndex = Settings.ZIndex;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        _sourceGlow = new GatheringLightRing { Name = "SourceGlow" };
        _triggerGlow = new GatheringLightRing { Name = "TriggerGlow" };
        AddChild(_sourceGlow);
        AddChild(_triggerGlow);
        Refresh();
    }

    // After moving the source, trigger or target, or changing Settings: works the paths out again.
    public void Refresh() => _paths = new Paths(Source, Trigger, Target, Settings);

    // ---------- The timeline (seconds) ----------

    private float Go => Mathf.Max(Settings.Ready, Settings.TriggerDelay + Settings.Ready);
    private float Met => Go + Settings.Rise + Settings.Travel;
    private float Merged => Met + Settings.Merge;
    private float LandedAt => Merged + Settings.ToTarget;
    public float Duration => LandedAt + Settings.Arrive;

    public override void _Process(double delta)
    {
        if (!Paused)
            Elapsed += delta;
        var t = (float)Elapsed;
        _sourceGlow.ShowAt(Source, Settings, t, Ease);
        _triggerGlow.ShowAt(Trigger, Settings, t - Settings.TriggerDelay, Ease);
        QueueRedraw();
        if (!_landed && t >= LandedAt)
        {
            _landed = true;
            Landed?.Invoke();
        }
        if (!_finished && t >= Duration)
        {
            _finished = true;
            Finished?.Invoke();
            if (FreeWhenDone)
                QueueFree();
        }
    }

    private float Ease(float p)
    {
        p = Mathf.Clamp(p, 0, 1);
        float smoother = p * p * p * (p * (p * 6 - 15) + 10);
        return Mathf.Lerp(p, smoother, Settings.Ease);
    }

    // ---------- Where the moving points are ----------

    private enum Stage { Gather, Travel, Merge, ToTarget, Burst, Done }

    // The stage at time t, and for a travelling point the share of its path covered (source's, trigger's), for the
    // merged point the share of its way to the target, else the stage's own progress (0–1).
    private (Stage Stage, float A, float B) StageAt(float t)
    {
        GatheringLightSettings s = Settings;
        if (t < Go)
            return (Stage.Gather, 0, 0);
        if (t < Met)
        {
            float Share(float riseShare)
            {
                if (s.Flow)
                    return Ease((t - Go) / Mathf.Max(0.001f, s.Rise + s.Travel));
                float u = t - Go;
                return u < s.Rise ? riseShare * Ease(u / Mathf.Max(0.001f, s.Rise))
                    : riseShare + (1 - riseShare) * Ease((u - s.Rise) / Mathf.Max(0.001f, s.Travel));
            }
            return (Stage.Travel, Share(_paths.RiseShareSource), Share(_paths.RiseShareTrigger));
        }
        if (t < Merged)
            return (Stage.Merge, s.Merge > 0 ? (t - Met) / s.Merge : 1, 0);
        if (t < LandedAt)
            return (Stage.ToTarget, Ease((t - Merged) / Mathf.Max(0.001f, s.ToTarget)), 0);
        if (t < Duration)
            return (Stage.Burst, s.Arrive > 0 ? (t - LandedAt) / s.Arrive : 1, 0);
        return (Stage.Done, 0, 0);
    }

    // How far a point has built up on an area `tau` seconds after the area's start (over the last PointBuild of gathering).
    private float PointAmount(float tau)
    {
        GatheringLightSettings s = Settings;
        if (s.PointBuild <= 0)
            return tau >= s.Build + s.Collect ? 1 : 0;
        return Mathf.Clamp((tau - (s.Build + s.Collect - s.PointBuild)) / s.PointBuild, 0, 1);
    }

    // ---------- Drawing (the points, their tails, the merge, the burst; the rings are the GatheringLightRing children) ----------

    public override void _Draw()
    {
        if (_paths is null)
            return;
        GatheringLightSettings s = Settings;
        var t = (float)Elapsed;
        var (stage, a, b) = StageAt(t);
        switch (stage)
        {
            case Stage.Gather:
                DrawPoint(Source.Centre, PointAmount(t));
                DrawPoint(Trigger.Centre, PointAmount(t - s.TriggerDelay));
                break;
            case Stage.Travel:
                DrawTail(_paths.FromSource, a, dt => StageAt(t - dt) is (Stage.Travel, var sa, _) ? sa : null);
                DrawTail(_paths.FromTrigger, b, dt => StageAt(t - dt) is (Stage.Travel, _, var sb) ? sb : null);
                DrawPoint(_paths.FromSource.At(a), 1);
                DrawPoint(_paths.FromTrigger.At(b), 1);
                break;
            case Stage.Merge:
                DrawPoint(_paths.Meeting, 1 + 0.25f * Mathf.Sin(Mathf.Pi * a), 1 + s.Flash * Mathf.Sin(Mathf.Pi * a));
                break;
            case Stage.ToTarget:
                DrawTail(_paths.ToTarget, a, dt => StageAt(t - dt) is (Stage.ToTarget, var sa, _) ? sa : null);
                DrawPoint(_paths.ToTarget.At(a), 1);
                break;
            case Stage.Burst:
            {
                float e = 1 - Mathf.Pow(1 - a, 3);
                DrawPoint(Target, 1 - a * 0.6f, 1 + s.Flash * 0.5f * Mathf.Sin(Mathf.Pi * Mathf.Min(1, a * 1.6f)), 1 - a);
                if (s.Burst > 0)
                {
                    float width = Mathf.Max(1, s.RingWidth * s.SizeScale * 0.5f * (1 - a)), blur = s.Blur * s.SizeScale * 0.4f, alpha = 0.9f * (1 - a);
                    float radius = s.Burst * s.SizeScale * e;
                    if (blur > 0) // softened: two wider, fainter rings under it
                    {
                        DrawArc(Target, radius, 0, Mathf.Tau, 96, new Color(1, 1, 1, alpha * 0.15f), width + 4 * blur, true);
                        DrawArc(Target, radius, 0, Mathf.Tau, 96, new Color(1, 1, 1, alpha * 0.3f), width + 2 * blur, true);
                    }
                    DrawArc(Target, radius, 0, Mathf.Tau, 96, new Color(1, 1, 1, alpha * (blur > 0 ? 0.6f : 1)), width, true);
                }
                break;
            }
        }
    }

    // Fading copies of a point at earlier moments along its path.
    private void DrawTail(Polyline path, float shareNow, Func<float, float?> shareAgo)
    {
        if (Settings.Tail <= 0)
            return;
        for (var i = 8; i >= 1; i--)
            if (shareAgo(i / 8f * Settings.Tail) is float share && share < shareNow)
                DrawPoint(path.At(share), 0.75f * (1 - i / 9f), 1, 0.5f * (1 - i / 9f));
    }

    // A point of light: a white core and a soft glow, both scaled by `amount` (0–1), the glow by `glowScale` too.
    private void DrawPoint(Vector2 at, float amount, float glowScale = 1, float alpha = 1)
    {
        if (amount <= 0.001f || alpha <= 0.001f)
            return;
        float k = Settings.SizeScale, glow = Settings.Glow * k * amount * glowScale, core = Settings.Core * k * amount;
        float radius = Mathf.Max(core + 0.5f, glow);
        DrawTextureRect(PointTexture(core / radius), new Rect2(at - Vector2.One * radius, Vector2.One * 2 * radius), false, new Color(1, 1, 1, alpha));
    }

    // The point's look for a core this share of its radius: white to the core's edge, then fading through a warm white
    // (a quarter strength halfway out) to nothing. One texture per core share, to the nearest 2%.
    private static readonly Dictionary<int, GradientTexture2D> PointTextures = new();
    private static GradientTexture2D PointTexture(float coreShare)
    {
        int key = Mathf.Clamp(Mathf.RoundToInt(coreShare * 50), 0, 49);
        if (PointTextures.TryGetValue(key, out GradientTexture2D texture))
            return texture;
        float q = key / 50f;
        var warm = new Color(1, 250 / 255f, 235 / 255f);
        texture = new GradientTexture2D
        {
            Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, Mathf.Min(0.99f, q), Mathf.Min(0.995f, q * 0.5f + 0.5f), 1f },
                Colors = new[] { Colors.White, new Color(1, 1, 1, 0.95f), new Color(warm, 0.25f), new Color(warm, 0) },
            },
        };
        PointTextures[key] = texture;
        return texture;
    }

    // ---------- The paths ----------

    // A path as points close together, with the distance along it to each, to move along it by share of its length.
    private sealed class Polyline
    {
        private readonly Vector2[] _points;
        private readonly float[] _along;
        public float Length => _along[^1];

        public Polyline(IReadOnlyList<Vector2> points)
        {
            _points = points.ToArray();
            _along = new float[_points.Length];
            for (var i = 1; i < _points.Length; i++)
                _along[i] = _along[i - 1] + _points[i].DistanceTo(_points[i - 1]);
        }

        public Vector2 At(float share)
        {
            float s = Mathf.Clamp(share, 0, 1) * Length;
            int i = 1;
            while (i < _along.Length - 1 && _along[i] < s)
                i++;
            float span = Mathf.Max(1e-4f, _along[i] - _along[i - 1]);
            return _points[i - 1].Lerp(_points[i], (s - _along[i - 1]) / span);
        }
    }

    // From the areas and target: each area's middle straight up to its risen point; one arc between the risen points
    // (control points above them by Bend × their distance, pulled towards each other by Tension); the meeting point
    // halfway along it; the merged point's arc to the target (leaving upwards by TargetLift, arriving from above by
    // Arrival, each of that distance).
    private sealed class Paths
    {
        public Polyline FromSource, FromTrigger, ToTarget;
        public Vector2 Meeting;
        public float RiseShareSource, RiseShareTrigger;

        public Paths(LightArea source, LightArea trigger, Vector2 target, GatheringLightSettings s)
        {
            float rise = s.RiseHeight * s.SizeScale;
            Vector2 s0 = source.Centre, t0 = trigger.Centre, s1 = s0 + Vector2.Up * rise, t1 = t0 + Vector2.Up * rise;
            float up = s1.DistanceTo(t1) * s.Bend, middleX = (s1.X + t1.X) / 2;
            var c1 = new Vector2(Mathf.Lerp(s1.X, middleX, s.Tension), s1.Y - up);
            var c2 = new Vector2(Mathf.Lerp(t1.X, middleX, s.Tension), t1.Y - up);
            List<Vector2> arcPoints = Bezier(s1, c1, c2, t1, 200);
            var arc = new Polyline(arcPoints);
            Meeting = arc.At(0.5f);
            // the arc's points before and after the meeting point
            float halfway = arc.Length / 2, run = 0;
            int half = 1;
            for (; half < arcPoints.Count; half++)
            {
                run += arcPoints[half].DistanceTo(arcPoints[half - 1]);
                if (run >= halfway)
                    break;
            }
            FromSource = new Polyline(new[] { s0 }.Concat(arcPoints.Take(half)).Append(Meeting).ToList());
            FromTrigger = new Polyline(new[] { t0 }.Concat(Enumerable.Reverse(arcPoints.Skip(half))).Append(Meeting).ToList());
            float distance = Meeting.DistanceTo(target);
            ToTarget = new Polyline(Bezier(Meeting, Meeting + Vector2.Up * distance * s.TargetLift, target + Vector2.Up * distance * s.Arrival, target, 160));
            RiseShareSource = rise / Mathf.Max(1, FromSource.Length);
            RiseShareTrigger = rise / Mathf.Max(1, FromTrigger.Length);
        }

        private static List<Vector2> Bezier(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, int steps) =>
            Enumerable.Range(0, steps + 1).Select(i => p0.BezierInterpolate(c1, c2, p1, i / (float)steps)).ToList();
    }

    private static Shader _areaShader;
    internal static Shader AreaShader => _areaShader ??= new Shader { Code = AreaShaderCode };

    private const string AreaShaderCode = @"
shader_type canvas_item;
render_mode blend_add;

uniform int shape;           // 0 rounded rectangle, 1 ellipse, 2 map hex (pointy-topped)
uniform vec2 half_size;      // px
uniform float corner;        // px (rounded rectangle)
uniform float hex_radius;    // px, centre to corner (hex)
uniform float ring_scale;    // the ring: the outline scaled by this about the centre
uniform float ring_width;    // px
uniform float blur;          // px, the Gaussian's spread
uniform float ring;          // brightness of the ring
uniform float halo;          // brightness of the halo (3 × as wide)
uniform float trail;         // brightness of the flat glow inside

varying vec2 local;

void vertex() { local = VERTEX; }

// Signed distance to the outline (negative inside), in px.
float sd_shape(vec2 p) {
    if (shape == 1) { // an ellipse (a close approximation)
        vec2 r = max(half_size, vec2(0.5));
        float k0 = length(p / r), k1 = length(p / (r * r));
        return k1 > 0.0 ? k0 * (k0 - 1.0) / k1 : -min(r.x, r.y);
    }
    if (shape == 2) { // a pointy-topped hexagon
        const vec3 k = vec3(-0.866025404, 0.5, 0.577350269);
        float a = hex_radius * 0.866025404; // centre to a flat side
        vec2 q = abs(p.yx);
        q -= 2.0 * min(dot(k.xy, q), 0.0) * k.xy;
        q -= vec2(clamp(q.x, -k.z * a, k.z * a), a);
        return length(q) * sign(q.y);
    }
    vec2 q = abs(p) - (half_size - vec2(corner)); // a rectangle with rounded corners
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
}

// The outline scaled by k about the centre.
float sd_scaled(vec2 p, float k) { return sd_shape(p / k) * k; }

float erf_approx(float x) {
    float s = sign(x); x = abs(x);
    float t = 1.0 / (1.0 + 0.3275911 * x);
    float y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * exp(-x * x);
    return s * y;
}

// A band `width` wide at distance d from its middle line, softened by a Gaussian of spread `sigma`.
float band(float d, float width, float sigma) {
    if (sigma < 0.3) return clamp(width * 0.5 - d + 0.5, 0.0, 1.0);
    float s = sigma * 1.41421356;
    return 0.5 * (erf_approx((width * 0.5 - d) / s) + erf_approx((width * 0.5 + d) / s));
}

void fragment() {
    float inside = clamp(0.5 - sd_shape(local), 0.0, 1.0); // cut off at the edge, 1 px soft
    float d = abs(sd_scaled(local, ring_scale));
    float light = trail + ring * band(d, ring_width, blur) + halo * band(d, ring_width * 3.0, blur);
    COLOR = vec4(vec3(1.0), clamp(light, 0.0, 1.0) * inside);
}
";
}

// The light on one area, drawn by a shader over the area's box: a ring at the area's outline scaled by k about its
// centre (k shrinking from 1 to 0), the ring RingWidth wide softened by a Gaussian of spread Blur, a halo 3× as wide
// and fainter, and a flat glow inside the area; everything cut off at the area's edge.
internal partial class GatheringLightRing : Node2D
{
    private ShaderMaterial _material;
    private Vector2 _half;

    public void ShowAt(LightArea area, GatheringLightSettings s, float tau, Func<float, float> ease)
    {
        Visible = tau >= 0 && tau <= s.Build + s.Collect;
        if (!Visible)
            return;
        _material ??= new ShaderMaterial { Shader = GatheringLight.AreaShader };
        Material = _material;
        Position = area.Centre;
        _half = area.Size / 2;
        float built = s.Build > 0 ? Mathf.Min(1, tau / s.Build) : 1;
        float p = s.Collect > 0 ? ease(Mathf.Max(0, (tau - s.Build) / s.Collect)) : 1;
        float fadeFrom = s.Build + s.Collect * 0.85f;
        float fade = tau > fadeFrom ? Mathf.Max(0, 1 - (tau - fadeFrom) / (s.Collect * 0.15f)) : 1;
        _material.SetShaderParameter("shape", (int)area.Shape);
        _material.SetShaderParameter("half_size", _half);
        _material.SetShaderParameter("corner", Mathf.Min(area.CornerRadius, Mathf.Min(_half.X, _half.Y)));
        _material.SetShaderParameter("hex_radius", area.HexRadius);
        _material.SetShaderParameter("ring_scale", Mathf.Max(0.001f, 1 - p));
        _material.SetShaderParameter("ring_width", s.RingWidth * s.SizeScale);
        _material.SetShaderParameter("blur", s.Blur * s.SizeScale);
        _material.SetShaderParameter("ring", s.Intensity * built * fade);
        _material.SetShaderParameter("halo", 0.35f * s.Halo * s.Intensity * built * fade);
        _material.SetShaderParameter("trail", 0.35f * s.Trail * built * (1 - p) * fade);
        QueueRedraw();
    }

    public override void _Draw() => DrawRect(new Rect2(-_half - Vector2.One, _half * 2 + Vector2.One * 2), Colors.White);
}

