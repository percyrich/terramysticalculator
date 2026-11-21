using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of PlayerPanel: shipping and spade upgrades (and the tunnel and carpet in their place). A file of the same
// class (partial).
public partial class PlayerPanel
{
    // ---------- Shipping and spade upgrades ----------

    // level: what Shipping/Level shows: the shipping level (levelText "2+1" with this round's bonus), the Fakirs'
    // carpet range, or the Dwarves' tunnel (levelText "": no number, the range is always 1);
    // shippingCost: the next shipping upgrade (null: none left, or no shipping at all);
    // sideCost: what each use costs, drawn right of the level (the Dwarves' tunnel), or null;
    // spadeRate: what a paid spade costs now (workers, or a priest for the Darklings);
    // spadeCost: the next spade upgrade (null: none left); possible: the upgrade would be accepted right now (not
    // shown at the moment).
    // Shipping/Level, a ResourceBundle, shows the level as any other number (the icon with the number on it);
    // Spades/Rate shows the spade cost (e.g. "3 workers"). The side cost goes in a ResourceBundle made on the Marker2D
    // ReachNumber. The upgrade costs are drawn by the ResourceBundles Shipping/Cost and Spades/Cost.
    public void ShowUpgrades(Amounts level, string levelText, Amounts shippingCost, bool canUpgradeShipping,
        Amounts sideCost, Amounts spadeRate, Amounts spadeCost, bool canUpgradeSpades)
    {
        ResourceKind levelKind = level.IsEmpty ? ResourceKind.Shipping : level.Parts[0].Kind;
        _levelKind = levelKind;
        bool ownReach = ShowReach(levelKind is ResourceKind.Tunnel or ResourceKind.CarpetRange ? levelKind : null);
        var bundle = GetNode<ResourceBundle>("Shipping/Level");
        bundle.Visible = !ownReach; // the tunnel or carpet on its own marker instead
        string kinds = Amounts.Of(levelKind, 2).ToString()[2..]; // the kind's name, e.g. "shipping", "carpet range"
        if (bundle.Kinds != kinds)
            bundle.Kinds = kinds; // so 0 is written too
        bundle.ShowAmounts(level, texts: levelText is null ? null : new Dictionary<ResourceKind, string> { [levelKind] = levelText });
        ShowSideCost(sideCost);
        // the Fakirs' range, beside the carpet on its own marker
        ShowSideText(ownReach && levelKind == ResourceKind.CarpetRange ? level.Parts[0].Count.ToString() : null);
        ShowUpgradeCost("Shipping", shippingCost);

        var rate = GetNode<ResourceBundle>("Spades/Rate");
        string rateKinds = spadeRate.IsEmpty ? "workers" : Amounts.Of(spadeRate.Parts[0].Kind, 2).ToString()[2..];
        if (rate.Kinds != rateKinds)
            rate.Kinds = rateKinds; // so a 1 is written too
        rate.ShowAmounts(spadeRate);
        ShowUpgradeCost("Spades", spadeCost);
        _upgradePossible["Shipping"] = canUpgradeShipping;
        _upgradePossible["Spades"] = canUpgradeSpades;
        ShowUpgradeHitBox("Shipping", ShippingLevelIcon);
        ShowUpgradeHitBox("Spades", _spadeIcon);
    }

    // The Prospectors (after ShowUpgrades): rate, the coins a golden spade costs (null: everyone else). In place of the
    // spade and its rate, "coins ⇒ golden spade" as on their stronghold row (ProspectorsSpade*); what each brings
    // (Spades/Cost: 1 power, 1 VP, given as ShowUpgrades' spadeCost) spaced out under it, no plus signs.
    private Node2D _goldenSpade;
    private float? _costSpacing; // Spades/Cost's own spacing, restored for everyone else

    public void ShowGoldenSpade(Amounts rate)
    {
        bool golden = rate is not null;
        if (_spadeIcon is not null)
            _spadeIcon.Visible = !golden;
        if (GetNodeOrNull<ResourceBundle>("Spades/Rate") is ResourceBundle plainRate)
            plainRate.Visible = !golden;
        if (GetNodeOrNull<ResourceBundle>("Spades/Cost") is ResourceBundle cost)
        {
            _costSpacing ??= cost.Spacing;
            float spacing = golden ? ProspectorsGainSpacing : _costSpacing.Value;
            if (cost.Spacing != spacing)
                cost.Spacing = spacing;
            if (cost.PlusSigns)
                cost.PlusSigns = false;
        }
        if (!golden)
        {
            if (_goldenSpade is not null && IsInstanceValid(_goldenSpade))
                _goldenSpade.Visible = false;
            return;
        }
        if (_goldenSpade is null || !IsInstanceValid(_goldenSpade))
        {
            if (GetNodeOrNull<Node2D>("Spades") is not Node2D spades)
                return;
            _goldenSpade = new Node2D { Name = "GoldenSpade" };
            spades.AddChild(_goldenSpade, false, InternalMode.Back);
            _goldenSpade.AddChild(new ResourceBundle { Name = "Coins", Centred = true, ShowOnes = true, IconSize = ProspectorsSpadeIconSize });
            _goldenSpade.AddChild(new Sprite2D { Name = "Arrow", Texture = IconArt.Texture(Icon.TriggerArrow, ProspectorsSpadeIconSize / 2) });
            _goldenSpade.AddChild(new Sprite2D { Name = "Spade", Texture = IconArt.Texture(Icon.GoldenSpade, ProspectorsSpadeIconSize) });
        }
        _goldenSpade.Visible = true;
        _goldenSpade.Position = ProspectorsSpadeOffset;
        _goldenSpade.GetNode<ResourceBundle>("Coins").Position = new Vector2(-ProspectorsSpadeSpacing, 0);
        _goldenSpade.GetNode<ResourceBundle>("Coins").ShowAmounts(rate);
        _goldenSpade.GetNode<Sprite2D>("Arrow").Position = Vector2.Zero;
        _goldenSpade.GetNode<Sprite2D>("Spade").Position = new Vector2(ProspectorsSpadeSpacing, 0);
    }

    private const string HitBoxName = "HitBox";
    private readonly Dictionary<string, Node2D> _hitBoxes = new(); // internal children, so kept here (not found by path)

    // Whether each upgrade would be accepted right now (ShowUpgrades): its box shows only then. The click area stays,
    // so a click that can't upgrade still says why.
    private readonly Dictionary<string, bool> _upgradePossible = new();

    private bool ShowsHitBox(string part) => !IsCompact && _upgradePossible.GetValueOrDefault(part);

    private void ShowUpgradeHitBox(string part, Sprite2D icon)
    {
        if (GetNodeOrNull<Node2D>(part) is not Node2D holder)
            return;
        Node2D box = _hitBoxes.TryGetValue(part, out Node2D known) && IsInstanceValid(known) ? known : null;
        if (icon?.Texture is null)
        {
            if (box is not null)
                box.Visible = false;
            return;
        }
        if (box is null)
        {
            // In a layer just over the backgrounds, so every icon drawn after them (the terrain wheel's badge, before
            // the holders in the scene, included) is over it; kept on the holder's transform by a RemoteTransform2D.
            box = new Node2D { Name = part + HitBoxName };
            box.AddChild(new Polygon2D { Name = "Fill" });
            box.AddChild(new Line2D { Name = "Edge", Antialiased = true });
            Node2D layer = HitBoxLayer();
            layer.AddChild(box);
            var follow = new RemoteTransform2D { Name = HitBoxName + "Follow" };
            holder.AddChild(follow, false, InternalMode.Front); // never saved
            follow.RemotePath = follow.GetPathTo(box);
            _hitBoxes[part] = box;
        }
        Vector2[] outline = HighlightArt.RoundRect(UpgradeHitRect(part), UpgradeHitCornerRadius);
        var fill = box.GetNode<Polygon2D>("Fill");
        fill.Polygon = outline;
        fill.Color = UpgradeHitFillColour;
        var edge = box.GetNode<Line2D>("Edge");
        edge.Points = outline.Append(outline[0]).ToArray();
        edge.Width = UpgradeHitEdgeWidth;
        edge.DefaultColor = UpgradeHitEdgeColour;
        box.Visible = ShowsHitBox(part);
    }

    private Node2D _hitBoxLayer;

    // The hit boxes' layer: right after the panel's backgrounds (Background, CompactLayout), before every icon.
    private Node2D HitBoxLayer()
    {
        if (_hitBoxLayer is not null && IsInstanceValid(_hitBoxLayer))
            return _hitBoxLayer;
        _hitBoxLayer = new Node2D { Name = "UpgradeHitBoxes" };
        AddChild(_hitBoxLayer);
        int after = new[] { "Background", "CompactLayout" }
            .Select(name => GetNodeOrNull(name)?.GetIndex() ?? -1).Max();
        MoveChild(_hitBoxLayer, after + 1);
        return _hitBoxLayer;
    }

    // An upgrade's box, in its holder's pixels.
    private Rect2 UpgradeHitRect(string part) =>
        new((part == "Shipping" ? ShippingHitCentre : SpadesHitCentre) - UpgradeHitSize / 2, UpgradeHitSize);

    // Over an upgrade's box, while that upgrade has its icon (as when the box is drawn).
    private bool IsOverUpgrade(string part, Sprite2D icon, Vector2 globalPoint) =>
        Visible && icon?.Texture is not null && GetNodeOrNull<Node2D>(part) is Node2D holder
        && UpgradeHitRect(part).HasPoint(holder.ToLocal(globalPoint));

    private int _tunnelSize = 51, _carpetSize = 58, _sideIconSize = 73, _sideFontSize = 38;
    private ResourceKind _levelKind = ResourceKind.Shipping;
    private ResourceBundle _sideCost;
    private ResourceBundle _reach;
    private Label _sideText;

    private Node2D ReachIconHolder => GetNode<Node2D>("ReachIcon");
    private Node2D ReachNumberHolder => GetNode<Node2D>("ReachNumber");

    // Shows the icon of `kind` (the tunnel or the carpet; null: neither) on ReachIcon, without a number; true if it
    // shows one.
    private bool ShowReach(ResourceKind? kind)
    {
        if (_reach is null || !IsInstanceValid(_reach))
        {
            _reach = new ResourceBundle { Name = "ReachBundle", Centred = true };
            ReachIconHolder.AddChild(_reach);
        }
        DressReach(_reach, kind);
        return kind is not null;
    }

    private void DressReach(ResourceBundle bundle, ResourceKind? kind)
    {
        bundle.IconSize = kind == ResourceKind.CarpetRange ? CarpetIconSize : TunnelIconSize;
        bundle.Visible = kind is not null;
        bundle.ShowAmounts(kind is ResourceKind shown ? Amounts.Of(shown, 1) : Amounts.None); // a 1: no number
    }

    // The shipping icon a click upgrades (or the carpet's, the tunnel's): on ReachIcon, or Shipping/Level's.
    private Sprite2D ShippingLevelIcon =>
        (_reach is { Visible: true } && IsInstanceValid(_reach) ? _reach.IconFor(_levelKind) : null)
        ?? GetNode<ResourceBundle>("Shipping/Level").IconFor(_levelKind);

    // A number alone on ReachNumber (the Fakirs' range), or nothing (null).
    private void ShowSideText(string text)
    {
        if ((_sideText is null || !IsInstanceValid(_sideText)) && text is not null)
        {
            _sideText = NewSideText(Vector2.Zero);
            ReachNumberHolder.AddChild(_sideText);
        }
        if (_sideText is not null && IsInstanceValid(_sideText))
            DressSideText(_sideText, text);
    }

    private static Label NewSideText(Vector2 centre)
    {
        var label = new Label
        {
            Name = "SideText", Position = centre, MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetMeta("centre", centre);
        return label;
    }

    private void DressSideText(Label label, string text)
    {
        label.Visible = text is not null;
        if (text is null)
            return;
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", SideCostFontSize);
        TextStyle.BlackOnWhite(label);
        label.ResetSize();
        label.Position = label.GetMeta("centre").AsVector2() - label.Size / 2;
    }

    private void ShowSideCost(Amounts cost)
    {
        if (_sideCost is null && cost is not null)
        {
            _sideCost = new ResourceBundle { Name = "SideCostBundle", Centred = true, ShowOnes = true };
            ReachNumberHolder.AddChild(_sideCost);
        }
        if (_sideCost is not null)
            DressSideCost(_sideCost, cost);
    }

    private void DressSideCost(ResourceBundle bundle, Amounts cost)
    {
        bundle.IconSize = SideCostIconSize;
        bundle.FontSize = SideCostFontSize;
        bundle.Visible = cost is not null;
        if (cost is not null)
            bundle.ShowAmounts(cost);
    }

    // Editor only (ShowPreview): the tunnel or carpet and its number, as the game shows them, made afresh (never saved).
    private const string ReachPreviewMeta = "reach_preview";

    private void PreviewReach(Faction faction)
    {
        Node2D iconHolder = ReachIconHolder, numberHolder = ReachNumberHolder;
        foreach (Node2D holder in new[] { iconHolder, numberHolder })
            foreach (Node old in holder.GetChildren(includeInternal: true))
                if (old.HasMeta(ReachPreviewMeta))
                {
                    holder.RemoveChild(old);
                    old.QueueFree();
                }
        ResourceKind? kind = faction switch
        {
            Faction.Fakirs => ResourceKind.CarpetRange,
            Faction.Dwarves => ResourceKind.Tunnel,
            _ => null,
        };
        if (kind is not ResourceKind shown)
            return;
        var bundle = new ResourceBundle { Name = "ReachPreview", Centred = true };
        bundle.SetMeta(ReachPreviewMeta, true);
        iconHolder.AddChild(bundle, false, InternalMode.Back);
        DressReach(bundle, shown);
        if (shown == ResourceKind.Tunnel)
        {
            var cost = new ResourceBundle { Name = "SideCostPreview", Centred = true, ShowOnes = true };
            cost.SetMeta(ReachPreviewMeta, true);
            numberHolder.AddChild(cost, false, InternalMode.Back);
            DressSideCost(cost, Amounts.Of(ResourceKind.Worker, Config.DwarvesTunnelWorkers));
        }
        else
        {
            Label range = NewSideText(Vector2.Zero);
            range.SetMeta(ReachPreviewMeta, true);
            numberHolder.AddChild(range, false, InternalMode.Back);
            DressSideText(range, Config.FakirsCarpetRange.ToString());
        }
    }

    // The next upgrade's cost, in the Cost bundle. Nothing at all once there is no upgrade left, and nothing in the
    // compact layout (see ShowUpgradeCosts).
    private void ShowUpgradeCost(string part, Amounts cost)
    {
        GetNode<ResourceBundle>($"{part}/Cost").ShowAmounts(cost);
        if (part == "Shipping")
            _shippingHasCost = cost is not null;
        else
            _spadesHaveCost = cost is not null;
        ShowUpgradeCosts();
    }

    private bool _shippingHasCost, _spadesHaveCost;

    // The upgrade costs show only in the full layout, and only while there is an upgrade left.
    private void ShowUpgradeCosts()
    {
        bool full = !IsCompact;
        foreach (var (part, box) in _hitBoxes)
            if (IsInstanceValid(box) && box.GetNode<Polygon2D>("Fill").Polygon.Length > 0)
                box.Visible = full && ShowsHitBox(part);
        GetNode<CanvasItem>("Shipping/Cost").Visible = full && _shippingHasCost;
        GetNode<CanvasItem>("Spades/Cost").Visible = full && _spadesHaveCost;
    }

    // Lights the upgrades' boxes (white, a glint each): the Alchemists' upgrades offered by their Gloomy Workshop.
    public void HighlightUpgrades(bool shipping, bool spades)
    {
        foreach (var (part, on) in new[] { ("Shipping", shipping), ("Spades", spades) })
            if (GetNodeOrNull<Node2D>(part) is Node2D holder && (on || holder.GetNodeOrNull(EdgeHighlight.NodeName) is not null))
                EdgeHighlight.SetOnArea(holder, on && !IsCompact, UpgradeHitRect(part), UpgradeHitCornerRadius, 1, HighlightArt.Colour.White);
    }

    // The upgrades' boxes (UpgradeHitRect). Shipping's icon is the carpet's or the tunnel's on ReachIcon, or
    // Shipping/Level's.
    public bool IsOverShippingIcon(Vector2 globalPoint) =>
        IsOverUpgrade("Shipping", ShippingLevelIcon, globalPoint);
    public bool IsOverSpadeIcon(Vector2 globalPoint) => IsOverUpgrade("Spades", _spadeIcon, globalPoint);
}
