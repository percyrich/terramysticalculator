using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: Fire & Ice's choices on the board (the faction screen's are in MainFactions.cs). A file of the
// same class (partial), only to keep Main.cs a manageable size.
//   The terrain wheel on a panel (PlayerPanel.TerrainWheel): the Riverwalkers' opens when clicked (a look; any click
//     closes it) and while they owe a priest choice (their locked terrains lit: white, purple for another faction's
//     home, which costs more; a lit one unlocks it, any other click takes the priest; their panel is full size and on
//     top meanwhile, even out of turn). The Shapeshifters' opens when they start their shift.
//   The shift: its octagon clicked, the wheel opens with the terrains their home may move to lit purple; one clicked
//     turns white and two chips appear just above it (spend 5 power, remove 5 tokens; greyed out if it can't be paid
//     that way); a chip clicked shifts there (another lit terrain moves the choice). Any other click (or Esc, or the
//     octagon again) cancels.
//   The Shapeshifters' token offer: their power bowl III lit purple with a glint; clicking it pays 1 VP for a token
//     there, any other click declines.
//   Setting up a game in progress (OnSetupWheelPress, from MainModes.OnModeInput): the selected faction's wheel
//     clicked opens it (Riverwalkers, Shapeshifters, Geologists). The Riverwalkers' priests on it are lit: one clicked
//     goes back to their supply (EditWheel), the wheel staying open. The Shapeshifters' terrains their home may move
//     to are lit: one clicked moves it there (EditHome) and the wheel closes. Any other click closes the wheel (and,
//     off the wheel, is then handled as usual); the Geologists' stays open for clicks on it (EditRock).
public partial class Main
{
    private Terrain? _shiftTarget;    // the terrain chosen for the Shapeshifters' new home, before the price
    private PlayerPanel _lookWheel;   // the panel whose terrain wheel is open for a look, if any
    // Where each priest on the Riverwalkers' wheel stood at the last render (global), so one taken off it starts there.
    private readonly Dictionary<Piece, Vector2> _wheelPriestSpots = new();

    // Something one player must answer before anything else happens: a priest choice, a token offer, or the Acolytes'
    // steps for a Volcano.
    private static bool OwedChoiceWaiting(GameState state) =>
        state.Players.Any(p => p.PriestChoicesOwed > 0 || p.TokenOfferOwed || p.CultStepsToPay > 0);

    // The top panel's keys (undo, redo, bookmark, map, log): clicks on the top panel never reach the board anyway
    // (OnPress), so this only matters for code that looks at the hovered button first.
    private static bool IsHistoryButton(BaseButton button) => button is TopPanelKey;

    // ---------- Clicks ----------

    // While a priest choice or a token offer waits: the history buttons work as usual; a lit terrain on the wheel is
    // taken by the wheel itself (OnPanelTerrainChosen); the token chip accepts; any other press answers no (any other
    // button then still does its own job; the step button's tick declines the token too: MainStepButton.StepMove).
    // True if the press was taken.
    private bool OnPressOwedChoice(Vector2 mouse)
    {
        GameState state = _history.Current;
        PlayerState priest = state.Players.FirstOrDefault(p => p.PriestChoicesOwed > 0);
        PlayerState token = state.Players.FirstOrDefault(p => p.TokenOfferOwed);
        if (priest is null && token is null)
            return false;
        BaseButton button = GetViewport().GuiGetHoveredControl() as BaseButton;
        if (button is not null && IsHistoryButton(button))
            return true; // the button's own signal does it
        if (button is null)
            GetViewport().SetInputAsHandled();
        if (priest is not null)
            Submit(new ChoosePriest(priest.Faction, null)); // the priest is taken
        else
            Submit(new AnswerTokenOffer(token.Faction, PanelFor(token.Faction)?.IsOverTokenOffer(mouse) ?? false));
        return true;
    }

    // The Riverwalkers' wheel: a click on it (on a full-size panel) opens it; while it is open, any click closes it
    // (one on the wheel does nothing else; any other is then handled as usual).
    private bool OnPressTerrainWheel(Vector2 mouse)
    {
        if (_lookWheel is not null)
        {
            bool over = _lookWheel.IsOverTerrainWheel(mouse);
            // Setting up: the Geologists' wheel stays open for more rocks (each click on it is EditRock).
            if (over && _lookWheel.ShownFaction == Faction.Geologists && _history.Current.Phase == Phase.SettingUp)
            {
                GetViewport().SetInputAsHandled();
                return true;
            }
            _lookWheel = null;
            Render(animate: true);
            if (over)
                GetViewport().SetInputAsHandled();
            return over;
        }
        if (_bonus is not null)
            return false;
        foreach (PlayerPanel panel in _panels)
            if (panel.Visible && !panel.IsCompact && panel.ShownFaction is Faction.Riverwalkers or Faction.Geologists
                && panel.IsOverTerrainWheel(mouse))
            {
                GetViewport().SetInputAsHandled();
                _lookWheel = panel;
                Render(animate: true);
                return true;
            }
        return false;
    }

    // Setting up: a press (left or right) with the selected faction's wheel open, or on it. True if used up here. (A
    // lit circle on the open wheel is taken by the wheel itself first: OnPanelTerrainChosen.)
    private bool OnSetupWheelPress(GameState state, Vector2 mouse, bool right)
    {
        if (_lookWheel is not null)
        {
            bool over = _lookWheel.IsOverTerrainWheel(mouse);
            if (over && _lookWheel.ShownFaction == Faction.Geologists)
                return true; // stays open for more rocks
            _lookWheel = null;
            Render(animate: true);
            if (over)
                GetViewport().SetInputAsHandled();
            return over;
        }
        if (right || state.ActivePlayer is not Faction selected
            || selected is not (Faction.Riverwalkers or Faction.Shapeshifters or Faction.Geologists)
            || PanelFor(selected) is not PlayerPanel panel || panel.IsCompact || !panel.IsOverTerrainWheel(mouse))
            return false;
        GetViewport().SetInputAsHandled();
        _lookWheel = panel;
        Render(animate: true);
        return true;
    }

    // The shift's octagon clicked: if it could be used now (either way of paying), the price chips appear.
    private void StartShift(Faction faction, BonusActionId bonus)
    {
        GameState state = _history.Current;
        ValueList<Terrain> targets = Rules.ShapeshiftTargets(state, faction);
        if (targets.Count == 0)
        {
            ShowNote($"There is no terrain the {NameOf(faction)}' home could move to.");
            return;
        }
        MoveResult spend = Rules.Apply(state, new BonusShapeshift(faction, bonus.Source, targets[0], PayWithTokens: false));
        MoveResult tokens = Rules.Apply(state, new BonusShapeshift(faction, bonus.Source, targets[0], PayWithTokens: true));
        if (!spend.Accepted && !tokens.Accepted)
        {
            ShowIssues(spend.Issues); // e.g. already used this round, or neither price can be paid
            Render(animate: true);
            return;
        }
        _bonus = (faction, bonus);
        _bonusOptions = new PieceOptions(ValueList<Destination>.Empty, null);
        _shiftTarget = null;
        ShowPrompt($"Click a lit terrain for the {NameOf(faction)}' new home. (Esc or click the octagon again to cancel.)");
        Render(animate: true);
    }

    // A click while the shift waits (a lit terrain on the open wheel is taken by the wheel itself: OnPanelTerrainChosen):
    // a price chip that can be paid shifts the home to the chosen terrain; anything else cancels.
    private void OnShiftClicked(Faction faction, BonusActionId bonus, Vector2 mouse)
    {
        if (_shiftTarget is Terrain target && PanelFor(faction)?.ShiftPriceAt(mouse) is bool tokens)
        {
            GetViewport().SetInputAsHandled();
            Move shift = new BonusShapeshift(faction, bonus.Source, target, tokens);
            MoveResult trial = Rules.Apply(_history.Current, shift);
            if (!trial.Accepted)
            {
                ShowIssues(trial.Issues); // e.g. too little power
                return;
            }
            Submit(shift);
            return;
        }
        CancelBonus();
        ShowNote("");
    }

    // A lit terrain clicked on a panel's wheel.
    private void OnPanelTerrainChosen(PlayerPanel panel, Terrain terrain)
    {
        if (panel.ShownFaction is not Faction faction)
            return;
        GameState state = _history.Current;
        if (OnRockChosen(faction, terrain)) // the Geologists (MainFanFactionsEmberGeologistsChangelings.cs)
            return;
        if (state.Phase == Phase.SettingUp && _lookWheel == panel)
        {
            if (faction == Faction.Riverwalkers)
                Submit(new EditWheel(faction, terrain)); // its priest back to the supply; the wheel stays open
            else if (faction == Faction.Shapeshifters)
            {
                _lookWheel = null; // the wheel closes on the new home
                Submit(new EditHome(faction, terrain));
            }
            return;
        }
        if (state.IsInGame(faction) && state[faction].PriestChoicesOwed > 0)
        {
            Submit(new ChoosePriest(faction, terrain)); // unlocks it: its priest goes to the supply
            return;
        }
        if (PolestarShiftOpen(state, faction)) // the Spectral Polestar's free move: no price to choose
        {
            Submit(new LandscapeShapeshift(faction, terrain));
            return;
        }
        if (_bonus is { } shift && shift.Faction == faction && shift.Bonus.Action == BonusAction.Shapeshift)
        {
            _shiftTarget = terrain; // the price chips appear over it
            ShowPrompt($"Choose how to pay: spend {Config.ShapeshiftCost} power, or remove {Config.ShapeshiftCost} power tokens. "
                + "(Esc or click the octagon again to cancel.)");
            Render(animate: true);
        }
    }

    // ---------- Drawing ----------

    // A panel's terrain wheel (open or not, what is lit on it), the shift's price chips and the token offer's bowl.
    // Called after PlayerPanel.ShowPlayer (which shows the wheel as a still picture) and ShowBonusActions.
    private void ShowTerrainChoices(PlayerPanel panel, GameState state, PlayerState player, bool animate)
    {
        Faction faction = player.Faction;
        Color colour = Art.PieceColour(player.Colour);
        bool shifting = _bonus is { } bonus && bonus.Faction == faction && bonus.Bonus.Action == BonusAction.Shapeshift;
        bool polestar = PolestarShiftOpen(state, faction); // the Spectral Polestar's free move, as the shift's wheel
        bool choosingPriest = player.PriestChoicesOwed > 0;
        bool setup = state.Phase == Phase.SettingUp;
        if (setup && _lookWheel == panel && (state.ActivePlayer != faction || panel.IsCompact))
            _lookWheel = null; // another faction selected: the wheel closes
        if (panel.TerrainWheelTop is Terrain top)
        {
            panel.SetTerrainWheelOpen(choosingPriest || shifting || polestar || _lookWheel == panel, animate);
            if (setup && _lookWheel == panel && faction == Faction.Riverwalkers)
                panel.TerrainWheel.ShowChoices(top, player.WheelPriests.Select(locked => locked.Terrain)); // a priest clicked: to the supply
            else if (setup && _lookWheel == panel && faction == Faction.Shapeshifters)
                panel.TerrainWheel.ShowChoices(top, Terrains.Wheel.Where(terrain =>
                    Rules.Apply(state, new EditHome(faction, terrain)).Accepted)); // not another faction's, not the home itself
            else if (choosingPriest)
                panel.TerrainWheel.ShowChoices(top, player.WheelPriests.Select(locked => locked.Terrain),
                    terrain => state.IsOthersHome(faction, terrain) ? HighlightArt.Colour.Purple : HighlightArt.Colour.White,
                    terrain =>
                    {
                        int coins = Rules.UnlockCost(state, faction, terrain);
                        return (Amounts.Of(ResourceKind.Coin, coins), player.Coins >= coins);
                    });
            else if (shifting || polestar)
                panel.TerrainWheel.ShowChoices(top, Rules.ShapeshiftTargets(state, faction),
                    terrain => terrain == _shiftTarget ? HighlightArt.Colour.White : HighlightArt.Colour.Purple);
        }
        else if (_lookWheel == panel)
            _lookWheel = null;

        if (shifting && _shiftTarget is Terrain target && panel.TerrainWheel?.CircleGlobalPosition(target) is Vector2 circle)
        {
            BonusActionId id = _bonus.Value.Bonus;
            bool spend = Rules.Apply(state, new BonusShapeshift(faction, id.Source, target, PayWithTokens: false)).Accepted;
            bool tokens = Rules.Apply(state, new BonusShapeshift(faction, id.Source, target, PayWithTokens: true)).Accepted;
            panel.ShowShiftPrices(circle, spend, tokens, colour);
        }
        else
            panel.ShowShiftPrices(null, false, false, colour);
        panel.ShowTokenOffer(player.TokenOfferOwed);
        ShowRockChoices(panel, state, player, animate); // the Geologists (MainFanFactionsEmberGeologistsChangelings.cs)
    }

    // The Shapeshifters' Spectral Polestar just placed: their home may move for free, on their turn (any lit terrain on
    // the open wheel; the tick on their panel gives it up).
    private static bool PolestarShiftOpen(GameState state, Faction faction) =>
        state.Phase == Phase.Playing && state.ActivePlayer == faction && state.IsInGame(faction)
        && state[faction].LandscapeOffers.HasFlag(LandscapeOffer.Shapeshift);

    // Where every priest on a Riverwalkers' wheel stands now (see _wheelPriestSpots).
    private void RememberWheelPriests(GameState state)
    {
        _wheelPriestSpots.Clear();
        foreach (PlayerState player in state.Players)
            if (player.WheelPriests.Count > 0 && PanelFor(player.Faction)?.TerrainWheel is ColourWheel wheel)
                foreach (WheelPriest locked in player.WheelPriests)
                    if (wheel.CircleGlobalPosition(locked.Terrain) is Vector2 at)
                        _wheelPriestSpots[locked.Priest] = at;
    }

    // ---------- Costs ----------

    // What turning this hex into `to` costs this faction, as amounts: the spades' workers (or priests), or a volcano
    // faction's price for Volcano. Null if it can't be done.
    private static Amounts TransformAmounts(GameState state, Faction faction, int hex, Terrain to)
    {
        if (to == Terrain.Volcano && Rules.VolcanoPrice(state, faction, hex) is int price)
            return VolcanoPriceAmounts(faction, price);
        return Rules.TransformCost(state, faction, hex, to) is Cost cost ? Amounts.From(cost) : null;
    }

    // A volcano faction's price for Volcano: power tokens removed (Dragonlords), steps paid from one track (Acolytes),
    // VP (fan factions: the Firewalkers; nothing for the Kingdom of Ember, who never transform).
    private static Amounts VolcanoPriceAmounts(Faction faction, int price) =>
        Amounts.Of(faction switch
        {
            Faction.Acolytes => ResourceKind.AnyCultStep,
            Faction.Firewalkers => ResourceKind.VictoryPoint,
            _ => ResourceKind.TokenRemoved,
        }, faction == Faction.Firewalkers ? -price : price);
}
