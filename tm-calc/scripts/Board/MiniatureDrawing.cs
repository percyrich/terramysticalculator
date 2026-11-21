using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// A map miniature on the map screen (MapPicker.Miniature): each hex a filled hexagon, the land outlined, drawn once
// (Godot keeps the commands). Flat shapes only: a preview needs no river banks, and hundreds of drawn hex textures
// made the map screen slow to appear.
public partial class MiniatureDrawing : Node2D
{
    public float Radius;
    public readonly List<(Vector2 Centre, Color Fill, bool Outlined)> Hexes = new();

    public override void _Draw()
    {
        float line = Mathf.Max(1f, Radius * 0.09f);
        foreach (var (centre, fill, outlined) in Hexes)
        {
            Vector2[] corners = HexArt.Corners(centre, outlined ? Radius : Radius + 0.5f);
            DrawColoredPolygon(corners, fill);
            if (outlined)
                DrawPolyline(corners.Append(corners[0]).ToArray(), HexArt.OutlineColour, line, antialiased: true);
        }
    }
}
