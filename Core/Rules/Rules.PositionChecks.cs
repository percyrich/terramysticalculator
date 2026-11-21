namespace Core;

// What follows from a position rather than being recorded: towns and the keys they may hold, favours due, and the
// checks a set-up position (or the end of Free play) must pass.
public static partial class Rules
{
    // ---------- Towns and keys ----------

    // A faction's pieces in groups for towns: its buildings joined by adjacency (its bridges included), and for the
    // Mermaids the river hexes holding their keys, which join the buildings around them.
    private static List<List<int>> TownGroups(GameState state, Faction faction)
    {
        var nodes = state.HexesBuiltOnBy(faction).ToHashSet();
        if (faction == Faction.Mermaids)
            nodes.UnionWith(state.PlacedKeys.Where(key => key.Owner == faction && state.HexTerrain[key.Hex] == Terrain.River)
                .Select(key => key.Hex));
        var groups = new List<List<int>>();
        var seen = new HashSet<int>();
        foreach (var start in nodes.OrderBy(hex => hex))
        {
            if (!seen.Add(start))
                continue;
            var group = new List<int> { start };
            for (var i = 0; i < group.Count; i++)
                foreach (var n in AdjacentHexes(state, faction, group[i]))
                    if (nodes.Contains(n) && seen.Add(n))
                        group.Add(n);
            groups.Add(group);
        }
        return groups;
    }

    private static List<int> KeyHexesIn(GameState state, Faction faction, IReadOnlyCollection<int> group) =>
        state.PlacedKeys.Where(key => key.Owner == faction && group.Contains(key.Hex)).Select(key => key.Hex).ToList();

    private static List<int> BuildingsIn(GameState state, IEnumerable<int> hexes) =>
        hexes.Where(hex => state.Map[hex] is not null).ToList();

    // Groups of the faction's buildings that are towns but hold no key: each needs one (Setup's Done).
    public static ValueList<ValueList<int>> PendingTowns(GameState state, Faction faction)
    {
        if (!state.IsInGame(faction))
            return ValueList<ValueList<int>>.Empty;
        var needed = TownPowerNeeded(state, faction);
        return new ValueList<ValueList<int>>(TownGroups(state, faction)
            .Where(group => KeyHexesIn(state, faction, group).Count == 0 && IsTown(state, faction, BuildingsIn(state, group), needed))
            .Select(group => new ValueList<int>(group.OrderBy(hex => hex))));
    }

    // Whether a group may hold these keys: one key, if it is a town; k keys, if some of its buildings and bridges, taken
    // away, would leave k separate groups, each a town holding exactly one of the keys' hexes (the towns were formed
    // apart and joined later). Removal sets are tried smallest first, within a budget; past it the keys are allowed.
    private static bool KeysFit(GameState state, Faction faction, IReadOnlyCollection<int> group, IReadOnlyCollection<int> keyHexes)
    {
        if (keyHexes.Count == 0)
            return true;
        // Kingdom of Ember: never checked. Their structures flow out of their towns, leaving keys in groups that could
        // never have formed a town (not worth working out).
        if (faction == Faction.KingdomOfEmber)
            return true;
        var needed = TownPowerNeeded(state, faction);
        var buildings = BuildingsIn(state, group);
        if (keyHexes.Count == 1)
            return IsTown(state, faction, buildings, needed);
        if (CityKeysFit(state, faction, group, keyHexes.Count)) // the Swarmlings' Breeding Ground
            return true;

        int Power(int hex) => Factions.BuildingPower(faction, state.Map[hex]!.Value.Kind);
        var k = keyHexes.Count;
        if (buildings.Sum(Power) < k * needed || buildings.Count < k * Config.TownBuildingsWithSanctuary)
            return false;
        var removable = buildings.Where(hex => !keyHexes.Contains(hex)).OrderBy(Power).ToList();
        // The most buildings that can go while k towns' power is left.
        var maxRemoved = 0;
        for (var left = buildings.Sum(Power); maxRemoved < removable.Count && left - Power(removable[maxRemoved]) >= k * needed; maxRemoved++)
            left -= Power(removable[maxRemoved]);
        maxRemoved = Math.Min(maxRemoved, buildings.Count - k * Config.TownBuildingsWithSanctuary);

        var bridges = state.Bridges.Where(b => b.Owner == faction && group.Contains(b.HexA) && group.Contains(b.HexB)).ToList();
        var budget = 200_000;
        for (var size = 0; size <= maxRemoved; size++)
            for (var mask = 0; mask < 1 << bridges.Count; mask++)
            {
                var kept = bridges.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                foreach (var removed in Combinations(removable, size))
                {
                    if (--budget < 0)
                        return true; // too many to try: allowed, as the reach check is
                    if (SplitWorks(state, faction, group, keyHexes, removed, kept, needed))
                        return true;
                }
            }
        return false;
    }

    // With these buildings gone and only these bridges kept, every remaining group holds at most one key, and every
    // group holding one is a town.
    private static bool SplitWorks(GameState state, Faction faction, IReadOnlyCollection<int> group, IReadOnlyCollection<int> keyHexes,
        IReadOnlyCollection<int> removed, List<Bridge> kept, int needed)
    {
        var nodes = group.Where(hex => !removed.Contains(hex)).ToHashSet();
        var seen = new HashSet<int>();
        foreach (var start in nodes)
        {
            if (!seen.Add(start))
                continue;
            var part = new List<int> { start };
            for (var i = 0; i < part.Count; i++)
            {
                var hex = part[i];
                var linked = state.Layout.Neighbours(hex)
                    .Concat(kept.Where(b => b.HexA == hex).Select(b => b.HexB))
                    .Concat(kept.Where(b => b.HexB == hex).Select(b => b.HexA));
                foreach (var n in linked)
                    if (nodes.Contains(n) && seen.Add(n))
                        part.Add(n);
            }
            var keys = part.Count(keyHexes.Contains);
            if (keys > 1 || (keys == 1 && !IsTown(state, faction, BuildingsIn(state, part), needed)))
                return false;
        }
        return true;
    }

    private static IEnumerable<List<int>> Combinations(List<int> items, int size)
    {
        var chosen = new int[size];
        IEnumerable<List<int>> From(int start, int depth)
        {
            if (depth == size)
            {
                yield return chosen.Select(i => items[i]).ToList();
                yield break;
            }
            for (var i = start; i <= items.Count - (size - depth); i++)
            {
                chosen[depth] = i;
                foreach (var combination in From(i + 1, depth + 1))
                    yield return combination;
            }
        }
        return From(0, 0);
    }

    // After every Setup edit: a key whose hex is no longer one of its faction's (or whose group no longer allows it:
    // the most recently placed goes first) goes back to the bank, its faction losing its keys; then each faction's town
    // buildings (TownHexes) are those of its groups holding a key.
    private static GameState KeepTownsConsistent(GameState state, List<GameEvent> events)
    {
        foreach (var faction in state.Players.Select(player => player.Faction))
        {
            while (true)
            {
                var groups = TownGroups(state, faction);
                int? drop = null;
                for (var i = state.PlacedKeys.Count - 1; i >= 0 && drop is null; i--)
                {
                    var key = state.PlacedKeys[i];
                    if (key.Owner != faction)
                        continue;
                    var group = groups.FirstOrDefault(g => g.Contains(key.Hex));
                    if (group is null || !KeysFit(state, faction, group, KeyHexesIn(state, faction, group)))
                        drop = i;
                }
                if (drop is not int index)
                    break;
                var removed = state.PlacedKeys[index];
                events.Add(new KeyRemoved(faction, removed.Tile, removed.Hex));
                state = RemoveKey(state, index);
            }
            var towns = TownGroups(state, faction).Where(group => KeyHexesIn(state, faction, group).Count > 0)
                .SelectMany(group => BuildingsIn(state, group)).OrderBy(hex => hex);
            var townHexes = new ValueList<int>(towns);
            if (townHexes != state[faction].TownHexes)
                state = state.WithPlayer(state[faction] with { TownHexes = townHexes });
        }
        return state;
    }

    // ---------- Reach ----------

    // How many starting dwellings the faction's buildings need at least: the groups they fall into when each building
    // must be in reach (at the current range: adjacency, bridges, shipping, carpet or tunnel) of the others of its group.
    private static int ReachGroups(GameState state, Faction faction)
    {
        // A landscape on a river (never reached as land is) or the Chaos Portal (placed anywhere) needs no reaching: it
        // stands throughout, lending its reach.
        var placedAnywhere = state.LandscapeHex(faction) is int landscape
            && (state.HexTerrain[landscape] == Terrain.River || faction == Faction.ChaosMagicians)
            ? landscape : -1;
        var left = state.HexesBuiltOnBy(faction).Where(hex => hex != placedAnywhere).ToList();
        var groups = 0;
        while (left.Count > 0)
        {
            var group = new HashSet<int> { left[0] };
            left.RemoveAt(0);
            for (var grew = true; grew;)
            {
                grew = false;
                // Only this group's buildings of the faction stand.
                var only = state with
                {
                    Map = new ValueList<Piece?>(state.Map.Select((piece, hex) =>
                        piece is { } p && p.Owner == faction && !group.Contains(hex) && hex != placedAnywhere ? null : piece)),
                };
                foreach (var hex in left.ToList())
                    if (ReachOf(only, faction, hex, allowSkip: true) != Reach.None)
                    {
                        group.Add(hex);
                        left.Remove(hex);
                        grew = true;
                    }
            }
            groups++;
        }
        return groups;
    }

    // ---------- Setup's Done (and leaving Free) ----------

    // Everything that stops a set-up position from being played on, worded for the top line, in the order a player would
    // want to fix them; empty when Done may be pressed. Also checked when leaving Free play.
    public static ValueList<string> SetupIssues(GameState state)
    {
        var issues = new List<string>();
        string Hex(int hex) => GameLog.HexName(state, hex);
        if (state.Round < 1 || state.Round > Config.Rounds)
            issues.Add("No current round: click the round tile being played");
        foreach (var player in state.Players)
        {
            var f = player.Faction;
            var who = Name(f);
            var buildings = state.HexesBuiltOnBy(f).ToList();
            if (player.PassTile is null)
                issues.Add($"No pass tile: {who}");
            var starting = Factions.StartingDwellings(f);
            if (buildings.Count < starting)
                issues.Add($"Too few buildings: {who} ({buildings.Count} on the map, {starting} to start with)");
            else if (ReachGroups(state, f) > starting)
                issues.Add($"Out of reach: {who} (their buildings can't all have been reached from {starting} starting dwelling{(starting == 1 ? "" : "s")})");
            if (f == Faction.Riverwalkers)
                foreach (var hex in buildings.Where(hex => state.HexTerrain[hex] != Terrain.River
                             && !state.Layout.Neighbours(hex).Any(n => state.HexTerrain[n] == Terrain.River)))
                    issues.Add($"Not beside a river: Riverwalkers at {Hex(hex)}");
            foreach (var bridge in state.Bridges.Where(b => b.Owner == f && !OwnsBuildingAt(state, f, b.HexA) && !OwnsBuildingAt(state, f, b.HexB)))
                issues.Add($"Bridge away from their buildings: {who} ({Hex(bridge.HexA)}–{Hex(bridge.HexB)})");
            foreach (var town in f == Faction.KingdomOfEmber ? ValueList<ValueList<int>>.Empty : PendingTowns(state, f))
                issues.Add($"Town without a key: {who} at {Hex(town[0])}"); // (never for the Kingdom of Ember: KeysFit)
            if (state.LandscapeHex(f) is int landscape)
            {
                if (!PrerequisiteMetInSetup(state, f))
                    issues.Add($"Landscape without its prerequisite: {who} ({Landscapes.PrerequisiteText(f)})");
                if (f == Faction.Swarmlings && TownGroups(state, f).FirstOrDefault(group => group.Contains(landscape)) is { } city
                    && CityGrows(state, f, BuildingsIn(state, city), TownPowerNeeded(state, f)))
                    issues.Add($"Town without its next key: {who} at {Hex(landscape)}");
            }
            foreach (var group in TownGroups(state, f))
                if (!KeysFit(state, f, group, KeyHexesIn(state, f, group)))
                    issues.Add($"Too many keys: {who} at {Hex(group.Min())}");
            if (FavoursToClaim(state, f) is > 0 and var claim)
                issues.Add($"Favours to claim: {who} ({claim})");
            if (FavoursOver(state, f) is > 0 and var over)
                issues.Add($"Too many favours: {who} ({over} more than their buildings give)");
            if (f is not (Faction.Dragonlords or Faction.Shapeshifters) && player.Power.Total > Config.PowerTokenCap)
                issues.Add($"Too many power tokens: {who} ({player.Power.Total}, at most {Config.PowerTokenCap})");
            if (player.Keys < 0)
                issues.Add($"Not enough keys for the tops of the tracks: {who}");
            foreach (var bonus in player.BonusActionsUsed)
                if (BonusActionProblem(state, f, bonus) is string problem)
                    issues.Add($"Bonus action: {problem}");
        }
        foreach (var track in Enum.GetValues<Track>())
        {
            var top = state.Players.Where(p => p.TrackSteps[(int)track] == Config.TrackMax).Select(p => Name(p.Faction)).ToList();
            if (top.Count > 1)
                issues.Add($"More than one faction at the top of {track}: {string.Join(", ", top)}");
        }
        if (state.Mode == PlayMode.Strict && state.AnyoneInDebt)
            issues.Add($"Strict: {DebtText(state, would: false)}");
        return new ValueList<string>(issues);
    }
}
