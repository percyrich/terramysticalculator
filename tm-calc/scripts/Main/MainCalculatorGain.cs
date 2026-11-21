using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: Calculator's +1 bar (CalculatorGainBar). From the start of the game the active player's board sits
// lower (CalculatorGainShift), leaving room for the bar, which shows once play has begun: directly above the board's
// resources, each icon over that income's icon; a click on a cell gives that resource (CalculatorGain: a coin, a
// worker, a priest, or a power from bowl I → II → III). Calculator Strict: power only. Repeated clicks merge into one
// undo step (Rules.MergeKey).
public partial class Main
{
    private CalculatorGainBar _gainBar;

    // Calculator, from the first board on: the faction whose board makes room for the bar (the active one, else the
    // first). The bar itself only once play has begun (CalculatorGainFaction).
    private Faction? CalculatorBoardFaction(GameState state) =>
        state.Options.Calculator && state.Phase >= Phase.PlacingDwellings && state.Phase != Phase.Finished
            ? state.ActivePlayer ?? (state.TurnOrder.Count > 0 ? (Faction?)state.TurnOrder[0] : null)
            : null;

    private Faction? CalculatorGainFaction(GameState state) =>
        state.Phase is Phase.Playing or Phase.RoundEndSpades && _board.Visible ? CalculatorBoardFaction(state) : null;

    // How far down the board goes for the bar (in the Board's pixels): the same from the start, so it never moves.
    private float CalculatorGainShift(GameState state, Faction faction) =>
        CalculatorBoardFaction(state) == faction ? (CalculatorGainHeight + CalculatorGainGap) * _panels[0].Scale.Y : 0f;

    private static ResourceKind KindOf(GainKind kind) => kind switch
    {
        GainKind.Coin => ResourceKind.Coin,
        GainKind.Worker => ResourceKind.Worker,
        GainKind.Priest => ResourceKind.Priest,
        _ => ResourceKind.Power,
    };

    private void RenderCalculatorGain(GameState state)
    {
        Faction? faction = CalculatorGainFaction(state);
        PlayerPanel panel = faction is Faction shown ? PanelFor(shown) : null;
        if (panel is null)
        {
            if (_gainBar is not null)
                _gainBar.Visible = false;
            return;
        }
        if (_gainBar is null)
        {
            _gainBar = new CalculatorGainBar { Name = "CalculatorGainBar" };
            _board.AddChild(_gainBar);
        }
        Faction f = faction.Value;
        // In the panel's own pixels, at its place and scale: each cell over its income icon (full layout).
        _gainBar.Scale = panel.Scale;
        _gainBar.Position = _panelTargets.TryGetValue(panel, out Vector2 target) ? target : panel.Position;
        IEnumerable<GainKind> kinds = state.Options.CalculatorStrict ? new[] { GainKind.Power } : CalculatorGainBar.Order;
        var cells = new List<(GainKind, Vector2)>();
        foreach (GainKind kind in kinds)
            if (panel.FullIncomeIconCentre(KindOf(kind)) is Vector2 icon)
                cells.Add((kind, icon));
        if (cells.Count == 0)
        {
            _gainBar.Visible = false;
            return;
        }
        float bottom = panel.FullTop - CalculatorGainGap, top = bottom - CalculatorGainHeight;
        float spacing = cells.Count > 1 ? cells[1].Item2.X - cells[0].Item2.X : panel.IncomeIconSize * 1.4f;
        var bar = new Rect2(cells[0].Item2.X - spacing / 2, top, cells[^1].Item2.X - cells[0].Item2.X + spacing, CalculatorGainHeight);
        cells = cells.Select(cell => (cell.Item1, new Vector2(cell.Item2.X, (top + bottom) / 2))).ToList();
        PlayerColour colour = state[f].Colour;
        _gainBar.ShowFor(cells, bar, panel.IncomeIconSize, Art.PieceColour(colour),
            PieceArt.TextureFor(PieceKind.Priest, Art.PieceColour(colour), Art.OutlineFor(colour)),
            kinds.Where(kind => Rules.Apply(state, new CalculatorGain(f, kind)).Accepted));
        _gainBar.Visible = true;
    }

    // A left click on a lit cell gives that resource (from OnModeInput). True if the event was taken.
    private bool OnCalculatorGainInput(InputEvent e, GameState state)
    {
        if (_gainBar is null || !_gainBar.Visible || CalculatorGainFaction(state) is not Faction faction)
            return false;
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return false;
        if (IsOverTopPanel() || _wheel.Visible || _upgradeChoice is not null || AnyMenuOpen)
            return false;
        if (_gainBar.KindAt(GetGlobalMousePosition()) is not GainKind kind)
            return false;
        GetViewport().SetInputAsHandled();
        Submit(new CalculatorGain(faction, kind));
        return true;
    }

    // Calculator: a right click on a pass tile in the bank takes a coin off it (TakePassTileCoin). From OnModeInput.
    private bool OnPassTileCoinInput(InputEvent e, GameState state)
    {
        if (!state.Options.Calculator || state.Phase < Phase.PlacingDwellings
            || e is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
            return false;
        if (PassTileUnder(GetGlobalMousePosition()) is not int tile || !_bankPassTiles.Contains(tile))
            return false;
        GetViewport().SetInputAsHandled();
        Submit(new TakePassTileCoin(tile)); // refused with a reason if it has none
        return true;
    }
}
