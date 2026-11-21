using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// A texture made of flat shapes (filled polygons and lines) instead of pixels, like DrawnPieceTexture: it goes
// anywhere a PNG did, stays sharp at any scale, and Modulate tints and fades it as usual. Used for the map's
// hexes and for bridges (HexArt). [Tool]: bridge icons are drawn in the editor too.
[Tool]
public partial class VectorTexture : Texture2D
{
    // Width 0: a filled polygon (with a thin smoothed edge in its own colour, so it isn't jagged).
    // Width above 0: a line through the points (Closed: back to the first).
    public readonly record struct Shape(Vector2[] Points, Color Colour, float Width = 0f, bool Closed = false);

    private Vector2 _size;
    private Shape[] _shapes = System.Array.Empty<Shape>();

    internal void Show(Vector2 size, IEnumerable<Shape> shapes)
    {
        _size = size;
        _shapes = shapes.Where(shape => shape.Points.Length >= 2).ToArray();
    }

    public override int _GetWidth() => (int)_size.X;
    public override int _GetHeight() => (int)_size.Y;
    public override bool _HasAlpha() => true;
    public override bool _IsPixelOpaque(int x, int y) => true;

    public override void _Draw(Rid toCanvasItem, Vector2 pos, Color modulate, bool transpose) =>
        Paint(toCanvasItem, new Rect2(pos, _size), new Rect2(Vector2.Zero, _size), modulate);

    public override void _DrawRect(Rid toCanvasItem, Rect2 rect, bool tile, Color modulate, bool transpose) =>
        Paint(toCanvasItem, rect, new Rect2(Vector2.Zero, _size), modulate);

    public override void _DrawRectRegion(Rid toCanvasItem, Rect2 rect, Rect2 srcRect, Color modulate, bool transpose, bool clipUV) =>
        Paint(toCanvasItem, rect, srcRect, modulate);

    private void Paint(Rid item, Rect2 target, Rect2 source, Color modulate)
    {
        if (source.Size.X <= 0 || source.Size.Y <= 0)
            return;
        Vector2 scale = target.Size / source.Size;
        Vector2[] Place(Vector2[] points) => points.Select(p => target.Position + (p - source.Position) * scale).ToArray();
        foreach (Shape shape in _shapes)
        {
            Vector2[] points = Place(shape.Points);
            Color colour = shape.Colour * modulate;
            if (shape.Width <= 0f)
            {
                if (Geometry2D.TriangulatePolygon(points).Length == 0)
                    continue; // too thin to fill
                RenderingServer.CanvasItemAddPolygon(item, points, new[] { colour });
                RenderingServer.CanvasItemAddPolyline(item, points.Append(points[0]).ToArray(), new[] { colour }, scale.X, true);
            }
            else
            {
                Vector2[] line = shape.Closed ? points.Append(points[0]).ToArray() : points;
                RenderingServer.CanvasItemAddPolyline(item, line, new[] { colour }, shape.Width * scale.X, true);
            }
        }
    }
}
