using Godot;

namespace TMCalc;

// A Sprite2D showing one of IconArt's drawn icons, chosen in the Inspector, so a scene can place a drawn icon
// as it would a picture file. The icon is drawn for IconSize pixels (to stay sharp, set that rather
// than scaling the node). The texture is made when the scene loads and is never saved into the scene file.
[Tool]
[GlobalClass]
public partial class IconSprite : Sprite2D
{
    [Export] public Icon Icon { get => _icon; set { _icon = value; Redraw(); } }
    [Export(PropertyHint.Range, "8,512,1")] public int IconSize { get => _size; set { _size = value; Redraw(); } }

    private Icon _icon = Icon.Coin;
    private int _size = 48;

    public override void _Ready() => Redraw();

    private void Redraw()
    {
        if (IsInsideTree())
            Texture = IconArt.Texture(_icon, _size);
    }

    // The texture is made from the settings above, so it isn't stored with the scene.
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() == Sprite2D.PropertyName.Texture)
            property["usage"] = (long)PropertyUsageFlags.None; // neither shown nor saved
    }
}
