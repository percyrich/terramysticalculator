using System.Linq;
using Godot;

namespace TMCalc;

// The top panel's rules slider, in the tab at the bar's top right: three labels (Strict,
// Lenient, Free; TopPanelArt.RulesLabels) over a groove with a notch under each (both drawn in the bar's backing), and
// a big round stone knob on the groove at the chosen notch (TopPanelArt.Knob). The labels are all alike: only the knob
// shows the choice. Pressing anywhere on the slider brings the knob under the pointer; it follows while held and settles
// on the nearest notch when let go, then ValueChosen fires (if that is a different notch). Setting Value (e.g. from the
// game state after the move, or back again after a refusal) glides the knob there; the first value is shown at once.
// Panel pixels; TopPanel places it on TopPanelArt.BarLayout.Rules.
[GlobalClass]
public partial class TopPanelSlider : Control
{
    [Signal] public delegate void ValueChosenEventHandler(int value);

    public float GlideRate { get; set; } = 17f; // how fast the knob glides to its notch (a quarter of the way a frame at 60 fps)
    public Color LabelColour { get; set; } = new("f7fafc");
    public Color LabelShadow { get; set; } = new(0, 0, 0, 0.6f);
    public float LabelEmbolden { get; set; } = 0.4f; // the default font is semibold; the labels bold

    public int Value
    {
        get => _value;
        set
        {
            _value = Mathf.Clamp(value, 0, Count - 1);
            if (!_shown)
                _at = _value; // the first value at once
            _shown = true;
            SetProcess(true);
        }
    }

    // One per notch, shown while the pointer is over that third of the slider (the Game modes doc's wording).
    public static readonly string[] Tooltips =
    {
        "Plays by the rules: illegal moves and debt are refused.",
        "Plays by the rules, but lets you go into debt with a warning.",
        "Plan ahead: any faction, any number of actions, with normal costs and rewards.",
    };

    public override string _GetTooltip(Vector2 atPosition) => Tooltips[Mathf.RoundToInt(NotchAt(atPosition.X))];

    public Color Frame { get => _frame; set { _frame = value; QueueRedraw(); } }
    public bool Dragging => _drag;

    private static int Count => TopPanelArt.RulesLabels.Length;
    private int _value, _labelSize = (int)TopPanelArt.RulesLabelSize;
    private float _at;   // where the knob is drawn: 0 at the first notch, Count − 1 at the last
    private bool _drag, _shown;
    private Color _frame = IconArt.PowerPurple;
    private readonly Label[] _labels;

    public TopPanelSlider()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        _labels = TopPanelArt.RulesLabels.Select(text => new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        }).ToArray();
        foreach (Label label in _labels)
            AddChild(label);
        Restyle();
        Resized += Relayout;
    }

    // Laid out whenever the size changes, and on entering the tree: a size set before then sends no Resized, which had
    // left all three labels on top of each other at the top left (size zero).
    public override void _Notification(int what)
    {
        if (what == NotificationEnterTree)
            Relayout();
    }

    private float NotchAt(float x)
    {
        float first = TopPanelArt.NotchX(Size, 0), step = TopPanelArt.NotchX(Size, 1) - first;
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
                    _at = NotchAt(button.Position.X);
                }
                else if (_drag)
                {
                    _drag = false;
                    int chosen = Mathf.RoundToInt(_at);
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
                _at = NotchAt(motion.Position.X);
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_drag)
        {
            _at = Mathf.Lerp(_at, _value, 1 - Mathf.Exp(-GlideRate * (float)delta));
            if (Mathf.Abs(_at - _value) < 0.002f)
            {
                _at = _value;
                SetProcess(false);
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Texture2D knob = TopPanelArt.Knob(_drag, _frame);
        if (knob is null)
            return;
        var centre = new Vector2(TopPanelArt.NotchX(Size, _at), TopPanelArt.KnobY(Size));
        DrawTexture(knob, centre - Vector2.One * (TopPanelArt.KnobRadius + TopPanelArt.KnobMargin));
    }

    private void Relayout()
    {
        float step = TopPanelArt.NotchX(Size, 1) - TopPanelArt.NotchX(Size, 0), y = TopPanelArt.LabelY(Size), h = _labelSize * 1.5f;
        for (var i = 0; i < _labels.Length; i++)
        {
            _labels[i].Position = new Vector2(TopPanelArt.NotchX(Size, i) - step / 2, y - h / 2);
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
        foreach (Label label in _labels ?? System.Array.Empty<Label>())
            label.LabelSettings = settings;
        if (_labels is not null)
            Relayout();
    }
}
