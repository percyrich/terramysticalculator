using System.Collections.Immutable;

namespace Core;

// One round bonus tile. Six are chosen during setup, one per round.
// During its round it scores victory points for some things a player does; at the end of its round
// (rounds 1-5 only) it rewards players for their position on one track, or for priests on the tracks.
public sealed record RoundTileDefinition
{
    public bool MiniExpansion { get; init; }     // only with the mini-expansion (GameOptions.MiniExpansion)

    // During the round.
    public ImmutableArray<PieceKind> ScoredBuildings { get; init; } = ImmutableArray<PieceKind>.Empty;
    public int VictoryPointsPerBuilding { get; init; }  // for each building of those kinds built
    public int VictoryPointsPerSpade { get; init; }     // for each spade spent (free or paid)
    public int VictoryPointsPerTown { get; init; }      // for each town formed

    // At the end of the round: Reward (and SpadesPerReward) once for every StepsPerReward steps on Track.
    public Track? Track { get; init; }
    public int StepsPerReward { get; init; } = 1;
    public Income Reward { get; init; }
    public int SpadesPerReward { get; init; }            // free spades, spent before the next round (no building)
    public int CoinsPerPriestOnTracks { get; init; }     // for every priest the player has in a priest zone

    public int VictoryPointsFor(PieceKind kind) => ScoredBuildings.Contains(kind) ? VictoryPointsPerBuilding : 0;
}

public static class RoundTiles
{
    private static readonly ImmutableArray<PieceKind> StrongholdOrSanctuary =
        ImmutableArray.Create(PieceKind.Stronghold, PieceKind.Sanctuary);

    // Indexed by tile id.
    public static readonly ImmutableArray<RoundTileDefinition> All = ImmutableArray.Create(
        new RoundTileDefinition { ScoredBuildings = StrongholdOrSanctuary, VictoryPointsPerBuilding = 5,
            Track = Core.Track.Fire, StepsPerReward = 2, Reward = new Income(0, 1, 0, 0) },
        new RoundTileDefinition { ScoredBuildings = ImmutableArray.Create(PieceKind.Dwelling), VictoryPointsPerBuilding = 2,
            Track = Core.Track.Fire, StepsPerReward = 4, Reward = new Income(0, 0, 0, 4) },
        new RoundTileDefinition { ScoredBuildings = ImmutableArray.Create(PieceKind.Dwelling), VictoryPointsPerBuilding = 2,
            Track = Core.Track.Water, StepsPerReward = 4, Reward = new Income(0, 0, 1, 0) },
        new RoundTileDefinition { ScoredBuildings = ImmutableArray.Create(PieceKind.TradingPost), VictoryPointsPerBuilding = 3,
            Track = Core.Track.Water, StepsPerReward = 4, SpadesPerReward = 1 },
        new RoundTileDefinition { VictoryPointsPerSpade = 2,
            Track = Core.Track.Earth, StepsPerReward = 1, Reward = new Income(1, 0, 0, 0) },
        new RoundTileDefinition { VictoryPointsPerTown = 5,
            Track = Core.Track.Earth, StepsPerReward = 4, SpadesPerReward = 1 },
        new RoundTileDefinition { ScoredBuildings = StrongholdOrSanctuary, VictoryPointsPerBuilding = 5,
            Track = Core.Track.Air, StepsPerReward = 2, Reward = new Income(0, 1, 0, 0) },
        new RoundTileDefinition { ScoredBuildings = ImmutableArray.Create(PieceKind.TradingPost), VictoryPointsPerBuilding = 3,
            Track = Core.Track.Air, StepsPerReward = 4, SpadesPerReward = 1 },
        new RoundTileDefinition { ScoredBuildings = ImmutableArray.Create(PieceKind.Temple), VictoryPointsPerBuilding = 4,
            CoinsPerPriestOnTracks = 2, MiniExpansion = true });

    public static int Count => All.Length;

    // Short text for a tile: what scores during the round, then the end-of-round reward.
    public static string Describe(int id)
    {
        var tile = All[id];
        var lines = new List<string>();
        if (tile.VictoryPointsPerBuilding > 0)
            lines.Add($"{string.Join(", ", tile.ScoredBuildings.Select(Buildings.Noun))}: +{tile.VictoryPointsPerBuilding} VP");
        if (tile.VictoryPointsPerSpade > 0)
            lines.Add($"Spade: +{tile.VictoryPointsPerSpade} VP");
        if (tile.VictoryPointsPerTown > 0)
            lines.Add($"Town: +{tile.VictoryPointsPerTown} VP");
        if (tile.Track is Track track)
        {
            var reward = tile.Reward != default ? tile.Reward.ToString() : "";
            if (tile.SpadesPerReward > 0)
                reward = $"{tile.SpadesPerReward} spade";
            lines.Add($"End: {reward} per {tile.StepsPerReward} {track}");
        }
        if (tile.CoinsPerPriestOnTracks > 0)
            lines.Add($"End: {tile.CoinsPerPriestOnTracks} coins per priest on the tracks");
        return string.Join("\n", lines);
    }
}
