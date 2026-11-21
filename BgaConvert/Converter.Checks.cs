using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// Converter, continued: checking the position against BGA's counts.
public sealed partial class Converter
{
    // ---------- Checking against BGA's counts ----------

    private void Check(int from, int to)
    {
        var counters = new Dictionary<string, string>();
        JsonObject? workers = null, priests = null;
        var vp = new Dictionary<string, int>();
        var steps = new Dictionary<(string, Track), int>();
        for (var i = from; i <= to; i++)
        {
            var n = _g.Notes[i];
            if (n.Args["counters"] is JsonObject c)
                foreach (var (key, value) in c)
                {
                    if (value is not JsonObject entry)
                        continue;
                    if (entry.ContainsKey("counter_value")) // logs.html: {"coins_count_<player id>": {counter_value}}
                    {
                        if (J.Str(entry["counter_value"]) is string v && v != "null")
                            counters[key] = v;
                    }
                    else // the replay page's g_gamelogs (the bookmarklet): {"<player id>": {"coins_count": …}}
                        foreach (var (name, count) in entry)
                            if (J.Str(count) is string v && v != "null")
                                counters[$"{name}_{key}"] = v;
                }
            if (n.Args["workers"] is JsonObject w)
                workers = w;
            if (n.Args["priests"] is JsonObject p)
                priests = p;
            if (n.Str("player_id") is string id)
            {
                // BGA's running total; shipping and spade upgrades give their points (score_inc) without a new total.
                if (n.Int("newScore") is int score)
                    vp[id] = _bgaScore[id] = score;
                else if (n.Type is "advanceShippingTrack" or "advanceExchangeTrack" && n.Args["score_inc"] is not null
                         && _bgaScore.TryGetValue(id, out var was))
                    vp[id] = _bgaScore[id] = was + n.Amount("score_inc");
            }
            if (n.Type == "advanceCultTrack" && n.Str("player_id") is string who && n.Int("final_pos") is int pos)
                steps[(who, Tables.ToTrack(n.Str("cult_id") ?? n.Str("cult_name") ?? ""))] = pos;
        }
        _packetsChecked++;
        var note = _g.Notes[to];
        // Players whose answer to a power offer BGA logs in the next few packets: Core may have applied it already
        // (or taken a free 1 power at once), so their counts are compared once BGA has caught up.
        var answering = _g.Notes.Skip(to + 1).TakeWhile(m => m.Packet <= note.Packet + 3)
            .Where(IsDecision).Select(PlayerOf).OfType<Faction>().ToHashSet();
        foreach (var (id, f) in _faction)
        {
            if (!S.IsInGame(f) || S.PowerOffers.Any(o => o.Player == f) || answering.Contains(f))
                continue; // still being offered power, or answering it later in the log
            var p = S[f];
            void Compare(string what, int? bga, int core)
            {
                if (bga is not int b)
                    return;
                var diff = core - b;
                var key = (f, what);
                var before = _lastDiff.GetValueOrDefault(key);
                _lastDiff[key] = diff;
                if (diff != before)
                {
                    _mismatchCount += diff != 0 ? 1 : 0;
                    Line(note, diff == 0
                        ? $"  ok again: {Factions.Name(f)} {what} (Core {core}, BGA {b})"
                        : $"DIFFERENCE {Factions.Name(f)} {what}: Core {core}, BGA {b}");
                }
            }
            int? Counter(string name) => counters.TryGetValue($"{name}_{id}", out var v) && int.TryParse(v, out var x) ? x : null;
            Compare("coins", Counter("coins_count"), p.Coins);
            Compare("power bowl 1", Counter("power1_count"), p.Power.Bowl1);
            Compare("power bowl 2", Counter("power2_count"), p.Power.Bowl2);
            Compare("power bowl 3", Counter("power3_count"), p.Power.Bowl3);
            if (S.Phase is Phase.Playing or Phase.RoundEndSpades)
                Compare("free spades", Counter("spades_count"), p.SpadesOwed);
            if (workers is not null)
            {
                var count = workers.Count(kv => J.Str(kv.Value?["player_id"]) == id);
                if (count > 0) // a list may cover only some players (and not say so): compare those it names
                    Compare("workers", count, p.Workers);
            }
            if (priests is not null && priests.Any(kv => J.Str(kv.Value?["player_id"]) == id))
            {
                var hand = priests.Count(kv => J.Str(kv.Value?["player_id"]) == id && J.Str(kv.Value?["priest_in_supply"]) == "0"
                    && J.Str(kv.Value?["cos_id"]) is null or "null" && J.Str(kv.Value?["priest_in_treasury"]) is null or "0");
                Compare("priests in hand", hand, p.Priests.Count);
            }
            if (vp.TryGetValue(id, out var score))
                Compare("VP", score, S.Phase == Phase.Finished && S.FinalScores.FirstOrDefault(s => s.Faction == f) is FinalScore fs ? fs.Total : p.VictoryPoints);
            foreach (var track in Enum.GetValues<Track>())
                if (steps.TryGetValue((id, track), out var step))
                    Compare($"{track} steps", step, p.TrackSteps[(int)track]);
        }
    }

    // BGA's final scoring beside Core's.
    // A BGA note's log line in plain words; null if it has none.
    private static string? PlainLine(Note n) => J.Plain(n.Log, n.Args) is { Length: > 0 } line ? line : null;

    // The final scores here that differ from BGA's (none unless the game reached its end).
    private List<ScoreDifference> ScoreDifferences()
    {
        var differences = new List<ScoreDifference>();
        if (S.Phase != Phase.Finished)
            return differences;
        var total = new Dictionary<string, int>();
        foreach (var n in _g.Notes)
            if (n.Str("player_id") is string id && n.Int("newScore") is int score)
                total[id] = score;
        foreach (var (id, f) in _faction)
            if (S.FinalScores.FirstOrDefault(s => s.Faction == f) is FinalScore core && total.TryGetValue(id, out var bga) && core.Total != bga)
                differences.Add(new ScoreDifference(f, _nameById.GetValueOrDefault(id, id), core.Total, bga));
        return differences;
    }

    private string FinalScores()
    {
        var text = new StringBuilder("== Final scoring\n");
        if (S.Phase != Phase.Finished)
            return text.Append("Core's game did not reach the end.\n").ToString();
        var bga = new Dictionary<string, List<string>>();
        var total = new Dictionary<string, int>();
        foreach (var n in _g.Notes)
        {
            if (n.Str("player_id") is string id && n.Int("newScore") is int score)
                total[id] = score;
            if (n.Type == "finalScoring" && n.Str("player_id") is string who)
                (bga.TryGetValue(who, out var list) ? list : bga[who] = new List<string>()).Add(J.Plain(n.Log, n.Args).Replace(n.Str("player_name") ?? "", "").Trim());
        }
        foreach (var (id, f) in _faction)
        {
            var core = S.FinalScores.FirstOrDefault(s => s.Faction == f);
            text.AppendLine($"{Factions.Name(f)} ({_nameById.GetValueOrDefault(id, id)}): Core {core?.Total.ToString() ?? "?"}, BGA {(total.TryGetValue(id, out var t) ? t.ToString() : "?")}");
            if (core is not null)
                text.AppendLine($"  Core: {core.VictoryPointsBefore} before final scoring; cults {string.Join("/", core.TrackPoints)} (steps {string.Join("/", core.TrackSteps)}); " +
                    $"network {core.Network} → {core.NetworkPoints}; tile {core.Tile?.ToString() ?? "none"} {core.TileValue} → {core.TilePoints}; coins {core.Coins} → {core.CoinPoints}");
            if (bga.TryGetValue(id, out var lines))
                text.AppendLine($"  BGA: {string.Join("; ", lines)}");
        }
        return text.ToString();
    }
}
