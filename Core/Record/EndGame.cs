using System.Collections.Immutable;

namespace Core;

// One player's final scoring, step by step, for the end-of-game table.
// TODO: a much more detailed breakdown later (VP by source over the game, resources earned, statistics).
public sealed record FinalScore(
    Faction Faction,
    int VictoryPointsBefore,      // before final scoring
    int Coins,                    // after converting priests, workers and power
    int CoinPoints,
    ValueList<int> TrackSteps,    // [(int)Track]
    ValueList<int> TrackPoints,   // [(int)Track]
    int Network,                  // buildings in the largest connected group (shipping included)
    int NetworkPoints,
    FinalScoringTile? Tile,       // Fire & Ice's final scoring tile, if one was chosen
    int TileValue,                // that tile's value for the player (FinalScoring.Value; 0 without a tile)
    int TilePoints,
    int Total);

// What final scoring's tracks, network and tile would give a player if the game ended now (EndGame.Preview).
public sealed record ScorePreview(
    Faction Faction,
    ValueList<int> TrackPoints,   // [(int)Track]
    int Network,                  // buildings in the largest network
    int NetworkPoints,
    FinalScoringTile? Tile,       // Fire & Ice's final scoring tile, if one was chosen
    int TileValue,
    int TilePoints)
{
    public int CultPoints => TrackPoints.Sum();
}

// Final scoring at the end of round 6 (instead of the round tile's reward):
//   1. resources to coins: priests to workers, workers to coins, power to coins (burning all it can first);
//      1 VP per 3 coins (the Alchemists: per 2);
//   2. each track: 8 / 4 / 2 VP for the highest / second / third (only players with a step on it);
//   3. the largest connected group of buildings (adjacency, the owner's bridges, and shipping; for the
//      Fakirs and Dwarves, what their carpet or tunnel could reach instead of shipping):
//      18 / 12 / 6 VP for the largest / second / third;
//   4. Fire & Ice's final scoring tile, if one was chosen (FinalScoring): the same, by the tile's value.
// Ties share: see RankPoints (a value of 0 scores nothing, whatever its place).
public static class EndGame
{
    public static readonly ImmutableArray<int> TrackPlacePoints = ImmutableArray.Create(8, 4, 2);
    public static readonly ImmutableArray<int> NetworkPlacePoints = ImmutableArray.Create(18, 12, 6);
    public const int CoinsPerVictoryPoint = 3;

    // Points for each value by its place, highest value first. Only places up to the number of players
    // count. Players tied share the points of every place they cover: added up, divided among them,
    // rounded down. Values of 0 or less score nothing (and take no place).
    // e.g. values 5, 5, 3 with points 8, 4, 2: (8 + 4) / 2 = 6 each, then 2.
    public static ValueList<int> RankPoints(IReadOnlyList<int> values, IReadOnlyList<int> points)
    {
        var places = Math.Min(points.Count, values.Count);
        var result = new int[values.Count];
        var place = 0;
        foreach (var group in values.Select((value, index) => (value, index))
                     .Where(entry => entry.value > 0)
                     .GroupBy(entry => entry.value)
                     .OrderByDescending(group => group.Key))
        {
            var tied = group.ToList();
            var shared = Enumerable.Range(place, tied.Count).Where(p => p < places).Sum(p => points[p]);
            foreach (var (_, index) in tied)
                result[index] = shared / tied.Count;
            place += tied.Count;
        }
        return new ValueList<int>(result);
    }

    // The final state: resources converted, every VP added, scores recorded, winners decided.
    public static GameState Finish(GameState state, List<GameEvent> events)
    {
        var befores = state.Players.Select(player => player.VictoryPoints).ToList();

        // 1. Everything to coins.
        foreach (var player in state.Players.ToList())
        {
            // Priests become workers, then coins; the Dynion Geifr's give 2 workers and 2 coins each, as their conversion
            // during play does.
            var workers = player.Workers + player.Priests.Count * (player.Faction == Faction.DynionGeifr
                ? Config.DynionGeifrPriestWorkers + Config.DynionGeifrPriestCoins : 1);
            var power = player.Power; // every possible burn (the Children of the Wyrm move 2 on for each)
            while (power.Bowl2 >= 2)
                power = power.BurnOnce(player.Faction == Faction.ChildrenOfTheWyrm);
            // Power to coins as the faction converts it (the Enlightened with their stronghold: 2 coins a power, as BGA:
            // 893445224).
            var coins = player.Coins + workers + power.Bowl3 * Rules.ConversionGain(state, player.Faction);
            state = state.WithPlayer(player with
            {
                Priests = ValueList<Piece>.Empty,
                PriestSupply = new ValueList<Piece>(player.Priests.Reverse().Concat(player.PriestSupply)),
                Workers = 0,
                Power = power.Spend(power.Bowl3),
                Coins = coins,
            });
        }
        var coinPoints = state.Players.Select(player => Math.Max(0, player.Coins) / Factions.CoinsPerVictoryPoint(player.Faction)).ToList();

        // 2.–4. Tracks, network and the final scoring tile (step 1 changes none of them).
        var previews = Preview(state);

        var scores = new List<FinalScore>();
        for (var p = 0; p < state.Players.Count; p++)
        {
            var player = state.Players[p];
            var preview = previews[p];
            var total = befores[p] + coinPoints[p] + preview.CultPoints + preview.NetworkPoints + preview.TilePoints;
            scores.Add(new FinalScore(player.Faction, befores[p], player.Coins, coinPoints[p],
                player.TrackSteps, preview.TrackPoints, preview.Network, preview.NetworkPoints,
                preview.Tile, preview.TileValue, preview.TilePoints, total));
        }
        foreach (var score in scores)
            state = state.WithPlayer(state[score.Faction] with { VictoryPoints = score.Total });

        var best = scores.Max(score => score.Total);
        var winners = new ValueList<Faction>(scores.Where(score => score.Total == best).Select(score => score.Faction));
        events.Add(new GameEnded(winners));
        return state with
        {
            Phase = Phase.Finished,
            ActivePlayer = null,
            Winners = winners,
            FinalScores = new ValueList<FinalScore>(scores),
        };
    }

    // Final scoring's steps 2 to 4 for every player as things stand (nothing changed; resources not converted), in the
    // order of state.Players: each track 8 / 4 / 2, the largest network 18 / 12 / 6, and the final scoring tile (if
    // one was chosen) 18 / 12 / 6 by its value.
    public static List<ScorePreview> Preview(GameState state)
    {
        // 2. Tracks. The Cultists with their Cult Palace break ties: half a step ahead of where they are (on a track they
        // have a step on).
        var trackPoints = state.Players.Select(_ => new int[Config.TrackCount]).ToList();
        foreach (var track in Enum.GetValues<Track>())
        {
            int Ranked(PlayerState player)
            {
                var steps = player.TrackSteps[(int)track];
                var palace = player.Faction == Faction.Cultists && state.HasLandscape(player.Faction) && steps > 0;
                return steps * 2 + (palace ? 1 : 0);
            }
            var points = RankPoints(state.Players.Select(Ranked).ToList(), TrackPlacePoints);
            for (var p = 0; p < state.Players.Count; p++)
                trackPoints[p][(int)track] = points[p];
        }

        // 3. Largest network.
        var networks = state.Players.Select(player => LargestNetwork(state, player.Faction)).ToList();
        var networkPoints = RankPoints(networks, NetworkPlacePoints);

        // 4. The final scoring tile.
        var tile = state.Options.FinalScoring;
        var tileValues = state.Players.Select(player => tile is FinalScoringTile chosen ? FinalScoring.Value(state, player.Faction, chosen) : 0).ToList();
        var tilePoints = tile is null ? ValueList<int>.Repeat(0, state.Players.Count) : RankPoints(tileValues, NetworkPlacePoints);

        return state.Players.Select((player, p) => new ScorePreview(player.Faction, new ValueList<int>(trackPoints[p]),
            networks[p], networkPoints[p], tile, tileValues[p], tilePoints[p])).ToList();
    }

    // The most buildings in one network (Networks).
    public static int LargestNetwork(GameState state, Faction faction) =>
        Networks(state, faction).Select(network => network.Count).DefaultIfEmpty(0).Max();

    // The faction's buildings in groups (networks), where two buildings are joined if one is within reach of the
    // other: touching (or bridged, for the owner), or across at most Shipping river hexes. Each group's hexes.
    public static List<List<int>> Networks(GameState state, Faction faction)
    {
        var buildings = state.HexesBuiltOnBy(faction).ToHashSet();
        var shipping = state[faction].Shipping; // no pass tile bonus: that is for the main phase only
        var seen = new HashSet<int>();
        var networks = new List<List<int>>();
        foreach (var start in buildings)
        {
            if (!seen.Add(start))
                continue;
            var group = new List<int> { start };
            for (var i = 0; i < group.Count; i++)
                foreach (var hex in LandWithinReach(state, faction, group[i], shipping))
                    if (buildings.Contains(hex) && seen.Add(hex))
                        group.Add(hex);
            networks.Add(group);
        }
        return networks;
    }

    // Land hexes touching this one (or bridged to it), or reached across at most `shipping` river hexes.
    // Fakirs and Dwarves: instead of shipping, any hex their carpet or tunnel could reach (at most
    // Rules.SkipRange hexes of any kind skipped).
    private static IEnumerable<int> LandWithinReach(GameState state, Faction faction, int from, int shipping)
    {
        if (!Factions.HasShipping(faction))
        {
            // Dwarves: from their Great Tunnel, a tunnel skips 2 hexes.
            var reach = faction == Faction.Dwarves && state.LandscapeHex(faction) == from
                ? Landscapes.DwarvesTunnelSkip + 1
                : Rules.SkipRange(state, faction) + 1;
            var near = Within(state, from, reach);
            // ... and so the Great Tunnel is within reach of whatever its tunnels reach (both ways).
            if (faction == Faction.Dwarves && state.LandscapeHex(faction) is int tunnel && tunnel != from
                && Within(state, from, Landscapes.DwarvesTunnelSkip + 1).Contains(tunnel))
                near.Add(tunnel);
            near.UnionWith(Rules.AdjacentHexes(state, faction, from));
            near.Remove(from);
            return near.Where(hex => state.HexTerrain[hex] != Terrain.River
                || state.Map[hex] is Piece piece && piece.Owner == faction);
        }

        bool IsRiver(int hex) => state.HexTerrain[hex] == Terrain.River;
        // A river hex holding the faction's own piece (a landscape or town on a river, a Selkies dwelling) is part of
        // the network like any building: reached like land, and crossed like river (BGA table 242946108: the
        // Riverwalkers' landscape on a river joins their groups; before, a group found it only by starting from it).
        bool Own(int hex) => state.Map[hex] is Piece piece && piece.Owner == faction;
        var found = new HashSet<int>();
        var rivers = new HashSet<int>();
        var frontier = new List<int> { from };
        for (var crossed = 0; frontier.Count > 0; crossed++)
        {
            var next = new List<int>();
            foreach (var hex in frontier)
                foreach (var neighbour in Rules.AdjacentHexes(state, faction, hex))
                {
                    if (!IsRiver(neighbour) || Own(neighbour))
                        found.Add(neighbour);
                    if (IsRiver(neighbour) && crossed < shipping && rivers.Add(neighbour))
                        next.Add(neighbour);
                }
            frontier = next;
        }
        found.Remove(from);
        return found;
    }

    // Every hex at most `distance` hexes from this one (counting through any hex), itself included.
    private static HashSet<int> Within(GameState state, int from, int distance)
    {
        var near = new HashSet<int> { from };
        var ring = new List<int> { from };
        for (var step = 0; step < distance; step++)
            ring = ring.SelectMany(state.Layout.Neighbours).Where(near.Add).ToList();
        return near;
    }
}
