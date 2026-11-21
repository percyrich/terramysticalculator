using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// The player button: on the active player's panel in place of End turn. A white disc, a soft
// dome, in a ring of the faction's colour (lit lighter at the top, darker at the bottom), with a dark line outside the
// ring and between it and the disc. Its marks, in ink:
//   Blank     nothing: the turn has no action yet;
//   Waiting   three dots, popping in one after another, then pulsing in a slow wave: an action is under way (a favour,
//             spades, a bridge, a town, power offers still to settle);
//   Confirm   a tick, the ring glowing softly in the faction's colour: the turn can end. Only now can it be pressed
//             (it is Disabled otherwise); hovered it lifts a little, pressed it sinks, and Pressed ends the turn.
// Changes are animated: the dots slide together and vanish as the tick draws itself; going back, the tick erases itself
// and the dots pop in again. Hidden: the button shrinks and fades away (the turn has ended), and grows in again when
// next shown. A BaseButton, so the board's click handling treats it as a button (GuiGetHoveredControl).
//   button.Show(PlayerButton.Mode.Waiting, colour, animate: true)
[GlobalClass]
public partial class PlayerButton : BaseButton
{
    public enum Mode { Hidden, Blank, Waiting, Confirm }

    [ExportGroup("Look")]
    [Export] public float Diameter { get => _diameter; set { _diameter = value; Resize(); } }
    [Export] public float RingWidth { get; set; } = 10f;
    [Export] public bool GlowWhenReady { get; set; } = true;
    [Export] public bool MarksInFactionColour { get; set; }   // dots and tick in the ring's colour, with a dark edge
    [Export] public Color InkColour { get; set; } = new("23292b");
    [Export] public Color LineColour { get; set; } = new("161a18");
    [Export] public float HoverScale { get; set; } = 1.04f;
    [Export] public float PressScale { get; set; } = 0.97f;
    [ExportGroup("Timing (seconds)")]
    [Export] public float PopSeconds { get; set; } = 0.22f;     // each dot popping in
    [Export] public float StaggerSeconds { get; set; } = 0.11f; // from one dot to the next
    [Export] public float GatherSeconds { get; set; } = 0.18f;  // the dots sliding together before the tick
    [Export] public float DrawSeconds { get; set; } = 0.26f;    // the tick drawing itself
    [Export] public float UndrawSeconds { get; set; } = 0.16f;  // the tick erasing itself
    [Export] public float LeaveSeconds { get; set; } = 0.22f;   // shrinking away when hidden, and growing in again
    [Export] public float PulseSeconds { get; set; } = 1.1f;    // the dots' wave
    [Export] public float GlowSeconds { get; set; } = 1.6f;     // the glow breathing in and out
    [ExportGroup("")]

    // The tick: a short stroke down to the bottom, a long one up to the right, in fractions of the disc's radius.
    private static readonly Vector2[] Tick = { new(-0.42f, 0.02f), new(-0.12f, 0.32f), new(0.45f, -0.3f) };

    private float _diameter = 84f;
    private Mode _mode = Mode.Blank, _from = Mode.Blank;   // the marks shown, and what they are changing from
    private double _since = -100;                            // when that change began
    private double? _leaving, _arriving;                     // when the button began to shrink away, or to grow in
    private bool _shown;
    private Color _colour = Colors.White;

    private static GradientTexture2D _soft, _dome, _domeLit;

    public Mode Shown => _shown ? _mode : Mode.Hidden;
    private static double Now => Time.GetTicksMsec() / 1000.0;

    public PlayerButton()
    {
        FocusMode = FocusModeEnum.None;
        Disabled = true;
        Visible = false;
        Resize();
    }

    // Shows the button in this state, its ring in `colour`; animate: changes animated (else at once).
    public void Show(Mode mode, Color colour, bool animate)
    {
        _colour = colour;
        double now = Now;
        Disabled = mode != Mode.Confirm;
        MouseDefaultCursorShape = mode == Mode.Confirm ? CursorShape.PointingHand : CursorShape.Arrow;
        if (!animate)
        {
            _shown = mode != Mode.Hidden;
            Visible = _shown;
            if (_shown)
                _mode = _from = mode;
            _since = now - 100;
            _leaving = _arriving = null;
            QueueRedraw();
            return;
        }
        if (mode == Mode.Hidden)
        {
            if (_shown && _leaving is null)
                _leaving = now; // shrinks away from what it shows now (then Visible = false)
            _shown = false;
            return;
        }
        if (!_shown)
        {
            // Growing in: already showing its marks (dots or tick), as they are what the turn is at.
            _shown = true;
            Visible = true;
            _leaving = null;
            _arriving = now;
            _mode = _from = mode;
            _since = now - 100;
        }
        else if (mode != _mode)
        {
            _from = _mode;
            _mode = mode;
            _since = now;
        }
        QueueRedraw();
    }

    public override bool _HasPoint(Vector2 point) => point.DistanceTo(Size / 2) <= _diameter / 2;

    public override void _Process(double delta)
    {
        if (_leaving is double left && Now - left >= LeaveSeconds)
        {
            _leaving = null;
            Visible = false;
        }
        if (_arriving is double arrived && Now - arrived >= LeaveSeconds)
            _arriving = null;
        if (Visible)
            QueueRedraw(); // the dots' wave, the glow, the changes
    }

    private void Resize()
    {
        CustomMinimumSize = new Vector2(_diameter, _diameter);
        Size = CustomMinimumSize;
    }

    // ---------- Drawing ----------

    private static float Clamp01(float v) => Mathf.Clamp(v, 0f, 1f);
    private static float EaseOut(float p) => 1 - Mathf.Pow(1 - p, 3);
    private static float EaseIn(float p) => p * p * p;
    private static float EaseBack(float p) { const float c = 1.7f; return 1 + (c + 1) * Mathf.Pow(p - 1, 3) + c * Mathf.Pow(p - 1, 2); }

    // What the marks look like `t` seconds after changing from `from` to `mode`: each dot's size (0–1) and its place
    // along the row (1 its own place, 0 the middle), and how much of the tick is drawn (0–1).
    private (float Grow, float Spread)[] Marks(Mode mode, Mode from, float t, out float tick)
    {
        var dots = new (float Grow, float Spread)[] { (0, 1), (0, 1), (0, 1) };
        tick = 0;
        void PopIn(float start)
        {
            for (var i = 0; i < 3; i++)
                dots[i].Grow = EaseBack(Clamp01((t - start - i * StaggerSeconds) / PopSeconds));
        }
        switch (mode)
        {
            case Mode.Waiting when from == Mode.Waiting:
                for (var i = 0; i < 3; i++)
                    dots[i].Grow = 1;
                break;
            case Mode.Waiting when from == Mode.Confirm:
                tick = 1 - EaseIn(Clamp01(t / UndrawSeconds));
                PopIn(UndrawSeconds);
                break;
            case Mode.Waiting:
                PopIn(0);
                break;
            case Mode.Confirm when from == Mode.Waiting:
            {
                float g = EaseIn(Clamp01(t / GatherSeconds));
                for (var i = 0; i < 3; i++)
                    dots[i] = (1 - g, 1 - g);
                tick = EaseOut(Clamp01((t - GatherSeconds) / DrawSeconds));
                break;
            }
            case Mode.Confirm:
                tick = from == Mode.Confirm ? 1 : EaseOut(Clamp01(t / DrawSeconds));
                break;
            case Mode.Blank when from == Mode.Waiting:
            {
                float g = EaseIn(Clamp01(t / GatherSeconds));
                for (var i = 0; i < 3; i++)
                    dots[i].Grow = 1 - g;
                break;
            }
            case Mode.Blank when from == Mode.Confirm:
                tick = 1 - EaseIn(Clamp01(t / UndrawSeconds));
                break;
        }
        return dots;
    }

    public override void _Draw()
    {
        double now = Now;
        float t = (float)(now - _since), scale = 1, alpha = 1;
        if (_leaving is double left)
        {
            float p = EaseIn(Clamp01((float)(now - left) / LeaveSeconds));
            scale = 1 - 0.35f * p;
            alpha = 1 - p;
        }
        else if (_arriving is double arrived)
        {
            float p = EaseOut(Clamp01((float)(now - arrived) / LeaveSeconds));
            scale = 0.65f + 0.35f * p;
            alpha = p;
        }
        bool ready = _mode == Mode.Confirm && _leaving is null && t >= (_from == Mode.Waiting ? GatherSeconds : 0);
        DrawMode state = GetDrawMode();
        bool hover = state is DrawMode.Hover or DrawMode.HoverPressed or DrawMode.Pressed, pressed = state is DrawMode.Pressed or DrawMode.HoverPressed;
        float lift = ready && hover ? (pressed ? PressScale : HoverScale) : 1;
        DrawSetTransform(Size / 2, 0, Vector2.One * scale * lift);
        Color Faded(Color c) => new(c, c.A * alpha);

        float r = _diameter / 2, ri = r - RingWidth;
        // shadow, and the glow when the turn can end, breathing
        DrawTextureRect(Soft, CentredRect(new Vector2(0, r * (pressed && ready ? 0.03f : 0.07f)), r * 2.5f), false, Faded(new Color(0, 0, 0, 0.5f)));
        if (ready && GlowWhenReady)
        {
            float b = 0.5f + 0.5f * Mathf.Sin((float)(now / GlowSeconds) * Mathf.Tau);
            DrawTextureRect(Soft, CentredRect(Vector2.Zero, r * (2.5f + 0.5f * b)), false, Faded(new Color(_colour, 0.55f + 0.35f * b)));
        }
        // the ring, lit from above; the disc, a soft dome lit at the top left (brighter while a tick is hovered)
        Vector2[] outer = CirclePoints(r), inner = CirclePoints(ri);
        Color top = IconArt.Shade(_colour, 0.16f), bottom = IconArt.Shade(_colour, -0.16f);
        DrawPolygon(outer, outer.Select(p => Faded(top.Lerp(bottom, p.Y / (2 * r) + 0.5f))).ToArray());
        DrawPolygon(inner, new[] { Faded(Colors.White) }, inner.Select(p => p / (2 * ri) + new Vector2(0.5f, 0.5f)).ToArray(), ready && hover ? DomeLit : Dome);
        DrawArc(Vector2.Zero, ri, 0, Mathf.Tau, 96, Faded(LineColour), Mathf.Max(1, r * 0.035f), true);
        float edge = Mathf.Max(1.5f, r * 0.05f);
        DrawArc(Vector2.Zero, r - edge / 2, 0, Mathf.Tau, 96, Faded(LineColour), edge, true);

        // the marks
        Color ink = MarksInFactionColour ? _colour : InkColour;
        var dots = Marks(_mode, _from, t, out float tick);
        float dotR = ri * 0.13f, gap = ri * 0.42f;
        bool settled = _mode == Mode.Waiting && t > PopSeconds + 2 * StaggerSeconds;
        for (var i = 0; i < 3; i++)
        {
            if (dots[i].Grow <= 0.001f)
                continue;
            float wave = settled ? 0.5f + 0.5f * Mathf.Sin(((float)(now / PulseSeconds) - i * 0.18f) * Mathf.Tau) : 1;
            float radius = dotR * dots[i].Grow * (settled ? 0.8f + 0.3f * wave : 1);
            var at = new Vector2((i - 1) * gap * dots[i].Spread, 0);
            if (MarksInFactionColour)
                DrawCircle(at, radius + Mathf.Max(1, r * 0.03f), Faded(LineColour), true, -1, true);
            DrawCircle(at, radius, Faded(new Color(ink, ink.A * (settled ? 0.55f + 0.45f * wave : 1))), true, -1, true);
        }
        if (tick > 0.001f)
        {
            List<Vector2> drawn = TickUpTo(tick, ri);
            float w = ri * 0.19f;
            if (MarksInFactionColour)
                RoundStroke(drawn, Faded(LineColour), w + Mathf.Max(2, r * 0.06f));
            RoundStroke(drawn, Faded(ink), w);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // The tick's points from its start to `part` (0–1) of its length, for a disc of radius `ri`.
    private static List<Vector2> TickUpTo(float part, float ri)
    {
        float total = 0;
        for (var i = 1; i < Tick.Length; i++)
            total += Tick[i].DistanceTo(Tick[i - 1]);
        float left = total * part;
        var points = new List<Vector2> { Tick[0] * ri };
        for (var i = 1; i < Tick.Length && left > 0; i++)
        {
            float length = Tick[i].DistanceTo(Tick[i - 1]);
            points.Add(Tick[i - 1].Lerp(Tick[i], Mathf.Min(1, left / length)) * ri);
            left -= length;
        }
        return points;
    }

    // A line with round ends and joins.
    private void RoundStroke(List<Vector2> points, Color colour, float width)
    {
        if (points.Count > 1)
            DrawPolyline(points.ToArray(), colour, width, true);
        foreach (Vector2 p in points)
            DrawCircle(p, width / 2, colour, true, -1, true);
    }

    private static Vector2[] CirclePoints(float radius) =>
        Enumerable.Range(0, 96).Select(i => Vector2.FromAngle(i / 96f * Mathf.Tau) * radius).ToArray();

    private static Rect2 CentredRect(Vector2 centre, float size) => new(centre - Vector2.One * size / 2, Vector2.One * size);

    // A white disc fading out over its outer part (shadow and glow, tinted by the modulate colour).
    private static GradientTexture2D Soft => _soft ??= new GradientTexture2D
    {
        Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient { Offsets = new[] { 0f, 0.7f, 1f }, Colors = new[] { Colors.White, Colors.White, new Color(1, 1, 1, 0) } },
    };

    // The disc's dome: white at the top left, grey-blue at the rim.
    private static GradientTexture2D Dome => _dome ??= DomeTexture(new Color("d9e0e5"));
    private static GradientTexture2D DomeLit => _domeLit ??= DomeTexture(new Color("e9eef2"));
    private static GradientTexture2D DomeTexture(Color rim) => new()
    {
        Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.33f, 0.28f), FillTo = new Vector2(1f, 0.6f),
        Gradient = new Gradient { Offsets = new[] { 0.1f, 1f }, Colors = new[] { Colors.White, rim } },
    };
}
