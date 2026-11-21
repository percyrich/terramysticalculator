using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class HistoryTests
{
    private const Faction N = Faction.Nomads, Dk = Faction.Darklings;

    // The map's vertices by name, in list order; the unbookmarked end of the active line is "end".
    private static string[] Names(History history) =>
        history.Map().Vertices.Select(v => v.Bookmark?.Name ?? "end").ToArray();

    private static MapVertex Vertex(History history, string name) =>
        history.Map().Vertices.Single(v => (v.Bookmark?.Name ?? "end") == name);

    private static Bookmark Named(History history, string name) => Vertex(history, name).Bookmark!;

    [Fact]
    public void The_log_lists_the_active_line_with_moves_events_and_bookmarks()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        history.Undo();

        var log = history.Log();
        Assert.Equal(4, log.Count); // Session start, the game, the Giants, the Witches (can be redone)
        Assert.Equal(2, history.LogIndex);
        Assert.Null(log[0].Move);
        Assert.Equal(History.SessionStart, log[0].Bookmark?.Name);
        Assert.Equal(new AddPlayer(W), log[3].Move);
        Assert.Same(log[2].After, log[3].Before);
    }

    [Fact]
    public void Bookmarks_can_be_renamed_but_not_Session_start_or_to_nothing()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        Bookmark first = history.AddBookmark()!;
        history.Apply(new AddPlayer(G));
        Bookmark second = history.AddBookmark()!;

        Bookmark renamed = history.RenameBookmark(first, "  Before the Giants ")!;
        Assert.Equal("Before the Giants", renamed.Name);
        Assert.Contains("Before the Giants", Names(history));
        Assert.Null(history.RenameBookmark(first, "Again"));           // the old record no longer finds it
        Assert.NotNull(history.RenameBookmark(second, "Before the Giants")); // names needn't be unique
        Assert.Equal(2, Names(history).Count(name => name == "Before the Giants"));
        Assert.Null(history.RenameBookmark(renamed, "   "));
        Assert.Null(history.RenameBookmark(Named(history, History.SessionStart), "Start"));
        Assert.True(history.JumpTo(renamed));                          // the renamed bookmark works as any other
    }

    [Fact]
    public void Jumping_along_the_log_bookmarks_the_position_left()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        var log = history.Log();

        Assert.True(history.JumpToPosition(log[1].Position));
        Assert.Same(log[1].After, history.Current);
        Assert.Equal(4, history.Log().Count); // nothing dropped: the rest can be redone
        Assert.NotNull(history.Log()[3].Bookmark); // where the jump came from
        Assert.True(history.Redo());
        Assert.Same(log[2].After, history.Current);

        // From a bookmarked position nothing new is bookmarked (log[3] was bookmarked by the first jump).
        Assert.True(history.JumpToPosition(log[3].Position));
        int bookmarks = history.Log().Count(entry => entry.Bookmark is not null);
        Assert.True(history.JumpToPosition(log[0].Position));
        Assert.Equal(bookmarks, history.Log().Count(entry => entry.Bookmark is not null));
    }

    [Fact]
    public void The_notes_list_the_game_line_whole_and_open_the_side_line_being_followed()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        history.MarkGameLine();
        history.Undo();
        history.Undo();                   // just after choosing the game
        history.Apply(new AddPlayer(Dk)); // a side line: the Darklings instead of the Giants
        history.Apply(new AddPlayer(W));

        static (Move? Move, int Depth, bool Opens, bool Active, int Number)[] Rows(History h) =>
            h.Notes().Select(n => (n.Entry.Move, n.Depth, n.Opens, n.Active, n.Number)).ToArray();
        Move choose = history.Log()[1].Move!;
        Assert.Equal(new (Move?, int, bool, bool, int)[]
        {
            (null, 0, false, true, 0),
            (choose, 0, false, true, 1),
            (new AddPlayer(Dk), 1, true, true, 2), // the side line, whole, under where it parts from the game line
            (new AddPlayer(W), 1, false, true, 3),
            (new AddPlayer(G), 0, false, false, 2), // the game line carries on below it
            (new AddPlayer(W), 0, false, false, 3),
        }, Rows(history));

        // Onto the game line from the notes: the side line is now only its first record (an Alternate).
        int giants = history.Notes().Single(n => n.Entry.Move == new AddPlayer(G)).Entry.Position;
        Assert.True(history.JumpToPosition(giants));
        Assert.True(history.GameLineStatus!.OnLine);
        Assert.Equal(new (Move?, int, bool, bool, int)[]
        {
            (null, 0, false, true, 0),
            (choose, 0, false, true, 1),
            (new AddPlayer(Dk), 1, true, false, 2),
            (new AddPlayer(G), 0, false, true, 2),
            (new AddPlayer(W), 0, false, true, 3),
        }, Rows(history));
        Assert.Equal(1, history.Map().Vertices.Count(v => !v.Active)); // the side line kept: its end bookmarked
    }

    [Fact]
    public void The_log_adds_and_removes_bookmarks_but_never_session_start()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        history.Apply(new AddPlayer(G));
        var log = history.Log();

        Assert.True(history.ToggleBookmark(log[1].Position));
        Assert.NotNull(history.Log()[1].Bookmark);
        Assert.True(history.ToggleBookmark(log[1].Position));
        Assert.Null(history.Log()[1].Bookmark);
        Assert.Equal(3, history.Log().Count); // the active line keeps its moves

        Assert.False(history.ToggleBookmark(log[0].Position));
        Assert.NotNull(history.Log()[0].Bookmark);
        Assert.False(history.ToggleBookmark(999));
    }

    [Fact]
    public void Undo_returns_the_exact_previous_state()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        var before = history.Current;

        history.Apply(new AddPlayer(W));
        Assert.True(history.Undo());

        Assert.Same(before, history.Current);
    }

    [Fact]
    public void Undo_reaches_the_empty_setup_and_then_stops()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseCalculator);
        history.Apply(new AddPlayer(G));
        history.Apply(new ConfirmFactions());

        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.Same(GameState.Initial, history.Current);
        Assert.False(history.CanUndo);
        Assert.False(history.Undo());
    }

    [Fact]
    public void Redo_reapplies_an_undone_move()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        var after = history.Current;

        history.Undo();
        Assert.True(history.Redo());
        Assert.Same(after, history.Current);
    }

    [Fact]
    public void A_new_move_after_undo_discards_redo()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.Undo();
        history.Apply(new AddPlayer(W));

        Assert.False(history.CanRedo);
        Assert.True(history.Current.IsInGame(W));
        Assert.False(history.Current.IsInGame(G));
    }

    [Fact]
    public void Refused_moves_do_not_enter_history()
    {
        var history = new History(GameState.Initial);
        var result = history.Apply(new ConfirmFactions()); // refused: no factions yet

        Assert.False(result.Accepted);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Identical_positions_reached_separately_are_equal()
    {
        // Guards ValueList's by-contents equality, all the way down through nested lists.
        var moves = new Move[] { GetPriest(W), Place(W, 0, Track.Fire, 0), End(W), Build(G, 2, 4) };
        var a = PlayCharged(TwoPlayerStart, moves);
        var b = PlayCharged(TwoPlayerStart, moves);

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
    }

    // ---------- Lines and bookmarks ----------

    [Fact]
    public void The_map_lists_each_line_in_order_with_its_branches_under_the_bookmark_they_leave_from()
    {
        // Session start → Setup_1 → Setup_3 → end, with Setup_2 branching off Setup_1 and Setup_4 off Setup_3.
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.AddBookmark();                // Setup_1
        history.Apply(new AddPlayer(W));
        history.AddBookmark();                // Setup_2
        history.Undo();
        history.Apply(new AddPlayer(M));
        history.AddBookmark();                // Setup_3
        history.Apply(new AddPlayer(N));
        history.AddBookmark();                // Setup_4
        history.Undo();
        history.Apply(new AddPlayer(Dk));     // the end of the active line, not bookmarked

        var map = history.Map();
        var at = map.Vertices.Select(v => v.Position).ToArray();
        Assert.Equal(new[] { "Session start", "Setup_1", "Setup_2", "Setup_3", "Setup_4", "end" }, Names(history));
        Assert.Equal(new[] { 0, 0, 1, 0, 1, 0 }, map.Vertices.Select(v => v.Indent));
        Assert.Equal(new[] { true, true, false, true, false, true }, map.Vertices.Select(v => v.Active));
        Assert.Equal(new int?[] { null, at[0], at[1], at[1], at[3], at[3] }, map.Vertices.Select(v => v.From));
        Assert.Equal(at[5], map.At); // on the end
        Assert.Null(map.Towards);
    }

    [Fact]
    public void Between_vertices_the_player_is_on_the_active_edge()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        history.Apply(new AddPlayer(M));
        history.Undo();

        var map = history.Map();
        Assert.Equal(new[] { "Session start", "end" }, Names(history));
        Assert.Equal(map.Vertices[0].Position, map.At);
        Assert.Equal(map.Vertices[1].Position, map.Towards);
        Assert.True(history.CanRedo);

        Bookmark here = history.AddBookmark()!;
        Assert.Equal("Setup_1", here.Name);
        map = history.Map();
        Assert.Equal(here.Position, map.At);
        Assert.Null(map.Towards);
        Assert.False(history.CanBookmark);
        Assert.Null(history.AddBookmark()); // one per position
    }

    [Fact]
    public void A_new_move_keeps_the_old_line_whole_with_its_end_bookmarked()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        history.AddBookmark();                // Setup_1
        history.Apply(new AddPlayer(M));      // past the line's last bookmark: kept, its end bookmarked when left
        history.Undo();
        history.Undo();
        history.Undo();                       // back at Session start
        history.Apply(new AddPlayer(N));

        Assert.False(history.CanRedo);
        Assert.Contains("Setup_2", Names(history));
        Assert.False(Vertex(history, "Setup_2").Active);
        Assert.True(history.JumpTo(Named(history, "Setup_2")));
        Assert.True(history.Current.IsInGame(M));
    }

    [Fact]
    public void Redo_follows_the_line_used_most_recently()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.AddBookmark();                // Setup_1
        history.Undo();
        history.Apply(new AddPlayer(W));
        history.AddBookmark();                // Setup_2
        history.Undo();

        Assert.True(history.Redo());
        Assert.True(history.Current.IsInGame(W));

        Assert.True(history.JumpTo(Named(history, "Setup_1")));
        history.Undo();
        Assert.True(history.Redo());
        Assert.True(history.Current.IsInGame(G));
        Assert.Equal(new[] { "Session start", "Setup_2", "Setup_1" }, Names(history)); // Setup_2 already marked its line's end
    }

    [Fact]
    public void Jumping_along_the_active_line_adds_no_bookmark()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.AddBookmark();                // Setup_1
        history.Apply(new AddPlayer(W));      // the end, not bookmarked

        Assert.True(history.JumpTo(Named(history, "Setup_1")));
        Assert.Equal(new[] { "Session start", "Setup_1", "end" }, Names(history));
        Assert.True(history.CanRedo);

        Assert.True(history.JumpTo(Named(history, "Session start")));
        Assert.False(history.CanUndo);
        Assert.Equal(new[] { "Session start", "Setup_1", "end" }, Names(history));
    }

    [Fact]
    public void Deleting_a_bookmark_collapses_the_map_or_drops_the_line_that_only_led_to_it()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.AddBookmark();                // Setup_1
        history.Apply(new AddPlayer(W));
        history.AddBookmark();                // Setup_2
        history.Undo();
        history.Undo();
        history.Apply(new AddPlayer(M));      // the end

        Bookmark first = Named(history, "Setup_1"), second = Named(history, "Setup_2");
        Assert.True(history.DeleteBookmark(first));
        Assert.Equal(new[] { "Session start", "Setup_2", "end" }, Names(history));
        Assert.Equal(Vertex(history, "Session start").Position, Vertex(history, "Setup_2").From);

        Assert.True(history.DeleteBookmark(second));
        Assert.Equal(new[] { "Session start", "end" }, Names(history));
        Assert.False(history.JumpTo(second)); // its line is gone
        Assert.False(history.DeleteBookmark(Named(history, "Session start")));
    }

    [Fact]
    public void Deleting_a_bookmark_on_the_active_line_keeps_its_moves()
    {
        var history = new History(Play(ChooseTM));
        history.Apply(new AddPlayer(G));
        history.Apply(new AddPlayer(W));
        Bookmark ahead = history.AddBookmark()!;
        history.Undo();

        Assert.True(history.DeleteBookmark(ahead));
        Assert.True(history.Redo());
        Assert.True(history.Current.IsInGame(W));

        Bookmark here = history.AddBookmark()!;
        Assert.True(history.DeleteBookmark(here)); // where the player is: they stay
        Assert.True(history.CanBookmark);
        Assert.True(history.Current.IsInGame(W));
    }

    [Fact]
    public void Game_start_and_each_round_start_are_bookmarked_and_repeats_numbered()
    {
        var history = new History(GameState.Initial);
        foreach (var move in TwoPlayerStart)
            Assert.True(history.Apply(move).Accepted);

        Assert.Equal(new[] { "Session start", "Game start", "R1_start" }, Names(history));
        Assert.False(history.CanBookmark); // the round's start already is

        var reached = history.Current;
        history.Undo();                    // the last pass tile pick again: the same position, so the same line
        history.Apply(TwoPlayerStart[^1]);
        Assert.Same(reached, history.Current);
        Assert.Equal(new[] { "Session start", "Game start", "R1_start" }, Names(history));

        history.Undo();                    // another pick: a second line to round 1
        history.Apply(new PickPassTile(W, 0));
        Assert.Equal(new[] { "Session start", "Game start", "R1_start", "R1_start_2" }, Names(history));
        Assert.False(Vertex(history, "R1_start").Active);
        Assert.True(Vertex(history, "R1_start_2").Active);
    }

    [Fact]
    public void Bookmark_names_follow_the_game_and_are_never_reused()
    {
        var history = new History(GameState.Initial);
        foreach (var move in TwoPlayerStart)
            history.Apply(move);
        history.Apply(new ConvertWorkerToCoin(W));
        Assert.Equal("R1_1", history.AddBookmark()!.Name);
        history.Apply(new ConvertWorkerToCoin(W));
        Bookmark second = history.AddBookmark()!;
        Assert.Equal("R1_2", second.Name);
        history.DeleteBookmark(second);
        Assert.Equal("R1_3", history.AddBookmark()!.Name);

        var setup = new History(Play(ChooseTM));
        setup.Apply(new AddPlayer(G));
        Assert.Equal("Setup_1", setup.AddBookmark()!.Name);
    }
}
