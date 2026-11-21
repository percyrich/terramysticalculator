namespace Core;

// Fire & Ice's final scoring tiles. One or none is chosen with the game (GameOptions.FinalScoring); at the end it is
// scored like the largest network (EndGame: 18 / 12 / 6 by place, ties sharing, a value of 0 scoring nothing).
// Every value can be worked out for any player at any time.
// "Connected": in one network, as for the largest network (EndGame.Networks: touching, the owner's bridges, shipping
// without the pass tile's bonus; for the Fakirs and Dwarves their carpet or tunnel reach instead of shipping).
// Distances are as the crow flies: the fewest hex steps between two hexes, whatever is on the hexes between.
//   StrongholdSanctuary  the distance between the stronghold and the sanctuary, if both are built and connected (else 0)
//   Distance             the longest distance between two connected buildings
//   Clusters             the most neighbourhoods in one network; a neighbourhood: buildings joined by touching or by
//                        the owner's bridges (as for towns)
//   Edges                the most buildings on the map's outer edge in one network (OuterEdge)
public static class FinalScoring
{
    public static int Value(GameState state, Faction faction, FinalScoringTile tile) => tile switch
    {
        FinalScoringTile.StrongholdSanctuary => StrongholdSanctuaryDistance(state, faction),
        FinalScoringTile.Distance => LongestDistance(state, faction),
        FinalScoringTile.Clusters => Clusters(state, faction),
        FinalScoringTile.Edges => Edges(state, faction),
        _ => 0,
    };

    public static int StrongholdSanctuaryDistance(GameState state, Faction faction)
    {
        if (HexOf(state, faction, PieceKind.Stronghold) is not int stronghold
            || HexOf(state, faction, PieceKind.Sanctuary) is not int sanctuary)
            return 0;
        return EndGame.Networks(state, faction).Any(network => network.Contains(stronghold) && network.Contains(sanctuary))
            ? Distance(state.Layout, stronghold, sanctuary)
            : 0;
    }

    public static int LongestDistance(GameState state, Faction faction) =>
        EndGame.Networks(state, faction)
            .SelectMany(network => network.SelectMany(a => network.Select(b => Distance(state.Layout, a, b))))
            .DefaultIfEmpty(0).Max();

    public static int Clusters(GameState state, Faction faction) =>
        EndGame.Networks(state, faction).Select(network => Neighbourhoods(state, faction, network)).DefaultIfEmpty(0).Max();

    public static int Edges(GameState state, Faction faction)
    {
        var edge = OuterEdge(state.Layout);
        return EndGame.Networks(state, faction).Select(network => network.Count(edge.Contains)).DefaultIfEmpty(0).Max();
    }

    // The fewest steps from hex a to hex b, crossing any hexes (on the map or not).
    public static int Distance(MapLayout layout, int a, int b)
    {
        var (rowA, xA) = Doubled(layout, a);
        var (rowB, xB) = Doubled(layout, b);
        int rows = Math.Abs(rowA - rowB), across = Math.Abs(xA - xB);
        return rows + Math.Max(0, (across - rows) / 2);
    }

    // The hexes on the map's outer edge: those touching a place off the map that can be reached from outside the
    // map without crossing it (a gap enclosed by the map doesn't count).
    public static HashSet<int> OuterEdge(MapLayout layout)
    {
        var onMap = new Dictionary<(int Row, int X), int>();
        for (var hex = 0; hex < layout.HexCount; hex++)
            onMap[Doubled(layout, hex)] = hex;
        int minX = onMap.Keys.Min(p => p.X) - 2, maxX = onMap.Keys.Max(p => p.X) + 2;
        bool InFrame((int Row, int X) p) => p.Row >= -1 && p.Row <= layout.RowCount && p.X >= minX && p.X <= maxX;

        // Every place off the map reachable from outside, within a frame one hex wider than the map all round; a
        // start just above hex 0 sets which places are hexes in this grid.
        var (row0, x0) = Doubled(layout, 0);
        var start = (row0 - 1, x0 - 1);
        var outside = new HashSet<(int, int)> { start };
        var queue = new Queue<(int Row, int X)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var here = queue.Dequeue();
            foreach (var next in Around(here))
                if (InFrame(next) && !onMap.ContainsKey(next) && outside.Add(next))
                    queue.Enqueue(next);
        }

        var edge = new HashSet<int>();
        foreach (var (place, hex) in onMap)
            if (Around(place).Any(next => !InFrame(next) || outside.Contains(next)))
                edge.Add(hex);
        return edge;
    }

    // The six places round one, in doubled coordinates (MapLayout.Neighbours: x is 2 × column, plus 1 in shifted rows).
    private static IEnumerable<(int Row, int X)> Around((int Row, int X) p) => new[]
    {
        (p.Row, p.X - 2), (p.Row, p.X + 2), (p.Row - 1, p.X - 1), (p.Row - 1, p.X + 1), (p.Row + 1, p.X - 1), (p.Row + 1, p.X + 1),
    };

    private static (int Row, int X) Doubled(MapLayout layout, int hex)
    {
        var (row, column) = layout.Position(hex);
        return (row, 2 * column + (layout.IsShifted(row) ? 1 : 0));
    }

    // How many neighbourhoods (touching, or the owner's bridges) the network's buildings form.
    // The faction's key on an empty river hex (the Mermaids' river town) joins the settlements on either side of it
    // (as on BGA); the key itself is no settlement.
    private static int Neighbourhoods(GameState state, Faction faction, List<int> network)
    {
        var buildings = network.ToHashSet();
        var riverKeys = state.PlacedKeys
            .Where(key => key.Owner == faction && state.HexTerrain[key.Hex] == Terrain.River && state.Map[key.Hex] is null)
            .Select(key => key.Hex).ToHashSet();
        var seen = new HashSet<int>();
        var count = 0;
        foreach (var start in network)
        {
            if (!seen.Add(start))
                continue;
            count++;
            var group = new List<int> { start };
            for (var i = 0; i < group.Count; i++)
                foreach (var hex in Rules.AdjacentHexes(state, faction, group[i]))
                    if ((buildings.Contains(hex) || riverKeys.Contains(hex)) && seen.Add(hex))
                        group.Add(hex);
        }
        return count;
    }

    private static int? HexOf(GameState state, Faction faction, PieceKind kind)
    {
        for (var hex = 0; hex < state.Map.Count; hex++)
            if (state.Map[hex] is { } piece && piece.Owner == faction && piece.Kind == kind)
                return hex;
        return null;
    }
}
