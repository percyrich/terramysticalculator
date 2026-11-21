using Core;
using Godot;

namespace TMCalc;

// A key token (town tile), drawn from its definition. Root of key_token.tscn, with children:
//   Background  the token (BlankTileSprite, KeyToken);
//   Points      a ResourceBundle: the victory points for forming the town;
//   Keys        a ResourceBundle: the keys it gives (one key icon; a number on it when more);
//   Reward      a ResourceBundle: what else it gives (income, steps on every track, a shipping upgrade).
// Parts with nothing to show are hidden.
[Tool]
[GlobalClass]
public partial class KeyTileView : Node2D
{
    public const string ScenePath = "res://scenes/key_token.tscn";

    // Editor preview: which token to show (-1: leave the parts' own preview text).
    [Export] public int PreviewTile { get => _previewTile; set { _previewTile = value; if (Engine.IsEditorHint() && IsInsideTree()) ShowTile(value); } }
    private int _previewTile = -1;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            ShowTile(PreviewTile);
    }

    public void ShowTile(int id)
    {
        if (id < 0 || id >= KeyTiles.Count)
            return;
        KeyTileDefinition token = KeyTiles.All[id];
        ShowPart("Points", Amounts.Of(ResourceKind.VictoryPoint, token.VictoryPoints));
        ShowPart("Keys", Amounts.Of(ResourceKind.Key, token.Keys));
        ShowPart("Reward", Amounts.From(token.Income) + Amounts.Of(
            (ResourceKind.EveryCultStep, token.StepsOnEveryTrack), (ResourceKind.Shipping, token.ShippingUpgrade ? 1 : 0)));
    }

    private void ShowPart(string name, Amounts amounts)
    {
        if (GetNodeOrNull<ResourceBundle>(name) is not ResourceBundle bundle)
            return;
        bundle.ShowAmounts(amounts);
        bundle.Visible = !amounts.IsEmpty;
    }

    // A new token view, from key_token.tscn.
    public static KeyTileView Create(int id)
    {
        var view = GD.Load<PackedScene>(ScenePath).Instantiate<KeyTileView>();
        view.Ready += () => view.ShowTile(id);
        return view;
    }
}
