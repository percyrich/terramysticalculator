using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The special landscapes' tokens (Landscapes expansion): one round token per faction, drawn
// in code through Svg, and IconArt turns the SVG into a texture:
//   LandscapeArt.Token(Faction.Auren, 110)      110 × 110 pixels, the token filling it
//   LandscapeArt.Token(faction, 110, marks: false)  the same without its mark (placed on the map)
//   LandscapeArt.TokenDiameterOnHex(HexArt.Radius)  the size that sits snugly on a map hex (touching its outline)
// Each token: a flat disc in the colour of the hex it sits on (HexArt.TerrainColour of the faction's home terrain;
// the river colour for the Mermaids and the Riverwalkers; the Shapeshifters' a wedge of each of the seven base home
// terrains), a pale bevel ring and a dark outline; on it the landscape's picture: SVG art (Pictures, in
// LandscapeArt.Pictures.cs), kept inside the outline. At the bottom, in ice white, a hint of what the landscape needs:
// the building it needs first (two temples for the Witches), a spade (Mermaids), a priest (Riverwalkers), or the
// any-cult symbol with a 3 (Acolytes). Where the picture would reach the hint, it is raised just enough to clear it
// (Fit), except the Swarmlings' and the Nomads' (a little overlap is fine there).
// Sizes and positions are in units of the token's radius, so the drawing is the same at any size; only the outline
// keeps to at least a pixel.
public static partial class LandscapeArt
{
    // A faction's token, `size` pixels across (made once per faction, size and marks, then reused). marks: false
    // leaves out what the landscape needs (once it is placed); the picture stays where it is.
    public static Texture2D Token(Faction faction, int size, bool marks = true) =>
        IconArt.Cached(("landscape", faction, size, marks), () => IconArt.Rasterise(SvgText(faction, size, marks), new Vector2I(size, size), 1f));

    // How big a token is on a map hex of radius `hexRadius` (HexArt's pixels): its edge touches the inside of the hex's
    // dark outline (the hex's inradius less half the outline, which is HexArt's max(2, 0.09 r)).
    public static float TokenDiameterOnHex(float hexRadius) =>
        2 * (hexRadius * Mathf.Sqrt(3) / 2 - Mathf.Max(2f, hexRadius * 0.09f) / 2) + 1; // + 1: Radius() leaves half a pixel each side

    // The terrain a token's disc is coloured for (null for the Shapeshifters: every terrain).
    public static Terrain? Ground(Faction faction) => faction switch
    {
        Faction.Shapeshifters => null,
        Faction.Mermaids or Faction.Riverwalkers => Terrain.River,
        _ => Factions.All[faction].Home,
    };

    // The token as SVG text, `size` pixels square.
    public static string SvgText(Faction faction, int size, bool marks = true)
    {
        var svg = new Svg(size, size);
        float r = size / 2f - 0.5f; // the token's radius in pixels (half a pixel spare for the outline's smoothing)
        var pen = new Pen(svg, r);
        svg.Save();
        svg.Translate(size / 2f, size / 2f);
        svg.Save();
        svg.Scale(r, r);
        if (Ground(faction) is Terrain ground)
            pen.Disc(HexArt.TerrainColour(ground), null);
        else
            pen.Disc(HexArt.TerrainColour(Terrain.Mountain), pen.Terrains);
        Fit fit = Fits.TryGetValue(faction, out Fit known) ? known : new Fit(0, 0);
        svg.Save();
        svg.Clip(SvgPath.Circle(0, 0, 1 - pen.Lw * 1.4f)); // inside the outline
        svg.Translate(0, -fit.Lift);
        svg.Scale(2 / ArtSize, 2 / ArtSize);
        svg.Translate(-ArtSize / 2, -ArtSize / 2);
        svg.Raw($"<g fill=\"none\">{Pictures[faction]}</g>"); // fill none: as on the art's own <svg> element
        svg.Restore();
        svg.Restore();
        if (marks && Marks.TryGetValue(faction, out string[] mark))
            DrawMark(svg, mark, r, fit.MarkY);
        svg.Restore();
        return svg.Text();
    }

    // ---------- The marks: what a landscape needs ----------

    private static readonly Color MarkColour = HexArt.TerrainColour(Terrain.Ice);
    private static readonly Color Out = new("1b1b1b");

    // TE temple, TP trading house, SA sanctuary, SH stronghold; spade, priest, cult3 (the any-cult symbol with a 3).
    private static readonly Dictionary<Faction, string[]> Marks = new()
    {
        [Faction.Witches] = new[] { "TE", "TE" },
        [Faction.Alchemists] = new[] { "SH" },
        [Faction.Darklings] = new[] { "SH" },
        [Faction.Halflings] = new[] { "SH" },
        [Faction.Mermaids] = new[] { "spade" },
        [Faction.Swarmlings] = new[] { "SA" },
        [Faction.ChaosMagicians] = new[] { "SH" },
        [Faction.Giants] = new[] { "TE" },
        [Faction.Nomads] = new[] { "TP" },
        [Faction.Acolytes] = new[] { "cult3" },
        [Faction.Dragonlords] = new[] { "SA" },
        [Faction.Riverwalkers] = new[] { "priest" },
    };

    private static readonly Dictionary<string, float> MarkSize = new()
    {
        ["TE"] = 0.3f, ["TP"] = 0.38f, ["SA"] = 0.42f, ["SH"] = 0.4f, ["spade"] = 0.4f, ["priest"] = 0.38f, ["cult3"] = 0.32f,
    };

    // Per marked token: how far the picture is raised (units of r) to clear the mark (measured from the art: its lowest
    // point over the mark, 0.04 r above the mark's top), and the mark's centre below the token's centre. Swarmlings and
    // Nomads are not raised (a little overlap is fine there).
    private readonly record struct Fit(float Lift, float MarkY);

    private static readonly Dictionary<Faction, Fit> Fits = new()
    {
        [Faction.Witches] = new(0, 0.7f),
        [Faction.Alchemists] = new(0.037f, 0.64f),
        [Faction.Darklings] = new(0, 0.64f),
        [Faction.Halflings] = new(0, 0.64f),
        [Faction.Mermaids] = new(0.203f, 0.68f),
        [Faction.Swarmlings] = new(0, 0.63f),
        [Faction.ChaosMagicians] = new(0.095f, 0.64f),
        [Faction.Giants] = new(0, 0.72f),
        [Faction.Nomads] = new(0, 0.65f),
        [Faction.Acolytes] = new(0, 0.69f),
        [Faction.Dragonlords] = new(0.122f, 0.63f),
        [Faction.Riverwalkers] = new(0, 0.67f),
    };

    // In pixels, centred under the token's middle, `y` radii down.
    private static void DrawMark(Svg svg, string[] items, float r, float y)
    {
        float[] sizes = items.Select(k => MarkSize[k] * r).ToArray();
        float gap = 0.03f * r, x = -(sizes.Sum() + gap * (items.Length - 1)) / 2;
        for (var i = 0; i < items.Length; i++)
        {
            float s = sizes[i];
            svg.Save();
            svg.Translate(x + s / 2, y * r);
            switch (items[i])
            {
                case "TE": IconArt.Solid(svg, PieceArt.PictureFor(PieceKind.Temple), s, MarkColour, shadow: false); break;
                case "TP": IconArt.Solid(svg, PieceArt.PictureFor(PieceKind.TradingPost), s, MarkColour, shadow: false); break;
                case "SA": IconArt.Solid(svg, PieceArt.PictureFor(PieceKind.Sanctuary), s, MarkColour, shadow: false); break;
                case "SH": IconArt.Solid(svg, PieceArt.PictureFor(PieceKind.Stronghold), s, MarkColour, shadow: false); break;
                case "spade": MarkSpade(svg, s); break;
                case "priest": IconArt.Priest(svg, s, MarkColour, shadow: false); break;
                case "cult3": MarkCult(svg, s); break;
            }
            svg.Restore();
            x += s + gap;
        }
    }

    // A spade, all ice white: the handle up and to the left.
    private static void MarkSpade(Svg svg, float s)
    {
        svg.Save();
        svg.Rotate(-Mathf.Pi / 4);
        svg.Translate(0, s * 0.06f);
        float w = Mathf.Max(1, s * 0.05f), top = -s * 0.6f;
        SvgPath grip = new SvgPath().MoveTo(-s * 0.09f, top).LineTo(s * 0.09f, top).MoveTo(0, top).LineTo(0, s * 0.02f);
        svg.Stroke(grip, Out, s * 0.08f + 2 * w, "round", "round");
        svg.Stroke(grip, MarkColour, s * 0.08f, "round", "round");
        float hw = s * 0.2f;
        SvgPath blade = new SvgPath().MoveTo(-hw, s * 0.02f).LineTo(hw, s * 0.02f)
            .CubicTo(hw * 1.07f, s * 0.24f, hw * 0.64f, s * 0.38f, 0, s * 0.48f).CubicTo(-hw * 0.64f, s * 0.38f, -hw * 1.07f, s * 0.24f, -hw, s * 0.02f).Close();
        svg.Fill(blade, MarkColour);
        svg.Stroke(blade, Out, w);
        svg.Restore();
    }

    // The any-cult symbol (four flat quarters in the cult colours) with a black 3 edged in white.
    private static void MarkCult(Svg svg, float s)
    {
        float r = s * 0.5f, w = Mathf.Max(1, s * 0.06f);
        foreach (var (cult, from, to) in new[] { ("e8772a", Mathf.Pi, 1.5f * Mathf.Pi), ("c8d5e1", 1.5f * Mathf.Pi, Mathf.Tau), ("8c6436", 0f, 0.5f * Mathf.Pi), ("3d8ad6", 0.5f * Mathf.Pi, Mathf.Pi) })
            svg.Fill(new SvgPath().MoveTo(0, 0).Arc(0, 0, r, from, to).Close(), new Color(cult));
        svg.Stroke(SvgPath.Circle(0, 0, r), Out, w);
        SvgPath three = new SvgPath().MoveTo(-0.15f * s, -0.19f * s)
            .CubicTo(-0.1f * s, -0.31f * s, 0.16f * s, -0.31f * s, 0.14f * s, -0.16f * s).CubicTo(0.13f * s, -0.07f * s, 0.05f * s, -0.04f * s, -0.01f * s, -0.035f * s)
            .CubicTo(0.08f * s, -0.035f * s, 0.17f * s, 0.02f * s, 0.16f * s, 0.11f * s).CubicTo(0.15f * s, 0.28f * s, -0.1f * s, 0.3f * s, -0.17f * s, 0.17f * s);
        float t = s * 0.1f;
        svg.Stroke(three, MarkColour, t + 2 * w, "round", "round");
        svg.Stroke(three, Out, t, "round", "round");
    }

    // ---------- The disc, in units of the token's radius ----------

    private sealed class Pen
    {
        public Svg Svg { get; }
        public float Lw { get; } // the outline round every shape: 0.035 of the radius, at least a pixel

        public Pen(Svg svg, float radius)
        {
            Svg = svg;
            Lw = Mathf.Max(1f / radius, 0.035f);
        }

        // The disc: flat in the hex's colour (or `ground` drawn over it), a thin pale bevel, the outline.
        public void Disc(Color colour, Action ground)
        {
            SvgPath disc = SvgPath.Circle(0, 0, 1);
            Svg.Fill(disc, colour);
            if (ground is not null)
            {
                Svg.Save();
                Svg.Clip(disc);
                ground();
                Svg.Restore();
            }
            Svg.Stroke(SvgPath.Circle(0, 0, 0.93f), new Color(1, 1, 1, 0.22f), 0.05f);
            Svg.Stroke(SvgPath.Circle(0, 0, 1 - Lw * 0.7f), Out, Lw * 1.4f);
        }

        // The Shapeshifters' ground: the seven base home terrains in equal wedges, clockwise in the order of the
        // terrain cycle (plains, swamp, lakes, forest, mountains, wasteland, desert), swamp at the top left as in the art.
        public void Terrains()
        {
            Terrain[] order = { Terrain.Plains, Terrain.Swamp, Terrain.Lake, Terrain.Forest, Terrain.Mountain, Terrain.Wasteland, Terrain.Desert };
            float step = Mathf.Tau / order.Length, start = Mathf.DegToRad(247f) - 1.5f * step;
            for (var i = 0; i < order.Length; i++)
                Svg.Fill(new SvgPath().MoveTo(0, 0).Arc(0, 0, 1.1f, start + i * step, start + (i + 1) * step + 0.01f).Close(), HexArt.TerrainColour(order[i]));
        }
    }
}
