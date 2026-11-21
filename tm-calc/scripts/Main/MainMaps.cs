using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the map screen. Every map is shown in miniature (MapPicker), two rows of three; clicking one
// chooses it (ChooseMap, through SetupMoveAt) and goes straight on to the round tiles. Under them, "Custom map" opens
// the custom map editor (MainCustomMap.cs). A file of the same class
// (partial), only to keep Main.cs a manageable size.
public partial class Main
{
    private MapPicker _mapChoices;

    private void ReadyMapChoices()
    {
        _mapChoices = GetNodeOrNull<MapPicker>("%MapChoices");
        if (_mapChoices is null)
        {
            _mapChoices = new MapPicker { Name = "MapChoices", Position = MapChoicesCentre };
            _mapSelect.AddChild(_mapChoices);
        }
        _mapChoices.Build(Maps.Premade);
        ReadyCustomMap(); // the "Custom map" key under them and its editor (MainCustomMap.cs)
    }
}
