using Godot;

namespace TMCalc;

// A board area that is normally shown smaller and grows to full size when it is needed (e.g. the priest
// zones while a priest is being placed). It shrinks and grows about one fixed point, the child Marker2D
// named "Corner" (e.g. the area's top centre), so that point never moves. Code can move it (Reshape).
// The node's position and scale as set in the editor are its full size.
public partial class ZoomableZone : Node2D
{
    // How big the area is normally, as a fraction of its full size.
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float SmallScale { get; set; } = 0.5f;
    // How long growing or shrinking takes.
    [Export] public float ZoomSeconds { get; set; } = 0.2f;
    // Its ZIndex while growing, full size and shrinking, so it lies over its neighbours (e.g. the priest zones over the
    // pass tiles beside them); back to the editor's value once it is small again.
    [Export] public int ExpandedZIndex { get; set; } = 20;

    public bool IsExpanded { get; private set; } = true;
    // True while growing or shrinking: anything drawn on the area must follow it every frame.
    public bool IsAnimating => _tween is not null && _tween.IsRunning();

    private Vector2 _fullPosition;
    private Vector2 _fullScale;
    private int _restingZIndex;
    private Vector2 _corner; // in this node's own pixels
    private Tween _tween;

    public override void _Ready()
    {
        _fullPosition = Position;
        _fullScale = Scale;
        _restingZIndex = ZIndex;
        var corner = GetNodeOrNull<Node2D>("Corner");
        if (corner is null)
            GD.PushWarning($"{GetPath()} has no Corner marker, so it shrinks towards its own origin.");
        _corner = corner?.Position ?? Vector2.Zero;
        Shrink(animate: false);
    }

    // Changes the fixed corner (in this node's own pixels) and how small the area normally is, then shows it small
    // at once. For code that lays the area out (e.g. so it lines up with something when small).
    public void Reshape(Vector2 corner, float smallScale)
    {
        _corner = corner;
        if (GetNodeOrNull<Node2D>("Corner") is Node2D marker)
            marker.Position = corner;
        SmallScale = smallScale;
        Shrink(animate: false);
    }

    // Its size and place as set in the editor (full size).
    public Vector2 FullPosition => _fullPosition;
    public Vector2 FullScale => _fullScale;

    public void Expand(bool animate = true) => ZoomTo(1f, expanded: true, animate);
    public void Shrink(bool animate = true) => ZoomTo(SmallScale, expanded: false, animate);

    // The corner stays where it is at full size: position + scale * corner is the same for every scale.
    private void ZoomTo(float factor, bool expanded, bool animate)
    {
        if (IsExpanded == expanded && !(_tween?.IsRunning() ?? false) && Scale == _fullScale * factor)
            return;
        IsExpanded = expanded;
        Vector2 scale = _fullScale * factor;
        Vector2 position = _fullPosition + (_fullScale - scale) * _corner;

        _tween?.Kill();
        _tween = null;
        if (expanded)
            ZIndex = ExpandedZIndex; // over its neighbours from the start of growing
        if (!animate || ZoomSeconds <= 0)
        {
            Scale = scale;
            Position = position;
            if (!expanded)
                ZIndex = _restingZIndex;
            return;
        }
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "scale", scale, ZoomSeconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(this, "position", position, ZoomSeconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (!expanded)
            _tween.Chain().TweenCallback(Callable.From(() => ZIndex = _restingZIndex)); // only once small again
    }

    // Is the point over any picture in the area (its zones, slots, ...)?
    public bool IsOver(Vector2 globalPoint) => IsOverAny(this, globalPoint);

    private static bool IsOverAny(Node node, Vector2 point)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Sprite2D { Visible: true } sprite && sprite.Texture is not null
                && sprite.GetRect().HasPoint(sprite.ToLocal(point)))
                return true;
            if (IsOverAny(child, point))
                return true;
        }
        return false;
    }
}
