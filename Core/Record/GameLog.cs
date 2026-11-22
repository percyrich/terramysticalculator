namespace Core;

// The game log's text: one line for each move, what was done ("Giants build a dwelling at E7"), then what it brought
// about, from its events (points, towns, power offered, spades given up, rounds ending and beginning, the game ending),
// each after " · ". Hexes are named as on the printed board: rows A, B, C, … from the top, land hexes numbered from 1
// along each row (river hexes skipped); river hexes r0, r1, … over the whole map, row by row.
public static class GameLog
{
    // ---------- Parts: what a record says, " · " between them, each with the faction it is about ----------

    // What a part is: the action itself, a power offer from it ("X offered 2pw", tagged "(yes)" or "(no)" once
    // answered: OfferTags), what accepting an offer brought (the power and the VP paid: shown by the tag instead), or
    // anything else.
    public enum PartKind { Action, Offer, OfferGain, Other }

    // One part of a record's text, and the faction it is about (null: nobody in particular, e.g. "round 2 begins").
    public sealed record LogPart(string Text, Faction? Faction, PartKind Kind = PartKind.Other);

    public const string Separator = " · ";

    public static string Plain(IEnumerable<LogPart> parts) => string.Join(Separator, parts.Select(part => part.Text));

    public static string Describe(LogEntry entry) => Plain(Parts(entry));

    public static string Describe(GameState before, Move move, IReadOnlyList<GameEvent> events, GameState after) =>
        Plain(Parts(before, move, events, after));

    public static IReadOnlyList<LogPart> Parts(LogEntry entry) =>
        entry.Move is Move move && entry.Before is GameState before
            ? Parts(before, move, entry.Events, entry.After)
            : new[] { new LogPart(History.SessionStart, null, PartKind.Action) };

    // The action (with what it paid), then what it brought about (Results), resource amounts written short. Power
    // offers answered in the same record (taken without asking) are tagged at once.
    public static IReadOnlyList<LogPart> Parts(GameState before, Move move, IReadOnlyList<GameEvent> events, GameState after)
    {
        string text = Action(before, move, events, after) + Paid(before, move, events, after);
        if (before.IsPlanning && !before.Options.Calculator && move is not SetMode) // Calculator: always Free
            text += " (Free)";
        var parts = new List<LogPart> { new(Abbreviated(text), PartFaction(move), PartKind.Action) };
        parts.AddRange(Results(move, Actor(move), events).Select(part => part with { Text = Abbreviated(part.Text) }));
        return OfferTags(parts, events);
    }

    // The faction a record's action is about.
    private static Faction? PartFaction(Move move) => move switch
    {
        EditBuilding m => m.Building.Owner,
        EditReturnPriest m => m.Priest.Owner,
        EditPlacePriest m => m.Priest.Owner,
        _ => Actor(move),
    };

    // Each offer part tagged with its answer among these events, in order: "(yes)", "(yes, −1 VP)" or "(no)"; offers
    // not answered yet stay as they are. What an accepted offer brought (OfferGain parts of its faction) is left out:
    // the tag says it. extra: parts added at the end (a run's later records' results).
    private static List<LogPart> OfferTags(IReadOnlyList<LogPart> parts, IEnumerable<GameEvent> events, IEnumerable<LogPart>? extra = null)
    {
        var answers = events.OfType<PowerOfferAnswered>().ToList();
        var tagged = new HashSet<Faction>();
        var result = new List<LogPart>();
        foreach (LogPart part in parts.Concat(extra ?? Enumerable.Empty<LogPart>()))
        {
            if (part.Kind == PartKind.Offer && part.Faction is Faction offered
                && answers.FirstOrDefault(a => a.Faction == offered) is PowerOfferAnswered answer)
            {
                answers.Remove(answer);
                if (answer.Accepted)
                    tagged.Add(offered);
                string tag = !answer.Accepted ? "no" : answer.VictoryPointsPaid > 0 ? $"yes, −{answer.VictoryPointsPaid} VP" : "yes";
                result.Add(part with { Text = $"{part.Text} ({tag})", Kind = PartKind.Other }); // (tagged once only)
            }
            else
                result.Add(part);
        }
        return result.Where(part => !(part.Kind == PartKind.OfferGain && part.Faction is Faction f && tagged.Contains(f))).ToList();
    }

    // Resource amounts written as the conversions and payments are: "3 coins, 1 worker" → "3c 1w", "4 power" → "4pw",
    // "2 priests" → "2p" (tile descriptions, incomes, Safes and gains alike). Power tokens and VP stay in words.
    private static readonly System.Text.RegularExpressions.Regex AmountPattern =
        new(@"\b(\d+) (?:(coin|worker|priest)s?\b|power\b(?! token))", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex JoinedPattern =
        new(@"(\d+(?:pw|[cwp])), (?=-?\d+(?:pw|[cwp])\b)", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Abbreviated(string text) => JoinedPattern.Replace(
        AmountPattern.Replace(text, m => m.Groups[2].Success ? $"{m.Groups[1].Value}{m.Groups[2].Value[0]}" : $"{m.Groups[1].Value}pw"),
        "$1 ");

    // ---------- Lines: conversions and power offers run together ----------

    // One line of the log as shown: its parts and the last record it covers. Among the records played:
    //   a run of one faction's conversions (IsConversion) becomes one line, rewritten as the run grows ("Alchemists
    //     burn 2pw. Alchemists do conversions: 8pw → 1p 1w");
    //   a record that offers power, with the answers right after it (AcceptPower, DeclinePower), becomes one line: the
    //     record's own, each offer tagged with its answer ("Engineers offered 1pw (yes) · Swarmlings offered 2pw (no)"),
    //     then whatever else the answers brought about (e.g. the Cultists' step).
    // A bookmark ends a run. Records not yet played (the redo line) keep a line each. describe: each record's own parts
    // (the caller may cache them).
    public sealed record LogLine(int Index, IReadOnlyList<LogPart> Parts)
    {
        public string Text => Plain(Parts);
    }

    public static List<LogLine> Lines(IReadOnlyList<LogEntry> entries, int played, Func<LogEntry, IReadOnlyList<LogPart>> describe)
    {
        var lines = new List<LogLine>();
        for (var i = 0; i < entries.Count; i++)
        {
            int last = RunEnd(entries, i, i < played ? played : i + 1, next => true);
            lines.Add(new LogLine(last, RunParts(entries, i, last, i < played, describe)));
            i = last;
        }
        return lines;
    }

    // The last record of the run starting at `first` (itself if none), going no further than `limit` (exclusive);
    // joins: whether the next record may be part of it at all (the notes: along the same line).
    private static int RunEnd(IReadOnlyList<LogEntry> entries, int first, int limit, Func<int, bool> joins)
    {
        int last = first;
        bool More(int next) => next < limit && entries[next - 1].Bookmark is null && joins(next)
            && ReferenceEquals(entries[next].Before, entries[next - 1].After);
        if (IsConversion(entries[first]) is Faction actor)
            while (More(last + 1) && IsConversion(entries[last + 1]) == actor)
                last++;
        else if (entries[first].Events.Any(e => e is PowerOffered))
            while (More(last + 1) && entries[last + 1].Move is AcceptPower or DeclinePower)
                last++;
        return last;
    }

    // A run's parts: conversions as one; a record with its offers' answers; otherwise the record's own. played: a
    // conversion on its own is written as a run (as it would be once followed by another), not when only to be redone.
    private static IReadOnlyList<LogPart> RunParts(IReadOnlyList<LogEntry> entries, int first, int last, bool played,
        Func<LogEntry, IReadOnlyList<LogPart>> describe)
    {
        if (IsConversion(entries[first]) is Faction actor && (played || last > first))
            return new[] { new LogPart(Conversions(entries, first, last), actor, PartKind.Action) };
        if (last == first)
            return describe(entries[first]);
        // The answers' own action ("X take 1pw for 0 VP") left out: the tags say it.
        IEnumerable<LogPart> later = Enumerable.Range(first + 1, last - first).SelectMany(i => describe(entries[i]).Skip(1));
        IEnumerable<GameEvent> events = Enumerable.Range(first, last - first + 1).SelectMany(i => entries[i].Events);
        return OfferTags(describe(entries[first]), events, later);
    }

    // ---------- The notes: conversions and power offers run together along a line ----------

    // One row of the notes window: its parts, how far in, whether it opens a line ("(Alternate)"), and the last record
    // it covers (an index into the notes). Runs as in Lines, along one line only; a bookmark, the current position
    // (`current`) or a parting of lines ends one.
    public sealed record NoteLine(int Index, IReadOnlyList<LogPart> Parts, int Depth, bool Opens)
    {
        public string Text => Plain(Parts);
    }

    public static List<NoteLine> NoteLines(IReadOnlyList<NoteEntry> notes, int current, Func<LogEntry, IReadOnlyList<LogPart>> describe)
    {
        var entries = notes.Select(n => n.Entry).ToList();
        var lines = new List<NoteLine>();
        for (var i = 0; i < notes.Count; i++)
        {
            int first = i;
            int last = RunEnd(entries, i, notes.Count, next => entries[next - 1].Position != current
                && !notes[next].Opens && notes[next].Depth == notes[first].Depth);
            lines.Add(new NoteLine(last, RunParts(entries, i, last, played: true, describe), notes[i].Depth, notes[i].Opens));
            i = last;
        }
        return lines;
    }

    // The faction converting, if this record is a conversion (a burn; power to a coin, worker or priest; a priest to a
    // worker; a worker to a coin).
    private static Faction? IsConversion(LogEntry entry) => entry.Move switch
    {
        BurnPower m => m.Player,
        ConvertPowerToCoin m => m.Player,
        ConvertPowerToWorker m => m.Player,
        ConvertPowerToPriest m => m.Player,
        ConvertPriestToWorker m => m.Player,
        ConvertWorkerToCoin m => m.Player,
        _ => null,
    };

    // The run's line: the burns, then what the conversions cost and brought, from before the first to after the last.
    private static string Conversions(IReadOnlyList<LogEntry> entries, int first, int last)
    {
        var faction = IsConversion(entries[first])!.Value;
        GameState before = entries[first].Before!, after = entries[last].After;
        int burns = Enumerable.Range(first, last - first + 1)
            .Sum(i => entries[i].Events.OfType<PowerBurned>().Count(e => e.Faction == faction));
        PlayerState was = before[faction], now = after[faction];
        var costs = new List<string>();
        var gains = new List<string>();
        void Part(int change, string unit)
        {
            if (change < 0)
                costs.Add($"{-change}{unit}");
            else if (change > 0)
                gains.Add($"{change}{unit}");
        }
        Part(now.Priests.Count - was.Priests.Count, "p");
        Part(now.Workers - was.Workers, "w");
        Part(now.Coins - was.Coins, "c");
        int spent = Math.Max(0, now.Power.Bowl1 - was.Power.Bowl1); // conversions move tokens from bowl III to bowl I
        if (spent > 0)
            costs.Add($"{spent}pw");
        var parts = new List<string>();
        if (burns > 0)
            parts.Add($"{Name(faction)} burn {burns}pw");
        if (costs.Count + gains.Count > 0)
            parts.Add($"{Name(faction)} do conversions: {string.Join(" ", costs)} → {string.Join(" ", gains)}");
        return parts.Count == 0 ? $"{Name(faction)} convert nothing" : string.Join(". ", parts);
    }

    // ---------- What an action paid ----------

    // What the actor paid for an action, e.g. " (2w 3c)": priests, workers, coins and power spent (p, w, c, pw), from
    // before and after (power spent: tokens arriving in bowl I); power tokens removed for good written out. Empty if
    // nothing, and for moves that aren't actions paid for (conversions have lines of their own: Lines).
    private static string Paid(GameState before, Move move, IReadOnlyList<GameEvent> events, GameState after)
    {
        if (move is not (BuildDwelling or UpgradeBuilding or TransformTerrain or TakePowerAction or BuildBridge or UpgradeShipping
                or UpgradeSpades or BonusShapeshift or AdvanceIncomeTrack or BonusSelkiesBuild or BuildOnSpadedHex or SpendSpades
                or PlaceLandscape or BuyPowerToken or AdvancePyro)
            || Actor(move) is not Faction actor || !before.IsInGame(actor) || !after.IsInGame(actor))
            return "";
        PlayerState was = before[actor], now = after[actor];
        var parts = new List<string>();
        void Spent(int amount, string unit)
        {
            if (amount > 0)
                parts.Add($"{amount}{unit}");
        }
        Spent(was.Priests.Count - now.Priests.Count, "p");
        Spent(was.Workers - now.Workers, "w");
        Spent(was.Coins - now.Coins, "c");
        Spent(now.Power.Bowl1 - was.Power.Bowl1, "pw");
        int removed = events.OfType<PowerTokensRemoved>().Where(e => e.Faction == actor).Sum(e => e.Count);
        if (removed > 0)
            parts.Add($"{removed} power token{(removed == 1 ? "" : "s")} removed");
        return parts.Count == 0 ? "" : $" ({string.Join(" ", parts)})";
    }

    // A hex's name on the board (see above).
    public static string HexName(GameState state, int hex)
    {
        if (!state.Layout.Contains(hex))
            return $"hex {hex}";
        if (state.HexTerrain[hex] == Terrain.River)
            return $"r{Enumerable.Range(0, hex).Count(h => state.HexTerrain[h] == Terrain.River)}";
        var (row, column) = state.Layout.Position(hex);
        int first = state.Layout.IndexOf(row, 0);
        int number = Enumerable.Range(first, column).Count(h => state.HexTerrain[h] != Terrain.River) + 1;
        return $"{(char)('A' + row)}{number}";
    }

    // ---------- What was done ----------

    private static string Action(GameState s, Move move, IReadOnlyList<GameEvent> events, GameState after)
    {
        string Hex(int hex) => HexName(s, hex);
        T? First<T>() where T : GameEvent => events.OfType<T>().FirstOrDefault();
        return move switch
        {
            // Setting up
            ChooseGame m => $"Terra Mystica: {Options(m.Options ?? GameOptions.All)}",
            AddPlayer m => $"{Name(m.Faction)} join the game",
            RemovePlayer m => $"{Name(m.Faction)} leave the game",
            ChooseStartingTerrain m => $"{Name(m.Player)} start on {m.Terrain}",
            ConfirmFactions => $"Factions: {string.Join(", ", after.Players.Select(p => Name(p.Faction)))}",
            ChooseTurnOrder m => $"{Name(m.Faction)} take place {First<TurnOrderChosen>()?.Position} in the turn order",
            UnchooseTurnOrder m => $"{Name(m.Faction)} taken out of the turn order",
            AdjustStartingVictoryPoints m => $"{Name(m.Player)} start with {Rules.StartingVictoryPoints(after[m.Player])} VP",
            ConfirmTurnOrder => $"Turn order: {string.Join(", ", after.TurnOrder.Select(f => Name(f)))}",
            ChooseMap m => $"Map: {Maps.Name(m.Map)}",
            ChooseCustomMap => $"Map: custom ({(after.Layout.RowLengths[0] == 12 ? "12" : "13")}-top)",
            ChooseRoundTile m => $"Round {First<RoundTileChosen>()?.Round}: {Short(RoundTiles.Describe(m.Tile))}",
            UnchooseRoundTile m => $"Round tile taken back ({Short(RoundTiles.Describe(m.Tile))})",
            ConfirmRoundTiles => "Round tiles confirmed",
            AddPassTile m => $"Pass tile added ({Short(PassTiles.Describe(m.Tile))})",
            RemovePassTile m => $"Pass tile taken out ({Short(PassTiles.Describe(m.Tile))})",
            StartGame => "Game started",
            PlaceStartingDwelling m => $"{Name(m.Dwelling.Owner)} place a starting dwelling at {Hex(m.Hex)}",
            PickPassTile m => $"{Name(m.Player)} pick a pass tile ({Short(PassTiles.Describe(m.Tile))}){Coins(First<PassTileTaken>()?.Coins)}",

            // Actions
            BuildDwelling m => $"{Name(m.Dwelling.Owner)} build a dwelling at {Hex(m.Hex)}{Transformed(events)}",
            UpgradeBuilding m => $"{Name(m.Building.Owner)} upgrade the {Noun(First<BuildingReturned>()?.Building.Kind)} at {Hex(m.Hex)} to a {Noun(First<BuildingUpgraded>()?.Building.Kind ?? m.Building.Kind)}",
            TransformTerrain m => First<TerrainTransformed>() is TerrainTransformed t
                ? $"{Name(m.Player)} transform {Hex(m.Hex)} from {t.From} to {t.To}"
                : $"{Name(m.Player)} transform {Hex(m.Hex)} to {m.To}",
            PlacePriest m => First<PriestPlaced>() is PriestPlaced p
                ? $"{Name(m.Priest.Owner)} send a priest to {m.Track} (+{p.StepsAfter - p.StepsBefore})"
                : $"{Name(m.Priest.Owner)} send a priest to {m.Track}",
            Pass m => $"{Name(m.Player)} pass{(m.PassTile is int tile ? $", taking a pass tile ({Short(PassTiles.Describe(tile))}){Coins(First<PassTileTaken>()?.Coins)}" : m.KeepTile ? ", keeping their pass tile" : First<PassTileReturned>() is not null ? ", handing back their pass tile" : "")}",
            BonusCultStep m => $"{Name(m.Player)} take a cult step on {m.Track} ({Source(m.Source)})",
            BonusSpades m => $"{Name(m.Player)} take {Spades(events.OfType<SpadesGained>().Sum(g => g.Count))} ({Source(m.Source)})",
            BonusBuild m => $"{Name(m.Player)} build a dwelling at {Hex(m.Hex)} for free ({Source(m.Source)})",
            BonusTradingPost m => $"{Name(m.Player)} build a trading post at {Hex(m.Hex)} for free ({Source(m.Source)})",
            BonusDoubleAction m => $"{Name(m.Player)} begin a double action ({Source(m.Source)})",
            BonusSandstorm m => $"{Name(m.Player)} sandstorm {Hex(m.Hex)} ({Source(m.Source)})",
            BonusShapeshift m => $"{Name(m.Player)} move their home to {m.To}, {(m.PayWithTokens ? $"removing {Config.ShapeshiftCost} power tokens" : $"spending {Config.ShapeshiftCost} power")}",
            BuildBridge m => $"{Name(m.Player)} build a bridge (book action)",
            UpgradeShipping m => First<CarpetRangeIncreased>() is CarpetRangeIncreased c
                ? $"{Name(m.Player)} raise their carpet range to {c.Range}"
                : $"{Name(m.Player)} upgrade shipping to {First<ShippingUpgraded>()?.Level}{NoPriest(s, m.Player, LandscapeOffer.Shipping)}",
            UpgradeSpades m => $"{Name(m.Player)} upgrade their spades{NoPriest(s, m.Player, LandscapeOffer.Spades)}",
            TakePowerAction m => $"{Name(m.Player)} take a power action ({PowerActions.Describe(m.Action)}){(m.PayWithCoins ? ", paying in coins" : "")}",
            SwapTracks m => $"{Name(m.Player)} swap their {m.A} and {m.B} steps (book action)",
            SpendTreasure m => $"{Name(m.Player)} spend a treasure on their {Noun(m.Kind)}s (book action)",
            AdvanceIncomeTrack m => $"{Name(m.Player)} advance their income track to {First<IncomeTrackAdvanced>()?.Step} (book action)",
            BonusMoveBridge m => $"{Name(m.Player)} move a bridge from {Hex(m.FromA)}–{Hex(m.FromB)} to {Hex(m.ToA)}–{Hex(m.ToB)} ({Source(m.Source)})",
            BonusTokensToBowlThree m => $"{Name(m.Player)} move {Plural(m.Count, "power token")} from bowl 1 to bowl 3 ({Source(m.Source)})",
            BonusCoinsForTradingPosts m => $"{Name(m.Player)} take {Plural(First<CoinGained>() is CoinGained c ? c.NewCoins - s[m.Player].Coins : 0, "coin")} for opponents' trading posts ({Source(m.Source)})",
            BonusFourPower m => $"{Name(m.Player)} gain {Config.EnlightenedStrongholdPower} power ({Source(m.Source)})",
            BonusSwapFavour m => $"{Name(m.Player)} swap a favour ({Short(Favours.Describe(m.Return))}) for another ({Short(Favours.Describe(m.Take))}) ({Source(m.Source)})",

            // During a turn
            PlaceBridge m => $"{Name(m.Player)} place a bridge between {Hex(m.HexA)} and {Hex(m.HexB)}",
            SkipBridges m => $"{Name(m.Player)} leave {Plural(First<BridgesSkipped>()?.Count ?? 0, "bridge")} unplaced",
            FormTown m => $"{Name(m.Player)} form a town at {Hex(m.Hex)}{(First<TownFormed>() is TownFormed town ? $" ({Short(KeyTiles.Describe(town.Tile.Id))})" : "")}",
            ChooseTownBonus m => m.Track is Track witches
                ? $"{Name(m.Player)} take {Landscapes.WitchesTownSteps} steps on {witches} for the town ({Landscapes.Name(m.Player)})"
                : $"{Name(m.Player)} take {Config.WitchesTownVictoryPoints} VP for the town",
            AcceptPower m => First<PowerOfferAnswered>() is PowerOfferAnswered a
                ? $"{Name(m.Player)} take {a.Gained} power for {a.VictoryPointsPaid} VP"
                : $"{Name(m.Player)} take the power",
            DeclinePower m => $"{Name(m.Player)} decline the power",
            SpendSpades m => First<SpadesSpent>() is SpadesSpent spent
                ? $"{Name(m.Player)} use {Spades(spent.FreeSpades + spent.PaidSpades)} on {Hex(m.Hex)}: {spent.From} to {spent.To}"
                : $"{Name(m.Player)} use spades on {Hex(m.Hex)}",
            BuildOnSpadedHex m => $"{Name(m.Dwelling.Owner)} build a dwelling at {Hex(m.Hex)}",
            FinishSpades m => First<SpadesForfeited>() is { Count: > 0 } lost
                ? $"{Name(m.Player)} give up {Spades(lost.Count)}"
                : $"{Name(m.Player)} build nothing there",
            ConvertPowerToCoin m => $"{Name(m.Player)} convert 1pw → 1c{Burned(events)}",
            ConvertPowerToWorker m => $"{Name(m.Player)} convert 3pw → 1w{Burned(events)}",
            ConvertPowerToPriest m => $"{Name(m.Player)} convert 5pw → 1p{Burned(events)}",
            ConvertPriestToWorker m => $"{Name(m.Player)} convert 1p → 1w",
            ConvertWorkerToCoin m => $"{Name(m.Player)} convert 1w → 1c",
            BurnPower m => $"{Name(m.Player)} burn 1 power",
            ConvertVictoryPointToCoin m => $"{Name(m.Player)} sell 1 VP for a coin",
            ConvertCoinsToVictoryPoint m => $"{Name(m.Player)} buy 1 VP with coins",
            TakeCultStep m => $"{Name(m.Player)} take a cult step on {m.Track}",
            DeclineCultStep m => $"{Name(m.Player)} give up their cult step",
            TakeHeldPower m => $"{Name(m.Player)} take their stronghold's power",
            TakeTopStep m => $"{Name(m.Player)} use a key to reach the top of {m.Track}",
            ExchangeWorkersForPriests m => $"{Name(m.Player)} exchange {Plural(m.Workers, "worker")} for priests",
            FinishPriestExchange m => $"{Name(m.Player)} stop exchanging workers for priests",
            PayCultSteps m => First<TrackDescended>() is TrackDescended d
                ? $"{Name(m.Player)} pay {Plural(d.StepsBefore - d.StepsAfter, "step")} on {m.Track}"
                : $"{Name(m.Player)} pay steps on {m.Track}",
            AnswerTokenOffer m => m.Accept ? $"{Name(m.Player)} pay 1 VP for a new power token" : $"{Name(m.Player)} decline the power token",
            ChoosePriest m => m.Unlock is Terrain unlocked ? $"{Name(m.Player)} unlock {unlocked}" : $"{Name(m.Player)} take the priest",
            PlaceLandscape m => $"{Name(m.Player)} place their {Landscapes.Name(m.Player)} at {Hex(m.Hex)}{Transformed(events)}",
            DeclineLandscapeBonus m => $"{Name(m.Player)} decline what their {Landscapes.Name(m.Player)} offers",
            LandscapeShapeshift m => $"{Name(m.Player)} move their home to {m.To} for free ({Landscapes.Name(m.Player)})",
            TakeCoinInstead m => $"{Name(m.Player)} take 1 coin instead of the power ({Landscapes.Name(m.Player)})",
            TakeFavour m => $"{Name(m.Player)} take a favour ({Short(Favours.Describe(m.Favour))})",
            BuyPowerToken m => $"{Name(m.Player)} buy a power token for {Plural(Config.EnlightenedTokenCoins, "coin")}",
            StoreInTreasury m => m.Coins + m.Workers + m.Priests == 0 ? $"{Name(m.Player)} keep everything out of their Safe"
                : $"{Name(m.Player)} put {Stored(m.Coins, m.Workers, m.Priests)} in their Safe",
            ChooseForSafe m => $"{Name(m.Player)} choose {Stored(m.Coins, m.Workers, m.Priests)} for their Safe",
            SkipFreeDwelling m => $"{Name(m.Player)} give up their free dwelling{(m.Player == Faction.SnowShamans ? "s" : "")}",
            TakeStartingSteps m => $"{Name(m.Player)} take their starting steps on {m.Track}",
            PlaceFreeDwelling m => $"{Name(m.Dwelling.Owner)} place a free dwelling at {Hex(m.Hex)}",
            ChooseChangelingTerrains m => $"{Name(m.Player)} choose {m.B} (B) and {m.C} (C)",
            PlaceLava m => $"{Name(m.Player)} put lava under {Hex(m.Hex)}",
            LetLavaFlow m => $"{Name(m.Player)} let the lava flow from {Hex(m.From)} to {Hex(m.Path[^1])}{(m.BridgeA is int a && m.BridgeB is int b ? $", bridging {Hex(a)} and {Hex(b)}" : "")}",
            AdvancePyro m => $"{Name(m.Player)} move their pyro token forward for 1 power",
            MoveRock m => First<RockMoved>() is RockMoved r
                ? $"{Name(m.Player)} {(r.Placed ? "cover" : "uncover")} {m.Terrain} with a glowing rock"
                : $"{Name(m.Player)} move a glowing rock on {m.Terrain}",
            BonusMoveRock m => First<RockMoved>() is RockMoved r
                ? $"{Name(m.Player)} {(r.Placed ? "cover" : "uncover")} {m.Terrain} with a glowing rock ({Source(m.Source)})"
                : $"{Name(m.Player)} move a glowing rock on {m.Terrain} ({Source(m.Source)})",
            BonusRiverTokens { HexA: null } m => $"{Name(m.Player)} use their stronghold action for nothing",
            BonusRiverTokens m => $"{Name(m.Player)} put {(m.HexB is null ? "a power token" : "power tokens")} on {Hex(m.HexA!.Value)}{(m.HexB is int second ? $" and {Hex(second)}" : "")} ({Source(m.Source)})",
            BonusSelkiesBuild m => $"{Name(m.Dwelling.Owner)} build a dwelling on {Hex(m.Hex)} with shipping +1 and a free spade ({Source(m.Source)})",
            EditLava m => $"Lava added under {Hex(m.Hex)}",
            EditWyrmToken m => $"{Name(m.Player)}: a power token from bowl {m.FromBowl} onto {Hex(m.Hex)}",
            EditRock m => $"{Name(m.Player)}: rock moved on {m.Terrain}",
            EndTurn m => $"{Name(m.Player)} end their turn",

            // Modes, Setup and Free
            SetMode m => $"{Rules.ModeName(m.Mode)} mode",
            SelectFaction m => $"{Name(m.Faction)} selected",
            CancelPriestDebt m => $"{Name(m.Player)} priest debt {s[m.Player].PriestDebt} → {after[m.Player].PriestDebt}",
            EditResource m => $"{Name(m.Player)} {ResourceNoun(m.Resource)} {Value(s[m.Player], m.Resource)} → {Value(after[m.Player], m.Resource)}",
            EditPowerToken m => $"{Name(m.Player)} power {s[m.Player].Power} → {after[m.Player].Power}",
            EditGainPriest m => $"{Name(m.Player)} priests in hand {s[m.Player].Priests.Count} → {after[m.Player].Priests.Count}",
            EditSafe m => $"{Name(m.Player)} Safe: {Stored(s[m.Player].SafeCoins, s[m.Player].SafeWorkers, s[m.Player].SafePriestList.Count)} → {Stored(after[m.Player].SafeCoins, after[m.Player].SafeWorkers, after[m.Player].SafePriestList.Count)}",
            EditReturnPriest m => $"{Name(m.Priest.Owner)} return a priest to their supply",
            EditPlacePriest m => $"{Name(m.Priest.Owner)} priest on {m.Track} (+{after[m.Priest.Owner].TrackSteps[(int)m.Track] - s[m.Priest.Owner].TrackSteps[(int)m.Track]})",
            EditTrack m => $"{Name(m.Player)} {m.Track} {s[m.Player].TrackSteps[(int)m.Track]} → {after[m.Player].TrackSteps[(int)m.Track]}",
            EditFavour m => m.Take
                ? $"{Name(m.Player)} take a favour ({Short(Favours.Describe(m.Favour))})"
                : $"{Name(m.Player)} return a favour ({Short(Favours.Describe(m.Favour))})",
            EditLevel m => m.Level == Level.Shipping
                ? $"{Name(m.Player)} shipping {s[m.Player].Shipping} → {after[m.Player].Shipping}"
                : $"{Name(m.Player)} spade upgrades {s[m.Player].SpadeUpgrades} → {after[m.Player].SpadeUpgrades}",
            EditBridge m => m.Place
                ? $"{Name(m.Player)} bridge between {Hex(m.HexA)} and {Hex(m.HexB)}"
                : $"{Name(m.Player)} bridge between {Hex(m.HexA)} and {Hex(m.HexB)} taken away",
            EditBuilding m => Placed(s, m, Hex),
            EditTerrain m => $"{Hex(m.Hex)}: {s.HexTerrain[m.Hex]} to {m.To}",
            EditPlaceKey m => $"{Name(m.Player)} key at {Hex(m.Hex)} ({Short(KeyTiles.Describe(m.Token))})",
            EditRemoveKey m => $"Key at {Hex(m.Hex)} back to the bank",
            SetRound m => $"Current round: {m.Round}",
            EditPassTile m => $"{Name(m.Player)} pass tile ({Short(PassTiles.Describe(m.Tile))})",
            EditPassTileCoins m => $"Coins on a pass tile {s.PassTileCoins[m.Tile]} → {after.PassTileCoins[m.Tile]} ({Short(PassTiles.Describe(m.Tile))})",
            MarkPassed m => m.Passed ? $"{Name(m.Player)} marked passed" : $"{Name(m.Player)} marked still in the round",
            TogglePowerAction m => $"Power action marked {(after.PowerActionsTaken.Contains(m.Action) ? "taken" : "not taken")} ({PowerActions.Describe(m.Action)})",
            ToggleBonusAction m => $"{Name(m.Player)} bonus action marked {(after[m.Player].BonusActionsUsed.Contains(m.Bonus) ? "used" : "not used")}",
            EditWheel m => after[m.Player].WheelPriests.Any(locked => locked.Terrain == m.Terrain)
                ? $"{Name(m.Player)} lock {m.Terrain}"
                : $"{Name(m.Player)} unlock {m.Terrain}",
            EditHome m => $"{Name(m.Player)} home: {m.Terrain}",
            EditLandscape m => m.Hex is int on
                ? $"{Name(m.Player)} {Landscapes.Name(m.Player)} at {Hex(on)}"
                : $"{Name(m.Player)} {Landscapes.Name(m.Player)} taken off the map",
            FinishSetup => "Setup done",
            PlaceNeutralDwelling m => $"Neutral dwelling at {Hex(m.Hex)}",
            TakePassTileCoin m => $"Coins on pass tile {m.Tile + 1}: {s.PassTileCoins[m.Tile]} → {after.PassTileCoins[m.Tile]}",
            RemoveNeutralDwelling m => $"Neutral dwelling at {Hex(m.Hex)} taken away",
            PlaceNeutralBridge m => $"Neutral bridge between {Hex(m.HexA)} and {Hex(m.HexB)}",
            RemoveNeutralBridge m => $"Neutral bridge between {Hex(m.HexA)} and {Hex(m.HexB)} taken away",
            PlaceNeutralPriest m => $"Neutral priest on {m.Track}",
            RemoveNeutralPriest m => $"Neutral priest on {m.Track} taken away",
            CalculatorGain { Kind: GainKind.Coin } m => $"{Name(m.Player)} given coins: {s[m.Player].Coins} → {after[m.Player].Coins}",
            CalculatorGain { Kind: GainKind.Worker } m => $"{Name(m.Player)} given workers: {s[m.Player].Workers} → {after[m.Player].Workers}",
            CalculatorGain { Kind: GainKind.Priest } m => $"{Name(m.Player)} given a priest: priests in hand {s[m.Player].Priests.Count} → {after[m.Player].Priests.Count}",
            CalculatorGain m => $"{Name(m.Player)} given power: {s[m.Player].Power} → {after[m.Player].Power}",
            _ => move.GetType().Name,
        };
    }

    // ---------- What it brought about ----------

    // Everything after the action itself, in the order it happened: points (one total per faction and source, at the
    // first of them), resources, power and steps gained along the way, towns, power offered, keys, favours earned,
    // income and cult rewards, rounds ending and beginning, the game ending. What the action's own text already says
    // (its own cost or gain) is left out, as are the separate coins, power and priests that income and cult rewards
    // are made of.
    private static List<LogPart> Results(Move move, Faction? actor, IReadOnlyList<GameEvent> events)
    {
        var results = new List<LogPart?>();
        var points = new Dictionary<(Faction Faction, string? Source), int>(); // index into results
        var pointTotals = new Dictionary<(Faction, string?), int>();
        bool skipActorPoints = move is AcceptPower or AnswerTokenOffer or ConvertCoinsToVictoryPoint or ConvertVictoryPointToCoin;
        var paid = new HashSet<Faction>(events.Select(e => e switch
        {
            IncomeReceived income => income.Faction,
            RoundEndRewardReceived reward => reward.Faction,
            _ => (Faction?)null,
        }).OfType<Faction>());
        var spadeRewards = new HashSet<Faction>(events.OfType<RoundEndRewardReceived>().Where(r => r.Spades > 0).Select(r => r.Faction));
        Track? ownTrack = move switch
        {
            ChooseTownBonus m => m.Track,
            PlacePriest m => m.Track,
            BonusCultStep m => m.Track,
            TakeCultStep m => m.Track,
            TakeTopStep m => m.Track,
            _ => null,
        };

        foreach (GameEvent e in events)
        {
            string? text = e switch
            {
                VictoryPointsGained => null, // below
                PowerGained g when g.Source is PowerSource.Income or PowerSource.RoundEnd => null,
                PowerGained g when move is AcceptPower && g.Faction == actor => null,
                PowerGained g => $"{Name(g.Faction)} gain {g.Amount} power",
                PriestGained g when paid.Contains(g.Priest.Owner) => null,
                PriestGained g when g.Priest.Owner == actor && move is ConvertPowerToPriest or ExchangeWorkersForPriests or ChoosePriest => null,
                PriestGained g => $"{Name(g.Priest.Owner)} gain a priest",
                SpadesGained g when spadeRewards.Contains(g.Faction) => null,
                SpadesGained g when g.Faction == actor && move is BonusSpades or TakePowerAction => null,
                SpadesGained g => $"{Name(g.Faction)} gain {Spades(g.Count)}",
                TrackAdvanced t when t.Faction == actor && t.Track == ownTrack => null,
                TrackAdvanced t => $"{Name(t.Faction)} +{t.StepsAfter - t.StepsBefore} on {t.Track}",
                KeySpent k => $"{Name(k.Faction)} use a key to reach the top of {k.Track}",
                KeyReturned k => $"{Name(k.Faction)} get their key back from {k.Track}",
                FavoursEarned f => $"{Name(f.Faction)} earn {(f.Count == 1 ? "a favour" : $"{f.Count} favours")}",
                CultStepOwed c => $"{Name(c.Faction)} may take a cult step",
                PowerTokensAdded when move is AnswerTokenOffer => null,
                PowerTokensAdded a => $"{Name(a.Faction)} add {Plural(a.Count, "power token")} to bowl {a.Bowl}",
                PowerTokensRemoved when move is BonusShapeshift => null,
                PowerTokensRemoved r => $"{Name(r.Faction)} remove {Plural(r.Count, "power token")}",
                IncomeReceived i => $"{Name(i.Faction)} income: {i.Income}",
                RoundEndRewardReceived r => $"{Name(r.Faction)} cult reward: {Reward(r.Income, r.Spades)}",
                TownFormed town when move is not FormTown => $"{Name(town.Faction)} form a town",
                PowerOffered offer => $"{Name(offer.Faction)} offered {offer.Power} power",
                SpadesForfeited lost when lost.Count > 0 && move is not FinishSpades => $"{Name(lost.Faction)} lose {Spades(lost.Count)}",
                PriestDebtIncurred d => $"{Name(d.Faction)} owe a priest",
                LandscapeBonusOffered o => Offered(o.Faction, o.Offer),
                TownBonusOwed t => $"{Name(t.Faction)} choose 5 VP or 3 cult steps",
                LandscapePlaced p when move is not PlaceLandscape and not EditLandscape => $"{Name(p.Landscape.Owner)} place their {Landscapes.Name(p.Landscape.Owner)}",
                PriestDebtPaid when move is CancelPriestDebt => null,
                PriestDebtPaid d => $"{Name(d.Faction)} pay back a priest owed",
                KeyRemoved k => $"{Name(k.Faction)} lose a key",
                TreasureGained t => $"{Name(t.Faction)} gain a treasure",
                SafeOpened o => $"{Name(o.Faction)} open their Safe: {Stored(o.Coins, o.Workers, o.Priests)}",
                FreeDwellingOffered f => $"{Name(f.Faction)} may place a free dwelling on a Lake hex",
                IceDwellingsOffered i => $"{Name(i.Faction)} may place a free dwelling on each of {(i.Masses == 1 ? "1 mass" : $"{i.Masses} masses")} of Ice",
                LavaOwed l => $"{Name(l.Faction)} owe {(l.Count == 1 ? "a lava tile" : $"{l.Count} lava tiles")}",
                PyroMoved p when move is not AdvancePyro => $"{Name(p.Faction)} pyro token {p.From} → {p.To}",
                RockMovesOwed r => $"{Name(r.Faction)} may move {(r.Count == 1 ? "a glowing rock" : $"{r.Count} glowing rocks")}",
                RocksCleared r => $"{Name(r.Faction)} clear every glowing rock",
                RockMoved r when move is not (MoveRock or BonusMoveRock) => $"{Name(r.Faction)} cover {r.Terrain} with a glowing rock",
                StartingTownOwed s => $"{Name(s.Faction)} take a key token for their stronghold next",
                AtlanteansTownReward a => $"{Name(a.Faction)}' town reaches a power of {a.Power}",
                FreeUpgradeOwed f => $"{Name(f.Faction)} take a free shipping or spade step",
                ChangelingTerrainsChosen => null,
                LavaPlaced => null,
                LavaFlowed => null,
                RiverTokensPlaced => null,
                BridgeMoved => null,
                FavourReturned => null,
                PlanningStarted => "Free play begins",
                TurnOrderStepBegan => "the turn order is chosen next",
                RoundEnded ended => $"round {ended.Round} ends",
                RoundStarted started => $"round {started.Round} begins",
                GameEnded over => $"game over: {string.Join(" and ", over.Winners.Select(f => Name(f)))} win",
                _ => null,
            };
            if (e is VictoryPointsGained gained && !(skipActorPoints && gained.Faction == actor))
            {
                var key = (gained.Faction, PointsSource(gained.Source, gained.Amount));
                if (!points.ContainsKey(key))
                {
                    points[key] = results.Count;
                    results.Add(null); // filled in below, once every point is counted
                }
                pointTotals[key] = pointTotals.GetValueOrDefault(key) + gained.Amount;
            }
            else if (text is not null)
                results.Add(new LogPart(text, EventFaction(e),
                    e is PowerOffered ? PartKind.Offer
                    : e is PowerGained { Source: PowerSource.NeighbouringBuild } ? PartKind.OfferGain : PartKind.Other));
        }
        foreach (var (key, at) in points)
        {
            int amount = pointTotals[key];
            results[at] = amount == 0 ? null : new LogPart(
                $"{(key.Faction == actor ? "" : Name(key.Faction) + " ")}{(amount > 0 ? "+" : "−")}"
                + $"{Math.Abs(amount)} VP{(key.Source is null ? "" : $" ({key.Source})")}",
                key.Faction, key.Source == OfferPointsSource ? PartKind.OfferGain : PartKind.Other);
        }
        return results.OfType<LogPart>().ToList();
    }

    private const string OfferPointsSource = "power"; // PointsSource for the VP an accepted offer cost

    // The faction an event is about (null: nobody in particular).
    private static Faction? EventFaction(GameEvent e) => e switch
    {
        PriestGained g => g.Priest.Owner,
        LandscapePlaced p => p.Landscape.Owner,
        PlanningStarted or TurnOrderStepBegan or RoundEnded or RoundStarted or GameEnded => null,
        _ => e.GetType().GetProperty("Faction")?.GetValue(e) as Faction?
             ?? e.GetType().GetProperty("Player")?.GetValue(e) as Faction?,
    };

    // Where points came from, in words (from the area the points' light starts at: VictoryPointsGained.Source).
    private static string? PointsSource(PointsArea? area, int amount) => area switch
    {
        PointsArea.RoundTile => "round bonus",
        PointsArea.Favour => "favour",
        PointsArea.PassTile => "pass tile",
        PointsArea.Portrait => "faction ability",
        PointsArea.Shipping => "shipping upgrade",
        PointsArea.Spades => "spade upgrade",
        PointsArea.BowlThree => "power token",
        PointsArea.OnHex => amount < 0 ? OfferPointsSource : "stronghold", // a power offer's cost, or a stronghold's ability
        PointsArea.OnBridge => "bridge",
        PointsArea.KeyOn => "town",
        PointsArea.Landscape => "landscape",
        _ => null,
    };

    // What a special landscape offers, in words.
    private static string Offered(Faction faction, LandscapeOffer offer)
    {
        var who = Name(faction);
        if (offer.HasFlag(LandscapeOffer.Portal))
            return $"{who} may place their {Landscapes.Name(faction)}";
        if (offer.HasFlag(LandscapeOffer.Shapeshift))
            return $"{who} may move their home for free";
        var upgrades = new List<string>();
        if (offer.HasFlag(LandscapeOffer.Shipping))
            upgrades.Add("shipping");
        if (offer.HasFlag(LandscapeOffer.Spades))
            upgrades.Add("spades");
        return $"{who} may upgrade {string.Join(" and ", upgrades)} without a priest";
    }

    // The Alchemists' upgrade offered by their Gloomy Workshop.
    private static string NoPriest(GameState before, Faction faction, LandscapeOffer offer) =>
        before.IsInGame(faction) && before[faction].LandscapeOffers.HasFlag(offer) ? $" without a priest ({Landscapes.Name(faction)})" : "";

    private static string Reward(Income income, int spades) =>
        income == default ? Spades(spades) : spades > 0 ? $"{income}, {Spades(spades)}" : income.ToString();

    // ---------- Pieces of text ----------

    private static Faction? Actor(Move move) => move switch
    {
        AddPlayer m => m.Faction,
        RemovePlayer m => m.Faction,
        ChooseTurnOrder m => m.Faction,
        UnchooseTurnOrder m => m.Faction,
        PlaceStartingDwelling m => m.Dwelling.Owner,
        BuildDwelling m => m.Dwelling.Owner,
        UpgradeBuilding m => m.Building.Owner,
        PlacePriest m => m.Priest.Owner,
        BuildOnSpadedHex m => m.Dwelling.Owner,
        PlaceFreeDwelling m => m.Dwelling.Owner,
        BonusSelkiesBuild m => m.Dwelling.Owner,
        _ => move.GetType().GetProperty("Player")?.GetValue(move) as Faction?,
    };

    private static string Name(Faction faction) => Factions.Name(faction);

    private static string ResourceNoun(Resource resource) => resource switch
    {
        Resource.VictoryPoints => "VP",
        Resource.Coins => "coins",
        _ => "workers",
    };

    private static int Value(PlayerState player, Resource resource) => resource switch
    {
        Resource.VictoryPoints => player.VictoryPoints,
        Resource.Coins => player.Coins,
        _ => player.Workers,
    };

    // A Setup building placed, moved or taken back.
    private static string Placed(GameState s, EditBuilding m, Func<int, string> hex)
    {
        var from = s.Map.IndexOf(m.Building);
        var noun = Buildings.Noun(m.Building.Kind);
        var who = Name(m.Building.Owner);
        return m.Hex is not int to ? $"{who} {noun} at {hex(from)} back to the board"
            : from >= 0 && from != to ? $"{who} {noun} moved from {hex(from)} to {hex(to)}"
            : $"{who} {noun} at {hex(to)}{(m.Terrain is Terrain t ? $" on {t}" : "")}";
    }

    private static string Noun(PieceKind? kind) => kind is PieceKind k ? Buildings.Noun(k) : "building";

    // The tiles' multi-line descriptions on one line.
    private static string Short(string text) => text.Replace("\n", "; ");

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static string Stored(int coins, int workers, int priests)
    {
        var parts = new[] { (coins, "coin"), (workers, "worker"), (priests, "priest") }
            .Where(p => p.Item1 > 0).Select(p => Plural(p.Item1, p.Item2)).ToList();
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Spades(int count) => count == 1 ? "1 spade" : $"{count} spades";

    private static string Coins(int? coins) => coins is > 0 ? $" with {Plural(coins.Value, "coin")}" : "";

    private static string Source(BonusSource source) => source switch
    {
        BonusSource.Stronghold => "stronghold action",
        BonusSource.PassTile => "pass tile action",
        _ => "favour action",
    };

    private static string Transformed(IReadOnlyList<GameEvent> events) =>
        events.OfType<TerrainTransformed>().FirstOrDefault() is TerrainTransformed t ? $", turning {t.From} into {t.To}" : "";

    private static string Burned(IReadOnlyList<GameEvent> events)
    {
        int burned = events.Count(e => e is PowerBurned);
        return burned > 0 ? $", burning {burned}pw first" : "";
    }

    private static string Options(GameOptions options)
    {
        var parts = new List<string>();
        if (options.Calculator)
            parts.Add(options.CalculatorStrict ? "Calculator (Strict)" : "Calculator");
        else
            parts.Add($"{Rules.ModeName(options.StartingMode)} mode");
        if (options.SetUpInProgress)
            parts.Add("a game in progress set up");
        if (options.VariableTurnOrder)
            parts.Add("variable turn order");
        if (options.MiniExpansion)
            parts.Add("mini-expansions");
        if (options.FireAndIce)
            parts.Add("Fire & Ice");
        if (options.Landscapes)
            parts.Add("special landscapes");
        if (options.FinalScoring is FinalScoringTile tile)
            parts.Add($"final scoring: {TileName(tile)}");
        return parts.Count == 0 ? "base game" : string.Join(", ", parts);
    }

    public static string TileName(FinalScoringTile tile) => tile switch
    {
        FinalScoringTile.StrongholdSanctuary => "stronghold to sanctuary",
        FinalScoringTile.Distance => "longest distance",
        FinalScoringTile.Clusters => "clusters",
        _ => "edges",
    };
}
