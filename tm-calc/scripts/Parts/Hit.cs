using Godot;

namespace TMCalc;

// Hit tests on things drawn by several sprites (tiles, conversions, bundles, action tokens).
public static class Hit
{
    // Is the point over any visible picture in the node or under it (the icons bundles make in code
    // included)?
    public static bool IsOver(Node node, Vector2 globalPoint)
    {
        if (node is Sprite2D sprite && IsPicture(sprite) && sprite.GetRect().HasPoint(sprite.ToLocal(globalPoint)))
            return true;
        foreach (Node child in node.GetChildren(includeInternal: true))
            if (child is not CanvasItem { Visible: false } && IsOver(child, globalPoint))
                return true;
        return false;
    }

    private static bool IsPicture(Sprite2D sprite) =>
        sprite.Texture is not null && sprite.IsVisibleInTree();
}
