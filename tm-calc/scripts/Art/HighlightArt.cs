using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// Sizes, colours and outlines for EdgeHighlight.
// Every outline is a closed list of points running clockwise on screen, in the pixels of the picture it goes
// round, centred on that picture (as a centred Sprite2D draws it).
//   HighlightArt.BlankTile(BlankTile.Pass, 0.5f)   a pass tile drawn at half size
//   HighlightArt.Hexagon(Vector2.Zero, 74)         a map hex (pointy-topped, as HexArt draws them)
//   HighlightArt.CultTrack()                       a cult track
public static class HighlightArt
{
    public enum Colour { White, Yellow, Purple, Red }

    public static Color ColourOf(Colour colour) => colour switch
    {
        Colour.White => new Color("eef3f8"),
        Colour.Yellow => new Color("ffc83a"),
        Colour.Red => new Color("ff4a3d"), // a token about to be burned
        _ => new Color("c45ad6"), // purple: for the colour wheel's other uses
    };

    // Widths at a picture's natural size (EdgeHighlight scales them with the picture): the soft glow, the solid
    // line and the white core; and the radius of a glint.
    public const float Glow = 18f, Line = 9f, Core = 3.3f, GlintRadius = 15f;

    // Glints move round the outline at GlintSpeed pixels a second (how many there are is up to each highlight).
    public const float GlintSpeed = 45f;

    // ---------- Shapes ----------

    // Points along an arc round (cx, cy): angles in radians, increasing clockwise on screen.
    private static IEnumerable<Vector2> Arc(Vector2 centre, float radius, float from, float to, int steps = 8) =>
        Enumerable.Range(0, steps + 1).Select(i => centre + Vector2.FromAngle(from + (to - from) * i / steps) * radius);

    public static Vector2[] RoundRect(Rect2 rect, float radius)
    {
        float r = Mathf.Clamp(radius, 0, Mathf.Min(rect.Size.X, rect.Size.Y) / 2);
        Vector2 a = rect.Position, b = rect.End;
        if (r <= 0)
            return new[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) };
        const float q = Mathf.Pi / 2;
        Vector2[] points = Arc(new Vector2(a.X + r, a.Y + r), r, 2 * q, 3 * q)
            .Concat(Arc(new Vector2(b.X - r, a.Y + r), r, 3 * q, 4 * q))
            .Concat(Arc(new Vector2(b.X - r, b.Y - r), r, 0, q))
            .Concat(Arc(new Vector2(a.X + r, b.Y - r), r, q, 2 * q)).ToArray();
        // With the radius at half the size (a circle, e.g. the burn highlight round a token) neighbouring arcs meet in
        // the same point: repeated points make the polygon impossible for Godot to triangulate, so they are dropped.
        var kept = new System.Collections.Generic.List<Vector2>();
        foreach (Vector2 p in points)
            if (kept.Count == 0 || kept[^1].DistanceTo(p) > 0.01f)
                kept.Add(p);
        if (kept.Count > 1 && kept[^1].DistanceTo(kept[0]) <= 0.01f)
            kept.RemoveAt(kept.Count - 1);
        return kept.ToArray();
    }

    public static Vector2[] Ellipse(Vector2 centre, Vector2 radii, int steps = 72) =>
        Enumerable.Range(0, steps).Select(i => centre + Vector2.FromAngle(Mathf.Tau * i / steps) * radii).ToArray();

    // Pointy-topped: corner i at (60i − 30)°, as HexArt and MapView (so edge d runs from corner d to corner d + 1).
    public static Vector2[] Hexagon(Vector2 centre, float radius) =>
        Enumerable.Range(0, 6).Select(i => centre + Vector2.FromAngle((60 * i - 30) * Mathf.Pi / 180) * radius).ToArray();

    // ---------- Pictures ----------

    // A blank tile drawn at `scale` of its natural size (IconArt.Texture(tile, scale)), centred.
    public static Vector2[] BlankTile(BlankTile tile, float scale = 1f)
    {
        Vector2 size = IconArt.NaturalSize(tile);
        Vector2[] outline = tile switch
        {
            TMCalc.BlankTile.Round => RoundRect(new Rect2(1, 1, size.X - 2, size.Y - 2), size.Y * 0.09f),
            TMCalc.BlankTile.Pass => RoundRect(new Rect2(1, 1, size.X - 2, size.Y - 2), size.X * 0.1f),
            TMCalc.BlankTile.KeyToken => Hexagon(size / 2, Mathf.Min(size.X / Mathf.Sqrt(3), size.Y / 2) - 2),
            TMCalc.BlankTile.PowerAction => IconArt.PowerActionOutline(),
            _ => Ellipse(size / 2, size / 2 - new Vector2(1.5f, 1.5f)), // favour: the oval inside its 3 px edge
        };
        return outline.Select(p => (p - size / 2) * scale).ToArray();
    }

    // A cult track (CultArt.TrackTexture at `scale`), centred: its column, the purple band along the column's top,
    // and step 10 standing on that.
    public static Vector2[] CultTrack(float scale = 1f) =>
        CultArt.Outline().Select(p => (p - CultArt.TrackSize / 2) * scale).ToArray();
}
