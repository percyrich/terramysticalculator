using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the end of the game's scores and statistics (EndgameScores; Core.GameStatistics from the log as
// far as the game's end). It opens by itself when the game is finished (RenderFinalScores); while it is open the step
// button shows its dots, and every input but its own goes to it (Esc closes it). Closed, the step button's tick opens
// it again. A file of the same class (partial), only to keep Main.cs a manageable size.
public partial class Main
{
    private EndgameScores _endgame;

    private void OpenEndgame()
    {
        GameState state = _history.Current;
        if (state.Phase != Phase.Finished)
            return;
        if (_endgame is null)
        {
            Node layer = (Node)_modal?.GetParent() ?? this; // the message boxes' layer (MainBgaImport.cs)
            layer.AddChild(_endgame = new EndgameScores { Name = "Endgame" });
            _endgame.Closed += () =>
            {
                _finalScoresClosed = true;
                RenderControls(_history.Current); // the tick again
            };
        }
        GameStats stats = GameStatistics.Compute(_history.Log().Take(_history.LogIndex + 1).ToList(), state);
        _endgame.Open(stats, _topPanel.FrameColour, _topPanel.PanelScale);
        RenderControls(state); // the dots
    }

    private void CloseEndgame()
    {
        if (_endgame?.IsOpen == true)
            _endgame.Close();
    }

    // While it is open: Esc closes it, and nothing else here sees the event (its own keys and tabs do).
    private bool OnEndgameInput(InputEvent @event)
    {
        if (_endgame is null || !_endgame.IsOpen)
            return false;
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            _endgame.Close();
        }
        return true;
    }
}
