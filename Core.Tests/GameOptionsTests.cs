using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// What a game includes (GameOptions, chosen with ChooseGame), and free actions only outside an action.
public class GameOptionsTests
{
    // ThreePlayerStart (Witches, Giants, Mermaids) with these options.
    private static GameState ThreePlayersWith(GameOptions options) =>
        Play(Concat(new Move[] { new ChooseGame(GameKind.TerraMystica, options) }, ThreePlayerStart.Skip(1).ToArray()));

    [Fact]
    public void Everything_is_in_the_game_unless_left_out()
    {
        var s = Play(ChooseTM);
        Assert.Equal(GameOptions.All, s.Options);
        Assert.All(s.KeyBank, stack => Assert.NotEmpty(stack));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_variable_turn_order_the_first_player_moves_to_the_end_each_round(bool variable)
    {
        var s = ThreePlayersWith(new GameOptions(VariableTurnOrder: variable));
        Assert.Equal(ValueList.Of(W, G, M), s.TurnOrder);
        // Passing in turn order: the pass order is the turn order, so only the rotation tells them apart.
        s = PlayFrom(s, PassAndEnd(W, G, M));
        Assert.Equal(2, s.Round);
        Assert.Equal(variable ? ValueList.Of(W, G, M) : ValueList.Of(G, M, W), s.TurnOrder);
        Assert.Equal(s.TurnOrder[0], s.ActivePlayer);
    }

    [Fact]
    public void Without_the_mini_expansion_its_key_tokens_round_tile_and_pass_tile_are_left_out()
    {
        var s = Play(new ChooseGame(GameKind.TerraMystica, new GameOptions(MiniExpansion: false, Calculator: true)));
        for (var id = 0; id < KeyTiles.Count; id++)
            Assert.Equal(KeyTiles.All[id].MiniExpansion ? 0 : KeyTiles.All[id].Copies, s.KeyBank[id].Count);
        Assert.Equal(3, KeyTiles.All.Count(token => token.MiniExpansion));

        var tiles = PlayFrom(s, new AddPlayer(W), new ConfirmFactions(), new ChooseMap(MapId.Original));
        Assert.Equal("That round tile is from the mini-expansion, which is not in this game.",
            Assert.Single(Rules.Apply(tiles, new ChooseRoundTile(RoundTiles.Count - 1)).Issues).Message);
        tiles = PlayFrom(tiles, new ChooseRoundTile(0), new ChooseRoundTile(1), new ChooseRoundTile(2), new ChooseRoundTile(3),
            new ChooseRoundTile(4), new ChooseRoundTile(5), new ConfirmRoundTiles());
        Assert.Equal("That pass tile is from the mini-expansion, which is not in this game.",
            Assert.Single(Rules.Apply(tiles, new AddPassTile(PassTiles.Count - 1)).Issues).Message);
        Assert.True(Rules.Apply(tiles, new AddPassTile(0)).Accepted);
    }

    [Fact]
    public void Without_Fire_and_Ice_its_factions_cannot_be_chosen()
    {
        var s = Play(new ChooseGame(GameKind.TerraMystica, new GameOptions(FireAndIce: false)));
        Assert.Equal("The Ice Maidens are from Fire & Ice, which is not in this game.",
            Assert.Single(Rules.Apply(s, new AddPlayer(Faction.IceMaidens)).Issues).Message);
        Assert.True(Rules.Apply(s, new AddPlayer(W)).Accepted);
    }

    [Fact]
    public void Free_actions_wait_until_the_action_is_finished()
    {
        // The Witches turn 10 (Lake) into Forest: the dwelling may follow, so the action isn't finished yet.
        var transformed = PlayFrom(Play(TwoPlayerStart), new TransformTerrain(W, 10, Terrain.Forest));
        Assert.True(transformed.SpadesPending);
        Assert.Equal("The Witches must finish spending their free spades first (or press Done).",
            Assert.Single(Rules.Apply(transformed, new BurnPower(W)).Issues).Message);
        Assert.False(Rules.Apply(transformed, new ConvertWorkerToCoin(W)).Accepted);

        var done = PlayFrom(transformed, new FinishSpades(W));
        Assert.True(Rules.Apply(done, new BurnPower(W)).Accepted);
        Assert.True(Rules.Apply(Play(TwoPlayerStart), new BurnPower(W)).Accepted); // before the action too
    }
}
