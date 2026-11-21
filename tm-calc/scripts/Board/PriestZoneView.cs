using System;
using Core;
using Godot;

namespace TMCalc;

// A priest zone (priest_zone.tscn's root): its picture drawn by CultArt in its cult's colour (the cult its node is
// named after: Fire, Water, Earth or Air; Fire otherwise), each slot (Slot0 to Slot4) moved onto its circle in the
// picture and its number (StepLabel) filling the circle in the cult's colour; in the editor too, where the numbers
// are the slots' own (the game's are Main's: LabelPriestSlots). Main also does all this (MainCults.DressCults). The
// picture is made when the scene loads and never saved into it.
[Tool]
[GlobalClass]
public partial class PriestZoneView : Sprite2D
{
    public Track Cult => Enum.TryParse(Name.ToString(), out Track track) ? track : Track.Fire;

    public override void _Ready()
    {
        Color colour = CultArt.Colour(Cult);
        Texture = CultArt.PriestZone(colour);
        for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
        {
            if (GetNodeOrNull<Node2D>($"Slot{slot}") is not Node2D node)
                continue;
            Rect2 area = CultArt.PriestSlot(slot);
            node.Position = area.GetCenter() - CultArt.PriestZoneSize / 2;
            if (node.GetNodeOrNull<CanvasItem>("SlotSprite") is CanvasItem oldSlot)
                oldSlot.Visible = false; // the circle is in the picture
            if (node.GetNodeOrNull<Label>("StepLabel") is not Label label)
                continue;
            label.Position = -area.Size / 2;
            label.Size = area.Size;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(area.Size.X * CultArt.NumberSize));
            label.AddThemeColorOverride("font_color", CultArt.NumberColour(colour));
            if (Engine.IsEditorHint())
                label.Text = Config.PriestSlots[slot].Steps.ToString();
        }
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() == Sprite2D.PropertyName.Texture)
            property["usage"] = (long)PropertyUsageFlags.None; // made here: neither shown nor saved
    }
}
