using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// An edge highlight: the power offer edge (MapGlow) without its sparkles, kept inside
// what it goes round. Just inside each outline: a wide soft glow and a solid line in the highlight's colour, a thin
// white core, and (if wanted) soft white glints running round it: `glints` of them on each outline, evenly spaced
// (0: none). The glow is cut off at the outline, so nothing
// spills onto the background or the pieces next to it.
//   EdgeHighlight.Set(tileView, on, HighlightArt.Colour.Yellow)        a tile view with a Background BlankTileSprite
//   EdgeHighlight.SetOnCultTrack(trackSprite, on)                      a cult track (yellow, over its purple band too)
//   EdgeHighlight.SetOnArea(node, on, rect, radius)                    a rectangle, e.g. part of a player panel
//   MapView.ShowHexHighlight(hex, on)                                  a map hex
// Each is a child named EdgeHighlight, made the first time it is shown and hidden afterwards.
// How the cut-off works: this node fills the area inside the outlines, and ClipChildren makes that fill a mask
// for its children (the lines and the glints) instead of something shown.
public partial class EdgeHighlight : Node2D
{
    public const string NodeName = "EdgeHighlight";

    private Vector2[][] _mask = Array.Empty<Vector2[]>();
    private readonly List<Vector2[]> _paths = new(); // the outlines moved in to the solid line's centre
    private float _scale = 1f;
    private int _glints;
    private (HighlightArt.Colour, int, Rect2, float) _shown; // what it was last shown as, so it isn't rebuilt every render
    private Node2D _lines;
    private Painter _glintPainter;

    // outlines: closed, clockwise on screen, in this node's pixels. scale: widths are HighlightArt's times this
    // (the picture's size compared with its natural size). mask: the area everything stays inside, if not just
    // inside the outlines (e.g. with parts kept clear, or for hexes, each hex on its own).
    public void Show(IEnumerable<Vector2[]> outlines, HighlightArt.Colour colour, int glints, float scale = 1f,
        IEnumerable<Vector2[]> mask = null)
    {
        Vector2[][] loops = outlines.ToArray();
        _mask = (mask ?? loops).ToArray();
        _scale = scale;
        _glints = glints;
        _shown = (colour, glints, default, 0);
        ClipChildren = ClipChildrenMode.Only;
        if (_lines is null)
        {
            _lines = new Node2D { Name = "Lines" };
            _glintPainter = new Painter { Name = "Glints", Paint = PaintGlints };
            AddChild(_lines);
            AddChild(_glintPainter);
        }
        foreach (Node old in _lines.GetChildren())
            old.QueueFree();

        // The solid line's centre runs half its width inside the outline; the glow is centred on it too, so the
        // mask cuts off its outer part.
        Color c = HighlightArt.ColourOf(colour);
        _paths.Clear();
        foreach (Vector2[] loop in loops)
            _paths.AddRange(Geometry2D.OffsetPolygon(loop, -HighlightArt.Line * scale / 2, Geometry2D.PolyJoinType.Round));
        foreach (Vector2[] path in _paths)
        {
            AddLine(path, HighlightArt.Glow * scale, new Color(c, 0.38f));
            AddLine(path, HighlightArt.Line * scale, new Color(c, 0.9f));
            AddLine(path, Mathf.Max(1.5f, HighlightArt.Core * scale), Colors.White);
        }
        SetProcess(glints > 0);
        _glintPainter.QueueRedraw();
        QueueRedraw();
    }

    // A mask polygon Godot can't triangulate (fewer than 3 points, zero area, or crossing itself: e.g. a rounded
    // rectangle whose corner radius is more than half its size) is skipped rather than drawn, which had raised
    // "Invalid polygon data, triangulation failed"; the first one on each highlight is reported, naming its parent.
    private bool _reported;

    public override void _Draw()
    {
        foreach (Vector2[] area in _mask)
        {
            if (area.Length >= 3 && Geometry2D.TriangulatePolygon(area).Length > 0)
            {
                DrawColoredPolygon(area, Colors.White);
                continue;
            }
            if (!_reported)
                GD.PushWarning($"EdgeHighlight on {GetParent()?.GetPath()}: a mask of {area.Length} points could not be triangulated; skipped.");
            _reported = true;
        }
    }

    // Godot turns processing on for any node with _Process, so one made but not yet shown (e.g. the colour wheel's)
    // would run it with nothing to redraw: off until Show says there are glints.
    public override void _Ready() => SetProcess(_glints > 0 && _glintPainter is not null);

    public override void _Process(double delta) => _glintPainter?.QueueRedraw();

    // Already showing this colour, with this many glints?
    internal bool IsShown(HighlightArt.Colour colour, int glints) => _lines is not null && _shown.Item1 == colour && _shown.Item2 == glints;

    // ---------- Showing on things ----------

    // A tile view (round, pass, key token, power action or favour) with a Background BlankTileSprite: round that
    // tile's edge, just above the background, so the tile's own icons stay on top.
    public static void Set(Node2D view, bool on, HighlightArt.Colour colour, int glints = 0)
    {
        if (view.GetNodeOrNull<BlankTileSprite>("Background") is not BlankTileSprite background)
            return;
        EdgeHighlight highlight = Find(view, on, background.GetIndex() + 1);
        if (highlight is null || highlight.IsShown(colour, glints))
            return;
        highlight.Position = background.Position + background.Offset;
        highlight.Scale = background.Scale;
        highlight.Show(new[] { HighlightArt.BlankTile(background.Tile, background.TileScale) }, colour, glints, background.TileScale);
    }

    // A cult track sprite (CultArt.TrackTexture, centred): yellow, inside the track, over the purple band below step 10
    // too (kept clear of it, it looked as if it ran under it).
    public static void SetOnCultTrack(Node2D track, bool on, int glints = 0)
    {
        EdgeHighlight highlight = Find(track, on, track.GetChildCount());
        if (highlight is null || highlight.IsShown(HighlightArt.Colour.Yellow, glints))
            return;
        highlight.Show(new[] { HighlightArt.CultTrack() }, HighlightArt.Colour.Yellow, glints);
    }

    // A rectangle (with rounded corners) in `parent`'s pixels, white unless told otherwise. Shown again with a
    // different rectangle, it moves and resizes. scale: the widths, as for Show (small areas want thinner lines).
    public static void SetOnArea(Node2D parent, bool on, Rect2 rect, float radius = 8f, int glints = 0,
        HighlightArt.Colour colour = HighlightArt.Colour.White, float scale = 1f)
    {
        EdgeHighlight highlight = Find(parent, on, parent.GetChildCount());
        if (highlight is null || highlight.IsShown(colour, glints) && highlight._shown.Item3 == rect && highlight._shown.Item4 == radius
            && highlight._scale == scale)
            return;
        highlight.Show(new[] { HighlightArt.RoundRect(rect, radius) }, colour, glints, scale);
        highlight._shown = (colour, glints, rect, radius);
    }

    // The highlight under `parent` (made at `index` among its children if there isn't one and it is wanted),
    // shown or hidden; null when hidden.
    internal static EdgeHighlight Find(Node2D parent, bool on, int index)
    {
        var highlight = parent.GetNodeOrNull<EdgeHighlight>(NodeName);
        if (!on)
        {
            if (highlight is not null)
            {
                highlight.Visible = false;
                highlight.SetProcess(false);
            }
            return null;
        }
        if (highlight is null)
        {
            highlight = new EdgeHighlight { Name = NodeName };
            parent.AddChild(highlight);
            parent.MoveChild(highlight, Mathf.Min(index, parent.GetChildCount() - 1));
        }
        highlight.Visible = true;
        highlight.SetProcess(highlight._glints > 0);
        return highlight;
    }

    // ---------- Drawing ----------

    private void AddLine(Vector2[] points, float width, Color colour) => _lines.AddChild(new Line2D
    {
        Points = points,
        Closed = true,
        Width = width,
        DefaultColor = colour,
        JointMode = Line2D.LineJointMode.Round,
        Antialiased = true,
    });

    // Glints: `_glints` evenly spaced round each path, all moving at GlintSpeed.
    private void PaintGlints(Painter g)
    {
        if (_glints <= 0)
            return;
        float now = Time.GetTicksMsec() / 1000f, radius = HighlightArt.GlintRadius * _scale;
        foreach (Vector2[] path in _paths)
        {
            var lengths = new float[path.Length + 1];
            for (var i = 0; i < path.Length; i++)
                lengths[i + 1] = lengths[i] + path[i].DistanceTo(path[(i + 1) % path.Length]);
            float perimeter = lengths[^1];
            if (perimeter <= 0)
                continue;
            int count = _glints;
            for (var k = 0; k < count; k++)
            {
                float s = Mathf.PosMod(now * HighlightArt.GlintSpeed * _scale + k * perimeter / count, perimeter);
                int i = 0;
                while (i < path.Length - 1 && lengths[i + 1] < s)
                    i++;
                float u = (s - lengths[i]) / Mathf.Max(lengths[i + 1] - lengths[i], 1e-3f);
                MapGlow.Glint(g, path[i].Lerp(path[(i + 1) % path.Length], u), radius, 0.9f);
            }
        }
    }
}
