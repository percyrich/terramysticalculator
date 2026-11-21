using Godot;

namespace TMCalc;

// A stone key with a word on it instead of an icon (the bookmark map's Save, Save as, Load, Mark as game line, Back to
// game line): TopPanelKey's look (TopPanelArt.TextKey: lighter under the pointer, pressed in while held, flat and dim
// when Disabled), the word white and bold with a soft shadow, as the rules slider's labels. A BaseButton, so Pressed,
// Disabled and TooltipText work as usual. Size it like any Control (panel pixels).
[GlobalClass]
public partial class TopPanelTextKey : BaseButton
{
    public string Text { get => _label.Text; set => _label.Text = value ?? ""; }
    public int FontSize { get => _fontSize; set { _fontSize = value; Restyle(); } }

    private int _fontSize = 19;
    private readonly Label _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        ClipText = true,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    public TopPanelTextKey()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddChild(_label);
        Restyle();
        Resized += () => QueueRedraw();
    }

    public override void _Draw()
    {
        TopPanelArt.KeyLook look = Disabled ? TopPanelArt.KeyLook.Disabled : GetDrawMode() switch
        {
            DrawMode.Pressed or DrawMode.HoverPressed => TopPanelArt.KeyLook.Pressed,
            DrawMode.Hover => TopPanelArt.KeyLook.Hover,
            _ => TopPanelArt.KeyLook.Normal,
        };
        MouseDefaultCursorShape = Disabled ? CursorShape.Arrow : CursorShape.PointingHand;
        if (TopPanelArt.TextKey(look, Size) is Texture2D texture)
            DrawTexture(texture, -Vector2.One * TopPanelArt.KeyMargin);
        float drop = look == TopPanelArt.KeyLook.Pressed ? 1.5f : 0;
        _label.Position = new Vector2(6, drop);
        _label.Size = Size - new Vector2(12, 0);
        _label.Modulate = new Color(1, 1, 1, Disabled ? 0.4f : 1);
    }

    private void Restyle() => _label.LabelSettings = new LabelSettings
    {
        Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.4f },
        FontSize = _fontSize,
        FontColor = new Color("f7fafc"),
        ShadowColor = new Color(0, 0, 0, 0.6f),
        ShadowSize = 2,
        ShadowOffset = new Vector2(0, 1),
    };
}
