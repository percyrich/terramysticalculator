using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The cult tracks, their padlocks and the priest zones. Drawn as SVG and turned into
// textures like IconArt's. Edges in the cult's colour, on the player panels' dark sunken colour (Base), shaded as
// their sections are (a shadow inside along the top and left, a faint light along the bottom and right); priest slots
// are that dark tinted SlotTint towards the cult's colour.
//   CultArt.TrackTexture(CultArt.Colour(Track.Fire))   a track, 120 × 404, without its padlock
//   CultArt.Padlock()                                 the padlock, shown at PadlockCentre until step 10 is reached
//   CultArt.PriestZone(CultArt.Neutral)               the neutral priest zone (for the round tile), 214 × 282
//   CultArt.Tracker(colour)                           a faction's marker on a track, 26 × 26 (the disc 22 across)
// Rectangles and points are in the texture's own pixels, origin at its top left.
public static class CultArt
{
    // ---------- Colours ----------

    public static Color Colour(Track track) => track switch
    {
        Track.Fire => new Color("d65a24"),
        Track.Water => new Color("2f74c0"),
        Track.Earth => new Color("7b5532"),
        _ => new Color("8d9cad"), // Air: a blue-grey
    };

    // The base of the tracks and zones (FactionBoardArt.SectionColour, #22374f); the priest slots' fill; the slot
    // numbers' colour (the cult's lightened, to read on the dark).
    public static Color Base => FactionBoardArt.SectionColour;
    public const float SlotTint = 0.3f;
    public static Color SlotFill(Color edge) => Base.Lerp(edge, SlotTint);
    public static Color NumberColour(Color edge) => edge.Lerp(Colors.White, 0.35f);

    public static readonly Color Neutral = new("8c9298");
    private static readonly Color PowerEdge = new("a8329a"); // the lines below the steps that pay power when reached

    // ---------- Tracks ----------
    // Eleven steps, 0 at the bottom to 10 at the top, Pitch apart. Steps 0 to 9 touch, as one column with thin
    // lines between them. Purple lines mark the steps that pay power when reached, as wide as the power paid:
    // below step 3 (1), steps 5 and 7 (2) and step 10 (3, along the column's top). Step 10 is an ellipse, TopWidth of
    // the column wide and a little taller than a step, a step above step 9; it lies under that purple line and the
    // padlock sits on it.

    public const float Pitch = 36;
    private const float TrackWidth = 120, ColumnWidth = 112, StepRadius = 8, Line = 3, Divider = 1.5f, PowerLine = 5;
    private const float TopWidth = 0.6f, TopHeight = 1.05f; // step 10's ellipse, as fractions of a full step
    // Room above and below, the same, so the track's centre stays on step 5's centre (as in the track scene).
    private const float Margin = 4;

    // How much power reaching each step pays (0 for most).
    private static int PowerAt(int step) => step switch { 3 => 1, 5 => 2, 7 => 2, 10 => 3, _ => 0 };

    public static Vector2 TrackSize => new(TrackWidth, 11 * Pitch + 2 * Margin);

    // Step 0 (bottom) to 10 (top).
    public static Rect2 Step(int step)
    {
        float x = (TrackWidth - ColumnWidth) / 2, y = Margin + (10 - step) * Pitch;
        if (step < 10)
            return new Rect2(x, y, ColumnWidth, Pitch);
        float w = ColumnWidth * TopWidth, h = Pitch * TopHeight;
        return new Rect2(TrackWidth / 2 - w / 2, Margin + Pitch / 2 - h / 2, w, h);
    }

    // Where the padlock's centre goes: halfway between the top of step 9 and the bottom of step 10.
    public static Vector2 PadlockCentre => new(TrackWidth / 2, (Step(9).Position.Y + Step(10).End.Y) / 2);

    // The purple band along the column's top, between step 9 and step 10 (it stands a little above the column).
    public static Rect2 PowerBand
    {
        get
        {
            float w = PowerLine * PowerAt(10);
            return new Rect2((TrackWidth - ColumnWidth) / 2, Step(9).Position.Y + Line / 2 - w / 2, ColumnWidth, w);
        }
    }

    // The track's silhouette, for EdgeHighlight: the column (rounded at the bottom), the purple band along its top,
    // and step 10's ellipse standing on that (the part above the band's top, from where it crosses that line on the
    // left, over the top, to where it crosses it on the right); clockwise, in texture pixels.
    public static Vector2[] Outline()
    {
        float x0 = (TrackWidth - ColumnWidth) / 2, x1 = x0 + ColumnWidth, top = PowerBand.Position.Y, bottom = Step(0).End.Y;
        Rect2 s = Step(10);
        Vector2 c = s.GetCenter();
        float rx = s.Size.X / 2, ry = s.Size.Y / 2, crossing = Mathf.Asin(Mathf.Clamp((top - c.Y) / ry, -1, 1));
        IEnumerable<Vector2> ellipse = Enumerable.Range(0, 41).Select(i =>
        {
            float t = Mathf.Pi - crossing + (Mathf.Pi + 2 * crossing) * i / 40;
            return c + new Vector2(rx * Mathf.Cos(t), ry * Mathf.Sin(t));
        });
        const float q = Mathf.Pi / 2;
        IEnumerable<Vector2> Arc(Vector2 centre, float r, float from, float to) =>
            Enumerable.Range(0, 9).Select(i => centre + Vector2.FromAngle(from + (to - from) * i / 8) * r);
        return new[] { new Vector2(x0, top) }
            .Concat(ellipse)
            .Concat(new[] { new Vector2(x1, top) })
            .Concat(Arc(new Vector2(x1 - StepRadius, bottom - StepRadius), StepRadius, 0, q))
            .Concat(Arc(new Vector2(x0 + StepRadius, bottom - StepRadius), StepRadius, q, 2 * q)).ToArray();
    }

    public static Texture2D TrackTexture(Color colour, float scale = 1f) =>
        Make(("cult track", colour, scale), () => TrackSvg(colour), TrackSize, scale);

    private static string TrackSvg(Color edge)
    {
        var svg = new Svg(TrackSize.X, TrackSize.Y);
        float x0 = (TrackWidth - ColumnWidth) / 2, x1 = x0 + ColumnWidth, top = Step(9).Position.Y, bottom = Step(0).End.Y;

        // The column of steps 0 to 9, inside its edge line: square at the top (its top edge is the purple line
        // into step 10), rounded at the bottom; dark and sunken.
        float a = x0 + Line / 2, b = x1 - Line / 2, y0 = top + Line / 2, y1 = bottom - Line / 2, r = StepRadius - Line / 2;
        SvgPath Column(float dx, float dy, float dw, float dh)
        {
            float left = a + dx, right = b + dx + dw, upper = y0 + dy, lower = y1 + dy + dh;
            return new SvgPath().MoveTo(left, upper).LineTo(right, upper)
                .Arc(right - r, lower - r, r, 0, Mathf.Pi / 2).Arc(left + r, lower - r, r, Mathf.Pi / 2, Mathf.Pi).Close();
        }
        Sunken(svg, Column, Base);
        SvgPath column = Column(0, 0, 0, 0);

        // The lines between steps: thin in the cult's colour; purple below a step that pays power, wider for more.
        for (var step = 1; step <= 9; step++)
        {
            float y = Step(step).End.Y;
            int power = PowerAt(step);
            svg.Stroke(new SvgPath().MoveTo(x0, y).LineTo(x1, y), power > 0 ? PowerEdge : edge, power > 0 ? PowerLine * power : Divider);
        }
        svg.Stroke(column, edge, Line);

        // Step 10 first, so the widest purple line, between it and step 9, lies over its foot.
        DarkEllipse(svg, Step(10), edge, Line);
        svg.Stroke(new SvgPath().MoveTo(x0, top + Line / 2).LineTo(x1, top + Line / 2), PowerEdge, PowerLine * PowerAt(10));
        return svg.Text();
    }

    // ---------- Padlock ----------

    // A padlock `size` tall: a steel shackle over a gold body with a keyhole, centred in a square texture a little
    // bigger than it (1.2 × size), so the texture's centre is the padlock's.
    public static Texture2D Padlock(float size = 20f)
    {
        float side = MathF.Ceiling(size * 1.2f);
        string Text()
        {
            var svg = new Svg(side, side);
            svg.Translate(side / 2, side / 2);
            DrawPadlock(svg, size);
            return svg.Text();
        }
        return Make(("padlock", size), Text, new Vector2(side, side), 1f);
    }

    private static void DrawPadlock(Svg svg, float s)
    {
        float bw = s * 0.78f, bh = s * 0.56f, by = s * 0.5f - bh, r = s * 0.24f, sw = s * 0.13f, line = Mathf.Max(1f, s * 0.07f);
        var outline = new Color("1b1b1b");
        SvgPath shackle = new SvgPath().MoveTo(-r, by + bh * 0.3f).LineTo(-r, by - s * 0.08f)
            .Arc(0, by - s * 0.08f, r, Mathf.Pi, 0).LineTo(r, by + bh * 0.3f);
        svg.Stroke(shackle, outline, sw + 2 * line);
        svg.Stroke(shackle, new Color("c3cad1"), sw);
        SvgPath body = new SvgPath().RoundRect(-bw / 2, by, bw, bh, s * 0.1f);
        svg.Fill(body, Paint.Linear(0, by, 0, by + bh, (0, new Color("f0d27a")), (1, new Color("c39a3c"))));
        svg.Stroke(body, outline, line);
        var keyhole = new Color("3a2a10");
        svg.Fill(SvgPath.Circle(0, by + bh * 0.42f, s * 0.07f), keyhole);
        svg.Fill(SvgPath.Polygon(new Vector2[]
        {
            new(-s * 0.035f, by + bh * 0.45f), new(s * 0.035f, by + bh * 0.45f), new(s * 0.05f, by + bh * 0.78f), new(-s * 0.05f, by + bh * 0.78f),
        }), keyhole);
    }

    // ---------- Trackers ----------

    // A faction's marker on a track: a disc in its colour (TrackerRadius: 22 across)
    // with a dark edge (1.6, whatever the size) and a soft highlight up and to the left (scaled with the disc),
    // centred in a TrackerSide square texture.
    public const float TrackerRadius = 11f;
    private const float TrackerSide = 26;

    public static Texture2D Tracker(Color colour)
    {
        const float side = TrackerSide, k = TrackerRadius / 8.5f; // the highlight was drawn for a radius of 8.5
        string Text()
        {
            var svg = new Svg(side, side);
            svg.Translate(side / 2, side / 2);
            SvgPath disc = SvgPath.Circle(0, 0, TrackerRadius);
            svg.Fill(disc, colour);
            svg.Stroke(disc, new Color("1b1b1b"), 1.6f);
            svg.Fill(SvgPath.Circle(-2.6f * k, -2.8f * k, 2.6f * k), new Color(1, 1, 1, 0.45f));
            return svg.Text();
        }
        return Make(("tracker", colour), Text, new Vector2(side, side), 1f);
    }

    // ---------- Priest zones ----------
    // Four round slots in a square (3, 2, 2, 2 steps) and a smaller one below (1 step), tucked up between the two
    // above it, on a dark panel edged in the cult's colour; the slots tinted towards that colour (SlotFill).

    private const float ZonePad = 12, SlotSize = 92, SlotGap = 6, SmallSlot = 72, SmallGap = -4;
    private const float ZoneRadius = 22, ZoneLine = 6, SlotLine = 3;

    public static Vector2 PriestZoneSize => new(2 * ZonePad + 2 * SlotSize + SlotGap, 2 * ZonePad + 2 * SlotSize + SlotGap + SmallGap + SmallSlot);

    // Slot 0 to 4: top left, top right, bottom left, bottom right, then the small one below.
    public static Rect2 PriestSlot(int slot) => slot < 4
        ? new Rect2(ZonePad + slot % 2 * (SlotSize + SlotGap), ZonePad + slot / 2 * (SlotSize + SlotGap), SlotSize, SlotSize)
        : new Rect2(PriestZoneSize.X / 2 - SmallSlot / 2, ZonePad + 2 * SlotSize + SlotGap + SmallGap, SmallSlot, SmallSlot);

    // The slot numbers (drawn in Godot): in NumberColour(edge), this fraction of the slot's width tall.
    public const float NumberSize = 0.67f;

    public static Texture2D PriestZone(Color colour, float scale = 1f) =>
        Make(("priest zone", colour, scale), () => PriestZoneSvg(colour), PriestZoneSize, scale);

    private static string PriestZoneSvg(Color edge)
    {
        var svg = new Svg(PriestZoneSize.X, PriestZoneSize.Y);
        DarkCell(svg, new Rect2(Vector2.Zero, PriestZoneSize), ZoneRadius, edge, ZoneLine, Base);
        for (var slot = 0; slot < 5; slot++)
        {
            Rect2 area = PriestSlot(slot);
            DarkCell(svg, area, area.Size.X / 2, edge, SlotLine, SlotFill(edge));
        }
        return svg.Text();
    }

    // ---------- Shared ----------

    // The dark sunken look inside shape(0, 0, 0, 0): the fill, a shadow along the inside top and left, a faint light
    // along the bottom and right (as the player panels' sections). shape(dx, dy, dw, dh) is the same outline moved by
    // (dx, dy) and grown by (dw, dh), for the two shading strokes.
    private static void Sunken(Svg svg, Func<float, float, float, float, SvgPath> shape, Color fill)
    {
        SvgPath inner = shape(0, 0, 0, 0);
        svg.Fill(inner, fill);
        svg.Save();
        svg.Clip(inner);
        svg.Stroke(shape(2, 2, 6, 6), new Color(0, 0, 0, 0.35f), 6);
        svg.Stroke(shape(-6, -6, 4, 4), new Color(1, 1, 1, 0.08f), 6);
        svg.Restore();
    }

    // A dark cell (a zone, a slot; also IconArt's Acolytes icon): sunken, `fill` (Base or SlotFill), and the edge line
    // inside the area, so the area's size is exact.
    internal static void DarkCell(Svg svg, Rect2 area, float radius, Color edge, float line, Color fill)
    {
        float x = area.Position.X + line / 2, y = area.Position.Y + line / 2, w = area.Size.X - line, h = area.Size.Y - line;
        float r = Mathf.Max(0, radius - line / 2);
        Sunken(svg, (dx, dy, dw, dh) => new SvgPath().RoundRect(x + dx, y + dy, w + dw, h + dh, r), fill);
        svg.Stroke(new SvgPath().RoundRect(x, y, w, h, r), edge, line);
    }

    // Step 10: a dark sunken ellipse filling its area, edged inside it.
    private static void DarkEllipse(Svg svg, Rect2 area, Color edge, float line)
    {
        Vector2 c = area.GetCenter();
        float rx = area.Size.X / 2 - line / 2, ry = area.Size.Y / 2 - line / 2;
        SvgPath Shape(float dx, float dy, float dw, float dh) =>
            new SvgPath().Ellipse(c.X + dx + dw / 2, c.Y + dy + dh / 2, Mathf.Max(1, rx + dw / 2), Mathf.Max(1, ry + dh / 2));
        Sunken(svg, Shape, Base);
        svg.Stroke(Shape(0, 0, 0, 0), edge, line);
    }

    // The texture for `key`, made from the SVG text once (see IconArt.Cached).
    private static Texture2D Make(object key, Func<string> svg, Vector2 size, float scale)
    {
        Vector2 pixels = size * scale;
        return IconArt.Cached(key, () => IconArt.Rasterise(svg(), new Vector2I(Mathf.RoundToInt(pixels.X), Mathf.RoundToInt(pixels.Y)), scale));
    }
}
