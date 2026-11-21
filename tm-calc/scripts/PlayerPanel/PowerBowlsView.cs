using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// A player's power: the three bowls (FactionBoardArt.PowerBowls: I, II, III left to right, centred on this node),
// with a purple token lying in them for each point of power.
//   In each bowl the tokens fill a 4 × 4 grid row by row, from the top left, and the filled part is kept centred
//     in the bowl (CentreRows: a part-filled last row is centred on its own too, rather than lined up under the
//     start of the rows above). More than 16 tokens make more rows of 4, bunched up into the height of 4 rows
//     (overlapping if need be).
//   When power changes, tokens slide between bowls: gained power from I to II, then II to III; spent power from
//     III back to I (the first tokens of III, the ones counted to pay); burned tokens fade out of II.
//   Tokens are counted most valuable first, as Power.TokenBowls: bowl III's in grid order, then II's, then I's.
//   ShowConversions: a small menu hanging under bowl III, a priest, a worker and a coin (Main's power conversions).
//   ShowBurnHover: bowl II's last token (the one burning takes away) outlined in strong red, following the token's
//     own shape (as the red outline round a priest owed).
//   Setup (Main): LiftToken picks up a bowl's token (the one a move out of it takes), DragLifted carries it,
//     DirectNextChange makes the next change move tokens straight from bowl to bowl (the lifted one first, to where a
//     token is wanted, or out of the game) instead of round the I → II → III cycle, ReleaseLifted puts it back.
// In the editor, PreviewPower is shown. Nothing made here is saved with the scene.
[Tool]
[GlobalClass]
public partial class PowerBowlsView : Node2D
{
    [ExportGroup("Tokens")]
    // Bowls I, II and III, shown in the editor.
    [Export] public Vector3I PreviewPower { get => _preview; set { _preview = value; Changed(); } }
    // How big a token is drawn, in pixels of this node, and from one grid place to the next.
    [Export] public int TokenSize { get => _tokenSize; set { _tokenSize = value; Changed(); } }
    [Export] public float TokenSpacing { get => _tokenSpacing; set { _tokenSpacing = value; Changed(); } }
    [Export] public bool CentreRows { get => _centreRows; set { _centreRows = value; Changed(); } }
    // How many times the bowls' natural size (460 × 152) their texture is made, for sharpness if scaled up.
    [Export(PropertyHint.Range, "0.5,4,0.25")] public float Resolution { get => _resolution; set { _resolution = value; Changed(); } }
    // The highlight's widths, of EdgeHighlight's (a bowl is small).
    [Export] public float BowlHighlightScale { get; set; } = 0.6f;

    [ExportGroup("Burning")]
    // The outline, solid from the token's own dark edge out: an extra margin, its thickness, its colour. A thin dark
    // line round its outside keeps it clear on the purple tokens and the bowl's colour.
    [Export] public float BurnHighlightMargin { get; set; } = 0f;
    [Export] public float BurnOutlineWidth { get; set; } = 4f;
    [Export] public Color BurnOutlineColour { get; set; } = new(0.92f, 0.06f, 0.06f);

    [ExportGroup("Conversions")]
    [Export] public int ConversionIconSize { get; set; } = 44;
    [Export] public float ConversionSpacing { get; set; } = 54f;          // from one icon's centre to the next
    [Export] public Vector2 ConversionPadding { get; set; } = new(14, 10); // round the icons
    [Export] public float ConversionCornerRadius { get; set; } = 14f;
    [Export] public Color ConversionFillColour { get; set; } = new(0.055f, 0.055f, 0.055f, 0.9f);
    [Export] public Color ConversionEdgeColour { get; set; } = new(0.62f, 0.45f, 0.78f);
    [Export] public float ConversionEdgeWidth { get; set; } = 2f;
    [Export] public float ConversionDisabledOpacity { get; set; } = 0.5f;  // a conversion that can't be made
    [Export] public int ConversionZIndex { get; set; } = 35;             // over the rest of the panel
    [Export] public Vector2 ConversionDoubleStep { get; set; } = new(9, -7);
    [ExportGroup("")]

    private const int PerRow = 4, Rows = 4; // more rows than Rows are bunched into their height

    private Vector3I _preview = new(5, 7, 0);
    private int _tokenSize = 24;
    private float _tokenSpacing = 34f;
    private bool _centreRows;
    private float _resolution = 1f;

    private readonly List<Sprite2D>[] _bowls = { new(), new(), new() }; // bowls I, II, III: their tokens in grid order
    private readonly Dictionary<Sprite2D, Tween> _moves = new();
    private Power _power;
    private bool _shownByCode;
    // Marks the nodes made in code (internal children: not in the scene tree, never saved).
    private const string MadeMeta = "power_bowls_made";

    public override void _Ready() => Build();

    // Shows this power, the tokens sliding between bowls over `seconds` (0: at once).
    public void ShowPower(Power power, float seconds = 0f)
    {
        if (_shownByCode && power == _power)
            return;
        bool animate = _shownByCode && seconds > 0f && !Engine.IsEditorHint();
        _shownByCode = true;
        _power = power;
        MoveTo(power, animate ? seconds : 0f);
    }

    private void Changed()
    {
        if (IsInsideTree())
            Build();
    }

    private void Build()
    {
        // Found by their mark rather than remembered, so none are left behind when scripts are reloaded.
        foreach (Node child in GetChildren(includeInternal: true))
            if (child.HasMeta(MadeMeta))
                child.QueueFree();
        foreach (List<Sprite2D> bowl in _bowls)
            bowl.Clear();
        _moves.Clear();
        _menu = null;
        _burnHolder = null;
        _burnToken = null;
        _burnAt = null;

        Made(new Sprite2D { Texture = FactionBoardArt.PowerBowls(Resolution), Scale = Vector2.One / Resolution });
        // Until code shows a player's power: the preview in the editor, no tokens in the game.
        Power shown = _shownByCode ? _power : Engine.IsEditorHint() ? new Power(_preview.X, _preview.Y, _preview.Z) : new Power(0, 0, 0);
        MoveTo(shown, 0f);
    }

    // ---------- Setup: tokens moved by hand ----------

    private Sprite2D _lifted;   // a token picked up by the mouse, if any
    private int _liftedBowl;    // its bowl (0 = I)
    private bool _direct;       // the next change moves tokens straight between bowls
    private const int LiftedZ = 60;

    public bool DirectNextChange { get => _direct; set => _direct = value; }

    // Picks up the token a move out of this bowl (1-3) takes; false if the bowl is empty.
    public bool LiftToken(int bowl)
    {
        List<Sprite2D> tokens = _bowls[bowl - 1];
        if (tokens.Count == 0)
            return false;
        _lifted = bowl == 3 ? tokens[0] : tokens[^1];
        _liftedBowl = bowl - 1;
        StopMove(_lifted);
        _lifted.ZIndex = LiftedZ;
        return true;
    }

    public void DragLifted(Vector2 globalPoint)
    {
        if (_lifted is not null)
            _lifted.GlobalPosition = globalPoint;
    }

    // The lifted token back to its place (nothing changed), and no direct change pending.
    public void ReleaseLifted()
    {
        _direct = false;
        if (_lifted is null)
            return;
        _lifted.ZIndex = 0;
        _lifted = null;
        Arrange(0.15f);
    }

    // Straight from bowl to bowl: the lifted token to a bowl that wants one more (or out of the game), then tokens
    // taken from bowls with too many and new ones made in bowls with too few.
    private void MoveDirectly(int[] want, float seconds)
    {
        if (_lifted is not null && _bowls[_liftedBowl].Contains(_lifted) && _bowls[_liftedBowl].Count > want[_liftedBowl])
        {
            _bowls[_liftedBowl].Remove(_lifted);
            int to = Enumerable.Range(0, 3).FirstOrDefault(b => b != _liftedBowl && _bowls[b].Count < want[b], -1);
            if (to >= 0)
                _bowls[to].Add(_lifted);
            else
                Remove(_lifted, seconds);
        }
        if (_lifted is not null)
            _lifted.ZIndex = 0;
        _lifted = null;
        for (var b = 0; b < 3; b++)
            while (_bowls[b].Count > want[b])
            {
                Sprite2D gone = _bowls[b][^1];
                _bowls[b].RemoveAt(_bowls[b].Count - 1);
                Remove(gone, seconds);
            }
        for (var b = 0; b < 3; b++)
            while (_bowls[b].Count < want[b])
            {
                Sprite2D made = NewToken(seconds);
                made.Position = Place(b, _bowls[b].Count, want[b]);
                _bowls[b].Add(made);
            }
        Arrange(seconds);
    }

    // Moves the tokens there are to show `power`, with as few moves as the rules would make.
    private void MoveTo(Power power, float seconds)
    {
        int[] want = { System.Math.Max(power.Bowl1, 0), System.Math.Max(power.Bowl2, 0), System.Math.Max(power.Bowl3, 0) };
        if (_direct)
        {
            _direct = false;
            MoveDirectly(want, seconds);
            return;
        }
        // Fewer tokens (burned): out of bowl II, else III, else I. More: new ones in bowl I.
        for (int extra = _bowls.Sum(b => b.Count) - want.Sum(); extra > 0; extra--)
        {
            List<Sprite2D> from = _bowls[1].Count > 0 ? _bowls[1] : _bowls[2].Count > 0 ? _bowls[2] : _bowls[0];
            Sprite2D gone = from[^1];
            from.RemoveAt(from.Count - 1);
            Remove(gone, seconds);
        }
        while (_bowls.Sum(b => b.Count) < want.Sum())
            _bowls[0].Add(NewToken(seconds));

        // Round the cycle I → II → III → I: the least spending (III → I) that makes the counts work, then gaining.
        int d1 = want[0] - _bowls[0].Count, d3 = want[2] - _bowls[2].Count;
        int spent = new[] { 0, d1, -d3 }.Max(), toTwo = spent - d1, toThree = spent + d3;
        Shift(2, 0, spent);
        Shift(0, 1, toTwo);
        Shift(1, 2, toThree);
        // Anything still off (a jump the rules can't make in one move, e.g. stepping back through history):
        // straight from where there are too many to where there are too few.
        for (var from = 0; from < 3; from++)
            for (var to = 0; to < 3; to++)
                if (from != to)
                    Shift(from, to, System.Math.Min(_bowls[from].Count - want[from], want[to] - _bowls[to].Count));

        Arrange(seconds);
    }

    // Moves up to n tokens from one bowl to another: out of bowl III the first ones (those counted to pay),
    // out of the others the last ones; into the end of the other bowl.
    private void Shift(int from, int to, int n)
    {
        n = System.Math.Min(n, _bowls[from].Count);
        if (n <= 0)
            return;
        List<Sprite2D> source = _bowls[from];
        int start = from == 2 ? 0 : source.Count - n;
        List<Sprite2D> moving = source.GetRange(start, n);
        source.RemoveRange(start, n);
        _bowls[to].AddRange(moving);
        foreach (Sprite2D token in moving)
            MoveChild(token, -1); // drawn over the tokens it passes
    }

    // Every token to its place in its bowl's grid.
    private void Arrange(float seconds)
    {
        for (var bowl = 0; bowl < 3; bowl++)
        {
            List<Sprite2D> tokens = _bowls[bowl];
            for (var i = 0; i < tokens.Count; i++)
                Slide(tokens[i], Place(bowl, i, tokens.Count), seconds);
        }
    }

    // Token i of n in a bowl (0 = I): the filled part of the grid centred in the bowl; past Rows rows, the rows
    // closer together so they take no more height than Rows do.
    private Vector2 Place(int bowl, int i, int n)
    {
        int rows = (n + PerRow - 1) / PerRow, row = i / PerRow, column = i % PerRow;
        int across = CentreRows && row == rows - 1 ? n - row * PerRow : System.Math.Min(n, PerRow);
        float down = rows > Rows ? TokenSpacing * (Rows - 1) / (rows - 1) : TokenSpacing;
        Vector2 centre = FactionBoardArt.PowerBowl(bowl + 1).GetCenter() - FactionBoardArt.PowerBowlsSize / 2;
        return centre + new Vector2((column - (across - 1) / 2f) * TokenSpacing, (row - (rows - 1) / 2f) * down);
    }

    private Sprite2D NewToken(float seconds)
    {
        var token = new Sprite2D { Texture = IconArt.Texture(Icon.PowerToken, TokenSize), Position = Place(0, _bowls[0].Count, _bowls[0].Count + 1) };
        Made(token);
        if (seconds > 0f)
        {
            token.Modulate = Colors.White with { A = 0f };
            CreateTween().TweenProperty(token, "modulate:a", 1f, seconds);
        }
        return token;
    }

    private void Remove(Sprite2D token, float seconds)
    {
        StopMove(token);
        if (seconds <= 0f)
        {
            token.QueueFree();
            return;
        }
        Tween fade = CreateTween();
        fade.TweenProperty(token, "modulate:a", 0f, seconds);
        fade.TweenCallback(Callable.From(token.QueueFree));
    }

    private void Slide(Sprite2D token, Vector2 target, float seconds)
    {
        StopMove(token);
        if (seconds <= 0f || token.Position == target)
        {
            token.Position = target;
            return;
        }
        Tween move = CreateTween();
        move.TweenProperty(token, "position", target, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _moves[token] = move;
    }

    private void StopMove(Sprite2D token)
    {
        if (_moves.Remove(token, out Tween move))
            move.Kill();
    }

    // ---------- A bowl lit up (the Shapeshifters' token offer: bowl III) ----------

    // Bowl 1, 2 or 3, in this node's pixels (the bowls are centred on it).
    public static Rect2 BowlRect(int bowl)
    {
        Rect2 rect = FactionBoardArt.PowerBowl(bowl);
        rect.Position -= FactionBoardArt.PowerBowlsSize / 2;
        return rect;
    }

    public bool IsOverBowl(int bowl, Vector2 globalPoint) => IsVisibleInTree() && BowlRect(bowl).HasPoint(ToLocal(globalPoint));

    // Round the bowl's edge (its corners' radius), drawn over the bowls and tokens: they are internal children placed
    // after every ordinary child, so drawn later; the highlight (an ordinary child) is raised above them by its ZIndex.
    public void HighlightBowl(int bowl, bool on, HighlightArt.Colour colour, int glints)
    {
        EdgeHighlight.SetOnArea(this, on, BowlRect(bowl), BowlCornerRadius, glints, colour, BowlHighlightScale);
        if (GetNodeOrNull<Node2D>(EdgeHighlight.NodeName) is Node2D highlight)
            highlight.ZIndex = 1;
    }
    private const float BowlCornerRadius = 26f; // as FactionBoardArt draws the bowls

    // ---------- Burning: bowl II's last token ringed in red ----------

    private Node2D _burnHolder; // the ring's node, over the tokens
    private Vector2? _burnAt;   // the token ringed (none: hidden)

    // Rings (or not) the token burning would take away: bowl II's last. Nothing if fewer than 2 are there.
    public void ShowBurnHover(bool on)
    {
        if (_burnHolder is null)
        {
            if (!on)
                return;
            _burnHolder = Made(new Node2D { Name = "BurnHighlight", ZIndex = 1 });
            _burnHolder.Draw += DrawBurnRing;
        }
        List<Sprite2D> two = _bowls[1];
        Vector2? at = on && two.Count >= 2 ? Place(1, two.Count - 1, two.Count) : null;
        if (at == _burnAt)
            return;
        _burnAt = at;
        _burnHolder.Visible = at is not null;
        _burnHolder.QueueRedraw();
    }

    private Sprite2D _burnToken; // a copy of the token, over its outline

    // The outline: the token's own picture (its dark edge included) drawn flat in BurnOutlineColour, shifted
    // BurnOutlineWidth every way round, with a thin dark rim beyond; then the token itself on top. Tight to its shape.
    private void DrawBurnRing()
    {
        if (_burnAt is not Vector2 at)
            return;
        Texture2D texture = IconArt.Texture(Icon.PowerToken, TokenSize);
        _burnHolder.Material ??= new ShaderMaterial { Shader = FlatShader };
        if (_burnToken is null || !IsInstanceValid(_burnToken))
        {
            _burnToken = new Sprite2D { Name = "Token" };
            _burnHolder.AddChild(_burnToken); // a child of a Made node: never saved; not in the flat material
        }
        _burnToken.Texture = texture;
        _burnToken.Position = at;
        Vector2 corner = at - texture.GetSize() / 2;
        const int Ways = 24;
        float width = BurnHighlightMargin + BurnOutlineWidth;
        foreach (var (radius, colour) in new[] { (width + 1.5f, new Color(0.15f, 0, 0, 0.9f)), (width, BurnOutlineColour) })
            for (var i = 0; i < Ways; i++)
                _burnHolder.DrawTexture(texture, corner + Vector2.FromAngle(Mathf.Tau * i / Ways) * radius, colour);
    }

    // Draws a texture's shape in the draw's colour (its alpha kept, its colours dropped).
    private static Shader _flatShader;
    private static Shader FlatShader => _flatShader ??= new Shader
    {
        Code = """
            shader_type canvas_item;
            varying vec4 tint;
            void vertex() { tint = COLOR; }
            void fragment() { COLOR = vec4(tint.rgb, tint.a * texture(TEXTURE, UV).a); }
            """,
    };

    // ---------- The conversion menu: priest, worker, coin in a row, its top edge on bowl III's bottom ----------

    public static readonly ResourceKind[] Conversions = { ResourceKind.Priest, ResourceKind.Worker, ResourceKind.Coin };

    private Node2D _menu;
    private readonly Sprite2D[] _menuIcons = new Sprite2D[3];

    public bool ConversionsShown => _menu?.Visible ?? false;

    // Shows the menu, each conversion (Conversions' order) at full opacity if it can be made, else faded. doubled: each
    // icon twice, the second behind it ConversionDoubleStep along (the Enlightened's stronghold: conversions doubled).
    public void ShowConversions(IReadOnlyList<bool> possible, bool doubled = false)
    {
        if (_menu is null)
            BuildMenu();
        _menu.Visible = true;
        for (var i = 0; i < _menuIcons.Length; i++)
        {
            _menuIcons[i].Modulate = Colors.White with { A = i < possible.Count && possible[i] ? 1f : ConversionDisabledOpacity };
            var second = _menuIcons[i].GetNodeOrNull<Sprite2D>("Double");
            if (second is null && doubled)
            {
                second = new Sprite2D { Name = "Double", Texture = _menuIcons[i].Texture, ShowBehindParent = true };
                _menuIcons[i].AddChild(second);
            }
            if (second is not null)
            {
                second.Visible = doubled;
                second.Position = ConversionDoubleStep;
                _menuIcons[i].Offset = doubled ? -ConversionDoubleStep / 2 : Vector2.Zero;
                second.Offset = _menuIcons[i].Offset;
            }
        }
    }

    public void HideConversions()
    {
        if (_menu is not null)
            _menu.Visible = false;
    }

    // The conversion whose icon is under the point (made possible or not), or null.
    public ResourceKind? ConversionAt(Vector2 globalPoint)
    {
        if (!ConversionsShown)
            return null;
        Vector2 local = ToLocal(globalPoint);
        for (var i = 0; i < _menuIcons.Length; i++)
            if (new Rect2(_menuIcons[i].Position - Vector2.One * ConversionIconSize / 2f, Vector2.One * ConversionIconSize)
                    .Grow((ConversionSpacing - ConversionIconSize) / 2f).HasPoint(local))
                return Conversions[i];
        return null;
    }

    public bool IsOverConversions(Vector2 globalPoint) => ConversionsShown && MenuRect().HasPoint(ToLocal(globalPoint));

    // In this node's pixels: centred under bowl III, its top on the bowl's bottom.
    private Rect2 MenuRect()
    {
        Rect2 bowl = BowlRect(3);
        var size = new Vector2(2 * ConversionPadding.X + (Conversions.Length - 1) * ConversionSpacing + ConversionIconSize,
            2 * ConversionPadding.Y + ConversionIconSize);
        return new Rect2(new Vector2(bowl.GetCenter().X - size.X / 2, bowl.End.Y), size);
    }

    private void BuildMenu()
    {
        Rect2 rect = MenuRect();
        _menu = Made(new Node2D { Name = "ConversionMenu", ZIndex = ConversionZIndex });
        Vector2[] outline = HighlightArt.RoundRect(rect, ConversionCornerRadius);
        _menu.AddChild(new Polygon2D { Polygon = outline, Color = ConversionFillColour });
        _menu.AddChild(new Line2D { Points = outline.Append(outline[0]).ToArray(), Width = ConversionEdgeWidth, DefaultColor = ConversionEdgeColour });
        for (var i = 0; i < Conversions.Length; i++)
        {
            _menuIcons[i] = new Sprite2D
            {
                Texture = Art.ResourceIcon(Conversions[i], size: ConversionIconSize),
                Position = new Vector2(rect.GetCenter().X + (i - (Conversions.Length - 1) / 2f) * ConversionSpacing, rect.GetCenter().Y),
            };
            _menu.AddChild(_menuIcons[i]);
        }
    }

    private T Made<T>(T node) where T : Node
    {
        node.SetMeta(MadeMeta, true);
        AddChild(node, false, InternalMode.Back);
        return node;
    }
}
