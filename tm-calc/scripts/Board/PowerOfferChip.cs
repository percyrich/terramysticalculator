using System;
using Core;
using Godot;

namespace TMCalc;

// A clickable chip: a small dark disc with something inside (a ResourceBundle), ringed by the same glow as the power
// offers' edges, in a player's colour, with a glint running round it. Used for:
//   the first waiting power offer, on the map (MapView places it over the middle of the offer's glowing edges; it
//     shows the power on offer, and clicking it accepts the offer: Main); it sits above the pieces;
//   the Shapeshifters' choices on their panel: how to pay for their shift, and the token offered for 1 VP.
// A chip that can't be chosen now is drawn greyed out, without its glint.
public partial class PowerOfferChip : Node2D
{
    private static readonly Color Fill = new(0.055f, 0.055f, 0.055f, 0.86f);
    private static readonly Color DisabledTint = new(0.5f, 0.5f, 0.5f);

    private ResourceBundle _bundle;
    private Sprite2D _overlay;
    private Color _colour = Colors.White;
    private float _radius = 31f;
    private float _shown;
    private bool _enabled = true;

    // The disc's radius, in its parent's pixels.
    public float Radius => _radius;

    public override void _Ready()
    {
        ZIndex = 40; // above the pieces on the map
        _bundle = new ResourceBundle { Centred = true, FontColour = Colors.White, OutlineColour = Colors.Black }; // on the dark disc
        AddChild(_bundle);
        _overlay = new Sprite2D { Name = "Overlay", Visible = false, ZIndex = 1 };
        AddChild(_overlay);
        SetProcess(false);
    }

    // Shows `power` in this colour; hexRadius: the map's hex corner radius (everything is sized from it).
    public void ShowOffer(int power, Color colour, float hexRadius) =>
        ShowContent(Amounts.Of(ResourceKind.Power, power), colour, hexRadius * 0.42f,
            Mathf.RoundToInt(hexRadius * 0.55f), Mathf.RoundToInt(hexRadius * 0.4f));

    // Shows any amount: the disc `radius` across, its icons iconSize and numbers fontSize. enabled: false greys it out.
    // repeat: each icon drawn as many times as its count (repeatSpacing apart) instead of a number; plusSigns: "+2";
    // overlay: a picture over the whole content (e.g. IconArt's PowerCross over tokens).
    public void ShowContent(Amounts content, Color colour, float radius, int iconSize, int fontSize, bool enabled = true,
        bool repeat = false, float repeatSpacing = 0f, bool plusSigns = false, Texture2D overlay = null)
    {
        bool fresh = !Visible || colour != _colour;
        _colour = colour;
        _radius = radius;
        _enabled = enabled;
        _bundle.IconSize = iconSize;
        _bundle.FontSize = fontSize;
        _bundle.OutlineSize = Mathf.Max(4, Mathf.RoundToInt(fontSize * 0.2f));
        _bundle.RepeatIcons = repeat;
        _bundle.RepeatSpacing = repeatSpacing;
        _bundle.PlusSigns = plusSigns;
        _bundle.ShowAmounts(content);
        _overlay.Texture = overlay;
        _overlay.Visible = overlay is not null;
        if (fresh)
            _shown = Time.GetTicksMsec() / 1000f;
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public void HideOffer()
    {
        Visible = false;
        SetProcess(false);
    }

    public bool IsOver(Vector2 globalPoint) =>
        IsVisibleInTree() && ToLocal(globalPoint).Length() <= _radius * 1.15f;

    // Fades in over 0.45 s when it first appears (as the edges do), then just keeps the glint moving.
    public override void _Process(double delta)
    {
        float intro = Mathf.Clamp((Time.GetTicksMsec() / 1000f - _shown) / 0.45f, 0f, 1f);
        Modulate = new Color(_enabled ? Colors.White : DisabledTint, 1 - MathF.Pow(1 - intro, 3));
        QueueRedraw();
    }

    public override void _Draw()
    {
        float now = Time.GetTicksMsec() / 1000f;
        float r = _radius, hex = r / 0.42f;
        Color colour = _enabled ? _colour : new Color(0.6f, 0.6f, 0.6f);
        DrawCircle(Vector2.Zero, r, Fill);
        // The edges' glow, round the disc: wide and faint, then the colour, then a white core.
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 64, new Color(colour, 0.38f), hex * 0.24f, true);
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 64, new Color(colour, 0.9f), hex * 0.12f, true);
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 64, Colors.White, Mathf.Max(1.5f, hex * 0.045f), true);
        if (!_enabled)
            return;
        // A glint running round it, as along the edges.
        float u = now * 0.6f - MathF.Floor(now * 0.6f);
        MapGlow.Glint(this, Vector2.FromAngle(u * Mathf.Tau - Mathf.Pi / 2) * r, hex * 0.2f, 0.9f);
    }
}
