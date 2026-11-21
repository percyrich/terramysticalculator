using System;
using System.Linq;
using System.Collections.Generic;
using Core;
using Godot;

namespace TMCalc;

// The cult tracks and priest zones, drawn by CultArt in each cult's colour. Set up once; then each track's padlock
// shows until a faction reaches step 10. A third file of the same class as Main (partial), to keep Main.cs smaller.
public partial class Main
{
    private readonly Dictionary<Track, Sprite2D> _padlocks = new();

    // Gives each track and priest zone its picture, puts a padlock on each track, and moves each priest slot (and
    // its number) onto its circle in the zone's picture. The track and zone sprites are centred on their nodes, so
    // a point in a picture is placed at that point less half the picture's size.
    private void DressCults()
    {
        SpaceTracks();
        foreach (Track track in AllTracks)
        {
            Color colour = CultArt.Colour(track);

            // A Sprite2D, but its C# object is its script's (EvenlySpaced, a Node2D), so the picture is set by name.
            var trackSprite = _tracks.GetNode<Node2D>(track.ToString());
            trackSprite.Set(Sprite2D.PropertyName.Texture, CultArt.TrackTexture(colour));
            var padlock = new Sprite2D
            {
                Name = "Padlock",
                Texture = CultArt.Padlock(),
                Position = CultArt.PadlockCentre - CultArt.TrackSize / 2,
            };
            trackSprite.AddChild(padlock);
            _padlocks[track] = padlock;

            var zone = _priestZones.GetNode<Node2D>(track.ToString());
            zone.Set(Sprite2D.PropertyName.Texture, CultArt.PriestZone(colour));
            for (var slot = 0; slot < Config.PriestSlotsPerZone; slot++)
            {
                Rect2 area = CultArt.PriestSlot(slot);
                Node2D node = PriestSlotNode(track, slot);
                node.Position = area.GetCenter() - CultArt.PriestZoneSize / 2;
                if (node.GetNodeOrNull<CanvasItem>("SlotSprite") is CanvasItem oldSlot)
                    oldSlot.Visible = false; // the slot's circle is in the zone's picture now
                if (node.GetNodeOrNull<Label>("StepLabel") is Label label)
                {
                    // the number fills the circle's box and is centred in it, in the cult's colour lightened (on the dark)
                    label.Position = -area.Size / 2;
                    label.Size = area.Size;
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(area.Size.X * CultArt.NumberSize));
                    label.AddThemeColorOverride("font_color", CultArt.NumberColour(colour));
                }
            }
        }
        AlignPriestZones();
    }

    // Each track TrackGap to the right of the one before (measured between the columns of steps, not the pictures,
    // whose edges are see-through), in the order fire, water, earth, air.
    private void SpaceTracks()
    {
        float pitch = CultArt.Step(0).Size.X + TrackGap;
        Vector2 first = _tracks.GetNode<Node2D>(AllTracks[0].ToString()).Position;
        for (var i = 0; i < AllTracks.Length; i++)
        {
            var track = _tracks.GetNode<Node2D>(AllTracks[i].ToString());
            track.Position = first + new Vector2(i * pitch * track.Scale.X, 0);
        }
    }

    // Lays the priest zones out so that, small, each sits exactly under its track: as wide as the track's column of
    // steps, centred on it, PriestZoneGap below its bottom step. They grow from the top centre of the small row
    // of zones (halfway between the fire and air zones' centres), so the full-size zones open downwards and out to
    // both sides, clear of the tracks.
    // Worked out in the board's pixels (the parent of Tracks and PriestZones); the zones' scale as set in the editor
    // is their full size, and its position no longer matters (the corner fixes where they are).
    private void AlignPriestZones()
    {
        var board = _priestZones.GetParent<Node2D>();
        float full = _priestZones.FullScale.X;
        Vector2 half = CultArt.PriestZoneSize / 2;
        Rect2 column = CultArt.Step(0); // the column of steps 0 to 9: its width, and its bottom
        Vector2 trackCentre = CultArt.TrackSize / 2;

        // Where each small zone's centre goes, and how big a zone is when small, in the board's pixels.
        var centres = new Dictionary<Track, Vector2>();
        float small = 0f;
        foreach (Track track in AllTracks)
        {
            var trackSprite = _tracks.GetNode<Node2D>(track.ToString());
            float scale = trackSprite.GlobalScale.X / board.GlobalScale.X;
            Vector2 at = board.ToLocal(trackSprite.GlobalPosition);
            small = column.Size.X * scale / CultArt.PriestZoneSize.X;
            float bottom = at.Y + (column.End.Y - trackCentre.Y) * scale;
            float x = at.X + (column.GetCenter().X - trackCentre.X) * scale;
            centres[track] = new Vector2(x, bottom + PriestZoneGap + half.Y * small);
        }

        // Small, the zones node is at `shrunk` with scale `small`; full size at FullPosition with scale `full`. The
        // corner (the row's top centre) is in the same place in both: shrunk + small × corner = FullPosition +
        // full × corner. With each zone at (centre − shrunk) / small, that gives `shrunk`.
        Vector2 cornerOnBoard = new((centres[Track.Fire].X + centres[Track.Air].X) / 2, centres[Track.Air].Y - half.Y * small);
        Vector2 shrunk = (full - small) / full * cornerOnBoard + small / full * _priestZones.FullPosition;
        foreach (Track track in AllTracks)
            _priestZones.GetNode<Node2D>(track.ToString()).Position = (centres[track] - shrunk) / small;
        Vector2 corner = (cornerOnBoard - shrunk) / small;
        _priestZones.Reshape(corner, small / full);
    }

    // Each track's padlock shows until a faction is on step 10.
    private void ShowPadlocks(GameState state)
    {
        foreach (Track track in AllTracks)
            if (_padlocks.TryGetValue(track, out Sprite2D padlock))
                padlock.Visible = state.OnStep(track, Config.TrackMax).Count == 0;
    }

    // One tracker per faction per track, on the step the faction has reached.
    // Factions on the same step stand side by side rather than on top of each other.
    private void RenderTrackers(GameState state, bool animate)
    {
        var wanted = new HashSet<(Faction, Track)>();

        // Trackers sharing a step sit side by side, centred on it, in the order they arrived there
        // (from the game state, so an undo puts them back in the same order).
        foreach (Track track in AllTracks)
        {
            for (var step = 0; step <= Config.TrackMax; step++)
            {
                ValueList<Faction> here = state.OnStep(track, step);
                Vector2 centre = _pieceLayer.ToLocal(TrackStepNode(track, step).GlobalPosition);
                for (var i = 0; i < here.Count; i++)
                {
                    var key = (here[i], track);
                    wanted.Add(key);
                    Vector2 target = centre + new Vector2((i - (here.Count - 1) / 2f) * TrackerSpacing, 0);
                    Sprite2D sprite = SpriteFor(_trackerSprites, key, target);
                    sprite.Texture = Art.TrackerTexture(state[here[i]].Colour);
                    Slide(sprite, target, animate);
                }
            }
        }

        foreach (var gone in _trackerSprites.Keys.Where(key => !wanted.Contains(key)).ToList())
        {
            RemoveSprite(_trackerSprites[gone]);
            _trackerSprites.Remove(gone);
        }
        ShowPadlocks(state);
    }
}
