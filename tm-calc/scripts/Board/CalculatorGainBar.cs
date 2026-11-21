using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// Calculator: the +1 bar over the active player's board, directly above its resources: a coin, a worker, a priest (in
// the faction's colour) and a power gain, each where that income's icon is on the board, with "+1" written over it, in
// a bar like the board (the frame in the faction's colour round the slate stone). Calculator Strict: the power alone.
// The cell under the mouse is lit; a click gives that resource (Main: CalculatorGain, MainCalculatorGain.cs). A resource
// that can't be given now (no priest left, all power in bowl III) is faded.
// Drawn in the panel's pixels (Main places it at the panel's position and scale). Made in code.
public partial class CalculatorGainBar : Node2D
{
    [Export] public int FontSize { get; set; } = 40;
    [Export] public int OutlineSize { get; set; } = 10;
    [Export] public float Margin { get; set; } = 14f;          // round the icons, inside the frame
    [Export] public float DisabledOpacity { get; set; } = 0.35f;

    public static readonly GainKind[] Order = { GainKind.Coin, GainKind.Worker, GainKind.Priest, GainKind.Power };

    private readonly List<(GainKind Kind, Vector2 Centre)> _cells = new();
    private readonly HashSet<GainKind> _enabled = new();
    private Rect2 _bar;
    private float _cellWidth;
    private int _iconSize = 64;
    private Color _frame = Colors.White;
    private Texture2D _priest, _backing;
    private GainKind? _hover;

    // cells: each resource shown and its icon's centre (in the panel's pixels); bar: the bar's rectangle; the faction's
    // colours; what can be given now.
    public void ShowFor(IReadOnlyList<(GainKind Kind, Vector2 Centre)> cells, Rect2 bar, int iconSize, Color frame, Texture2D priest,
        IEnumerable<GainKind> enabled)
    {
        _cells.Clear();
        _cells.AddRange(cells);
        _cellWidth = cells.Count > 1 ? Mathf.Abs(cells[1].Centre.X - cells[0].Centre.X) : iconSize * 1.4f;
        _bar = bar;
        _iconSize = iconSize;
        if (frame != _frame || _backing is null || _backing.GetSize() != bar.Size)
            _backing = FactionBoardArt.FramedStone(frame, bar.Size);
        _frame = frame;
        _priest = priest;
        _enabled.Clear();
        _enabled.UnionWith(enabled);
        QueueRedraw();
    }

    private Rect2 Cell(Vector2 centre) => new(new Vector2(centre.X - _cellWidth / 2, _bar.Position.Y), new Vector2(_cellWidth, _bar.Size.Y));

    // The resource under the point (only one that can be given now).
    public GainKind? KindAt(Vector2 globalPoint)
    {
        if (!IsVisibleInTree())
            return null;
        Vector2 local = ToLocal(globalPoint);
        foreach (var (kind, centre) in _cells)
            if (Cell(centre).HasPoint(local))
                return _enabled.Contains(kind) ? kind : null;
        return null;
    }

    public override void _Process(double delta)
    {
        GainKind? hover = IsVisibleInTree() ? KindAt(GetGlobalMousePosition()) : null;
        if (hover == _hover)
            return;
        _hover = hover;
        QueueRedraw();
    }

    private Texture2D TextureOf(GainKind kind, int size) => kind switch
    {
        GainKind.Coin => IconArt.Texture(Icon.Coin, size),
        GainKind.Worker => IconArt.Texture(Icon.Worker, size),
        GainKind.Priest => _priest,
        _ => IconArt.Texture(Icon.PowerGain, size),
    };

    public override void _Draw()
    {
        if (_backing is not null)
            DrawTexture(_backing, _bar.Position);
        Font font = ThemeDB.FallbackFont;
        const string plusOne = "+1";
        Vector2 textSize = font.GetStringSize(plusOne, HorizontalAlignment.Left, -1, FontSize);
        foreach (var (kind, centre) in _cells)
        {
            if (_hover == kind)
            {
                var lit = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.16f), CornerDetail = 8 };
                lit.SetCornerRadiusAll(10);
                DrawStyleBox(lit, Cell(centre).Grow(-Margin * 0.5f));
            }
            var tint = new Color(1, 1, 1, _enabled.Contains(kind) ? 1f : DisabledOpacity);
            Texture2D texture = TextureOf(kind, _iconSize);
            Vector2 size = texture.GetSize();
            if (kind == GainKind.Priest) // a piece texture: fitted into the icon's square
                size *= _iconSize * 6f / 7f / Mathf.Max(size.X, size.Y);
            DrawTextureRect(texture, new Rect2(centre - size / 2, size), false, tint);
            // "+1" over the icon, as a count is written on one
            float baseline = centre.Y - font.GetHeight(FontSize) / 2 + font.GetAscent(FontSize);
            var at = new Vector2(centre.X - textSize.X / 2, baseline);
            DrawStringOutline(font, at, plusOne, HorizontalAlignment.Left, -1, FontSize, OutlineSize, new Color(TextStyle.Outline, tint.A));
            DrawString(font, at, plusOne, HorizontalAlignment.Left, -1, FontSize, new Color(TextStyle.Text, tint.A));
        }
    }
}
