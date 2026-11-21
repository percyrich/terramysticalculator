using Godot;

namespace TMCalc;

// A player panel's backing (FactionBoardArt.Backing), drawn in code: full (1544 × 600) or Compact (1544 × 300, the last 144 for the pass tile),
// from its top left (Centered off). In the editor it shows PreviewTerrain's colour; in the game PlayerPanel paints it
// in the shown faction's colour. The texture is made when the scene loads and is never saved into the scene file.
[Tool]
[GlobalClass]
public partial class BackingSprite : Sprite2D
{
    [Export] public bool Compact { get => _compact; set { _compact = value; Redraw(); } }
    [Export] public Core.Terrain PreviewTerrain { get => _preview; set { _preview = value; Redraw(); } }

    private bool _compact;
    private Core.Terrain _preview = Core.Terrain.Wasteland;

    public override void _Ready()
    {
        Centered = false;
        Redraw();
    }

    // In the faction's frame colour (PlayerPanel calls this).
    public void ShowFrame(Color frame) => Texture = FactionBoardArt.Backing(frame, Compact);

    private void Redraw()
    {
        if (IsInsideTree())
            ShowFrame(FactionBoardArt.FrameColour(_preview));
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() == Sprite2D.PropertyName.Texture)
            property["usage"] = (long)PropertyUsageFlags.None; // neither shown nor saved
    }
}
