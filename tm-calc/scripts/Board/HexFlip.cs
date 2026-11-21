using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TMCalc;

// A hex being transformed (MapView makes one; it frees itself when done). As a tile:
//   rising     it lifts straight up off the board (Rise seconds), growing to Thickness as it goes;
//   turning    it turns over from right to left about its upright middle line (Flip seconds), the old terrain on
//              top and the new underneath, its dark rim showing;
//   sinking    it settles back (Rise seconds), flattening again;
// then Landed runs (MapView puts the new terrain's picture on the hex). While it is off the board the empty
// socket shows under it. Nothing is drawn before Start (the hex still shows its old picture).
// Height moves a point up the screen (0.6 of it); the turn adds a touch of perspective, the lift none.
// Sizes in this node's pixels (MapView scales its exports by the hex radius).
public partial class HexFlip : Node2D
{
    public Color From, To;
    public float Radius, Lift, Thickness;
    public float RiseSeconds, FlipSeconds;
    public double Start;            // engine seconds (Now)
    public Action Landed;
    private readonly ulong _madeInFrame = Engine.GetProcessFrames();

    private static readonly Color Socket = new("161a18"), Rim = new("3a403c"), Line = new("161a18");
    private static readonly Vector3 Light = new Vector3(-0.35f, -0.45f, 0.82f).Normalized();
    private const float Height = 0.6f;          // how far up the screen a pixel of height moves a point
    private const float PerspectiveDistance = 5; // in radii
    // Faces seen almost edge-on (twice their screen area below this, in pixels²) are skipped: they would draw nothing
    // but their outline, which the faces beside them cover.
    private const float MinScreenArea = 2f;
    private bool _delayed;

    public static double Now => Time.GetTicksMsec() / 1000.0;
    public double Total => 2 * RiseSeconds + FlipSeconds;
    public double SecondsLeft => Math.Max(0, Start + Total - Now);

    // Starts it `seconds` from now instead (once, and only in the frame it was made: e.g. so a dwelling put on the hex
    // with the same move can get over it first; a flip already turning is never set back, e.g. by a redo part-way).
    public void Delay(float seconds)
    {
        if (_delayed || Engine.GetProcessFrames() != _madeInFrame)
            return;
        _delayed = true;
        Start = Now + seconds;
    }

    public override void _Process(double delta)
    {
        if (Now - Start >= Total)
        {
            Landed?.Invoke();
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    private static float Ease(float p) => p < 0.5f ? 2 * p * p : 1 - MathF.Pow(-2 * p + 2, 2) / 2;

    public override void _Draw()
    {
        float t = (float)(Now - Start);
        if (t < 0)
            return;
        float up, turn;
        if (t < RiseSeconds) { up = Ease(t / RiseSeconds); turn = 0; }
        else if (t < RiseSeconds + FlipSeconds) { up = 1; turn = (t - RiseSeconds) / FlipSeconds; }
        else { up = 1 - Ease(Mathf.Min(1, (t - RiseSeconds - FlipSeconds) / RiseSeconds)); turn = 1; }

        float thick = Thickness * up, lift = Lift * up, angle = Mathf.Pi * Ease(turn), d = Radius * PerspectiveDistance;
        float ct = MathF.Cos(angle), st = MathF.Sin(angle);
        // A point of the tile (x, y across the board, z up from its middle) turned, lifted and drawn: (screen point, height).
        (Vector2 At, float Z) Place(Vector3 p)
        {
            float x = p.X * ct - p.Z * st, z = p.X * st + p.Z * ct, s = d / (d - z);
            return (new Vector2(x * s, p.Y * s - (z + lift) * Height), z + lift);
        }
        Vector3 Turn(Vector3 n) => new(n.X * ct - n.Z * st, n.Y, n.X * st + n.Z * ct);

        Vector2[] socket = Corners(Radius).Select(c => new Vector2(c.X, c.Y)).ToArray();
        DrawColoredPolygon(socket, Socket);

        Vector3[] top = Corners(Radius).Select(c => new Vector3(c.X, c.Y, thick / 2)).ToArray();
        Vector3[] bottom = Corners(Radius).Select(c => new Vector3(c.X, c.Y, -thick / 2)).ToArray();
        var faces = new List<(Vector3[] Points, Vector3 Normal, Color Colour, bool IsRim)>
        {
            (top, Vector3.Back, From, false),                        // Vector3.Back is (0, 0, 1): up off the board
            (bottom.Reverse().ToArray(), Vector3.Forward, To, false),
        };
        for (var i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            Vector3 mid = (top[i] + top[j]) / 2;
            faces.Add((new[] { top[j], top[i], bottom[i], bottom[j] }, new Vector3(mid.X, mid.Y, 0).Normalized(), Rim, true));
        }
        // Far to near; only faces turned towards the viewer as drawn (rims only once there is some thickness): every
        // face's corners are listed clockwise as seen from outside the tile, so it faces the viewer when they still run
        // clockwise on screen. (Testing which way its normal points would ignore the tilted view, and show far sides
        // over the top.)
        foreach (var face in faces
            .Select(f => (f, placed: f.Points.Select(Place).ToArray(), normal: Turn(f.Normal)))
            .Where(x => ScreenArea(x.placed.Select(p => p.At).ToArray()) > MinScreenArea && (!x.f.IsRim || thick > 0.3f))
            .OrderBy(x => x.placed.Average(p => p.Z)))
        {
            float lit = Mathf.Max(0, face.normal.Dot(Light));
            Color colour = face.f.IsRim ? face.f.Colour.Lightened(0.25f * lit) : face.f.Colour.Darkened(0.45f * (1 - lit));
            Vector2[] points = face.placed.Select(p => p.At).ToArray();
            FillConvex(points, colour);
            DrawPolyline(points.Append(points[0]).ToArray(), Line, Mathf.Max(1.5f, Radius * (face.f.IsRim ? 0.04f : 0.07f)), true);
        }
    }

    // A convex face filled as a fan of triangles from its first corner. DrawColoredPolygon triangulates instead, which
    // fails on a face seen nearly edge-on ("Invalid polygon data, triangulation failed"); every face here stays convex
    // on screen (a flat convex shape seen in perspective, all of it in front of the eye), so a fan is always right.
    private void FillConvex(Vector2[] points, Color colour)
    {
        var indices = new int[(points.Length - 2) * 3];
        for (var i = 1; i < points.Length - 1; i++)
        {
            indices[3 * (i - 1)] = 0;
            indices[3 * (i - 1) + 1] = i;
            indices[3 * (i - 1) + 2] = i + 1;
        }
        var colours = new Color[points.Length];
        Array.Fill(colours, colour);
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), indices, points, colours);
    }

    // Twice the area inside the points, positive when they run clockwise on screen (y down).
    private static float ScreenArea(Vector2[] points)
    {
        float sum = 0;
        for (var i = 0; i < points.Length; i++)
            sum += points[i].Cross(points[(i + 1) % points.Length]);
        return sum;
    }

    // Pointy-topped, as HexArt: corner i at (60i − 30)°.
    private static IEnumerable<Vector2> Corners(float r) =>
        Enumerable.Range(0, 6).Select(i => Vector2.FromAngle((60 * i - 30) * Mathf.Pi / 180) * r);
}
