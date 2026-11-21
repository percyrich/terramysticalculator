using System.Collections.Immutable;

namespace Core;

// One key token (town tile). Id says which kind (an index into KeyTiles.All); Copy only tells apart
// identical tokens, like Piece.Number. Taken when a town is formed, and placed on one of its hexes.
public readonly record struct KeyTile(int Id, int Copy);

// What forming a town with this token gives: victory points, resources, steps on every track,
// possibly a free shipping upgrade, and keys (needed for the top step of a track).
public sealed record KeyTileDefinition(int VictoryPoints, int Keys, int Copies)
{
    public Income Income { get; init; }
    public int StepsOnEveryTrack { get; init; }
    public bool ShippingUpgrade { get; init; }   // scores like a paid upgrade, but costs nothing
    public bool MiniExpansion { get; init; }     // only with the mini-expansion (GameOptions.MiniExpansion)
}

public static class KeyTiles
{
    // Indexed by token id.
    public static readonly ImmutableArray<KeyTileDefinition> All = ImmutableArray.Create(
        new KeyTileDefinition(9, 1, 2) { Income = new Income(0, 0, 1, 0) },
        new KeyTileDefinition(8, 1, 2) { StepsOnEveryTrack = 1 },
        new KeyTileDefinition(7, 1, 2) { Income = new Income(0, 2, 0, 0) },
        new KeyTileDefinition(6, 1, 2) { Income = new Income(0, 0, 0, 8) },
        new KeyTileDefinition(5, 1, 2) { Income = new Income(6, 0, 0, 0) },
        new KeyTileDefinition(4, 1, 2) { ShippingUpgrade = true, MiniExpansion = true },
        new KeyTileDefinition(11, 1, 1) { MiniExpansion = true },
        new KeyTileDefinition(2, 2, 1) { StepsOnEveryTrack = 2, MiniExpansion = true });

    public static int Count => All.Length;

    // [id] = every copy of that token, bottom first, top last. miniExpansion false: the mini-expansion's stacks empty.
    public static ValueList<ValueList<KeyTile>> FullBank(bool miniExpansion = true) =>
        new(All.Select((token, id) => token.MiniExpansion && !miniExpansion
            ? ValueList<KeyTile>.Empty
            : new ValueList<KeyTile>(Enumerable.Range(0, token.Copies).Select(copy => new KeyTile(id, copy)))));

    public static string Describe(int id)
    {
        var token = All[id];
        var lines = new List<string> { $"{token.VictoryPoints} VP, {token.Keys} key{(token.Keys == 1 ? "" : "s")}" };
        if (token.Income != default)
            lines.Add(token.Income.ToString());
        if (token.StepsOnEveryTrack > 0)
            lines.Add($"+{token.StepsOnEveryTrack} on every track");
        if (token.ShippingUpgrade)
            lines.Add("Free shipping upgrade");
        return string.Join("\n", lines);
    }
}
