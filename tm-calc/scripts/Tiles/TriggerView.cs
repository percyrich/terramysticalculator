using Godot;

namespace TMCalc;

// Something that happens, then what it earns, e.g. "dwelling → 2 VP" or "4 fire steps → 1 priest":
// children named Trigger (a ResourceBundle of what is counted, or a Sprite2D picture of the event),
// Separator (optional Sprite2D, e.g. an arrow or a colon) and Reward (a ResourceBundle). Separator and
// Reward are placed after Trigger automatically, Gap apart: to its right, or below it when Vertical.
// With FixedSeparatorAndReward, the other way round: Separator and Reward stay where they are placed in the editor,
// and Trigger is moved to end Gap before them, so a bigger or smaller trigger never moves what follows it.
[Tool]
[GlobalClass]
public partial class TriggerView : Node2D
{
    // Editor previews, e.g. "1 stronghold, 1 sanctuary" and "5 VP" (see Amounts.Parse).
    [Export] public string TriggerText { get => _triggerText; set { _triggerText = value; Preview(); } }
    [Export] public string RewardText { get => _rewardText; set { _rewardText = value; Preview(); } }
    // Between the drawings (not counting icons' transparent margins), in this node's pixels; negative overlaps.
    [Export] public float Gap { get => _gap; set { _gap = value; Arrange(); } }
    // Top to bottom instead of left to right. Ticking it turns the Separator a quarter turn clockwise (so an
    // arrow pointing right points down); unticking turns it back. Only the parts' positions along the line are
    // set; place the trigger across it (e.g. centre it) in the editor.
    [Export]
    public bool Vertical
    {
        get => _vertical;
        set
        {
            bool changed = value != _vertical;
            _vertical = value;
            // Only when changed in the editor (or by code), not as the scene loads: the Separator's turn is saved.
            if (changed && IsInsideTree() && GetNodeOrNull<Node2D>("Separator") is Node2D separator)
                separator.Rotation += value ? Mathf.Pi / 2 : -Mathf.Pi / 2;
            Arrange();
        }
    }

    // Separator and Reward stay put (as placed in the editor); Trigger is moved to end Gap before the Separator (or
    // before the Reward, if there is no Separator showing).
    [Export] public bool FixedSeparatorAndReward { get => _fixed; set { _fixed = value; Arrange(); } }
    private bool _fixed;

    private string _triggerText = "";
    private string _rewardText = "";
    // Other children (e.g. a "when passing" icon beside a vertical trigger) kept level with the Separator:
    // their drawing's middle is moved to the Separator's along the line (down it when Vertical, across
    // otherwise); where they sit across the line is left as placed.
    [Export] public Godot.Collections.Array<NodePath> LevelWithSeparator { get => _level; set { _level = value; Arrange(); } }
    private Godot.Collections.Array<NodePath> _level = new();

    private float _gap = 12f;
    private bool _vertical;

    public override void _Ready() => Preview();

    // Trigger as counted things (a Trigger bundle). Hidden altogether when there is no reward
    // (in the editor too; only its own preview text is always shown).
    public void ShowTrigger(Amounts trigger, Amounts reward) => ShowTrigger(trigger, reward, preview: false);

    // Trigger as a picture (a Trigger sprite); null keeps the picture set in the editor.
    public void ShowTrigger(Texture2D trigger, Amounts reward) => ShowTrigger(trigger, reward, preview: false);

    private void ShowTrigger(Amounts trigger, Amounts reward, bool preview)
    {
        if (GetNodeOrNull<ResourceBundle>("Trigger") is ResourceBundle triggerView)
            triggerView.ShowAmounts(trigger);
        else if (GetNodeOrNull<Node2D>("Trigger") is null)
            GD.PushWarning($"{GetPath()} needs a child named Trigger.");
        ShowReward(reward, preview);
    }

    private void ShowTrigger(Texture2D trigger, Amounts reward, bool preview)
    {
        if (GetNodeOrNull<Sprite2D>("Trigger") is Sprite2D picture && trigger is not null)
            picture.Texture = trigger;
        ShowReward(reward, preview);
    }

    private void ShowReward(Amounts reward, bool preview)
    {
        if (GetNodeOrNull<ResourceBundle>("Reward") is not ResourceBundle rewardView)
        {
            GD.PushWarning($"{GetPath()} needs a ResourceBundle named Reward.");
            return;
        }
        rewardView.ShowAmounts(reward);
        Visible = !(reward ?? Amounts.None).IsEmpty || preview;
        Arrange();
    }

    // Lines the parts up again with what they show now (e.g. after Gap changes).
    private void Arrange()
    {
        if (!IsInsideTree())
            return;
        var separator = GetNodeOrNull<Node2D>("Separator");
        if (FixedSeparatorAndReward)
            PlaceTriggerBefore(separator is { Visible: true } ? separator : GetNodeOrNull<Node2D>("Reward"));
        else
            Arrangement.InALine(Gap, Vertical, GetNodeOrNull<Node2D>("Trigger"), separator, GetNodeOrNull<Node2D>("Reward"));
        if (separator is null || !separator.Visible)
            return;
        Vector2 level = separator.Position + Arrangement.Extent(separator).GetCenter();
        foreach (NodePath path in _level ?? new())
        {
            if (GetNodeOrNull<Node2D>(path) is not Node2D node)
                continue;
            Vector2 middle = node.Position + Arrangement.Extent(node).GetCenter();
            node.Position += Vertical ? new Vector2(0, level.Y - middle.Y) : new Vector2(level.X - middle.X, 0);
        }
    }

    // Trigger's drawing ends Gap before where `next`'s drawing starts, along the line; `next` doesn't move.
    private void PlaceTriggerBefore(Node2D next)
    {
        if (GetNodeOrNull<Node2D>("Trigger") is not { Visible: true } trigger || next is null)
            return;
        Rect2 nextDrawn = Arrangement.Extent(next), drawn = Arrangement.Extent(trigger);
        if (Vertical)
            trigger.Position = new Vector2(trigger.Position.X, next.Position.Y + nextDrawn.Position.Y - Gap - drawn.End.Y);
        else
            trigger.Position = new Vector2(next.Position.X + nextDrawn.Position.X - Gap - drawn.End.X, trigger.Position.Y);
    }

    private void Preview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree() || (TriggerText == "" && RewardText == ""))
            return;
        if (GetNodeOrNull<ResourceBundle>("Trigger") is not null)
            ShowTrigger(Amounts.Parse(TriggerText, out _), Amounts.Parse(RewardText, out _), preview: true);
        else
            ShowTrigger((Texture2D)null, Amounts.Parse(RewardText, out _), preview: true);
    }
}
