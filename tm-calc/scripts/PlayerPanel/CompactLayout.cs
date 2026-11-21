using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// The compact layout of a player panel, shown when it is not that player's turn.
// Attach to a Node2D child of the panel (any name). Inside it:
//   - a Marker2D named after one of the panel's parts (any other child of the panel, e.g. Portrait,
//     Resources, Favours) gives that part's position and scale in the compact layout;
//   - a part with no marker fades out in the compact layout, unless it is listed in AlwaysShown;
//   - everything else in Compact (a smaller background, the building row) belongs to the compact
//     layout only, and fades in with it.
// Tick Preview in the editor to see the compact layout, then move the markers: the parts follow them.
// While previewing, move only things inside Compact (the parts themselves snap back to their markers).
// The scene is always saved in its full layout.
[Tool]
[GlobalClass]
public partial class CompactLayout : Node2D
{
    // Parts that stay visible in the compact layout even without a marker (in their full-layout place).
    [Export] public string[] AlwaysShown { get; set; } = { "PlayerButton", "BridgeSupply" };

    // Editor only: shows the compact layout while ticked.
    [Export] public bool Preview { get; set; }

    // The layout shown, or being changed to.
    public bool IsCompact { get; private set; }

    private readonly record struct Pose(Vector2 Position, Vector2 Scale, float Alpha);

    // Each part's full-layout pose, as authored.
    private readonly Dictionary<CanvasItem, Pose> _full = new();
    private readonly Dictionary<CanvasItem, Tween> _tweens = new();
    private bool _applied;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return; // the preview runs in _Process
        SetProcess(false);
        foreach (CanvasItem part in Parts())
            _full[part] = PoseOf(part);
        Visible = true; // even if hidden in the editor while working on the full layout
        Modulate = Modulate with { A = 0f };
    }

    // Changes to the compact (true) or full layout, over `seconds` (0: at once).
    public void Apply(bool compact, float seconds)
    {
        if (_applied && compact == IsCompact)
            return;
        _applied = true;
        IsCompact = compact;
        foreach (var (part, full) in _full)
            if (IsInstanceValid(part))
                Animate(part, TargetPose(part, full, compact), seconds);
        Animate(this, new Pose(Position, Scale, compact ? 1f : 0f), seconds);
    }

    // Where a node inside the panel will be once the layout change under way has finished,
    // in the panel's own coordinates.
    public Transform2D FinalTransform(Node2D node) => LayoutTransform(node, IsCompact);

    // Where a node inside the panel is in the compact (true) or full layout, in the panel's own coordinates.
    public Transform2D LayoutTransform(Node2D node, bool compact)
    {
        var panel = GetParent<Node2D>();
        Transform2D now = panel.GlobalTransform.AffineInverse() * node.GlobalTransform;
        if (PartOf(node) is not Node2D part || !_full.TryGetValue(part, out Pose full))
            return now;
        Pose end = TargetPose(part, full, compact);
        var partEnd = new Transform2D(part.Rotation, end.Scale, part.Skew, end.Position);
        return partEnd * part.Transform.AffineInverse() * now;
    }

    // How much bigger or smaller than in the full layout things drawn at a node of the panel (pieces, tiles)
    // should be once the layout change under way has finished: its part's final scale over its full-layout
    // scale. Inside Compact itself, which has no full layout: the node's own scale relative to the panel.
    public Vector2 ScaleFactor(Node2D node)
    {
        CanvasItem part = PartOf(node);
        if (part == this)
            return FinalTransform(node).Scale;
        if (part is null || !_full.TryGetValue(part, out Pose full))
            return Vector2.One;
        return TargetPose(part, full, IsCompact).Scale / full.Scale;
    }

    // Whether a node inside the panel is visible in the layout shown (or being changed to).
    public bool IsShown(Node node)
    {
        CanvasItem part = PartOf(node);
        if (part is null)
            return true;
        if (part == this)
            return IsCompact;
        return !_full.TryGetValue(part, out Pose full) || TargetPose(part, full, IsCompact).Alpha > 0f;
    }

    private Pose TargetPose(CanvasItem part, Pose full, bool compact)
    {
        if (!compact)
            return full;
        string name = part.Name.ToString();
        if (GetNodeOrNull<Marker2D>(name) is Marker2D marker)
            return new Pose(Position + Scale * marker.Position, Scale * marker.Scale, full.Alpha);
        return AlwaysShown.Contains(name) ? full : full with { Alpha = 0f };
    }

    // The panel's parts: its other children.
    private IEnumerable<CanvasItem> Parts() =>
        GetParent()?.GetChildren().OfType<CanvasItem>().Where(part => part != this) ?? Enumerable.Empty<CanvasItem>();

    // The part a node belongs to (the panel's child it is in), or null if it is not in the panel.
    private CanvasItem PartOf(Node node)
    {
        Node panel = GetParent();
        while (node is not null && node.GetParent() != panel)
            node = node.GetParent();
        return node as CanvasItem;
    }

    private void Animate(CanvasItem item, Pose pose, float seconds)
    {
        if (_tweens.Remove(item, out Tween running))
            running.Kill();
        if (seconds <= 0f)
        {
            SetPose(item, pose);
            return;
        }

        Tween tween = item.CreateTween().SetParallel();
        if (item is Node2D or Control)
        {
            tween.TweenProperty(item, "position", pose.Position, seconds)
                 .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(item, "scale", pose.Scale, seconds)
                 .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        tween.TweenProperty(item, "modulate:a", pose.Alpha, seconds);
        _tweens[item] = tween;
    }

    private static Pose PoseOf(CanvasItem item) => item switch
    {
        Node2D node => new Pose(node.Position, node.Scale, item.Modulate.A),
        Control control => new Pose(control.Position, control.Scale, item.Modulate.A),
        _ => new Pose(Vector2.Zero, Vector2.One, item.Modulate.A),
    };

    private static void SetPose(CanvasItem item, Pose pose)
    {
        switch (item)
        {
            case Node2D node:
                node.Position = pose.Position;
                node.Scale = pose.Scale;
                break;
            case Control control:
                control.Position = pose.Position;
                control.Scale = pose.Scale;
                break;
        }
        item.Modulate = item.Modulate with { A = pose.Alpha };
    }

    // ---------- Editor preview ----------
    // While previewing, each part keeps its full-layout pose in its metadata, so the pose survives
    // script reloads. Unticking Preview, or saving, puts every part back and removes the metadata.

    private const string FullPoseMeta = "compact_preview_full_pose";

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
            return;
        foreach (CanvasItem part in Parts())
        {
            if (Preview)
            {
                if (!part.HasMeta(FullPoseMeta))
                    part.SetMeta(FullPoseMeta, Encode(PoseOf(part)));
                SetPose(part, TargetPose(part, Decode(part.GetMeta(FullPoseMeta)), compact: true));
            }
            else if (part.HasMeta(FullPoseMeta))
                Restore(part);
        }
    }

    // The scene is saved in its full layout; the preview comes back on the next frame.
    public override void _Notification(int what)
    {
        if (!Engine.IsEditorHint() || what != NotificationEditorPreSave)
            return;
        foreach (CanvasItem part in Parts())
            if (part.HasMeta(FullPoseMeta))
                Restore(part);
    }

    private static void Restore(CanvasItem part)
    {
        SetPose(part, Decode(part.GetMeta(FullPoseMeta)));
        part.RemoveMeta(FullPoseMeta);
    }

    private static Godot.Collections.Array Encode(Pose pose) => new() { pose.Position, pose.Scale, pose.Alpha };

    private static Pose Decode(Variant value)
    {
        Godot.Collections.Array array = value.AsGodotArray();
        return new Pose(array[0].AsVector2(), array[1].AsVector2(), array[2].AsSingle());
    }
}
