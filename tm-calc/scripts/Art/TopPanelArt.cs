using System;
using System.Linq;
using Godot;

namespace TMCalc;

// The top panel's pictures, drawn in panel
// pixels (the player panels' pixels; TopPanel scales the whole bar by its PanelScale, 0.6 as the step button):
//   TopPanelArt.Layout(width)                      where everything on the bar goes, for a bar `width` wide
//   TopPanelArt.Backing(width, frame)              the backing: frame in `frame`, stone, the game button's round socket,
//                                                  the text section and the history keys' section (both sunken), and the
//                                                  rules tab past the bar's right end (TabReach wider than `width`), with
//                                                  the slider's groove and notches
//   TopPanelArt.Knob(held, frame)                  the rules slider's knob, KnobMargin bigger all round than the knob
//   TopPanelArt.Key(kind, look, frame, filled)     one raised stone key, KeyMargin bigger all round than the key
//   TopPanelArt.LogBacking(size, frame)            the whole log's backing: the same frame and stone, a sunken list
// The frame is the panels' (FactionBoardArt.Backing): a dark edge, the colour lit from above, a dark edge.
public static class TopPanelArt
{
    // ---------- Sizes (panel pixels) ----------
    public const float Frame = 7.8f;          // the frame's width (the panels' 6 × BackingBoardScale 1.3)
    public const float Margin = 12, Gap = 12;  // inside the frame, and between the sections
    public const float OuterRadius = 24, SectionRadius = 14;
    public const float ButtonDiameter = 84, ButtonRing = 10; // the game button (the PlayerButton's own sizes)
    public const float SectionHeight = ButtonDiameter + 12;  // the sections, and the socket's diameter
    public const float KeyPad = 6, KeyGap = 4, KeySide = SectionHeight - 2 * KeyPad, KeyRadius = 7;
    public const float KeyMargin = 6;          // a key's picture reaches this far outside the key (shadow, glow)
    public const float Height = SectionHeight + 2 * (Frame + Margin);
    public const float LogGap = 0;             // between the bar and the whole log under it (it hangs from the bar)

    // The rules tab (Strict, Lenient, Free): it sticks out past the bar's right end (the bar
    // keeps its width, the map's), from the top down to the bar's midline, in the same frame as one outline; the slider
    // sits straight on its stone. Its slider area starts where the bar's frame would be (Gap from the history keys).
    public const float TabSlider = 240;                    // the slider's width
    public const float TabMargin = 6;                      // between the slider and the tab's frame, on the right
    public const float TabReach = TabSlider + TabMargin;   // how far the tab reaches past the bar
    public const float TabHeight = Height / 2;
    public const float TabCornerRadius = 18, TabHollowRadius = 12; // the tab's lower right corner; the hollow under it
    public const float RulesLabelSize = 17, KnobRadius = 16.5f, GrooveHeight = 8, NotchRadius = 2.6f;
    public const float KnobMargin = 8;                     // the knob's picture reaches this far past it (shadow)
    public static readonly string[] RulesLabels = { "Strict", "Lenient", "Free" };

    public static readonly Color MessageColour = new("f2f6f9"), LogColour = new("a9b9c8");
    private static readonly Color Section = new("22374f"), KeyOutline = new("16202b"), IconFill = new("eef3f8"), BookmarkGold = new("f0d27a");

    public enum KeyKind { Log, Undo, Redo, Bookmark, Map }
    public enum KeyLook { Normal, Hover, Pressed, Disabled }

    // Socket: the game button's centre and the socket's radius. Text: the text section (the log key at its right end).
    // MessageLine, LogLine: where each line of text goes (full height of its half, text centred in it). Rule: the faint
    // line between them (from, to). History: the section of the four history keys; Keys: undo, redo, bookmark, map.
    // Size: the bar alone (its width as asked); Rules: the slider's area in the tab; FullWidth: the bar and the tab.
    public sealed record BarLayout(Vector2 Size, Vector2 Socket, float SocketRadius, Rect2 Text, Rect2 LogKey,
        Rect2 MessageLine, Rect2 LogLine, Vector2 RuleFrom, Vector2 RuleTo, Rect2 History, Rect2[] Keys, Rect2 Rules, float FullWidth);

    public static BarLayout Layout(float width)
    {
        float x0 = Frame + Margin, y0 = Frame + Margin, h = SectionHeight;
        float hw = 4 * KeySide + 3 * KeyGap + 2 * KeyPad, hx = width - x0 - hw;
        var history = new Rect2(hx, y0, hw, h);
        var keys = new Rect2[4];
        for (var i = 0; i < 4; i++)
            keys[i] = new Rect2(hx + KeyPad + i * (KeySide + KeyGap), y0 + KeyPad, KeySide, KeySide);
        float tx = x0 + h + Gap, tw = Mathf.Max(KeySide + 2 * KeyPad, hx - Gap - tx);
        var text = new Rect2(tx, y0, tw, h);
        var logKey = new Rect2(tx + tw - KeyPad - KeySide, y0 + KeyPad, KeySide, KeySide);
        float lineX = tx + 16, lineW = Mathf.Max(0, logKey.Position.X - tx - 30);
        // the two lines are centred at 0.29 and 0.73 of the section's height
        var message = new Rect2(lineX, y0 + h * 0.29f - h * 0.2f, lineW, h * 0.4f);
        var log = new Rect2(lineX, y0 + h * 0.73f - h * 0.2f, lineW, h * 0.4f);
        return new BarLayout(new Vector2(width, Height), new Vector2(x0 + h / 2, y0 + h / 2), h / 2, text, logKey, message, log,
            new Vector2(tx + 12, y0 + h / 2), new Vector2(logKey.Position.X - 10, y0 + h / 2), history, keys,
            new Rect2(width - Frame, Frame + 3, TabSlider, TabHeight - 2 * Frame - 6), width + TabReach);
    }

    // The rules slider, in its own area (`area` its size; origin its top left): notch `at` (0 the first, fractions
    // between) along it, the notches spread so each label has a third of the width; the knob's centre near the bottom,
    // the labels centred in what is left above it.
    public static float NotchX(Vector2 area, float at) => NotchX(area, at, RulesLabels.Length);

    // The same for a slider of `count` notches (TopPanelSwitch: two).
    public static float NotchX(Vector2 area, float at, int count)
    {
        float pad = area.X / (2 * count);
        return pad + at * (area.X - 2 * pad) / Mathf.Max(1, count - 1);
    }

    public static float KnobY(Vector2 area) => area.Y - KnobRadius - 1.5f;
    public static float LabelY(Vector2 area) => (KnobY(area) - KnobRadius) / 2;

    // ---------- Textures ----------

    // The bar `width` wide and the tab past it: the texture is width + TabReach wide.
    public static Texture2D Backing(float width, Color frame) =>
        IconArt.Cached(("top-panel", Mathf.RoundToInt(width), frame), () =>
            IconArt.Rasterise(BackingSvg(width, frame), new Vector2I(Mathf.RoundToInt(width + TabReach), Mathf.RoundToInt(Height)), 1));

    // The rules slider's knob; held: being dragged (lower, darker, ringed in the frame colour).
    public static Texture2D Knob(bool held, Color frame)
    {
        int side = Mathf.RoundToInt(2 * (KnobRadius + KnobMargin));
        return IconArt.Cached(("top-panel-knob", held, frame), () => IconArt.Rasterise(KnobSvg(held, frame), new Vector2I(side, side), 1));
    }

    public static Texture2D Key(KeyKind kind, KeyLook look, bool on, Color frame, bool filled)
    {
        float side = KeySide + 2 * KeyMargin;
        return IconArt.Cached(("top-panel-key", kind, look, on, on ? frame : Colors.Black, filled), () =>
            IconArt.Rasterise(KeySvg(kind, look, on, frame, filled), new Vector2I(Mathf.RoundToInt(side), Mathf.RoundToInt(side)), 1));
    }

    // The log rows' small buttons, `size` pixels square: a bookmark ribbon, gold with a dark outline when the record is
    // bookmarked (filled), only a pale outline when not; the jump arrow, pale with a dark outline, as the redo key's.
    // Pencil and Bin (the bookmark map's list): pale with a dark outline, as the keys' icons; `filled` is ignored.
    public enum RowIconKind { Bookmark, Jump, Pencil, Bin }

    public static Texture2D RowIcon(RowIconKind kind, bool filled, int size) =>
        IconArt.Cached(("top-panel-row", kind, filled, size), () =>
        {
            var svg = new Svg(size, size);
            float c = size / 2f, s = size * 0.9f;
            if (kind == RowIconKind.Jump)
                Icon(svg, KeyKind.Redo, c, c, s, IconFill, KeyOutline);
            else if (kind == RowIconKind.Pencil)
                Pencil(svg, c, c, s);
            else if (kind == RowIconKind.Bin)
                Bin(svg, c, c, s);
            else if (filled)
                Icon(svg, KeyKind.Bookmark, c, c, s, BookmarkGold, KeyOutline);
            else
            {
                Vector2 P(float u, float v) => new(c + u * s, c + v * s);
                SvgPath ribbon = new SvgPath().MoveTo(P(-0.28f, -0.45f)).LineTo(P(0.28f, -0.45f)).LineTo(P(0.28f, 0.45f))
                    .LineTo(P(0, 0.2f)).LineTo(P(-0.28f, 0.45f)).Close();
                svg.Stroke(ribbon, IconFill, Mathf.Max(1, s * 0.07f));
            }
            return IconArt.Rasterise(svg.Text(), new Vector2I(size, size), 1);
        });

    // The dark sections' colour (the expanded message's background).
    public static Color SectionColour => Section;

    // The list's place inside the log's backing (the rows go in it).
    public static Rect2 LogList(Vector2 size) =>
        new(Frame + 10, Frame + 10, size.X - 2 * (Frame + 10), size.Y - 2 * (Frame + 10));

    public static Texture2D LogBacking(Vector2 size, Color frame) =>
        IconArt.Cached(("top-panel-log", Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), frame), () =>
        {
            var svg = new Svg(size.X, size.Y);
            Framed(svg, new Rect2(Vector2.Zero, size), OuterRadius, frame, Frame,
                inside => IconArt.Stone(svg, inside, size.X, size.Y, IconArt.StoneTop, IconArt.StoneBottom));
            Rect2 list = LogList(size);
            Sunken(svg, list, 12);
            return IconArt.Rasterise(svg.Text(), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), 1);
        });

    // ---------- The bookmark map (HistoryOverlay, BookmarkRow, HistoryMapView) ----------

    // The map's lines: the rim round every line, the core of the active line and of the others, the game line's gold.
    public static readonly Color MapRim = new("0e1824"), MapActive = new("eef3f8"), MapOther = new("5d7690"), MapGold = new("e0b84c");

    // A drop-down's backing (`size`): the top panel's frame and stone, with a dark sunken section at each of `sections`
    // (an area and its corner radius).
    public static Texture2D DrawerBacking(Vector2 size, Color frame, params (Rect2 Area, float Radius)[] sections) =>
        IconArt.Cached(("top-panel-drawer", Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), frame,
            string.Join(";", sections.Select(s => $"{s.Area}/{s.Radius}"))), () =>
        {
            var svg = new Svg(size.X, size.Y);
            Framed(svg, new Rect2(Vector2.Zero, size), OuterRadius, frame, Frame,
                inside => IconArt.Stone(svg, inside, size.X, size.Y, IconArt.StoneTop, IconArt.StoneBottom));
            foreach (var (area, radius) in sections)
                Sunken(svg, area, radius);
            return IconArt.Rasterise(svg.Text(), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), 1);
        });

    // Where the player is: a small round stone knob (lighter stone, lit along its top, a soft shadow) with a jewel in
    // the frame colour, `radius` across the knob, centred in a square texture 2 × (radius + 5) across.
    public static Texture2D HereKnob(float radius, Color frame)
    {
        int side = Mathf.CeilToInt(2 * (radius + 5));
        return IconArt.Cached(("top-panel-here", radius, frame), () =>
        {
            float c = side / 2f, r = radius;
            var svg = new Svg(side, side);
            svg.Fill(SvgPath.Circle(c, c + 2, r + 1.5f), new Color(0, 0, 0, 0.18f));
            svg.Fill(SvgPath.Circle(c, c + 1.2f, r + 0.5f), new Color(0, 0, 0, 0.3f));
            SvgPath body = SvgPath.Circle(c, c, r);
            svg.Fill(body, Paint.Linear(0, c - r, 0, c + r, (0, Shift(new Color("a7bacb"), 0.04f)), (1, new Color("5d7690"))));
            svg.Save();
            svg.Clip(body);
            svg.Stroke(SvgPath.Circle(c, c + 1.4f, r - 0.8f), new Color(1, 1, 1, 0.45f), 2);
            svg.Restore();
            svg.Stroke(body, KeyOutline, 1.5f);
            float jr = r * 0.42f;
            SvgPath jewel = SvgPath.Circle(c, c, jr);
            svg.Fill(jewel, Paint.Radial(c - jr * 0.35f, c - jr * 0.4f, c, c, jr, (0, Shift(frame, 0.45f)), (1, Shift(frame, -0.15f))));
            svg.Stroke(jewel, KeyOutline, 1);
            return IconArt.Rasterise(svg.Text(), new Vector2I(side, side), 1);
        });
    }

    // A ribbon `size` tall, centred in a texture RibbonMargin bigger all round: gold with a dark outline when bookmarked
    // (marked), a pale outline over the dark when not (the unbookmarked end of the active line); lit (hovered): outlined
    // in the frame colour, with a glow of it round the ribbon.
    public const float RibbonMargin = 8;

    public static Texture2D Ribbon(bool marked, bool lit, Color frame, int size)
    {
        int side = Mathf.CeilToInt(size + 2 * RibbonMargin);
        return IconArt.Cached(("top-panel-ribbon", marked, lit, lit ? frame : Colors.Black, size), () =>
        {
            float c = side / 2f, s = size;
            var svg = new Svg(side, side);
            SvgPath shape = RibbonPath(c, c, s);
            if (lit)
                for (var i = 3; i >= 1; i--) // the glow: widening, fainter strokes
                    svg.Stroke(shape, new Color(frame, 0.12f * (4 - i)), 3.5f * i);
            if (marked)
                Icon(svg, KeyKind.Bookmark, c, c, s, BookmarkGold, lit ? frame : KeyOutline);
            else
            {
                svg.Fill(shape, new Color("0e1824", 0.85f));
                svg.Stroke(shape, lit ? frame : IconFill, Mathf.Max(1, s * 0.07f));
            }
            return IconArt.Rasterise(svg.Text(), new Vector2I(side, side), 1);
        });
    }

    private static SvgPath RibbonPath(float cx, float cy, float s)
    {
        Vector2 P(float u, float v) => new(cx + u * s, cy + v * s);
        return new SvgPath().MoveTo(P(-0.28f, -0.45f)).LineTo(P(0.28f, -0.45f)).LineTo(P(0.28f, 0.45f)).LineTo(P(0, 0.2f)).LineTo(P(-0.28f, 0.45f)).Close();
    }

    // A stone key's body without an icon (TopPanelTextKey), `size` (panel pixels), KeyMargin bigger all round.
    public static Texture2D TextKey(KeyLook look, Vector2 size)
    {
        if (size.X < 2 || size.Y < 2)
            return null;
        Vector2I pixels = new(Mathf.CeilToInt(size.X + 2 * KeyMargin), Mathf.CeilToInt(size.Y + 2 * KeyMargin));
        return IconArt.Cached(("top-panel-text-key", look, Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), () =>
        {
            float x = KeyMargin, y = KeyMargin, w = size.X, h = size.Y;
            var svg = new Svg(pixels.X, pixels.Y);
            bool enabled = look != KeyLook.Disabled, down = look == KeyLook.Pressed;
            float drop = down ? 1.5f : 0, tone = (look == KeyLook.Hover ? 0.1f : 0) + (down ? -0.12f : 0);
            if (enabled && !down)
                svg.Fill(new SvgPath().RoundRect(x, y + 2, w, h, KeyRadius), new Color(0, 0, 0, 0.45f));
            Color top = new(enabled ? "93a9bd" : "6d7f90"), bottom = new(enabled ? "5d7690" : "55667a");
            SvgPath body = new SvgPath().RoundRect(x, y + drop, w, h, KeyRadius);
            svg.Fill(body, Paint.Linear(0, y, 0, y + h, (0, Shift(top, tone)), (1, Shift(bottom, tone))));
            if (enabled && !down)
            {
                svg.Save();
                svg.Clip(body);
                svg.Stroke(new SvgPath().RoundRect(x + 1, y + 1.5f, w - 2, h, KeyRadius), new Color(1, 1, 1, 0.35f), 2);
                svg.Restore();
            }
            svg.Stroke(body, KeyOutline, 1.5f);
            return IconArt.Rasterise(svg.Text(), pixels, 1);
        });
    }

    // A pencil, pointing up and to the right, about `s` across: pale with a dark outline, a dark tip.
    private static void Pencil(Svg svg, float cx, float cy, float s)
    {
        svg.Save();
        svg.Translate(cx, cy);
        svg.Rotate(-Mathf.Pi / 4);
        float w = s * 0.13f;
        SvgPath body = new SvgPath().MoveTo(-s * 0.3f, -w).LineTo(s * 0.22f, -w).LineTo(s * 0.45f, 0).LineTo(s * 0.22f, w).LineTo(-s * 0.3f, w).Close();
        SvgPath end = new SvgPath().RoundRect(-s * 0.46f, -w, s * 0.16f, 2 * w, s * 0.05f);
        foreach (SvgPath part in new[] { end, body })
        {
            svg.Stroke(part, KeyOutline, s * 0.14f);
            svg.Fill(part, IconFill);
        }
        svg.Stroke(new SvgPath().MoveTo(s * 0.22f, -w).LineTo(s * 0.22f, w).MoveTo(-s * 0.3f, -w).LineTo(-s * 0.3f, w), KeyOutline, s * 0.06f);
        svg.Fill(new SvgPath().MoveTo(s * 0.45f, 0).LineTo(s * 0.36f, -w * 0.4f).LineTo(s * 0.36f, w * 0.4f).Close(), KeyOutline);
        svg.Restore();
    }

    // A bin with a lid, about `s` across: pale with a dark outline.
    private static void Bin(Svg svg, float cx, float cy, float s)
    {
        svg.Save();
        svg.Translate(cx, cy);
        SvgPath body = new SvgPath().MoveTo(-s * 0.3f, -s * 0.22f).LineTo(s * 0.3f, -s * 0.22f).LineTo(s * 0.24f, s * 0.42f).LineTo(-s * 0.24f, s * 0.42f).Close();
        SvgPath lid = new SvgPath().RoundRect(-s * 0.4f, -s * 0.36f, s * 0.8f, s * 0.11f, s * 0.04f);
        SvgPath handle = new SvgPath().RoundRect(-s * 0.12f, -s * 0.46f, s * 0.24f, s * 0.12f, s * 0.04f);
        foreach (SvgPath part in new[] { handle, lid, body })
        {
            svg.Stroke(part, KeyOutline, s * 0.14f);
            svg.Fill(part, IconFill);
        }
        svg.Stroke(new SvgPath().MoveTo(-s * 0.1f, -s * 0.1f).LineTo(-s * 0.09f, s * 0.3f).MoveTo(s * 0.1f, -s * 0.1f).LineTo(s * 0.09f, s * 0.3f),
            KeyOutline, s * 0.07f, "round", "round");
        svg.Restore();
    }

    // ---------- Drawing ----------

    private static string BackingSvg(float width, Color frame)
    {
        BarLayout l = Layout(width);
        var svg = new Svg(l.FullWidth, Height);
        FramedBar(svg, width, frame, inside => IconArt.Stone(svg, inside, l.FullWidth, Height, IconArt.StoneTop, IconArt.StoneBottom));
        float r = l.SocketRadius;
        Sunken(svg, new Rect2(l.Socket - new Vector2(r, r), new Vector2(2 * r, 2 * r)), r);
        Sunken(svg, l.History, SectionRadius);
        Sunken(svg, l.Text, SectionRadius);
        svg.Stroke(new SvgPath().MoveTo(l.RuleFrom).LineTo(l.RuleTo), new Color(1, 1, 1, 0.12f), 1);
        Groove(svg, l.Rules);
        return svg.Text();
    }

    // The bar and the tab as one outline in the panels' frame: a dark edge, the colour lit from above, a dark edge;
    // `inside` fills the rest.
    private static void FramedBar(Svg svg, float width, Color frame, Action<SvgPath> inside)
    {
        Color edge = frame.Luminance < 0.25f ? new Color("d6d0c8") : new Color("1b1b1b");
        svg.Fill(BarShape(width, 0), edge);
        svg.Fill(BarShape(width, 1.5f), Paint.Linear(0, 0, 0, Height, (0, IconArt.Shade(frame, 0.16f)), (1, IconArt.Shade(frame, -0.16f))));
        svg.Fill(BarShape(width, Frame - 1.5f), edge);
        inside(BarShape(width, Frame));
    }

    // The bar (0..width × 0..Height) and the tab on top of its right end (width..width + TabReach × 0..TabHeight) as one
    // outline, moved in by `d`. Corners round where they stick out (OuterRadius; the tab's lower right TabCornerRadius),
    // the one under the tab hollow (TabHollowRadius). Moving in shrinks the round corners and grows the hollow one, so
    // each layer of the frame keeps its width all the way round. Corners go clockwise on screen.
    private static SvgPath BarShape(float width, float d)
    {
        float right = width + TabReach;
        var corners = new (Vector2 At, float Radius)[]
        {
            (new Vector2(d, d), OuterRadius), (new Vector2(right - d, d), OuterRadius),
            (new Vector2(right - d, TabHeight - d), TabCornerRadius), (new Vector2(width - d, TabHeight - d), -TabHollowRadius),
            (new Vector2(width - d, Height - d), OuterRadius), (new Vector2(d, Height - d), OuterRadius),
        };
        var path = new SvgPath();
        int n = corners.Length;
        for (var i = 0; i < n; i++)
        {
            Vector2 before = corners[(i + n - 1) % n].At, at = corners[i].At, after = corners[(i + 1) % n].At;
            float radius = corners[i].Radius >= 0 ? Mathf.Max(0.5f, corners[i].Radius - d) : -corners[i].Radius + d;
            Vector2 comingIn = (at - before).Normalized(), goingOut = (after - at).Normalized();
            Vector2 start = at - comingIn * radius, end = at + goingOut * radius;
            bool turnsRight = comingIn.X * goingOut.Y - comingIn.Y * goingOut.X > 0; // clockwise on screen: a round corner
            path.LineTo(start);
            path.Data($"A{Svg.N(radius)} {Svg.N(radius)} 0 0 {(turnsRight ? 1 : 0)} {Svg.N(end.X)} {Svg.N(end.Y)}");
        }
        return path.Close();
    }

    // The slider's groove, on the tab's stone: a dark slot, shadowed inside along its top, lit along its lower edge,
    // a pale notch under each label.
    private static void Groove(Svg svg, Rect2 area, int n = 3)
    {
        Vector2 size = area.Size, o = area.Position;
        float gy = o.Y + KnobY(size), gx = o.X + NotchX(size, 0, n) - 8, gw = NotchX(size, n - 1, n) - NotchX(size, 0, n) + 16, gh = GrooveHeight;
        SvgPath groove = new SvgPath().RoundRect(gx, gy - gh / 2, gw, gh, gh / 2);
        svg.Fill(groove, Section);
        svg.Save();
        svg.Clip(groove);
        svg.Stroke(new SvgPath().RoundRect(gx + 1, gy - gh / 2 + 1.5f, gw + 4, gh + 4, gh / 2), new Color(0, 0, 0, 0.45f), 3);
        svg.Restore();
        svg.Stroke(new SvgPath().MoveTo(gx + gh / 2, gy + gh / 2 + 0.8f).LineTo(gx + gw - gh / 2, gy + gh / 2 + 0.8f), new Color(1, 1, 1, 0.22f), 1);
        svg.Stroke(groove, new Color("0a121c", 0.7f), 1);
        for (var i = 0; i < n; i++)
            svg.Fill(SvgPath.Circle(o.X + NotchX(size, i, n), gy, NotchRadius), new Color("8ea2b5"));
    }

    // A slider's groove alone (TopPanelSwitch draws it on whatever stone it sits on): `size` its area, `count` notches.
    public static Texture2D SliderGroove(Vector2 size, int count)
    {
        if (size.X < 2 || size.Y < 2)
            return null;
        return IconArt.Cached(("top-panel-groove", Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y), count), () =>
        {
            var svg = new Svg(size.X, size.Y);
            Groove(svg, new Rect2(Vector2.Zero, size), count);
            return IconArt.Rasterise(svg.Text(), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), 1);
        });
    }

    // The knob: a round raised stone key (lighter stone, lit along its top, a soft shadow under it), a jewel in the frame
    // colour in the middle; held: lower, a little darker, its ring in the frame colour.
    private static string KnobSvg(bool held, Color frame)
    {
        float r = KnobRadius, c = r + KnobMargin, side = 2 * c, drop = held ? 1.5f : 3;
        var svg = new Svg(side, side);
        svg.Fill(SvgPath.Circle(c, c + drop, r + 2), new Color(0, 0, 0, 0.16f));
        svg.Fill(SvgPath.Circle(c, c + drop * 0.6f, r + 0.6f), new Color(0, 0, 0, 0.32f));
        SvgPath body = SvgPath.Circle(c, c, r);
        svg.Fill(body, Paint.Linear(0, c - r, 0, c + r, (0, Shift(new Color("a7bacb"), held ? -0.06f : 0.04f)), (1, Shift(new Color("5d7690"), held ? -0.06f : 0))));
        svg.Save();
        svg.Clip(body);
        svg.Stroke(SvgPath.Circle(c, c + 1.8f, r - 1), new Color(1, 1, 1, 0.45f), 2.5f);
        svg.Restore();
        svg.Stroke(body, held ? frame : KeyOutline, held ? 2.5f : 1.8f);
        float jr = r * 0.36f;
        SvgPath jewel = SvgPath.Circle(c, c, jr);
        svg.Fill(jewel, Paint.Radial(c - jr * 0.35f, c - jr * 0.4f, c, c, jr, (0, Shift(frame, 0.45f)), (1, Shift(frame, -0.15f))));
        svg.Stroke(jewel, KeyOutline, 1.2f);
        return svg.Text();
    }

    // A frame like the panels': a dark edge, the colour lit from above, a dark edge; `inside` fills the rest.
    private static void Framed(Svg svg, Rect2 area, float radius, Color frame, float width, Action<SvgPath> inside)
    {
        float x = area.Position.X, y = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        Color edge = frame.Luminance < 0.25f ? new Color("d6d0c8") : new Color("1b1b1b");
        svg.Fill(new SvgPath().RoundRect(x, y, w, h, radius), edge);
        svg.Fill(new SvgPath().RoundRect(x + 1.5f, y + 1.5f, w - 3, h - 3, radius - 1.5f),
            Paint.Linear(0, y, 0, y + h, (0, IconArt.Shade(frame, 0.16f)), (1, IconArt.Shade(frame, -0.16f))));
        svg.Fill(new SvgPath().RoundRect(x + width - 1.5f, y + width - 1.5f, w - 2 * width + 3, h - 2 * width + 3, Mathf.Max(2, radius - width + 1.5f)), edge);
        inside(new SvgPath().RoundRect(x + width, y + width, w - 2 * width, h - 2 * width, Mathf.Max(1, radius - width)));
    }

    // A dark sunken section, as the panels' (radius half the height makes the round socket).
    private static void Sunken(Svg svg, Rect2 area, float radius)
    {
        float x = area.Position.X, y = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        SvgPath section = new SvgPath().RoundRect(x, y, w, h, radius);
        svg.Fill(section, Section);
        svg.Save();
        svg.Clip(section);
        svg.Stroke(new SvgPath().RoundRect(x + 2, y + 2, w + 6, h + 6, radius), new Color(0, 0, 0, 0.35f), 6);
        svg.Stroke(new SvgPath().RoundRect(x - 6, y - 6, w + 4, h + 4, radius), new Color(1, 1, 1, 0.08f), 6);
        svg.Restore();
        svg.Stroke(section, new Color("0a121c", 0.7f), 1);
    }

    // A raised stone key: lighter stone, lit along its top, a drop shadow under it; lighter still under the pointer,
    // pressed in (no shadow, darker, 1.5 lower) while held, flat and dim when it can't be used; `on` (the log or the map
    // open) outlined and glowing in the frame colour. `filled`: the bookmark in gold (this position is bookmarked).
    private static string KeySvg(KeyKind kind, KeyLook look, bool on, Color frame, bool filled)
    {
        float side = KeySide + 2 * KeyMargin, x = KeyMargin, y = KeyMargin, w = KeySide, h = KeySide;
        var svg = new Svg(side, side);
        bool enabled = look != KeyLook.Disabled, down = look == KeyLook.Pressed;
        float drop = down ? 1.5f : 0, lift = look == KeyLook.Hover ? 0.1f : 0, dark = down ? -0.12f : 0;
        if (enabled && !down)
            svg.Fill(new SvgPath().RoundRect(x, y + 2, w, h, KeyRadius), new Color(0, 0, 0, 0.45f));
        if (on)
            for (var i = 3; i >= 1; i--) // the glow: widening, fainter strokes round the key
                svg.Stroke(new SvgPath().RoundRect(x, y + drop, w, h, KeyRadius), new Color(frame, 0.12f * (4 - i)), 3.5f * i);
        Color top = new(enabled ? "93a9bd" : "6d7f90"), bottom = new(enabled ? "5d7690" : "55667a");
        SvgPath body = new SvgPath().RoundRect(x, y + drop, w, h, KeyRadius);
        svg.Fill(body, Paint.Linear(0, y, 0, y + h, (0, Shift(top, lift + dark)), (1, Shift(bottom, lift + dark))));
        if (enabled && !down)
        {
            svg.Save();
            svg.Clip(body);
            svg.Stroke(new SvgPath().RoundRect(x + 1, y + 1.5f, w - 2, h, KeyRadius), new Color(1, 1, 1, 0.35f), 2);
            svg.Restore();
        }
        svg.Stroke(body, on ? frame : KeyOutline, 1.5f);
        if (!enabled)
        {
            svg.Save();
            svg.Opacity(0.4f);
        }
        Icon(svg, kind, x + w / 2, y + h / 2 + drop, Mathf.Min(w, h) * 0.5f, kind == KeyKind.Bookmark && filled ? BookmarkGold : IconFill, KeyOutline);
        if (!enabled)
            svg.Restore();
        return svg.Text();
    }

    // Towards white for a ≥ 0, black below.
    private static Color Shift(Color c, float a) => IconArt.Shade(c, a);

    // A key's icon, centred on (cx, cy), about `s` across: filled pale with a dark outline.
    private static void Icon(Svg svg, KeyKind kind, float cx, float cy, float s, Color fill, Color line)
    {
        float lw = Mathf.Max(1, s * 0.09f);
        var p = new SvgPath();
        Vector2 P(float u, float v) => new(cx + u * s, cy + v * s);
        switch (kind)
        {
            case KeyKind.Undo:
            case KeyKind.Redo:
            {
                float d = kind == KeyKind.Undo ? -1 : 1;
                p.MoveTo(P(d * 0.38f, 0)).LineTo(P(-d * 0.28f, -0.4f)).LineTo(P(-d * 0.28f, 0.4f)).Close();
                break;
            }
            case KeyKind.Bookmark:
                p.MoveTo(P(-0.28f, -0.45f)).LineTo(P(0.28f, -0.45f)).LineTo(P(0.28f, 0.45f)).LineTo(P(0, 0.2f)).LineTo(P(-0.28f, 0.45f)).Close();
                break;
            case KeyKind.Map:
                p.MoveTo(P(-0.45f, -0.32f)).LineTo(P(-0.15f, -0.42f)).LineTo(P(0.15f, -0.32f)).LineTo(P(0.45f, -0.42f)).LineTo(P(0.45f, 0.32f))
                    .LineTo(P(0.15f, 0.42f)).LineTo(P(-0.15f, 0.32f)).LineTo(P(-0.45f, 0.42f)).Close();
                break;
            case KeyKind.Log:
                p.RoundRect(cx - 0.34f * s, cy - 0.44f * s, 0.68f * s, 0.88f * s, 0.08f * s);
                break;
        }
        svg.Stroke(p, line, lw * 2);
        svg.Fill(p, fill);
        var details = new SvgPath();
        if (kind == KeyKind.Map)
            details.MoveTo(P(-0.15f, -0.42f)).LineTo(P(-0.15f, 0.32f)).MoveTo(P(0.15f, -0.32f)).LineTo(P(0.15f, 0.42f));
        else if (kind == KeyKind.Log)
            foreach (float v in new[] { -0.24f, -0.08f, 0.08f, 0.24f })
                details.MoveTo(P(-0.2f, v)).LineTo(P(0.2f, v));
        if (kind is KeyKind.Map or KeyKind.Log)
            svg.Stroke(details, line, lw * 0.9f, "round", "round");
    }
}
