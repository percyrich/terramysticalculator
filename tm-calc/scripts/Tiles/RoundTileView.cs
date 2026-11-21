using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// A round tile, drawn from its definition. Root of round_tile.tscn, with children:
//   Background  the tile (Sprite2D);
//   Scoring     a TriggerView: what scores during the round, e.g. "dwelling built → 2 VP", "spade → 2 VP";
//   EndOfRound  a TriggerView: the reward at the end of the round, e.g. "4 fire steps → 1 priest",
//               "priest on a track → 2 coins".
// A trigger with nothing to show is hidden; so is EndOfRound on the last round's tile (no reward after round 6).
[Tool]
[GlobalClass]
public partial class RoundTileView : Node2D
{
    public const string ScenePath = "res://scenes/round_tile.tscn";

    // Editor preview: which tile to show (-1: leave the triggers' own preview text).
    [Export] public int PreviewTile { get => _previewTile; set { _previewTile = value; if (Engine.IsEditorHint() && IsInsideTree()) ShowTile(value); } }
    private int _previewTile = -1;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            ShowTile(PreviewTile);
    }

    // endOfRound false: the tile is in the last round, which has no end-of-round reward; EndOfRound stays hidden.
    public void ShowTile(int id, bool endOfRound = true)
    {
        if (id < 0 || id >= RoundTiles.Count)
            return;
        RoundTileDefinition tile = RoundTiles.All[id];

        // Buildings score each time one is built: the "when built" icon (one for a stronghold or sanctuary).
        Amounts scoring = tile.VictoryPointsPerBuilding > 0 ? Amounts.Of(Amounts.WhenBuilt(tile.ScoredBuildings), 1)
            : tile.VictoryPointsPerSpade > 0 ? Amounts.Of(ResourceKind.Spade, 1)
            : tile.VictoryPointsPerTown > 0 ? Amounts.Of(ResourceKind.Key, 1) // a town formed: its key
            : Amounts.None;
        int points = tile.VictoryPointsPerBuilding + tile.VictoryPointsPerSpade + tile.VictoryPointsPerTown;
        GetNodeOrNull<TriggerView>("Scoring")?.ShowTrigger(scoring, Amounts.Of(ResourceKind.VictoryPoint, points));

        Amounts counted = tile.Track is Track track ? Amounts.Of(Amounts.StepsOn(track), tile.StepsPerReward)
            : tile.CoinsPerPriestOnTracks > 0 ? Amounts.Of(ResourceKind.PriestOnTrack, 1)
            : Amounts.None;
        Amounts reward = Amounts.From(tile.Reward) + Amounts.Of(ResourceKind.Spade, tile.SpadesPerReward)
            + Amounts.Of(ResourceKind.Coin, tile.CoinsPerPriestOnTracks);
        if (endOfRound)
            GetNodeOrNull<TriggerView>("EndOfRound")?.ShowTrigger(counted, reward);
        else
            GetNodeOrNull<TriggerView>("EndOfRound")?.ShowTrigger(Amounts.None, Amounts.None); // nothing: hidden
        ShowFinalSymbol(!endOfRound);
    }

    // The last round's tile: the Terra Mystica symbol in the right-hand panel instead (the game ends after it).
    [Export(PropertyHint.Range, "0.1,1.5,0.01")] public float FinalSymbolScale { get; set; } = 0.96f; // of the panel's side
    private const string FinalSymbolName = "FinalSymbol";

    private void ShowFinalSymbol(bool show)
    {
        var symbol = GetNodeOrNull<Sprite2D>(FinalSymbolName);
        if (!show)
        {
            if (symbol is not null)
                symbol.Visible = false;
            return;
        }
        if (GetNodeOrNull<BlankTileSprite>("Background") is not BlankTileSprite background)
            return;
        // The panel's centre as a share of the tile, found on the background's own drawn rectangle and carried into
        // this node's pixels by its transform (whatever its offset, centring or scale).
        Rect2 panel = IconArt.RoundTilePanel(1);
        Vector2 natural = IconArt.NaturalSize(BlankTile.Round);
        Rect2 drawn = background.GetRect();
        Vector2 centre = background.Transform * (drawn.Position + panel.GetCenter() / natural * drawn.Size);
        float side = panel.Size.X / natural.X * drawn.Size.X * background.Scale.X;
        if (symbol is null)
        {
            symbol = new Sprite2D { Name = FinalSymbolName };
            AddChild(symbol, false, InternalMode.Back); // made here, never saved with the scene
        }
        symbol.Visible = true;
        symbol.Texture = IconArt.Texture(Icon.TerraMysticaSymbol, Mathf.Max(1, Mathf.RoundToInt(side * FinalSymbolScale)));
        symbol.Position = centre;
    }

    // A half of the tile (0: the left, the scoring; 1: the right, the cult reward), its panel grown by margin (a share of
    // the panel's side), in this node's pixels; null without the background.
    public Rect2? HalfRect(int half, float margin = 0.08f)
    {
        if (GetNodeOrNull<BlankTileSprite>("Background") is not BlankTileSprite background)
            return null;
        Rect2 panel = IconArt.RoundTilePanel(half);
        Vector2 natural = IconArt.NaturalSize(BlankTile.Round);
        Rect2 drawn = background.GetRect();
        Vector2 a = background.Transform * (drawn.Position + panel.Position / natural * drawn.Size);
        Vector2 b = background.Transform * (drawn.Position + panel.End / natural * drawn.Size);
        var rect = new Rect2(a, b - a).Abs();
        return rect.Grow(rect.Size.X * margin);
    }

    // A new tile view, from round_tile.tscn.
    public static RoundTileView Create(int id, bool endOfRound = true)
    {
        var view = GD.Load<PackedScene>(ScenePath).Instantiate<RoundTileView>();
        view.Ready += () => view.ShowTile(id, endOfRound);
        return view;
    }
}
