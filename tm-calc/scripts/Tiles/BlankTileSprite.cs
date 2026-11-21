using Godot;

namespace TMCalc;

// A Sprite2D showing one of IconArt's drawn blank tiles (round, pass, key token, power action, favour), chosen
// in the Inspector. TileScale draws it at that fraction of its natural size (to stay sharp, set that rather than
// scaling the node). The texture is made when the scene loads and is never saved into the scene file.
[Tool]
[GlobalClass]
public partial class BlankTileSprite : Sprite2D
{
    [Export] public BlankTile Tile { get => _tile; set { _tile = value; Redraw(); } }
    [Export(PropertyHint.Range, "0.1,4,0.01")] public float TileScale { get => _scale; set { _scale = value; Redraw(); } }

    private BlankTile _tile = BlankTile.Round;
    private float _scale = 1f;

    public override void _Ready() => Redraw();

    private void Redraw()
    {
        if (IsInsideTree())
            Texture = IconArt.Texture(_tile, _scale);
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() == Sprite2D.PropertyName.Texture)
            property["usage"] = (long)PropertyUsageFlags.None; // neither shown nor saved
    }
}
