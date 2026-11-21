using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// What a conversion gives: the history (the game line marked, at its last move), the report and trace (for bug
// reports), a one-paragraph summary, and for the app:
//   Completed: the whole log converted (to the end of the game, or a game in progress to its latest move);
//   InProgress: the log has no final scoring yet;
//   Stop: where and why it stopped (null when completed);
//   ScoreDifferences: final scores here that differ from BGA's (the game converted to its end);
//   VersionDifferences: factions in BGA's game whose material differs from Core's (an older version of the faction).
public sealed record ConversionResult(History History, string Report, string Trace, string Summary, bool Completed,
    string TableId = "", bool InProgress = false, ConversionStop? Stop = null,
    IReadOnlyList<ScoreDifference>? ScoreDifferences = null, IReadOnlyList<string>? VersionDifferences = null);

// Where a conversion stopped: BGA's move number and its log line in plain words (both null during setup), the Core move
// refused there in the log's words (null if the converter stopped for another reason), and why.
public sealed record ConversionStop(string? BgaMove, string? BgaLine, string? RefusedMove, string Reason);

// A final score that differs from BGA's: the faction, the BGA player, Core's total and BGA's.
public sealed record ScoreDifference(Faction Faction, string Player, int Core, int Bga);

// Replays a BGA game through Core's rules, one BGA notification at a time, turning each into the Core moves that do the
// same thing. Moves Core does on its own (income, cult rewards, town bonuses, VP) are not sent; they are checked instead:
// after every BGA packet, each player's coins, power, spades, workers, priests, VP and cult steps are compared with
// BGA's counts. A move Core refuses stops the conversion; everything up to it is kept.
public sealed partial class Converter
{
    private readonly BgaGame _g;
    private History _h = new(GameState.Initial);
    private readonly List<Move> _applied = new(); // every move accepted, in order: the save is rebuilt from these
    // Where the active player's current action began: the index in _applied of its first move and the state before it.
    private (int Index, GameState Before)? _actionStart;
    private readonly StringBuilder _problems = new();
    private readonly StringBuilder _trace = new();
    private readonly Dictionary<string, Faction> _faction = new();             // BGA player id → faction
    private readonly Dictionary<string, string> _idByName = new();             // BGA player name → id
    private readonly Dictionary<string, string> _nameById = new();
    private readonly Dictionary<int, (string Up, string Down)> _bridgeSpots = new(); // bbs_id → the two hexes (BGA ids)
    private readonly Dictionary<int, int> _passTiles = new();                  // bonus_id → Core pass tile
    private readonly Dictionary<int, int> _keyTiles = new();                   // town_id → Core key tile
    private readonly HashSet<int> _consumed = new();                           // notes already turned into moves
    private readonly HashSet<string> _unknownTypes = new();
    private readonly Dictionary<(Faction, string), int> _lastDiff = new();     // last reported Core − BGA difference
    private readonly Dictionary<string, int> _bgaScore = new();                // BGA's VP total so far, by player id
    private int _pos;
    private int _moves, _problemCount, _mismatchCount, _packetsChecked;
    private bool _stopped;
    private string? _stopReason;
    private ConversionStop? _stop;
    private string? _refused;                   // the Core move just refused, in the log's words (for _stop)
    private List<string> _versionDiffs = new();
    private string _header = "";

    public Converter(BgaGame game) => _g = game;

    private GameState S => _h.Current;

    public ConversionResult Run()
    {
        try
        {
            Setup();
            if (!_stopped)
                Play();
            if (!_stopped)
                Finish();
        }
        catch (Exception e)
        {
            _stopped = true;
            _stopReason ??= $"converter error at note {_pos}: {e.Message}";
            _stop ??= new ConversionStop(_pos < _g.Notes.Count ? _g.Notes[_pos].MoveId : null,
                _pos < _g.Notes.Count ? PlainLine(_g.Notes[_pos]) : null, null, $"The converter failed here ({e.Message}).");
            Problem(_pos < _g.Notes.Count ? _g.Notes[_pos] : null, $"CONVERTER ERROR: {e}");
        }
        // Moves put back before an action (MoveBeforeAction) left side lines in _h: the save is the moves as finally made.
        var clean = new History(GameState.Initial);
        foreach (var move in _applied)
            if (!clean.Apply(move).Accepted)
            {
                Problem(null, $"CONVERTER ERROR: rebuilding the save, {move} was refused");
                break;
            }
        _h = clean;
        _h.MarkGameLine();

        // A game in progress (its log has no final scoring yet): converted when every note was read.
        var caughtUp = !_stopped && S.Phase != Phase.Finished && InProgress;
        var completed = !_stopped && (S.Phase == Phase.Finished || caughtUp);
        var summary = new StringBuilder();
        summary.AppendLine($"BGA table {_g.TableId}: {_g.Notes.Count} notifications, {_moves} Core moves.");
        summary.AppendLine(caughtUp ? $"Game in progress: converted to its latest move ({Name(S.ActivePlayer)} to act)."
            : completed ? "Converted to the end of the game."
            : $"Stopped: {_stopReason ?? $"Core is in {S.Phase}, not at the end of the game."}");
        summary.AppendLine($"{_packetsChecked} packets checked against BGA's counts: {_mismatchCount} differences, {_problemCount} other problems.");
        var report = new StringBuilder();
        report.AppendLine($"BGA import report — table {_g.TableId}");
        report.AppendLine();
        report.Append(_header);
        report.AppendLine();
        report.AppendLine("== Summary");
        report.Append(summary);
        report.AppendLine();
        report.AppendLine("== Problems and differences, in game order");
        report.AppendLine("(Differences: \"Core x, BGA y\" after that BGA packet. A difference is reported again only when it changes.)");
        report.Append(_problems.Length == 0 ? "None.\n" : _problems.ToString());
        report.AppendLine();
        report.Append(FinalScores());
        var stop = completed ? null
            : _stop ?? new ConversionStop(null, null, null, $"BGA's log ended before the game did here ({S.Phase}).");
        return new ConversionResult(_h, report.ToString(), _trace.ToString(), summary.ToString(), completed,
            _g.TableId, InProgress, stop, ScoreDifferences(), _versionDiffs);
    }

    // ---------- Small helpers ----------

    // Conversions and burns BGA logs in the middle of an action (free spades still to spend, a favour or town to take,
    // offers still being answered, ...): Core takes free actions only between actions, so they wait and are made
    // as soon as the action is complete, at the latest before the turn ends.
    private readonly List<(Faction Player, Move Move, Note Note)> _deferred = new();

    // Spaded hexes a move just closed (spades all spent, no dwelling affordable there in Strict): a conversion BGA logs
    // next, before a dwelling on one of them, paid for that dwelling, so it goes before the action too.
    private ValueList<int> _closedSpaded = ValueList<int>.Empty;

    private void NoteActionStart(GameState before, GameState after, Move move)
    {
        if (before.SpadedHexes.Count > 0 && after.SpadedHexes.Count == 0 && after.ActivePlayer == before.ActivePlayer)
            _closedSpaded = new ValueList<int>(_closedSpaded.Concat(before.SpadedHexes).Distinct());
        // A transform whose hex is not open for a dwelling afterwards (not affordable yet in Strict).
        if (move switch { TransformTerrain t => t.Hex, SpendSpades sp => sp.Hex, _ => (int?)null } is int transformed
            && !after.SpadedHexes.Contains(transformed) && after.ActivePlayer == before.ActivePlayer)
            _closedSpaded = new ValueList<int>(_closedSpaded.Append(transformed).Distinct()); // all the action's closed hexes
        if (after.ActivePlayer != before.ActivePlayer || after.Phase != Phase.Playing)
        {
            _actionStart = null;
            _closedSpaded = ValueList<int>.Empty;
        }
        else if (!before.ActionTaken && after.ActionTaken)
            _actionStart = (_applied.Count - 1, before);
    }

    // A conversion BGA logs in the middle of an action: made just before the action instead, the
    // action's moves then made again after it. False (and nothing changed) if that doesn't work.
    private bool MoveBeforeAction(Faction f, Move move, Note n)
    {
        if (_actionStart is not (int index, GameState start) || S.ActivePlayer != f)
            return false;
        var tail = _applied.Skip(index).ToList();
        void Restore()
        {
            while (!ReferenceEquals(_h.Current, start) && _h.Undo()) { }
            foreach (var t in tail)
                _h.Apply(t); // the old positions are still there: the same states again
        }
        for (var guard = 0; guard <= tail.Count + 1 && !ReferenceEquals(_h.Current, start); guard++)
            _h.Undo();
        if (!ReferenceEquals(_h.Current, start))
        {
            Restore();
            return false;
        }
        var converted = _h.Apply(move);
        if (!converted.Accepted)
        {
            Restore();
            return false;
        }
        var afterConversion = _h.Current;
        foreach (var t in tail)
            if (!_h.Apply(t).Accepted)
            {
                // the action no longer works with the conversion first: back as it was
                while (!ReferenceEquals(_h.Current, start) && _h.Undo()) { }
                foreach (var u in tail)
                    _h.Apply(u);
                return false;
            }
        _applied.RemoveRange(index, tail.Count);
        _applied.Add(move);
        _applied.AddRange(tail);
        _actionStart = (index + 1, afterConversion);
        _moves++;
        string described;
        try { described = GameLog.Describe(start, move, converted.Events, converted.State); }
        catch (Exception e) { described = $"(no log line: {e.Message})"; }
        _trace.AppendLine($"#{n.MoveId,-6} {described} (logged mid-action by BGA: made before the action)");
        return true;
    }

    private bool BuildOnClosedSpadedHexFollows(Faction f, Note n)
    {
        if (_closedSpaded.Count == 0 || S.ActivePlayer != f)
            return false;
        var build = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet <= n.Packet + 1)
            .FirstOrDefault(m => m.Type == "dwellingPlaced" && m.Str("player_id") == n.Str("player_id"));
        return build?.Str("bts_id") is string bts && _closedSpaded.Contains(Hex(bts));
    }

    // A conversion or burn BGA logs just before an action of the same player that can't be part of the action already
    // taken (a priest sent, an upgrade, a power action): it belongs to the player's next turn (everyone else has
    // passed), so the turn ends first, as for an action (the priest in a returning slot is back by then).
    private static readonly HashSet<string> NewActionTypes = new() { "orderAPriest", "structureUpgraded", "powerActionConvert", "powerActionBridge" };

    private bool StartsNextTurn(Faction f, Note n) =>
        S.ActivePlayer == f && S.ActionTaken && !Rules.IsMidAction(S, f) && _closedSpaded.Count == 0
        && _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
            .Any(m => NewActionTypes.Contains(m.Type) && m.Str("player_id") == n.Str("player_id"));

    private void Free(Faction f, Move move, Note n)
    {
        // Converting earlier (ConvertBeforeLastEndTurn); or mid-action where Core allows it (a town still to be formed:
        // 931629591 #359), unless a dwelling on a hex the action left closed follows (that conversion paid for it: made
        // before the action, below).
        if (_convertingEarlier || _deferred.Count == 0 && Rules.IsMidAction(S, f) && !BuildOnClosedSpadedHexFollows(f, n)
                && Rules.Apply(S, move).Accepted)
        {
            Do(move, n);
            return;
        }
        if (_deferred.Count == 0 && (Rules.IsMidAction(S, f) || BuildOnClosedSpadedHexFollows(f, n)) && MoveBeforeAction(f, move, n))
            return;
        if (_deferred.Count > 0 || Rules.IsMidAction(S, f))
        {
            if (_deferred.Count == 0)
                _trace.AppendLine($"#{n.MoveId,-6} (conversion can't go before the action either: held until the {Name(f)}' action is complete)");
            _deferred.Add((f, move, n));
            return;
        }
        Do(move, n);
    }

    private void FlushDeferred()
    {
        while (_deferred.Count > 0 && !_stopped)
        {
            var (f, move, n) = _deferred[0];
            if (S.ActivePlayer != f || Rules.IsMidAction(S, f))
                return;
            _deferred.RemoveAt(0);
            Do(move, n);
        }
    }

    // BGA lets a town's key lie on its own piece on a river (Riverwalkers, Selkies); Core doesn't.
    // The key's hex matters to no score, so it goes on a land hex of the same town instead.
    private int KeyHex(Faction f, int hex)
    {
        if (f == Faction.Mermaids || !S.Layout.Contains(hex) || S.HexTerrain[hex] != Terrain.River)
            return hex;
        foreach (var town in Rules.FormableTowns(S, f))
            if (town.Contains(hex))
                return town.Where(h => S.HexTerrain[h] != Terrain.River && !S.PlacedKeys.Any(k => k.Hex == h)).DefaultIfEmpty(hex).First();
        return hex;
    }

    private bool Do(Move move, Note? n)
    {
        if (_stopped)
            return false;
        var before = S;
        var result = _h.Apply(move);
        var at = n is null ? "setup" : $"#{n.MoveId}";
        if (!result.Accepted)
        {
            var reason = string.Join(" ", result.Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Message));
            _trace.AppendLine($"{at,-7} REFUSED {move} — {reason}");
            try { _refused = GameLog.Describe(before, move, Array.Empty<GameEvent>(), before); }
            catch (Exception) { _refused = move.ToString(); }
            Stop(n, $"Core REFUSED {move}: {reason}");
            return false;
        }
        _moves++;
        _applied.Add(move);
        if (before.TreasuryOffer is null && result.State.TreasuryOffer is not null)
            _treasuryOfferPacket = n?.Packet;
        NoteActionStart(before, result.State, move);
        string described;
        try { described = GameLog.Describe(before, move, result.Events, result.State); }
        catch (Exception e) { described = $"(no log line: {e.Message})"; }
        _trace.AppendLine($"{at,-7} {described}");
        foreach (var issue in result.Issues.Where(i => i.Severity != Severity.Error))
        {
            _trace.AppendLine($"        {issue.Severity}: {issue.Message}");
            if (issue.Severity == Severity.Warning)
                Problem(n, $"warning: {issue.Message}");
        }
        return true;
    }

    private void Stop(Note? n, string reason)
    {
        _stopped = true;
        _stopReason ??= $"{(n is null ? "setup" : $"#{n.MoveId}")}: {reason}";
        if (_stop is null)
        {
            // A refusal: Do's reason is "Core REFUSED <move>: <Core's words>"; the user gets Core's words.
            var refused = _refused is not null && reason.StartsWith("Core REFUSED ");
            var words = refused && reason.IndexOf("}: ", StringComparison.Ordinal) is int at && at >= 0 ? reason[(at + 3)..]
                : refused && reason.IndexOf(": ", StringComparison.Ordinal) is int colon && colon >= 0 ? reason[(colon + 2)..] : reason;
            _stop = new ConversionStop(n?.MoveId, n is null ? null : PlainLine(n), refused ? _refused : null, words);
        }
        Problem(n, reason);
        if (n is not null)
            Line(n, $"  BGA: {n}");
    }

    private void Problem(Note? n, string text)
    {
        _problemCount++;
        Line(n, text);
    }

    private void Line(Note? n, string text) =>
        _problems.AppendLine($"{(n is null ? "setup" : $"#{n.MoveId}"),-7} {text}");

    private void WithPlayer(Note n, Action<Faction> act)
    {
        if (PlayerOf(n) is Faction f)
            act(f);
        else
            Problem(n, $"unknown player in {n}");
    }

    private Faction? PlayerOf(Note n)
    {
        var id = n.Str("player_id");
        if ((id is null || id == "null") && n.Str("player_name") is string name)
            _idByName.TryGetValue(name, out id);
        return id is not null && _faction.TryGetValue(id, out var f) ? f : null;
    }

    private static string Name(Faction? f) => f is Faction x ? Factions.Name(x) : "nobody";

    private Piece Next(Faction f, PieceKind kind) =>
        S[f].NextBuilding(kind) ?? throw new InvalidOperationException($"The {Factions.Name(f)} have no {kind} left in Core.");

    // The Changelings' dwelling BGA built: the one beside the stronghold (struct_order 9), else one Core accepts on
    // that hex, of its terrain if any (each dwelling has its own terrain: A, B or C); the leftmost if none.
    private Piece ChangelingDwelling(Note n, int hex, bool spaded = false)
    {
        var player = S[Faction.Changelings];
        if (n.Int("struct_order") == 9 && player.ExtraDwelling is Piece beside)
            return beside;
        // One of the hex's own terrain first (BGA logs any transform before it), so Core doesn't transform again.
        foreach (var piece in player.DwellingBank.OfType<Piece>().OrderBy(p => Rules.ChangelingTerrain(player, p) == S.HexTerrain[hex] ? 0 : 1))
            if (Rules.Apply(S, spaded ? (Move)new BuildOnSpadedHex(piece, hex) : new BuildDwelling(piece, hex)).Accepted)
                return piece;
        return Next(Faction.Changelings, PieceKind.Dwelling);
    }

    private int PassTileOf(Note n)
    {
        var id = n.Int("bonus_id") ?? -1;
        return _passTiles.TryGetValue(id, out var tile) ? tile : throw new InvalidDataException($"Bonus card {id} is not in the game data.");
    }

    // A BGA hex id ("H6": row H, 6th space counting rivers) → Core's hex number on the map being played.
    private int Hex(string? bts)
    {
        if (string.IsNullOrEmpty(bts) || bts.Length < 2 || !int.TryParse(bts[1..], out var column))
            throw new InvalidDataException($"Not a BGA hex id: \"{bts}\".");
        var hex = S.Layout.IndexOf(char.ToUpperInvariant(bts[0]) - 'A', column - 1);
        return hex >= 0 ? hex : throw new InvalidDataException($"BGA hex {bts} is off Core's map.");
    }
}
