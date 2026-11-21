using System.Linq;
using Godot;

namespace TMCalc;

// The rest of Main: the score badges on the portraits (ScoreBadge, PlayerPanel.ShowScores; filled in by RenderPanels
// from EndGame.Preview). A file of the same class (partial), only to keep Main.cs a manageable size.
//   A click on a badge opens its score preview (what final scoring would give now), or closes it if open; it does
//   nothing else (a compact panel stays compact, a build offer or a priest choice still waits). Several can be open:
//   each badge clicked in turn opens.
//   Any other click closes every preview, then does whatever it would have done.
public partial class Main
{
    // The panel whose score badge is under the point, if any.
    private PlayerPanel ScoreBadgeUnder(Vector2 point) => _panels.FirstOrDefault(panel => panel.IsOverScoreBadge(point));

    private void ToggleScorePreview(PlayerPanel panel) => panel.SetScorePreviewOpen(!panel.ScorePreviewOpen);

    private void CloseScorePreviews()
    {
        foreach (PlayerPanel panel in _panels)
            panel.SetScorePreviewOpen(false);
    }
}
