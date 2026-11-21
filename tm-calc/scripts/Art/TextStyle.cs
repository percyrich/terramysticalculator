using Godot;

namespace TMCalc;

// The look of text on tiles, tokens, power actions, favours and player boards: black, outlined in white (ResourceBundle's
// default; BuildingBoard has its own exports). Things drawn over the map keep white outlined in black.
public static class TextStyle
{
    public static readonly Color Text = Colors.Black, Outline = Colors.White;
    public const int OutlineSize = 6;

    // Light text on the setup screens' dark background: outlined in black (the font's own colour kept).
    public static void OutlinedBlack(Label label)
    {
        if (label is null)
            return;
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", OutlineSize);
    }

    public static void BlackOnWhite(Label label)
    {
        if (label is null)
            return;
        label.AddThemeColorOverride("font_color", Text);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", OutlineSize);
    }
}
