using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;

namespace TMCalc;

// A texture that draws one building (from PieceArt) instead of holding pixels. Sprites size and place it like
// any texture (its size is the picture's, with the piece centred), and their Modulate (tints, fades) applies
// as usual. [Tool]: ResourceBundle draws building icons in the editor too. In its own file because Godot only
// treats a C# class as a script (and so calls its overrides) when the file has the class's name.
[Tool]
public partial class DrawnPieceTexture : Texture2D
{
    private PieceArt.Picture _picture;
    private Color _colour, _outline, _edge;

    internal void Show(PieceArt.Picture picture, Color colour, Color outline)
    {
        _picture = picture;
        _colour = colour;
        _outline = outline;
        _edge = outline.Lerp(colour, 0.7f); // the thin lines between faces: close to the piece's own colour
    }

    public override int _GetWidth() => (int)(_picture?.Size.X ?? 0);
    public override int _GetHeight() => (int)(_picture?.Size.Y ?? 0);
    public override bool _HasAlpha() => true;
    public override bool _IsPixelOpaque(int x, int y) => true; // nothing asks; hit tests use the rectangle

    // The three ways something can draw a texture: at a point, into a rectangle, or part of it into a rectangle.
    public override void _Draw(Rid toCanvasItem, Vector2 pos, Color modulate, bool transpose) =>
        Paint(toCanvasItem, new Rect2(pos, Whole.Size), Whole, modulate);

    public override void _DrawRect(Rid toCanvasItem, Rect2 rect, bool tile, Color modulate, bool transpose) =>
        Paint(toCanvasItem, rect, Whole, modulate);

    public override void _DrawRectRegion(Rid toCanvasItem, Rect2 rect, Rect2 srcRect, Color modulate, bool transpose, bool clipUV) =>
        Paint(toCanvasItem, rect, srcRect, modulate);

    private Rect2 Whole => new(Vector2.Zero, _picture?.Size ?? Vector2.Zero);

    // The grown silhouette of each picture (in texture pixels), worked out once. (Drawing a thick line round
    // every face instead left spikes at sharp corners and fringes along curved sides, where the line's
    // separate pieces met.)
    private static readonly ConditionalWeakTable<PieceArt.Picture, StrongBox<List<Vector2[]>>> Silhouette = new();

    private static StrongBox<List<Vector2[]>> Grown(PieceArt.Picture picture)
    {
        // Each face grown by the outline's width, then all of them joined into one shape (or several, if the
        // piece were ever in separate parts). Holes left inside are dropped: the faces cover them.
        var shapes = new List<Vector2[]>();
        foreach (var face in picture.Faces)
        {
            // All one way round, so growing never shrinks a face that happens to run the other way.
            Vector2[] points = Geometry2D.IsPolygonClockwise(face.Points) ? Enumerable.Reverse(face.Points).ToArray() : face.Points;
            foreach (Vector2[] grown in Geometry2D.OffsetPolygon(points, PieceArt.OutlineWidth, Geometry2D.PolyJoinType.Round))
                shapes = Join(shapes, grown);
        }
        return new StrongBox<List<Vector2[]>>(shapes);
    }

    // Adds a shape to a set of separate shapes, joining it with any it overlaps.
    private static List<Vector2[]> Join(List<Vector2[]> shapes, Vector2[] added)
    {
        var apart = new List<Vector2[]>();
        Vector2[] joined = added;
        foreach (Vector2[] shape in shapes)
        {
            Vector2[][] merged = Geometry2D.MergePolygons(shape, joined).ToArray();
            if (merged.Length == 0)
            {
                apart.Add(shape);
                continue;
            }
            // Overlapping: one outline (plus any holes, which run the other way round from it). Apart: both come
            // back as outlines. The biggest result is always an outline.
            bool outerWay = Geometry2D.IsPolygonClockwise(merged.OrderByDescending(Area).First());
            Vector2[][] outers = merged.Where(p => Geometry2D.IsPolygonClockwise(p) == outerWay).ToArray();
            if (outers.Length == 1)
                joined = outers[0];
            else
                apart.Add(shape);
        }
        apart.Add(joined);
        return apart;
    }

    private static float Area(Vector2[] p)
    {
        float twice = 0f;
        for (var i = 0; i < p.Length; i++)
            twice += p[i].Cross(p[(i + 1) % p.Length]);
        return Mathf.Abs(twice) / 2;
    }

    // Draws the picture's `source` part (in texture pixels) into `target` on the canvas item.
    private void Paint(Rid item, Rect2 target, Rect2 source, Color modulate)
    {
        if (_picture is null || source.Size.X <= 0 || source.Size.Y <= 0)
            return;
        Vector2 scale = target.Size / source.Size;
        float widths = scale.X;
        Vector2[] Place(Vector2[] points) => points.Select(p => target.Position + (p - source.Position) * scale).ToArray();
        Vector2[] Closed(Vector2[] points) => points.Append(points[0]).ToArray();

        // 1. The ground shadow.
        if (_picture.Shadow is not null)
            RenderingServer.CanvasItemAddPolygon(item, Place(_picture.Shadow), new[] { PieceArt.ShadowColour * modulate });

        // 2. The outline: the piece's silhouette grown by the outline's width, with round corners, filled in the
        // outline colour (and a hairline round it, so its edge is smooth). The faces drawn next cover all of it
        // except a band round the outside of the whole piece, the same width all the way round.
        Color outline = _outline * modulate;
        foreach (Vector2[] shape in Silhouette.GetValue(_picture, Grown).Value)
        {
            Vector2[] points = Place(shape);
            RenderingServer.CanvasItemAddPolygon(item, points, new[] { outline });
            RenderingServer.CanvasItemAddPolyline(item, Closed(points), new[] { outline }, 1f, true);
        }

        // 3. The faces, far to near: filled, softened round the edge, then their thin crease lines.
        Color edge = _edge * modulate;
        foreach (var face in _picture.Faces)
        {
            Vector2[] points = Place(face.Points);
            Color fill = PieceArt.FaceColour(_colour, face.Lit) * modulate;
            RenderingServer.CanvasItemAddPolygon(item, points, new[] { fill });
            RenderingServer.CanvasItemAddPolyline(item, Closed(points), new[] { fill }, PieceArt.SmoothWidth * widths, true);
            for (var i = 0; i < points.Length; i++)
                if (face.Hard[i])
                    RenderingServer.CanvasItemAddLine(item, points[i], points[(i + 1) % points.Length], edge, PieceArt.EdgeWidth * widths, true);
        }
    }
}
