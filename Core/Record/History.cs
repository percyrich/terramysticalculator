namespace Core;

// A bookmarked position in the history: the position's number (positions are numbered as they are made) and its name.
public sealed record Bookmark(int Position, string Name);

// One point on the bookmark map: a bookmark, or the end of the active line when that has no bookmark (Bookmark null).
// From: the vertex its edge comes from, the nearest bookmark before it on its line (null for Session start). Lines
// never fork on the map: a line that split off between two bookmarks is drawn from the last bookmark before the
// split. Active: on the active line. Indent: for the list, how many times the lines branched off on the way here.
public sealed record MapVertex(int Position, Bookmark? Bookmark, int? From, bool Active, int Indent, bool OnGameLine = false);

// Where the player is with respect to the game line (History.GameLineStatus): on it, or `Moves` moves off it since
// leaving it at position LeftFrom.
public sealed record GameLineStatus(bool OnLine, int Moves, int LeftFrom);

// The bookmark map: every vertex in list order (each line in order; the branches leaving a vertex are listed under
// it, before its own line carries on), and where the player is: on vertex At (Towards null), or between two
// vertices, along the active edge from At to Towards.
public sealed record HistoryMap(ValueList<MapVertex> Vertices, int At, int? Towards);

// One record of the game log: a position on the active line, the move that made it (null for Session start), the
// state before the move (null for Session start) and after it, what the move brought about (its events), and the
// position's bookmark, if any.
public sealed record LogEntry(int Position, Move? Move, GameState? Before, GameState After, IReadOnlyList<GameEvent> Events,
    Bookmark? Bookmark, bool OnGameLine = false);

// One record in the notes (History.Notes), oldest first: the game line in full (Depth 0), and under each position
// where lines part, the lines leaving it one step further in (Depth 1, 2, …) before its own line carries on. A line
// the active line runs along is listed whole (its own partings under it in turn); any other line only by its first
// record, which Opens it (shown "(Alternate)"; the first record of a line listed whole Opens it too). Active: on the
// active line. Number: moves from Session start (as the record's index in Log() when it is on the active line).
public sealed record NoteEntry(LogEntry Entry, int Depth, bool Opens, bool Active, int Number);

// The timeline: a tree of positions, each made by one move from the one before it. This is the ONLY mutable object
// in Core: it never recomputes or copies states; undo, redo and jumps just move which position is current.
//   The active line runs from Session start through the current position and on along the redo line (at a split,
//     the line used most recently) to its end. Other lines are kept only up to their last bookmark: whatever lies
//     past it is dropped (so a new move after undoing drops an unbookmarked old future, and deleting a bookmark
//     drops the moves that only led to it).
//   Bookmarks are not moves, and are not undone. One per position. Session start (the first position) is always
//     bookmarked and can't be deleted; "Game start" and each round's start (e.g. "R2_start", "R2_start_2" on a
//     second line) are bookmarked as they are reached; others are named by how far the game has got ("Setup_1",
//     "R3_2", "End_1"), numbers never reused.
public sealed class History
{
    public const string SessionStart = "Session start";
    public const string GameStart = "Game start";

    private sealed class Position
    {
        public Position(int id, Position? parent, IReadOnlyList<Move> moves, GameState state, IReadOnlyList<GameEvent> events)
        {
            Id = id;
            Parent = parent;
            Moves = moves;
            State = state;
            Events = events;
        }

        public int Id { get; }
        public Position? Parent { get; }
        public IReadOnlyList<Move> Moves { get; }  // the moves that made it from its parent: one, or several merged (Setup)
        public Move? Move => Moves.Count == 0 ? null : Moves[^1]; // null for Session start
        public GameState State { get; }
        public IReadOnlyList<GameEvent> Events { get; } // what the move brought about (none for Session start)
        public List<Position> Children { get; } = new(); // in the order made
        public Position? Next { get; set; }              // where redo goes: the line used most recently; null if none
        public Bookmark? Bookmark { get; set; }
    }

    private readonly Position _root;
    private Position _current;
    private int _made;
    private readonly Dictionary<int, Position> _positions = new();
    private readonly Dictionary<string, int> _used = new(); // the last number used in each series of names
    private Position? _gameEnd;                              // the game line's last position (null: no game line)
    private HashSet<Position> _gameLine = new();             // every position on the game line, Session start first

    public History(GameState initial)
    {
        _root = Make(null, Array.Empty<Move>(), initial, Array.Empty<GameEvent>());
        _root.Bookmark = new Bookmark(_root.Id, SessionStart);
        _current = _root;
    }

    public GameState Current => _current.State;
    public IReadOnlyList<GameEvent> CurrentEvents => _current.Events; // what the current position's move brought about
    public bool CanUndo => _current.Parent is not null;
    public bool CanRedo => NextOf(_current) is not null;
    public bool CanBookmark => _current.Bookmark is null;

    // A move the rules accept moves the player on (refused moves never enter the history):
    //   to a position already there after this one with the same state (its redo line, the game line, a side line
    //     planned before): no new position, so a game played through by hand stays on its line;
    //   else back onto the game line, if the move reaches a position on it (e.g. free actions in another order);
    //   else to a new position: repeated edits of one value (Rules.MergeKey) merge into the last position while it is
    //     the end of its line and has no bookmark (one record, one undo step, every move kept); a line left behind
    //     (the old redo line) has its end bookmarked, so it stays on the map; Free play beginning bookmarks the
    //     position it began from.
    public MoveResult Apply(Move move)
    {
        var result = Rules.Apply(Current, move);
        if (!result.Accepted)
            return result;
        var before = _current;
        if (before.Children.FirstOrDefault(child => SameState(child.State, result.State)) is Position existing)
        {
            MarkDivergence(before, existing);
            before.Next = existing;
            _current = existing;
            return result;
        }
        if (_gameEnd is not null && !_gameLine.Contains(before) && GamePositionWith(result.State) is Position rejoined)
        {
            before.Bookmark ??= NewBookmark(before); // the side line so far stays on the map
            MakeCurrent(rejoined);
            return result;
        }
        if (Rules.MergeKey(move) is string key && before.Move is Move last && Rules.MergeKey(last) == key
            && before.Bookmark is null && before.Children.Count == 0 && before.Parent is Position parent && !_gameLine.Contains(before))
        {
            parent.Children.Remove(before);
            _positions.Remove(before.Id);
            var merged = Make(parent, before.Moves.Append(move).ToList(), result.State, before.Events.Concat(result.Events).ToList());
            parent.Children.Add(merged);
            parent.Next = merged;
            _current = merged;
            return result;
        }
        if (NextOf(before) is Position leftBehind)
            LineEnd(leftBehind).Bookmark ??= NewBookmark(LineEnd(leftBehind));
        if (!before.State.IsPlanning && result.State.IsPlanning && !result.State.Options.Calculator)
            before.Bookmark ??= NewBookmark(before);
        var made = Make(before, new[] { move }, result.State, result.Events);
        MarkDivergence(before, made);
        before.Children.Add(made);
        before.Next = made;
        _current = made;
        if (AutomaticName(before.State, made.State) is string name)
            made.Bookmark = new Bookmark(made.Id, Numbered(name, numberFirst: false));
        return result;
    }

    // ---------- The game line ----------

    // The line of the game being followed (from a save, an import, or "Mark as game line"): the player's own moves follow
    // it while they match it, and leaving it shows (GameLineStatus).
    public bool HasGameLine => _gameEnd is not null;

    // Whether the active line (back to the start and on to its end) is the game line, so marking it changes nothing.
    public bool ActiveLineIsGameLine => _gameEnd is not null && LineEnd(_current) == _gameEnd;

    // The active line becomes the game line (its end, as far as it goes).
    public void MarkGameLine()
    {
        _gameEnd = LineEnd(_current);
        RefreshGameLine();
    }

    // Where the player is with respect to the game line: null without one; otherwise whether they are on it, and if not,
    // how many moves since they left it and the position they left it from.
    public GameLineStatus? GameLineStatus
    {
        get
        {
            if (_gameEnd is null)
                return null;
            var moves = 0;
            var p = _current;
            while (!_gameLine.Contains(p))
            {
                moves++;
                p = p.Parent!;
            }
            return new GameLineStatus(moves == 0, moves, p.Id);
        }
    }

    // Back to where the game line was left (nothing if on it). False without a game line.
    public bool ReturnToGameLine()
    {
        if (GameLineStatus is not { } status)
            return false;
        if (!status.OnLine)
            MakeCurrent(_positions[status.LeftFrom]);
        return true;
    }

    // Leaving the game line from one of its positions: that position is bookmarked (if it isn't already), so it is clear
    // on the bookmark map where the game line was left and where Back to game line goes.
    private void MarkDivergence(Position from, Position to)
    {
        if (_gameEnd is not null && _gameLine.Contains(from) && !_gameLine.Contains(to))
            from.Bookmark ??= NewBookmark(from);
    }

    private void RefreshGameLine()
    {
        _gameLine = new HashSet<Position>();
        for (var p = _gameEnd; p is not null; p = p.Parent)
            _gameLine.Add(p);
    }

    private Position? GamePositionWith(GameState state) =>
        _gameLine.FirstOrDefault(p => SameState(p.State, state));

    // Cheap fields first: the full comparison is deep.
    private static bool SameState(GameState a, GameState b) =>
        a.Round == b.Round && a.Phase == b.Phase && a.ActivePlayer == b.ActivePlayer && a.Equals(b);

    // Where redo goes: on the game line, along it; elsewhere the line used most recently.
    private Position? NextOf(Position p) =>
        _gameLine.Contains(p) && p.Children.FirstOrDefault(_gameLine.Contains) is Position onLine ? onLine : p.Next;

    private Position LineEnd(Position from)
    {
        var p = from;
        while (NextOf(p) is Position next)
            p = next;
        return p;
    }

    // That position current, and the lines through it the ones redo follows on the way there.
    private void MakeCurrent(Position target)
    {
        for (var p = target; p.Parent is Position parent; p = parent)
            parent.Next = p;
        _current = target;
    }

    public bool Undo()
    {
        if (_current.Parent is not Position before)
            return false;
        _current = before;
        return true;
    }

    public bool Redo()
    {
        if (NextOf(_current) is not Position after)
            return false;
        _current.Next = after;
        _current = after;
        return true;
    }

    // Bookmarks the current position (named by how far the game has got); null if it already has one.
    public Bookmark? AddBookmark()
    {
        if (!CanBookmark)
            return null;
        _current.Bookmark = NewBookmark(_current);
        return _current.Bookmark;
    }

    // Removes the bookmark, and whatever only led to it. False for Session start, or a bookmark no longer there.
    public bool DeleteBookmark(Bookmark bookmark)
    {
        if (Find(bookmark) is not Position position || position == _root)
            return false;
        position.Bookmark = null;
        Prune();
        return true;
    }

    // Gives a bookmark a new name (trimmed; names needn't be unique: the tree tells them apart). Null for Session start,
    // an empty name, or a bookmark no longer there; otherwise the renamed bookmark (Bookmark is a record, so the old one
    // no longer finds it).
    public Bookmark? RenameBookmark(Bookmark bookmark, string name)
    {
        if (Find(bookmark) is not Position position || position == _root || string.IsNullOrWhiteSpace(name))
            return null;
        position.Bookmark = bookmark with { Name = name.Trim() };
        return position.Bookmark;
    }

    // Makes the bookmarked position current, and its line the active one. Leaving the active line, its end is
    // bookmarked first (if it isn't already), so nothing on it is lost. False for a bookmark no longer there.
    public bool JumpTo(Bookmark bookmark)
    {
        if (Find(bookmark) is not Position target)
            return false;
        List<Position> active = ActiveLine();
        if (!active.Contains(target))
        {
            Position end = active[^1];
            end.Bookmark ??= NewBookmark(end);
        }
        MakeCurrent(target);
        Prune();
        return true;
    }

    // The game log: every position on the active line, Session start first, the ones after the current position
    // included (they can be redone).
    public ValueList<LogEntry> Log() => new(ActiveLine().Select(Entry));

    private LogEntry Entry(Position p) => new(p.Id, p.Move, p.Parent?.State, p.State, p.Events, p.Bookmark, _gameLine.Contains(p));

    // The notes (see NoteEntry). A line's own next position: along the game line on it; without a game line, along the
    // active line on it; anywhere else the first one made (so lines keep their places as the player moves about).
    // Every other position after it starts a line of its own: under it, the one the active line takes first (listed
    // whole), then the others (their first records only), then its own line carries on.
    public ValueList<NoteEntry> Notes()
    {
        List<Position> active = ActiveLine();
        var onActive = new HashSet<Position>(active);
        var activeNext = new Dictionary<Position, Position>();
        for (var i = 0; i + 1 < active.Count; i++)
            activeNext[active[i]] = active[i + 1];
        Position? Continuation(Position p) =>
            _gameEnd is not null && _gameLine.Contains(p) ? p.Children.FirstOrDefault(_gameLine.Contains)
            : _gameEnd is null && activeNext.TryGetValue(p, out Position? next) ? next
            : p.Children.FirstOrDefault();

        var notes = new List<NoteEntry>();
        void Line(Position start, int depth, int number)
        {
            for (Position? p = start; p is not null; p = Continuation(p), number++)
            {
                notes.Add(new NoteEntry(Entry(p), depth, depth > 0 && p == start, onActive.Contains(p), number));
                Position? next = Continuation(p);
                List<Position> branches = p.Children.Where(c => c != next).ToList();
                foreach (Position taken in branches.Where(onActive.Contains))
                    Line(taken, depth + 1, number + 1);
                foreach (Position other in branches.Where(b => !onActive.Contains(b)))
                    notes.Add(new NoteEntry(Entry(other), depth + 1, true, false, number + 1));
            }
        }
        Line(_root, 0, 0);
        return new ValueList<NoteEntry>(notes);
    }

    // Where the current position is in Log().
    public int LogIndex => ActiveLine().IndexOf(_current);

    // Makes a position current (the log's and the notes' arrows), as undoing or redoing all the way there would. The
    // position left is bookmarked first if it isn't already, so it is easy to come back to. On the active line, the
    // line stays as it is; elsewhere (the notes: another line), the line through it becomes the active one, the old
    // active line's end bookmarked too (as JumpTo). False for a position no longer there.
    public bool JumpToPosition(int position)
    {
        if (!_positions.TryGetValue(position, out Position? target))
            return false;
        if (target == _current)
            return true;
        _current.Bookmark ??= NewBookmark(_current);
        List<Position> active = ActiveLine();
        bool elsewhere = !active.Contains(target);
        if (elsewhere)
            active[^1].Bookmark ??= NewBookmark(active[^1]);
        MakeCurrent(target);
        if (elsewhere)
            Prune();
        return true;
    }

    // Bookmarks a position (the log's and the notes' bookmark buttons), or removes its bookmark (as DeleteBookmark).
    // False for Session start (always bookmarked) or a position no longer there.
    public bool ToggleBookmark(int position)
    {
        if (!_positions.TryGetValue(position, out Position? target) || target == _root)
            return false;
        if (target.Bookmark is Bookmark bookmark)
            return DeleteBookmark(bookmark);
        target.Bookmark = NewBookmark(target);
        return true;
    }

    // What the bookmark map shows (see HistoryMap).
    public HistoryMap Map()
    {
        List<Position> active = ActiveLine();
        var onActive = new HashSet<Position>(active);
        Position end = active[^1];
        bool IsVertex(Position p) => p.Bookmark is not null || p == end;

        // Each vertex's edge back to the nearest vertex above it, and the vertices next after it along its lines
        // (in tree order: children in the order made).
        var from = new Dictionary<Position, Position>();
        var below = new Dictionary<Position, List<Position>>();
        var stack = new Stack<(Position Position, Position? Above)>();
        stack.Push((_root, null));
        while (stack.Count > 0)
        {
            var (p, above) = stack.Pop();
            Position? vertexHere = above;
            if (IsVertex(p))
            {
                below[p] = new List<Position>();
                if (above is not null)
                {
                    from[p] = above;
                    below[above].Add(p);
                }
                vertexHere = p;
            }
            for (var i = p.Children.Count - 1; i >= 0; i--)
                stack.Push((p.Children[i], vertexHere));
        }

        // The list: each vertex, then the branches leaving it (one step further in), then its own line carries on.
        var vertices = new List<MapVertex>();
        var todo = new Stack<(Position Vertex, int Indent)>();
        todo.Push((_root, 0));
        while (todo.Count > 0)
        {
            var (v, indent) = todo.Pop();
            vertices.Add(new MapVertex(v.Id, v.Bookmark, from.TryGetValue(v, out Position? f) ? f.Id : null, onActive.Contains(v), indent,
                _gameLine.Contains(v)));
            Position? main = NextVertex(v, IsVertex);
            if (main is not null)
                todo.Push((main, indent));
            List<Position> branches = below[v];
            for (var i = branches.Count - 1; i >= 0; i--)
                if (branches[i] != main)
                    todo.Push((branches[i], indent + 1));
        }

        if (IsVertex(_current))
            return new HistoryMap(new ValueList<MapVertex>(vertices), _current.Id, null);
        Position at = _current.Parent!;
        while (at.Bookmark is null) // Session start always has one
            at = at.Parent!;
        return new HistoryMap(new ValueList<MapVertex>(vertices), at.Id, NextVertex(_current, IsVertex)!.Id);
    }

    // ---------- Saving and loading (SaveGame) ----------

    // Every position (Session start first, each after its parent), with the moves that made it, its bookmark's name and
    // where redo goes from it; the current position, the game line's end, and the bookmark numbering.
    public HistoryData Export() => new(
        _positions.Values.OrderBy(p => p.Id).Select(p => new PositionData(p.Id, p.Parent?.Id, p.Moves.ToArray(), p.Bookmark?.Name, p.Next?.Id)).ToList(),
        _current.Id, _gameEnd?.Id, new Dictionary<string, int>(_used));

    // Rebuilt by replaying every position's moves from `initial`. A move the rules now refuse stops that position and
    // everything after it; the first such is reported (Problem), the rest is kept. Positions keep their numbers.
    public static History Import(HistoryData data, GameState initial, out LoadProblem? problem)
    {
        problem = null;
        var history = new History(initial);
        foreach (PositionData saved in data.Positions.Where(p => p.Parent is not null).OrderBy(p => p.Id))
        {
            if (!history._positions.TryGetValue(saved.Parent!.Value, out Position? parent))
                continue; // its parent could not be replayed
            GameState state = parent.State;
            var events = new List<GameEvent>();
            string? refused = null;
            foreach (Move move in saved.Moves)
            {
                MoveResult result = Rules.Apply(state, move);
                if (!result.Accepted)
                {
                    refused = result.Issues.FirstOrDefault(issue => issue.Severity == Severity.Error)?.Message ?? "refused";
                    problem ??= new LoadProblem(saved.Id, move, parent.State, refused);
                    break;
                }
                state = result.State;
                events.AddRange(result.Events);
            }
            if (refused is not null)
                continue;
            var position = new Position(saved.Id, parent, saved.Moves, state, events);
            history._positions[saved.Id] = position;
            parent.Children.Add(position);
            if (saved.Bookmark is string name)
                position.Bookmark = new Bookmark(saved.Id, name);
        }
        history._made = history._positions.Keys.Max() + 1;
        foreach (PositionData saved in data.Positions)
            if (saved.Next is int next && history._positions.TryGetValue(saved.Id, out Position? from)
                && history._positions.TryGetValue(next, out Position? to) && to.Parent == from)
                from.Next = to;
        foreach (var (series, last) in data.Used)
            history._used[series] = last;

        // The current position and the game line's end, or the nearest of their ancestors that could be replayed.
        Position Nearest(int id)
        {
            var parents = data.Positions.ToDictionary(p => p.Id, p => p.Parent);
            for (int? at = id; at is int known; at = parents.GetValueOrDefault(known))
                if (history._positions.TryGetValue(known, out Position? found))
                    return found;
            return history._root;
        }
        if (data.GameEnd is int gameEnd)
        {
            history._gameEnd = Nearest(gameEnd);
            history.RefreshGameLine();
        }
        history.MakeCurrent(Nearest(data.Current));
        return history;
    }

    // ---------- Inside ----------

    private Position Make(Position? parent, IReadOnlyList<Move> moves, GameState state, IReadOnlyList<GameEvent> events)
    {
        var position = new Position(_made++, parent, moves, state, events);
        _positions[position.Id] = position;
        return position;
    }

    private Position? Find(Bookmark bookmark) =>
        _positions.TryGetValue(bookmark.Position, out Position? position) && position.Bookmark == bookmark ? position : null;

    // Session start to the current position, then each redo step to the end.
    private List<Position> ActiveLine()
    {
        var line = new List<Position>();
        for (Position? p = _current; p is not null; p = p.Parent)
            line.Add(p);
        line.Reverse();
        for (Position? p = NextOf(_current); p is not null; p = NextOf(p))
            line.Add(p);
        return line;
    }

    // The first vertex after this position along its redo line (null at the end of a line). Every line ends at a
    // vertex: the active one at its end, the others at their last bookmark.
    private Position? NextVertex(Position position, Func<Position, bool> isVertex)
    {
        for (Position? p = NextOf(position); p is not null; p = NextOf(p))
            if (isVertex(p))
                return p;
        return null;
    }

    // Keeps the active line, and elsewhere only positions with a bookmark on them or after them. A position whose
    // redo line was dropped redoes along its most recent remaining one.
    private void Prune()
    {
        var keep = new HashSet<Position>(ActiveLine());
        keep.UnionWith(_gameLine); // the game line is never dropped
        var all = new List<Position>(); // every position, each before everything after it
        var stack = new Stack<Position>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            Position p = stack.Pop();
            all.Add(p);
            foreach (Position child in p.Children)
                stack.Push(child);
        }
        for (var i = all.Count - 1; i >= 0; i--) // everything after a position is decided before it
            if (all[i].Bookmark is not null || all[i].Children.Any(keep.Contains))
                keep.Add(all[i]);
        foreach (Position p in all)
        {
            if (!keep.Contains(p))
            {
                _positions.Remove(p.Id);
                continue;
            }
            p.Children.RemoveAll(child => !keep.Contains(child));
            if (p.Next is not null && !keep.Contains(p.Next))
                p.Next = p.Children.Count > 0 ? p.Children[^1] : null;
        }
    }

    // "Game start" when Start is pressed; "R3_start" the first time a line reaches round 3's actions (after income).
    private static string? AutomaticName(GameState before, GameState after) =>
        after.Phase is Phase.PlacingDwellings or Phase.SettingUp && before.Phase < Phase.PlacingDwellings ? GameStart
        : after.Phase == Phase.Playing && (before.Phase != Phase.Playing || before.Round != after.Round) ? $"R{after.Round}_start"
        : null;

    // How far the game has got: Setup before round 1; R1 to R6 during a round and its round-end spades; End once over.
    private static string Stage(GameState state) => state.Phase switch
    {
        Phase.Playing or Phase.RoundEndSpades => $"R{state.Round}",
        Phase.OrderingTurns when state.Round > 0 => $"R{state.Round}",
        Phase.Finished => "End",
        _ => "Setup",
    };

    private Bookmark NewBookmark(Position position) => new(position.Id, Numbered(Stage(position.State), numberFirst: true));

    // The next name in a series, never reusing a number: numberFirst, "R3_1", "R3_2", ...; otherwise "R3_start",
    // "R3_start_2", ...
    private string Numbered(string series, bool numberFirst)
    {
        int n = _used.GetValueOrDefault(series) + 1;
        _used[series] = n;
        return n == 1 && !numberFirst ? series : $"{series}_{n}";
    }
}
