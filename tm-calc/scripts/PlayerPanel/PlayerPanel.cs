using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// One player's area: portrait, status, priest supply, favours, resources and income, power, building board.
// It only displays. It never changes game data; Main tells it which player to show.
// A tool script only so PreviewFaction works in the editor: there it does nothing else (_Ready returns at once).
[Tool]
public partial class PlayerPanel : Node2D
{
    [ExportGroup("Preview")]
    // Editor only: the faction the scene is shown for: the building board (BuildingBoard.PreviewFaction), the backings
    // in its colour (BackingSprite.PreviewTerrain), and a preview of the Fire & Ice terrain wheel on TerrainMarker.
    [Export] public Faction PreviewFaction { get => _previewFaction; set { _previewFaction = value; ShowPreview(); } }

    [ExportGroup("Layout")]
    // From one bridge in the supply to the next, in the BridgeSupply node's pixels. The bridges are parallel slants
    // (each about 17 across): at 44 the clear gap between them is half what it was at 64.
    [Export] public float BridgeSpacing { get; set; } = 44f;

    [ExportGroup("Bonus actions")]
    // Where the n-th bonus action goes (0 = first), in the BonusActions node's pixels: Step from one to the next; with
    // PerRow above 0, a new row every PerRow, RowStep from one row to the next.
    [Export] public Vector2 BonusActionStep { get; set; } = new(100, 0);
    [Export] public int BonusActionsPerRow { get; set; }
    [Export] public Vector2 BonusActionRowStep { get; set; } = new(0, 100);
    [Export] public float BonusActionSlideSeconds { get; set; } = 0.3f;

    [ExportGroup("Favours")]
    // Favours in the dark section on the right of the backing (FavourOffset).
    [Export] public float FavourPadding { get; set; } = 8f; // panel pixels
    [Export] public float FavourGap { get; set; } = 6f;     // panel pixels

    [ExportGroup("Compact layout")]
    // In the compact layout's building row (in the row's own pixels): from one building to the next of the same
    // kind, and from the last of one kind to the first of the next.
    [Export] public float CompactBuildingSpacing { get; set; } = 24f;
    [Export] public float CompactGroupSpacing { get; set; } = 40f;

    [ExportGroup("Highlights")]
    [Export] public float AreaCornerRadius { get; set; } = 8f;
    [Export] public float AreaHighlightScale { get; set; } = 0.5f; // the highlight's widths: these areas are small
    [Export] public float ZoneMargin { get; set; } = 10f; // in the icon's own pixels
    [Export] public Rect2 PriestExchangeArea { get; set; }
    [Export] public int PriestExchangeGlints { get; set; } = 1;
    // How far the coin/worker highlight stands out from the icon's drawing, in the bundle's pixels.
    [Export] public float ZoneHighlightMargin { get; set; } = 6f;

    [ExportGroup("Shipping and spade upgrades")]
    // Where a click upgrades (IsOverShippingIcon, IsOverSpadeIcon), drawn under everything else in Shipping and Spades:
    // a faint rounded rectangle with a slightly brighter edge. Both UpgradeHitSize, centred ShippingHitCentre and
    // SpadesHitCentre from their holder (Shipping, Spades: in its pixels, so a box moves with its group), each round
    // the icon, its level or rate, and the cost under it. Full layout only (the compact one takes no clicks).
    [Export] public Vector2 UpgradeHitSize { get; set; } = new(134, 112);
    [Export] public Vector2 ShippingHitCentre { get; set; } = new(4, 17);
    [Export] public Vector2 SpadesHitCentre { get; set; } = new(-3, -50);
    [Export] public Color UpgradeHitFillColour { get; set; } = new(1f, 1f, 1f, 0.05f);
    [Export] public Color UpgradeHitEdgeColour { get; set; } = new(1f, 1f, 1f, 0.18f);
    [Export] public float UpgradeHitEdgeWidth { get; set; } = 1.5f;
    [Export] public float UpgradeHitCornerRadius { get; set; } = 10f;
    // The Prospectors' golden spade in place of the spade and its rate, as on their stronghold row: "4 coins ⇒ golden
    // spade", centred ProspectorsSpadeOffset in Spades, ProspectorsSpadeSpacing between the parts; what each spade brings
    // (power, VP) under it, ProspectorsGainSpacing apart, no plus signs.
    [Export] public Vector2 ProspectorsSpadeOffset { get; set; } = new(-5, -72);
    [Export] public float ProspectorsSpadeSpacing { get; set; } = 52f;
    [Export] public int ProspectorsSpadeIconSize { get; set; } = 56;
    [Export] public float ProspectorsGainSpacing { get; set; } = 60f;

    // The Dwarves' tunnel and the Fakirs' carpet in place of shipping: the icon alone (no number) in a ResourceBundle on
    // ReachIcon, TunnelIconSize or CarpetIconSize; on ReachNumber the tunnel's cost (a ResourceBundle, SideCostIconSize
    // and SideCostFontSize) or the carpet's range as a number alone (SideCostFontSize). ReachIcon and ReachNumber are
    // Marker2Ds directly under the panel, so CompactLayout moves them like any part: Marker2Ds of the same names in
    // CompactLayout place them in the compact layout (without one they fade out there). In the editor, PreviewFaction
    // shows them for the Fakirs and the Dwarves (range 1; tunnel 2 workers).
    [ExportGroup("Tunnel and carpet")]
    [Export] public int TunnelIconSize { get => _tunnelSize; set { _tunnelSize = value; ShowPreview(); } }
    [Export] public int CarpetIconSize { get => _carpetSize; set { _carpetSize = value; ShowPreview(); } } // 0.8 of the Level bundle's 73
    [Export] public int SideCostIconSize { get => _sideIconSize; set { _sideIconSize = value; ShowPreview(); } }
    [Export] public int SideCostFontSize { get => _sideFontSize; set { _sideFontSize = value; ShowPreview(); } }

    [ExportGroup("Bonus action contents")]
    [Export] public int BonusContentIconSize { get; set; } = 41;
    [Export] public float BonusContentRepeatSpacing { get; set; } = 12f;

    // The Safe's size (Icon.Safe: twice as wide), the icons on the book or in the Safe, and the amounts under the wide
    // book (BookBelowOffset below its centre, as the upgrade costs under shipping and spades).
    [ExportGroup("Book")]
    [Export] public int SafeIconSize { get; set; } = 72;
    // Room each side of the book (or Safe), so the octagons after it don't overlap it.
    [Export] public float BookPadding { get; set; } = 14f;
    // In the Safe; on Chash Dallah's wide book (their income); a count on the book (the Djinni's lamps, the Goblins'
    // treasures: as big as the book's octagon panel).
    [Export] public int BookContentIconSize { get; set; } = 30;
    [Export] public int BookIncomeIconSize { get; set; } = 38;
    [Export] public int BookCountIconSize { get; set; } = 80;
    [Export] public int BookCountFontSize { get; set; } = 36;
    [Export] public int BookContentFontSize { get; set; } = 26;
    [Export] public float BookContentSpacing { get; set; } = 34f;
    [Export] public Vector2 BookContentOffset { get; set; } = new(0, -2);
    [Export] public int BookBelowIconSize { get; set; } = 36;
    [Export] public int BookBelowFontSize { get; set; } = 28;
    [Export] public float BookBelowSpacing { get; set; } = 40f;
    [Export] public Vector2 BookBelowOffset { get; set; } = new(0, 62);

    // left: what was received and isn't going in (null: nothing offered), hovering SafeOfferOffset above the Safe; the
    // All button (SafeAllOffset from the Safe's centre: below it) puts it all in. Main adds what is going in to the
    // Safe's contents (ShowBonusActions' book). Setup: the Safe's editor (ShowSafeEditor), a priest, a worker and a
    // coin in a row hanging SafeEditorGap below the Safe, drawn as the power conversions' menu.
    [ExportGroup("Safe offer")]
    [Export] public Vector2 SafeOfferOffset { get; set; } = new(0, -56);
    [Export] public int SafeOfferIconSize { get; set; } = 44;
    [Export] public Vector2 SafeAllOffset { get; set; } = new(0, 50); // under the Safe
    [Export] public Vector2 SafeAllSize { get; set; } = new(56, 34);
    [Export] public int SafeEditorIconSize { get; set; } = 40;
    [Export] public float SafeEditorSpacing { get; set; } = 50f;
    [Export] public Vector2 SafeEditorPadding { get; set; } = new(12, 8);
    [Export] public float SafeEditorGap { get; set; } = 4f;
    [Export] public int SafeEditorZIndex { get; set; } = 35;
    // The turn-order step: a clicked board's white outline (round the backing shown), its corner and line widths.
    [ExportGroup("Board highlight")]
    [Export] public float BoardHighlightRadius { get; set; } = 24f;
    [Export] public float BoardHighlightScale { get; set; } = 1.5f;

    [ExportGroup("Terrain wheel")]
    [Export] public float TerrainBadgeScale { get => _badgeScale; set { _badgeScale = value; ShowPreview(); } }
    [Export] public float TerrainWheelSmallScale { get => _smallScale; set { _smallScale = value; ShowPreview(); } }
    [Export] public float TerrainWheelOpenScale { get; set; } = 0.9f;
    [Export] public float TerrainWheelSeconds { get; set; } = 0.3f;
    [Export] public int TerrainWheelZIndex { get; set; } = 30;    // over the rest of the panel while open
    // The Geologists' small wheel in the full layout only, from TerrainMarker: midway between the shipping cost and
    // the bridges/keys (their wheel is bigger than the other factions' badges).
    [Export] public Vector2 GeologistsWheelOffset { get; set; } = new(0, 30);
    // The ring, of its circle (ColourWheel.RingScale), and its nudge (ColourWheel.RingOffset).
    [Export] public float TerrainRingScale { get => _ringScale; set { _ringScale = value; ShowPreview(); } }
    [Export] public Vector2 TerrainRingOffset { get => _ringOffset; set { _ringOffset = value; ShowPreview(); } }

    // The Shapeshifters' two ways to pay for their shift: chips over the terrain chosen on the open wheel
    // (ShapeshiftPriceOffset from its circle's centre to the point between them, ShapeshiftPriceSpacing apart, panel pixels).
    [ExportGroup("Shapeshift price")]
    [Export] public Vector2 ShapeshiftPriceOffset { get; set; } = new(0, -100);
    [Export] public float ShapeshiftPriceSpacing { get; set; } = 90f;
    [Export] public float ShapeshiftPriceRadius { get; set; } = 36f;

    // The score badge (ScoreBadge) under the VP count: its top left ScoreBadgeOffset from the VP icon's drawn bottom left
    // (the VP bundle's pixels; without a VP bundle, from the portrait's centre), over the rest of the panel and its
    // neighbours: ScoreBadgeZIndex in the full layout, ScoreBadgeCompactZIndex in the compact one (relative to the panel,
    // whose own z Main sets: compact 0, open 30 and its pieces 35, inspected 50 and its pieces 60; so 15 and 25 keep a
    // badge over its own pieces and under any board above its own). Both layouts. Open, the arrow (ScoreArrowSize) and the VP total sit to the right of
    // the VP count, ScoreTotalGap apart; the total's icon and number are the VP count's sizes.
    [ExportGroup("Score badge")]
    [Export] public Vector2 ScoreBadgeOffset { get; set; } = new(0, 6);
    [Export] public int ScoreArrowSize { get; set; } = 48;
    [Export] public float ScoreTotalGap { get; set; } = 6f;
    [Export] public int ScoreBadgeZIndex { get; set; } = 15;
    [Export] public int ScoreBadgeCompactZIndex { get; set; } = 25;
    [Export] public int ScoreIconSize { get; set; } = 40;
    [Export] public int ScoreFontSize { get; set; } = 26;
    [Export] public Vector2 ScoreNumberOffset { get; set; } = new(0, -2);
    [Export] public Vector2 ScorePadding { get; set; } = new(8, 6);
    [Export] public float ScorePairGap { get; set; } = 4f;
    [Export] public float ScoreGroupGap { get; set; } = 12f;
    [Export] public float ScoreCornerRadius { get; set; } = 10f;
    [Export] public float ScoreStrokeWidth { get; set; } = 3f;
    [Export] public Color ScoreBoxColour { get; set; } = FactionBoardArt.SectionColour;
    // The box and everything in it (sizes, gaps, padding, corners, edge) this many times the settings above; the arrow
    // and total beside the VP count are not scaled.
    [Export] public float ScoreBadgeScale { get; set; } = 1.2f;

    // A flame with its number, PyroGap right of the VP count (both layouts; the score preview may cover it). null: none.
    [ExportGroup("Pyro")]
    [Export] public float PyroGap { get; set; } = 10f;
    [Export] public int PyroIconSize { get; set; } = 40;
    [Export] public int PyroFontSize { get; set; } = 26;
    [ExportGroup("")]
    private Faction _previewFaction = Faction.Giants;

    public static readonly Color PassedTint = new(0.725f, 0.725f, 0.725f); // greys out a passed player
    public static readonly Color SupplyTint = new(0.35f, 0.35f, 0.35f); // priests not yet gained

    // The faction currently shown, or null when the panel is unused.
    public Faction? ShownFaction { get; private set; }
    public Sprite2D Portrait { get; private set; }
    public Node2D PriestStack { get; private set; }  // gained priests, in the resources subpanel
    public Node2D PriestSupply { get; private set; } // priests not yet gained, under the portrait
    public PlayerButton PlayerButton { get; private set; } // ends the turn (its Pressed)
    public PowerBowlsView PowerBank { get; private set; } // the node named PowerBank: three bowls of power tokens
    public Node2D FavourRow { get; private set; }    // favours taken, in a row under the portrait
    public Node2D PassTileAnchor { get; private set; } // where the player's pass tile sits
    // The building slots (the child with the BuildingBoard script, any name): buildings still in the bank sit there.
    public BuildingBoard Board { get; private set; }

    // The player's resources and income: every ResourceBundle directly under Resources. Those named
    // Income... show the income (e.g. Kinds "priests, workers, coins, power", IncomeIcons on); the others show
    // what the player has (e.g. Kinds "workers, coins, VP, keys"). Set Kinds on them, so zeros and ones show.
    private readonly List<ResourceBundle> _holdings = new();
    private readonly List<ResourceBundle> _incomes = new();
    // Bridges not yet placed, in a row; highlighted while one must be placed.
    private Node2D _bridgeSupply;
    private readonly List<Sprite2D> _supplyBridges = new();
    private Node2D _bonusActionRow;
    // The spade icon (click to upgrade); hidden for the Prospectors' golden spade.
    private Sprite2D _spadeIcon;
    // One action token (octagon, icon, X when used) per unlocked bonus action.
    private readonly Dictionary<BonusActionId, ActionToken> _octagons = new();

    // Favours fill the dark section on the right of the backing (FactionBoardArt.BackingLayout(compact).Right, less
    // FavourPadding), the first at the top, each FavourGap below the one before, centred across it. If they would run
    // past its bottom, they are all brought closer together by the same amount so the last one ends at the bottom
    // (overlapping, later ones on top). Worked out for the layout shown (or being changed to), so the full and compact
    // layouts space them differently. tileSize: a favour tile's size in its own pixels (at the Favours node's scale it
    // is drawn at). The result is in the Favours node's pixels, as Main places tiles.
    public Vector2 FavourOffset(int n, int count, Vector2 tileSize)
    {
        Rect2 section = FactionBoardArt.BackingLayout(IsCompact).Right.Grow(-FavourPadding);
        Transform2D row = FinalTransform(FavourRow); // the Favours node, in panel pixels, once any layout change is done
        float height = tileSize.Y * Mathf.Abs(row.Scale.Y);
        float step = height + FavourGap;
        if (count > 1 && height + (count - 1) * step > section.Size.Y)
            step = Mathf.Max(0f, (section.Size.Y - height) / (count - 1));
        var centre = new Vector2(section.GetCenter().X, section.Position.Y + height / 2 + n * step);
        return row.AffineInverse() * centre;
    }
    private Vector2 BonusActionOffset(int n) => Grid(n, BonusActionStep, BonusActionsPerRow, BonusActionRowStep);

    private static Vector2 Grid(int n, Vector2 step, int perRow, Vector2 rowStep) =>
        perRow > 0 ? step * (n % perRow) + rowStep * (n / perRow) : step * n;

    // The bonus actions in the order they were gained (see ShowBonusActions), and whose they are.
    private readonly List<BonusActionId> _bonusOrder = new();
    private Faction? _bonusFaction;
    public static readonly Color SelectedTint = new(1f, 1f, 0.45f); // the bonus action waiting for a target

    // The compact layout (the child with the CompactLayout script), or null if the panel has none:
    // then it is always full size.
    private CompactLayout _layout;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            ShowPreview();
            return;
        }
        Portrait = GetNode<Sprite2D>("Portrait");
        PriestStack = GetNode<Node2D>("Resources/PriestStack");
        PriestSupply = GetNode<Node2D>("PriestSupply");
        PlayerButton = GetNode<PlayerButton>("PlayerButton");
        PowerBank = GetNode<PowerBowlsView>("PowerBank");
        FavourRow = GetNode<Node2D>("Favours");
        PassTileAnchor = GetNode<Node2D>("PassTile");
        _bonusActionRow = GetNode<Node2D>("BonusActions");
        _spadeIcon = GetNode<Sprite2D>("Spades/SpadeIcon");
        // Every ResourceBundle under Resources, and any directly on the panel named Have... or Income... (one taken
        // out of Resources so the compact layout can place it on its own, e.g. HaveKeys with a marker of that name).
        IEnumerable<ResourceBundle> bundles = GetNode("Resources").GetChildren().OfType<ResourceBundle>()
            .Concat(GetChildren().OfType<ResourceBundle>().Where(bundle =>
                bundle.Name.ToString().StartsWith("Have") || bundle.Name.ToString().StartsWith("Income")));
        foreach (ResourceBundle bundle in bundles)
            (bundle.Name.ToString().StartsWith("Income") ? _incomes : _holdings).Add(bundle);
        if (_holdings.Count == 0)
            GD.PushWarning($"{GetPath()}/Resources has no ResourceBundle for the player's coins and workers.");
        _bridgeSupply = GetNode<Node2D>("BridgeSupply");
        // Found by type, so the node can have any name.
        Board = GetChildren().OfType<BuildingBoard>().FirstOrDefault();
        if (Board is null)
            GD.PushWarning($"{GetPath()} has no BuildingBoard child, so buildings in the bank have nowhere to sit.");
        _layout = GetChildren().OfType<CompactLayout>().FirstOrDefault();
        if (_layout is null)
            GD.PushWarning($"{GetPath()} has no CompactLayout child, so it is always shown full size.");
        ReadyTerrainWheel();
    }

    // Where a building still in the bank sits: slot `slot` (from the left) of its kind on the building board
    // (the panel itself if there is no board, or no such slot).
    public Node2D BankSlot(PieceKind kind, int slot) => Board?.Slot(kind, slot) ?? this;

    // ---------- Lit-up areas: a white EdgeHighlight ----------
    // The bridge supply: BridgeHighlightSize, centred on the BridgeSupply node (the supply's centre).

    private static readonly Vector2 BridgeHighlightSize = new(100, 50); // in BridgeSupply's pixels

    // ---------- Drop zones: the coin and worker icons of the player's resources ----------
    // A zone is the icon (with its number on it), grown by ZoneMargin on every side.

    public bool IsOverHolding(ResourceKind kind, Vector2 globalPoint) => IsOverZone(HoldingIcon(kind), globalPoint);
    public bool IsOverCoinZone(Vector2 globalPoint) => IsOverZone(HoldingIcon(ResourceKind.Coin), globalPoint);
    public bool IsOverWorkerZone(Vector2 globalPoint) => IsOverZone(HoldingIcon(ResourceKind.Worker), globalPoint);
    public bool IsOverVictoryPointZone(Vector2 globalPoint) => IsOverZone(HoldingIcon(ResourceKind.VictoryPoint), globalPoint);
    public void HighlightCoinZone(bool on) => Highlight(HoldingIcon(ResourceKind.Coin), on);
    public void HighlightWorkerZone(bool on) => Highlight(HoldingIcon(ResourceKind.Worker), on);
    public void HighlightVictoryPointZone(bool on) => Highlight(HoldingIcon(ResourceKind.VictoryPoint), on);

    // ---------- The Darklings' priest exchange: workers dragged onto the worker and priest section ----------
    // PriestExchangeArea is that section, a rectangle in Resources' pixels; left empty, it is the worker icon and the
    // priest stack together (grown by ZoneHighlightMargin). Lit (white) while workers can still be exchanged.

    public void ShowPriestExchange(bool on)
    {
        if (GetNodeOrNull<Node2D>("Resources") is not Node2D resources)
            return;
        EdgeHighlight.SetOnArea(resources, on, ExchangeRect(resources), AreaCornerRadius, PriestExchangeGlints,
            HighlightArt.Colour.White, AreaHighlightScale);
    }

    public bool IsOverPriestExchange(Vector2 globalPoint) =>
        Visible && GetNodeOrNull<Node2D>("Resources") is Node2D resources
        && ExchangeRect(resources).HasPoint(resources.ToLocal(globalPoint));

    private Rect2 ExchangeRect(Node2D resources)
    {
        if (PriestExchangeArea.HasArea())
            return PriestExchangeArea;
        var rect = new Rect2(resources.ToLocal(PriestStack.GlobalPosition), Vector2.Zero).Grow(40);
        if (HoldingIcon(ResourceKind.Worker) is Sprite2D { Texture: not null } worker)
        {
            Vector2 half = worker.Texture.GetSize() * worker.GlobalScale / resources.GlobalScale / 2;
            Vector2 centre = resources.ToLocal(worker.GlobalPosition);
            rect = rect.Merge(new Rect2(centre - half, half * 2));
        }
        return rect.Grow(ZoneHighlightMargin);
    }

    // The icon of a kind in the player's resources (one shown in the current layout, if any), or null if no bundle
    // shows it.
    private Sprite2D HoldingIcon(ResourceKind kind) =>
        _holdings.Select(bundle => bundle.IconFor(kind)).Where(icon => icon is not null)
            .OrderByDescending(icon => icon.IsVisibleInTree()).FirstOrDefault();

    // Victory points, coins and workers still flying to this panel (Main's point lights): each count shows that many
    // fewer until they land.
    private readonly Dictionary<ResourceKind, int> _inFlight = new();

    public void SetInFlight(ResourceKind kind, int amount)
    {
        if (amount == 0)
            _inFlight.Remove(kind);
        else
            _inFlight[kind] = amount;
    }

    public void ClearInFlight() => _inFlight.Clear();

    // Shows the counts again with this many still flying (as each lands).
    public void ShowInFlight(ResourceKind kind, int amount)
    {
        SetInFlight(kind, amount);
        ShowHoldings();
        if (_book is not null && _bookAction is not null && IsInstanceValid(_bookAction))
            ShowBookAmounts(_book); // the Goblins' treasures
    }

    // Where a count is (victory points, coins, workers): what the lights fly to.
    public Vector2 HoldingCentre(ResourceKind kind) =>
        kind is ResourceKind.Treasure or ResourceKind.Lamp && _bookContent is not null && IsInstanceValid(_bookContent)
            && _bookContent.IconFor(kind) is Sprite2D onBook
            ? onBook.GlobalPosition // the Goblins' treasures fly to their book
            : HoldingIcon(kind)?.GlobalPosition ?? GlobalPosition;

    private PlayerState _shownPlayer;

    private void ShowHoldings()
    {
        if (_shownPlayer is not PlayerState player)
            return;
        int points = player.VictoryPoints - _inFlight.GetValueOrDefault(ResourceKind.VictoryPoint);
        int coins = player.Coins - _inFlight.GetValueOrDefault(ResourceKind.Coin);
        int workers = player.Workers - _inFlight.GetValueOrDefault(ResourceKind.Worker);
        Amounts holdings = Amounts.Of(
            (ResourceKind.Priest, player.Priests.Count), (ResourceKind.Worker, workers),
            (ResourceKind.Coin, coins), (ResourceKind.VictoryPoint, points),
            (ResourceKind.Key, player.Keys), (ResourceKind.Shipping, player.Shipping));
        var debts = new List<ResourceKind>();
        if (coins < 0) debts.Add(ResourceKind.Coin);
        if (workers < 0) debts.Add(ResourceKind.Worker);
        if (points < 0) debts.Add(ResourceKind.VictoryPoint);
        foreach (ResourceBundle bundle in _holdings)
            bundle.ShowAmounts(holdings, debts);
    }

    // A white edge highlight (no glint) round the icon, in its bundle: where a picked-up worker or priest goes.
    // Only one icon per bundle is lit at a time (a worker lights the coins, a priest the workers).
    private void Highlight(Sprite2D icon, bool on)
    {
        if (icon?.Texture is null || icon.GetParent() is not Node2D bundle)
            return;
        Rect2 painted = Arrangement.VisibleRect(icon);
        var rect = new Rect2(icon.Position + painted.Position * icon.Scale, painted.Size * icon.Scale).Grow(ZoneHighlightMargin);
        EdgeHighlight.SetOnArea(bundle, on, rect, AreaCornerRadius, 0, HighlightArt.Colour.White, AreaHighlightScale);
    }

    private bool IsOverZone(Sprite2D icon, Vector2 globalPoint)
    {
        if (!Visible || icon?.Texture is null)
            return false;
        return icon.GetRect().Grow(ZoneMargin).HasPoint(icon.ToLocal(globalPoint));
    }

    // seconds: how long power tokens take to move between bowls (0: at once; always at once for a new faction).
    // income: what the income shows, if not player.Income (Main leaves out a pass tile whose income is already drawn).
    // showIncome false: the income bundles hidden, both layouts (the last round: no income to come). Their amounts are
    // still set, so their icons keep their places (Calculator's +1 bar is laid out on them).
    public void ShowPlayer(PlayerState player, bool hasPassed, float seconds = 0f, Income? income = null, bool showIncome = true)
    {
        bool sameFaction = ShownFaction == player.Faction;
        Visible = true;
        ShownFaction = player.Faction;
        Portrait.Texture = Art.PortraitTexture(player.Faction);
        // A passed player's panel is greyed out, labels included: every part but the player button, which stays bright
        // (it can still be pressed to end the turn after passing). Only the tint is set: CompactLayout fades parts by
        // their alpha.
        Modulate = Colors.White;
        Color tint = hasPassed ? PassedTint : Colors.White;
        foreach (CanvasItem part in GetChildren().OfType<CanvasItem>())
            if (part != PlayerButton)
                part.Modulate = new Color(tint, part.Modulate.A);

        // What the player has (debts in the warning colour), and their income (more priest income than
        // priests left in the supply in the warning colour: some of it will be lost).
        _shownPlayer = player;
        ShowHoldings();

        Income shownIncome = income ?? player.Income;
        ResourceKind[] lost = shownIncome.Priests > player.PriestSupply.Count ? new[] { ResourceKind.Priest } : null;
        foreach (ResourceBundle bundle in _incomes)
        {
            bundle.PriestColour = Art.PieceColour(player.Colour); // the priest income in the faction's colour
            bundle.ShowAmounts(Amounts.From(shownIncome), lost);
            bundle.Visible = showIncome;
        }
        PowerBank.ShowPower(player.Power, sameFaction ? seconds : 0f);

        // In the player's colour (the variable factions take their starting terrain's).
        Color frame = Art.PieceColour(player.Colour);
        Board?.ShowFaction(player.Faction, frame); // frame colour, slot incomes, base income, costs, power values
        Board?.ShowStrongholdBuilt(player.StrongholdBuilt); // its bonus's spent effects faded
        Board?.ShowMilestones(player.AtlanteansRewards);     // the Atlanteans' thresholds reached faded
        ShowBackings(frame);
        // Spade upgrades mean nothing to factions without spades (volcano factions, Riverwalkers).
        if (GetNodeOrNull<CanvasItem>("Spades") is CanvasItem spades)
            spades.Visible = player.Kind != FactionKind.Volcano && player.Faction != Faction.Riverwalkers && player.Faction != Faction.Geologists;
        ShowTerrain(player);
    }

    // The backings in the faction's colour (FactionBoardArt.Backing): Background for the full layout, 1544 × 600 from
    // the panel's top left, and CompactLayout/CompactBackground for the compact one, 1544 × 300 (1400 laid out in
    // sections, then FactionBoardArt.PassColumnWidth of stone for the pass tile).
    private void ShowBackings(Color frame)
    {
        if (GetNodeOrNull<Sprite2D>("Background") is Sprite2D full)
            full.Texture = FactionBoardArt.Backing(frame);
        if (_layout?.GetNodeOrNull<Sprite2D>("CompactBackground") is Sprite2D compact)
            compact.Texture = FactionBoardArt.Backing(frame, compact: true);
        // The board belongs centred on the full backing's bottom edge, its frame's bottom line on the backing's.
        if (Board is not null && !_boardChecked && FactionBoardArt.BackingLayout(false).Board is Rect2 room)
        {
            _boardChecked = true;
            if (Board.Position.DistanceTo(room.Position) > 0.5f || !Mathf.IsEqualApprox(Board.Scale.X, FactionBoardArt.BackingBoardScale))
                GD.PushWarning($"{GetPath()}: BuildingBoard is at {Board.Position} × {Board.Scale.X}; the backing has room for it at {room.Position} × {FactionBoardArt.BackingBoardScale}.");
        }
    }
    private bool _boardChecked;

    // The whole board outlined in white (the turn-order step: a board clicked), round each layout's backing (each fades
    // with its layout).
    public void ShowBoardHighlight(bool on)
    {
        foreach (Sprite2D backing in new[] { GetNodeOrNull<Sprite2D>("Background"), _layout?.GetNodeOrNull<Sprite2D>("CompactBackground") })
            if (backing is not null && (backing.Texture is not null || !on))
                EdgeHighlight.SetOnArea(backing, on && backing.Texture is not null, on ? backing.GetRect() : default,
                    BoardHighlightRadius, 0, HighlightArt.Colour.White, BoardHighlightScale);
    }

    // The player button: only on the active player's panel (Hidden otherwise); blank before the turn's action, dots
    // while an action is under way, a tick (and pressable) once the turn can end. Its ring in the player's colour.
    public void ShowPlayerButton(PlayerButton.Mode mode, Color colour, bool animate) => PlayerButton.Show(mode, colour, animate);

    public void ShowEmpty()
    {
        Visible = false;
        _scoreBadge?.SetOpen(false);
        ShownFaction = null;
        _shownPlayer = null;
    }

    // ---------- Power offers and bridges ----------


    // One bridge sprite per bridge left, in a row centred on the BridgeSupply node; lit white (one glint) round the
    // supply while one must be placed now.
    public void ShowBridgeSupply(int left, PlayerColour colour, bool mustPlace)
    {
        while (_supplyBridges.Count > left)
        {
            _supplyBridges[^1].QueueFree();
            _supplyBridges.RemoveAt(_supplyBridges.Count - 1);
        }
        while (_supplyBridges.Count < left)
        {
            var bridge = new Sprite2D();
            _bridgeSupply.AddChild(bridge);
            _supplyBridges.Add(bridge);
        }
        for (var i = 0; i < _supplyBridges.Count; i++)
        {
            _supplyBridges[i].Position = new Vector2((i - (_supplyBridges.Count - 1) / 2f) * BridgeSpacing, 0);
            _supplyBridges[i].Texture = Art.BridgeIconTexture(colour);
        }
        EdgeHighlight.SetOnArea(_bridgeSupply, mustPlace, new Rect2(-BridgeHighlightSize / 2, BridgeHighlightSize), AreaCornerRadius, 1,
            HighlightArt.Colour.White, AreaHighlightScale);
    }

    public bool IsOverBridgeSupply(Vector2 globalPoint) =>
        Visible && !IsCompact && _supplyBridges.Any(bridge => IsUnder(bridge, globalPoint));

    // ---------- Layouts: full size (whose turn it is, or being inspected) and compact (everyone else) ----------
    // The compact layout is authored in the child with the CompactLayout script. In it, the Node2D
    // BuildingRow is where the buildings still in the banks sit in the compact layout, side by side
    // (CompactBuildingSpacing apart, CompactGroupSpacing between kinds): strongholds, trading posts, dwellings,
    // temples, sanctuaries.

    public bool IsCompact => _layout?.IsCompact ?? false;

    // Changes to the compact (true) or full layout over `seconds` (0: at once).
    public void SetLayout(bool compact, float seconds)
    {
        _layout?.Apply(compact, seconds);
        PlaceTerrainWheel(seconds);
        if (_scoreBadge is not null && IsInstanceValid(_scoreBadge))
            _scoreBadge.ZIndex = compact ? ScoreBadgeCompactZIndex : ScoreBadgeZIndex;
        ShowUpgradeCosts(); // hidden in the compact layout
        PlaceBonusActions(); // the book action only in the full layout
    }

    // Where a node in the panel will be once the layout change under way has finished, in the panel's coordinates.
    public Transform2D FinalTransform(Node2D node) =>
        _layout?.FinalTransform(node) ?? GlobalTransform.AffineInverse() * node.GlobalTransform;

    // The same in a given layout (compact or full), whichever is shown.
    // Calculator's +1 bar: the centre of the income's icon of this kind in the full layout, in the panel's pixels (null if
    // no income bundle shows it), its icon size, and the top of the full backing.
    public Vector2? FullIncomeIconCentre(ResourceKind kind)
    {
        foreach (ResourceBundle bundle in _incomes)
            if (bundle.IconFor(kind) is Sprite2D icon)
                return LayoutTransform(bundle, compact: false) * icon.Position;
        return null;
    }
    public int IncomeIconSize => _incomes.Count > 0 ? _incomes[0].IconSize : 64;
    public float FullTop => GetNodeOrNull<Sprite2D>("Background") is Sprite2D background
        ? (background.Transform * background.GetRect().Position).Y : 0f;

    public Transform2D LayoutTransform(Node2D node, bool compact) =>
        _layout?.LayoutTransform(node, compact) ?? GlobalTransform.AffineInverse() * node.GlobalTransform;

    // How much smaller (or bigger) than in the full layout pieces drawn at a node of the panel should be
    // once the layout change under way has finished (see CompactLayout.ScaleFactor).
    public Vector2 ScaleFactor(Node2D node) => _layout?.ScaleFactor(node) ?? Vector2.One;

    // Whether a node in the panel is visible in the layout shown (or being changed to).
    public bool IsShownInLayout(Node node) => _layout?.IsShown(node) ?? true;

    // Where the compact layout lines up the buildings still in the banks, or null if it has no BuildingRow.
    public Node2D CompactBuildingRow => _layout?.GetNodeOrNull<Node2D>("BuildingRow");

    // Is the point over the panel's background for its layout? Compact: Compact/CompactBackground
    // (or Background, if there is none). Full: Background.
    public bool IsOver(Vector2 globalPoint)
    {
        if (!Visible)
            return false;
        Sprite2D background = (IsCompact ? _layout?.GetNodeOrNull<Sprite2D>("CompactBackground") : null)
            ?? GetNodeOrNull<Sprite2D>("Background");
        return background is not null && IsUnder(background, globalPoint);
    }

    private static bool IsUnder(Sprite2D sprite, Vector2 globalPoint) =>
        sprite.Texture is not null && sprite.GetRect().HasPoint(sprite.ToLocal(globalPoint));

    // ---------- Editor preview ----------

    private void ShowPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;
        Faction faction = _previewFaction;
        if (GetChildren().OfType<BuildingBoard>().FirstOrDefault() is BuildingBoard board && board.PreviewFaction != faction)
            board.PreviewFaction = faction;
        PlayerColour colour = Factions.ColourOf(faction);
        Terrain frame = colour switch
        {
            PlayerColour.White => Terrain.Ice,
            PlayerColour.Orange => Terrain.Volcano,
            _ => Terrains.Wheel[(int)colour],
        };
        foreach (string path in new[] { "Background", "CompactLayout/CompactBackground" })
            if (GetNodeOrNull<BackingSprite>(path) is BackingSprite backing && backing.PreviewTerrain != frame)
                backing.PreviewTerrain = frame;
        PreviewReach(faction); // the tunnel or carpet, and its number

        // The terrain wheel: made afresh (never saved), as the game would show it before it is opened.
        if (GetNodeOrNull<Node2D>("TerrainMarker") is not Node2D marker)
            return;
        foreach (Node old in marker.GetChildren(includeInternal: true))
            if (old is TerrainWheelPreview)
            {
                marker.RemoveChild(old);
                old.QueueFree();
            }
        FactionKind kind = Factions.Kind(faction);
        if (kind is not (FactionKind.Ice or FactionKind.Variable))
            return;
        bool river = faction == Faction.Riverwalkers;
        var preview = new TerrainWheelPreview
        {
            Name = "TerrainWheelPreview", Full = river, HasRing = !river,
            RingIcon = kind == FactionKind.Ice ? Icon.IceRing : Icon.VariableRing,
            RingScale = TerrainRingScale, RingOffset = TerrainRingOffset,
            Scale = Vector2.One * (river ? TerrainWheelSmallScale : TerrainBadgeScale),
        };
        marker.AddChild(preview, false, InternalMode.Back);
    }

    private static PowerOfferChip Chip(Node parent, string name)
    {
        var chip = new PowerOfferChip { Name = name, Visible = false };
        parent.AddChild(chip);
        return chip;
    }

    // ---------- Fan factions: the Firewalkers' pyro ----------
    private ResourceBundle _pyro;

    public void ShowPyro(int? pyro)
    {
        ResourceBundle count = _holdings.FirstOrDefault(bundle => bundle.IconFor(ResourceKind.VictoryPoint) is not null);
        if (pyro is not int value || count is null)
        {
            if (_pyro is not null && IsInstanceValid(_pyro))
                _pyro.Visible = false;
            return;
        }
        if (_pyro is null || !IsInstanceValid(_pyro) || _pyro.GetParent() != count)
        {
            _pyro = new ResourceBundle { Name = "Pyro", ShowOnes = true, Kinds = "pyro", IconSize = PyroIconSize, FontSize = PyroFontSize };
            count.AddChild(_pyro, false, InternalMode.Back); // kept by the bundle's redraws
        }
        _pyro.Visible = true;
        _pyro.Position = new Vector2(count.Right + PyroGap + PyroIconSize / 2f, (count.Top + count.Bottom) / 2);
        _pyro.ShowAmounts(Amounts.Of(ResourceKind.Pyro, value)); // (Kinds: a 0 is written too)
    }

    public bool IsOverPyro(Vector2 globalPoint) =>
        Visible && _pyro is not null && IsInstanceValid(_pyro) && _pyro.IsVisibleInTree()
        && _pyro.IconFor(ResourceKind.Pyro) is Sprite2D icon && icon.GetRect().Grow(6).HasPoint(icon.ToLocal(globalPoint));

    // ---------- Score badge ----------

    private ScoreBadge _scoreBadge;

    // The player's network (and final scoring tile) values, and what final scoring would give now with the VP total it
    // would make from `points` (shown while open); outlined in `colour` (the player's).
    public void ShowScores(ScorePreview scores, int points, Color colour)
    {
        ResourceBundle count = _holdings.FirstOrDefault(bundle => bundle.IconFor(ResourceKind.VictoryPoint) is not null);
        Node2D parent = (Node2D)count ?? Portrait;
        if (_scoreBadge is null || !IsInstanceValid(_scoreBadge))
            _scoreBadge = new ScoreBadge { Name = "ScoreBadge" };
        if (_scoreBadge.GetParent() != parent)
        {
            _scoreBadge.GetParent()?.RemoveChild(_scoreBadge);
            parent.AddChild(_scoreBadge, false, InternalMode.Back); // internal: never saved, kept by the bundle's redraws
        }
        _scoreBadge.Position = (count is null ? Vector2.Zero : new Vector2(count.Left, count.Bottom)) + ScoreBadgeOffset;
        _scoreBadge.TotalAnchor = (count is null ? Vector2.Zero : new Vector2(count.Right, (count.Top + count.Bottom) / 2))
            - _scoreBadge.Position;
        _scoreBadge.TotalGap = ScoreTotalGap;
        _scoreBadge.ArrowSize = ScoreArrowSize;
        _scoreBadge.TotalIconSize = count?.IconSize ?? 74;
        _scoreBadge.TotalFontSize = count?.FontSize ?? 40;
        _scoreBadge.TotalNumberOffset = count?.CountOffset ?? new Vector2(0, -2);
        _scoreBadge.ZIndex = IsCompact ? ScoreBadgeCompactZIndex : ScoreBadgeZIndex;
        float k = ScoreBadgeScale; // sizes multiplied rather than the node scaled, so icons and text stay sharp
        _scoreBadge.IconSize = Mathf.Max(1, Mathf.RoundToInt(ScoreIconSize * k));
        _scoreBadge.FontSize = Mathf.Max(1, Mathf.RoundToInt(ScoreFontSize * k));
        _scoreBadge.NumberOffset = ScoreNumberOffset * k;
        _scoreBadge.Padding = ScorePadding * k;
        _scoreBadge.PairGap = ScorePairGap * k;
        _scoreBadge.GroupGap = ScoreGroupGap * k;
        _scoreBadge.CornerRadius = ScoreCornerRadius * k;
        _scoreBadge.StrokeWidth = ScoreStrokeWidth * k;
        _scoreBadge.Fill = ScoreBoxColour;
        _scoreBadge.Show(scores, points, colour);
    }

    public bool IsOverScoreBadge(Vector2 globalPoint) => Visible && _scoreBadge is not null && _scoreBadge.IsOver(globalPoint);
    public bool ScorePreviewOpen => _scoreBadge?.IsOpen ?? false;
    public void SetScorePreviewOpen(bool open) => _scoreBadge?.SetOpen(open);
}
