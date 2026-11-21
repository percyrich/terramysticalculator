using System.Collections.Generic;
using Godot;

namespace TMCalc;

// Fades items in and out through their Modulate alpha, leaving the tint (Modulate's colour) alone.
// A new fade of an item cancels its earlier one.
public sealed class Fader
{
    private readonly Dictionary<CanvasItem, Tween> _running = new();
    private readonly Dictionary<CanvasItem, float> _targets = new(); // where each running fade is going

    // To `alpha`: after `delay` seconds, over `seconds` seconds (both 0: at once). A fade already running to the same
    // alpha carries on (rendering again part-way through, e.g. a redo, doesn't start it again).
    public void To(CanvasItem item, float alpha, float seconds = 0f, float delay = 0f)
    {
        if ((seconds > 0f || delay > 0f) && _running.TryGetValue(item, out Tween going) && going.IsValid() && going.IsRunning()
            && _targets.TryGetValue(item, out float to) && Mathf.IsEqualApprox(to, alpha))
            return;
        Forget(item);
        if (Mathf.IsEqualApprox(item.Modulate.A, alpha) || (seconds <= 0f && delay <= 0f))
        {
            SetAlpha(item, alpha);
            return;
        }

        Tween tween = item.CreateTween();
        if (delay > 0f)
            tween.TweenInterval(delay);
        if (seconds > 0f)
            tween.TweenProperty(item, "modulate:a", alpha, seconds);
        else
            tween.TweenCallback(Callable.From(() => SetAlpha(item, alpha)));
        _running[item] = tween;
        _targets[item] = alpha;
    }

    // Stops any fade of the item (e.g. before it is freed).
    public void Forget(CanvasItem item)
    {
        _targets.Remove(item);
        if (_running.Remove(item, out Tween running))
            running.Kill();
    }

    private static void SetAlpha(CanvasItem item, float alpha) => item.Modulate = item.Modulate with { A = alpha };
}
