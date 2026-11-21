using Godot;

namespace TMCalc;

// A tab key of EndgameScores: a stone key (TopPanelArt.TextKey: lighter under the pointer, pressed in while selected)
// with its icons on it (Kind 0: VP; 1: the action octagon; 2: coin, worker, priest and power), ringed in the frame
// colour while selected.
public partial class EndgameTab : BaseButton
{
    public int Kind { get; set; }
    public int IconSize { get => _iconSize; set { _iconSize = value; QueueRedraw(); } }
    public Color Frame { get => _frame; set { _frame = value; QueueRedraw(); } }
    public bool Selected { get => _selected; set { _selected = value; QueueRedraw(); } }

    private int _iconSize = 46;
    private Color _frame = IconArt.PowerPurple;
    private bool _selected;

    public EndgameTab()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
    }

    public override void _Draw()
    {
        TopPanelArt.KeyLook look = _selected ? TopPanelArt.KeyLook.Pressed : IsHovered() ? TopPanelArt.KeyLook.Hover : TopPanelArt.KeyLook.Normal;
        if (TopPanelArt.TextKey(look, Size) is Texture2D key)
            DrawTexture(key, -Vector2.One * TopPanelArt.KeyMargin);
        if (_selected)
        {
            var ring = new StyleBoxFlat { DrawCenter = false, BorderColor = _frame };
            ring.SetBorderWidthAll(3);
            ring.SetCornerRadiusAll(Mathf.RoundToInt(TopPanelArt.KeyRadius));
            DrawStyleBox(ring, new Rect2(Vector2.Zero, Size));
        }
        float drop = _selected ? 1.5f : 0;
        Icon[] icons = Kind switch
        {
            0 => new[] { Icon.Points },
            1 => new[] { Icon.ActionOctagon },
            _ => new[] { Icon.Coin, Icon.Worker, Icon.Priest, Icon.PowerToken },
        };
        int size = icons.Length == 1 ? _iconSize : Mathf.RoundToInt(_iconSize * 0.62f);
        float step = size * 0.62f, width = step * (icons.Length - 1) + size;
        for (var i = 0; i < icons.Length; i++)
        {
            Texture2D texture = IconArt.Texture(icons[i], size);
            var centre = new Vector2((Size.X - width) / 2 + size / 2f + i * step, Size.Y / 2 + drop);
            DrawTexture(texture, centre - texture.GetSize() / 2);
        }
    }
}
