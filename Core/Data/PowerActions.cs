using System.Collections.Immutable;

namespace Core;

// One of the special power actions: spend power from bowl 3 (burning first if needed) for a benefit.
// Each can be taken by only one player per round, as that player's action.
public sealed record PowerActionDefinition(int Cost)
{
    public Income Income { get; init; }
    public int Spades { get; init; }   // free spades, spent at once (a dwelling may be built, paid as normal)
    public int Bridges { get; init; }  // bridges to place at once
}

public static class PowerActions
{
    // Indexed by action id.
    public static readonly ImmutableArray<PowerActionDefinition> All = ImmutableArray.Create(
        new PowerActionDefinition(3) { Bridges = 1 },
        new PowerActionDefinition(3) { Income = new Income(0, 0, 1, 0) },
        new PowerActionDefinition(4) { Income = new Income(7, 0, 0, 0) },
        new PowerActionDefinition(4) { Income = new Income(0, 2, 0, 0) },
        new PowerActionDefinition(4) { Spades = 1 },
        new PowerActionDefinition(6) { Spades = 2 });

    public static int Count => All.Length;

    public static string Describe(int id)
    {
        var action = All[id];
        var gain = action.Bridges > 0 ? "1 bridge"
            : action.Spades > 0 ? $"{action.Spades} spade{(action.Spades == 1 ? "" : "s")}"
            : action.Income.ToString();
        return $"{action.Cost} power: {gain}";
    }
}
