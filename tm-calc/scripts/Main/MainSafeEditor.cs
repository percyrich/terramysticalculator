using Core;
using Godot;

namespace TMCalc;

// Setup (a game in progress): the Treasurers' Safe clicked opens its editor under it (PlayerPanel.ShowSafeEditor), a
// priest, a worker and a coin as the power conversions' menu: left click puts one in, right click takes one out,
// Shift ten (EditSafe). It stays open while it is used; Esc, a click on the Safe, or any other click closes it (the
// other click then does what it would have done anyway). Closed whenever setup ends or the Treasurers aren't selected.
public partial class Main
{
    private PlayerPanel _safeEditPanel; // the panel whose Safe editor is open (null: none)

    private void OpenSafeEditor(PlayerPanel panel)
    {
        _safeEditPanel = panel;
        RenderSafeEditor(_history.Current);
    }

    private void CloseSafeEditor()
    {
        _safeEditPanel?.ShowSafeEditor(false);
        _safeEditPanel = null;
    }

    // After every render: still open only on the selected Treasurers' full panel during setup.
    private void RenderSafeEditor(GameState state)
    {
        if (_safeEditPanel is not PlayerPanel panel)
            return;
        if (state.Phase != Phase.SettingUp || state.ActivePlayer != Faction.Treasurers
            || panel.ShownFaction != Faction.Treasurers || panel.IsCompact || !_board.Visible)
        {
            CloseSafeEditor();
            return;
        }
        panel.ShowSafeEditor(true);
    }

    // A press while the editor is open (from OnModeInput): true if it was used up here.
    private bool OnSafeEditorPress(Vector2 mouse, bool right)
    {
        if (_safeEditPanel is not PlayerPanel panel)
            return false;
        if (panel.SafeEditorKindAt(mouse) is ResourceKind kind)
        {
            GetViewport().SetInputAsHandled();
            int delta = (right ? -1 : 1) * (Input.IsKeyPressed(Key.Shift) ? 10 : 1);
            Submit(new EditSafe(Faction.Treasurers, kind == ResourceKind.Coin ? delta : 0,
                kind == ResourceKind.Worker ? delta : 0, kind == ResourceKind.Priest ? delta : 0));
            return true;
        }
        if (panel.IsOverSafeEditor(mouse))
        {
            GetViewport().SetInputAsHandled();
            return true;
        }
        bool onSafe = panel.IsOverSafe(mouse);
        CloseSafeEditor();
        if (!onSafe)
            return false;
        GetViewport().SetInputAsHandled(); // the Safe's own press only closes it
        return true;
    }
}
