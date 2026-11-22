using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// The end-of-game statistics (GameStatistics), worked out from the log.
public class GameStatsTests
{
    private static StatRow Row(StatTable table, string label) => table.Rows.First(row => row.Label == label);

    [Fact]
    public void Statistics_count_what_each_faction_did_by_source()
    {
        var history = new History(GameState.Initial);
        foreach (var move in Concat(TwoPlayerStart, new Move[] { new ConvertWorkerToCoin(W), Build(W, 2, 10) }))
            Assert.True(history.Apply(move).Accepted, move.ToString());
        var stats = GameStatistics.Compute(history.Log().Take(history.LogIndex + 1).ToList(), history.Current);

        Assert.Equal(new[] { W, G }, stats.Factions);
        Assert.Equal(new int?[] { 20, 20 }, Row(stats.VictoryPoints, "Starting VPs").Values);
        Assert.Equal(history.Current[W].VictoryPoints, stats.Totals[0]);
        Assert.DoesNotContain(stats.VictoryPoints.Rows, row => row.Label is "Adjustments" or "Fire & Ice scoring");
        Assert.Contains(stats.VictoryPoints.Rows, row => row.Label == "Landscapes"); // the tests' games have the expansion (GameOptions.All)
        Assert.DoesNotContain(stats.VictoryPoints.Rows, row => row.Label == "Faction Stronghold"); // neither can have any
        Assert.Equal(new int?[] { 0, null }, Row(stats.VictoryPoints, "Faction ability").Values); // the Witches' towns only

        IReadOnlyList<int?> dwellings = Row(stats.Actions, "Dwellings built").Values!;
        Assert.Equal(dwellings[1] + 1, dwellings[0]); // the starting ones, and the Witches' one more
        Assert.Equal(new int?[] { 1, 0 }, Row(stats.Actions, "Number of actions taken").Values);
        Assert.Equal(new int?[] { 1, 0 }, Row(stats.Actions, "Transforms to home terrain").Values);
        Assert.Equal(1, Row(stats.Resources, "Conversions").Values![0]); // coins: the worker's coin
        Assert.Equal(new[] { "Coins", "Workers", "Priests", "Power" },
            stats.Resources.Rows.Where(row => row.IsSection).Select(row => row.Label));
    }

    // 892842578: the Alchemists' VP sold for coins showed under Adjustments; both trades are their ability.
    [Fact]
    public void The_Alchemists_trades_count_under_their_faction_ability()
    {
        const Faction Al = Faction.Alchemists;
        var history = new History(GameState.Initial);
        foreach (var move in Concat(Solo(Al, 7, 101), new Move[]
                 {
                     new ConvertVictoryPointToCoin(Al), new ConvertVictoryPointToCoin(Al), new ConvertCoinsToVictoryPoint(Al),
                 }))
            Assert.True(history.Apply(move).Accepted, move.ToString());
        var stats = GameStatistics.Compute(history.Log().Take(history.LogIndex + 1).ToList(), history.Current);

        var abilityRows = stats.VictoryPoints.Rows.Where(row => row.Label == "Faction ability").ToList();
        Assert.Equal(new int?[] { 1 }, abilityRows[0].Values);  // gains: 2 coins → 1 VP
        Assert.Equal(new int?[] { -2 }, abilityRows[1].Values); // losses: 2 VP sold
        Assert.DoesNotContain(stats.VictoryPoints.Rows, row => row.Label == "Adjustments");
    }
}
