namespace Core;

// Calculator: the neutral opponent's pieces (GameState.Neutral). Placed and taken away at any time once the game has
// started (Setup included); they only take up room (see NeutralBoard). And the +1 panel (CalculatorGain), a coin taken
// off a pass tile in the bank (TakePassTileCoin), and Calculator Strict (IsCalculatorStrict).
public static partial class Rules
{
    private static MoveResult ApplyNeutral(GameState state, Move move)
    {
        if (!state.Options.Calculator)
            return MoveResult.Rejected(state, "Only Calculator games have a neutral opponent.");
        if (state.Phase < Phase.PlacingDwellings)
            return MoveResult.Rejected(state, "The neutral opponent's pieces go on the board once the game has started.");
        var board = state.Neutrals;
        NeutralBoard? changed = null;
        string? refusal = null;
        switch (move)
        {
            case PlaceNeutralDwelling m:
                if (!state.Layout.Contains(m.Hex) || state.HexTerrain[m.Hex] == Terrain.River)
                    refusal = "A neutral dwelling goes on a land hex.";
                else if (state.IsOccupied(m.Hex))
                    refusal = "That hex is already occupied.";
                else
                    changed = board with { Hexes = board.Hexes.Add(m.Hex) };
                break;
            case RemoveNeutralDwelling m:
                if (!board.Hexes.Contains(m.Hex))
                    refusal = "There is no neutral dwelling there.";
                else
                    changed = board with { Hexes = board.Hexes.RemoveAt(board.Hexes.IndexOf(m.Hex)) };
                break;
            case PlaceNeutralBridge m:
            {
                var (a, b) = (Math.Min(m.HexA, m.HexB), Math.Max(m.HexA, m.HexB));
                if (!BridgeEdges(state).Contains((a, b)))
                    refusal = "A bridge must join two land hexes across the edge between two river hexes.";
                else if (state.IsBridged(a, b))
                    refusal = "There is already a bridge there.";
                else
                    changed = board with { Bridges = board.Bridges.Add((a, b)) };
                break;
            }
            case RemoveNeutralBridge m:
            {
                var spot = (Math.Min(m.HexA, m.HexB), Math.Max(m.HexA, m.HexB));
                if (!board.Bridges.Contains(spot))
                    refusal = "There is no neutral bridge there.";
                else
                    changed = board with { Bridges = board.Bridges.RemoveAt(board.Bridges.IndexOf(spot)) };
                break;
            }
            case PlaceNeutralPriest m:
                if (m.Slot < 0 || m.Slot >= Config.PriestSlotsPerZone)
                    refusal = "There is no such slot.";
                else if (state.IsSlotTaken(m.Track, m.Slot))
                    refusal = "That slot is already taken.";
                else
                    changed = board with { Priests = board.Priests.Add((m.Track, m.Slot)) };
                break;
            case RemoveNeutralPriest m:
                if (!board.Priests.Contains((m.Track, m.Slot)))
                    refusal = "There is no neutral priest there.";
                else
                    changed = board with { Priests = board.Priests.RemoveAt(board.Priests.IndexOf((m.Track, m.Slot))) };
                break;
        }
        if (changed is null)
            return MoveResult.Rejected(state, refusal ?? "Nothing to change.");
        return MoveResult.Ok(state with { Neutral = changed });
    }

    // The +1 panel: a coin, a worker, a priest or a power given to the faction (CalculatorGain).
    private static MoveResult ApplyCalculatorGain(GameState state, CalculatorGain move)
    {
        if (!state.Options.Calculator)
            return MoveResult.Rejected(state, "Resources are given by hand only in Calculator games.");
        if (state.Phase is not (Phase.Playing or Phase.RoundEndSpades))
            return MoveResult.Rejected(state, "Resources are given by hand once play has begun.");
        if (!state.IsInGame(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are not in the game.");
        if (IsCalculatorStrict(state) && move.Kind != GainKind.Power)
            return MoveResult.Rejected(state, "Calculator Strict gives power only.");
        var player = state[move.Player];
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        GameState next;
        switch (move.Kind)
        {
            case GainKind.Coin:
                next = state.WithPlayer(player with { Coins = player.Coins + 1 });
                events.Add(new CoinGained(move.Player, player.Coins + 1));
                break;
            case GainKind.Worker:
                next = state.WithPlayer(player with { Workers = player.Workers + 1 });
                break;
            case GainKind.Priest:
                if (player.PriestDebt == 0 && player.PriestSupply.Count == 0
                    && !(move.Player == Faction.Riverwalkers && player.WheelPriests.Count > 0))
                    return MoveResult.Rejected(state, $"The {Name(move.Player)} have no priest left in their supply.");
                next = GainPriest(state, move.Player, events, warnings);
                break;
            default:
                if (player.Power.GainCapacity == 0)
                    return MoveResult.Rejected(state, $"The {Name(move.Player)}' power is all in bowl III already.");
                next = GainPower(state, move.Player, 1, PowerSource.Calculator, events);
                break;
        }
        return MoveResult.Ok(next, events, warnings);
    }

    // Calculator Strict (GameOptions.CalculatorStrict): debt and the pyro rule refused as in Strict mode.
    private static bool IsCalculatorStrict(GameState state) => state.Options is { Calculator: true, CalculatorStrict: true };

    // A coin off a pass tile in the bank (Calculator: right-click), once the game has started.
    private static MoveResult ApplyTakePassTileCoin(GameState state, TakePassTileCoin move)
    {
        if (!state.Options.Calculator)
            return MoveResult.Rejected(state, "Coins are taken off pass tiles by hand only in Calculator games.");
        if (state.Phase < Phase.PlacingDwellings || state.PassTileBank.IndexOf(move.Tile) < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");
        if (state.PassTileCoins[move.Tile] == 0)
            return MoveResult.Rejected(state, "There is no coin on that pass tile.");
        return MoveResult.Ok(state with { PassTileCoins = state.PassTileCoins.SetItem(move.Tile, state.PassTileCoins[move.Tile] - 1) });
    }
}
