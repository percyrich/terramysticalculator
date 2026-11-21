using System.Collections.Generic;
using Core;
using Godot;

namespace TMCalc;

// A player's score badge, under their VP count (made by PlayerPanel): their largest network (the network icon, the
// count over it) and, while Fire & Ice's final scoring tile is in play, that tile's value (its icon, the value over it),
// in a row on a rounded box in the dark sections' colour outlined in the player's colour. This node is the box's top
// left: it grows to the right.
// Open (Main: a click on it), the score preview: what final scoring would give now, a VP icon with the points over it
// after each of those, then the cult scoring icon and the tracks' points the same way; and beside the VP count (at
// TotalAnchor) the conversion arrow and the VP total those would make. Closed again by Main.
public partial class ScoreBadge : Node2D
{
    // Set by PlayerPanel (its "Score badge" exports) before Show.
    public int IconSize = 40;
    public int FontSize = 26;
    public Vector2 NumberOffset = new(0, -2);  // the numbers from their icon's centre
    public Vector2 Padding = new(8, 6);        // round the row, inside the box
    public float PairGap = 4f;                 // between an icon and the points it scores
    public float GroupGap = 12f;               // between one icon-and-points and the next
    public float CornerRadius = 10f;
    public float StrokeWidth = 3f;
    public Color Fill = FactionBoardArt.SectionColour;
    // The total: from TotalAnchor (the VP count's right edge, level with its middle, in this node's pixels), TotalGap,
    // the arrow (ArrowSize), TotalGap, then the VP icon (TotalIconSize) with the total over it (TotalFontSize).
    public Vector2 TotalAnchor;
    public float TotalGap = 6f;
    public int ArrowSize = 48;
    public int TotalIconSize = 74;
    public int TotalFontSize = 40;
    public Vector2 TotalNumberOffset = new(0, -2);

    public bool IsOpen { get; private set; }

    private ScorePreview _scores;
    private int _points; // the player's VP now
    private Color _stroke = Colors.White;
    private Rect2 _box; // the box, in this node's pixels (empty: nothing shown)

    public void Show(ScorePreview scores, int points, Color stroke)
    {
        _scores = scores;
        _points = points;
        _stroke = stroke;
        Rebuild();
    }

    public void SetOpen(bool open)
    {
        if (open == IsOpen)
            return;
        IsOpen = open;
        Rebuild();
    }

    public bool IsOver(Vector2 globalPoint) =>
        IsVisibleInTree() && _box.HasArea() && _box.HasPoint(ToLocal(globalPoint));

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();
        _box = default;
        if (_scores is null)
        {
            QueueRedraw();
            return;
        }

        // Each item: its picture, the number over it (null: none), and the gap before it.
        Texture2D points = Art.ResourceIcon(ResourceKind.VictoryPoint, size: IconSize);
        var items = new List<(Texture2D Picture, string Number, float Gap)>
        {
            (IconArt.Texture(Icon.Network, IconSize), _scores.Network.ToString(), 0f),
        };
        if (IsOpen)
            items.Add((points, _scores.NetworkPoints.ToString(), PairGap));
        if (_scores.Tile is FinalScoringTile tile)
        {
            items.Add((IconArt.Texture(Main.FinalTileIcon(tile), IconSize), _scores.TileValue.ToString(), GroupGap));
            if (IsOpen)
                items.Add((points, _scores.TilePoints.ToString(), PairGap));
        }
        if (IsOpen)
        {
            items.Add((IconArt.Texture(Icon.CultScoring, IconSize), null, GroupGap));
            items.Add((points, _scores.CultPoints.ToString(), PairGap));
        }

        float x = Padding.X, y = Padding.Y + IconSize / 2f;
        foreach (var (picture, number, gap) in items)
        {
            x += gap;
            float width = Width(picture, IconSize);
            var at = new Vector2(x + width / 2, y);
            x += width;
            AddChild(new Sprite2D { Texture = picture, Position = at });
            if (number is not null)
                AddChild(Number(number, at, IconSize, FontSize, NumberOffset));
        }
        _box = new Rect2(0, 0, x + Padding.X, IconSize + 2 * Padding.Y);

        if (IsOpen)
        {
            Texture2D arrow = IconArt.Texture(Icon.TriggerArrow, ArrowSize);
            float arrowWidth = Width(arrow, ArrowSize);
            var arrowAt = TotalAnchor + new Vector2(TotalGap + arrowWidth / 2, 0);
            AddChild(new Sprite2D { Texture = arrow, Position = arrowAt });
            Texture2D total = Art.ResourceIcon(ResourceKind.VictoryPoint, size: TotalIconSize);
            var totalAt = arrowAt + new Vector2(arrowWidth / 2 + TotalGap + Width(total, TotalIconSize) / 2, 0);
            AddChild(new Sprite2D { Texture = total, Position = totalAt });
            int sum = _points + _scores.NetworkPoints + _scores.TilePoints + _scores.CultPoints;
            AddChild(Number(sum.ToString(), totalAt, TotalIconSize, TotalFontSize, TotalNumberOffset));
        }
        QueueRedraw();
    }

    private static float Width(Texture2D picture, int size) => picture?.GetSize().X ?? size;

    private static Label Number(string text, Vector2 centre, int iconSize, int fontSize, Vector2 offset)
    {
        var size = new Vector2(iconSize, iconSize);
        var label = new Label
        {
            Text = text,
            Position = centre - size / 2 + offset,
            Size = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        TextStyle.BlackOnWhite(label);
        return label;
    }

    public override void _Draw()
    {
        if (!_box.HasArea())
            return;
        Vector2[] outline = HighlightArt.RoundRect(_box, CornerRadius);
        DrawColoredPolygon(outline, Fill);
        var closed = new Vector2[outline.Length + 1];
        outline.CopyTo(closed, 0);
        closed[^1] = outline[0];
        DrawPolyline(closed, _stroke, StrokeWidth, true);
    }
}
