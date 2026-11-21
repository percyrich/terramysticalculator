using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Saving and loading (SaveGame), and the game line (History.MarkGameLine).
public class SaveGameTests
{
    private static History Played(params Move[] moves)
    {
        var history = new History(GameState.Initial);
        foreach (var move in moves)
            Assert.True(history.Apply(move).Accepted, move.ToString());
        return history;
    }

    private static History RoundTrip(History history)
    {
        var loaded = SaveGame.FromJson(SaveGame.ToJson(history));
        Assert.Null(loaded.Error);
        Assert.Null(loaded.Problem);
        return loaded.History!;
    }

    [Fact]
    public void A_saved_game_loads_back_to_the_same_positions_bookmarks_and_place()
    {
        var history = Played(TwoPlayerStart);
        history.Apply(new ConvertWorkerToCoin(W));
        history.AddBookmark();
        history.Apply(new ConvertWorkerToCoin(W));
        history.Undo();
        history.Apply(new BurnPower(W));      // a second line from the bookmark
        history.Undo();

        var loaded = RoundTrip(history);
        Assert.Equal(history.Current, loaded.Current);
        Assert.Equal(history.Map(), loaded.Map());
        Assert.Equal(history.Log().Select(e => e.Position), loaded.Log().Select(e => e.Position));
        Assert.Equal(history.CanRedo, loaded.CanRedo);
        Assert.True(loaded.Redo());
        Assert.True(history.Redo());
        Assert.Equal(history.Current, loaded.Current);
    }

    [Fact]
    public void A_custom_map_is_saved_with_the_game()
    {
        var history = Played(Concat(Confirmed(W, G), new Move[] { new ConfirmTurnOrder(), new ChooseCustomMap(MapTests.CustomRows(twelveTop: true)) }));
        var loaded = RoundTrip(history);
        Assert.Equal(MapId.Custom, loaded.Current.ChosenMap);
        Assert.Equal(history.Current.HexTerrain, loaded.Current.HexTerrain);
    }

    [Fact]
    public void Merged_setup_edits_replay_every_move()
    {
        var history = Played(new ChooseGame(GameKind.TerraMystica, GameOptions.All with { SetUpInProgress = true, Calculator = true }),
            new AddPlayer(G), new ConfirmFactions(), new ChooseMap(MapId.Original), new ConfirmRoundTiles(), new StartGame());
        for (var i = 0; i < 7; i++)
            history.Apply(new EditResource(G, Resource.Coins, 1));
        Assert.Equal(22, history.Current[G].Coins);

        Assert.Equal(22, RoundTrip(history).Current[G].Coins);
    }

    [Fact]
    public void A_move_the_rules_now_refuse_stops_the_load_there_and_is_reported()
    {
        var data = Played(TwoPlayerStart).Export();
        data.Positions[^1] = data.Positions[^1] with { Moves = new Move[] { new PickPassTile(W, 8) } }; // not in the bank
        var loaded = History.Import(data, GameState.Initial, out LoadProblem? problem);
        Assert.NotNull(problem);
        Assert.Equal("That pass tile is not in the bank.", problem!.Reason);
        Assert.Equal(Phase.PickingPassTiles, loaded.Current.Phase); // as far as it could go
    }

    [Fact]
    public void Other_files_are_refused()
    {
        Assert.NotNull(SaveGame.FromJson("{ \"Format\": \"something else\" }").Error);
        Assert.NotNull(SaveGame.FromJson("not json").Error);
    }

    [Fact]
    public void Playing_the_game_line_by_hand_stays_on_it_and_leaving_it_shows()
    {
        var history = Played(TwoPlayerStart);
        history.Apply(new ConvertWorkerToCoin(W));
        history.Apply(Build(W, 2, 10));
        Assert.False(history.ActiveLineIsGameLine);
        history.MarkGameLine();
        Assert.True(history.ActiveLineIsGameLine);
        for (var i = 0; i < 2; i++)
            history.Undo();
        Assert.True(history.ActiveLineIsGameLine); // back along it, its end still ahead
        Assert.Equal(new GameLineStatus(true, 0, history.Log()[history.LogIndex].Position), history.GameLineStatus);

        history.Apply(new ConvertWorkerToCoin(W)); // the game's own move: still on the line, nothing new
        Assert.True(history.GameLineStatus!.OnLine);
        Assert.True(history.CanRedo);

        history.Apply(new BurnPower(W));             // off the line
        Assert.Equal(1, history.GameLineStatus!.Moves);
        Assert.False(history.ActiveLineIsGameLine);
        Assert.True(history.ReturnToGameLine());
        Assert.True(history.GameLineStatus!.OnLine);
        Assert.True(history.Redo());                 // along the game line, not the side line just made
        Assert.Equal(PieceKind.Dwelling, history.Current.Map[10]!.Value.Kind);

        var loaded = RoundTrip(history);
        Assert.True(loaded.HasGameLine);
        Assert.True(loaded.GameLineStatus!.OnLine);
    }

    [Fact]
    public void Leaving_the_game_line_bookmarks_where_it_was_left()
    {
        var history = Played(TwoPlayerStart);
        history.Apply(new ConvertWorkerToCoin(W));
        history.Apply(Build(W, 2, 10));
        history.MarkGameLine();
        history.Undo();
        int left = history.Log()[history.LogIndex].Position;
        Assert.Null(history.Log()[history.LogIndex].Bookmark);

        history.Apply(new BurnPower(W)); // off the line
        Assert.Equal(left, history.GameLineStatus!.LeftFrom);
        Assert.NotNull(history.Log().Single(entry => entry.Position == left).Bookmark);

        // Back on the line and off it the same way again: the bookmark stays as it is (one per position).
        Assert.True(history.ReturnToGameLine());
        Bookmark first = history.Log()[history.LogIndex].Bookmark!;
        history.Apply(new BurnPower(W));
        Assert.Equal(first, history.Log().Single(entry => entry.Position == left).Bookmark);
    }
}
