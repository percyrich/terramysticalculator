using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the Kingdom of Ember's lava flow, the Geologists' wheel and rocks, and the Changelings. A file of
// the same class (partial).
//   Kingdom of Ember's lava flow: a click on a structure of theirs with lava starts it (it stays where it is, on its
//     stack); each lit hex clicked moves it on, and the board shows the flow so far (Rules.LavaFlowSoFar, through
//     FlowShown in Render). A step (FlowStep): the stack, the structure still on top, slides to the
//     clicked hex at its height; there the tiles spent come off its top, the structure drops onto what is left, and
//     the hex becomes Volcano (its picture changes, no turning over); taking a step back runs the same way. A click on the last hex takes that step back (Esc: all of it). The
//     tick (Done), or no step left, ends the path: the bridge spots the flow may take light up (if any), a click
//     chooses one (again: unchosen), and the tick submits the whole flow as one move. Their key: only the town's
//     strongest structures light up.
//   Geologists' wheel: small on the panel with their rocks; it opens on a click and while a rock choice is owed (a spade,
//     their stronghold's octagon, or any time in Setup): coverable terrains lit white, liftable purple.
//   Changelings: B and C on the faction screen (once every other terrain is chosen), on the wheel over their portrait:
//     the first click is B (clicked again: unchosen), the second C.
public partial class Main
{
    private int? _flowFrom;
    private readonly List<int> _flowPath = new();
    private bool _flowBridging;
    private (int A, int B)? _flowBridge;
    private readonly List<((int A, int B) Edge, Sprite2D Sprite)> _flowSpots = new();
    private Terrain? _changelingB;

    // A step of the flow being shown: the structure, from and to which hex, its stack's tiles before it, when the stack
    // lands (engine seconds), the hexes whose picture changes then; Fresh until the structure's own move is set.
    private sealed class FlowStep
    {
        public Piece Piece;
        public int From, To, Discs;
        public double Lands;
        public int[] Switched;
        public bool Fresh = true;
    }
    private FlowStep _flowStep;
    private GameState _flowShownBefore; // the board as shown before a step clicked or taken back (Render uses it once)
    private Piece? _flowMoving;

    // From OnPressFlow, before a step is added or taken back.
    private void NoteFlowStep(GameState state)
    {
        _flowShownBefore = FlowShown(state);
        _flowMoving = _flowFrom is int from ? state.Map[from] : null;
    }

    // From Render: the step just made (the board as it was before it, then as now), or the one still under way.
    private FlowStep NewFlowStep(GameState shown, bool animate)
    {
        GameState before = _flowShownBefore;
        _flowShownBefore = null;
        static int? HexOf(GameState s, Piece piece) =>
            Enumerable.Range(0, s.Map.Count).Where(hex => s.Map[hex] == piece).Select(hex => (int?)hex).FirstOrDefault();
        if (animate && before is not null && _flowMoving is Piece piece && HexOf(before, piece) is int from
            && HexOf(shown, piece) is int to && from != to && before.Layout.Equals(shown.Layout))
        {
            var step = new FlowStep
            {
                Piece = piece, From = from, To = to, Discs = before.LavaAt(from), Lands = NowSeconds + SlideSeconds - 0.02,
                Switched = Enumerable.Range(0, shown.HexTerrain.Count).Where(hex => before.HexTerrain[hex] != shown.HexTerrain[hex]).ToArray(),
            };
            // Once it has landed, the tiles spent come off the stack.
            GetTree().CreateTimer(SlideSeconds).Timeout += () => RenderFanMap(FlowShown(_history.Current), animate: true);
            return step;
        }
        return _flowStep is { } running && NowSeconds < running.Lands ? running : null;
    }

    // The flowing structure, after RenderPieces: carried at its stack's old height to the hex, then dropped onto what is
    // left of the stack there.
    private void AnimateFlowStep(GameState shown)
    {
        if (_flowStep is not FlowStep step || !step.Fresh || !_pieceSprites.TryGetValue(step.Piece, out Sprite2D sprite))
            return;
        step.Fresh = false;
        Vector2 centre = _pieceLayer.ToLocal(_map.Hex(step.To).GlobalPosition);
        Vector2 carried = centre - new Vector2(0, step.Discs * IconArt.LavaLift(LavaDiscPixels));
        Vector2 landed = centre - new Vector2(0, LavaRaise(shown, step.To));
        StopSlide(sprite);
        Tween tween = sprite.CreateTween();
        tween.TweenProperty(sprite, "position", carried, SlideSeconds).SetTrans(SlideTransition).SetEase(SlideEase);
        tween.TweenProperty(sprite, "position", landed, Mathf.Max(0.05f, SlideSeconds * 0.4f))
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _tweens[sprite] = tween;
        _tweenTargets[sprite] = (landed, false); // (renders meanwhile let it carry on: Slide's Going)
    }

    private void EndFlow()
    {
        _flowFrom = null;
        _flowPath.Clear();
        _flowBridging = false;
        _flowBridge = null;
    }

    // ---------- Kingdom of Ember: the flow ----------

    private Move FlowMove(Faction faction, IEnumerable<int> path, (int A, int B)? bridge = null) =>
        new LetLavaFlow(faction, _flowFrom!.Value, new ValueList<int>(path), bridge?.A, bridge?.B);

    // The hexes the flow could go on to now: each a step that ends a legal flow, or a river it could cross.
    private List<int> FlowNext(GameState state, Faction faction)
    {
        int last = _flowPath.Count > 0 ? _flowPath[^1] : _flowFrom!.Value;
        var result = new List<int>();
        foreach (int n in state.Layout.Neighbours(last).Distinct())
        {
            if (n == _flowFrom || _flowPath.Contains(n))
                continue;
            var path = _flowPath.Append(n).ToList();
            if (Rules.Apply(state, FlowMove(faction, path)).Accepted
                || state.HexTerrain[n] == Terrain.River && state.Layout.Neighbours(n).Any(m => m != last && !path.Contains(m) && m != _flowFrom
                    && Rules.Apply(state, FlowMove(faction, path.Append(m))).Accepted))
                result.Add(n);
        }
        return result;
    }

    // The bridges the finished flow could take (across a river it crossed, one end theirs).
    private List<(int A, int B)> FlowBridges(GameState state, Faction faction) =>
        Rules.BridgeEdges(state)
            .Where(edge => Rules.Apply(state, FlowMove(faction, _flowPath, edge)).Accepted)
            .Select(edge => (edge.HexA, edge.HexB)).ToList();

    private bool OnPressFlow(Vector2 mouse)
    {
        GameState state = _history.Current;
        if (state.Phase != Phase.Playing || state.ActivePlayer is not Faction active || active != Faction.KingdomOfEmber)
            return false;
        int? hex = _map.HexAt(mouse);
        if (_flowFrom is null)
        {
            if (hex is int start && state.LavaAt(start) > 0 && state.Map[start] is { } piece && piece.Owner == active)
            {
                _flowFrom = start;
                if (FlowNext(state, active).Count == 0)
                {
                    EndFlow(); // can't flow from here: as usual (the upgrades)
                    return false;
                }
                ShowPrompt("Click lit hexes for the lava's path, then Done. (Click the last hex again to take it back; Esc to cancel.)");
                Render(animate: true);
                return Handled();
            }
            return false;
        }
        if (_flowBridging)
        {
            if (FlowSpotAt(mouse) is { } spot)
            {
                _flowBridge = _flowBridge == spot ? null : spot;
                Render(animate: true);
            }
            return Handled();
        }
        if (hex is not int step)
            return Handled();
        if (_flowPath.Count > 0 && step == _flowPath[^1])
        {
            NoteFlowStep(state);
            _flowPath.RemoveAt(_flowPath.Count - 1); // taken back
        }
        else if (_flowPath.Count == 0 && step == _flowFrom)
        {
            EndFlow(); // the structure again: no flow
            ShowPrompt("");
        }
        else if (FlowNext(state, active).Contains(step))
        {
            NoteFlowStep(state);
            _flowPath.Add(step);
            if (FlowNext(state, active).Count == 0)
                FinishFlowPath(state, active); // out of tiles
        }
        else
            ShowIssues(Rules.Apply(state, FlowMove(active, _flowPath.Append(step))).Issues);
        Render(animate: true);
        return Handled();
    }

    // The path is done: the bridge spots if any, else the flow.
    private void FinishFlowPath(GameState state, Faction faction)
    {
        if (!Rules.Apply(state, FlowMove(faction, _flowPath)).Accepted)
        {
            ShowIssues(Rules.Apply(state, FlowMove(faction, _flowPath)).Issues); // e.g. stopped on a river
            return;
        }
        if (!_flowBridging && FlowBridges(state, faction).Count > 0)
        {
            _flowBridging = true;
            ShowPrompt("Click a lit spot for a free bridge (or not), then Done.");
            return;
        }
        Move flow = FlowMove(faction, _flowPath, _flowBridge);
        EndFlow();
        Submit(flow);
    }

    // The tick while the flow is under way.
    private bool OnFlowTick(Faction faction)
    {
        if (_flowFrom is null || faction != Faction.KingdomOfEmber)
            return false;
        if (_flowPath.Count == 0)
        {
            EndFlow();
            Render(animate: true);
            return true;
        }
        FinishFlowPath(_history.Current, faction);
        Render(animate: true);
        return true;
    }

    private (int A, int B)? FlowSpotAt(Vector2 point) =>
        _flowSpots.Where(spot => spot.Sprite.GlobalPosition.DistanceTo(point) <= SnapRadius)
            .OrderBy(spot => spot.Sprite.GlobalPosition.DistanceTo(point))
            .Select(spot => ((int A, int B)?)spot.Edge).FirstOrDefault();

    // The board while the path is chosen: the flow so far (as if it stopped at the path's end), else the game as it is.
    private GameState FlowShown(GameState state) =>
        _flowFrom is int from && state.Phase == Phase.Playing && state.ActivePlayer == Faction.KingdomOfEmber
            ? Rules.LavaFlowSoFar(state, Faction.KingdomOfEmber, from, _flowPath) ?? state
            : state;

    // The flow's bridge spots, once the path is done (the structure and the lava are drawn from FlowShown; the next
    // hexes lit by FlowHexesLit).
    private void RenderFlow()
    {
        foreach (var (_, sprite) in _flowSpots)
            sprite.QueueFree();
        _flowSpots.Clear();
        GameState state = _history.Current; // (the flow's moves are tried from the game as it is)
        if (_flowFrom is null || !_flowBridging || state.ActivePlayer is not Faction ember)
            return;
        foreach (var edge in FlowBridges(state, ember))
        {
            var spot = new Sprite2D { Texture = edge == _flowBridge ? Art.BridgeTexture(state[ember].Colour) : Art.BridgeSpotTexture(),
                ZIndex = MapTopDepth(state) + 1 };
            _pieceLayer.AddChild(spot);
            PlaceAcross(spot, edge.A, edge.B);
            _flowSpots.Add((edge, spot));
        }
    }

    // Hexes lit for these (FanHexesLit adds them): the flow's next steps; the Kingdom of Ember's key spots.
    private IEnumerable<int> FlowHexesLit(GameState state)
    {
        if (_flowFrom is not null && !_flowBridging && state.ActivePlayer is Faction ember)
            return FlowNext(state, ember);
        if (state.Phase == Phase.Playing && state.ActivePlayer == Faction.KingdomOfEmber
            && Enumerable.Range(0, state.KeyBank.Count).FirstOrDefault(id => state.KeyBank[id].Count > 0, -1) is int key and >= 0)
            return Rules.FormableTowns(state, Faction.KingdomOfEmber).SelectMany(town => town)
                .Where(hex => Rules.Apply(state, new FormTown(Faction.KingdomOfEmber, key, hex)).Accepted).ToList();
        return Enumerable.Empty<int>();
    }

    // ---------- Geologists' wheel ----------

    // The terrains a rock may go onto (white) or come off (purple) now, or null when no rock choice is open.
    private static (List<Terrain> Cover, List<Terrain> Lift)? RockChoices(PlayerState player) =>
        (Terrains.Wheel.Where(t => Rules.RockCanCover(player, t)).ToList(), Terrains.Wheel.Where(t => Rules.RockCanLift(player, t)).ToList());

    private bool RockChoiceOpen(GameState state, PlayerState player) =>
        player.Faction == Faction.Geologists
        && (player.RockMovesOwed > 0
            || _bonus is { } bonus && bonus.Faction == player.Faction && bonus.Bonus.Action == BonusAction.MoveRock
            || state.Phase == Phase.SettingUp && _lookWheel is PlayerPanel look && look.ShownFaction == player.Faction);

    // From ShowTerrainChoices: the wheel open and lit while a rock choice is open.
    private void ShowRockChoices(PlayerPanel panel, GameState state, PlayerState player, bool animate)
    {
        if (player.Faction != Faction.Geologists || panel.TerrainWheelTop is not Terrain top)
            return;
        panel.TerrainWheel.SetRocks(player.RockList);
        if (!RockChoiceOpen(state, player))
            return;
        panel.SetTerrainWheelOpen(true, animate);
        var (cover, lift) = RockChoices(player)!.Value;
        panel.TerrainWheel.ShowChoices(top, cover.Concat(lift),
            terrain => cover.Contains(terrain) ? HighlightArt.Colour.White : HighlightArt.Colour.Purple);
    }

    // A terrain clicked on the Geologists' open wheel.
    private bool OnRockChosen(Faction faction, Terrain terrain)
    {
        GameState state = _history.Current;
        if (faction != Faction.Geologists || !state.IsInGame(faction) || !RockChoiceOpen(state, state[faction]))
            return false;
        if (state.Phase == Phase.SettingUp)
            Submit(new EditRock(faction, terrain));
        else if (_bonus is { } bonus && bonus.Bonus.Action == BonusAction.MoveRock)
            Submit(new BonusMoveRock(faction, bonus.Bonus.Source, terrain));
        else
            Submit(new MoveRock(faction, terrain));
        return true;
    }

    // The Geologists' stronghold octagon: the wheel opens for the choice.
    private bool StartRockBonus(Faction faction, BonusActionId bonus)
    {
        GameState state = _history.Current;
        var (cover, lift) = RockChoices(state[faction])!.Value;
        Terrain? any = cover.Concat(lift).Select(t => (Terrain?)t).FirstOrDefault();
        MoveResult trial = Rules.Apply(state, new BonusMoveRock(faction, bonus.Source, any ?? Terrain.Plains));
        if (!trial.Accepted)
        {
            ShowIssues(trial.Issues);
            return true;
        }
        _bonus = (faction, bonus);
        _bonusOptions = new PieceOptions(ValueList<Destination>.Empty, null);
        ShowPrompt("Click a lit terrain on the wheel: white, a rock onto it; purple, a rock off it. (Esc or click the octagon again to cancel.)");
        Render(animate: true);
        return true;
    }

    // ---------- Geologists: a rock flies from the hex built on to their (small) wheel ----------

    private void FlyRocks(MoveResult result)
    {
        if (!result.Accepted || result.Events.OfType<DwellingBuilt>().FirstOrDefault(built => built.Dwelling.Owner == Faction.Geologists) is not DwellingBuilt built
            || result.Events.OfType<RockMoved>().FirstOrDefault(rock => rock.Placed) is not RockMoved rock
            || PanelFor(Faction.Geologists) is not PlayerPanel panel || panel.TerrainWheel is not ColourWheel wheel
            || wheel.CircleGlobalPosition(rock.Terrain) is not Vector2 circle || MoveSeconds <= 0)
            return;
        ValueList<Terrain> after = _history.Current[Faction.Geologists].RockList;
        wheel.SetRocks(after.Where(t => t != rock.Terrain)); // it lands there
        var flying = new Sprite2D
        {
            Texture = IconArt.Texture(Icon.GlowingRock, Mathf.RoundToInt(wheel.CircleRadius * 2 * wheel.RockScale * wheel.GlobalScale.X / _pieceLayer.GlobalScale.X * 1.4f)),
            ZIndex = 120,
            Position = _pieceLayer.ToLocal(_map.Hex(built.Hex).GlobalPosition),
        };
        _pieceLayer.AddChild(flying);
        Tween tween = CreateTween();
        tween.TweenProperty(flying, "position", _pieceLayer.ToLocal(circle), MoveSeconds * 2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(flying, "scale", Vector2.One / 1.4f, MoveSeconds * 2);
        tween.TweenCallback(Callable.From(() =>
        {
            flying.QueueFree();
            if (IsInstanceValid(wheel))
                wheel.SetRocks(_history.Current.IsInGame(Faction.Geologists) ? _history.Current[Faction.Geologists].RockList : ValueList<Terrain>.Empty);
        }));
    }

    // ---------- Changelings: pieces ----------

    // A Changelings' building in its spot's letter's colour (A, B or C: Rules.ChangelingTerrain; A until B and C are
    // chosen), with a thick outline of the A colour: the piece again in A, a little bigger, behind it.
    private static void ShowChangelingPiece(Sprite2D sprite, Piece piece, PlayerState player)
    {
        Terrain a = player.StartingTerrain ?? Terrain.Plains;
        Terrain letter = Rules.ChangelingTerrain(player, piece) ?? a;
        Color colourA = FactionBoardArt.FrameColour(a);
        sprite.Texture = PieceArt.TextureFor(piece.Kind, FactionBoardArt.FrameColour(letter), Art.OutlineFor(player.Colour));
        var halo = sprite.GetNodeOrNull<Sprite2D>("ChangelingHalo");
        if (halo is null)
        {
            halo = new Sprite2D { Name = "ChangelingHalo", ShowBehindParent = true };
            sprite.AddChild(halo);
        }
        halo.Texture = PieceArt.TextureFor(piece.Kind, colourA, colourA);
        halo.Offset = sprite.Offset;
        halo.Centered = sprite.Centered;
        halo.Scale = Vector2.One * ChangelingHaloScale;
    }

    private const float ChangelingHaloScale = 1.14f; // the A-coloured outline: the piece in A this much bigger

    // ---------- Changelings: where a returning dwelling goes ----------
    // With their stronghold, an upgrade whose dwelling could go back either to its row or beside the stronghold: both
    // spots light up (yellow) and a click picks one; anywhere else cancels the upgrade.

    private UpgradeBuilding _returnChoice;
    private readonly List<Node2D> _returnSpotsLit = new();

    private bool InterceptFanMove(Move move)
    {
        if (move is not UpgradeBuilding upgrade || upgrade.ReturnBesideStronghold || upgrade.Building.Owner != Faction.Changelings)
            return false;
        GameState state = _history.Current;
        if (!Rules.Apply(state, upgrade).Accepted || !Rules.Apply(state, upgrade with { ReturnBesideStronghold = true }).Accepted)
            return false; // one way only: as usual
        EndBonus();
        _returnChoice = upgrade;
        ShowPrompt("Click where the dwelling goes back: its row, or beside the stronghold. (Anywhere else: no upgrade.)");
        Render(animate: true);
        return true;
    }

    // The two spots: the dwelling's own slot in its row, and the one beside the stronghold.
    private (Node2D Row, Node2D Beside)? ReturnSpots(GameState state)
    {
        if (_returnChoice is not UpgradeBuilding upgrade || state.Map[upgrade.Hex] is not Piece dwelling
            || PanelFor(Faction.Changelings)?.Board is not BuildingBoard board)
            return null;
        int extra = Buildings.PerPlayer(PieceKind.Dwelling);
        Node2D row = board.Slot(PieceKind.Dwelling, System.Math.Min(dwelling.Number, extra - 1));
        Node2D beside = board.Slot(PieceKind.Dwelling, extra);
        return row is null || beside is null ? null : (row, beside);
    }

    private Rect2 ReturnSpotRect(BuildingBoard board) => new Rect2(-Vector2.One * board.BuildingSize * 0.7f, Vector2.One * board.BuildingSize * 1.4f);

    private bool OnPressReturnChoice(Vector2 mouse)
    {
        if (_returnChoice is not UpgradeBuilding upgrade)
            return false;
        GameState state = _history.Current;
        UpgradeBuilding chosen = null;
        if (ReturnSpots(state) is { } spots && PanelFor(Faction.Changelings)?.Board is BuildingBoard board)
        {
            var (row, beside) = spots;
            Rect2 rect = ReturnSpotRect(board);
            if (rect.HasPoint(row.ToLocal(mouse)))
                chosen = upgrade;
            else if (rect.HasPoint(beside.ToLocal(mouse)))
                chosen = upgrade with { ReturnBesideStronghold = true };
        }
        _returnChoice = null;
        if (chosen is not null)
            Submit(chosen);
        else
        {
            ShowPrompt("");
            Render(animate: true);
        }
        return Handled();
    }

    private void RenderReturnChoice(GameState state)
    {
        foreach (Node2D holder in _returnSpotsLit)
            if (IsInstanceValid(holder))
                EdgeHighlight.SetOnArea(holder, false, default);
        _returnSpotsLit.Clear();
        if (ReturnSpots(state) is not { } spots || PanelFor(Faction.Changelings)?.Board is not BuildingBoard board)
            return;
        foreach (Node2D slot in new[] { spots.Row, spots.Beside })
        {
            var holder = slot.GetNodeOrNull<Node2D>("ReturnSpot");
            if (holder is null)
            {
                holder = new Node2D { Name = "ReturnSpot", ZIndex = 5 };
                slot.AddChild(holder, false, InternalMode.Back);
            }
            EdgeHighlight.SetOnArea(holder, true, ReturnSpotRect(board), 8f, 1, HighlightArt.Colour.Yellow);
            _returnSpotsLit.Add(holder);
        }
    }

    // ---------- The top line ----------

    // What a fan faction must (or may) do now, if anything (Instruction, before everything else).
    private string FanInstruction(GameState state)
    {
        string Who(Faction faction) => NameOf(faction);
        if (ChangelingsChoosing(state))
            return _changelingB is null
                ? $"{Who(Faction.Changelings)} must choose their B and C terrains: click B on the wheel"
                : $"{Who(Faction.Changelings)} must choose their C terrain: click it on the wheel (B again: unchoose it)";
        if (state.TreasuryOffer is not null)
            return state[Faction.Treasurers].StrongholdBuilt
                ? $"{Who(Faction.Treasurers)} may put what they just received in their Safe (convert first if wanted): click it above the Safe, then click confirm"
                : $"{Who(Faction.Treasurers)} may put what they just received in their Safe: click it above the Safe, then click confirm";
        if (Rules.IsStartingStepsTurn(state) && state[state.ActivePlayer!.Value] is PlayerState djinni)
            return $"{Who(djinni.Faction)} must take their starting steps: click a track";
        if (state.IsInGame(Faction.Atlanteans) && state[Faction.Atlanteans].StartingTownOwed)
            return $"{Who(Faction.Atlanteans)} must take a key token for their starting town: click one";
        if (state.Phase == Phase.PlacingDwellings && state.ActivePlayer == Faction.Atlanteans)
            return $"{Who(Faction.Atlanteans)} must place their stronghold";
        if (state.Phase is not (Phase.Playing or Phase.RoundEndSpades) || state.ActivePlayer is not Faction active)
            return null;
        PlayerState me = state[active];
        if (_flowFrom is not null)
            return _flowBridging
                ? $"{Who(active)} may take a free bridge: click a lit spot, then click confirm"
                : $"{Who(active)}: click lit hexes for the lava's path, then click confirm (the last hex again takes it back)";
        if (me.LavaOwed + me.LavaOwedAny > 0)
            return $"{Who(active)} must place {me.LavaOwed + me.LavaOwedAny} lava: click a lit structure";
        if (me.RockMovesOwed > 0)
            return $"{Who(active)} may move a rock: click a lit terrain on their wheel (or click confirm to give it up)";
        if (me.LakeDwellingOwed)
            return $"{Who(active)} may build a free dwelling: click a lit Lake hex (or click confirm to decline)";
        if (me.IceMassesOwed is { Count: > 0 } masses)
            return $"{Who(active)} may build a free dwelling on each mass of Ice ({masses.Count} left): click a lit hex (or click confirm to decline)";
        if (me.FreeUpgradeOwed)
            return $"{Who(active)} may take a free shipping or spade step: click one";
        return null;
    }

    // ---------- Changelings: B and C ----------

    // The Changelings choosing B and C now: on the faction screen, alongside the volcano faction's starting terrain, in
    // the order the factions were chosen (Rules.LateTerrainChooser).
    private static bool ChangelingsChoosing(GameState state) => Rules.LateTerrainChooser(state) == Faction.Changelings;

    private List<Terrain> ChangelingChoices(GameState state) =>
        Terrains.Wheel.Where(t => t == _changelingB || Terrains.Wheel.Any(other => other != t
            && Rules.Apply(state, _changelingB is Terrain b
                ? new ChooseChangelingTerrains(Faction.Changelings, b, t)
                : new ChooseChangelingTerrains(Faction.Changelings, t, other)).Accepted)).ToList();

    private void OnChangelingTerrainChosen(Terrain terrain)
    {
        if (_changelingB == terrain)
            _changelingB = null;
        else if (_changelingB is Terrain b)
        {
            _changelingB = null;
            Submit(new ChooseChangelingTerrains(Faction.Changelings, b, terrain));
            return;
        }
        else
            _changelingB = terrain;
        Render(animate: true);
    }
}
