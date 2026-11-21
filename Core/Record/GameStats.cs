using System.Reflection;

namespace Core;

// The end-of-game statistics: three tables, one column per faction in
// the first round's turn order. A row is a label and a value per faction (null: a dash, the row can't apply to that
// faction); a row with no values at all is a section heading ("Gains", "Coins", ...).
//   GameStatistics.Compute(played, final): played = the log's records up to the game's end (History.Log() as far as
//   LogIndex), final = the finished game's state.
// VPs: Starting VPs (as play began); Gains by source (VictoryPointsGained.Source); Losses (paying for power, a faction's
// ability); Endgame (GameState.FinalScores); Adjustments (Setup and Calculator edits: whatever else changed the VP,
// only when some faction has any); Total.
// Actions: actions taken (Rules.IsAction), buildings built (starting dwellings too), landscapes, towns, favours,
// transforms (to the faction's home terrain or another), and each power action taken.
// Resources: coins, workers, priests and power gained, by where they came from; each section's Total adds its rows.
// Income, round bonuses, pass tiles' coins, town keys, power actions and the Treasurers' Safe come from the events and
// the boards' data; whatever else a move gave (its after less its before, less what is accounted for) goes to
// Conversions (a conversion), Power actions (a power action), the faction's Stronghold (its stronghold's action, or the
// move building it) or the faction's Ability (anything else). Setup and Calculator edits, and final scoring's own
// conversions, count for nothing. Power: PowerGained by source; Burning is the power each burn frees (one token into
// bowl III).
// Dashes: Spade upgrade and Shipping upgrade for a faction that can't upgrade them (FactionDefinition); the Faction
// ability and Faction Stronghold rows by FactionStats.Effects (what each faction's ability and stronghold can give);
// a faction with something in such a row always shows it. A row that can apply to nobody here (all dashes) is left
// out, and so are Fire & Ice scoring without a final scoring tile, Landscapes without the expansion, and Adjustments
// when all are 0.
public sealed record StatRow(string Label, IReadOnlyList<int?>? Values)
{
    public bool IsSection => Values is null;
}

public sealed record StatTable(string Name, IReadOnlyList<StatRow> Rows);

public sealed record GameStats(
    IReadOnlyList<Faction> Factions,  // the columns: the first round's turn order
    IReadOnlyList<int> Totals,        // final VP
    IReadOnlyList<int> Places,        // 1 = first; tied factions share a place
    IReadOnlyList<bool> Tied,
    StatTable VictoryPoints,
    StatTable Actions,
    StatTable Resources);

// What each faction's own ability and its stronghold can give, for the statistics' dashes (GameStats).
[Flags]
public enum StatEffects
{
    None = 0,
    VictoryPoints = 1, VictoryPointsLost = 2, Coins = 4, Workers = 8, Priests = 16, Power = 32,
    StrongholdVictoryPoints = 64, StrongholdCoins = 128, StrongholdWorkers = 256, StrongholdPriests = 512, StrongholdPower = 1024,
}

public static class FactionStats
{
    // Unlisted: nothing (Auren, Giants, Chaos Magicians, Nomads, Mermaids, Yetis, Dragonlords, Acolytes, Riverwalkers,
    // Goblins, Children of the Wyrm, Chash Dallah, Conspirators, Snow Shamans, Geologists, Changelings).
    public static StatEffects Effects(Faction faction) => faction switch
    {
        Faction.Witches => StatEffects.VictoryPoints,                  // +5 VP a town
        Faction.Fakirs => StatEffects.VictoryPoints,                   // carpet flights
        Faction.Halflings => StatEffects.VictoryPoints,                // 1 VP a spade
        Faction.Dwarves => StatEffects.VictoryPoints,                  // tunnelling
        Faction.Darklings => StatEffects.VictoryPoints | StatEffects.StrongholdPriests, // priest spades; workers → priests
        Faction.Alchemists => StatEffects.VictoryPoints | StatEffects.VictoryPointsLost | StatEffects.Coins
            | StatEffects.StrongholdPower,                             // VP ↔ coins; 12 power, then 2 a spade
        Faction.Cultists => StatEffects.Power | StatEffects.StrongholdVictoryPoints, // power when declined; 7 VP
        Faction.Engineers => StatEffects.StrongholdVictoryPoints,      // bridges when passing
        Faction.Swarmlings => StatEffects.Workers,                     // 3 workers a town
        Faction.IceMaidens => StatEffects.StrongholdVictoryPoints,     // temples when passing
        Faction.Shapeshifters => StatEffects.Power | StatEffects.VictoryPointsLost,
        Faction.Architects => StatEffects.StrongholdVictoryPoints,     // a bridge moved
        Faction.Treasurers => StatEffects.Coins | StatEffects.Workers | StatEffects.Priests, // the Safe, doubled
        Faction.Djinni => StatEffects.StrongholdVictoryPoints,         // priests on the tracks when passing
        Faction.Archivists => StatEffects.Power,                       // power for the coins on a pass tile
        Faction.TimeTravellers => StatEffects.StrongholdPower,         // tokens into bowl III
        Faction.Prospectors => StatEffects.VictoryPoints | StatEffects.Power | StatEffects.Priests
            | StatEffects.StrongholdCoins,                             // coin spades; a coin per opponent trading post
        Faction.Wisps => StatEffects.StrongholdVictoryPoints,
        Faction.Atlanteans => StatEffects.VictoryPoints,               // the town's power thresholds
        Faction.Enlightened => StatEffects.StrongholdPower,            // 4 power
        Faction.DynionGeifr => StatEffects.StrongholdPriests,          // 2 priests
        Faction.Selkies => StatEffects.VictoryPoints,                  // river dwellings
        Faction.Firewalkers => StatEffects.VictoryPoints | StatEffects.VictoryPointsLost | StatEffects.Power
            | StatEffects.StrongholdVictoryPoints,                     // temple VP; volcano price; pyro; groups
        Faction.KingdomOfEmber => StatEffects.Power,                   // 1 power an upgrade
        _ => StatEffects.None,
    };

    public static bool CanUpgradeSpades(Faction faction) => Factions.All[faction].MaxSpadeUpgrades > 0;

    public static bool CanUpgradeShipping(Faction faction) =>
        Factions.All[faction] is var definition && definition.HasShipping && definition.MaxShipping > definition.StartingShipping;
}

public static class GameStatistics
{
    public static GameStats Compute(IReadOnlyList<LogEntry> played, GameState final)
    {
        var factions = FirstTurnOrder(played, final);
        int n = factions.Count;
        int Column(Faction faction) => factions.IndexOf(faction);
        var sums = new Dictionary<string, int[]>();
        void Add(string key, Faction faction, int amount)
        {
            int c = Column(faction);
            if (c < 0 || amount == 0)
                return;
            if (!sums.TryGetValue(key, out int[]? values))
                sums[key] = values = new int[n];
            values[c] += amount;
        }
        int[] Sum(string key) => sums.TryGetValue(key, out int[]? values) ? values : new int[n];

        // Play begins with the first record into play (its before: the starting VP).
        int start = Enumerable.Range(0, played.Count).FirstOrDefault(i => played[i].Before is GameState b && b.Phase < Phase.OrderingTurns
            && played[i].After.Phase >= Phase.OrderingTurns, -1);
        GameState? startState = start >= 0 ? played[start].Before : null;

        for (var i = 0; i < played.Count; i++)
        {
            LogEntry entry = played[i];
            if (entry.Move is not Move move || entry.Before is not GameState before)
                continue;
            GameState after = entry.After;
            Faction? actor = Actor(move) ?? before.ActivePlayer;
            bool edit = Rules.IsEdit(move) || move is CalculatorGain or TakePassTileCoin;

            // ---- Actions (setup included: starting dwellings, the Ice Maidens' favour) ----
            if (!edit && actor is Faction doer && Rules.IsAction(before, move))
                Add("actions", doer, 1);
            foreach (GameEvent happened in entry.Events)
                switch (happened)
                {
                    case DwellingBuilt built:
                        Add($"built {built.Dwelling.Kind}", built.Dwelling.Owner, 1); // (the Atlanteans' starting stronghold too)
                        break;
                    case BuildingUpgraded upgraded:
                        Add($"built {upgraded.Building.Kind}", upgraded.Building.Owner, 1);
                        break;
                    case LandscapePlaced placed:
                        Add("landscapes", placed.Landscape.Owner, 1);
                        break;
                    case TownFormed town:
                        Add("towns", town.Faction, 1);
                        break;
                    case TerrainTransformed transformed when actor is Faction transformer && after.IsInGame(transformer):
                        Add(after[transformer].Home == transformed.To ? "transform home" : "transform other", transformer, 1);
                        break;
                }
            if (move is TakeFavour favour)
                Add("favours", favour.Player, 1);
            if (move is TakePowerAction powerAction && !edit)
                Add($"power action {powerAction.Action}", powerAction.Player, 1);

            if (i < start || start < 0)
                continue;

            // ---- VPs ----
            if (!edit)
                foreach (VictoryPointsGained gained in entry.Events.OfType<VictoryPointsGained>())
                    Add(PointsRow(gained), gained.Faction, gained.Amount);

            // ---- Power ----
            foreach (GameEvent happened in entry.Events)
                switch (happened)
                {
                    case PowerGained powerGained when powerGained.Source != PowerSource.Calculator:
                        Add($"power {PowerRow(powerGained)}", powerGained.Faction, powerGained.Amount);
                        break;
                    case PowerBurned burned:
                        Add("power Burning", burned.Faction, 1);
                        break;
                }

            // ---- Coins, workers, priests ----
            bool finishing = after.Phase == Phase.Finished && before.Phase != Phase.Finished; // final scoring's conversions
            foreach (Faction faction in factions)
            {
                if (!before.IsInGame(faction) || !after.IsInGame(faction))
                    continue;
                var explained = new int[3];
                void Explain(string row, Income income)
                {
                    Add($"coins {row}", faction, income.Coins);
                    Add($"workers {row}", faction, income.Workers);
                    Add($"priests {row}", faction, income.Priests);
                    explained[0] += income.Coins;
                    explained[1] += income.Workers;
                    explained[2] += income.Priests;
                }
                foreach (GameEvent happened in entry.Events)
                    switch (happened)
                    {
                        case IncomeReceived income when income.Faction == faction:
                            Explain("Income", income.Income);
                            break;
                        case RoundEndRewardReceived reward when reward.Faction == faction:
                            Explain("Round bonus", reward.Income);
                            break;
                        case PassTileTaken tile when tile.Faction == faction:
                            Explain("Pass tiles", new Income(tile.Coins, 0, 0, 0));
                            break;
                        case TownFormed town when town.Faction == faction:
                            Explain("Towns", KeyTiles.All[town.Tile.Id].Income);
                            break;
                        case SafeOpened safe when safe.Faction == faction:
                            Explain("Ability", new Income(safe.Coins, safe.Workers, safe.Priests, 0));
                            break;
                    }
                if (move is TakePowerAction taken && taken.Player == faction && !edit)
                    Explain("Power actions", PowerActions.All[taken.Action].Income);
                if (edit || finishing)
                    continue;
                // What else the move gave.
                PlayerState was = before[faction], now = after[faction];
                int[] change = { now.Coins - was.Coins, now.Workers - was.Workers, now.Priests.Count - was.Priests.Count };
                string row = faction != actor ? "Ability"
                    : move is ConvertPowerToCoin or ConvertPowerToWorker or ConvertPowerToPriest or ConvertPriestToWorker or ConvertWorkerToCoin
                        ? "Conversions"
                    : move is TakePowerAction ? "Ability" // (beyond the action's own: e.g. the Prospectors' spade as a priest)
                    : IsStrongholdMove(move, entry.Events, faction) ? "Stronghold"
                    : "Ability";
                string[] names = { "coins", "workers", "priests" };
                for (var r = 0; r < 3; r++)
                    if (change[r] - explained[r] > 0)
                        Add($"{names[r]} {row}", faction, change[r] - explained[r]);
            }
        }

        // ---- The tables ----
        int?[] Values(int[] values) => values.Select(value => (int?)value).ToArray();
        int?[] Applying(int[] values, Func<Faction, bool> applies) =>
            Enumerable.Range(0, n).Select(c => applies(factions[c]) || values[c] != 0 ? (int?)values[c] : null).ToArray();
        bool Has(Faction faction, StatEffects effect) => FactionStats.Effects(faction).HasFlag(effect);
        var finals = factions.Select(f => final.FinalScores.FirstOrDefault(score => score.Faction == f)).ToList();

        // VPs.
        var vp = new List<StatRow>();
        int[] starting = factions.Select(f => startState is not null && startState.IsInGame(f) ? startState[f].VictoryPoints : 0).ToArray();
        vp.Add(new StatRow("Starting VPs", Values(starting)));
        vp.Add(new StatRow("Gains", null));
        foreach (string label in new[] { "Round bonus", "Favor tiles", "Pass tiles", "Towns" })
            vp.Add(new StatRow(label, Values(Sum(label))));
        if (final.Options.Landscapes)
            vp.Add(new StatRow("Landscapes", Values(Sum("Landscapes"))));
        vp.Add(new StatRow("Faction ability", Applying(Sum("Faction ability"), f => Has(f, StatEffects.VictoryPoints))));
        vp.Add(new StatRow("Faction Stronghold", Applying(Sum("Faction Stronghold"), f => Has(f, StatEffects.StrongholdVictoryPoints))));
        vp.Add(new StatRow("Spade upgrade", Applying(Sum("Spade upgrade"), FactionStats.CanUpgradeSpades)));
        vp.Add(new StatRow("Shipping upgrade", Applying(Sum("Shipping upgrade"), FactionStats.CanUpgradeShipping)));
        vp.Add(new StatRow("Losses", null));
        vp.Add(new StatRow("Paid for power", Values(Sum("Paid for power"))));
        vp.Add(new StatRow("Faction ability", Applying(Sum("Faction ability lost"), f => Has(f, StatEffects.VictoryPointsLost))));
        vp.Add(new StatRow("Endgame", null));
        vp.Add(new StatRow("Area scoring", factions.Select((_, c) => (int?)(finals[c]?.NetworkPoints ?? 0)).ToArray()));
        vp.Add(new StatRow("Cult scoring", factions.Select((_, c) => (int?)(finals[c]?.TrackPoints.Sum() ?? 0)).ToArray()));
        if (final.Options.FinalScoring is not null || finals.Any(score => score?.Tile is not null))
            vp.Add(new StatRow("Fire & Ice scoring", factions.Select((_, c) => (int?)(finals[c]?.TilePoints ?? 0)).ToArray()));
        vp.Add(new StatRow("Resource scoring", factions.Select((_, c) => (int?)(finals[c]?.CoinPoints ?? 0)).ToArray()));
        int[] totals = factions.Select((f, c) => finals[c]?.Total ?? (final.IsInGame(f) ? final[f].VictoryPoints : 0)).ToArray();
        // Whatever the rows don't account for (Setup's and Calculator's edits).
        int[] adjustments = Enumerable.Range(0, n).Select(c => totals[c] - vp.Where(row => !row.IsSection)
            .Sum(row => row.Values![c] ?? 0)).ToArray();
        if (adjustments.Any(value => value != 0))
            vp.Add(new StatRow("Adjustments", Values(adjustments)));
        vp.Add(new StatRow("Total", null));
        vp.Add(new StatRow("Total", Values(totals)));

        // Actions.
        var actions = new List<StatRow>
        {
            new("Number of actions taken", Values(Sum("actions"))),
            new("Dwellings built", Values(Sum("built Dwelling"))),
            new("Trading posts built", Values(Sum("built TradingPost"))),
            new("Temples built", Values(Sum("built Temple"))),
            new("Sanctuary built", Values(Sum("built Sanctuary"))),
            new("Stronghold built", Values(Sum("built Stronghold"))),
        };
        if (final.Options.Landscapes)
            actions.Add(new StatRow("Landscapes built", Values(Sum("landscapes"))));
        actions.Add(new StatRow("Towns founded", Values(Sum("towns"))));
        actions.Add(new StatRow("Favor tiles taken", Values(Sum("favours"))));
        actions.Add(new StatRow("Transforms to home terrain", Values(Sum("transform home"))));
        actions.Add(new StatRow("Transforms to other terrain", Values(Sum("transform other"))));
        string[] powerActions = { "Bridge", "Priest", "Coins", "Workers", "1 Spade", "2 Spades" }; // PowerActions' ids
        foreach (int id in new[] { 0, 1, 3, 2, 4, 5 }) // the sheet's order
            actions.Add(new StatRow($"{powerActions[id]} power actions", Values(Sum($"power action {id}"))));

        // Resources.
        var resources = new List<StatRow>();
        void Section(string title, string key, string[] rows, StatEffects ability, StatEffects stronghold)
        {
            resources.Add(new StatRow(title, null));
            var section = new List<int?[]>();
            foreach (string row in rows)
            {
                int?[] values = row switch
                {
                    "Faction Stronghold" => Applying(Sum($"{key} Stronghold"), f => Has(f, stronghold)),
                    "Faction Ability" => Applying(Sum($"{key} Ability"), f => Has(f, ability)),
                    _ => Values(Sum($"{key} {row}")),
                };
                if (values.All(value => value is null))
                    continue;
                resources.Add(new StatRow(row, values));
                section.Add(values);
            }
            resources.Add(new StatRow("Total", Enumerable.Range(0, n).Select(c => (int?)section.Sum(values => values[c] ?? 0)).ToArray()));
        }
        string[] usual = { "Income", "Power actions", "Towns", "Round bonus", "Conversions", "Faction Stronghold", "Faction Ability" };
        Section("Coins", "coins", new[] { "Income", "Pass tiles", "Power actions", "Towns", "Round bonus", "Conversions", "Faction Stronghold", "Faction Ability" },
            StatEffects.Coins, StatEffects.StrongholdCoins);
        Section("Workers", "workers", usual, StatEffects.Workers, StatEffects.StrongholdWorkers);
        Section("Priests", "priests", usual, StatEffects.Priests, StatEffects.StrongholdPriests);
        Section("Power", "power", new[] { "Income", "Buildings", "Cult tracks", "Towns", "Round bonus", "Burning", "Faction Stronghold", "Faction Ability" },
            StatEffects.Power, StatEffects.StrongholdPower);

        // Rows nobody here can have (all dashes) go.
        List<StatRow> Kept(List<StatRow> rows) => rows.Where(row => row.IsSection || row.Values!.Any(value => value is not null)).ToList();

        var places = totals.Select(total => 1 + totals.Count(other => other > total)).ToList();
        var tied = totals.Select(total => totals.Count(other => other == total) > 1).ToList();
        return new GameStats(factions, totals, places, tied,
            new StatTable("VPs", Kept(vp)), new StatTable("Actions", Kept(actions)), new StatTable("Resources", Kept(resources)));
    }

    // The first round's turn order (the factions still in the game), else the game's.
    private static List<Faction> FirstTurnOrder(IReadOnlyList<LogEntry> played, GameState final)
    {
        LogEntry? first = played.FirstOrDefault(entry => entry.After.Phase == Phase.Playing && entry.After.Round == 1);
        IEnumerable<Faction> order = first?.After.TurnOrder is { Count: > 0 } turnOrder ? turnOrder : final.TurnOrder;
        var result = order.Where(final.IsInGame).Distinct().ToList();
        result.AddRange(final.Players.Select(player => player.Faction).Where(f => !result.Contains(f)));
        return result;
    }

    // The VP row a gain belongs to (its source; negative: a loss).
    private static string PointsRow(VictoryPointsGained gained) => gained.Amount < 0
        ? gained.Source is PointsArea.OnHex ? "Paid for power" : "Faction ability lost"
        : gained.Source switch
        {
            PointsArea.RoundTile => "Round bonus",
            PointsArea.Favour => "Favor tiles",
            PointsArea.PassTile => "Pass tiles",
            PointsArea.KeyOn => "Towns",
            PointsArea.Spades => "Spade upgrade",
            PointsArea.Shipping => "Shipping upgrade",
            PointsArea.OnHex or PointsArea.OnBridge => "Faction Stronghold",
            PointsArea.Landscape => "Landscapes",
            _ => "Faction ability", // the portrait, bowl III, or no source (the Alchemists' trades)
        };

    // The power row a gain belongs to.
    private static string PowerRow(PowerGained power) => power.Source switch
    {
        PowerSource.Income => "Income",
        PowerSource.NeighbouringBuild => "Buildings",
        PowerSource.Track => "Cult tracks",
        PowerSource.Town => "Towns",
        PowerSource.RoundEnd => "Round bonus",
        PowerSource.Stronghold => "Stronghold",
        PowerSource.Spades when power.Faction == Faction.Alchemists => "Stronghold", // 2 a spade, with their stronghold
        _ => "Ability",
    };

    // A stronghold's action (its octagon), the move building the stronghold, or the Darklings' exchange.
    private static bool IsStrongholdMove(Move move, IReadOnlyList<GameEvent> events, Faction faction) =>
        move.GetType().GetProperty("Source")?.GetValue(move) is BonusSource.Stronghold
        || move is ExchangeWorkersForPriests
        || events.OfType<BuildingUpgraded>().Any(built => built.Building.Owner == faction && built.Building.Kind == PieceKind.Stronghold);

    // The faction making the move (its Player, or a piece's owner), if it says.
    private static Faction? Actor(Move move)
    {
        Type type = move.GetType();
        if (type.GetProperty("Player")?.GetValue(move) is Faction player)
            return player;
        foreach (PropertyInfo property in type.GetProperties())
            if (property.GetValue(move) is Piece piece)
                return piece.Owner;
        return null;
    }
}
