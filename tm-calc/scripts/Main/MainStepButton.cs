using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the step button: the top panel's game button (TopPanel.GameButton, a PlayerButton in the power tokens' purple; its
// GamePressed is wired in MainTopPanel.cs), always shown.
//   Waiting (the dots): something must be done before the game can go on: select the game, choose factions (or a
//     choice owed), set the turn order, choose a map, pick the round tiles or enough pass tiles, place starting
//     dwellings, pick pass tiles, the round-end cult steps and spades.
//   Confirm (the tick): the step can be finished as it is; pressing it does that: the game (with its options), the
//     factions, the turn order, the round tiles, starting with the pass tiles chosen (StartGame),
//     no more free spades (FinishSpades: those left are lost, the build offer declined), no more of the Riverwalkers'
//     free bridges (SkipBridges), no more of the Darklings' worker exchanges (FinishPriestExchange), the Cultists' cult
//     step given up (DeclineCultStep), the Shapeshifters' power token declined (AnswerTokenOffer, no), the stronghold's
//     held power or tokens taken (TakeHeldPower: the Alchemists, the Children of the Wyrm). Each only when the rules
//     would accept it.
//   Blank: otherwise (an ordinary turn: the panels' player buttons end turns), and once the game is over.
public partial class Main
{
    private PlayerButton _stepButton;

    // After ReadyTopPanel.
    private void ReadyStepButton()
    {
        _stepButton = _topPanel.GameButton;
    }

    private void RenderStepButton(GameState state)
    {
        if (_stepButton is null)
            return;
        _stepButton.Show(StepMode(state), _topPanel.GameButtonColour, animate: true);
        // Setup's Done: clickable with its dots too, to show what still needs fixing (MainModes.OnSetupDone).
        if (state.Phase == Phase.SettingUp)
        {
            _stepButton.Disabled = false;
            _stepButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        }
    }

    private PlayerButton.Mode StepMode(GameState state)
    {
        bool Can(Move move) => Rules.Apply(state, move).Accepted;
        return state.Phase switch
        {
            Phase.ChoosingGame => _gameChosen ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting,
            Phase.ChoosingFactions => Can(new ConfirmFactions()) ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting,
            Phase.ChoosingTurnOrder => state.IsTurnOrderComplete ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting,
            Phase.ChoosingRoundTiles => Can(new ConfirmRoundTiles()) ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting, // Calculator: any number
            Phase.ChoosingPassTiles => Can(new StartGame()) ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting,
            Phase.ChoosingVolcanoTerrain or Phase.ChoosingMap or Phase.PlacingDwellings or Phase.PickingPassTiles => PlayerButton.Mode.Waiting,
            Phase.Playing or Phase.RoundEndSpades when StepMove(state) is Move move && Can(move) => PlayerButton.Mode.Confirm,
            Phase.RoundEndSpades => PlayerButton.Mode.Waiting,
            Phase.SettingUp => Rules.SetupIssues(state).Count == 0 ? PlayerButton.Mode.Confirm : PlayerButton.Mode.Waiting, // Done
            Phase.OrderingTurns => PlayerButton.Mode.Waiting,
            Phase.Finished => _endgame?.IsOpen == true ? PlayerButton.Mode.Waiting : PlayerButton.Mode.Confirm, // the scores (MainEndgame.cs)
            _ => PlayerButton.Mode.Blank, // an ordinary turn; the game over
        };
    }

    // During play, what the tick would finish for the active player, if anything.
    private static Move StepMove(GameState state)
    {
        if (state.ActivePlayer is not Faction active)
            return null;
        PlayerState player = state[active];
        if (player.BridgesOptional && player.BridgesOwed > 0)
            return new SkipBridges(active);
        if (state.SpadesPending || player.RockMovesOwed > 0) // (fan factions: the Geologists' rock moves given up)
            return new FinishSpades(active);
        if (player.PriestExchangesLeft > 0)
            return new FinishPriestExchange(active);
        if (active == Faction.Cultists && player.CultStepsOwed > 0 && state.PowerOffers.Count == 0)
            return new DeclineCultStep(active); // the Cultists give up their step (BGA lets them)
        if (player.TokenOfferOwed)
            return new AnswerTokenOffer(active, Accept: false); // Shapeshifters keep their VP (a click elsewhere does the same)
        if ((player.PowerHeld > 0 || player.TokensHeld > 0) && state.PowerOffers.Count == 0)
            return new TakeHeldPower(active); // the stronghold's power or tokens, now (else at End turn)
        return null;
    }

    // The tick (the button is Disabled in any other mode).
    private void OnStep()
    {
        GameState state = _history.Current;
        switch (state.Phase)
        {
            case Phase.ChoosingGame or Phase.ChoosingFactions or Phase.ChoosingTurnOrder or Phase.ChoosingRoundTiles:
                OnProceed();
                break;
            case Phase.ChoosingPassTiles:
                Submit(new StartGame());
                break;
            case Phase.SettingUp:
                OnSetupDone(); // MainModes.cs
                break;
            case Phase.Playing or Phase.RoundEndSpades when StepMove(state) is Move move:
                Submit(move);
                break;
            case Phase.Finished:
                OpenEndgame(); // the scores again (MainEndgame.cs)
                break;
        }
    }
}
