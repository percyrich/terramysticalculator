using Godot;

namespace TMCalc;

// A tick box in the top panel's style (the game screen's options): a small raised stone key (TopPanelArt.TextKey's
// look, lighter under the pointer) with a white tick on it while on, then its words in the top panel's label style
// (white, bold, soft shadow). Disabled: the key flat and dim, the words and any tick faded. A toggle BaseButton, so
// ButtonPressed, Toggled, Disabled and TooltipText work as usual; clicking the words toggles it too. Sized by its
// minimum size (the box, the gap, the words) unless set larger; panel pixels.
[GlobalClass]
public partial class TopPanelCheck : BaseButton
{
    public string Text { get => _label.Text; set { _label.Text = value ?? ""; UpdateMinimumSize(); QueueRedraw(); } }
    public int FontSize { get => _fontSize; set { _fontSize = value; Restyle(); } }
    public float BoxSize { get => _box; set { _box = value; UpdateMinimumSize(); QueueRedraw(); } }

    private int _fontSize = 30;
    private float _box = 36f;
    private const float Gap = 12f; // between the box and the words
    private readonly Label _label = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    public TopPanelCheck()
    {
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddChild(_label);
        Restyle();
        Toggled += _ => QueueRedraw();
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
        Resized += QueueRedraw;
    }

    public override Vector2 _GetMinimumSize()
    {
        float text = _label.LabelSettings?.Font is Font font ? font.GetStringSize(_label.Text, HorizontalAlignment.Left, -1, _fontSize).X : 0;
        return new Vector2(_box + Gap + text + 4, Mathf.Max(_box, _fontSize * 1.4f));
    }

    public override void _Draw()
    {
        TopPanelArt.KeyLook look = Disabled ? TopPanelArt.KeyLook.Disabled
            : IsHovered() ? TopPanelArt.KeyLook.Hover : TopPanelArt.KeyLook.Normal;
        MouseDefaultCursorShape = Disabled ? CursorShape.Arrow : CursorShape.PointingHand;
        var at = new Vector2(0, (Size.Y - _box) / 2);
        if (TopPanelArt.TextKey(look, Vector2.One * _box) is Texture2D key)
            DrawTexture(key, at - Vector2.One * TopPanelArt.KeyMargin);
        if (ButtonPressed)
        {
            Vector2 P(float u, float v) => at + new Vector2(u, v) * _box;
            var tick = new[] { P(0.24f, 0.52f), P(0.43f, 0.70f), P(0.77f, 0.30f) };
            float alpha = Disabled ? 0.4f : 1f;
            DrawPolyline(tick, new Color(0, 0, 0, 0.5f * alpha), _box * 0.17f, true);
            DrawPolyline(tick, new Color(1, 1, 1, alpha), _box * 0.11f, true);
        }
        _label.Position = new Vector2(_box + Gap, 0);
        _label.Size = new Vector2(Mathf.Max(0, Size.X - _box - Gap), Size.Y);
        _label.Modulate = new Color(1, 1, 1, Disabled ? 0.4f : 1f);
    }

    private void Restyle()
    {
        _label.LabelSettings = new LabelSettings
        {
            Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.4f },
            FontSize = _fontSize,
            FontColor = new Color("f7fafc"),
            ShadowColor = new Color(0, 0, 0, 0.6f),
            ShadowSize = 2,
            ShadowOffset = new Vector2(0, 1),
        };
        UpdateMinimumSize();
        QueueRedraw();
    }
}
