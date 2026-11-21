using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// A pass tile, drawn from its definition. Root of pass_tile.tscn, with children:
//   Background   the tile (Sprite2D);
//   Income       a ResourceBundle (IncomeIcons on): the income it adds;
//   Action       an ActionToken: its bonus action (hidden on tiles without one), crossed out once used;
//   PassScoring  a TriggerView: VP for passing with it, e.g. "dwelling → 1 VP", "shipping → 3 VP";
//   Shipping     a ResourceBundle: extra shipping while held (hidden when none);
//   Coins        a ResourceBundle: coins left on it in the bank (hidden when none).
// Parts with nothing to show are hidden. Parts can be grouped under other nodes (found by name): e.g. put
// Income, Action, PassScoring and Shipping in a SpreadColumn to centre whichever one or two are shown.
[Tool]
[GlobalClass]
public partial class PassTileView : Node2D
{
    public const string ScenePath = "res://scenes/pass_tile.tscn";

    // Editor preview: which tile to show (-1: leave the parts' own preview text).
    [Export] public int PreviewTile { get => _previewTile; set { _previewTile = value; if (Engine.IsEditorHint() && IsInsideTree()) ShowTile(value); } }
    private int _previewTile = -1;

    public int Tile { get; private set; } = -1;
    public ActionToken Action => Part<ActionToken>("Action");

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            ShowTile(PreviewTile);
    }

    public void ShowTile(int id)
    {
        if (id < 0 || id >= PassTiles.Count)
            return;
        Tile = id;
        PassTileDefinition tile = PassTiles.All[id];

        ShowPart<ResourceBundle>("Income", bundle =>
        {
            // As printed: a worker with power (tile 3) shows the power first.
            string order = tile.Income.Power > 0 && tile.Income.Workers > 0 ? "power" : "";
            if (bundle.Order != order)
                bundle.Order = order;
            bundle.ShowAmounts(Amounts.From(tile.Income));
        }, !Amounts.From(tile.Income).IsEmpty);

        var action = Part<Node2D>("Action");
        if (action is not null)
        {
            action.Visible = tile.BonusAction is not null;
            if (tile.BonusAction is BonusAction bonus)
            {
                if (action is ActionToken token)
                    token.ShowIcon(Art.BonusActionIcon(bonus));
                else if (action.GetNodeOrNull<Sprite2D>("Icon") is Sprite2D icon) // in the editor
                    icon.Texture = Art.BonusActionIcon(bonus);
            }
        }

        Amounts counted = tile.PassVictoryPointsPerBuilding > 0 ? Amounts.Each(tile.PassScoredBuildings.Select(Amounts.KindOf))
            : tile.PassVictoryPointsPerShipping > 0 ? Amounts.Of(ResourceKind.Shipping, 1)
            : Amounts.None;
        int points = tile.PassVictoryPointsPerBuilding + tile.PassVictoryPointsPerShipping;
        Part<TriggerView>("PassScoring")?.ShowTrigger(counted, Amounts.Of(ResourceKind.VictoryPoint, points));

        ShowPart<ResourceBundle>("Shipping", bundle => bundle.ShowAmounts(Amounts.Of(ResourceKind.Shipping, tile.ShippingBonus)),
            tile.ShippingBonus > 0);
        if (!Engine.IsEditorHint())
            ShowCoins(0);
        // Parts were shown or hidden: any SpreadColumn holding them spaces them out again.
        foreach (SpreadColumn column in SpreadColumn.Within(this))
            column.Arrange();
    }

    // A part by name, anywhere under the tile (so parts can be grouped, e.g. in a SpreadColumn).
    private T Part<T>(string name) where T : Node => FindChild(name, recursive: true, owned: false) as T;

    // The coins left on the tile in the bank.
    public void ShowCoins(int coins) =>
        ShowPart<ResourceBundle>("Coins", bundle => bundle.ShowAmounts(Amounts.Of(ResourceKind.Coin, coins)), coins > 0);

    // The income on it already drawn (held through the round it was paid for): faded to DrawnOpacity; back to full in
    // the bank.
    public const float DrawnOpacity = 0.5f;
    public void ShowIncomeDrawn(bool drawn)
    {
        if (Part<ResourceBundle>("Income") is ResourceBundle income)
            income.Modulate = Colors.White with { A = drawn ? DrawnOpacity : 1f };
    }

    // The bonus action crossed out (used this round), and highlighted (waiting for its target).
    public void ShowAction(bool used, bool selected, Color selectedTint)
    {
        if (Action is not ActionToken token)
            return;
        token.IsUsed = used;
        token.Modulate = selected ? selectedTint : Colors.White;
    }

    public bool IsOver(Vector2 globalPoint) => Hit.IsOver(this, globalPoint);

    private void ShowPart<T>(string name, System.Action<T> show, bool visible) where T : Node2D
    {
        if (Part<T>(name) is not T node)
            return;
        show(node);
        node.Visible = visible || (Engine.IsEditorHint() && PreviewTile < 0);
    }

    // A new tile view, from pass_tile.tscn.
    public static PassTileView Create(int id)
    {
        var view = GD.Load<PackedScene>(ScenePath).Instantiate<PassTileView>();
        view.Ready += () => view.ShowTile(id);
        return view;
    }
}
