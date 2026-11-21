using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// Like a VBoxContainer for Node2D parts (containers only arrange Controls): spreads its visible children down
// a column Height tall, centred on this node, with equal space above, between and below them (measured by what
// each draws; see Arrangement.Extent). One visible child sits in the middle; two divide the column in thirds of
// free space; and so on. Only their heights change; place them across (e.g. X = 0) in the editor.
// A child named Rule (e.g. an IconSprite of a pass tile's rule) is not one of them: it is shown only between
// the first two, halfway, and hidden when fewer than two are shown.
// It spaces them out again when a child is shown or hidden, and when Arrange() is called (after their contents
// change size).
[Tool]
[GlobalClass]
public partial class SpreadColumn : Node2D
{
    [Export] public float Height { get => _height; set { _height = value; Arrange(); } }
    private float _height = 300f;

    private const string RuleName = "Rule";

    public override void _Ready()
    {
        ChildEnteredTree += OnChildEntered;
        foreach (Node child in GetChildren())
            OnChildEntered(child);
        Arrange();
    }

    private readonly HashSet<ulong> _watched = new(); // children whose showing and hiding is already followed

    private void OnChildEntered(Node child)
    {
        if (child is CanvasItem item && child.Name != RuleName && _watched.Add(child.GetInstanceId()))
            item.VisibilityChanged += ArrangeLater;
    }

    // Several parts may change visibility one after another: space them out once, at the end of the frame.
    private void ArrangeLater() => CallDeferred(MethodName.Arrange);

    public void Arrange()
    {
        if (!IsInsideTree())
            return;
        Node2D rule = GetNodeOrNull<Node2D>(RuleName);
        var parts = GetChildren().OfType<Node2D>().Where(part => part != rule && part.Visible)
            .Select(part => (Part: part, Drawn: Arrangement.Extent(part))).ToList();

        float free = Height - parts.Sum(p => p.Drawn.Size.Y), gap = free / (parts.Count + 1);
        float top = -Height / 2 + gap;
        var middles = new List<float>(); // where each part's drawing ends up, top and bottom
        foreach (var (part, drawn) in parts)
        {
            part.Position = new Vector2(part.Position.X, top - drawn.Position.Y);
            middles.Add(top);
            middles.Add(top + drawn.Size.Y);
            top += drawn.Size.Y + gap;
        }

        if (rule is null)
            return;
        rule.Visible = parts.Count >= 2;
        if (rule.Visible) // halfway between the bottom of the first and the top of the second
            rule.Position = new Vector2(rule.Position.X, (middles[1] + middles[2]) / 2);
    }

    // Every SpreadColumn under a node.
    public static IEnumerable<SpreadColumn> Within(Node node) =>
        node.GetChildren().SelectMany(child => (child is SpreadColumn column ? new[] { column } : Enumerable.Empty<SpreadColumn>()).Concat(Within(child)));
}
