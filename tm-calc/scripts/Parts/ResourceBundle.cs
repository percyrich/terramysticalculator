using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// Draws an Amounts in a row, left to right: each kind as its icon with its number written over it, e.g.
// "1 priest, 2 workers, 5 coins" (a 1 is left off, so "1 priest" is just the priest icon). With RepeatIcons,
// each kind is drawn as that many icons instead (e.g. a building's power value as 2 small tokens).
// Two kinds are always drawn as columns, without numbers: buildings (one small icon each, sharing one place)
// and steps on a track (that many track icons).
// With Kinds set, the bundle always shows those kinds, in that order, zeros and ones included: for things
// that are counted rather than given (a player's coins, workers, VP).
// The same node is used everywhere amounts are shown; ConversionView and TriggerView are built from it.
// Code sets what it shows with ShowAmounts(); Text is shown until then, and in the editor.
[Tool]
[GlobalClass]
public partial class ResourceBundle : Node2D
{
    [ExportGroup("Content")]
    // What to show until code sets it (in the editor too), e.g. "1 priest, 2 workers, 5 coins". See Amounts.Parse.
    [Export] public string Text { get => _text; set { _text = value; Changed(); } }
    // If set, e.g. "workers, coins, VP": always these kinds, in this order, with every number written.
    [Export] public string Kinds { get => _kinds; set { _kinds = value; Changed(); } }
    // Without Kinds: these kinds first, in this order (e.g. "power, coins"), then the rest in the usual order.
    [Export] public string Order { get => _order; set { _order = value; Changed(); } }
    [ExportGroup("What is shown")]
    // Draw each kind as that many icons, without a number (for small counts, e.g. power value).
    [Export] public bool RepeatIcons { get => _repeat; set { _repeat = value; Changed(); } }
    // Kinds that do the opposite of RepeatIcons, e.g. "coins, workers" (names as in Kinds).
    [Export] public string RepeatIconsExcept { get => _repeatExcept; set { _repeatExcept = value; Changed(); } }
    // Write a 1 too (normally left off, the icon alone meaning one).
    [Export] public bool ShowOnes { get => _showOnes; set { _showOnes = value; Changed(); } }
    // Kinds that do the opposite of ShowOnes, e.g. "power" (names as in Kinds).
    [Export] public string ShowOnesExcept { get => _showOnesExcept; set { _showOnesExcept = value; Changed(); } }
    // Write positive numbers with a plus, e.g. "+1" for a bonus on top of a level (implies ShowOnes).
    [Export] public bool PlusSigns { get => _plusSigns; set { _plusSigns = value; Changed(); } }
    // Use the income versions of the icons, where there are any.
    [Export] public bool IncomeIcons { get => _income; set { _income = value; Changed(); } }
    // With IncomeIcons: plain icons, and one hand (its usual size) centred under the whole row instead of one each.
    [Export] public bool OneHand { get => _oneHand; set { _oneHand = value; Changed(); } }
    // With OneHand: a nudge for the hand from its usual place (e.g. (0, -6) to tuck it up under the lowest icon).
    [Export] public Vector2 HandOffset { get => _handOffset; set { _handOffset = value; Changed(); } }
    [ExportGroup("Layout")]
    // How big the icons are drawn, in pixels (they are drawn for this size, so stay sharp): the resource's size,
    // the same with or without the income hand (an income icon is drawn bigger, the hand below the resource).
    // Buildings keep their own size, scaled by BuildingScale.
    [Export] public int IconSize { get => _iconSize; set { _iconSize = value; Changed(); } }
    // Some kinds drawn bigger or smaller than IconSize in this bundle only, e.g. "spades 1.2" or
    // "spades 1.2, coins 0.9" (names as in Kinds; drawn at that size, so they stay sharp).
    [Export] public string IconScales { get => _iconScales; set { _iconScales = value; Changed(); } }
    // From one kind to the next, across and down, in this node's pixels, e.g. (60, 0) for a row, (0, 50) for a
    // column, (50, 20) for a slanting line. Columns of buildings and steps hang down from a mostly-across line and
    // continue along a mostly-down one; repeated icons go the same way, RepeatSpacing apart.
    // Left at (0, 0), Spacing and Vertical below decide it instead.
    [Export] public Vector2 Step { get => _step; set { _step = value; Changed(); } }
    // Used only while Step is (0, 0): Spacing from one kind to the next, left to right, or top to bottom with Vertical.
    [Export] public bool Vertical { get => _vertical; set { _vertical = value; Changed(); } }
    [Export] public float Spacing { get => _spacing; set { _spacing = value; Changed(); } }
    // Between repeated icons of one kind.
    [Export] public float RepeatSpacing { get => _repeatSpacing; set { _repeatSpacing = value; Changed(); } }
    // Repeated icons side by side across the line instead of along it, centred on it, RepeatSpacing apart (e.g. two
    // priests side by side in a column of costs); the next kind follows as after a single icon.
    [Export] public bool RepeatAcross { get => _repeatAcross; set { _repeatAcross = value; Changed(); } }
    // Repeated icons of one kind stacked in one place instead, each RepeatStep from the one before, the stack centred on
    // the place (e.g. (-12, 8): two priests on a diagonal); the later ones are drawn over the earlier ones, so flipping
    // the step's sign keeps the places and swaps which is in front. Overrides RepeatSpacing and RepeatAcross; (0, 0): off.
    [Export] public Vector2 RepeatStep { get => _repeatStep; set { _repeatStep = value; Changed(); } }
    // Buildings (e.g. "1 stronghold, 1 sanctuary" in a trigger) share one place in the row: a column of icons
    // at this size, this far apart (top to bottom, centred on the row).
    [Export] public float BuildingScale { get => _buildingScale; set { _buildingScale = value; Changed(); } }
    [Export] public float BuildingSpacing { get => _buildingSpacing; set { _buildingSpacing = value; Changed(); } }
    // Steps on a track (e.g. "4 fire steps") are drawn as a column of that many track icons, this far apart.
    [Export] public float StepSpacing { get => _stepSpacing; set { _stepSpacing = value; Changed(); } }
    // Centre the row on this node (otherwise the first icon is centred on it and the rest follow along Step).
    [Export] public bool Centred { get => _centred; set { _centred = value; Changed(); } }
    [ExportGroup("Numbers")]
    // The number, centred over its icon: nudged by CountOffset, in this size and colour, with an outline.
    [Export] public Vector2 CountOffset { get => _countOffset; set { _countOffset = value; Changed(); } }
    [Export] public int FontSize { get => _fontSize; set { _fontSize = value; Changed(); } }
    // A number of three digits or more (e.g. 100 VP) in this share of FontSize, so it still fits on its icon (still
    // centred on it).
    [Export(PropertyHint.Range, "0.3,1,0.01")] public float LongNumberScale { get => _longScale; set { _longScale = value; Changed(); } }
    private float _longScale = 0.75f;
    [Export] public Color FontColour { get => _fontColour; set { _fontColour = value; Changed(); } }
    // Priests drawn in this colour instead of white (the building board's incomes: the faction's colour). Code only.
    public Color? PriestColour { get => _priestColour; set { if (_priestColour == value) return; _priestColour = value; Changed(); } }
    private Color? _priestColour;
    [Export] public int OutlineSize { get => _outlineSize; set { _outlineSize = value; Changed(); } }
    [Export] public Color OutlineColour { get => _outlineColour; set { _outlineColour = value; Changed(); } }
    // The number's colour for a kind code marks as a warning (e.g. debt).
    [Export] public Color WarningColour { get => _warningColour; set { _warningColour = value; Changed(); } }

    // What is shown now.
    public Amounts Shown { get; private set; } = Amounts.None;
    public bool IsEmpty => _drawnCount == 0;
    // The outer edges of the icons' drawings, in this node's pixels.
    public float Left { get; private set; }
    public float Right { get; private set; }
    public float Top { get; private set; }
    public float Bottom { get; private set; }

    private string _text = "";
    private string _kinds = "";
    private string _order = "";
    private bool _repeat;
    private string _repeatExcept = "";
    private bool _income;
    private bool _oneHand;
    private Vector2 _handOffset = Vector2.Zero;
    private Vector2 _repeatStep = Vector2.Zero;
    private bool _showOnes;
    private string _showOnesExcept = "";
    private bool _plusSigns;
    private int _iconSize = 48;
    private float _spacing = 80f;
    private float _repeatSpacing = 24f;
    private bool _repeatAcross;
    private string _iconScales = "";
    private Vector2 _step = Vector2.Zero;
    private bool _vertical;
    private float _buildingScale = 0.5f;
    private float _buildingSpacing = 40f;
    private float _stepSpacing = 30f;
    private bool _centred;
    private Vector2 _countOffset = new(0, -2); // the number sits 2 px above the icon's centre by default
    private int _fontSize = 40;
    // Black outlined in white by default: tiles, tokens, power actions, favours and player boards. Things drawn over the
    // map (hex costs, the colour wheel's cost, chips) set white outlined in black.
    private Color _fontColour = Colors.Black;
    private int _outlineSize = 8;
    private Color _outlineColour = Colors.White;
    private Color _warningColour = Colors.Red;
    private HashSet<ResourceKind> _warnings = new();
    private Dictionary<ResourceKind, string> _texts = new();
    private bool _shownByCode;
    private int _drawnCount;
    private readonly Dictionary<ResourceKind, Sprite2D> _icons = new(); // the first icon of each kind
    // Marks the nodes made in code (internal children: not shown in the scene tree and never saved).
    private const string DrawnMeta = "resource_bundle_drawn";

    public override void _Ready()
    {
        if (!_shownByCode)
            ShowText();
    }

    // Shows these amounts (nothing for Amounts.None or null). warnings: kinds whose number is written in
    // WarningColour. texts: what to write for some kinds instead of their number (e.g. "3/6"; always written).
    public void ShowAmounts(Amounts amounts, IEnumerable<ResourceKind> warnings = null,
        IReadOnlyDictionary<ResourceKind, string> texts = null)
    {
        amounts ??= Amounts.None;
        var warn = new HashSet<ResourceKind>(warnings ?? Enumerable.Empty<ResourceKind>());
        var written = new Dictionary<ResourceKind, string>(texts ?? new Dictionary<ResourceKind, string>());
        if (_shownByCode && amounts == Shown && warn.SetEquals(_warnings)
            && written.Count == _texts.Count && !written.Except(_texts).Any())
            return; // already shown
        _shownByCode = true;
        Shown = amounts;
        _warnings = warn;
        _texts = written;
        Redraw();
    }

    // The icon drawn for a kind (the first, if repeated), or null if it is not shown.
    public Sprite2D IconFor(ResourceKind kind) => _icons.GetValueOrDefault(kind);

    private void Changed()
    {
        if (!IsInsideTree())
            return;
        if (_shownByCode)
            Redraw();
        else
            ShowText();
    }

    private void ShowText()
    {
        Shown = Amounts.Parse(Text, out string problem);
        if (problem is not null)
            GD.PushWarning($"{Name}: {problem}");
        Redraw();
    }

    private List<Amount> Drawn
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Kinds))
            {
                if (string.IsNullOrWhiteSpace(Order))
                    return Shown.Parts.ToList();
                List<ResourceKind> first = Amounts.ParseKinds(Order, out string orderProblem);
                if (orderProblem is not null)
                    GD.PushWarning($"{Name}: {orderProblem}");
                return Shown.Parts.OrderBy(part => first.Contains(part.Kind) ? first.IndexOf(part.Kind) : first.Count).ToList();
            }
            List<ResourceKind> kinds = Amounts.ParseKinds(Kinds, out string problem);
            if (problem is not null)
                GD.PushWarning($"{Name}: {problem}");
            return kinds.Select(kind => new Amount(kind, Shown[kind])).ToList();
        }
    }

    private void Redraw()
    {
        // Found by their mark rather than remembered, so none are left behind when scripts are reloaded.
        foreach (Node child in GetChildren(includeInternal: true))
            if (child.HasMeta(DrawnMeta))
                child.QueueFree();
        _icons.Clear();

        List<Amount> parts = Drawn;
        _drawnCount = parts.Count;
        bool fixedKinds = !string.IsNullOrWhiteSpace(Kinds);
        var drawn = new List<Sprite2D>();
        var x = 0f; // the centre of the next place along the row (along Step)
        float spacing = Gap;
        for (var p = 0; p < parts.Count;)
        {
            Amount part = parts[p];
            // Buildings next to each other share one place: a column of small icons, one per building.
            if (!fixedKinds && IsBuilding(part.Kind))
            {
                var column = new List<ResourceKind>();
                for (; p < parts.Count && IsBuilding(parts[p].Kind); p++)
                    column.AddRange(Enumerable.Repeat(parts[p].Kind, System.Math.Max(System.Math.Abs(parts[p].Count), 1)));
                for (var k = 0; k < column.Count; k++)
                    drawn.Add(AddIcon(column[k], ColumnPosition(x, k, column.Count, BuildingSpacing), BuildingScale));
                x += ColumnLength(column.Count, BuildingSpacing) + spacing;
                continue;
            }
            p++;
            // Steps on a track: a column of that many track icons.
            if (!fixedKinds && IsStep(part.Kind))
            {
                int steps = System.Math.Max(System.Math.Abs(part.Count), 1);
                for (var k = 0; k < steps; k++)
                    drawn.Add(AddIcon(part.Kind, ColumnPosition(x, k, steps, StepSpacing), 1f));
                x += ColumnLength(steps, StepSpacing) + spacing;
                continue;
            }
            if (Repeats(part.Kind) && !fixedKinds && RepeatStep != Vector2.Zero)
            {
                int n = System.Math.Max(System.Math.Abs(part.Count), 1);
                for (var k = 0; k < n; k++)
                    drawn.Add(AddIcon(part.Kind, Along(x, 0) + (k - (n - 1) / 2f) * RepeatStep, 1f));
                x += spacing;
                continue;
            }
            if (Repeats(part.Kind) && !fixedKinds && RepeatAcross)
            {
                for (var k = 0; k < part.Count; k++)
                    drawn.Add(AddIcon(part.Kind, Along(x, (k - (part.Count - 1) / 2f) * RepeatSpacing), 1f));
                x += spacing;
                continue;
            }
            if (Repeats(part.Kind) && !fixedKinds)
            {
                for (var k = 0; k < part.Count; k++)
                    drawn.Add(AddIcon(part.Kind, Along(x + k * RepeatSpacing, 0), 1f));
                x += (System.Math.Max(part.Count, 1) - 1) * RepeatSpacing + spacing;
                continue;
            }
            Sprite2D icon = AddIcon(part.Kind, Along(x, 0), 1f);
            drawn.Add(icon);
            // A 1 goes without saying, unless every number is written (Kinds, ShowOnes, PlusSigns).
            if (part.Count != 1 || fixedKinds || WritesOnes(part.Kind) || PlusSigns || _texts.ContainsKey(part.Kind))
                icon.AddChild(Number(icon, part.Kind, part.Count, _warnings.Contains(part.Kind)));
            x += spacing;
        }

        if (drawn.Count == 0)
        {
            Left = Right = Top = Bottom = 0f;
            return;
        }
        // Centred: the row's middle on this node. Then its extent, from the icons as drawn.
        if (Centred)
        {
            Vector2 along = Direction;
            float Main(Sprite2D icon) => icon.Position.Dot(along);
            float shift = -(drawn.Min(Main) + drawn.Max(Main)) / 2;
            foreach (Sprite2D icon in drawn)
                icon.Position += Along(shift, 0);
        }
        // One hand for the row: its usual size and distance below the lowest resource, centred across the row.
        if (IncomeIcons && OneHand)
        {
            float below = IconArt.IncomeHandBelow(IconSize);
            var at = new Vector2((drawn.Min(icon => icon.Position.X) + drawn.Max(icon => icon.Position.X)) / 2,
                drawn.Max(icon => icon.Position.Y) + below) + HandOffset;
            var hand = new Sprite2D { Texture = IconArt.Texture(Icon.IncomeHand, IconSize), Position = at };
            hand.SetMeta(DrawnMeta, true);
            AddChild(hand, false, InternalMode.Front); // behind the icons
            drawn.Add(hand);
        }
        // The painted parts of the icons (their transparent margins don't count), so gaps are between drawings.
        Left = drawn.Min(icon => icon.Position.X + Arrangement.VisibleRect(icon).Position.X * icon.Scale.X);
        Right = drawn.Max(icon => icon.Position.X + Arrangement.VisibleRect(icon).End.X * icon.Scale.X);
        Top = drawn.Min(icon => icon.Position.Y + Arrangement.VisibleRect(icon).Position.Y * icon.Scale.Y);
        Bottom = drawn.Max(icon => icon.Position.Y + Arrangement.VisibleRect(icon).End.Y * icon.Scale.Y);
    }

    // The step from one kind to the next: Step, or (while it is zero) Spacing across, or down with Vertical.
    private Vector2 KindStep => _step != Vector2.Zero ? _step : Vertical ? new Vector2(0, Spacing) : new Vector2(Spacing, 0);
    private float Gap => KindStep.Length();
    private Vector2 Direction => KindStep == Vector2.Zero ? Vector2.Right : KindStep.Normalized();
    // Mostly downwards: columns continue along the line rather than hanging across it.
    private bool Downwards => Mathf.Abs(Direction.Y) > Mathf.Abs(Direction.X);

    // A point `along` the line and `across` it (downwards across a mostly-across line, rightwards across a
    // mostly-down one).
    private Vector2 Along(float along, float across) =>
        Direction * along + (Downwards ? new Vector2(across, 0) : new Vector2(0, across));

    // How much bigger than IconSize this kind is drawn here (IconScales; 1 if not listed).
    private float ScaleOf(ResourceKind kind)
    {
        if (string.IsNullOrWhiteSpace(IconScales))
            return 1f;
        foreach (string entry in IconScales.Split(','))
        {
            string[] words = entry.Replace('=', ' ').Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || !float.TryParse(words[^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float scale))
            {
                GD.PushWarning($"{Name}: IconScales entry \"{entry.Trim()}\" should be a name then a number, e.g. \"spades 1.2\".");
                continue;
            }
            List<ResourceKind> kinds = Amounts.ParseKinds(string.Join(' ', words[..^1]), out string problem);
            if (problem is not null)
                GD.PushWarning($"{Name}: {problem}");
            else if (kinds.Contains(kind))
                return scale;
        }
        return 1f;
    }

    // RepeatIcons and ShowOnes for one kind: the setting, or its opposite for the kinds listed against it.
    private bool Repeats(ResourceKind kind) => RepeatIcons != Listed(RepeatIconsExcept, kind);
    private bool WritesOnes(ResourceKind kind) => ShowOnes != Listed(ShowOnesExcept, kind);

    private bool Listed(string kinds, ResourceKind kind)
    {
        if (string.IsNullOrWhiteSpace(kinds))
            return false;
        List<ResourceKind> listed = Amounts.ParseKinds(kinds, out string problem);
        if (problem is not null)
            GD.PushWarning($"{Name}: {problem}");
        return listed.Contains(kind);
    }

    private static bool IsBuilding(ResourceKind kind) => kind is >= ResourceKind.Stronghold and <= ResourceKind.Sanctuary;
    private static bool IsStep(ResourceKind kind) => kind is >= ResourceKind.FireStep and <= ResourceKind.AirStep;

    // Icon k of a column of n: hanging down across a mostly-across line, centred on it at x; continuing along a
    // mostly-down one, from x on.
    private Vector2 ColumnPosition(float x, int k, int n, float spacing) =>
        Downwards ? Direction * (x + k * spacing) : Direction * x + new Vector2(0, (k - (n - 1) / 2f) * spacing);

    // How much further along the next kind goes after a column (only on a mostly-down line).
    private float ColumnLength(int n, float spacing) => Downwards ? (n - 1) * spacing : 0f;


    private Sprite2D AddIcon(ResourceKind kind, Vector2 position, float scale)
    {
        var icon = new Sprite2D
        {
            Texture = Art.ResourceIcon(kind, IncomeIcons && !OneHand, Mathf.Max(1, Mathf.RoundToInt(IconSize * ScaleOf(kind))), _priestColour),
            Position = position,
            Scale = Vector2.One * scale,
        };
        icon.SetMeta(DrawnMeta, true);
        AddChild(icon, false, InternalMode.Back); // internal: not in the scene tree, never saved
        _icons.TryAdd(kind, icon);
        return icon;
    }

    // The number over an icon, centred on the resource itself (for income icons, the one resting on the hand).
    private Label Number(Sprite2D icon, ResourceKind kind, int count, bool warn)
    {
        Rect2 area = icon.Texture is null ? new Rect2(-20, -20, 40, 40) : icon.GetRect();
        area.Position += Art.ResourceNumberCentre(kind, IncomeIcons, IconSize);
        var label = new Label
        {
            Text = _texts.TryGetValue(kind, out string text) ? text : PlusSigns && count > 0 ? $"+{count}" : count.ToString(),
            Position = area.Position + CountOffset,
            Size = area.Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bool longNumber = label.Text.Count(char.IsDigit) >= 3;
        label.AddThemeFontSizeOverride("font_size", longNumber ? Mathf.Max(1, Mathf.RoundToInt(FontSize * LongNumberScale)) : FontSize);
        label.AddThemeColorOverride("font_color", warn ? WarningColour : FontColour);
        label.AddThemeConstantOverride("outline_size", OutlineSize);
        label.AddThemeColorOverride("font_outline_color", OutlineColour);
        return label;
    }
}
