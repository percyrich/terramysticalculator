using System;
using Godot;

namespace TMCalc;

// A node that draws whatever Paint draws, for drawing made in code on a node of its own (e.g. MapGlow's
// animated parts, which go over its lines). QueueRedraw() it to draw again.
public partial class Painter : Node2D
{
    public Action<Painter> Paint;

    public override void _Draw() => Paint?.Invoke(this);
}
