using System.Collections.Immutable;

namespace Core;

// One pass tile. A player holds one at a time: picked during setup, swapped for another when passing.
// Its income is added to the holder's income; its bonus action is usable while it is held;
// its pass victory points score when the holder passes with it (before swapping it).
public sealed record PassTileDefinition
{
    public bool MiniExpansion { get; init; }     // only with the mini-expansion (GameOptions.MiniExpansion)
    public Income Income { get; init; }
    public BonusAction? BonusAction { get; init; }
    public ImmutableArray<PieceKind> PassScoredBuildings { get; init; } = ImmutableArray<PieceKind>.Empty;
    public int PassVictoryPointsPerBuilding { get; init; } // for each such building of the holder's on the map
    public int PassVictoryPointsPerShipping { get; init; } // for each level of the holder's own shipping
    public int ShippingBonus { get; init; }                // extra shipping while held, during the main phase only
    // TODO: pass tiles in later games may score towns, keys, ... in the same way.
}

public static class PassTiles
{
    // Indexed by tile id.
    public static readonly ImmutableArray<PassTileDefinition> All = ImmutableArray.Create(
        new PassTileDefinition { Income = new Income(6, 0, 0, 0) },
        new PassTileDefinition { Income = new Income(2, 0, 0, 0), BonusAction = Core.BonusAction.OneSpade },
        new PassTileDefinition { Income = new Income(0, 1, 0, 0),
            PassScoredBuildings = ImmutableArray.Create(PieceKind.TradingPost), PassVictoryPointsPerBuilding = 2 },
        new PassTileDefinition { Income = new Income(0, 1, 0, 3) },
        new PassTileDefinition { Income = new Income(0, 2, 0, 0),
            PassScoredBuildings = ImmutableArray.Create(PieceKind.Stronghold, PieceKind.Sanctuary), PassVictoryPointsPerBuilding = 4 },
        new PassTileDefinition { Income = new Income(0, 0, 0, 3), ShippingBonus = 1 },
        new PassTileDefinition { Income = new Income(0, 0, 1, 0) },
        new PassTileDefinition { Income = new Income(2, 0, 0, 0),
            PassScoredBuildings = ImmutableArray.Create(PieceKind.Dwelling), PassVictoryPointsPerBuilding = 1 },
        new PassTileDefinition { Income = new Income(4, 0, 0, 0), BonusAction = Core.BonusAction.CultStep },
        new PassTileDefinition { Income = new Income(0, 0, 0, 3), PassVictoryPointsPerShipping = 3, MiniExpansion = true });

    public static int Count => All.Length;

    // Victory points this faction scores for passing with this tile: from its buildings on the map,
    // and from its own shipping level (not counting any shipping bonus).
    public static int PassVictoryPoints(GameState state, Faction faction, int id)
    {
        var tile = All[id];
        var buildings = state.Map.Count(piece => piece is { } p && p.Owner == faction && tile.PassScoredBuildings.Contains(Landscapes.CountsAs(p)));
        return buildings * tile.PassVictoryPointsPerBuilding + state[faction].Shipping * tile.PassVictoryPointsPerShipping;
    }

    public static string Describe(int id)
    {
        var tile = All[id];
        var lines = new List<string> { $"Income: {tile.Income}" };
        if (tile.BonusAction is BonusAction action)
            lines.Add($"Bonus action: {BonusActions.Describe(action)}");
        if (tile.PassVictoryPointsPerBuilding > 0)
            lines.Add($"Pass: +{tile.PassVictoryPointsPerBuilding} VP per {string.Join("/", tile.PassScoredBuildings.Select(Buildings.Noun))}");
        if (tile.PassVictoryPointsPerShipping > 0)
            lines.Add($"Pass: +{tile.PassVictoryPointsPerShipping} VP per shipping level");
        if (tile.ShippingBonus > 0)
            lines.Add($"+{tile.ShippingBonus} shipping");
        return string.Join("\n", lines);
    }
}
