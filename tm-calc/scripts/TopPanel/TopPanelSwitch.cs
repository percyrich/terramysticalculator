using System.Linq;
using Godot;

namespace TMCalc;

// A two-way switch in the top panel's style (the rules slider, TopPanelSlider, with two notches): a label over each
// end of a groove (TopPanelArt.SliderGroove, drawn here) and the big round stone knob (TopPanelArt.Knob) at the chosen
// end. A click anywhere on it changes sides (or the knob can be dragged and let go nearer the other end); ValueChosen
// fires with the new side (0 the left, 1 the right). Setting Value glides the knob there (the first value at once).
// Tooltips: one per side, shown over that half. Sized like any Control; panel pixels.
//   var sw = new TopPanelSwitch { Labels = new[] { "13-top", "12-top" }, Size = new Vector2(260, 60) };
//   sw.ValueChosen += side => …;
[GlobalClass]
public partial class TopPanelSwitch : Control
{
    [Signal] public delegate void ValueChosenEventHandler(int value);

    public float GlideRate { get; set; } = 17f;
    public Color LabelColour { get; set; } = new("f7fafc");
    public Color LabelShadow { get; set; } = new(0, 0, 0, 0.6f);
    public float LabelEmbolden { get; set; } = 0.4f;
    public string[] Tooltips { get; set; } = { "", "" };

    public string[] Labels
    {
        get => _labels.Select(label => label.Text).ToArray();
        set
        {
            for (var i = 0; i < Count; i++)
                _labels[i].Text = value is not null && i < value.Length ? value[i] : "";
        }
    }

    public int LabelSize { get => _labelSize; set { _labelSize = value; Restyle(); } }

    public int Value
    {
        get => _value;
        set
        {
            _value = Mathf.Clamp(value, 0, Count - 1);
            if (!_shown)
                _at = _value;
            _shown = true;
            SetProcess(true);
        }
    }

    public Color Frame { get => _frame; set { _frame = value; QueueRedraw(); } }

    private const int Count = 2;
    private int _value, _labelSize = (int)TopPanelArt.RulesLabelSize;
    private float _at, _pressedAt;
    private bool _drag, _moved, _shown;
    private Color _frame = IconArt.PowerPurple;
    private readonly Label[] _labels = new Label[Count];

    public TopPanelSwitch()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        for (var i = 0; i < Count; i++)
        {
            _labels[i] = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(_labels[i]);
        }
        Restyle();
        Resized += Relayout;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationEnterTree)
            Relayout();
    }

    public override string _GetTooltip(Vector2 atPosition) => Tooltips?[Mathf.RoundToInt(NotchAt(atPosition.X))] ?? "";

    private float NotchAt(float x)
    {
        float first = TopPanelArt.NotchX(Size, 0, Count), step = TopPanelArt.NotchX(Size, 1, Count) - first;
        return step <= 0 ? 0 : Mathf.Clamp((x - first) / step, 0, Count - 1);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _drag = true;
                    _moved = false;
                    _pressedAt = button.Position.X;
                }
                else if (_drag)
                {
                    _drag = false;
                    // A click changes sides; a drag goes to the nearer end.
                    int chosen = _moved ? Mathf.RoundToInt(_at) : 1 - _value;
                    bool changed = chosen != _value;
                    _value = chosen;
                    if (changed)
                        EmitSignal(SignalName.ValueChosen, chosen);
                }
                SetProcess(true);
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _drag:
                if (Mathf.Abs(motion.Position.X - _pressedAt) > 4)
                    _moved = true;
                if (_moved)
                    _at = NotchAt(motion.Position.X);
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_drag || !_moved)
        {
            _at = Mathf.Lerp(_at, _value, 1 - Mathf.Exp(-GlideRate * (float)delta));
            if (!_drag && Mathf.Abs(_at - _value) < 0.002f)
            {
                _at = _value;
                SetProcess(false);
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (TopPanelArt.SliderGroove(Size, Count) is Texture2D groove)
            DrawTexture(groove, Vector2.Zero);
        Texture2D knob = TopPanelArt.Knob(_drag, _frame);
        if (knob is null)
            return;
        var centre = new Vector2(TopPanelArt.NotchX(Size, _at, Count), TopPanelArt.KnobY(Size));
        DrawTexture(knob, centre - Vector2.One * (TopPanelArt.KnobRadius + TopPanelArt.KnobMargin));
    }

    private void Relayout()
    {
        float step = TopPanelArt.NotchX(Size, 1, Count) - TopPanelArt.NotchX(Size, 0, Count), y = TopPanelArt.LabelY(Size), h = _labelSize * 1.5f;
        for (var i = 0; i < Count; i++)
        {
            _labels[i].Position = new Vector2(TopPanelArt.NotchX(Size, i, Count) - step / 2, y - h / 2);
            _labels[i].Size = new Vector2(step, h);
        }
        QueueRedraw();
    }

    private void Restyle()
    {
        var settings = new LabelSettings
        {
            Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = LabelEmbolden },
            FontSize = _labelSize,
            FontColor = LabelColour,
            ShadowColor = LabelShadow,
            ShadowSize = 2,
            ShadowOffset = new Vector2(0, 1),
        };
        foreach (Label label in _labels)
            if (label is not null)
                label.LabelSettings = settings;
        Relayout();
    }
}
