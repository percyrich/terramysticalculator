using Godot;

namespace TMCalc;

// One thing turned into another, e.g. "3 power → 1 priest": children named From (a ResourceBundle),
// Arrow (a Sprite2D) and To: a ResourceBundle, or something holding one named Content (e.g. an ActionToken,
// for a power action, so the octagon is drawn around what it gives). Arrow and To are placed after From
// automatically, Gap apart.
[Tool]
[GlobalClass]
public partial class ConversionView : Node2D
{
    // Editor previews, e.g. "5 power" and "1 priest" (see Amounts.Parse). Code sets the real ones with ShowConversion().
    [Export] public string FromText { get => _fromText; set { _fromText = value; Preview(); } }
    [Export] public string ToText { get => _toText; set { _toText = value; Preview(); } }
    // Between the drawings (not counting icons' transparent margins), in this node's pixels; negative overlaps.
    [Export] public float Gap { get => _gap; set { _gap = value; Arrange(); } }

    private string _fromText = "";
    private string _toText = "";
    private float _gap = 12f;

    // The node named To (a ResourceBundle, or e.g. an ActionToken holding one).
    public Node2D To => GetNodeOrNull<Node2D>("To");

    public override void _Ready() => Preview();

    public void ShowConversion(Amounts from, Amounts to)
    {
        var fromView = GetNodeOrNull<ResourceBundle>("From");
        Node2D toNode = To;
        ResourceBundle toView = toNode as ResourceBundle ?? toNode?.GetNodeOrNull<ResourceBundle>("Content");
        if (fromView is null || toView is null)
        {
            GD.PushWarning($"{GetPath()} needs a ResourceBundle named From, and To (a ResourceBundle, or holding one named Content).");
            return;
        }
        fromView.ShowAmounts(from);
        toView.ShowAmounts(to);
        Arrange();
    }

    // Lines the parts up again with what they show now (e.g. after Gap changes).
    private void Arrange()
    {
        if (IsInsideTree())
            Arrangement.InARow(Gap, GetNodeOrNull<Node2D>("From"), GetNodeOrNull<Node2D>("Arrow"), To);
    }

    private void Preview()
    {
        if (Engine.IsEditorHint() && IsInsideTree() && (FromText != "" || ToText != ""))
            ShowConversion(Amounts.Parse(FromText, out _), Amounts.Parse(ToText, out _));
    }
}
