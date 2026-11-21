using Godot;

namespace TMCalc;

// One of the top panel's raised stone keys (TopPanelArt.Key): lighter under the pointer, pressed in while held, flat
// and dim when Disabled. On: outlined and glowing in the frame colour (the log or the bookmark map open). Filled: the
// bookmark drawn in gold (this position is bookmarked). A BaseButton, so Pressed, Disabled and TooltipText work as
// usual and the board's click handling treats it as a button. TopPanelArt.KeySide square, in panel pixels.
[GlobalClass]
public partial class TopPanelKey : BaseButton
{
    public TopPanelArt.KeyKind Kind { get => _kind; set { _kind = value; QueueRedraw(); } }
    public bool On { get => _on; set { _on = value; QueueRedraw(); } }
    public bool Filled { get => _filled; set { _filled = value; QueueRedraw(); } }
    public Color Frame { get => _frame; set { _frame = value; QueueRedraw(); } }

    private TopPanelArt.KeyKind _kind;
    private bool _on, _filled;
    private Color _frame = IconArt.PowerPurple;

    public TopPanelKey()
    {
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(TopPanelArt.KeySide, TopPanelArt.KeySide);
        Size = CustomMinimumSize;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
            QueueRedraw();
    }

    public override void _Pressed() => QueueRedraw();

    public override void _Toggled(bool on) => QueueRedraw();

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton)
            QueueRedraw(); // pressed in and out
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
        Texture2D texture = TopPanelArt.Key(_kind, look, _on, _frame, _filled);
        if (texture is not null)
            DrawTexture(texture, -Vector2.One * TopPanelArt.KeyMargin);
    }
}
