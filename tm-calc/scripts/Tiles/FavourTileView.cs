using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// A favour tile, drawn from its definition. Root of favour.tscn, with children:
//   Background   the tile (BlankTileSprite, Favour);
//   Steps        a ResourceBundle: the steps it gives at once on its track (a column of that track's discs). Its X
//                is set here: StepsBesideX beside another part, StepsAloneX when it is the only one;
//   Income       a ResourceBundle (IncomeIcons on): the income it adds every round;
//   WhenBuilt    a TriggerView: VP each time a dwelling or trading post is built;
//   PassScoring  VP for trading posts when passing. Either a TriggerView (trading post → VP, with an optional
//                Label "Points" listing the VP for 1, 2, 3, 4 or more), or any Node2D holding a picture built by
//                hand (e.g. "on pass: 2/3/3/4 VP for 1/2/3/4 trading posts"), which is only shown or hidden;
//   Town         a ResourceBundle: the power a town needs while the favour is held (the town icon, numbered);
//   Action       an ActionToken: its bonus action, crossed out once used this round.
// Parts the favour doesn't have are hidden.
[Tool]
[GlobalClass]
public partial class FavourTileView : Node2D
{
    public const string ScenePath = "res://scenes/favour.tscn";

    // Editor preview: which favour to show (-1: leave the parts' own preview text).
    [Export] public int PreviewTile { get => _previewTile; set { _previewTile = value; if (Engine.IsEditorHint() && IsInsideTree()) ShowTile(value); } }
    private int _previewTile = -1;

    public ActionToken Action => GetNodeOrNull<ActionToken>("Action");

    // Where Steps sits across the tile: beside the favour's other part, or alone (on the 3-step favours).
    [Export] public float StepsBesideX { get => _besideX; set { _besideX = value; Refresh(); } }
    [Export] public float StepsAloneX { get => _aloneX; set { _aloneX = value; Refresh(); } }
    private float _besideX = -39f, _aloneX;
    private int _shown = -1;

    private void Refresh()
    {
        if (IsInsideTree())
            ShowTile(_shown >= 0 ? _shown : PreviewTile);
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            ShowTile(PreviewTile);
    }

    public void ShowTile(int id)
    {
        if (id < 0 || id >= Favours.Count)
            return;
        _shown = Engine.IsEditorHint() ? -1 : id;
        FavourDefinition favour = Favours.All[id];
        bool stepsOnly = favour.Income == default && favour.VictoryPointsPerDwelling == 0 && favour.VictoryPointsPerTradingPost == 0
            && favour.PassVictoryPoints.IsEmpty && favour.BonusAction is null && favour.TownPowerReduction == 0;
        if (GetNodeOrNull<Node2D>("Steps") is Node2D stepsNode)
            stepsNode.Position = new Vector2(stepsOnly ? StepsAloneX : StepsBesideX, stepsNode.Position.Y);

        ShowPart<ResourceBundle>("Steps", b => b.ShowAmounts(Amounts.Of(Amounts.StepsOn(favour.Track), favour.Steps)), true);
        Amounts income = Amounts.From(favour.Income);
        ShowPart<ResourceBundle>("Income", b => b.ShowAmounts(income), !income.IsEmpty);

        // VP each time a dwelling (or trading post) is built.
        int builtPoints = favour.VictoryPointsPerDwelling + favour.VictoryPointsPerTradingPost;
        ResourceKind built = favour.VictoryPointsPerTradingPost > 0 ? ResourceKind.WhenTradingPostBuilt : ResourceKind.WhenDwellingBuilt;
        ShowPart<TriggerView>("WhenBuilt", t => t.ShowTrigger(Amounts.Of(built, 1), Amounts.Of(ResourceKind.VictoryPoint, builtPoints)),
            builtPoints > 0);

        // VP for trading posts when passing: the table of points for 1, 2, 3, 4 or more.
        bool passes = !favour.PassVictoryPoints.IsEmpty;
        ShowPart<Node2D>("PassScoring", node =>
        {
            if (node is not TriggerView t)
                return; // built by hand: nothing to fill in
            t.ShowTrigger(Amounts.Of(ResourceKind.TradingPost, 1), Amounts.Of(ResourceKind.VictoryPoint, 1));
            if (t.GetNodeOrNull<Label>("Points") is Label points)
            {
                points.Text = string.Join(" / ", favour.PassVictoryPoints.Skip(1));
                TextStyle.BlackOnWhite(points);
            }
        }, passes);

        ShowPart<ResourceBundle>("Town", b => b.ShowAmounts(Amounts.Of(ResourceKind.Town, Config.TownPower - favour.TownPowerReduction)),
            favour.TownPowerReduction > 0);

        var action = GetNodeOrNull<Node2D>("Action");
        if (action is not null)
        {
            action.Visible = favour.BonusAction is not null;
            if (favour.BonusAction is BonusAction bonus)
            {
                if (action is ActionToken token)
                    token.ShowIcon(Art.BonusActionIcon(bonus));
                else if (action.GetNodeOrNull<Sprite2D>("Icon") is Sprite2D icon) // in the editor
                    icon.Texture = Art.BonusActionIcon(bonus);
            }
        }
    }

    // The bonus action crossed out (its holder used it this round).
    public void ShowActionUsed(bool used)
    {
        if (Action is ActionToken token)
            token.IsUsed = used;
    }

    private void ShowPart<T>(string name, System.Action<T> show, bool visible) where T : Node2D
    {
        if (GetNodeOrNull<T>(name) is not T node)
            return;
        show(node);
        node.Visible = visible || (Engine.IsEditorHint() && PreviewTile < 0);
    }

    // A new favour view, from favour.tscn.
    public static FavourTileView Create(int id)
    {
        var view = GD.Load<PackedScene>(ScenePath).Instantiate<FavourTileView>();
        view.Ready += () => view.ShowTile(id);
        return view;
    }
}
