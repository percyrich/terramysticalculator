using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// The game log's text (GameLog). Base map rows A (0–12, no river) and B (13–24: D R R P S R R D S R R D), then C from 25
// (R R S R M …).
public class GameLogTests
{
    [Theory]
    [InlineData(0, "A1")]
    [InlineData(12, "A13")]
    [InlineData(13, "B1")]
    [InlineData(14, "r0")]
    [InlineData(15, "r1")]
    [InlineData(16, "B2")]
    [InlineData(25, "r6")]
    [InlineData(27, "C1")]
    [InlineData(29, "C2")]
    public void Hexes_are_named_as_on_the_board(int hex, string name) =>
        Assert.Equal(name, GameLog.HexName(Play(SoloStart), hex));

    [Fact]
    public void Each_record_says_what_was_done()
    {
        var history = new History(GameState.Initial);
        history.Apply(ChooseTM);
        history.Apply(new AddPlayer(G));
        var log = history.Log();

        Assert.Equal(History.SessionStart, GameLog.Describe(log[0]));
        Assert.Equal("Terra Mystica: Lenient mode, variable turn order, mini-expansions, Fire & Ice, special landscapes", GameLog.Describe(log[1]));
        Assert.Equal("Giants join the game", GameLog.Describe(log[2]));
    }

    [Fact]
    public void A_run_of_conversions_is_one_line_rewritten_as_it_grows()
    {
        var s0 = Play(SoloStart);
        var r1 = Rules.Apply(s0, new BurnPower(G));
        var r2 = Rules.Apply(r1.State, new BurnPower(G));
        var r3 = Rules.Apply(r2.State, new ConvertPowerToCoin(G));
        var entries = new[]
        {
            new LogEntry(1, new BurnPower(G), s0, r1.State, r1.Events, null),
            new LogEntry(2, new BurnPower(G), r1.State, r2.State, r2.Events, null),
            new LogEntry(3, new ConvertPowerToCoin(G), r2.State, r3.State, r3.Events, null),
        };
        Assert.Equal(new GameLog.LogLine(2, "Giants burn 2pw. Giants do conversions: 1pw → 1c"),
            Assert.Single(GameLog.Lines(entries, 3, GameLog.Describe)));
        Assert.Equal(new GameLog.LogLine(1, "Giants burn 2pw"), GameLog.Lines(entries, 2, GameLog.Describe)[0]); // the last one undone
        Assert.Equal(2, GameLog.Lines(entries, 2, GameLog.Describe).Count);
    }

    [Fact]
    public void An_action_says_what_it_cost()
    {
        var s = Play(SoloStart);
        var result = Rules.Apply(s, UpgradeTo(G, PieceKind.TradingPost, 0, GiantsStartA));
        Assert.Matches(@"\(2w \d+c\)$", GameLog.Describe(s, UpgradeTo(G, PieceKind.TradingPost, 0, GiantsStartA), result.Events, result.State)
            .Split(" · ")[0]);
    }

    [Fact]
    public void Points_scored_follow_the_action()
    {
        var events = new List<GameEvent>
        {
            new VictoryPointsGained(G, 2, 22), new VictoryPointsGained(G, 3, 25), new VictoryPointsGained(W, 1, 21),
            new RoundEnded(1),
        };
        var s = Play(SoloStart);
        string text = GameLog.Describe(s, new EndTurn(G), events, s);
        Assert.Equal("Giants end their turn · +5 VP · Witches +1 VP · round 1 ends", text);
    }

    [Fact]
    public void Results_say_where_points_came_from_and_what_else_was_gained()
    {
        var s = Play(SoloStart);
        var events = new List<GameEvent>
        {
            new PriestPlaced(new Piece(G, PieceKind.Priest, 0), Track.Air, 0, 3, 6),
            new TrackAdvanced(G, Track.Air, 3, 6),                       // the action itself: not repeated
            new PowerGained(W, PowerSource.Track, 1, s[G].Power, s[G].Power),
            new VictoryPointsGained(G, 2, 22, new PointsArea.RoundTile()),
            new VictoryPointsGained(G, 3, 25, new PointsArea.Favour(5)),
            new VictoryPointsGained(G, 1, 26, new PointsArea.RoundTile()), // added to the first round bonus
        };
        string text = GameLog.Describe(s, new PlacePriest(new Piece(G, PieceKind.Priest, 0), Track.Air, 0), events, s);
        Assert.Equal("Giants send a priest to Air (+3) · Witches gain 1pw · +3 VP (round bonus) · +3 VP (favour)", text);
    }

    [Fact]
    public void Resource_amounts_are_written_short_as_in_conversions()
    {
        Assert.Equal("Giants pick a pass tile (Income: 2w; Pass: +4 VP per stronghold) · Giants income: 5w · Selkies income: 3w 1p",
            GameLog.Abbreviated("Giants pick a pass tile (Income: 2 workers; Pass: +4 VP per stronghold) · Giants income: 5 workers · Selkies income: 3 workers, 1 priest"));
        Assert.Equal("Nomads income: 3c 1w 2pw, 1 VP · Witches remove 2 power tokens · Witches gain 1pw",
            GameLog.Abbreviated("Nomads income: 3 coins, 1 worker, 2 power, 1 VP · Witches remove 2 power tokens · Witches gain 1 power"));
    }
}
