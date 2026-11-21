using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of PlayerPanel: Fire & Ice's terrain wheel and the Shapeshifters' chips. A file of the same class (partial).
public partial class PlayerPanel
{
    // ---------- Fire & Ice: the terrain wheel ----------
    // A ColourWheel (TerrainWheel) on the Marker2D TerrainMarker (made where the spades are if the scene has none):
    //   ice factions: a badge, their starting terrain's circle with the ice ring over it;
    //   Shapeshifters: a badge, their home terrain's circle with their ring over it; it opens into the whole wheel
    //     while they choose where their home moves (Main);
    //   Riverwalkers: the whole wheel, small, the terrains still locked (a priest on them) dimmed; it opens to full
    //     size, the priests standing on their terrains, when clicked or while they choose what a priest does (Main).
    // Nothing for the other factions. Badge, small and open sizes are scales of the marker's pixels.

    private float _badgeScale = 0.55f, _smallScale = 0.3f, _ringScale = 1f;
    private Vector2 _ringOffset;

    public ColourWheel TerrainWheel { get; private set; }
    // The terrain at the wheel's top (and on the badge): the starting terrain, or the Shapeshifters' home; null when
    // the wheel isn't shown.
    public Terrain? TerrainWheelTop { get; private set; }
    public bool IsTerrainWheelOpen { get; private set; }
    private Faction? _wheelFaction;
    private bool _wheelIsBadge;                 // ice factions and Shapeshifters: one circle until opened
    private readonly List<Terrain> _locked = new();
    private PlayerColour _wheelColour;
    private PowerOfferChip _spendChip, _removeChip;

    private void ReadyTerrainWheel()
    {
        var marker = GetNodeOrNull<Node2D>("TerrainMarker");
        if (marker is null)
        {
            GD.PushWarning($"{GetPath()} has no Marker2D TerrainMarker for the Fire & Ice terrain wheel: it is put where the spades are.");
            marker = new Marker2D { Name = "TerrainMarker", Position = GetNodeOrNull<Node2D>("Spades")?.Position ?? Vector2.Zero };
            AddChild(marker);
        }
        TerrainWheel = new ColourWheel
        {
            Name = "TerrainWheel", Visible = false, RingScale = TerrainRingScale, RingOffset = TerrainRingOffset,
            IgnoreClick = point => ShiftPriceAt(point) is not null, // the price chips over it take their own clicks
        };
        marker.AddChild(TerrainWheel);
    }

    private void ShowTerrain(PlayerState player)
    {
        if (TerrainWheel is null)
            return;
        bool newFaction = _wheelFaction != player.Faction;
        _wheelFaction = player.Faction;
        Terrain? top = player.Faction == Faction.Shapeshifters ? player.ShapeshifterHome : player.StartingTerrain;
        bool shown = top is not null && player.Kind is FactionKind.Ice or FactionKind.Variable;
        TerrainWheel.Visible = shown;
        TerrainWheelTop = shown ? top : null;
        if (!shown)
        {
            IsTerrainWheelOpen = false;
            return;
        }
        bool river = player.Faction == Faction.Riverwalkers;
        _wheelIsBadge = !river && player.Faction != Faction.Geologists; // (the Geologists' wheel is small, with their rocks)
        TerrainWheel.SetRocks(player.Faction == Faction.Geologists ? player.RockList : Enumerable.Empty<Terrain>());
        _wheelColour = player.Colour;
        _locked.Clear();
        _locked.AddRange(player.WheelPriests.Select(priest => priest.Terrain));
        TerrainWheel.Focus = top;
        TerrainWheel.Ring = river ? null : top;
        TerrainWheel.RingIcon = player.Kind == FactionKind.Ice ? Icon.IceRing : Icon.VariableRing;
        TerrainWheel.RingChooses = null;
        TerrainWheel.ShowStill(top.Value); // Main lights a choice afterwards, if there is one
        if (newFaction)
            IsTerrainWheelOpen = false;
        ShapeTerrainWheel(newFaction ? 0f : -1f);
        PlaceTerrainWheel(0f);
    }

    private Tween _wheelPlaceTween;

    // Its place on TerrainMarker: GeologistsWheelOffset for the Geologists in the full layout, else the marker itself.
    private void PlaceTerrainWheel(float seconds)
    {
        if (TerrainWheel is null)
            return;
        Vector2 target = _wheelFaction == Faction.Geologists && !IsCompact ? GeologistsWheelOffset : Vector2.Zero;
        if (_wheelPlaceTween is not null && _wheelPlaceTween.IsValid())
            _wheelPlaceTween.Kill();
        _wheelPlaceTween = null;
        if (seconds <= 0 || !IsInsideTree() || TerrainWheel.Position.IsEqualApprox(target))
        {
            TerrainWheel.Position = target;
            return;
        }
        _wheelPlaceTween = CreateTween();
        _wheelPlaceTween.TweenProperty(TerrainWheel, "position", target, seconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    // Opens the wheel to full size (the Riverwalkers' priests standing on it), or closes it to its badge or small size.
    public void SetTerrainWheelOpen(bool open, bool animate)
    {
        if (TerrainWheel is null || !TerrainWheel.Visible)
            return;
        IsTerrainWheelOpen = open;
        ShapeTerrainWheel(animate ? TerrainWheelSeconds : 0f);
    }

    // seconds: how long the change takes; below 0, only if the shape changed (then TerrainWheelSeconds).
    private void ShapeTerrainWheel(float seconds)
    {
        float spread = IsTerrainWheelOpen || !_wheelIsBadge ? 1f : 0f;
        float scale = IsTerrainWheelOpen ? TerrainWheelOpenScale : _wheelIsBadge ? TerrainBadgeScale : TerrainWheelSmallScale;
        // Open: every colour in full, the priests standing on the locked ones. Closed: the locked ones dimmed.
        TerrainWheel.SetPriests(IsTerrainWheelOpen ? _locked : Enumerable.Empty<Terrain>(),
            Art.PieceColour(_wheelColour), Art.OutlineFor(_wheelColour));
        TerrainWheel.SetDisabled(IsTerrainWheelOpen ? Enumerable.Empty<Terrain>() : _locked);
        TerrainWheel.ZIndex = IsTerrainWheelOpen ? TerrainWheelZIndex : 0;
        if (seconds < 0)
            seconds = Mathf.IsEqualApprox(TerrainWheel.Spread, spread) && TerrainWheel.Scale.IsEqualApprox(Vector2.One * scale) ? 0f : TerrainWheelSeconds;
        TerrainWheel.Animate(spread, Vector2.One * scale, seconds);
    }

    public bool IsOverTerrainWheel(Vector2 globalPoint) => Visible && TerrainWheel is not null && TerrainWheel.IsOver(globalPoint);

    // ---------- Fire & Ice: the Shapeshifters' chips ----------

    // The two ways to pay for the shift, over the chosen terrain's circle (at: its global position; null hides them):
    // spend 5 power, or remove 5 power tokens (five tokens crossed out). Each is greyed out if it can't be paid now.
    public void ShowShiftPrices(Vector2? at, bool canSpend, bool canRemove, Color colour)
    {
        if (at is not Vector2 circle)
        {
            _spendChip?.HideOffer();
            _removeChip?.HideOffer();
            return;
        }
        _spendChip ??= Chip(this, "ShiftSpend");
        _removeChip ??= Chip(this, "ShiftRemove");
        Vector2 middle = ToLocal(circle) + ShapeshiftPriceOffset;
        float r = ShapeshiftPriceRadius;
        _spendChip.Position = middle - new Vector2(ShapeshiftPriceSpacing / 2, 0);
        _removeChip.Position = middle + new Vector2(ShapeshiftPriceSpacing / 2, 0);
        _spendChip.ShowContent(Amounts.Of(ResourceKind.PowerSpend, Config.ShapeshiftCost), colour, r,
            Mathf.RoundToInt(r * 1.05f), Mathf.RoundToInt(r * 0.8f), canSpend);
        _removeChip.ShowContent(Amounts.Of(ResourceKind.PowerValue, Config.ShapeshiftCost), colour, r,
            Mathf.RoundToInt(r * 0.85f), Mathf.RoundToInt(r * 0.8f), canRemove, repeat: true, repeatSpacing: r * 0.22f,
            overlay: IconArt.Texture(Icon.PowerCross, Mathf.RoundToInt(r * 1.7f)));
    }

    // The price chip under the point: false to spend power, true to remove tokens; null for neither.
    public bool? ShiftPriceAt(Vector2 globalPoint) =>
        _spendChip?.IsOver(globalPoint) == true ? false
        : _removeChip?.IsOver(globalPoint) == true ? true
        : null;

    // The Shapeshifters' token for 1 VP: power bowl III lit purple with a glint while it is offered; clicking the bowl
    // accepts (Main).
    public void ShowTokenOffer(bool show) => PowerBank.HighlightBowl(3, show, HighlightArt.Colour.Purple, glints: 1);

    public bool IsOverTokenOffer(Vector2 globalPoint) => Visible && PowerBank.IsOverBowl(3, globalPoint);
}
