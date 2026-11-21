using System.Linq;
using System.Runtime.CompilerServices;
using Godot;

namespace TMCalc;

// Lines nodes up left to right (or top to bottom), each `gap` pixels after the one before (in the parent's
// pixels; negative overlaps them), measuring each by what it visibly draws, turned and scaled as the node is: a
// ResourceBundle by its icons; a Sprite2D by the painted part of its texture (icons leave a transparent margin
// round their drawing, which is not counted); anything else (e.g. an ActionToken) by its child sprite named
// Octagon or Background. Hidden nodes are skipped. Only the position along the line changes; the first node
// stays where it is.
public static class Arrangement
{
    public static void InARow(float gap, params Node2D[] nodes) => InALine(gap, vertical: false, nodes);

    public static void InALine(float gap, bool vertical, params Node2D[] nodes)
    {
        float? next = null;
        foreach (Node2D node in nodes)
        {
            if (node is null || !node.Visible)
                continue;
            Rect2 drawn = Extent(node); // relative to the node's position, in the parent's pixels
            float start = vertical ? drawn.Position.Y : drawn.Position.X, end = vertical ? drawn.End.Y : drawn.End.X;
            if (next is float at)
                node.Position = vertical ? new Vector2(node.Position.X, at - start) : new Vector2(at - start, node.Position.Y);
            next = (vertical ? node.Position.Y : node.Position.X) + end + gap;
        }
    }

    // The painted part of a sprite, in its own pixels (as Sprite2D.GetRect, without the transparent margin).
    // Worked out once per texture; textures that aren't images (drawn pieces, bridges) count whole.
    public static Rect2 VisibleRect(Sprite2D sprite)
    {
        if (sprite.Texture is null)
            return new Rect2(-20, -20, 40, 40);
        Rect2 whole = sprite.GetRect();
        if (sprite.Texture is not ImageTexture texture)
            return whole;
        Rect2 used = Painted.GetValue(texture, PaintedPart).Value; // as fractions of the texture
        return new Rect2(whole.Position + used.Position * whole.Size, used.Size * whole.Size);
    }

    private static readonly ConditionalWeakTable<Texture2D, StrongBox<Rect2>> Painted = new();

    private static StrongBox<Rect2> PaintedPart(Texture2D texture)
    {
        Image image = texture.GetImage();
        if (image is null || image.IsEmpty())
            return new StrongBox<Rect2>(new Rect2(0, 0, 1, 1));
        Rect2I used = image.GetUsedRect();
        if (used.Size.X == 0 || used.Size.Y == 0)
            return new StrongBox<Rect2>(new Rect2(0, 0, 1, 1));
        Vector2 size = image.GetSize();
        return new StrongBox<Rect2>(new Rect2(used.Position / size, used.Size / size));
    }

    // A Label of this name under a Node2D counts as what that node draws (its rectangle), e.g. "1x" in a row of icons.
    public const string MeasuredText = "MeasuredText";

    // What the node draws, round its origin, in its parent's pixels (turned and scaled as the node is).
    // Empty if it draws nothing it can measure.
    public static Rect2 Extent(Node2D node)
    {
        Rect2 local; // in the node's own pixels
        switch (node)
        {
            case ResourceBundle bundle:
                if (bundle.IsEmpty)
                    return new Rect2();
                local = new Rect2(bundle.Left, bundle.Top, bundle.Right - bundle.Left, bundle.Bottom - bundle.Top);
                break;
            case Sprite2D { Texture: not null } sprite:
                local = VisibleRect(sprite);
                break;
            case not null when node.GetNodeOrNull<Label>(MeasuredText) is Label text:
                local = new Rect2(text.Position, text.Size);
                break;
            default:
                Sprite2D frame = node.GetNodeOrNull<Sprite2D>("Octagon") ?? node.GetNodeOrNull<Sprite2D>("Background");
                if (frame?.Texture is not null)
                {
                    local = Bounds(frame.Transform, VisibleRect(frame));
                    break;
                }
                // A group of parts (e.g. a TriggerView): everything its visible parts draw.
                Rect2? all = null;
                foreach (Node2D part in node.GetChildren().OfType<Node2D>().Where(part => part.Visible))
                {
                    Rect2 drawn = Extent(part);
                    if (drawn.Size == Vector2.Zero)
                        continue;
                    drawn.Position += part.Position;
                    all = all?.Merge(drawn) ?? drawn;
                }
                if (all is not Rect2 parts)
                    return new Rect2();
                local = parts;
                break;
        }
        Transform2D turnAndScale = node.Transform with { Origin = Vector2.Zero };
        return Bounds(turnAndScale, local);
    }

    // The smallest upright rectangle holding `rect` after `transform`.
    private static Rect2 Bounds(Transform2D transform, Rect2 rect)
    {
        Vector2[] corners = { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y) };
        var bounds = new Rect2(transform * corners[0], Vector2.Zero);
        foreach (Vector2 corner in corners)
            bounds = bounds.Expand(transform * corner);
        return bounds;
    }
}
