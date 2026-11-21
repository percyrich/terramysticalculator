using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The map screen: every map as a miniature (Miniature) on a dark backing with its name under it, in a grid centred on
// this node. Main asks MapAt on a click and submits ChooseMap; the hovered map is lit white with a glint (EdgeHighlight).
// Made in code by Main (MainMaps.cs) unless the scene has a %MapChoices node.
[GlobalClass]
public partial class MapPicker : Node2D
{
    [Export] public int Columns { get; set; } = 3;
    [Export] public float MapWidth { get; set; } = 500f;               // a miniature, across its long rows
    [Export] public Vector2 Step { get; set; } = new(590, 430);        // centre to centre
    [Export] public float BackingMargin { get; set; } = 14f;           // round the miniature
    [Export] public float CornerRadius { get; set; } = 16f;
    [Export] public Color BackingColour { get; set; } = new("22374f");
    [Export] public int NameFontSize { get; set; } = 30;
    [Export] public float NameGap { get; set; } = 8f;                  // from the backing to the name
    [Export] public int HoverGlints { get; set; } = 1;

    private readonly List<(MapId Map, Node2D Choice, Rect2 Backing, Rect2 Area)> _choices = new();
    private MapId? _hovered;

    // One choice per map, in the order given: left to right, then down.
    public void Build(IReadOnlyList<MapId> maps)
    {
        foreach (var (_, choice, _, _) in _choices)
        {
            RemoveChild(choice);
            choice.QueueFree();
        }
        _choices.Clear();
        _hovered = null;

        int rows = (maps.Count + Columns - 1) / Columns;
        for (var i = 0; i < maps.Count; i++)
        {
            int column = i % Columns, row = i / Columns;
            var choice = new Node2D
            {
                Name = $"Map{maps[i]}",
                Position = new Vector2((column - (Columns - 1) / 2f) * Step.X, (row - (rows - 1) / 2f) * Step.Y),
            };
            AddChild(choice);

            Node2D miniature = Miniature(Maps.ById(maps[i]), MapWidth, out Vector2 size);
            Rect2 backing = new Rect2(-size / 2, size).Grow(BackingMargin);
            choice.AddChild(new Polygon2D
            {
                Name = "Backing", Polygon = HighlightArt.RoundRect(backing, CornerRadius), Color = BackingColour, Antialiased = true,
            });
            choice.AddChild(miniature);

            var name = new Label
            {
                Name = "Name", Text = Maps.Name(maps[i]),
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(backing.Position.X, backing.End.Y + NameGap),
                Size = new Vector2(backing.Size.X, NameFontSize * 1.4f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            name.AddThemeFontSizeOverride("font_size", NameFontSize);
            name.AddThemeColorOverride("font_color", Colors.White);
            name.AddThemeColorOverride("font_outline_color", Colors.Black);
            name.AddThemeConstantOverride("outline_size", TextStyle.OutlineSize);
            choice.AddChild(name);

            Rect2 area = backing.Expand(new Vector2(backing.End.X, name.Position.Y + name.Size.Y));
            _choices.Add((maps[i], choice, backing, area));
        }
    }

    // The map whose miniature (or name) is under the point, if any.
    public MapId? MapAt(Vector2 globalPoint)
    {
        if (!IsVisibleInTree())
            return null;
        foreach (var (map, choice, _, area) in _choices)
            if (area.HasPoint(choice.ToLocal(globalPoint)))
                return map;
        return null;
    }

    public override void _Process(double delta)
    {
        MapId? over = MapAt(GetGlobalMousePosition());
        if (over == _hovered)
            return;
        _hovered = over;
        foreach (var (map, choice, backing, _) in _choices)
            EdgeHighlight.SetOnArea(choice, map == over, backing, CornerRadius, HoverGlints);
    }

    // A map drawn small, centred on the node's origin, in the board's style (HexArt: land by terrain, rivers with
    // their banks, rivers first so the land outlines lie over the banks). width: across the longest row, outside
    // edge to outside edge; size: the whole picture's width and height.
    public static Node2D Miniature(MapDefinition map, float width, out Vector2 size)
    {
        MapLayout layout = map.Layout;
        // Hexes are placed by "doubled" column, as on the board (MapView): short rows sit half a hex to the right.
        int Doubled(int row, int column) => 2 * column + (layout.IsShifted(row) ? 1 : 0);
        int widest = Enumerable.Range(0, layout.RowCount).Max(row => Doubled(row, layout.RowLengths[row] - 1));
        float hexWidth = width / (widest / 2f + 1);
        float radius = hexWidth / MathF.Sqrt(3);
        float rowSpacing = hexWidth * MathF.Sqrt(3) / 2;
        var middle = new Vector2(widest * hexWidth / 4, (layout.RowCount - 1) * rowSpacing / 2);
        size = new Vector2(width, (layout.RowCount - 1) * rowSpacing + 2 * radius);

        Vector2 PositionOf(int hex)
        {
            var (row, column) = layout.Position(hex);
            return new Vector2(Doubled(row, column) * hexWidth / 2, row * rowSpacing) - middle;
        }

        // One node drawing every hex as a flat polygon (rivers first, then the land with its outline): a preview needs no
        // river banks, and hundreds of drawn hex textures made the map screen slow to appear.
        ValueList<Terrain> terrain = map.HexTerrain;
        var drawing = new MiniatureDrawing { Name = "Miniature", Radius = radius };
        foreach (int hex in Enumerable.Range(0, layout.HexCount).OrderBy(h => terrain[h] == Terrain.River ? 0 : 1))
            drawing.Hexes.Add((PositionOf(hex), HexArt.TerrainColour(terrain[hex]), terrain[hex] != Terrain.River));
        return drawing;
    }

}
