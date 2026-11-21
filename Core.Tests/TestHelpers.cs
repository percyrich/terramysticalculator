using Core;

namespace Core.Tests;

internal static class TestHelpers
{
    // The three factions most tests use. The Witches follow the usual rules most closely; the Giants always need
    // 2 spades and start with Fire 1 and Air 1; the Mermaids start with shipping 1, Water 2 and power 3/9/0.
    public const Faction G = Faction.Giants;
    public const Faction W = Faction.Witches;
    public const Faction M = Faction.Mermaids;

    // Hexes on the base map used throughout the tests (see MapTests for the neighbours relied on).
    public const int GiantsStartA = 5, GiantsStartB = 112;     // Wasteland
    public const int WitchesStartA = 9, WitchesStartB = 60;    // Forest
    public const int MermaidsStartA = 3, MermaidsStartB = 89;  // Lake

    // Applies moves in order from the initial state, failing the test if any is refused.
    // Warnings are allowed (they don't refuse a move).
    public static GameState Play(params Move[] moves) => PlayFrom(GameState.Initial, moves);

    public static GameState PlayFrom(GameState state, params Move[] moves)
    {
        foreach (var move in moves)
        {
            var result = Rules.Apply(state, move);
            Assert.True(result.Accepted, $"Move {move} was refused: {string.Join("; ", result.Issues)}");
            state = result.State;
        }
        return state;
    }

    // Dwellings leave the bank left to right, so a faction's k-th dwelling (starting ones included) is number k.
    public static Move StartAt(Faction f, int number, int hex) =>
        new PlaceStartingDwelling(new Piece(f, PieceKind.Dwelling, number), hex);

    public static Move Build(Faction f, int number, int hex) =>
        new BuildDwelling(new Piece(f, PieceKind.Dwelling, number), hex);

    // Priests are numbered by purchase: the k-th priest a faction buys is number k.
    public static Move Place(Faction f, int number, Track track, int slot) =>
        new PlacePriest(new Piece(f, PieceKind.Priest, number), track, slot);

    public static Move End(Faction f) => new EndTurn(f);

    // An opponent's answer to the power offered when someone builds next to them.
    public static Move Decline(Faction f) => new DeclinePower(f);
    public static Move Accept(Faction f) => new AcceptPower(f);

    // Upgrading to a kind of building: number is that building's own number (its bank slot, left to right).
    public static Move UpgradeTo(Faction f, PieceKind kind, int number, int hex) =>
        new UpgradeBuilding(new Piece(f, kind, number), hex);

    // 5 power from bowl 3 for a priest. Priests are numbered in the order gained.
    public static Move GetPriest(Faction f) => new ConvertPowerToPriest(f);

    // A copy of the state with every player's power set to 0/0/12: enough for two priests each,
    // so tests about priests don't have to earn the power first.
    public static GameState Charged(GameState s) =>
        s with { Players = new ValueList<PlayerState>(s.Players.Select(p => p with { Power = new Power(0, 0, 12) })) };

    // Plays the start, charges everyone's power, then plays the moves.
    public static GameState PlayCharged(Move[] start, params Move[] moves) => PlayFrom(Charged(Play(start)), moves);

    // Passing and ending the turn, keeping the pass tile (see PassKeeping), so tests can pass
    // without caring which tiles are in the bank.
    public static Move[] PassAndEnd(params Faction[] order) =>
        order.SelectMany(f => new Move[] { PassKeeping(f), new EndTurn(f) }).ToArray();

    // Passing without picking a new pass tile (a calculator shortcut; warns in rounds 1-5).
    public static Move PassKeeping(Faction f) => new Pass(f, KeepTile: true);

    // ---------- Setup ----------

    public static readonly Move ChooseTM = new ChooseGame(GameKind.TerraMystica);
    // A solo (Calculator) game with the turn rules of Strict and Lenient (one action per turn, End turn after it), for
    // tests of those rules: Free play taken away.
    public static GameState Ruled(GameState s) => s with { Mode = PlayMode.Lenient, Planning = null };

    public static readonly Move ChooseCalculator = new ChooseGame(GameKind.TerraMystica, GameOptions.All with { Calculator = true });

    // The round tiles the standard starts use, round 1 first. Chosen so that the end of a round never
    // owes transforms, and round 1 scores only strongholds and sanctuaries (5 VP each).
    public static readonly int[] TestRoundTiles = { 0, 1, 2, 4, 6, 8 };

    // Each faction's pass tile in the standard starts. Giants: tile 9 (3 power; VP for shipping when
    // passing, 0 at shipping 0). Witches: tile 3 (3 power, 1 worker). Mermaids: tile 0 (6 coins).
    // So nobody gets extra shipping or a bonus action from their tile.
    // Power after round 1's income: 2/10/0 for Giants and Witches (5/7/0 + 3), 3/9/0 for the Mermaids.
    public static int TestPassTile(Faction f) => f switch { Faction.Giants => 9, Faction.Witches => 3, _ => 0 };

    // Pass tiles for the other factions in StartWith: the first of these not already taken. None has a bonus
    // action or extra shipping, or scores buildings when passing (tile 9 scores shipping: 0 for these starts).
    private static readonly int[] OtherPassTiles = { 9, 3, 0, 6 };

    // The pass tile each of these factions picks in StartWith (in the same order).
    public static int[] PassTilesFor(Faction[] factions)
    {
        var taken = factions.Where(f => f is Faction.Giants or Faction.Witches or Faction.Mermaids).Select(TestPassTile).ToHashSet();
        return factions
            .Select(f => f is Faction.Giants or Faction.Witches or Faction.Mermaids ? TestPassTile(f) : OtherPassTiles.First(taken.Add))
            .ToArray();
    }

    // The order starting dwellings are placed in, as Rules.ApplyStartGame makes it: in turn order, then reverse
    // turn order; then the Nomads' third; then the Chaos Magicians' only one.
    public static Faction[] PlacementOrder(Faction[] turnOrder)
    {
        var twice = turnOrder.Where(f => Factions.StartingDwellings(f) >= 2).ToArray();
        return twice.Concat(Enumerable.Reverse(twice))
            .Concat(turnOrder.Where(f => Factions.StartingDwellings(f) >= 3))
            .Concat(turnOrder.Where(f => Factions.StartingDwellings(f) == 1))
            .ToArray();
    }

    // Any factions, in this turn order: set up and started, their starting dwellings on these hexes (in placement
    // order, see PlacementOrder), and their pass tiles picked (PassTilesFor). Round 1 has begun, the first in turn
    // order to act, with the usual round tiles (TestRoundTiles).
    public static Move[] StartWith(Faction[] turnOrder, params int[] hexes)
    {
        var order = PlacementOrder(turnOrder);
        Assert.Equal(order.Length, hexes.Length);
        var placed = new Dictionary<Faction, int>();
        var placements = new List<Move>();
        for (var i = 0; i < order.Length; i++)
        {
            placed.TryGetValue(order[i], out var number);
            placed[order[i]] = number + 1;
            placements.Add(StartAt(order[i], number, hexes[i]));
        }
        var tiles = PassTilesFor(turnOrder);
        var picks = Enumerable.Range(0, turnOrder.Length).Reverse()
            .Select(i => (Move)new PickPassTile(turnOrder[i], tiles[i])).ToArray();
        return Concat(Placing(turnOrder), placements.ToArray(), picks);
    }

    // One faction alone (see StartWith).
    public static Move[] Solo(Faction f, params int[] hexes) => StartWith(new[] { f }, hexes);

    // The game chosen, these factions added (in this order) and confirmed, and the turn order this order.
    // One faction alone plays in Calculator mode (no turn order to choose; always Free).
    public static Move[] Confirmed(params Faction[] turnOrder) => turnOrder.Length == 1
        ? new Move[] { ChooseCalculator, new AddPlayer(turnOrder[0]), new ConfirmFactions() }
        : Concat(
        new[] { ChooseTM },
        turnOrder.Select(f => (Move)new AddPlayer(f)).ToArray(),
        new Move[] { new ConfirmFactions() },
        turnOrder.Take(turnOrder.Length - 1).Select(f => (Move)new ChooseTurnOrder(f)).ToArray()); // the last is automatic

    // From a complete turn order (none in Calculator): the map, these round tiles, the pass tiles (PassTileChoice;
    // Calculator: none chosen, so all of them), then Start.
    public static Move[] SetUpAndStart(Faction[] factions, int[] roundTiles, int[]? passTiles = null) => Concat(
        factions.Length > 1 ? new Move[] { new ConfirmTurnOrder() } : System.Array.Empty<Move>(),
        new Move[] { new ChooseMap(MapId.Original) },
        roundTiles.Select(t => (Move)new ChooseRoundTile(t)).ToArray(),
        new Move[] { new ConfirmRoundTiles() },
        factions.Length > 1 ? (passTiles ?? PassTileChoice(factions)).Select(t => (Move)new AddPassTile(t)).ToArray() : System.Array.Empty<Move>(),
        new Move[] { new StartGame() });

    public static Move[] SetUpAndStart(params Faction[] factions) => SetUpAndStart(factions, TestRoundTiles);

    // The pass tiles of a game with these factions: those they pick (PassTilesFor), then others up to three more than
    // the factions.
    private static readonly int[] FillerPassTiles = { 0, 1, 5, 2, 4, 7, 6, 8, 3, 9 }; // 0, 1 and 5 are picked in tests
    public static int[] PassTileChoice(Faction[] factions)
    {
        var tiles = PassTilesFor(factions).Distinct().ToList();
        foreach (var tile in FillerPassTiles)
            if (tiles.Count < factions.Length + Config.ExtraPassTiles && !tiles.Contains(tile))
                tiles.Add(tile);
        return tiles.ToArray();
    }

    // Started: starting dwellings are placed next.
    public static Move[] Placing(params Faction[] turnOrder) => Concat(Confirmed(turnOrder), SetUpAndStart(turnOrder));

    // Every faction picks its TestPassTile, in reverse turn order. Round 1 begins after the last pick.
    public static Move[] Picks(params Faction[] turnOrder) =>
        Enumerable.Reverse(turnOrder).Select(f => (Move)new PickPassTile(f, TestPassTile(f))).ToArray(); // not turnOrder.Reverse(): on arrays that is the in-place Span version in C# 14

    // Giants alone, starting dwellings on hexes 5 and 112. Round 1 has begun.
    // Coins: 15 starting. Workers: 3 starting + 1 base income + 2 (1 under each emptied slot) = 6. Power 2/10/0.
    // Giants start with Fire 1 and Air 1.
    public static readonly Move[] SoloStart = Concat(
        Placing(G),
        new[] { StartAt(G, 0, GiantsStartA), StartAt(G, 1, GiantsStartB) },
        Picks(G));

    // Witches and Giants; Witches first. Placement order: Witches, Giants, Giants, Witches.
    // Round 1 has begun with the Witches to act. Coins: 15 each. Workers: Witches 7, Giants 6. Power 2/10/0 each.
    // Next to the Witches' 9: 8 Plains (3 spades to Forest), 10 Lake (1), 21 Swamp (2).
    public static readonly Move[] TwoPlayerStart = Concat(
        Placing(W, G),
        new[]
        {
            StartAt(W, 0, WitchesStartA), StartAt(G, 0, GiantsStartA),
            StartAt(G, 1, GiantsStartB), StartAt(W, 1, WitchesStartB),
        },
        Picks(W, G));

    // All three factions (added Giants, Witches, Mermaids), confirmed, nobody placed in the turn order yet.
    public static readonly Move[] ThreePlayersConfirmed =
    {
        ChooseTM, new AddPlayer(G), new AddPlayer(W), new AddPlayer(M), new ConfirmFactions(),
    };

    // Turn order Witches, Giants, Mermaids. Round 1 has begun with the Witches to act.
    public static readonly Move[] ThreePlayerStart = Concat(
        ThreePlayersConfirmed,
        new Move[] { new ChooseTurnOrder(W), new ChooseTurnOrder(G) },
        SetUpAndStart(W, G, M),
        new[]
        {
            StartAt(W, 0, WitchesStartA), StartAt(G, 0, GiantsStartA), StartAt(M, 0, MermaidsStartA),
            StartAt(M, 1, MermaidsStartB), StartAt(G, 1, GiantsStartB), StartAt(W, 1, WitchesStartB),
        },
        Picks(W, G, M));

    public static Move[] Concat(params Move[][] parts) => parts.SelectMany(part => part).ToArray();
}
