using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the fan factions that act on the map (the Kingdom of Ember's lava, the Children of the Wyrm, the
// Selkies, the Snow Shamans, the Firewalkers, the Atlanteans). A file of the same class (partial).
//   The map: the Kingdom of Ember's lava, discs stacked under the structure (which sits on top of them); the Children of
//     the Wyrm's power tokens, discs on their river hexes, flying in from the bowls.
//   Children of the Wyrm's stronghold: the octagon lights the rivers a token could go on; a first click puts one there
//     (lit again for the second); a second click places both; the tick (Done) places only the first, or, before any,
//     uses the action for nothing.
//   Selkies' stronghold: after the octagon, a dwelling dragged to a lit hex is built with shipping +1 and a spade free.
//   Snow Shamans' stronghold: the empty Ice hexes of each mass still owed lit; a click (or a drag) builds there.
//   Firewalkers' pyro beside the VP: a click moves it on (1 power).
//   Atlanteans' starting town: a click on a key token puts it on their stronghold.
public partial class Main
{
    private int? _riverFirst;                  // the Wyrm's first river hex chosen
    private Sprite2D _riverFirstSprite;        // its token, waiting
    private readonly Dictionary<int, List<Sprite2D>> _lavaSprites = new();
    private readonly Dictionary<int, Sprite2D> _wyrmSprites = new();
    private System.Func<Destination, Move> _dragMove; // a drag's move other than Rules.MoveTo (the Selkies' stronghold)

    // Lava discs as wide as the hex takes them, in the Pieces layer's pixels.
    private float LavaDiscPixels => IconArt.LavaDiameter * _map.HexRadius * _map.GlobalScale.Y / Mathf.Max(0.0001f, _pieceLayer.GlobalScale.Y);

    // How far up a structure on this hex sits: on top of its lava.
    private float LavaRaise(GameState state, int hex) => state.LavaAt(hex) * IconArt.LavaLift(LavaDiscPixels);

    private int WyrmTokenPixels => Mathf.Max(8, Mathf.RoundToInt(_map.HexRadius * 0.9f * _map.GlobalScale.Y / Mathf.Max(0.0001f, _pieceLayer.GlobalScale.Y)));

    private Node2D _lavaLayer;

    // The lava's own layer: just before the Pieces layer under the board (the same transform), so the discs lie over
    // the map and under every piece, key and token. (On the Pieces layer at ZIndex -1 they fell behind the map.)
    private Node2D LavaLayer()
    {
        if (_lavaLayer is null)
        {
            _lavaLayer = new Node2D { Name = "Lava" };
            Node board = _pieceLayer.GetParent();
            board.AddChild(_lavaLayer);
            board.MoveChild(_lavaLayer, _pieceLayer.GetIndex());
        }
        _lavaLayer.Transform = _pieceLayer.Transform;
        _lavaLayer.ZIndex = _pieceLayer.ZIndex;
        return _lavaLayer;
    }

    // ---------- Drawing ----------

    private void RenderFanMap(GameState state, bool animate)
    {
        // Lava: a disc per tile, each LavaLift above the one under it, under the structure.
        LavaLayer(); // keeps its transform with the Pieces layer's
        float d = LavaDiscPixels;
        int size = Mathf.Max(8, Mathf.RoundToInt(d / (IconArt.LavaIconDiameter * 6f / 7f)));
        Texture2D disc = IconArt.Texture(Icon.LavaToken, size);
        List<int> emptied = _lavaSprites.Keys.Where(hex => state.LavaAt(hex) == 0).ToList();
        List<int> arriving = Enumerable.Range(0, state.Layout.HexCount)
            .Where(hex => state.LavaAt(hex) > 0 && !_lavaSprites.ContainsKey(hex)).ToList();
        // One stack gone and one come: it moved on (the lava flowing, a step of it taken back, an undo), and its
        // discs slide with it (the spent ones taken off its top below: for a step of the flow, once it has landed).
        if (emptied.Count == 1 && arriving.Count == 1)
        {
            _lavaSprites[arriving[0]] = _lavaSprites[emptied[0]];
            _lavaSprites.Remove(emptied[0]);
            emptied.Clear();
        }
        FlowStep step = _flowStep;
        bool carrying = step is not null && NowSeconds < step.Lands;
        foreach (int gone in emptied)
        {
            foreach (Sprite2D sprite in _lavaSprites[gone])
            {
                StopSlide(sprite);
                sprite.QueueFree();
            }
            _lavaSprites.Remove(gone);
        }
        for (var hex = 0; hex < state.Layout.HexCount; hex++)
        {
            int count = state.LavaAt(hex);
            if (count == 0)
                continue;
            if (!_lavaSprites.TryGetValue(hex, out List<Sprite2D> stack))
                _lavaSprites[hex] = stack = new List<Sprite2D>();
            bool flowing = carrying && step.To == hex;
            while (stack.Count > count && !flowing)
            {
                StopSlide(stack[^1]);
                stack[^1].QueueFree();
                stack.RemoveAt(stack.Count - 1);
            }
            Vector2 centre = _pieceLayer.ToLocal(_map.Hex(hex).GlobalPosition);
            // A new disc starts where it goes (on top of the stack), or, coming with the flow, where the stack was.
            Vector2 from = flowing ? _pieceLayer.ToLocal(_map.Hex(step.From).GlobalPosition) : centre;
            while (stack.Count < count)
            {
                var sprite = new Sprite2D { Texture = disc, Position = from + new Vector2(0, -stack.Count * IconArt.LavaLift(d)) };
                LavaLayer().AddChild(sprite); // appended: a disc added later lies on top of those under it
                stack.Add(sprite);
            }
            for (var i = 0; i < stack.Count; i++)
            {
                stack[i].Texture = disc;
                stack[i].ZIndex = MapGroundDepth(state, hex); // in front of what stands on the rows behind (MainPieces.MapDepth)
                Slide(stack[i], centre + new Vector2(0, -i * IconArt.LavaLift(d)), animate); // (moved on: with the structure)
            }
        }

        // The Wyrm's tokens: new ones fly in from the bowls (bowl 1 first).
        var tokens = new HashSet<int>(state.WyrmTokenHexes);
        foreach (int gone in _wyrmSprites.Keys.Except(tokens).ToList())
        {
            _wyrmSprites[gone].QueueFree();
            _wyrmSprites.Remove(gone);
        }
        Texture2D token = IconArt.Texture(Icon.PowerToken, WyrmTokenPixels);
        PlayerPanel wyrmPanel = state.IsInGame(Faction.ChildrenOfTheWyrm) ? PanelFor(Faction.ChildrenOfTheWyrm) : null;
        var order = 0;
        foreach (int hex in state.WyrmTokenHexes)
        {
            Vector2 at = _pieceLayer.ToLocal(_map.Hex(hex).GlobalPosition);
            if (_wyrmSprites.TryGetValue(hex, out Sprite2D sprite))
            {
                sprite.Position = at;
                continue;
            }
            sprite = new Sprite2D { Texture = token, ZIndex = MapGroundDepth(state, hex), Position = at }; // (MainPieces.MapDepth)
            _pieceLayer.AddChild(sprite);
            _wyrmSprites[hex] = sprite;
            if (animate && wyrmPanel is not null && MoveSeconds > 0)
            {
                sprite.Position = _pieceLayer.ToLocal(wyrmPanel.PowerBank.ToGlobal(PowerBowlsView.BowlRect(1).GetCenter()));
                CreateTween().TweenProperty(sprite, "position", at, MoveSeconds).SetDelay(order++ * 0.12f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }

        // The Wyrm's first river hex, its token waiting there.
        if (_riverFirst is int first && _bonus is { } waiting && waiting.Bonus.Action == BonusAction.RiverTokens)
        {
            _riverFirstSprite ??= new Sprite2D { Modulate = new Color(1, 1, 1, 0.8f) };
            _riverFirstSprite.ZIndex = MapGroundDepth(state, first);
            if (_riverFirstSprite.GetParent() is null)
                _pieceLayer.AddChild(_riverFirstSprite);
            _riverFirstSprite.Texture = token;
            _riverFirstSprite.Position = _pieceLayer.ToLocal(_map.Hex(first).GlobalPosition);
            _riverFirstSprite.Visible = true;
        }
        else if (_riverFirstSprite is not null)
            _riverFirstSprite.Visible = false;

        // The Firewalkers' pyro, beside their VP.
        RenderFlow(); // MainFanFactionsEmberGeologistsChangelings.cs
        foreach (PlayerPanel panel in _panels)
            panel.ShowPyro(panel.ShownFaction == Faction.Firewalkers && state.IsInGame(Faction.Firewalkers)
                ? state[Faction.Firewalkers].Pyro : null);
    }

    // The hexes these light (RenderFanFactions adds them): the Wyrm's rivers; the Snow Shamans' masses; the Kingdom of
    // Ember's structures that may take the lava owed.
    private IEnumerable<int> FanHexesLit(GameState state)
    {
        if (FlowHexesLit(state).ToList() is { Count: > 0 } flowOrKey) // the lava flow, Ember's key (MainFanFactionsEmberGeologistsChangelings.cs)
            return flowOrKey;
        if (LavaOwer(state) is Faction ember)
            return Enumerable.Range(0, state.Layout.HexCount)
                .Where(hex => state.Map[hex] is { } piece && piece.Owner == ember && Rules.Apply(state, new PlaceLava(ember, hex)).Accepted).ToList();
        if (_bonus is { } waiting && waiting.Bonus.Action == BonusAction.RiverTokens)
            return Enumerable.Range(0, state.Layout.HexCount).Where(hex => state.HexTerrain[hex] == Terrain.River
                && Rules.Apply(state, RiverMove(waiting.Faction, waiting.Bonus.Source, hex)).Accepted).ToList();
        if (state.Phase == Phase.Playing && state.ActivePlayer is Faction active && state[active].IceMassesOwed is { Count: > 0 } masses
            && NextDwelling(state, active) is Piece dwelling)
            return masses.SelectMany(mass => mass)
                .Where(hex => Rules.Apply(state, new PlaceFreeDwelling(dwelling, hex)).Accepted).ToList();
        return Enumerable.Empty<int>();
    }

    // The Kingdom of Ember, owing lava on their turn (or between rounds).
    private static Faction? LavaOwer(GameState state) =>
        state.Phase is Phase.Playing or Phase.RoundEndSpades && state.ActivePlayer is Faction active
        && state[active].LavaOwed + state[active].LavaOwedAny > 0 ? active : null;

    private Move RiverMove(Faction faction, BonusSource source, int hex) =>
        _riverFirst is int first ? new BonusRiverTokens(faction, source, first, hex) : new BonusRiverTokens(faction, source, hex);

    // ---------- Clicks ----------

    // Early in OnPress (from OnPressFanFactions): the pyro, the Atlanteans' key, the Snow Shamans' masses.
    private bool OnPressFanFactionsOnMap(Vector2 mouse)
    {
        GameState state = _history.Current;
        if (OnPressFlow(mouse)) // the Kingdom of Ember's lava flow (MainFanFactionsEmberGeologistsChangelings.cs)
            return true;
        foreach (PlayerPanel panel in _panels)
            if (panel.ShownFaction == Faction.Firewalkers && IsInteractive(panel) && panel.IsOverPyro(mouse))
            {
                Submit(new AdvancePyro(Faction.Firewalkers));
                return Handled();
            }
        if (state.IsInGame(Faction.Atlanteans) && state[Faction.Atlanteans].StartingTownOwed && KeyUnder(mouse) is int key
            && Rules.StrongholdHex(state, Faction.Atlanteans) is int stronghold)
        {
            Submit(new FormTown(Faction.Atlanteans, key, stronghold));
            return Handled();
        }
        if (LavaOwer(state) is Faction ember && _map.HexAt(mouse) is int under && _fanHexesLit.Contains(under))
        {
            Submit(new PlaceLava(ember, under));
            return Handled();
        }
        if (state.Phase == Phase.Playing && state.ActivePlayer is Faction active && state[active].IceMassesOwed is { Count: > 0 }
            && _map.HexAt(mouse) is int ice && _fanHexesLit.Contains(ice) && NextDwelling(state, active) is Piece dwelling)
        {
            Submit(new PlaceFreeDwelling(dwelling, ice));
            return Handled();
        }
        return false;
    }

    // The Wyrm's and Selkies' octagons (from OnFanBonusActionClicked).
    private bool OnFanBonusActionClickedOnMap(Faction faction, BonusActionId bonus)
    {
        GameState state = _history.Current;
        if (bonus.Action is not (BonusAction.RiverTokens or BonusAction.SelkiesSpade))
            return false;
        Move trial = bonus.Action == BonusAction.RiverTokens
            ? new BonusRiverTokens(faction, bonus.Source, null)
            : null;
        if (trial is not null && !Rules.Apply(state, trial).Accepted)
        {
            ShowIssues(Rules.Apply(state, trial).Issues);
            return true;
        }
        _riverFirst = null;
        _bonus = (faction, bonus);
        _bonusOptions = new PieceOptions(ValueList<Destination>.Empty, null);
        ShowPrompt((bonus.Action == BonusAction.RiverTokens
            ? "Click a highlighted river for a power token (Done: one, or none)."
            : "Drag a dwelling to a highlighted hex: shipping +1 and a spade free.") + " (Esc or click the octagon again to cancel.)");
        Render(animate: true);
        return true;
    }

    // A click while the Wyrm's or Selkies' octagon waits.
    private bool OnFanBonusTargetClickedOnMap(Vector2 mouse)
    {
        if (_bonus is not { } waiting)
            return false;
        GameState state = _history.Current;
        var (faction, bonus) = waiting;
        if (bonus.Action == BonusAction.RiverTokens)
        {
            if (_map.HexAt(mouse) is not int hex)
                return false;
            if (_riverFirst == hex)
                _riverFirst = null; // taken back
            else if (!_fanHexesLit.Contains(hex))
                ShowIssues(Rules.Apply(state, RiverMove(faction, bonus.Source, hex)).Issues);
            else if (_riverFirst is int first)
            {
                Submit(new BonusRiverTokens(faction, bonus.Source, first, hex));
                return Handled();
            }
            else
                _riverFirst = hex;
            Render(animate: true);
            return Handled();
        }
        if (bonus.Action == BonusAction.SelkiesSpade)
        {
            if (PieceUnder(mouse) is not Piece piece || piece.Kind != PieceKind.Dwelling || piece.Owner != faction)
                return _map.HexAt(mouse) is not null && Handled();
            var hexes = Enumerable.Range(0, state.Layout.HexCount)
                .Where(hex => Rules.Apply(state, new BonusSelkiesBuild(piece, bonus.Source, hex)).Accepted)
                .Select(hex => (Destination)new HexDestination(hex)).ToList();
            if (hexes.Count == 0)
            {
                ShowIssues(Rules.Apply(state, new BonusSelkiesBuild(piece, bonus.Source, -1)).Issues);
                return Handled();
            }
            BonusSource source = bonus.Source;
            TryStartDrag(piece, mouse, new PieceOptions(new ValueList<Destination>(hexes), null),
                target => new BonusSelkiesBuild(piece, source, ((HexDestination)target).Hex));
            return Handled();
        }
        return false;
    }

    // The tick while the Wyrm choose rivers: the first token alone, or none.
    private bool OnFanFactionsTickOnMap(Faction faction)
    {
        if (OnFlowTick(faction)) // MainFanFactionsEmberGeologistsChangelings.cs
            return true;
        if (_bonus is { } waiting && waiting.Faction == faction && waiting.Bonus.Action == BonusAction.RiverTokens)
        {
            Submit(new BonusRiverTokens(faction, waiting.Bonus.Source, _riverFirst));
            return true;
        }
        return false;
    }
}
