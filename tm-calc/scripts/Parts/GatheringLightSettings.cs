using Godot;

namespace TMCalc;

// Every setting of a GatheringLight (times in seconds). A Resource, so a particular animation can keep its own
// copy (made in code with Duplicate(), or saved as a .tres and tweaked in the Inspector); GatheringLightSettings.Default
// is the one shared by animations that don't bring their own. Sizes are pixels in the layer the animation is drawn in,
// times SizeScale.
[GlobalClass]
public partial class GatheringLightSettings : Resource
{
    private static GatheringLightSettings _default;
    public static GatheringLightSettings Default => _default ??= new GatheringLightSettings();

    [ExportGroup("Timing (seconds)")]
    [Export] public float Build { get; set; } = 0.1f;        // the edge light building up
    [Export] public float Collect { get; set; } = 0.3f;      // the light gathering to the middle
    [Export] public float PointBuild { get; set; } = 0.15f;    // the point building up, over the end of the gathering
    [Export] public float Hold { get; set; } = 0.05f;          // before rising
    [Export] public float TriggerDelay { get; set; } = 0f;    // the trigger starts this much later (the source's point waits)
    [Export] public float Rise { get; set; } = 0.20f;
    [Export] public float Travel { get; set; } = 0.4f;        // to the meeting point
    [Export] public float Merge { get; set; } = 0.1f;
    [Export] public float ToTarget { get; set; } = 0.75f;
    [Export] public float Arrive { get; set; } = 0.42f;       // the burst at the target

    [ExportGroup("Paths")]
    [Export] public float RiseHeight { get; set; } = 15f;     // px
    [Export] public float Bend { get; set; } = 0.15f;         // the arc's height, of the risen points' distance
    [Export] public float Tension { get; set; } = 0.05f;      // 0: the arc leaves straight up (round), 1: pointed
    [Export] public float TargetLift { get; set; } = 0.15f;   // the merged point lifts first, of its distance to the target
    [Export] public float Arrival { get; set; } = 0.0f;       // and arrives from above, of that distance
    [Export] public float Ease { get; set; } = 0.7f;          // 0: even speed, 1: smooth start and stop
    [Export] public bool Flow { get; set; } = true;           // the rise flows into the arc (one eased movement)

    [ExportGroup("Light")]
    [Export] public float RingWidth { get; set; } = 14f;      // px
    [Export] public float Blur { get; set; } = 10f;           // px (the ring's softness: a Gaussian blur's spread)
    [Export] public float Halo { get; set; } = 0.45f;         // a wider (3×), fainter ring round it
    [Export] public float Trail { get; set; } = 0.25f;        // the glow left behind the ring
    [Export] public float Intensity { get; set; } = 0.9f;     // the ring's brightness
    [Export] public float Core { get; set; } = 5f;            // px, the point's solid middle
    [Export] public float Glow { get; set; } = 26f;           // px, the point's glow
    [Export] public float Tail { get; set; } = 0.14f;         // seconds of comet tail behind a moving point (0: none)
    [Export] public float Flash { get; set; } = 1.6f;         // the merge flash: the glow grows to (1 + Flash) times
    [Export] public float Burst { get; set; } = 60f;          // px, the burst ring at the target

    [ExportGroup("Placing")]
    [Export] public float SizeScale { get; set; } = 1f;       // every px setting times this (e.g. for a zoomed layer)
    [Export] public int ZIndex { get; set; } = 200;           // over the board and the pieces

    // ---------- Derived ----------

    public float Ready => Build + Collect + Hold;
}
