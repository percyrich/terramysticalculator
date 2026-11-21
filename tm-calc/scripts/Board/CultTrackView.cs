using System;
using Core;
using Godot;

namespace TMCalc;

// A cult track (track.tscn's root, a Sprite2D): its steps laid out by EvenlySpaced, and its picture drawn by CultArt in
// its cult's colour, in the editor too (the cult its node is named after: Fire, Water, Earth or Air; Fire otherwise,
// e.g. in track.tscn itself). The editor also shows the padlock (the game's is Main's: MainCults). The picture is
// made when the scene loads and never saved into it.
[Tool]
[GlobalClass]
public partial class CultTrackView : EvenlySpaced
{
    private const string PreviewMeta = "cult_preview";

    public Track Cult => Enum.TryParse(Name.ToString(), out Track track) ? track : Track.Fire;

    public override void _Ready()
    {
        base._Ready();
        Set(Sprite2D.PropertyName.Texture, CultArt.TrackTexture(CultArt.Colour(Cult)));
        if (!Engine.IsEditorHint())
            return;
        foreach (Node made in GetChildren(includeInternal: true))
            if (made.HasMeta(PreviewMeta))
                made.QueueFree();
        var padlock = new Sprite2D { Texture = CultArt.Padlock(), Position = CultArt.PadlockCentre - CultArt.TrackSize / 2 };
        padlock.SetMeta(PreviewMeta, true);
        AddChild(padlock, false, InternalMode.Back); // no owner: never saved
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() == Sprite2D.PropertyName.Texture)
            property["usage"] = (long)PropertyUsageFlags.None; // made here: neither shown nor saved
    }
}
