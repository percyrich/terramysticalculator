using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: bonus actions (the octagons): click one, then its target. A file of the same class (partial).
public partial class Main
{
    // ---------- Bonus actions: click the octagon, then the target ----------

    private (Faction Faction, BonusActionId Bonus)? BonusActionUnder(Vector2 point)
    {
        foreach (PlayerPanel panel in _panels)
            if (panel.ShownFaction is Faction faction && panel.BonusActionAt(point) is BonusActionId bonus)
                return (faction, bonus);
        // The octagon on the active player's own pass tile works like the one on their panel.
        GameState state = _history.Current;
        if (state.ActivePlayer is Faction active && state[active].PassTile is int tile
            && PassTiles.All[tile].BonusAction is BonusAction action
            && _passTileSprites.GetValueOrDefault(tile) is PassTileView { Action: { } token } && token.IsOver(point))
            return (active, new BonusActionId(BonusSource.PassTile, action));
        return null;
    }

    // Clicking an octagon starts that bonus action (if the rules allow it now) and highlights its targets.
    // Clicking the same octagon again cancels it.
    private void OnBonusActionClicked(Faction faction, BonusActionId bonus)
    {
        if (_bonus is { } current && current.Faction == faction && current.Bonus == bonus)
        {
            CancelBonus();
            ShowNote("");
            return;
        }

        EndBonus();
        if (OnFanBonusActionClicked(faction, bonus)) // the fan factions' strongholds (MainFanFactions.cs)
            return;
        // Spade bonus actions have no target of their own: the free spades arrive at once, then each hex
        // is clicked as usual.
        if (BonusActions.Spades(bonus.Action) > 0)
        {
            Submit(new BonusSpades(faction, bonus.Source));
            return;
        }
        // Nor has the double action: the two actions follow.
        if (bonus.Action == BonusAction.DoubleAction)
        {
            Submit(new BonusDoubleAction(faction, bonus.Source));
            return;
        }
        // The shift: first the price, then the terrain (MainFireAndIce.cs).
        if (bonus.Action == BonusAction.Shapeshift)
        {
            StartShift(faction, bonus);
            return;
        }

        PieceOptions options = Rules.BonusOptions(_history.Current, faction, bonus);
        if (!options.CanMove)
        {
            ShowNote(options.Reason); // e.g. "...already used that bonus action this round."
            Render(animate: true);
            return;
        }

        _bonus = (faction, bonus);
        _bonusOptions = options;
        SetHighlights(options.Destinations, on: true);
        ShowPrompt(BonusPrompt(bonus.Action) + " (Esc or click the octagon again to cancel.)");
        Render(animate: true); // shows the octagon as selected
    }

    private static string BonusPrompt(BonusAction action) => action switch
    {
        BonusAction.CultStep           => "Click a track to move one step up it.",
        BonusAction.TwoCultSteps       => "Click a track to move two steps up it.",
        BonusAction.Sandstorm          => "Click a highlighted hex to turn it into Desert.",
        BonusAction.BuildOnHomeTerrain => "Click a highlighted hex to build a free dwelling there.",
        BonusAction.FreeTradingPost    => "Click a highlighted dwelling to upgrade it for free.",
        _ => "",
    };

    // A click while a bonus action waits for a target. A click on a track or hex is claimed: a legal target
    // uses the bonus action, anything else says why not. Other clicks (e.g. Undo) carry on as normal.
    private void OnBonusTargetClicked(Vector2 mouse)
    {
        var (faction, bonus) = _bonus.Value;
        if (bonus.Action == BonusAction.Shapeshift)
        {
            OnShiftClicked(faction, bonus, mouse);
            return;
        }
        if (bonus.Action is BonusAction.MoveBridge or BonusAction.SwapFavour)
        {
            OnFanBonusTargetClicked(mouse); // MainFanFactions.cs
            return;
        }
        if (bonus.Action is BonusAction.RiverTokens or BonusAction.SelkiesSpade)
        {
            OnFanBonusTargetClickedOnMap(mouse); // MainFanFactionsOnMap.cs
            return;
        }
        Destination target = bonus.Action is BonusAction.CultStep or BonusAction.TwoCultSteps
            ? (TrackAt(mouse) is Track track ? new TrackDestination(track) : null)
            : (_map.HexAt(mouse) is int hex ? new HexDestination(hex) : null);
        if (target is null)
            return;

        GetViewport().SetInputAsHandled();
        Move move = Rules.BonusMove(faction, bonus, target);
        if (!_bonusOptions.Destinations.Contains(target))
        {
            ShowIssues(Rules.Apply(_history.Current, move).Issues); // the bonus action stays waiting
            return;
        }

        Submit(move);
    }

    // The track whose step nearest the point is within snapping distance, if any.
    private Track? TrackAt(Vector2 point)
    {
        foreach (Track track in AllTracks)
            for (var step = 0; step <= Config.TrackMax; step++)
                if (TrackStepNode(track, step).GlobalPosition.DistanceTo(point) <= SnapRadius)
                    return track;
        return null;
    }

    // Stops waiting for a target, without redrawing (for callers about to redraw anyway).
    private void EndBonus()
    {
        CloseUpgradeChoice();
        _shiftTarget = null;
        EndFanChoices(); // the Djinni's lamp, the Goblins' treasure, a bridge or favour chosen (MainFanFactions.cs)
        if (_bonus is null)
            return;
        SetHighlights(_bonusOptions.Destinations, on: false);
        _bonus = null;
        _bonusOptions = null;
    }

    private void CancelBonus()
    {
        EndBonus();
        Render(animate: true);
    }
}
