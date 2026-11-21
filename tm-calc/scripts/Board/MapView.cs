using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// Draws the hex map. Main calls Build with the map's layout (again whenever the chosen map changes: IsBuiltFor),
// then ShowTerrain on every render.
// Hexes are generated in code because 113 hand-placed hexes would be slow to make and easy to misalign.
// Their pictures are drawn in code too (HexArt): land hexes by terrain, river hexes by which of their edges
// face land. River hexes are drawn first, so the land hexes' outlines lie over the river banks.
// Hex i here is hex i in GameState (numbered row by row, left to right).
// [Tool] only so the editor shows the base map (PreviewInEditor); nothing else here runs in the editor.
[Tool]
public partial class MapView : Node2D
{
    [ExportGroup("Hexes")]
    // Spacing in art pixels for pointy-top hexes. Must match the hex art. Adjustable in the Inspector.
    [Export] public float HexWidth = 128f;
    [Export] public float RowSpacing = 111f;

    // A land hex transformed (with animation) turns over as a tile (HexFlip): it rises, turns from right to left,
    // and sinks back. Sizes in hex-art pixels for a 74 px hex radius (scaled with the hexes).
    [ExportGroup("Transform animation")]
    [Export] public float FlipSeconds { get; set; } = 0.7f;   // turning over
    [Export] public float RiseSeconds { get; set; } = 0.3f;   // rising off the board, and again sinking back
    [Export] public float FlipLift { get; set; } = 30f;       // how high it rises
    [Export] public float FlipThickness { get; set; } = 20f;  // how thick it looks while off the board

    [ExportGroup("Preview")]
    // Editor only: tick to draw the preview again.
    [Export] public bool RedrawPreview
    {
        get => false;
        set
        {
            if (value && Engine.IsEditorHint() && IsInsideTree())
                PreviewInEditor();
        }
    }

    [ExportGroup("Cost")]
    // The cost shown over a hex while a piece is held: its icons' size, its numbers' size, and where it sits
    // (from the hex's centre).
    [Export] public int CostIconSize = 40;
    [Export] public int CostFontSize = 34;
    [Export] public Vector2 CostOffset = new(0, -30);
    [ExportGroup("")]

    private Terrain[] _shownTerrain;                               // what ShowTerrain last showed
    private readonly Dictionary<int, HexFlip> _flips = new();      // hexes turning over now
    private Node2D _flipLayer;                                     // over every hex, under the glows

    private readonly List<Sprite2D> _hexes = new();
    // _neighbours[hex][d]: the hex across edge d (0 = E, 1 = SE, ... 5 = NE), or -1 off the map.
    private readonly List<int[]> _neighbours = new();
    private readonly HashSet<int> _riversMovedBack = new();
    private ReachOverlay _reach; // drawn over every hex (it stays the last child)
    private MapGlow _glow;       // town rings and power offer edges, over the hexes
    private PowerOfferChip _chip; // the first waiting power offer, over the pieces
    private PowerOfferChip _coinChip; // beside it: the Nomads' coin instead (their Trading Camp), when they may take it

    // The hex radius the pictures are drawn at: half the hex's height, from the spacing above.
    private float Radius => HexWidth / MathF.Sqrt(3);
    public float HexRadius => Radius;

    // What the hexes were last built for. The map is the chosen map (null: none chosen yet, the base map shown):
    // two maps can share a layout but not their rivers, and rivers are only moved to the back once (ShowTerrain).
    private MapLayout _builtLayout;
    private MapId? _builtMap;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            PreviewInEditor();
            _previewMade = true;
        }
        else
            SetProcess(false);
    }

    // Editor only: the preview again after the scripts are rebuilt (the editor doesn't call _Ready again then, and a
    // rebuild after a failed one left the map empty). The flag is reset with the scripts, so this runs once per build.
    private bool _previewMade;

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            SetProcess(false);
            return;
        }
        if (_previewMade)
            return;
        _previewMade = true;
        PreviewInEditor();
    }

    // The base map's hexes as the game first shows them: just the pictures (no flips, glows, reach or chip). Made
    // without an owner, so never saved with the scene.
    private void PreviewInEditor()
    {
        foreach (Node old in GetChildren().Where(node => node.HasMeta(PreviewMeta)))
            old.QueueFree();
        MapDefinition map = Maps.Base;
        var positions = new Vector2[map.Layout.HexCount];
        for (var hex = 0; hex < positions.Length; hex++)
        {
            var (row, column) = map.Layout.Position(hex);
            positions[hex] = new Vector2((2 * column + (map.Layout.IsShifted(row) ? 1 : 0)) * HexWidth / 2, row * RowSpacing);
        }
        foreach (bool rivers in new[] { true, false }) // rivers first, so the land outlines lie over the banks
            for (var hex = 0; hex < positions.Length; hex++)
            {
                Terrain terrain = map.HexTerrain[hex];
                if ((terrain == Terrain.River) != rivers)
                    continue;
                Texture2D texture;
                if (terrain == Terrain.River)
                {
                    var land = new bool[6];
                    foreach (int neighbour in map.Layout.Neighbours(hex))
                        land[HexArt.EdgeTowards(positions[neighbour] - positions[hex])] = map.HexTerrain[neighbour] != Terrain.River;
                    texture = HexArt.River(land, Radius);
                }
                else
                    texture = HexArt.Land(terrain, Radius);
                var sprite = new Sprite2D { Texture = texture, Position = positions[hex] };
                sprite.SetMeta(PreviewMeta, true);
                AddChild(sprite);
            }
    }
    private const string PreviewMeta = "map_preview";

    public bool IsBuiltFor(MapLayout layout, MapId? map) =>
        _builtLayout is not null && _builtLayout.Equals(layout) && _builtMap == map;

    // Makes a hex sprite for every hex of the layout, replacing any made before (and everything shown on them).
    public void Build(MapLayout layout, MapId? map = null)
    {
        Clear();
        _builtLayout = layout;
        _builtMap = map;
        for (var hex = 0; hex < layout.HexCount; hex++)
        {
            var (row, column) = layout.Position(hex);
            var sprite = new Sprite2D { Name = $"Hex{hex}" };
            // Short rows sit half a hex to the right of long rows.
            var doubledColumn = 2 * column + (layout.IsShifted(row) ? 1 : 0);
            sprite.Position = new Vector2(doubledColumn * HexWidth / 2, row * RowSpacing);
            AddChild(sprite);
            _hexes.Add(sprite);
        }

        _flipLayer = new Node2D { Name = "Flips" };
        AddChild(_flipLayer);
        _glow = new MapGlow { Name = "Glow" };
        AddChild(_glow);
        _reach = new ReachOverlay { Name = "Reach" };
        AddChild(_reach);
        _chip = new PowerOfferChip { Name = "PowerOfferChip", Visible = false };
        AddChild(_chip);
        _coinChip = new PowerOfferChip { Name = "CoinChip", Visible = false };
        AddChild(_coinChip);

        for (var hex = 0; hex < layout.HexCount; hex++)
        {
            var across = Enumerable.Repeat(-1, 6).ToArray();
            foreach (int neighbour in layout.Neighbours(hex))
                across[HexArt.EdgeTowards(_hexes[neighbour].Position - _hexes[hex].Position)] = neighbour;
            _neighbours.Add(across);
        }
    }

    // Takes away the hexes and the layers over them (Build makes them again).
    private void Clear()
    {
        foreach (Sprite2D hex in _hexes)
        {
            RemoveChild(hex);
            hex.QueueFree();
        }
        foreach (Node2D layer in new Node2D[] { _flipLayer, _glow, _reach, _chip, _coinChip })
        {
            if (layer is null)
                continue;
            RemoveChild(layer);
            layer.QueueFree();
        }
        _hexes.Clear();
        _neighbours.Clear();
        _riversMovedBack.Clear();
        _flips.Clear();
        _shownTerrain = null;
        _flipLayer = null;
        _glow = null;
        _reach = null;
        _chip = null;
        _coinChip = null;
    }

    public Node2D Hex(int index) => _hexes[index];

    private bool IsHex(int hex) => hex >= 0 && hex < _hexes.Count;

    // The hexes' outline box on screen (global): the leftmost hex's flat side to the rightmost's, the top row's top
    // point to the bottom row's bottom point. Empty before Build.
    public Rect2 ShownBounds()
    {
        if (_hexes.Count == 0)
            return default;
        var half = new Vector2(HexWidth / 2, Radius);
        var local = new Rect2(_hexes[0].Position - half, half * 2);
        foreach (Sprite2D hex in _hexes)
            local = local.Merge(new Rect2(hex.Position - half, half * 2));
        return GlobalTransform * local;
    }

    // animate: a land hex that has become another land terrain turns over (HexFlip) and gets its new picture when
    // it lands; otherwise every picture changes at once (and any turning stops). later: hexes whose land picture
    // changes laterSeconds from now instead, without turning over (the Kingdom of Ember's lava flow, as the stack
    // lands on it).
    public void ShowTerrain(ValueList<Terrain> terrain, bool animate = false, IReadOnlyCollection<int> later = null, float laterSeconds = 0f)
    {
        for (var hex = 0; hex < _hexes.Count; hex++)
        {
            Terrain? before = _shownTerrain?[hex];
            if (later is not null && later.Contains(hex) && laterSeconds > 0 && before is Terrain was && was != terrain[hex]
                && was != Terrain.River && terrain[hex] != Terrain.River)
            {
                if (_flips.Remove(hex, out HexFlip flipping))
                    flipping.QueueFree();
                int at = hex;
                GetTree().CreateTimer(laterSeconds).Timeout += () =>
                {
                    // The picture of whatever the hex is to show by then (another change may have come since).
                    if (IsHex(at) && IsInstanceValid(_hexes[at]) && _shownTerrain is not null && at < _shownTerrain.Length
                        && !_flips.ContainsKey(at) && _shownTerrain[at] != Terrain.River)
                        _hexes[at].Texture = HexArt.Land(_shownTerrain[at], Radius);
                };
                continue;
            }
            if (_flips.TryGetValue(hex, out HexFlip running))
            {
                if (animate && running.To == HexArt.TerrainColour(terrain[hex]))
                    continue;                      // already turning over to this
                _flips.Remove(hex);
                running.QueueFree();
            }
            else if (animate && before is Terrain old && old != terrain[hex] && old != Terrain.River && terrain[hex] != Terrain.River)
            {
                StartFlip(hex, old, terrain[hex]);
                continue;                          // its picture changes when it lands
            }
            if (terrain[hex] != Terrain.River)
            {
                // A river become land (a custom map: the editor, or another custom map after an undo): back over the
                // rivers, so its outline lies over their banks again.
                if (_riversMovedBack.Remove(hex) && _flipLayer is not null)
                    MoveChild(_hexes[hex], _flipLayer.GetIndex() - 1);
                _hexes[hex].Texture = HexArt.Land(terrain[hex], Radius);
                continue;
            }
            bool[] land = _neighbours[hex].Select(n => n >= 0 && terrain[n] != Terrain.River).ToArray();
            _hexes[hex].Texture = HexArt.River(land, Radius);
            // Rivers never change, so each is moved behind the land hexes once.
            if (_riversMovedBack.Add(hex))
                MoveChild(_hexes[hex], 0);
        }
        _shownTerrain = Enumerable.Range(0, _hexes.Count).Select(i => terrain[i]).ToArray();
    }

    private void StartFlip(int hex, Terrain from, Terrain to)
    {
        float k = Radius / HexArt.Radius;
        var flip = new HexFlip
        {
            Name = $"Flip{hex}",
            Position = _hexes[hex].Position,
            From = HexArt.TerrainColour(from),
            To = HexArt.TerrainColour(to),
            Radius = Radius,
            Lift = FlipLift * k,
            Thickness = FlipThickness * k,
            RiseSeconds = RiseSeconds,
            FlipSeconds = FlipSeconds,
            Start = HexFlip.Now,
        };
        flip.Landed = () =>
        {
            _flips.Remove(hex);
            _hexes[hex].Texture = HexArt.Land(to, Radius);
        };
        _flips[hex] = flip;
        _flipLayer.AddChild(flip);
    }

    // For a piece put on a hex as it is transformed: the seconds until that hex has landed (null if it isn't
    // turning over), and a way to make it wait before rising (once), until the piece is over it.
    // Calculator: hexes holding a neutral dwelling shown in this colour instead of their terrain (after ShowTerrain).
    public void ShowNeutral(IReadOnlyCollection<int> hexes, Color colour)
    {
        if (_shownTerrain is null)
            return;
        for (var hex = 0; hex < _hexes.Count; hex++)
            if (_shownTerrain[hex] != Terrain.River && !_flips.ContainsKey(hex))
                _hexes[hex].Texture = hexes.Contains(hex) ? HexArt.LandIn(colour, Radius) : HexArt.Land(_shownTerrain[hex], Radius);
    }

    public float? FlipSecondsLeft(Node2D hexNode) =>
        FlipOn(hexNode) is HexFlip flip ? (float)flip.SecondsLeft : null;

    public void DelayFlip(Node2D hexNode, float seconds) => FlipOn(hexNode)?.Delay(seconds);

    private HexFlip FlipOn(Node2D hexNode)
    {
        int hex = _hexes.IndexOf(hexNode as Sprite2D);
        return hex >= 0 && _flips.TryGetValue(hex, out HexFlip flip) ? flip : null;
    }

    // Marks the hexes a dwelling could go on with the reach highlight (a small pulsing hexagon); none clears it.
    public void ShowReach(IEnumerable<int> hexes) =>
        _reach?.ShowAt(hexes.Distinct().Where(IsHex).Select(hex => _hexes[hex].Position), Radius);

    // A white edge highlight inside a hex (EdgeHighlight), for picking it. No glints by default: several hexes are
    // usually lit at once.
    public void ShowHexHighlight(int hex, bool on, int glints = 0)
    {
        if (!IsHex(hex))
            return; // lit on a map since replaced (a jump to a game on another map)
        EdgeHighlight highlight = EdgeHighlight.Find(_hexes[hex], on, 0);
        // White, but purple on an ice hex (white doesn't show on it).
        HighlightArt.Colour colour = _shownTerrain is not null && hex < _shownTerrain.Length && _shownTerrain[hex] == Terrain.Ice
            ? HighlightArt.Colour.Purple : HighlightArt.Colour.White;
        if (highlight is null || highlight.IsShown(colour, glints))
            return;
        highlight.Show(new[] { HighlightArt.Hexagon(Vector2.Zero, Radius) }, colour, glints, Radius / HexArt.Radius);
    }

    // ---------- Towns and power offers (MapGlow) ----------

    // A gold ring round each town: the outer edges of its hexes (hexes joined by a bridge get rings of their own),
    // with a bright band drifting across it if wanted (a town waiting for its key: Sparkles); otherwise
    // steady.
    public void ShowTowns(IEnumerable<(IReadOnlyCollection<int> Hexes, bool Sparkles)> towns)
    {
        var shown = new List<(HashSet<int>, List<MapGlow.Segment>, bool)>();
        foreach (var (town, sparkles) in towns)
        {
            var hexes = new HashSet<int>(town);
            var outer = new List<MapGlow.Segment>();
            foreach (int hex in hexes)
                for (var d = 0; d < 6; d++)
                    if (!hexes.Contains(_neighbours[hex][d]))
                        outer.Add(EdgeOf(hex, d));
            shown.Add((hexes, outer, sparkles));
        }
        _glow?.ShowTowns(shown, Radius);
    }

    // Power offers waiting, first to answer first: each lights the edges between the hex built on and the offered
    // player's buildings next to it (Neighbours; one joined by a bridge lights the bridge), in that player's colour.
    // The first also gets the chip, showing Power, over its middle edge; coin: and to its right a coin chip (the Nomads
    // may take 1 coin instead, for a build next to their Trading Camp).
    public void ShowPowerOffers(IReadOnlyList<(Color Colour, int Hex, IReadOnlyList<int> Neighbours, int Power)> offers,
        bool coin = false)
    {
        _coinChip?.HideOffer();
        var shown = new List<MapGlow.Offer>();
        foreach (var (colour, hex, neighbours, _) in offers)
            shown.Add(new MapGlow.Offer($"{colour.ToHtml()}:{hex}", colour,
                neighbours.Select(n => Between(hex, n)).ToList()));
        _glow?.ShowOffers(shown, Radius);

        if (_chip is null)
            return;
        if (offers.Count == 0 || shown[0].Segments.Count == 0)
        {
            _chip.HideOffer();
            return;
        }
        MapGlow.Segment middle = shown[0].Segments[shown[0].Segments.Count / 2];
        _chip.Position = (middle.A + middle.B) / 2;
        _chip.ShowOffer(offers[0].Power, offers[0].Colour, Radius);
        if (coin && _coinChip is not null)
        {
            float r = Radius * 0.42f;
            _coinChip.Position = _chip.Position + new Vector2(2 * r + Radius * 0.25f, 0);
            _coinChip.ShowContent(Amounts.Of(ResourceKind.Coin, 1), offers[0].Colour, r,
                Mathf.RoundToInt(Radius * 0.55f), Mathf.RoundToInt(Radius * 0.4f));
        }
    }

    // Is the point over the coin chip beside the power offer's?
    public bool IsOverCoinChip(Vector2 globalPoint) => _coinChip?.IsOver(globalPoint) ?? false;

    // Is the point over the power offer's chip?
    public bool IsOverOfferChip(Vector2 globalPoint) => _chip?.IsOver(globalPoint) ?? false;

    // Edge d of a hex (0 = E, 1 = SE, ... 5 = NE): from corner d to corner d + 1, in this node's pixels.
    private MapGlow.Segment EdgeOf(int hex, int d)
    {
        Vector2 c = _hexes[hex].Position;
        Vector2 Corner(int i) => c + Vector2.FromAngle((60 * i - 30) * MathF.PI / 180) * Radius;
        return new MapGlow.Segment(Corner(d), Corner((d + 1) % 6));
    }

    // Between two hexes: their shared edge if they touch; otherwise (joined by a bridge) where the bridge goes,
    // from the corner of each that faces the other, pulled in by 0.12 of a radius at each end.
    private MapGlow.Segment Between(int a, int b)
    {
        int d = Array.IndexOf(_neighbours[a], b);
        if (d >= 0)
            return EdgeOf(a, d);
        Vector2 ca = _hexes[a].Position, cb = _hexes[b].Position;
        Vector2 Nearest(Vector2 c, Vector2 towards) => Enumerable.Range(0, 6)
            .Select(i => c + Vector2.FromAngle((60 * i - 30) * MathF.PI / 180) * Radius)
            .OrderBy(p => p.DistanceTo(towards)).First();
        Vector2 pa = Nearest(ca, cb), pb = Nearest(cb, ca);
        float k = Radius * 0.12f / pa.DistanceTo(pb);
        return new MapGlow.Segment(pa.Lerp(pb, k), pb.Lerp(pa, k));
    }

    // Shows a cost over a hex while a piece is held over the map; null hides it.
    // As a ResourceBundle (e.g. 3 workers), made on the hex the first time.
    public void ShowCost(int hex, Amounts cost)
    {
        if (!IsHex(hex))
            return;
        var bundle = _hexes[hex].GetNodeOrNull<ResourceBundle>("Cost");
        if (cost is null || cost.IsEmpty)
        {
            if (bundle is not null)
                bundle.Visible = false;
            return;
        }
        if (bundle is null)
        {
            bundle = new ResourceBundle
            {
                Name = "Cost", Centred = true, IconSize = CostIconSize, FontSize = CostFontSize, Position = CostOffset,
                ShowOnes = true, ZIndex = 45, // over the pieces on the map
                FontColour = Colors.Black, OutlineColour = Colors.White, // black with a white edge
            };
            _hexes[hex].AddChild(bundle);
        }
        bundle.Visible = true;
        bundle.ShowAmounts(cost);
    }

    public void HideAllCosts()
    {
        for (var hex = 0; hex < _hexes.Count; hex++)
            ShowCost(hex, null);
    }

    // The hex whose centre is nearest the point, if the point is inside it (roughly); otherwise null.
    public int? HexAt(Vector2 globalPoint)
    {
        if (_hexes.Count == 0)
            return null;
        var radius = HexWidth / 2 * GlobalScale.X;
        var nearest = Enumerable.Range(0, _hexes.Count)
            .OrderBy(hex => _hexes[hex].GlobalPosition.DistanceTo(globalPoint))
            .First();
        return _hexes[nearest].GlobalPosition.DistanceTo(globalPoint) <= radius ? nearest : null;
    }
}
