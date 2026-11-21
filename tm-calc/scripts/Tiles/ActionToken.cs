using System.Linq;
using Godot;

namespace TMCalc;

// An action that can be taken once per round: an orange octagon around what it does, covered by an
// X token once used. Its children:
//   Octagon  the octagon (made in code if missing);
//   Icon     a Sprite2D picture of the action (optional; ShowIcon makes one if missing), and/or
//   Content  a ResourceBundle with what it gives (optional);
//   Used     the X token (made in code if missing), always drawn on top.
// While the mouse is over a used token, the X becomes see-through so the action can be read
// (Main does this for every ActionToken; see HoverUsed).
[GlobalClass]
public partial class ActionToken : Node2D
{
    // Every token is in this group, so hover effects reach them wherever they are.
    public const string Group = "action_tokens";
    public const string ScenePath = "res://scenes/action_token.tscn";

    public Sprite2D Octagon { get; private set; }
    public Sprite2D Used { get; private set; }
    public ResourceBundle Content => GetNodeOrNull<ResourceBundle>("Content");

    public bool IsUsed
    {
        get => Used?.Visible ?? false;
        set
        {
            EnsureParts();
            if (value && !Used.Visible)
                _opaqueUntilLeft = true; // just used (clicked): the X stays solid until the mouse leaves
            Used.Visible = value;
        }
    }
    private bool _opaqueUntilLeft;

    // A new token: from action_token.tscn if it exists (so its look is set in the editor), else made in code.
    public static ActionToken Create()
    {
        if (ResourceLoader.Exists(ScenePath) && GD.Load<PackedScene>(ScenePath).Instantiate() is ActionToken fromScene)
            return fromScene;
        return new ActionToken();
    }

    public override void _Ready()
    {
        AddToGroup(Group);
        EnsureParts();
    }

    public void ShowIcon(Texture2D texture)
    {
        EnsureParts();
        var icon = GetNodeOrNull<Sprite2D>("Icon");
        if (icon is null)
        {
            icon = new Sprite2D { Name = "Icon" };
            AddChild(icon);
            MoveChild(Used, -1);
        }
        icon.Texture = texture;
    }

    public bool IsOver(Vector2 globalPoint) =>
        IsVisibleInTree() && Octagon?.Texture is not null && Octagon.GetRect().HasPoint(Octagon.ToLocal(globalPoint));

    // The X goes see-through (opacity) while the point is over this token; null: nothing is hovered.
    public void HoverUsed(Vector2? globalPoint, float opacity)
    {
        if (Used is null)
            return;
        bool hovered = globalPoint is Vector2 point && IsUsed && IsVisibleInTree()
            && (IsOver(point) || (Used.Texture is not null && Used.GetRect().HasPoint(Used.ToLocal(point))));
        if (!hovered)
            _opaqueUntilLeft = false;
        Used.SelfModulate = new Color(1f, 1f, 1f, hovered && !_opaqueUntilLeft ? opacity : 1f);
    }

    private void EnsureParts()
    {
        Octagon ??= GetNodeOrNull<Sprite2D>("Octagon");
        if (Octagon is null)
        {
            Octagon = new Sprite2D { Name = "Octagon", Texture = Art.BonusOctagonTexture() };
            AddChild(Octagon);
            MoveChild(Octagon, 0);
        }
        Used ??= GetNodeOrNull<Sprite2D>("Used");
        if (Used is null)
        {
            Used = new Sprite2D { Name = "Used", Texture = Art.BonusUsedTexture(), Visible = false };
            AddChild(Used);
        }
        if (GetChildren().Last() != Used)
            MoveChild(Used, -1); // the X always covers everything else
    }
}
