using Core;
using Godot;

namespace TMCalc;

// Editor only: what a player panel's terrain wheel will look like (PlayerPanel.PreviewFaction), on its TerrainMarker,
// so the marker can be placed. Drawn as ColourWheel draws itself at the same sizes: the ice factions' and the
// Shapeshifters' badge (one circle with its ring), or the Riverwalkers' small wheel (all but the top terrain locked).
// Made by PlayerPanel without an owner, so it is never saved into the scene.
[Tool]
public partial class TerrainWheelPreview : Node2D
{
    public Terrain Top { get; set; } = Terrain.Lake;
    public bool Full { get; set; }                     // the whole wheel (else a badge: one circle)
    public Icon RingIcon { get; set; } = Icon.IceRing;
    public bool HasRing { get; set; }
    public float RingScale { get; set; } = 1f;
    public Vector2 RingOffset { get; set; }

    public override void _Draw()
    {
        const float ringRadius = 130f, circleRadius = 42f, ringWidth = 9f, edgeWidth = 4f;
        var dark = new Color("161a18");
        var dim = new Color(0.42f, 0.42f, 0.42f, 0.8f); // ColourWheel.DisabledColour's default
        int top = Terrains.Wheel.IndexOf(Top);
        void Circle(Vector2 at, Terrain terrain, bool locked)
        {
            Color fill = HexArt.TerrainColour(terrain);
            if (locked)
                fill = fill.Lerp(new Color(dim, 1f), dim.A);
            DrawCircle(at, circleRadius, fill);
            DrawArc(at, circleRadius - edgeWidth / 2, 0, Mathf.Tau, 64, dark, edgeWidth, true);
        }
        Vector2 topAt = Vector2.Zero;
        if (Full)
        {
            DrawArc(Vector2.Zero, ringRadius, 0, Mathf.Tau, 128, dark, ringWidth, true);
            for (var i = 0; i < 7; i++)
            {
                Vector2 at = Vector2.FromAngle(-Mathf.Pi / 2 + Mathf.PosMod(i - top, 7) * Mathf.Tau / 7) * ringRadius;
                Circle(at, Terrains.Wheel[i], i != top);
                if (i == top)
                    topAt = at;
            }
        }
        else
            Circle(Vector2.Zero, Top, false);
        if (!HasRing || IconArt.Texture(RingIcon, IconArt.RingIconSize) is not Texture2D ring)
            return;
        float k = circleRadius * RingScale / (0.48f * IconArt.RingIconSize * 6f / 7f);
        DrawSetTransform(topAt + RingOffset, 0, Vector2.One * k);
        DrawTexture(ring, -ring.GetSize() / 2);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
