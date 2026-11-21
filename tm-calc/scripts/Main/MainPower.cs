using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: power conversions and burning, on the active player's (full) panel. A file of the same class
// (partial), only to keep Main.cs a manageable size.
//   Bowl III clicked: a menu under it (PowerBowlsView.ShowConversions): a priest, a worker and a coin, each at full
//     opacity if it can be had now (burning first as needed; a priest also needs one to gain), else half. Clicking
//     one that can be had converts (ConvertPowerTo…, BurnFirst) and the menu stays, its opacities kept up to date;
//     Esc or a click on bowl III closes it; any other click closes it and does what it would have done anyway
//     (MainInput._Input).
//   Bowl II: while burning is possible, the mouse anywhere over it lights the token burning takes away (red, no
//     glints); clicking it burns.
// Neither works mid-action (Rules.FreeActionIssues): clicking bowl III then shows why instead, and so does
// clicking bowl II (BurnPower's refusal).
public partial class Main
{
    private PlayerPanel _conversionPanel; // the panel whose conversions are open (null: none)

    // A click on an interactive panel's bowl III or II: handled (true), or not over either.
    private bool OnPressPowerBowls(Vector2 mouse)
    {
        GameState state = _history.Current;
        foreach (PlayerPanel panel in _panels)
        {
            if (!IsInteractive(panel) || panel.ShownFaction is not Faction faction)
                continue;
            if (panel.PowerBank.IsOverBowl(3, mouse))
            {
                IReadOnlyList<Issue> refusal = Rules.FreeActionIssues(state, faction);
                if (refusal.Count > 0)
                {
                    ShowIssues(refusal);
                    return true;
                }
                ShowNote("");
                _conversionPanel = panel;
                RenderConversions(state);
                UpdatePowerHover(mouse);
                return true;
            }
            if (panel.PowerBank.IsOverBowl(2, mouse))
            {
                Submit(new BurnPower(faction)); // refused (mid-action, too little power): the reason is shown
                return true;
            }
            // Fan factions: the Enlightened buy a new token for bowl I with a coin (Setup mode adds one free: MainModes).
            if (faction == Faction.Enlightened && panel.PowerBank.IsOverBowl(1, mouse))
            {
                Submit(new BuyPowerToken(faction));
                return true;
            }
        }
        return false;
    }

    private void OnPressConversions(Vector2 mouse)
    {
        PlayerPanel panel = _conversionPanel;
        GetViewport().SetInputAsHandled();
        if (panel.PowerBank.ConversionAt(mouse) is ResourceKind kind)
        {
            if (panel.ShownFaction is Faction faction && ConversionMove(_history.Current, faction, kind) is Move move)
                Submit(move); // stays open (RenderConversions updates it)
            return; // a faded one does nothing
        }
        if (!panel.PowerBank.IsOverConversions(mouse))
            CloseConversions(); // (already closed by _Input before a press gets here)
    }

    private void CloseConversions()
    {
        _conversionPanel?.PowerBank.HideConversions();
        _conversionPanel = null;
        UpdatePowerHover(GetGlobalMousePosition());
    }

    // Keeps the open menu up to date; closes it if its panel is no longer the one to act on (or can't act now:
    // e.g. the turn moved on, or an undo went back into an action).
    private void RenderConversions(GameState state)
    {
        foreach (PlayerPanel other in _panels.Where(other => other != _conversionPanel))
            other.PowerBank.HideConversions();
        if (_conversionPanel is not PlayerPanel panel)
            return;
        if (!_board.Visible || !IsInteractive(panel) || panel.ShownFaction is not Faction faction
            || Rules.FreeActionIssues(state, faction).Count > 0)
        {
            CloseConversions();
            return;
        }
        panel.PowerBank.ShowConversions(PowerBowlsView.Conversions.Select(kind => ConversionMove(state, faction, kind) is not null).ToList(),
            doubled: faction == Faction.Enlightened && state[faction].StrongholdBuilt); // fan factions: conversions doubled
    }

    // The conversion to this resource, if the rules would accept it now (and, for a priest, there is one to gain).
    private static Move ConversionMove(GameState state, Faction faction, ResourceKind kind)
    {
        PlayerState player = state[faction];
        Move move = kind switch
        {
            ResourceKind.Priest => new ConvertPowerToPriest(faction, BurnFirst: true),
            ResourceKind.Worker => new ConvertPowerToWorker(faction, BurnFirst: true),
            ResourceKind.Coin => new ConvertPowerToCoin(faction, BurnFirst: true),
            _ => null,
        };
        if (move is null || !Rules.Apply(state, move).Accepted)
            return null;
        bool priestToGain = player.PriestSupply.Count > 0 || faction == Faction.Riverwalkers && player.WheelPriests.Count > 0;
        return kind == ResourceKind.Priest && !priestToGain ? null : move;
    }

    // Lights bowl II's next burned token on the panel the mouse is over, while burning is possible there.
    private void UpdatePowerHover(Vector2 mouse)
    {
        GameState state = _history.Current;
        foreach (PlayerPanel panel in _panels)
        {
            bool burnable = _board.Visible && !AnyMenuOpen && _bonus is null && IsInteractive(panel)
                && panel.ShownFaction is Faction faction && panel.PowerBank.IsOverBowl(2, mouse)
                && Rules.Apply(state, new BurnPower(faction)).Accepted;
            panel.PowerBank.ShowBurnHover(burnable);
        }
    }
}
