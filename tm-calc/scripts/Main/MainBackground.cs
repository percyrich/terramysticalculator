using Godot;

namespace TMCalc;

// The rest of Main: a plain background rectangle behind everything, and a small colour picker (bottom left of the
// screen) to try background colours while the game runs. The rectangle fills the screen on a CanvasLayer of its own at
// BackgroundLayer (below the default canvas, 0), so the board, the panels and every other layer sit on top of it; it
// ignores the mouse. The picker is on a layer above everything (BackgroundPickerLayer); the colour chosen is printed
// to the Output panel as hex, to copy into BackgroundColour. While the picker or its popup is under the pointer, the
// board leaves clicks alone (IsOverTopPanel).
public partial class Main
{
    private ColorRect _background;
    private ColorPickerButton _backgroundPicker;

    private void ReadyBackground()
    {
        var layer = new CanvasLayer { Name = "BackgroundLayer", Layer = BackgroundLayer };
        AddChild(layer);
        _background = new ColorRect { Name = "Background", Color = BackgroundColour, MouseFilter = Control.MouseFilterEnum.Ignore };
        _background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_background);
        if (!ShowBackgroundPicker)
            return;

        var pickerLayer = new CanvasLayer { Name = "BackgroundPickerLayer", Layer = BackgroundPickerLayer };
        AddChild(pickerLayer);
        _backgroundPicker = new ColorPickerButton
        {
            Name = "BackgroundPicker",
            Color = BackgroundColour,
            EditAlpha = false,
            TooltipText = "Background colour",
            CustomMinimumSize = BackgroundPickerSize,
            FocusMode = Control.FocusModeEnum.None,
        };
        // pinned to the bottom left corner, whatever the window's size
        _backgroundPicker.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _backgroundPicker.OffsetLeft = BackgroundPickerMargin;
        _backgroundPicker.OffsetRight = BackgroundPickerMargin + BackgroundPickerSize.X;
        _backgroundPicker.OffsetTop = -BackgroundPickerMargin - BackgroundPickerSize.Y;
        _backgroundPicker.OffsetBottom = -BackgroundPickerMargin;
        _backgroundPicker.ColorChanged += colour =>
        {
            _background.Color = colour;
            BackgroundColour = colour;
        };
        _backgroundPicker.PopupClosed += () => GD.Print($"Background colour: #{_background.Color.ToHtml(false)}");
        pickerLayer.AddChild(_backgroundPicker);
    }

    // Whether the pointer is on the background picker, or its popup is open (the board leaves clicks alone then).
    private bool IsOverBackgroundPicker(Control hovered) =>
        _backgroundPicker is not null
        && (_backgroundPicker.GetPopup().Visible
            || hovered is not null && (hovered == _backgroundPicker || _backgroundPicker.IsAncestorOf(hovered)));
}
