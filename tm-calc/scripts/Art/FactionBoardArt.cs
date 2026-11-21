using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// One row of building slots on a faction board (all rectangles in board pixels, origin at the board's top left):
//   Panel       the parchment the row is drawn on
//   Cost        the cost column, on the left
//   Power       the power column, on the right (the building's power value, as small tokens)
//   Slots       the sunken slots, left to right: each holds a building, with its income under it
//   BaseIncome  dwellings only: the space for the faction's base income
//   Spare       stronghold and sanctuary only: the free space between the slot and the power column (the ability)
//   Landscape   dwellings only, with the Landscapes expansion: the well for the special landscape's token, just before
//               the power column (as high as it is wide: the row's inside height)
public sealed record SlotRow(Rect2 Panel, Rect2 Cost, Rect2 Power, Rect2[] Slots, Rect2? BaseIncome, Rect2? Spare,
    Rect2? Landscape = null);

// Where everything is on the building-slots board: its size, and a row per building kind.
public sealed record BuildingSlotsLayout(Vector2 Size, IReadOnlyDictionary<PieceKind, SlotRow> Rows);

// Parts of a faction board: the building slots, and the power bowls (see PowerBowls).
// The building slots: a frame in the faction's colour round slate stone, and a parchment panel per building kind.
// Stronghold (left) and sanctuary (right) on top; four trading posts (left) and three temples (right); eight
// dwellings across the bottom. The board is blank: icons, buildings and numbers are placed on it in Godot, using
// Layout.
//   FactionBoardArt.BuildingSlots(FactionBoardArt.FrameColour(Terrain.Forest))   the board, 822 × 400
//   FactionBoardArt.Layout.Rows[PieceKind.Temple].Slots[0]                        the first temple's slot
// The slots are sized for buildings drawn LayoutUnit pixels per dwelling length (a PieceArt texture shown at
// scale LayoutUnit / PieceArt.Unit); buildings can be drawn bigger or smaller than that independently.
public static class FactionBoardArt
{
    public const float LayoutUnit = 38f;

    // The frame's colour for a faction, by its home terrain: the player colours.
    public static Color FrameColour(Terrain home) => home switch
    {
        Terrain.Wasteland => new Color("d23c30"),
        Terrain.Desert => new Color("ecc840"),
        Terrain.Plains => new Color("8c5c30"),
        Terrain.Swamp => new Color("2b2827"),
        Terrain.Lake => new Color("2f7ad0"),
        Terrain.Forest => new Color("3fa04d"),
        Terrain.Mountain => new Color("a0a3a8"),
        Terrain.Ice => new Color("f1f5f8"),
        Terrain.Volcano => new Color("ee7a2c"),
        _ => new Color("a0a3a8"),
    };

    // The board in the faction's colour, at `scale` times its natural size (Layout's rectangles scale the same).
    // landscapes: the variant with a well for the special landscape in the dwelling row (LandscapeLayout).
    public static Texture2D BuildingSlots(Color frame, float scale = 1f, bool landscapes = false)
    {
        Vector2 size = LayoutFor(landscapes).Size * scale;
        return IconArt.Cached(("building slots", frame, scale, landscapes),
            () => IconArt.Rasterise(SvgText(frame, landscapes), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), scale));
    }

    public static BuildingSlotsLayout Layout => _layout ??= MakeLayout(false);
    private static BuildingSlotsLayout _layout;

    // With the Landscapes expansion: the dwelling row makes room for the landscape's well just before its power column.
    // The board keeps its size; the eight dwelling slots narrow (equally, only as far as needed) and close up, never
    // nearer than SlotGap.
    public static BuildingSlotsLayout LandscapeLayout => _landscapeLayout ??= MakeLayout(true);
    private static BuildingSlotsLayout _landscapeLayout;

    public static BuildingSlotsLayout LayoutFor(bool landscapes) => landscapes ? LandscapeLayout : Layout;

    // ---------- Sizes (board pixels) ----------

    // The coloured frame, the stone between it and the panels, the space between panels.
    private const float Frame = 6, Pad = 9, Gap = 10;
    private const float Margin = 8, Spacing = 8, SlotGap = 6, Room = 5; // in a panel: margin, space between its areas, least space between slots, room round a building in its slot
    private const float CostWidth = 64, PowerWidth = 26, BaseWidth = 64;
    private const float MinInner = 68; // a panel's least inside height: two cost icons, or three power tokens

    // ---------- The layout ----------

    private static BuildingSlotsLayout MakeLayout(bool landscapes)
    {
        // How big each building is drawn at LayoutUnit, with its outline's thickness.
        Vector2 SizeOf(PieceKind kind)
        {
            Vector2 drawn = PieceArt.PictureFor(kind).Bounds.Size * (LayoutUnit / PieceArt.Unit);
            float line = Mathf.Max(1.1f, Mathf.Max(drawn.X, drawn.Y) * 0.035f);
            return drawn + Vector2.One * 2 * line;
        }
        // A slot: the biggest of these buildings, with room round it.
        Vector2 Slot(params PieceKind[] kinds) => new(
            MathF.Ceiling(kinds.Max(k => SizeOf(k).X) + 2 * Room),
            MathF.Ceiling(kinds.Max(k => SizeOf(k).Y) + 2 * Room));
        Vector2 big = Slot(PieceKind.Stronghold, PieceKind.Sanctuary), mid = Slot(PieceKind.TradingPost, PieceKind.Temple), dwelling = Slot(PieceKind.Dwelling);

        float PanelWidth(int n, float slotWidth) => Margin + CostWidth + Spacing + n * slotWidth + (n - 1) * SlotGap + Spacing + PowerWidth + Margin;
        float tradingNeed = PanelWidth(4, mid.X), templeNeed = PanelWidth(3, mid.X);
        float inner = Mathf.Max(PanelWidth(8, dwelling.X) + BaseWidth + Spacing,
            Mathf.Max(tradingNeed + Gap + templeNeed, 2 * PanelWidth(1, big.X) + 2 * 40 + Gap));
        // The two upper columns share the width in proportion to what the trading posts and temples need.
        float leftWidth = MathF.Floor((inner - Gap) * tradingNeed / (tradingNeed + templeNeed) + 0.5f), rightWidth = inner - Gap - leftWidth;
        float RowHeight(Vector2 slot) => Mathf.Max(slot.Y, MinInner) + 2 * Margin;

        float inset = Frame + Pad;
        var rows = new Dictionary<PieceKind, SlotRow>();
        void Row(PieceKind kind, float x, float y, float width, float height, int n, float slotWidth)
        {
            float top = y + Margin, innerHeight = height - 2 * Margin;
            var cost = new Rect2(x + Margin, top, CostWidth, innerHeight);
            var power = new Rect2(x + width - Margin - PowerWidth, top, PowerWidth, innerHeight);
            float sx = x + Margin + CostWidth + Spacing;
            Rect2? baseIncome = null, spare = null;
            if (kind == PieceKind.Dwelling)
            {
                baseIncome = new Rect2(sx, top, BaseWidth, innerHeight);
                sx += BaseWidth + Spacing;
            }
            float end = power.Position.X - Spacing;
            Rect2? landscape = null;
            if (kind == PieceKind.Dwelling && landscapes)
            {
                landscape = new Rect2(end - innerHeight, top, innerHeight, innerHeight);
                end = landscape.Value.Position.X - Spacing;
                slotWidth = Mathf.Min(slotWidth, MathF.Floor((end - sx - (n - 1) * SlotGap) / n));
            }
            float step = n > 1 ? (end - sx - n * slotWidth) / (n - 1) : 0; // slots spread across the panel
            Rect2[] slots = Enumerable.Range(0, n).Select(i => new Rect2(sx + i * (slotWidth + step), top, slotWidth, innerHeight)).ToArray();
            if (n == 1)
            {
                float fx = slots[0].End.X + Spacing;
                spare = new Rect2(fx, top, end - fx, innerHeight);
            }
            rows[kind] = new SlotRow(new Rect2(x, y, width, height), cost, power, slots, baseIncome, spare, landscape);
        }
        float rowY = inset;
        Row(PieceKind.Stronghold, inset, rowY, leftWidth, RowHeight(big), 1, big.X);
        Row(PieceKind.Sanctuary, inset + leftWidth + Gap, rowY, rightWidth, RowHeight(big), 1, big.X);
        rowY += RowHeight(big) + Gap;
        Row(PieceKind.TradingPost, inset, rowY, leftWidth, RowHeight(mid), 4, mid.X);
        Row(PieceKind.Temple, inset + leftWidth + Gap, rowY, rightWidth, RowHeight(mid), 3, mid.X);
        rowY += RowHeight(mid) + Gap;
        Row(PieceKind.Dwelling, inset, rowY, inner, RowHeight(dwelling), 8, dwelling.X);
        rowY += RowHeight(dwelling);
        return new BuildingSlotsLayout(new Vector2(inner + 2 * inset, rowY + inset), rows);
    }

    // ---------- Drawing ----------

    private static string SvgText(Color frame, bool landscapes)
    {
        Vector2 size = LayoutFor(landscapes).Size;
        float w = size.X, h = size.Y;
        var svg = new Svg(w, h);
        Color edge = frame.Luminance < 0.25f ? new Color("d6d0c8") : new Color("1b1b1b"); // a black frame gets light edges so it shows
        // The frame: a dark edge, the faction's colour (lit from above), a dark edge, then stone.
        svg.Fill(new SvgPath().RoundRect(0, 0, w, h, 18), edge);
        svg.Fill(new SvgPath().RoundRect(1.5f, 1.5f, w - 3, h - 3, 16.5f),
            Paint.Linear(0, 0, 0, h, (0, IconArt.Shade(frame, 0.16f)), (1, IconArt.Shade(frame, -0.16f))));
        svg.Fill(new SvgPath().RoundRect(Frame - 1.5f, Frame - 1.5f, w - 2 * Frame + 3, h - 2 * Frame + 3, 12.5f), edge);
        IconArt.Stone(svg, new SvgPath().RoundRect(Frame, Frame, w - 2 * Frame, h - 2 * Frame, 11), w, h, IconArt.StoneTop, IconArt.StoneBottom);

        foreach (SlotRow row in LayoutFor(landscapes).Rows.Values)
        {
            // the panel: parchment; then its slots as wells and its power column as a lilac strip (the cost column
            // and the base income space are plain parchment)
            Rect2 p = row.Panel;
            SvgPath panel = new SvgPath().RoundRect(p.Position.X, p.Position.Y, p.Size.X, p.Size.Y, 8);
            svg.Fill(panel, Paint.Linear(0, p.Position.Y, 0, p.End.Y, (0, new Color("f4e9c3")), (1, new Color("e2cf96"))));
            svg.Stroke(panel, new Color("7d683a"), 1.2f);
            foreach (Rect2 slot in row.Slots)
                Well(svg, slot, 6, new Color("d8c38a"), new Color("6e5528", 0.75f));
            Well(svg, row.Power, PowerWidth / 2, new Color("d9c6dc"), new Color("693c73", 0.55f));
            if (row.Landscape is Rect2 well)
                Well(svg, well, well.Size.X / 2, new Color("d8c38a"), new Color("6e5528", 0.75f)); // round: the token is
        }
        return svg.Text();
    }

    // One building slot's well on its own, `size` board pixels, at `scale` (the Changelings' 9th dwelling's slot).
    public static Texture2D SlotWell(Vector2 size, float scale = 1f) =>
        IconArt.Cached(("slot well", size, scale), () =>
        {
            var svg = new Svg(size.X, size.Y);
            Well(svg, new Rect2(Vector2.Zero, size), 6, new Color("d8c38a"), new Color("6e5528", 0.75f));
            return IconArt.Rasterise(svg.Text(), new Vector2I(Mathf.RoundToInt(size.X * scale), Mathf.RoundToInt(size.Y * scale)), scale);
        });

    // A sunken well: its fill, a soft shadow along the top and left, a highlight along the bottom and right, a thin edge.
    private static void Well(Svg svg, Rect2 area, float radius, Color fill, Color edge)
    {
        float x = area.Position.X, y = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        SvgPath well = new SvgPath().RoundRect(x, y, w, h, radius);
        svg.Fill(well, fill);
        svg.Save();
        svg.Clip(well);
        svg.Stroke(new SvgPath().RoundRect(x + 1.5f, y + 1.5f, w + 4, h + 4, radius), new Color("5a3e14", 0.3f), 4);
        svg.Stroke(new SvgPath().RoundRect(x - 4, y - 4, w + 2.5f, h + 2.5f, radius), new Color("fffae6", 0.5f), 4);
        svg.Restore();
        svg.Stroke(well, edge, 1);
    }

    // ---------- Power bowls ----------
    // Bowls I, II and III left to right: squares with rounded corners, edged in each bowl's purple from the printed
    // board (I darkest, III brightest) and filled with that purple brought most of the way to white, so the purple
    // counters stand out. 460 × 152 at natural size.
    //   FactionBoardArt.PowerBowls()      the three bowls, one texture
    //   FactionBoardArt.PowerBowl(2)      where bowl II is in it, in its pixels (origin top left)

    private const float BowlSide = 152, BowlGap = 2, BowlRadius = 26, BowlLine = 6; // 3 × 152 + 2 × 2 = 460 across
    private const float BowlFillTowardsWhite = 0.85f;
    private static readonly Color[] BowlEdges = { new("5b3a6d"), new("83417b"), new("b0308f") };

    public static Vector2 PowerBowlsSize => new(3 * BowlSide + 2 * BowlGap, BowlSide);

    // Bowl 1, 2 or 3 (I, II, III).
    public static Rect2 PowerBowl(int bowl) => new((bowl - 1) * (BowlSide + BowlGap), 0, BowlSide, BowlSide);

    public static Texture2D PowerBowls(float scale = 1f)
    {
        Vector2 size = PowerBowlsSize * scale;
        return IconArt.Cached(("power bowls", scale),
            () => IconArt.Rasterise(PowerBowlsSvg(), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), scale));
    }

    // ---------- Player panel backings ----------
    // What the building board sits on: framed in the faction's colour like the board, stone inside, darker sunken
    // sections. The frame is as wide as the board's frame at BackingBoardScale, so where the board (or, compact, the
    // parchment panel) sits on the bottom edge, the two bottom lines lie on top of each other. Sections keep
    // BackingMargin inside the frame and BackingGap from each other and from the board.
    //   Full, 1400 × 600: the board centred on the bottom edge; a section either side of it, one above it (250
    //     wide from 350 in a 900-wide panel, scaled by 1400 / 900, less SectionShade at each side; where the compact
    //     top section is, from the top margin and as high).
    //   Compact, 1400 × 300: the top section, where the full one is across (less SectionShade at each side, as
    //     there), down to a parchment panel straight under it on the bottom edge (CompactStripHeight high, framed the
    //     same way, from the section's unnarrowed left edge); a low section to its left and a full-height one at the
    //     right.
    //   Both are PassColumnWidth wider than the 1400 the sections are laid out in: plain stone at the right, room for the
    //     player's pass tile (a pass tile's width, 120, plus BackingMargin either side).
    //   FactionBoardArt.Backing(colour)                   the full backing; Backing(colour, compact: true) the other
    //   FactionBoardArt.BackingLayout(compact)            where everything on it goes, in its pixels (origin top left)
    public const float BackingBoardScale = 1.3f;
    private const float BackingWidth = 1400, FullBackingHeight = 600, CompactBackingHeight = 300;
    public const float PassColumnWidth = 120 + 2 * 12; // a pass tile's width, plus BackingMargin either side
    private static float TotalWidth => BackingWidth + PassColumnWidth;

    private const float BackingMargin = 12, BackingGap = 12, BackingRadius = 24, SectionRadius = 14, CompactRightWidth = 175;
    private const float TopSectionShift = 20; // the top section (and, compact, the parchment panel under it) moved right
    // Compact: the parchment panel's height, fixed (its own, not the full layout's board's), so changes to the board
    // don't resize the compact sections. The top section fills down to it.
    private const float CompactStripHeight = 139.4f;
    private static readonly Color Section = new("22374f");
    public static Color SectionColour => Section; // the dark sections' fill (e.g. the score badge's)

    // Board: where the building board goes (full only, at BackingBoardScale); Strip: the parchment panel (compact only).
    public sealed record BackingParts(Vector2 Size, Rect2 Top, Rect2 Left, Rect2 Right, Rect2? Board, Rect2? Strip);

    public static BackingParts BackingLayout(bool compact)
    {
        float m = Frame * BackingBoardScale + BackingMargin, g = BackingGap;
        Vector2 board = Layout.Size * BackingBoardScale;
        var boardRect = new Rect2((BackingWidth - board.X) / 2, FullBackingHeight - board.Y, board);
        var top = new Rect2(350 * BackingWidth / 900 + TopSectionShift, m, 250 * BackingWidth / 900, boardRect.Position.Y - g - m);
        float stripY = CompactBackingHeight - CompactStripHeight;
        if (!compact)
        {
            // The top section where the compact one is (as high, from the top margin), narrower by SectionShade on
            // each side.
            top = new Rect2(top.Position.X + SectionShade, m, top.Size.X - 2 * SectionShade, stripY - g - m);
            float y = boardRect.Position.Y, h = FullBackingHeight - m - y, rightX = boardRect.End.X + g;
            return new BackingParts(new Vector2(TotalWidth, FullBackingHeight), top,
                new Rect2(m, y, boardRect.Position.X - g - m, h), new Rect2(rightX, y, BackingWidth - m - rightX, h), boardRect, null);
        }
        var right = new Rect2(BackingWidth - m - CompactRightWidth, m, CompactRightWidth, CompactBackingHeight - 2 * m);
        // The parchment panel and the low section beside it keep to the top section's full width; the section itself is
        // narrower by SectionShade on each side, as the full one.
        float x = top.Position.X;
        var strip = new Rect2(x, stripY, right.Position.X - g - x, CompactBackingHeight - stripY);
        top = new Rect2(x + SectionShade, m, top.Size.X - 2 * SectionShade, stripY - g - m);
        return new BackingParts(new Vector2(TotalWidth, CompactBackingHeight), top,
            new Rect2(m, stripY, x - g - m, CompactBackingHeight - m - stripY), right, null, strip);
    }

    public static Texture2D Backing(Color frame, bool compact = false, float scale = 1f)
    {
        Vector2 size = BackingLayout(compact).Size * scale;
        return IconArt.Cached(("backing", frame, compact, scale),
            () => IconArt.Rasterise(BackingSvg(frame, compact), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), scale));
    }

    private static string BackingSvg(Color frame, bool compact)
    {
        BackingParts parts = BackingLayout(compact);
        var svg = new Svg(parts.Size.X, parts.Size.Y);
        float f = Frame * BackingBoardScale;
        Framed(svg, new Rect2(Vector2.Zero, parts.Size), BackingRadius, frame, f,
            inside => IconArt.Stone(svg, inside, parts.Size.X, parts.Size.Y, IconArt.StoneTop, IconArt.StoneBottom));
        foreach (Rect2 r in new[] { parts.Top, parts.Left, parts.Right })
            SunkenSection(svg, r);
        if (parts.Strip is Rect2 strip)
            Framed(svg, strip, 18 * BackingBoardScale, frame, f, inside => svg.Fill(inside,
                Paint.Linear(0, strip.Position.Y, 0, strip.End.Y, (0, new Color("f4e9c3")), (1, new Color("e2cf96")))));
        return svg.Text();
    }

    // A plain bar in the boards' look, `size` of the board's pixels: the frame in the faction's colour round the slate
    // stone (Calculator's +1 bar).
    public static Texture2D FramedStone(Color frame, Vector2 size, float scale = 1f) =>
        IconArt.Cached(("framed stone", frame, size, scale), () =>
        {
            var svg = new Svg(size.X, size.Y);
            Framed(svg, new Rect2(Vector2.Zero, size), Mathf.Min(BackingRadius, size.Y / 3), frame, Frame * BackingBoardScale,
                inside => IconArt.Stone(svg, inside, size.X, size.Y, IconArt.StoneTop, IconArt.StoneBottom));
            return IconArt.Rasterise(svg.Text(), new Vector2I(Mathf.RoundToInt(size.X * scale), Mathf.RoundToInt(size.Y * scale)), scale);
        });

    // A frame like the board's: a dark edge, the faction's colour lit from above, a dark edge; then `inside` fills
    // the rest (given its outline).
    private static void Framed(Svg svg, Rect2 area, float radius, Color frame, float width, Action<SvgPath> inside)
    {
        float x = area.Position.X, y = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        Color edge = frame.Luminance < 0.25f ? new Color("d6d0c8") : new Color("1b1b1b"); // a black frame gets light edges
        svg.Fill(new SvgPath().RoundRect(x, y, w, h, radius), edge);
        svg.Fill(new SvgPath().RoundRect(x + 1.5f, y + 1.5f, w - 3, h - 3, radius - 1.5f),
            Paint.Linear(0, y, 0, y + h, (0, IconArt.Shade(frame, 0.16f)), (1, IconArt.Shade(frame, -0.16f))));
        svg.Fill(new SvgPath().RoundRect(x + width - 1.5f, y + width - 1.5f, w - 2 * width + 3, h - 2 * width + 3, Mathf.Max(2, radius - width + 1.5f)), edge);
        inside(new SvgPath().RoundRect(x + width, y + width, w - 2 * width, h - 2 * width, Mathf.Max(1, radius - width)));
    }

    // A darker sunken section: a shadow inside along the top and left, a faint light along the bottom and right.
    // How far into a section its inner shadow (top, left) and light (bottom, right) reach: the 6-wide strokes below,
    // centred 1 outside its edge.
    private const float SectionShade = 5;

    private static void SunkenSection(Svg svg, Rect2 area)
    {
        float x = area.Position.X, y = area.Position.Y, w = area.Size.X, h = area.Size.Y;
        SvgPath section = new SvgPath().RoundRect(x, y, w, h, SectionRadius);
        svg.Fill(section, Section);
        svg.Save();
        svg.Clip(section);
        svg.Stroke(new SvgPath().RoundRect(x + 2, y + 2, w + 6, h + 6, SectionRadius), new Color(0, 0, 0, 0.35f), 6);
        svg.Stroke(new SvgPath().RoundRect(x - 6, y - 6, w + 4, h + 4, SectionRadius), new Color(1, 1, 1, 0.08f), 6);
        svg.Restore();
        svg.Stroke(section, new Color("0a121c", 0.7f), 1);
    }

    private static string PowerBowlsSvg()
    {
        var svg = new Svg(PowerBowlsSize.X, PowerBowlsSize.Y);
        for (var bowl = 1; bowl <= 3; bowl++)
        {
            Color edge = BowlEdges[bowl - 1], fill = edge.Lerp(Colors.White, BowlFillTowardsWhite);
            Rect2 r = PowerBowl(bowl);
            float t = BowlLine, x = r.Position.X + t / 2, y = r.Position.Y + t / 2, w = r.Size.X - t, h = r.Size.Y - t, radius = BowlRadius - t / 2;
            // the edge line sits inside the square, so each bowl is exactly BowlSide across
            SvgPath inner = new SvgPath().RoundRect(x, y, w, h, radius);
            svg.Fill(inner, fill);
            // a soft shadow inside along the top and left, in the bowl's own purple, so it reads as a bowl
            svg.Save();
            svg.Clip(inner);
            svg.Stroke(new SvgPath().RoundRect(x + 3, y + 3, w + 6, h + 6, radius), new Color(edge, 0.22f), 8);
            svg.Restore();
            svg.Stroke(inner, edge, t);
        }
        return svg.Text();
    }
}
