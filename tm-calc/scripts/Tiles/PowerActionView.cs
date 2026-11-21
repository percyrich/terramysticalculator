using Core;
using Godot;

namespace TMCalc;

// A power action (power_action.tscn): a ConversionView from the power it costs to what it gives (the To part an
// ActionToken, crossed out once taken this round). Main shows each action with ShowAction; in the editor,
// PreviewAction shows one (0 to 5; -1 shows FromText / ToText instead).
[Tool]
[GlobalClass]
public partial class PowerActionView : ConversionView
{
    [Export(PropertyHint.Range, "-1,5,1")]
    public int PreviewAction { get => _previewAction; set { _previewAction = value; if (Engine.IsEditorHint() && IsInsideTree()) ShowAction(value); } }
    private int _previewAction = -1;

    public override void _Ready()
    {
        base._Ready();
        if (Engine.IsEditorHint())
            ShowAction(ActionFromName() is int named and >= 0 ? named : PreviewAction);
    }

    // In the editor: the action its name says, e.g. 3 for "Action3" (as Main uses them), else PreviewAction.
    private int ActionFromName()
    {
        string name = Name.ToString();
        return name.StartsWith("Action") && int.TryParse(name["Action".Length..], out int id) ? id : -1;
    }

    // Shows action `id` (an index into PowerActions.All); anything else is ignored.
    public void ShowAction(int id)
    {
        if (id < 0 || id >= PowerActions.Count)
            return;
        ShowConversion(Cost(id), Gain(id));
    }

    // What action `id` costs: power from bowl 3.
    public static Amounts Cost(int id) => Amounts.Of(ResourceKind.PowerSpend, PowerActions.All[id].Cost);

    // What action `id` gives: its income, spades and bridges.
    public static Amounts Gain(int id)
    {
        PowerActionDefinition definition = PowerActions.All[id];
        return Amounts.From(definition.Income) + Amounts.Of(
            (ResourceKind.Spade, definition.Spades), (ResourceKind.Bridge, definition.Bridges));
    }
}
