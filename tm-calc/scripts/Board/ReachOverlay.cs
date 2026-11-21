using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// The reach highlight: a small hexagon outline inside each hex a dwelling could go on, white over a
// faint dark edge, pulsing gently with a slow ripple across the board. MapView keeps it above the hexes and
// tells it which hexes to mark (in its own coordinates). Redrawn every frame only while something is marked.
public partial class ReachOverlay : Node2D
{
    private readonly List<Vector2> _centres = new();
    private float _radius = 74f;

    public void ShowAt(IEnumerable<Vector2> centres, float radius)
    {
        _centres.Clear();
        _centres.AddRange(centres);
        _radius = radius;
        SetProcess(_centres.Count > 0);
        QueueRedraw();
    }

    public override void _Ready() => SetProcess(_centres.Count > 0);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        float r = _radius, ringRadius = r * 0.78f;
        float time = Time.GetTicksMsec() / 1000f;
        foreach (Vector2 centre in _centres)
        {
            // 0..1, rising and falling about every 3 seconds; neighbouring hexes slightly out of step.
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 2.2f - (centre.X + centre.Y) / (r * 5));
            Ring(centre, ringRadius, Mathf.Max(2.5f, r * 0.1f), new Color(0f, 0f, 0f, 0.35f));
            Ring(centre, ringRadius, Mathf.Max(1.2f, r * 0.045f), new Color(1f, 1f, 1f, 0.6f + 0.4f * pulse));
        }
    }

    // A hexagon outline `width` wide centred on the hexagon of the given corner radius: one quad per side, so
    // the corners are mitred with no gaps, and a hairline round both edges to smooth them.
    private void Ring(Vector2 centre, float radius, float width, Color colour)
    {
        float slant = MathF.Cos(MathF.PI / 6); // corner distance = side distance / cos 30°
        Vector2[] outer = Corners(centre, radius + width / 2 / slant), inner = Corners(centre, radius - width / 2 / slant);
        for (var d = 0; d < 6; d++)
            DrawColoredPolygon(new[] { outer[d], outer[(d + 1) % 6], inner[(d + 1) % 6], inner[d] }, colour);
        DrawPolyline(outer.Append(outer[0]).ToArray(), colour, 1f, true);
        DrawPolyline(inner.Append(inner[0]).ToArray(), colour, 1f, true);
    }

    private static Vector2[] Corners(Vector2 c, float r) =>
        Enumerable.Range(0, 6).Select(i => c + Vector2.FromAngle((60 * i - 30) * MathF.PI / 180) * r).ToArray();
}
