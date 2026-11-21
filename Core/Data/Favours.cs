using System.Collections.Immutable;

namespace Core;

// One physical favour tile. Id says which favour it is (an index into Favours.All).
// Copy only tells apart identical tiles of the same favour, so the view can follow each one as it moves;
// like Piece.Number, it means nothing in the rules.
public readonly record struct FavourTile(int Id, int Copy);

// What one favour does. Taking it moves its owner Steps up Track at once (with any track power rewards
// passed on the way). Some favours also have a lasting effect, listed in the optional properties.
public sealed record FavourDefinition(Track Track, int Steps, int Copies)
{
    public Income Income { get; init; }                  // added to the owner's income every round
    public int VictoryPointsPerDwelling { get; init; }   // whenever the owner builds a dwelling
    public int VictoryPointsPerTradingPost { get; init; } // whenever the owner builds a trading post
    // When the owner passes: victory points by how many of their trading posts are on the map.
    // Index = number of trading posts; any number beyond the last entry scores the last entry.
    public ImmutableArray<int> PassVictoryPoints { get; init; } = ImmutableArray<int>.Empty;
    public BonusAction? BonusAction { get; init; }        // unlocked for as long as the favour is held
    public int TownPowerReduction { get; init; }          // towns need this much less power while it is held

    public int PassVictoryPointsFor(int tradingPosts) =>
        PassVictoryPoints.IsEmpty ? 0 : PassVictoryPoints[Math.Min(tradingPosts, PassVictoryPoints.Length - 1)];
}

public static class Favours
{
    // Indexed by favour id. Ids 0-2 are on track 0, 3-5 on track 1, and so on; within a track, 1, 2, 3 steps.
    public static readonly ImmutableArray<FavourDefinition> All = ImmutableArray.Create(
        new FavourDefinition(Track.Fire, 1, 3) { Income = new Income(Coins: 3, Workers: 0, Priests: 0, Power: 0) },
        new FavourDefinition(Track.Fire, 2, 3) { TownPowerReduction = 1 },
        new FavourDefinition(Track.Fire, 3, 1),
        new FavourDefinition(Track.Water, 1, 3) { VictoryPointsPerTradingPost = 3 },
        new FavourDefinition(Track.Water, 2, 3) { BonusAction = Core.BonusAction.CultStep },
        new FavourDefinition(Track.Water, 3, 1),
        new FavourDefinition(Track.Earth, 1, 3) { VictoryPointsPerDwelling = 2 },
        new FavourDefinition(Track.Earth, 2, 3) { Income = new Income(Coins: 0, Workers: 1, Priests: 0, Power: 1) },
        new FavourDefinition(Track.Earth, 3, 1),
        new FavourDefinition(Track.Air, 1, 3) { PassVictoryPoints = ImmutableArray.Create(0, 2, 3, 3, 4) },
        new FavourDefinition(Track.Air, 2, 3) { Income = new Income(Coins: 0, Workers: 0, Priests: 0, Power: 4) },
        new FavourDefinition(Track.Air, 3, 1));

    public static int Count => All.Length;

    // The full supply at the start of a game: [id] = every copy of that favour, bottom first, top last.
    public static ValueList<ValueList<FavourTile>> FullBank() =>
        new(All.Select((favour, id) =>
            new ValueList<FavourTile>(Enumerable.Range(0, favour.Copies).Select(copy => new FavourTile(id, copy)))));

    // Short text for a tile, one effect per line, e.g. "Fire +1\nIncome: 3 coins".
    public static string Describe(int id)
    {
        var favour = All[id];
        var lines = new List<string> { $"{favour.Track} +{favour.Steps}" };
        if (favour.Income != default)
            lines.Add($"Income: {favour.Income}");
        if (favour.VictoryPointsPerDwelling > 0)
            lines.Add($"Dwelling: +{favour.VictoryPointsPerDwelling} VP");
        if (favour.VictoryPointsPerTradingPost > 0)
            lines.Add($"Trading post: +{favour.VictoryPointsPerTradingPost} VP");
        if (!favour.PassVictoryPoints.IsEmpty)
            lines.Add($"Pass: VP for trading posts ({string.Join("/", favour.PassVictoryPoints.Skip(1))})");
        if (favour.TownPowerReduction > 0)
            lines.Add($"Towns: {Config.TownPower - favour.TownPowerReduction} power");
        if (favour.BonusAction == Core.BonusAction.CultStep)
            lines.Add("Bonus action: +1 on any track");
        return string.Join("\n", lines);
    }
}
