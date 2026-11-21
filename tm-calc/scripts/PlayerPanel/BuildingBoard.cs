using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The building slots of a faction board as one panel (FactionBoardArt): stronghold and sanctuary on top, trading
// posts and temples, then dwellings. Everything on it is made in code from FactionBoardArt.Layout (board pixels,
// origin at the board's top left; scale this node to size the board):
//   a marker at the centre of each slot, where a building still in the bank sits (Slot(kind, i)); under it, the
//     income that slot gives once emptied (covered by the building until then);
//   each kind's cost (in its cost column; a trading post's coins with and without a neighbour, e.g. "3/6"; priests one
//     icon each, side by side) and power value (in its power column, one small token per point);
//   the faction's base income, left of the dwellings;
//   a blank favour tile, see-through (FavourOpacity), between the sanctuary and the temples, with "1x" over it:
//     each of those buildings brings a favour;
//   what the stronghold brings besides its income, in icons, in the free space right of its slot (see
//     ShowStrongholdBonus).
// Buildings in the slots are drawn BuildingSize board pixels per dwelling length, whatever this node's scale
// (Main sizes them with PieceScale). The frame is in the faction's colour. In the editor, PreviewFaction is shown.
// Nothing made here is saved with the scene.
[Tool]
[GlobalClass]
public partial class BuildingBoard : Node2D
{
    [ExportGroup("Board")]
    [Export] public Faction PreviewFaction { get => _preview; set { _preview = value; Changed(); } }
    // With the Landscapes expansion: the dwelling row has a well for the special landscape's token
    // (FactionBoardArt.LandscapeLayout; LandscapeSlot). Main sets it from the game's options.
    [Export] public bool Landscapes { get => _landscapes; set { if (_landscapes != value) { _landscapes = value; Changed(); } } }
    // A dwelling's length in board pixels, for buildings in the slots (the slots are made for LayoutUnit, 38).
    [Export] public float BuildingSize { get => _buildingSize; set { _buildingSize = value; Changed(); } }
    // How many times the board's natural size its texture is made, for sharpness if this node is scaled up.
    [Export(PropertyHint.Range, "0.5,4,0.25")] public float Resolution { get => _resolution; set { _resolution = value; Changed(); } }

    [ExportGroup("Income")]
    [Export] public int IncomeIconSize { get => _incomeIconSize; set { _incomeIconSize = value; Changed(); } }
    [Export] public float IncomeSpacing { get => _incomeSpacing; set { _incomeSpacing = value; Changed(); } }
    [Export] public bool IncomeVertical { get => _incomeVertical; set { _incomeVertical = value; Changed(); } }
    [Export] public int IncomeFontSize { get => _incomeFontSize; set { _incomeFontSize = value; Changed(); } }
    // Nudges the income from the slot's centre (the base income from its space's centre).
    [Export] public Vector2 IncomeOffset { get => _incomeOffset; set { _incomeOffset = value; Changed(); } }
    // Passed to every slot's income bundle and the base income (see ResourceBundle): the kinds that write a 1, and
    // one hand under the whole income instead of one per icon.
    [Export] public string IncomeShowOnesExcept { get => _incomeShowOnesExcept; set { _incomeShowOnesExcept = value; Changed(); } }
    [Export] public bool IncomeOneHand { get => _incomeOneHand; set { _incomeOneHand = value; Changed(); } }
    // Kinds drawn one icon each instead of one icon and a number (e.g. "priests"), stacked IncomeRepeatStep apart (see
    // ResourceBundle.RepeatStep: (0, 0) puts them in a line, IncomeSpacing's way, RepeatSpacing apart).
    [Export] public string IncomeRepeatIcons { get => _incomeRepeatIcons; set { _incomeRepeatIcons = value; Changed(); } }
    [Export] public Vector2 IncomeRepeatStep { get => _incomeRepeatStep; set { _incomeRepeatStep = value; Changed(); } }
    // The trading posts' income only: nudges its hand (ResourceBundle.HandOffset), up so the coin just overlaps it.
    [Export] public Vector2 TradingPostHandOffset { get => _tpHandOffset; set { _tpHandOffset = value; Changed(); } }

    [ExportGroup("Cost")]
    [Export] public int CostIconSize { get => _costIconSize; set { _costIconSize = value; Changed(); } }
    [Export] public float CostSpacing { get => _costSpacing; set { _costSpacing = value; Changed(); } }
    [Export] public bool CostVertical { get => _costVertical; set { _costVertical = value; Changed(); } }
    [Export] public int CostFontSize { get => _costFontSize; set { _costFontSize = value; Changed(); } }
    // Priests in a cost (the Swarmlings' and Darklings' sanctuaries) are drawn one per priest, side by side this far apart.
    [Export] public float CostRepeatSpacing { get => _costRepeatSpacing; set { _costRepeatSpacing = value; Changed(); } }
    // The numbers in a cost, from the middle of their icon (ResourceBundle.CountOffset; its default is (0, -2)).
    [Export] public Vector2 CostCountOffset { get => _costCountOffset; set { _costCountOffset = value; Changed(); } }

    [ExportGroup("Favour")]
    // The tile's size (of its natural 220 × 112), its opacity, and a nudge from its place: centred across the
    // sanctuary's free space, halfway down the gap between the sanctuary and the temples.
    [Export] public float FavourScale { get => _favourScale; set { _favourScale = value; Changed(); } }
    [Export(PropertyHint.Range, "0,1,0.05")] public float FavourOpacity { get => _favourOpacity; set { _favourOpacity = value; Changed(); } }
    [Export] public Vector2 FavourOffset { get => _favourOffset; set { _favourOffset = value; Changed(); } }
    [Export] public string FavourText { get => _favourText; set { _favourText = value; Changed(); } }
    [Export] public int FavourFontSize { get => _favourFontSize; set { _favourFontSize = value; Changed(); } }

    [ExportGroup("Town")]
    // The three-hex town icon (the sanctuary forms a town with fewer buildings), just right of the sanctuary's slot:
    // its size in pixels, and a nudge from there.
    [Export] public int TownIconSize { get => _townIconSize; set { _townIconSize = value; Changed(); } }
    [Export] public Vector2 TownIconOffset { get => _townIconOffset; set { _townIconOffset = value; Changed(); } }

    [ExportGroup("Stronghold bonus")]
    // Icons' size and their numbers' font size; the action octagon's size as a share of the panel's (Art.OctagonSize);
    // the space between the parts of one effect and between effects; the favour tile's scale (Auren).
    // Everything is shrunk together if it does not fit the free space (less StrongholdBonusPadding all round).
    [Export] public int StrongholdIconSize { get => _shIconSize; set { _shIconSize = value; Changed(); } }
    [Export] public int StrongholdFontSize { get => _shFontSize; set { _shFontSize = value; Changed(); } }
    [Export] public float StrongholdActionScale { get => _shActionScale; set { _shActionScale = value; Changed(); } }
    [Export] public float StrongholdSpacing { get => _shSpacing; set { _shSpacing = value; Changed(); } }
    [Export] public float StrongholdEffectGap { get => _shEffectGap; set { _shEffectGap = value; Changed(); } }
    [Export] public float StrongholdFavourScale { get => _shFavourScale; set { _shFavourScale = value; Changed(); } }
    [Export] public float StrongholdBonusPadding { get => _shPadding; set { _shPadding = value; Changed(); } }
    [Export] public Vector2 StrongholdBonusOffset { get => _shOffset; set { _shOffset = value; Changed(); } }
    // Repeated icons: the Halflings' spades this far apart in a row; the Darklings' workers and priests each a slanting
    // stack, one icon to the next this far across and down (overlapping).
    [Export] public float StrongholdRepeatSpacing { get => _shRepeatSpacing; set { _shRepeatSpacing = value; Changed(); } }
    [Export] public Vector2 StrongholdDiagonalStep { get => _shDiagonal; set { _shDiagonal = value; Changed(); } }
    // Nudges for numbers: on the action octagon (Auren's 2) and on VP (Cultists, Engineers).
    [Export] public Vector2 StrongholdActionCountOffset { get => _shActionCount; set { _shActionCount = value; Changed(); } }
    [Export] public Vector2 StrongholdPointsCountOffset { get => _shPointsCount; set { _shPointsCount = value; Changed(); } }
    // The upright rule between the Alchemists' two effects: the pass tiles' rule (Icon.PassTileRuleUpright) drawn at this
    // icon size (it runs nearly the icon's height, diamonds included).
    [Export] public float StrongholdRuleHeight { get => _shRuleHeight; set { _shRuleHeight = value; Changed(); } }
    // The Engineers' bridge between two buildings (a picture of four hexes, so bigger than the other icons).
    [Export] public int StrongholdBridgeIconSize { get => _shBridgeSize; set { _shBridgeSize = value; Changed(); } }
    // Fire & Ice's pictures: the Ice Maidens' temple on ice, the Dragonlords' tokens for every player, the Acolytes'
    // priest to a zone, the Yetis' power action taken again (each a small scene, so bigger than the other icons).
    [Export] public int StrongholdPictureSize { get => _shPictureSize; set { _shPictureSize = value; Changed(); } }
    // The Fakirs' carpet in their stronghold bonus, of StrongholdIconSize (the carpet is drawn twice an icon's size).
    [Export] public float CarpetBonusScale { get => _carpetBonusScale; set { _carpetBonusScale = value; Changed(); } }

    [ExportGroup("Text")]
    // Every number and word on the board (incomes, costs, the stronghold's bonus, "1x"): this colour, outlined in that.
    [Export] public Color TextColour { get => _textColour; set { _textColour = value; Changed(); } }
    [Export] public Color TextOutlineColour { get => _textOutline; set { _textOutline = value; Changed(); } }

    [ExportGroup("Power")]
    [Export] public int PowerIconSize { get => _powerIconSize; set { _powerIconSize = value; Changed(); } }
    [Export] public float PowerSpacing { get => _powerSpacing; set { _powerSpacing = value; Changed(); } }

    [ExportGroup("Landscape and dimming")]
    // The landscape token's size in the well, in board pixels: the well less LandscapeMargin all round.
    [Export] public float LandscapeMargin { get; set; } = 3f;
    [Export] public float DimmedOpacity { get; set; } = 0.5f;
    [ExportGroup("")]
    private bool _landscapes;

    // The faction shown, or null before ShowFaction (then PreviewFaction).
    public Faction? ShownFaction { get; private set; }

    private Faction _preview = Faction.Giants;
    private float _buildingSize = FactionBoardArt.LayoutUnit;
    private float _resolution = 1f;
    private int _incomeIconSize = 22;
    private float _incomeSpacing = 28f;
    private bool _incomeVertical;
    private int _incomeFontSize = 20;
    private Vector2 _incomeOffset = Vector2.Zero;
    private string _incomeShowOnesExcept = "power";
    private bool _incomeOneHand = true;
    private string _incomeRepeatIcons = "priests";
    private Vector2 _incomeRepeatStep = new(-12, 8);
    private Vector2 _tpHandOffset = new(0, -7);
    private Vector2 _costCountOffset = new(0, -2);
    private int _costIconSize = 26;
    private float _costSpacing = 32f;
    private bool _costVertical = true;
    private int _costFontSize = 22;
    private int _powerIconSize = 18;
    private int _townIconSize = 48;
    private Vector2 _townIconOffset = Vector2.Zero;
    private float _favourScale = 0.4f;
    private float _favourOpacity = 0.8f;
    private Vector2 _favourOffset = Vector2.Zero;
    private string _favourText = "1x";
    private int _favourFontSize = 26;
    private float _powerSpacing = 20f;
    private int _shIconSize = 30;
    private int _shFontSize = 22;
    private float _shActionScale = 0.7f;
    private float _shSpacing = 6f;
    private float _shEffectGap = 14f;
    private float _shFavourScale = 0.3f;
    private float _shPadding = 4f;
    private Vector2 _shOffset = Vector2.Zero;
    private float _shRepeatSpacing = 26f;
    private Vector2 _shDiagonal = new(12, 9);
    private Vector2 _shActionCount = new(0, -5);
    private Vector2 _shPointsCount = new(0, -4);
    private float _shRuleHeight = 48f;
    private int _shBridgeSize = 60;
    private int _shPictureSize = 44;
    private float _carpetBonusScale = 0.8f;
    private float _costRepeatSpacing = 20f;
    private Color _textColour = Colors.Black;
    private Color _textOutline = Colors.White;

    // The nodes made in code that are used again later, kept in a plain object rather than fields of their own. Godot
    // saves and restores a [Tool] script's Godot-object fields across an assembly reload, but only after the restored
    // exports have rebuilt the board (their setters call Build), so such fields would be left pointing at the old,
    // freed nodes (ObjectDisposedException at the next reload). A plain object isn't saved, so it keeps the new ones.
    private sealed class MadeNodes
    {
        public Sprite2D Art;
        public ResourceBundle BaseIncome;
        public Label FavourLabel;      // FavourText, or "2x" for the Chaos Magicians
        public Sprite2D FavourTreasure; // the Goblins' treasure with the favour (temples, sanctuary): shown only for them
        public Node2D StrongholdBonus;
        public Faction? BonusShownFor; // the stronghold bonus is rebuilt only when the faction changes
        public Marker2D LandscapeSlot; // the landscape's well, with Landscapes
        public Sprite2D ExtraWell;      // the Changelings' 9th dwelling's slot (shown only for them)
        public List<Node2D> Dimmed = new(); // the stronghold bonus's effects at DimmedOpacity once it is built
        public List<Node2D> Milestones = new(); // the Atlanteans' three thresholds, dimmed one by one as reached
    }
    private MadeNodes _made = new();
    private readonly Dictionary<(PieceKind Kind, int Slot), Marker2D> _slots = new();
    private readonly Dictionary<(PieceKind Kind, int Slot), ResourceBundle> _slotIncome = new();
    private readonly Dictionary<PieceKind, ResourceBundle> _costs = new();
    private readonly Dictionary<PieceKind, ResourceBundle> _powers = new();
    // Marks the nodes made in code (internal children: not in the scene tree, never saved).
    private const string MadeMeta = "building_board_made";

    public override void _Ready() => Build();

    // Where a building still in the bank sits: the centre of slot `slot` (from the left) of its kind's row.
    public Node2D Slot(PieceKind kind, int slot) => _slots.GetValueOrDefault((kind, slot));

    private IReadOnlyDictionary<PieceKind, SlotRow> Rows => FactionBoardArt.LayoutFor(Landscapes).Rows;

    // With Landscapes: the centre of the landscape's well, where its token sits until it is placed (null without).
    public Node2D LandscapeSlot => _made.LandscapeSlot;

    public float LandscapeTokenSize =>
        Rows.TryGetValue(PieceKind.Dwelling, out SlotRow row) && row.Landscape is Rect2 well
            ? Mathf.Min(well.Size.X, well.Size.Y) - 2 * LandscapeMargin
            : 0f;

    // ---------- Rows as choices (the Goblins' treasure) ----------
    // The rows lit (yellow, a glint each): the stronghold's and the sanctuary's as one choice (Stronghold), a box round
    // both. RowAt: the row (Stronghold for either big building) under a point.
    private static readonly PieceKind[] ChoiceRows = { PieceKind.Stronghold, PieceKind.TradingPost, PieceKind.Temple, PieceKind.Dwelling };

    public void HighlightRows(IReadOnlyCollection<PieceKind> kinds)
    {
        foreach (PieceKind kind in ChoiceRows)
        {
            bool on = kinds.Contains(kind);
            var holder = GetNodeOrNull<Node2D>($"RowHighlight{kind}");
            if (holder is null)
            {
                if (!on)
                    continue;
                holder = new Node2D { Name = $"RowHighlight{kind}", ZIndex = 5 };
                AddChild(holder, false, InternalMode.Back);
            }
            if (ChoiceRect(kind) is Rect2 rect)
                EdgeHighlight.SetOnArea(holder, on, rect, 8f, 1, HighlightArt.Colour.Yellow);
        }
    }

    public PieceKind? RowAt(Vector2 globalPoint)
    {
        Vector2 local = ToLocal(globalPoint);
        foreach (PieceKind kind in ChoiceRows)
            if (ChoiceRect(kind) is Rect2 rect && rect.HasPoint(local))
                return kind;
        return null;
    }

    private Rect2? ChoiceRect(PieceKind kind)
    {
        if (!Rows.TryGetValue(kind, out SlotRow row))
            return null;
        Rect2 rect = row.Panel;
        if (kind == PieceKind.Stronghold && Rows.TryGetValue(PieceKind.Sanctuary, out SlotRow sanctuary))
            rect = rect.Merge(sanctuary.Panel);
        return rect;
    }

    // A slot's well, round its marker (Slot), in the marker's pixels; null if the board has no such slot. The Changelings'
    // 9th dwelling's spot is a dwelling slot's size.
    public Rect2? SlotRect(PieceKind kind, int slot)
    {
        if (!Rows.TryGetValue(kind, out SlotRow row) || row.Slots.Length == 0 || Slot(kind, slot) is null)
            return null;
        Vector2 size = row.Slots[Mathf.Min(slot, row.Slots.Length - 1)].Size;
        return new Rect2(-size / 2, size);
    }

    // Whether a node is one of this board's slots (buildings there are drawn at BuildingSize).
    public bool IsSlot(Node node) => node is Marker2D marker && _slots.ContainsValue(marker);

    // How much a building's texture is scaled, in this board's pixels, to be drawn BuildingSize per dwelling length.
    public float PieceScale => BuildingSize / PieceArt.Unit;

    // The faction's colours and numbers: frame, slot incomes, base income, costs, power values. frame: the frame's
    // colour, if not the faction's own (the variable factions take their starting terrain's: Art.PieceColour).
    public void ShowFaction(Faction faction, Color? frame = null)
    {
        ShownFaction = faction;
        _frame = frame;
        Paint(faction);
    }
    private Color? _frame;

    private void Changed()
    {
        if (IsInsideTree())
            Build();
    }

    private void Build()
    {
        // Found by their mark rather than remembered, so none are left behind when scripts are reloaded.
        foreach (Node child in GetChildren(includeInternal: true))
            if (child.HasMeta(MadeMeta))
                child.QueueFree();
        _slots.Clear();
        _made = new MadeNodes();
        _slotIncome.Clear();
        _costs.Clear();
        _powers.Clear();

        _made.Art = new Sprite2D { Centered = false, Scale = Vector2.One / Resolution };
        Add(this, _made.Art);

        foreach (var (kind, row) in Rows)
        {
            int count = Buildings.PerPlayer(kind);
            if (count > row.Slots.Length)
                GD.PushWarning($"{Name}: the board has {row.Slots.Length} {kind} slots, but a player has {count}.");
            for (var i = 0; i < System.Math.Min(count, row.Slots.Length); i++)
            {
                var marker = new Marker2D { Name = $"{kind}Slot{i}", Position = row.Slots[i].GetCenter() };
                Add(this, marker);
                _slots[(kind, i)] = marker;
                ResourceBundle income = IncomeBundle(IncomeOffset);
                if (kind == PieceKind.TradingPost)
                {
                    income.Order = "power, coins"; // power first on the trading posts' income
                    income.HandOffset = TradingPostHandOffset;
                }
                _slotIncome[(kind, i)] = Add(marker, income);
            }
            _costs[kind] = Add(this, new ResourceBundle
            {
                IconSize = CostIconSize, Spacing = CostSpacing, Vertical = CostVertical, FontSize = CostFontSize,
                FontColour = TextColour, OutlineColour = TextOutlineColour,
                Centred = true, Position = row.Cost.GetCenter(), CountOffset = CostCountOffset,
                RepeatIconsExcept = "priests", RepeatAcross = true, RepeatSpacing = CostRepeatSpacing,
            });
            _powers[kind] = Add(this, new ResourceBundle
            {
                IconSize = PowerIconSize, RepeatIcons = true, RepeatSpacing = PowerSpacing, Vertical = true,
                Centred = true, Position = row.Power.GetCenter(),
            });
            if (row.BaseIncome is Rect2 baseArea)
                _made.BaseIncome = Add(this, IncomeBundle(baseArea.GetCenter() + IncomeOffset));
            if (row.Landscape is Rect2 well)
                _made.LandscapeSlot = Add(this, new Marker2D { Name = "LandscapeSlot", Position = well.GetCenter() });
        }
        AddExtraDwellingSlot();
        AddFavourTile();
        AddTownIcon();
        _made.StrongholdBonus = Add(this, new Node2D { Name = "StrongholdBonus" });
        Paint(ShownFaction ?? PreviewFaction);
    }

    // Fan factions: the Changelings' 9th dwelling, in the middle of the stronghold row's free space, its income (2 workers,
    // once built) under it as under the other slots. Slot(Dwelling, 8); shown only for them (Paint).
    private void AddExtraDwellingSlot()
    {
        if (!Rows.TryGetValue(PieceKind.Stronghold, out SlotRow row) || row.Spare is not Rect2 spare)
            return;
        int slot = Buildings.PerPlayer(PieceKind.Dwelling);
        var marker = new Marker2D { Name = $"DwellingSlot{slot}", Position = spare.GetCenter() };
        // its well, a dwelling slot's size, under the dwelling and its income
        if (Rows.TryGetValue(PieceKind.Dwelling, out SlotRow dwellings) && dwellings.Slots.Length > 0)
            _made.ExtraWell = Add(this, new Sprite2D
            {
                Texture = FactionBoardArt.SlotWell(dwellings.Slots[0].Size, Resolution), Scale = Vector2.One / Resolution,
                Position = spare.GetCenter(), Visible = false,
            });
        Add(this, marker);
        _slots[(PieceKind.Dwelling, slot)] = marker;
        _slotIncome[(PieceKind.Dwelling, slot)] = Add(marker, IncomeBundle(IncomeOffset));
    }

    // The three-hex town icon: its left edge 6 px right of the sanctuary's slot, level with the slot's middle.
    private void AddTownIcon()
    {
        if (!Rows.TryGetValue(PieceKind.Sanctuary, out SlotRow sanctuary) || sanctuary.Slots.Length == 0)
            return;
        Rect2 slot = sanctuary.Slots[0];
        var at = new Vector2(slot.End.X + 6 + TownIconSize / 2f, slot.GetCenter().Y) + TownIconOffset;
        Add(this, new Sprite2D { Texture = IconArt.Texture(Icon.ThreeHexTown, TownIconSize), Position = at });
    }

    // The see-through favour tile between the sanctuary and the temples, its text on top (not see-through).
    private void AddFavourTile()
    {
        if (!Rows.TryGetValue(PieceKind.Sanctuary, out SlotRow sanctuary)
            || !Rows.TryGetValue(PieceKind.Temple, out SlotRow temples))
            return;
        var at = new Vector2((sanctuary.Spare ?? sanctuary.Panel).GetCenter().X,
            (sanctuary.Panel.End.Y + temples.Panel.Position.Y) / 2) + FavourOffset;
        Texture2D texture = IconArt.Texture(BlankTile.Favour, FavourScale);
        Add(this, new Sprite2D { Texture = texture, Position = at, Modulate = new Color(1, 1, 1, FavourOpacity) });
        Vector2 size = texture?.GetSize() ?? new Vector2(88, 45);
        var label = new Label
        {
            Text = FavourText,
            Position = at - size / 2,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", FavourFontSize);
        label.AddThemeColorOverride("font_color", TextColour);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", TextOutlineColour);
        _made.FavourLabel = Add(this, label);
        // Fan factions: the Goblins' treasure, centred on the favour tile's rightmost point, as see-through as the tile.
        _made.FavourTreasure = Add(this, new Sprite2D
        {
            Texture = IconArt.Texture(Icon.GoblinsTreasure, Mathf.Max(8, Mathf.RoundToInt(size.Y * FavourTreasureScale))),
            Position = at + new Vector2(size.X / 2, 0), Modulate = new Color(1, 1, 1, FavourOpacity), Visible = false,
        });
    }

    private const float FavourTreasureScale = 0.9f; // the Goblins' treasure, of the favour tile's height

    private ResourceBundle IncomeBundle(Vector2 position) => new()
    {
        IncomeIcons = true, IconSize = IncomeIconSize, Spacing = IncomeSpacing, Vertical = IncomeVertical,
        FontSize = IncomeFontSize, Centred = true, Position = position, FontColour = TextColour, OutlineColour = TextOutlineColour,
        ShowOnesExcept = IncomeShowOnesExcept, OneHand = IncomeOneHand,
        RepeatIconsExcept = IncomeRepeatIcons, RepeatStep = IncomeRepeatStep,
    };

    private void Paint(Faction faction)
    {
        if (_made.Art is null)
            return;
        _made.Art.Texture = FactionBoardArt.BuildingSlots(_frame ?? Art.PieceColour(Factions.ColourOf(faction)), Resolution, Landscapes);
        if (_made.ExtraWell is not null)
            _made.ExtraWell.Visible = faction == Faction.Changelings;
        Color priests = _frame ?? Art.PieceColour(Factions.ColourOf(faction)); // the incomes' priests in the faction's colour
        foreach (var ((kind, slot), bundle) in _slotIncome)
        {
            bundle.PriestColour = priests;
            if (kind == PieceKind.Stronghold)
            {
                // The Kingdom of Ember's stronghold income: power on top, the worker below (as the trading posts').
                bool ember = faction == Faction.KingdomOfEmber;
                string order = ember ? "power, workers" : "";
                Vector2 hand = ember ? TradingPostHandOffset : Vector2.Zero;
                if (bundle.Order != order)
                    bundle.Order = order;
                if (bundle.HandOffset != hand)
                    bundle.HandOffset = hand;
            }
            var incomes = Factions.SlotIncome(faction, kind);
            bundle.ShowAmounts(slot < incomes.Length ? Amounts.From(incomes[slot])
                : faction == Faction.Changelings && kind == PieceKind.Dwelling // their 9th dwelling's
                ? Amounts.Of(ResourceKind.Worker, Config.ChangelingsExtraDwellingWorkers) : Amounts.None);
        }
        foreach (var (kind, bundle) in _costs)
        {
            Cost cost = Factions.BuildingCost(faction, kind);
            // A trading post costs more with no opponent next to it: its coins written both ways, e.g. "3/6".
            // Fan factions: the Children of the Wyrm pay every structure's coins twice with no opponent next to it
            // ("1/2", "3/6", "4/8", "5/10").
            Dictionary<ResourceKind, string> texts = faction == Faction.ChildrenOfTheWyrm
                ? new() { [ResourceKind.Coin] = $"{cost.Coins}/{2 * cost.Coins}" }
                : kind == PieceKind.TradingPost
                ? new() { [ResourceKind.Coin] = $"{cost.Coins}/{cost.Coins + Factions.LonelyTradingPostSurcharge(faction)}" }
                : null;
            bundle.ShowAmounts(Amounts.From(cost), texts: texts);
        }
        foreach (var (kind, bundle) in _powers)
            bundle.ShowAmounts(Amounts.Of(ResourceKind.PowerValue, Factions.BuildingPower(faction, kind)));
        if (_made.BaseIncome is not null)
            _made.BaseIncome.PriestColour = priests;
        _made.BaseIncome?.ShowAmounts(Amounts.From(Factions.BaseIncome(faction)));
        int favours = Factions.FavoursFor(faction, PieceKind.Temple);
        if (_made.FavourLabel is not null)
            _made.FavourLabel.Text = favours == 1 ? FavourText : $"{favours}x";
        if (_made.FavourTreasure is not null)
            _made.FavourTreasure.Visible = faction == Faction.Goblins;
        ShowStrongholdBonus(faction);
    }

    // ---------- The stronghold's bonus ----------

    // In the free space right of the stronghold's slot, one effect after another (side by side, or one above the
    // other, whichever needs less shrinking to fit):
    //   its action, as a small octagon drawn as on the player panel;
    //   what it brings once, when built (being next to the stronghold says when), e.g. 7 VP, or 3 spades side by side;
    //   what it changes for good, e.g. spade → 2 power (Alchemists), +1 carpet range (Fakirs), a tunnel for 1 worker
    //     (Dwarves), when passing a bridge between two of their buildings → 3 VP (Engineers).
    // The income is not repeated: it is under the slot.
    private void ShowStrongholdBonus(Faction faction)
    {
        if (_made.StrongholdBonus is null || _made.BonusShownFor == faction
            || !Rows.TryGetValue(PieceKind.Stronghold, out SlotRow row) || row.Spare is not Rect2 spare)
            return;
        _made.BonusShownFor = faction;
        foreach (Node old in _made.StrongholdBonus.GetChildren())
        {
            _made.StrongholdBonus.RemoveChild(old); // at once, so it is not measured below
            old.QueueFree();
        }
        _made.Dimmed.Clear();
        _made.Milestones.Clear();

        if (Factions.StrongholdAction(faction) is BonusAction action)
            Effect(ActionOctagon(action, faction));
        Texture2D arrow = IconArt.Texture(Icon.TriggerArrow, StrongholdIconSize);
        bool sideBySide = false;
        switch (faction)
        {
            case Faction.Auren: // a favour, "1x" on it as on the temples' and sanctuary's
                Effect(FavourTile());
                break;
            case Faction.Fakirs: // carpet range +1
                // the carpet then "+1" on its right (not on it); the carpet at CarpetBonusScale of the other icons
                Effect(Amount(Amounts.Of(ResourceKind.CarpetRange, 1), iconSize: Mathf.Max(1, Mathf.RoundToInt(StrongholdIconSize * CarpetBonusScale * 0.8f))),
                    Text("+1"));
                break;
            case Faction.Halflings: // "1x" the spades, side by side
                Effect(Text("1x"), Amount(Amounts.Of(ResourceKind.Spade, Config.HalflingsStrongholdSpades), repeat: StrongholdRepeatSpacing * Vector2.Right));
                break;
            case Faction.Cultists:
                Effect(Points(Config.CultistsStrongholdVictoryPoints));
                break;
            case Faction.Alchemists: // "1x" 12 power | spade → 2 power
                Effect(Text("1x"), Amount(Amounts.Of(ResourceKind.Power, Config.AlchemistsStrongholdPower)));
                Effect(Rule());
                Effect(Amount(Amounts.Of(ResourceKind.Spade, 1)), Picture(arrow),
                    Amount(Amounts.Of(ResourceKind.Power, Config.AlchemistsPowerPerSpade)));
                sideBySide = true;
                break;
            case Faction.Darklings: // "1x" up to 3 workers become priests: each a slanting, overlapping stack, at half size
                Effect(Text("1x"),
                    Amount(Amounts.Of(ResourceKind.Worker, Config.DarklingsPriestExchange), repeat: StrongholdDiagonalStep * 0.5f, iconSize: StrongholdIconSize / 2),
                    Picture(arrow),
                    Amount(Amounts.Of(ResourceKind.Priest, Config.DarklingsPriestExchange), repeat: StrongholdDiagonalStep * 0.5f, iconSize: StrongholdIconSize / 2));
                break;
            case Faction.Mermaids: // a shipping level, free
                Effect(Amount(Amounts.Of(ResourceKind.Shipping, 1), plusSigns: true));
                break;
            case Faction.Dwarves: // a tunnel costs 1 worker now
                Effect(Amount(Amounts.Of(ResourceKind.Tunnel, 1), iconSize: Mathf.RoundToInt(StrongholdIconSize * 0.7f)),
                    Amount(Amounts.Of(ResourceKind.Worker, Config.DwarvesTunnelWorkersWithStronghold), showOnes: true));
                break;
            case Faction.Engineers: // when passing, per bridge joining two of their buildings
                Effect(Picture(IconArt.Texture(Icon.OnPass, StrongholdIconSize)), Picture(IconArt.Texture(Icon.BridgeBetween, StrongholdBridgeIconSize)),
                    Picture(arrow), Points(Config.EngineersVictoryPointsPerBridge));
                break;
            case Faction.IceMaidens: // when passing, per temple (on an ice hex, so on the map)
                Effect(Picture(IconArt.Texture(Icon.OnPass, StrongholdIconSize)),
                    Picture(IconArt.Texture(Icon.TempleOnIce, StrongholdPictureSize, Art.PieceColour(Factions.ColourOf(faction)))),
                    Picture(arrow), Points(Config.IceMaidensVictoryPointsPerTemple));
                break;
            case Faction.Yetis: // power actions already taken can be taken again
                Effect(Picture(IconArt.Texture(Icon.PowerActionForever, StrongholdPictureSize * 2)));
                break;
            case Faction.Dragonlords: // a new token in bowl 1 per player
                Effect(Picture(IconArt.Texture(Icon.PlayersToBowl, StrongholdPictureSize * 2)));
                break;
            case Faction.Acolytes: // a priest sent to a track goes 1 step further
                Effect(Picture(IconArt.Texture(Icon.PriestToZone, Mathf.RoundToInt(StrongholdPictureSize * 1.5f))),
                    Amount(Amounts.Of(ResourceKind.AnyCultStep, Config.AcolytesPriestBonusSteps), plusSigns: true));
                break;
            case Faction.Riverwalkers: // "1x" the free bridges, side by side
                Effect(Text("1x"), Amount(Amounts.Of(ResourceKind.Bridge, Config.RiverwalkersStrongholdBridges),
                    repeat: StrongholdRepeatSpacing * Vector2.Right));
                break;
            // Fan factions (their actions: the octagon above).
            case Faction.Treasurers: // everything received may go into the Safe
                Effect(Amount(Amounts.Of(ResourceKind.Priest, 1)),
                    Amount(Amounts.Of(ResourceKind.Worker, 1)),
                    Amount(Amounts.Of(ResourceKind.Coin, 1)),
                    Picture(arrow), Picture(IconArt.Texture(Icon.Safe, StrongholdIconSize)));
                break;
            case Faction.Djinni: // when passing, per own priest on the tracks
                Effect(Picture(IconArt.Texture(Icon.OnPass, StrongholdIconSize)),
                    Picture(IconArt.Texture(Icon.PriestToZone, Mathf.RoundToInt(StrongholdPictureSize * 1.5f))),
                    Picture(arrow),
                    Amount(Amounts.Of(ResourceKind.VictoryPoint,1), showOnes: true));
                break;
            case Faction.Archivists: // two pass tiles
                Effect(Picture(IconArt.Texture(Icon.ArchivistsTwoPassTiles, StrongholdPictureSize * 2)));
                break;
            case Faction.Prospectors: // a golden spade for 3 coins
                Effect(Rule());
                Effect(
                    Amount(Amounts.Of(ResourceKind.Coin, Config.ProspectorsSpadeCoinsWithStronghold)),
                    Picture(arrow),
                    Picture(IconArt.Texture(Icon.GoldenSpade, StrongholdIconSize))
                    );
                break;
            case Faction.Goblins: // a treasure with every town
                Effect(Amount(Amounts.Of(ResourceKind.Town, 1)), Picture(arrow), Amount(Amounts.Of(ResourceKind.Treasure, 1)));
                break;
            case Faction.Wisps: // "1x" 7 VP and a free dwelling on a Lake hex
                Effect(Text("1x"), Points(Config.WispsStrongholdVictoryPoints));
                Effect(Rule());
                Effect(Picture(IconArt.Texture(Icon.DwellingOnLake, StrongholdIconSize, Art.PieceColour(Factions.ColourOf(faction)))));
                break;
            case Faction.ChashDallah: // power actions paid in coins
                Effect(Picture(IconArt.Texture(Icon.ChashPowerAction, StrongholdPictureSize * 2)));
                break;
            case Faction.Enlightened: // conversions doubled
                Effect(Rule());
                Effect(Picture(IconArt.Texture(Icon.EnlightenedConversions, StrongholdPictureSize * 2)));
                break;
            case Faction.ChildrenOfTheWyrm: // "1x" every burned token back into bowl I
                Effect(Rule());
                Effect(Text("1x"), Picture(IconArt.Texture(Icon.WyrmTokensToBowl, StrongholdPictureSize)));
                break;
            case Faction.Atlanteans: // their town's power at 7, 10 and 16 pays out: +1 shipping, 2 steps on every track, 20 VP
            {
                System.Action<Node2D>[] rewards =
                {
                    Amount(Amounts.Of(ResourceKind.Shipping, 1), plusSigns: true), // as the Mermaids'
                    Amount(Amounts.Of(ResourceKind.EveryCultStep, Config.AtlanteansStepsOnEveryTrack)), // as the key token's
                    Points(Config.AtlanteansTownVictoryPoints),
                };
                for (var i = 0; i < rewards.Length; i++)
                    Effect(AtlanteanThreshold(Config.AtlanteansTownThresholds[i], rewards[i]));
                _made.Milestones.AddRange(_made.StrongholdBonus.GetChildren().OfType<Node2D>());
                sideBySide = true;
                break;
            }
            case Faction.SnowShamans: // "1x" a dwelling on every mass of ice
                Effect(Text("1x"), Picture(IconArt.Texture(Icon.SnowShamansIce, StrongholdPictureSize * 2)));
                break;
            case Faction.Firewalkers: // when passing, a point per neighbourhood
                Effect(Picture(IconArt.Texture(Icon.FirewalkersNeighbourhoods, StrongholdPictureSize * 2)));
                break;
            case Faction.KingdomOfEmber: // "1x" 2 lava tokens under any building
                Effect(Picture(IconArt.Texture(Icon.EmberLavaUnder, StrongholdPictureSize * 2, Art.PieceColour(Factions.ColourOf(faction)))),
                    Text($"×{Config.EmberStrongholdLava}"));
                break;
            // (Changelings: the stronghold row's free space holds their 9th dwelling instead.)
            case Faction.DynionGeifr: // a town of three with it; "1x" 2 priests
                Effect(Picture(IconArt.Texture(Icon.ThreeHexTown, StrongholdIconSize)));
                Effect(Rule());
                Effect(Text("1x"), Amount(Amounts.Of(ResourceKind.Priest, Config.DynionGeifrStrongholdPriests),
                    repeat: StrongholdRepeatSpacing * Vector2.Right, iconSize: StrongholdIconSize / 2));
                break;
        }
        // Once the stronghold is built (ShowStrongholdBuilt), the effects that were one-off, are shown elsewhere in the UI
        // or are an action octagon fade; those still in force, not shown anywhere else, stay: the Alchemists' spade → 2
        // power (the last effect), the Dynion Geifr's three-hex town (the first); nothing changes for the rest listed.
        List<Node2D> effects = _made.StrongholdBonus.GetChildren().OfType<Node2D>().ToList();
        if (faction == Faction.Alchemists)
            _made.Dimmed.AddRange(effects.Take(1)); // "1x" 12 power
        else if (faction == Faction.DynionGeifr)
            _made.Dimmed.AddRange(effects.Skip(1)); // "1x" 2 priests
        else if (!NeverDimmed.Contains(faction))
            _made.Dimmed.AddRange(effects);
        FitStrongholdBonus(spare, sideBySide);
        ShowStrongholdBuilt(_strongholdBuilt);
        ShowMilestones(_milestonesReached);
    }

    private int _milestonesReached;

    // The Atlanteans' thresholds paid out so far (PlayerState.AtlanteansRewards): that many, from the left, at
    // DimmedOpacity.
    public void ShowMilestones(int reached)
    {
        _milestonesReached = reached;
        for (var i = 0; i < _made.Milestones.Count; i++)
            if (IsInstanceValid(_made.Milestones[i]))
                _made.Milestones[i].Modulate = Colors.White with { A = i < reached ? DimmedOpacity : 1f };
    }

    // One of the Atlanteans' thresholds, one above the other: the eight lake hexes with a power token on them and the
    // threshold on that, a brace (the round tiles', turned to point down), and the reward.
    private System.Action<Node2D> AtlanteanThreshold(int power, System.Action<Node2D> reward) => effect =>
    {
        var section = new Node2D { Name = $"Threshold{power}" };
        effect.AddChild(section);
        var hexes = new Sprite2D { Texture = IconArt.Texture(Icon.AtlanteansHexes, StrongholdPictureSize) };
        section.AddChild(hexes);
        Vector2 hexSize = hexes.Texture.GetSize();
        section.AddChild(new Sprite2D { Texture = IconArt.Texture(Icon.PowerToken, Mathf.RoundToInt(hexSize.Y * 0.85f)) });
        var number = new Node2D();
        section.AddChild(number);
        Text(power.ToString())(number);
        int braceSize = Mathf.Max(8, Mathf.RoundToInt(hexSize.X * 0.9f));
        section.AddChild(new Sprite2D
        {
            Texture = IconArt.Texture(Icon.EndOfRoundBrace, braceSize), Rotation = Mathf.Pi / 2,
            Position = new Vector2(0, hexSize.Y / 2 + braceSize * 0.13f),
        });
        var holder = new Node2D();
        section.AddChild(holder);
        reward(holder);
        Rect2 drawn = Arrangement.Extent(holder);
        holder.Position = new Vector2(-drawn.GetCenter().X, hexSize.Y / 2 + braceSize * 0.28f - drawn.Position.Y);
    };

    private static readonly HashSet<Faction> NeverDimmed = new()
    {
        Faction.Engineers, Faction.IceMaidens, Faction.Yetis, Faction.Acolytes, Faction.Treasurers, Faction.Djinni,
        Faction.Archivists, Faction.Goblins, Faction.ChashDallah, Faction.Firewalkers, Faction.Changelings,
        Faction.Atlanteans, // (their thresholds dim one by one instead: ShowMilestones)
    };

    private bool _strongholdBuilt;

    // The stronghold built (or not): its fading effects at DimmedOpacity (or full).
    public void ShowStrongholdBuilt(bool built)
    {
        _strongholdBuilt = built;
        foreach (Node2D effect in _made.Dimmed)
            if (IsInstanceValid(effect))
                effect.Modulate = Colors.White with { A = built ? DimmedOpacity : 1f };
    }

    // Side by side or one above the other (whichever is shrunk less to fit, unless sideBySide); centred in the free
    // space.
    private void FitStrongholdBonus(Rect2 spare, bool sideBySide)
    {
        Node2D[] effects = _made.StrongholdBonus.GetChildren().OfType<Node2D>().ToArray();
        _made.StrongholdBonus.Scale = Vector2.One;
        _made.StrongholdBonus.Position = Vector2.Zero;
        if (effects.Length == 0)
            return;
        Vector2 room = spare.Size - Vector2.One * 2 * StrongholdBonusPadding;
        float FitFor(bool vertical)
        {
            foreach (Node2D effect in effects)
                effect.Position = Vector2.Zero; // each is centred on its origin (Effect), so the line runs through them
            Arrangement.InALine(StrongholdEffectGap, vertical, effects);
            Vector2 size = ExtentOf(effects).Size;
            return Mathf.Min(1f, Mathf.Min(room.X / Mathf.Max(1f, size.X), room.Y / Mathf.Max(1f, size.Y)));
        }
        float fit = FitFor(vertical: false);
        if (!sideBySide)
        {
            float down = FitFor(vertical: true); // left laid out one above the other
            if (fit > down)
                FitFor(vertical: false); // laid out side by side again
            else
                fit = down;
        }
        Rect2 drawn = ExtentOf(effects);
        _made.StrongholdBonus.Scale = Vector2.One * fit;
        _made.StrongholdBonus.Position = spare.GetCenter() + StrongholdBonusOffset - drawn.GetCenter() * fit;
        // Text kept at the board's own size however much the bonus was shrunk (the "1x" on the Auren's favour).
        foreach (Label label in _made.StrongholdBonus.FindChildren("*", "Label", owned: false).OfType<Label>())
            if (label.HasMeta(KeepSizeMeta))
            {
                label.PivotOffset = label.Size / 2;
                label.Scale = Vector2.One / fit;
            }
    }

    private static Rect2 ExtentOf(IEnumerable<Node2D> nodes)
    {
        Rect2? all = null;
        foreach (Node2D node in nodes)
        {
            Rect2 drawn = Arrangement.Extent(node);
            drawn.Position += node.Position;
            all = all?.Merge(drawn) ?? drawn;
        }
        return all ?? new Rect2();
    }

    // One effect: its parts in a row, each centred on the row's middle line, the whole centred on the effect's origin.
    private void Effect(params System.Action<Node2D>[] parts)
    {
        var effect = new Node2D();
        _made.StrongholdBonus.AddChild(effect);
        foreach (System.Action<Node2D> part in parts)
            part(effect); // made in the tree, so amounts and text can be measured
        Node2D[] made = effect.GetChildren().OfType<Node2D>().ToArray();
        Arrangement.InARow(StrongholdSpacing, made);
        Rect2 drawn = ExtentOf(made);
        foreach (Node2D part in made)
            part.Position -= drawn.GetCenter();
    }

    private static System.Action<Node2D> Picture(Texture2D texture) => effect => effect.AddChild(new Sprite2D { Texture = texture });

    // An amount, its numbers written as ResourceBundle does. repeat: each icon drawn that many times instead of a
    // number, this far apart (and that way: e.g. right for a row, down and right for a slanting stack).
    // iconSize: the icons' size, if not StrongholdIconSize.
    private System.Action<Node2D> Amount(Amounts amounts, bool plusSigns = false, bool showOnes = false, Vector2? repeat = null,
        Vector2? countOffset = null, int? iconSize = null) => effect =>
    {
        int size = iconSize ?? StrongholdIconSize;
        var bundle = new ResourceBundle
        {
            IconSize = size, Spacing = size + 6, FontSize = StrongholdFontSize,
            FontColour = TextColour, OutlineColour = TextOutlineColour,
            Centred = true, PlusSigns = plusSigns, ShowOnes = showOnes, CountOffset = countOffset ?? Vector2.Zero,
        };
        if (repeat is Vector2 step && step != Vector2.Zero)
        {
            bundle.RepeatIcons = true;
            bundle.Step = step.Normalized() * (size + 6); // the line's way; one kind, so its length is not used
            bundle.RepeatSpacing = step.Length();
        }
        effect.AddChild(bundle);
        bundle.ShowAmounts(amounts);
    };

    // VP, the number raised by StrongholdPointsCountOffset so it sits on the VP icon.
    private System.Action<Node2D> Points(int points) =>
        Amount(Amounts.Of(ResourceKind.VictoryPoint, points), countOffset: StrongholdPointsCountOffset);

    // Words, e.g. "1x": TextColour outlined in TextOutlineColour, like the numbers. Measured by Arrangement (its
    // MeasuredText label).
    private System.Action<Node2D> Text(string text) => effect =>
    {
        var holder = new Node2D();
        effect.AddChild(holder);
        var label = new Label { Name = Arrangement.MeasuredText, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", StrongholdFontSize);
        label.AddThemeColorOverride("font_color", TextColour);
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", TextOutlineColour);
        holder.AddChild(label);
        label.ResetSize();
        label.Position = -label.Size / 2;
    };

    // The favour tile (StrongholdFavourScale), with FavourText on it at the size of the temples' and sanctuary's
    // (FavourFontSize, not shrunk with the bonus: see FitStrongholdBonus). Measured by its tile (named Background).
    private const string KeepSizeMeta = "building_board_keep_size";
    private System.Action<Node2D> FavourTile() => effect =>
    {
        var holder = new Node2D();
        effect.AddChild(holder);
        Texture2D texture = IconArt.Texture(BlankTile.Favour, StrongholdFavourScale);
        holder.AddChild(new Sprite2D { Name = "Background", Texture = texture, Modulate = new Color(1, 1, 1, FavourOpacity) });
        Vector2 size = texture?.GetSize() ?? new Vector2(66, 34);
        var label = new Label
        {
            Text = FavourText, Position = -size / 2, Size = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetMeta(KeepSizeMeta, true);
        label.AddThemeFontSizeOverride("font_size", FavourFontSize);
        label.AddThemeColorOverride("font_color", TextColour);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", TextOutlineColour);
        holder.AddChild(label);
    };

    // The pass tiles' rule, upright, between two effects (StrongholdRuleHeight).
    private System.Action<Node2D> Rule() => effect =>
        effect.AddChild(new Sprite2D { Texture = IconArt.Texture(Icon.PassTileRuleUpright, Mathf.Max(1, Mathf.RoundToInt(StrongholdRuleHeight))) });

    // The action's octagon, drawn as on the player panel (PlayerPanel.CreateOctagon): an amount (two spades, two
    // cult steps; its number raised by StrongholdActionCountOffset) or the action's icon (pieces in the faction's
    // colour), at StrongholdActionScale. Never crossed out: this is the board, not the turn.
    private System.Action<Node2D> ActionOctagon(BonusAction action, Faction faction) => effect =>
    {
        var octagon = new Node2D { Name = "ActionOctagon", Scale = Vector2.One * StrongholdActionScale };
        effect.AddChild(octagon);
        octagon.AddChild(new Sprite2D { Name = "Octagon", Texture = Art.BonusOctagonTexture() }); // measured by Arrangement
        if (Art.BonusActionContent(action) is Amounts content)
        {
            var bundle = new ResourceBundle
            {
                IconSize = 41, RepeatIconsExcept = "spades", RepeatSpacing = 12f, ShowOnes = true, Centred = true, // as PlayerPanel's defaults
                FontColour = TextColour, OutlineColour = TextOutlineColour,
                CountOffset = StrongholdActionCountOffset,
            };
            octagon.AddChild(bundle);
            bundle.ShowAmounts(content);
        }
        else
            octagon.AddChild(new Sprite2D { Texture = Art.BonusActionIcon(action, faction) });
    };

    private static T Add<T>(Node parent, T node) where T : Node
    {
        node.SetMeta(MadeMeta, true);
        parent.AddChild(node, false, InternalMode.Back);
        return node;
    }
}
