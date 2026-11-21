using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// What the editor shows on the children of a bank laid out by EvenlySpaced (nothing in the game, where Main puts the
// real tiles there): one tile on each child, as the game draws it there.
public enum BankPreview { None, RoundTiles, Favours, KeyTokens, PassTiles }

// Lays out this node's children evenly in a grid: the first at Start, columns Step apart and rows RowStep apart.
// Filled row by row (PerRow to a row), or with ColumnsFirst column by column (PerRow then counts how many go
// down each column). PerRow 0: one line, along Step (or along RowStep with ColumnsFirst).
// Only Node2D children whose names start with Prefix are moved (all Node2D children if Prefix is empty),
// so a background sprite or a label can sit alongside them untouched. Their order in the scene tree is
// their order on screen.
// [Tool]: this also runs in the editor, so changing a value in the Inspector moves every child at once.
// [GlobalClass]: it appears in the Add Node dialog and can be chosen as a node's script type.
[Tool]
[GlobalClass]
public partial class EvenlySpaced : Node2D
{
    private string _prefix = "";
    private Vector2 _start = Vector2.Zero;
    private Vector2 _step = new(100, 0);
    private int _perRow;
    private Vector2 _rowStep = new(0, 100);
    private bool _columnsFirst;

    [Export] public string Prefix { get => _prefix; set { _prefix = value ?? ""; Arrange(); } }
    [Export] public Vector2 Start { get => _start; set { _start = value; Arrange(); } }
    [Export] public Vector2 Step { get => _step; set { _step = value; Arrange(); } }
    [Export] public int PerRow { get => _perRow; set { _perRow = Mathf.Max(0, value); Arrange(); } }
    [Export] public Vector2 RowStep { get => _rowStep; set { _rowStep = value; Arrange(); } }
    [Export] public bool ColumnsFirst { get => _columnsFirst; set { _columnsFirst = value; Arrange(); } }
    // Editor only: a tile on each child (see BankPreview). Never saved, never in the game.
    [Export] public BankPreview Preview { get => _preview; set { _preview = value; ShowPreview(); } }
    private BankPreview _preview;
    private const string PreviewMeta = "bank_preview";

    // Connected once, for the node's whole life, and never disconnected (Godot drops the connection when the node is
    // freed; Arrange does nothing outside the tree). Disconnecting in _ExitTree broke when a node left the tree and came
    // back (e.g. switching scene tabs in the editor): _Ready doesn't run again, so the next exit tried to disconnect a
    // connection that was no longer there ("Attempt to disconnect a nonexistent connection").
    public override void _Ready()
    {
        ChildOrderChanged += Arrange; // children added, removed or reordered
        Arrange();
        ShowPreview();
    }

    // In the editor: on the n-th child (by Prefix, in order), round tile n (round 6 without its end-of-round reward),
    // the favour the game stacks there (Main.FavourBankPlace), key token n, or pass tile n, at the child's scale.
    private void ShowPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;
        foreach (Node child in GetChildren())
            foreach (Node made in child.GetChildren(includeInternal: true).Where(node => node.HasMeta(PreviewMeta)))
                made.QueueFree();
        if (_preview == BankPreview.None)
            return;
        var markers = GetChildren().OfType<Node2D>().Where(node => node.Name.ToString().StartsWith(_prefix)).ToList();
        for (var n = 0; n < markers.Count; n++)
        {
            Node2D view = _preview switch
            {
                BankPreview.RoundTiles when n < RoundTiles.Count => RoundTileView.Create(n, endOfRound: n < markers.Count - 1),
                BankPreview.Favours => Enumerable.Range(0, Favours.Count)
                    .Where(id => (int)Favours.All[id].Track * 3 + (3 - Favours.All[id].Steps) == n)
                    .Select(id => (Node2D)FavourTileView.Create(id)).FirstOrDefault(),
                BankPreview.KeyTokens when n < KeyTiles.Count => KeyTileView.Create(n),
                BankPreview.PassTiles when n < PassTiles.Count => PassTileView.Create(n),
                _ => null,
            };
            if (view is null)
                continue;
            view.SetMeta(PreviewMeta, true);
            markers[n].AddChild(view, false, InternalMode.Back); // no owner, internal: never saved with the scene
        }
    }

    public void Arrange()
    {
        if (!IsInsideTree())
            return; // still loading: _Ready arranges once the children exist
        var index = 0;
        foreach (Node child in GetChildren())
        {
            if (child is not Node2D node || !node.Name.ToString().StartsWith(_prefix))
                continue;
            // Along the line being filled, and which line it is.
            int along = _perRow > 0 ? index % _perRow : index;
            int line = _perRow > 0 ? index / _perRow : 0;
            (int column, int row) = _columnsFirst ? (line, along) : (along, line);
            node.Position = _start + _step * column + _rowStep * row;
            index++;
        }
    }
}
