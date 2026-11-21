using System.Collections.Immutable;

namespace Core;

// The terrain wheel. The seven basic terrains form a cycle by colour: red, yellow, brown, black, blue,
// green, grey, and back to red. One spade moves a hex one step around it.
public static class Terrains
{
    public static readonly ImmutableArray<Terrain> Wheel = ImmutableArray.Create(
        Terrain.Wasteland, // red
        Terrain.Desert,    // yellow
        Terrain.Plains,    // brown
        Terrain.Swamp,     // black
        Terrain.Lake,      // blue
        Terrain.Forest,    // green
        Terrain.Mountain); // grey

    public static bool IsBasic(Terrain terrain) => Wheel.Contains(terrain);

    // The colour of a terrain: its place on the wheel; white for Ice, orange for Volcano.
    public static PlayerColour ColourOf(Terrain terrain) => terrain switch
    {
        Terrain.Ice => PlayerColour.White,
        Terrain.Volcano => PlayerColour.Orange,
        Terrain.River => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, "Rivers have no colour."),
        _ => (PlayerColour)Wheel.IndexOf(terrain),
    };

    // Spades needed to turn one terrain into the other: the steps between them going either way round
    // the wheel, whichever is shorter (TM). Null if either is not a basic terrain (rivers, ice, volcano).
    // TODO: Age of Innovation and Terra Nova restrict which way spades may turn the wheel.
    public static int? Spades(Terrain from, Terrain to)
    {
        var a = Wheel.IndexOf(from);
        var b = Wheel.IndexOf(to);
        if (a < 0 || b < 0)
            return null;
        var steps = Math.Abs(a - b);
        return Math.Min(steps, Wheel.Length - steps);
    }
}
