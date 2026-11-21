using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: victory points (and some coins and workers) shown arriving. For each point a player gains
// (VictoryPointsGained with a source and a trigger: Core says what scored it and what set that off), a GatheringLight
// gathers on the source and the trigger and flies to the player's VP count, one point at a time, PointsIntervalSeconds apart
// however many moves queue them (so points arrive at a steady rate); a point lost is the same light in
// PointsLossColour. The end of a round's cult rewards in coins and workers come the same way, one light per coin or
// worker, to those counts: from the round tile that ended and the player's marker on its track (or, for coins per
// priest in the priest zones, each of their priests there). Spades and the other rewards are not shown. Each count
// ticks as its lights land (PlayerPanel.SetInFlight). Moves made here (Submit) and redone (StepForward, from the
// position's events) show them; undo and history jumps stop any still to come at once, and the counts show the true
// totals. A file of the same class (partial), only to keep Main.cs a manageable size.
// Areas are measured when the move's lights are queued, just after its render starts (so a tile on its way somewhere,
// e.g. the pass tile given back, is measured where it was); the target is followed every frame (a panel may move).
public partial class Main
{
    private readonly Queue<PointShot> _pointShots = new(); // (ExportGroup("") must stand on a field or property)
    private readonly List<(GatheringLight Light, Faction Faction, ResourceKind Kind)> _pointLights = new();
    private readonly Dictionary<(Faction, ResourceKind), int> _inFlight = new(); // queued or flying, signed
    private double _nextShotAt;                                                    // engine seconds

    // One light: from where to where. A gain: an amount of one resource with what to show it from (worked out when
    // queued; null: nothing to show it from, so it is counted at once).
    private sealed record PointShot(Faction Faction, ResourceKind Kind, int Sign, LightArea Source, LightArea Trigger);
    private sealed record Gain(Faction Faction, ResourceKind Kind, int Amount, Func<LightArea?> Source, Func<LightArea?> Trigger);

    private static double NowSeconds => Time.GetTicksMsec() / 1000.0;

    // What an accepted move gains that can be shown (a source, a trigger and a panel to fly to), counted as in flight
    // before the render, so the panels show the counts without it. before: the state the move was made in.
    private List<Gain> TakeGainsInFlight(MoveResult result, GameState before)
    {
        var gains = new List<Gain>();
        if (!result.Accepted)
            return gains;
        foreach (GameEvent happened in result.Events)
            switch (happened)
            {
                case VictoryPointsGained { Source: PointsArea source, Trigger: PointsArea trigger } gained:
                    gains.Add(new Gain(gained.Faction, ResourceKind.VictoryPoint, gained.Amount,
                        () => AreaFor(source, gained.Faction, _history.Current), () => AreaFor(trigger, gained.Faction, _history.Current)));
                    break;
                case RoundEndRewardReceived reward:
                    gains.AddRange(RoundRewardGains(reward, before));
                    break;
                // Fan factions: the Goblins' treasure from the town's key or the temple or sanctuary built; the
                // Treasurers' Safe, doubled, from the Safe to their counts.
                case TreasureGained treasure:
                {
                    PointsArea from = result.Events.OfType<TownFormed>().Where(town => town.Faction == treasure.Faction)
                        .Select(town => (PointsArea)new PointsArea.KeyOn(town.Hex)).FirstOrDefault()
                        ?? result.Events.OfType<BuildingUpgraded>().Where(built => built.Building.Owner == treasure.Faction)
                            .Select(built => (PointsArea)new PointsArea.OnHex(built.Hex)).FirstOrDefault();
                    if (from is not null)
                        gains.Add(new Gain(treasure.Faction, ResourceKind.Treasure, 1,
                            () => AreaFor(from, treasure.Faction, _history.Current), () => AreaFor(from, treasure.Faction, _history.Current)));
                    break;
                }
                case SafeOpened opened:
                {
                    Func<LightArea?> safe = () => PanelFor(opened.Faction)?.BookNode is Node2D book ? ExtentArea(book) : null;
                    gains.Add(new Gain(opened.Faction, ResourceKind.Coin, opened.Coins, safe, safe));
                    gains.Add(new Gain(opened.Faction, ResourceKind.Worker, opened.Workers, safe, safe));
                    break;
                }
            }
        gains.RemoveAll(gain => gain.Amount == 0 || PanelFor(gain.Faction) is null);
        foreach (Gain gain in gains)
            _inFlight[(gain.Faction, gain.Kind)] = _inFlight.GetValueOrDefault((gain.Faction, gain.Kind)) + gain.Amount;
        return gains;
    }

    // A round's cult reward in coins and workers: from that round's tile (the round the move ended) and the player's
    // marker on its track; coins per priest in the priest zones from each of their priests there.
    private IEnumerable<Gain> RoundRewardGains(RoundEndRewardReceived reward, GameState before)
    {
        if (before.Round < 1 || before.Round > before.ChosenRoundTiles.Count)
            yield break;
        int index = before.Round - 1;
        RoundTileDefinition tile = RoundTiles.All[before.ChosenRoundTiles[index]];
        Faction faction = reward.Faction;
        Func<LightArea?> roundTile = () => _roundTileSprites[index] is Node2D view ? ExtentArea(view) : null;
        int coins = reward.Income.Coins, workers = reward.Income.Workers;
        if (tile.CoinsPerPriestOnTracks > 0 && coins > 0)
        {
            List<Piece> priests = before.PriestZones.SelectMany(zone => zone)
                .Where(slot => slot is Piece priest && priest.Owner == faction).Select(slot => slot!.Value).ToList();
            if (priests.Count * tile.CoinsPerPriestOnTracks == coins)
                foreach (Piece priest in priests)
                    yield return new Gain(faction, ResourceKind.Coin, tile.CoinsPerPriestOnTracks, roundTile, () => PieceArea(priest));
            else
                yield return new Gain(faction, ResourceKind.Coin, coins, roundTile, roundTile);
            coins = 0;
        }
        Func<LightArea?> marker = tile.Track is Track track ? () => TrackerArea(faction, track) : roundTile;
        if (coins > 0)
            yield return new Gain(faction, ResourceKind.Coin, coins, roundTile, marker);
        if (workers > 0)
            yield return new Gain(faction, ResourceKind.Worker, workers, roundTile, marker);
    }

    // After the render: one shot per point (coin, worker), in the order Core gave them.
    private void QueueGains(List<Gain> gains)
    {
        foreach (Gain gain in gains)
        {
            if (gain.Source() is not LightArea from || gain.Trigger() is not LightArea by)
            {
                Land(gain.Faction, gain.Kind, gain.Amount); // nothing to show it from: counted at once
                continue;
            }
            int sign = gain.Amount > 0 ? 1 : -1;
            for (var i = 0; i < Mathf.Abs(gain.Amount); i++)
                _pointShots.Enqueue(new PointShot(gain.Faction, gain.Kind, sign, from, by));
        }
        _nextShotAt = Math.Max(_nextShotAt, NowSeconds);
    }

    // Every frame: the next shots that are due, and each flying light's target where its count now is.
    private void UpdatePointLights()
    {
        double now = NowSeconds;
        while (_pointShots.Count > 0 && now >= _nextShotAt)
        {
            PointShot shot = _pointShots.Dequeue();
            _nextShotAt += PointsIntervalSeconds;
            if (PanelFor(shot.Faction) is not PlayerPanel panel)
            {
                Land(shot.Faction, shot.Kind, shot.Sign);
                continue;
            }
            GatheringLight light = GatheringLight.Play(_board, shot.Source, shot.Trigger, _board.ToLocal(panel.HoldingCentre(shot.Kind)));
            if (shot.Sign < 0)
                light.Modulate = PointsLossColour;
            var entry = (light, shot.Faction, shot.Kind);
            _pointLights.Add(entry);
            light.Landed += () => Land(shot.Faction, shot.Kind, shot.Sign);
            light.Finished += () => _pointLights.Remove(entry);
        }
        foreach (var (light, faction, kind) in _pointLights)
        {
            if (!IsInstanceValid(light) || PanelFor(faction) is not PlayerPanel panel)
                continue;
            Vector2 target = _board.ToLocal(panel.HoldingCentre(kind));
            if (target.DistanceSquaredTo(light.Target) > 0.25f)
            {
                light.Target = target;
                light.Refresh();
            }
        }
    }

    private void Land(Faction faction, ResourceKind kind, int amount)
    {
        int left = _inFlight.GetValueOrDefault((faction, kind)) - amount;
        if (left == 0)
            _inFlight.Remove((faction, kind));
        else
            _inFlight[(faction, kind)] = left;
        PanelFor(faction)?.ShowInFlight(kind, left);
    }

    // Before a panel shows its player: what is still flying to it.
    private void ShowInFlightOn(PlayerPanel panel, Faction faction)
    {
        panel.ClearInFlight();
        foreach (var ((owner, kind), amount) in _inFlight)
            if (owner == faction)
                panel.SetInFlight(kind, amount);
    }

    // Undo, redo, a history jump: every light still to come or flying goes, and the counts show the true totals
    // (the render that follows shows them).
    private void StopPointLights()
    {
        _pointShots.Clear();
        foreach (var (light, _, _) in _pointLights)
            if (IsInstanceValid(light))
                light.QueueFree();
        _pointLights.Clear();
        _inFlight.Clear();
        foreach (PlayerPanel panel in _panels)
            panel.ClearInFlight();
    }

    // A piece's sprite (a priest in a zone) and a cult track marker, as ellipses round what they draw.
    private LightArea? PieceArea(Piece piece) =>
        _pieceSprites.TryGetValue(piece, out Sprite2D sprite) ? EllipseArea(sprite) : null;

    private LightArea? TrackerArea(Faction faction, Track track) =>
        _trackerSprites.TryGetValue((faction, track), out Sprite2D sprite) ? EllipseArea(sprite) : null;

    private LightArea EllipseArea(Sprite2D sprite)
    {
        LightArea box = RoundedArea(sprite, Arrangement.VisibleRect(sprite), 0f);
        return LightArea.Ellipse(box.Centre, box.Size);
    }

    // ---------- Where things are, as light areas in the board's pixels ----------

    private LightArea? AreaFor(PointsArea area, Faction faction, GameState state)
    {
        PlayerPanel panel = PanelFor(faction);
        switch (area)
        {
            case PointsArea.OnHex on:
                return LightArea.MapHex(_map, on.Hex, _board);
            case PointsArea.KeyOn key: // on its way there now: where it lands, the hex at the key's size
                LightArea hex = LightArea.MapHex(_map, key.Hex, _board);
                return LightArea.Hex(hex.Centre, hex.HexRadius * KeyOnMapScale);
            case PointsArea.OnBridge on when _bridgeSprites.TryGetValue(on.Bridge, out Sprite2D bridge):
                return RoundedArea(bridge, Arrangement.VisibleRect(bridge), 4f);
            case PointsArea.RoundTile when state.Round >= 1 && _roundTileSprites[state.Round - 1] is Node2D tile:
                return ExtentArea(tile);
            case PointsArea.Favour favour:
            {
                FavourTile? held = state[faction].FavourTiles.Where(t => t.Id == favour.Id).Select(t => (FavourTile?)t).FirstOrDefault();
                return held is FavourTile owned && _favourSprites.TryGetValue(owned, out Node2D view) ? ExtentArea(view) : null;
            }
            case PointsArea.PassTile pass when _passTileSprites.TryGetValue(pass.Id, out Node2D view):
                return ExtentArea(view);
            case PointsArea.Portrait when panel?.Portrait is Sprite2D portrait:
            {
                LightArea box = RoundedArea(portrait, Arrangement.VisibleRect(portrait), 0f);
                return LightArea.Ellipse(box.Centre, box.Size);
            }
            case PointsArea.Shipping when panel?.GetNodeOrNull<Node2D>("Shipping") is Node2D shipping:
                return ExtentArea(shipping);
            case PointsArea.Spades when panel?.GetNodeOrNull<Node2D>("Spades") is Node2D spades:
                return ExtentArea(spades);
            case PointsArea.BowlThree when panel is not null:
                return RoundedArea(panel.PowerBank, PowerBowlsView.BowlRect(3), PointsTileCornerRadius);
            default:
                return null;
        }
    }

    // What a node draws (Arrangement.Extent), as a rounded rectangle; null if it draws nothing measurable.
    private LightArea? ExtentArea(Node2D node)
    {
        Rect2 drawn = Arrangement.Extent(node); // in its parent's pixels, round the node's origin
        if (drawn.Size == Vector2.Zero || node.GetParent() is not CanvasItem parent)
            return null;
        drawn.Position += node.Position;
        return RoundedArea(parent, drawn, PointsTileCornerRadius);
    }

    // A rectangle in a node's own pixels, as a rounded rectangle in the board's (upright, holding it).
    private LightArea RoundedArea(CanvasItem node, Rect2 rect, float cornerRadius)
    {
        Transform2D toBoard = _board.GetGlobalTransform().AffineInverse() * node.GetGlobalTransform();
        Vector2[] corners = { rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y) };
        var box = new Rect2(toBoard * corners[0], Vector2.Zero);
        foreach (Vector2 corner in corners.Skip(1))
            box = box.Expand(toBoard * corner);
        return LightArea.RoundedRect(box, cornerRadius);
    }
}
