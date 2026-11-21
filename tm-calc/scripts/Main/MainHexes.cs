using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: clicking a hex: the colour wheel, transform costs, and the offer to build after spades.
// A file of the same class (partial).
public partial class Main
{
    // ---------- Clicking a hex: choose a terrain (home terrain then offers a dwelling: RenderBuildOffer) ----------

    private void OnHexClicked(int hex)
    {
        GameState state = _history.Current;
        if (state.ActivePlayer is not Faction active)
        {
            ShowNote("Nobody is taking a turn right now.");
            return;
        }

        // Free spades being spent (after a spade bonus action, or between rounds).
        if (state.SpadesPending)
        {
            OnSpadeHexClicked(state, active, hex);
            return;
        }

        Piece? dwelling = NextDwelling(state, active);

        // Starting dwellings (the Atlanteans' stronghold): just confirm the spot.
        if (state.Phase == Phase.PlacingDwellings)
        {
            if (StartingPiece(state, active) is not Piece starting)
                return;
            Submit(new PlaceStartingDwelling(starting, hex)); // refused with a reason off starting terrain
            return;
        }

        // In a round, clicking one of your own buildings offers to upgrade it.
        if (state.Map[hex] is Piece own && own.Owner == active)
        {
            OfferUpgrades(state, active, hex, own.Kind); // the buildings it could become, over it (MainUpgrades.cs)
            return;
        }

        // In a round: a hex the player builds on (home terrain; Ice, Volcano; the Riverwalkers' unlocked terrains) is
        // built on at once; other hexes offer every terrain they can be transformed into (choosing home terrain then
        // offers a dwelling there: RenderBuildOffer).
        PlayerState player = state[active];
        if (player.CanBuildOn(state.HexTerrain[hex]))
        {
            if (dwelling is not Piece next)
            {
                ShowNote($"The {NameOf(active)} have no dwellings left to build.");
                return;
            }
            Submit(new BuildDwelling(next, hex)); // refused with a reason (out of range, ...)
            return;
        }

        ValueList<TransformChoice> choices = Rules.TransformChoices(state, active, hex);
        if (choices.Count == 0)
        {
            // e.g. "That hex is out of range of the Giants' buildings." (The Riverwalkers never transform: why they
            // can't build there.)
            Move attempt = player.BuildTerrain is Terrain build ? new TransformTerrain(active, hex, build)
                : dwelling is Piece unlocked ? new BuildDwelling(unlocked, hex)
                : null;
            if (attempt is not null)
                ShowIssues(Rules.Apply(state, attempt).Issues);
            return;
        }
        OpenColourWheel(state, active, hex, choices);
    }

    // While free spades are being spent: a hex just made home terrain can be built on (main phase);
    // any other hex offers the terrains the free spades (topped up if allowed) can make it.
    private void OnSpadeHexClicked(GameState state, Faction active, int hex)
    {
        // A hex just made home terrain with free spades: build on it at once.
        if (state.Phase == Phase.Playing && Rules.SpadedBuildTargets(state, active).Contains(hex)
            && NextDwelling(state, active) is Piece spadedDwelling)
        {
            Submit(new BuildOnSpadedHex(spadedDwelling, hex));
            return;
        }

        Terrain target = state[active].BuildTerrain ?? Terrains.Wheel[0];
        ValueList<TransformChoice> choices = Rules.TransformChoices(state, active, hex);
        if (choices.Count == 0)
        {
            Terrain anyOther = state.HexTerrain[hex] == target ? Terrains.Wheel.First(t => t != target) : target;
            ShowIssues(Rules.Apply(state, new SpendSpades(active, hex, anyOther)).Issues);
            return;
        }
        OpenColourWheel(state, active, hex, choices);
    }

    // The colour wheel, centred on the hex, home terrain at the top. Hovering a terrain shows what it costs in the
    // middle: the workers for the spades that aren't free (written as a warning if it can't be done now).
    // Used for every spade except a dragged dwelling's: the turn's transform here, and free spades (bonus and
    // power actions, cult rewards between rounds) through OnSpadeHexClicked.
    // Fire & Ice: an ice faction's starting terrain is at the top with the ice ring over it (the ring is Ice); a
    // volcano faction's wheel has the hex's own terrain at the top and Volcano in the middle, costing its price (power
    // tokens removed, or steps from a track); the Shapeshifters' ring sits on their home.
    // The choice is submitted at once; home terrain then offers a dwelling there (RenderBuildOffer).
    private void OpenColourWheel(GameState state, Faction active, int hex, ValueList<TransformChoice> choices)
    {
        PlayerState player = state[active];
        Terrain from = state.HexTerrain[hex];
        _wheelChoices = choices;
        _wheelPlayer = active;
        ShowNote("");
        _pendingHex = hex;
        _wheel.GlobalPosition = _map.Hex(hex).GlobalPosition;
        // Its cost as big on screen as the map's hex costs (a dwelling picked up from the panel).
        float costScale = _map.Hex(hex).GlobalScale.X / Mathf.Max(0.0001f, _wheel.GlobalScale.X);
        _wheel.SetCostSize(Mathf.RoundToInt(_map.CostIconSize * costScale), Mathf.RoundToInt(_map.CostFontSize * costScale));
        Terrain top = player.Kind == FactionKind.Volcano ? from : player.Home ?? player.StartingTerrain ?? from;
        _wheel.Ring = player.Kind == FactionKind.Ice || active == Faction.Shapeshifters ? top : null;
        _wheel.RingIcon = player.Kind == FactionKind.Ice ? Icon.IceRing : Icon.VariableRing;
        _wheel.RingChooses = player.Kind == FactionKind.Ice ? Terrain.Ice : null;
        _wheel.RingOutside = player.Kind == FactionKind.Ice; // above the starting terrain, touching it: all of it is Ice
        _wheel.Centre = player.Kind == FactionKind.Volcano ? Terrain.Volcano : null;
        // A volcano faction has one choice: just the Volcano circle where the top circle would be, its cost on the hex.
        _wheel.CentreOnly = player.Kind == FactionKind.Volcano;
        _wheel.ShowFor(top, from, to =>
        {
            if (choices.FirstOrDefault(c => c.To == to) is TransformChoice legal)
                return (Amounts.From(legal.Cost) + (legal.VolcanoPrice > 0 ? VolcanoPriceAmounts(active, legal.VolcanoPrice) : Amounts.None), true);
            if (to == Terrain.Volcano && Rules.VolcanoPrice(state, active, hex) is int price)
                return (VolcanoPriceAmounts(active, price), false);
            if (player.SpadesNeeded(from, to) is not int spades || spades == 0)
                return (Amounts.None, false);
            int free = state.SpadesPending ? Math.Min(spades, player.SpadesOwed) : 0;
            return (Amounts.From(player.SpadeCost * (spades - free)), false);
        });
        KeepWheelOnScreen();
        _wheel.Show();
    }

    private const float WheelScreenMargin = 6f;

    // The wheel moved as little as needed to lie below the top panel (on its own layer, so it would hide the wheel)
    // and inside the window; below the top panel wins if both can't be met. Its cost stays at its centre.
    private void KeepWheelOnScreen()
    {
        float reach = _wheel.Reach * Mathf.Abs(_wheel.GlobalScale.Y) + WheelScreenMargin;
        Rect2 view = GetViewportRect();
        float top = view.Position.Y;
        if (_topPanel is not null && _topPanel.IsVisibleInTree())
            top = Mathf.Max(top, _topPanel.GlobalPosition.Y + _topPanel.ShownSize.Y);
        Vector2 at = _wheel.GlobalPosition;
        at.X = Mathf.Clamp(at.X, view.Position.X + reach, Mathf.Max(view.Position.X + reach, view.End.X - reach));
        at.Y = Mathf.Max(Mathf.Min(at.Y, view.End.Y - reach), top + reach);
        _wheel.GlobalPosition = at;
    }

    private void CloseWheel()
    {
        _wheel.Hide();
        _pendingHex = null;
    }

    // A terrain clicked on the wheel: the hex's own terrain does nothing; one it can't become says why.
    private void OnWheelTerrainChosen(Terrain to)
    {
        if (!_wheel.Visible || _pendingHex is not int hex)
            return;
        GameState state = _history.Current;
        if (state.HexTerrain[hex] == to)
            return;
        if (_wheelChoices.FirstOrDefault(choice => choice.To == to) is not TransformChoice chosen)
        {
            Move move = state.SpadesPending ? new SpendSpades(_wheelPlayer, hex, to) : new TransformTerrain(_wheelPlayer, hex, to);
            ShowIssues(Rules.Apply(state, move).Issues);
            return;
        }
        CloseWheel();
        Submit(chosen.Move); // the hex turns over (MapView); home terrain then offers a dwelling (RenderBuildOffer)
    }

    // ---------- Building after spades ----------

    private Sprite2D _buildOfferSprite;                       // the dwelling lit by RenderBuildOffer, if any
    private readonly HashSet<int> _buildOfferHexesLit = new();

    // The hexes just made home terrain that the active player's leftmost dwelling could now go on: after spending
    // free spades (once none are left), a transform to home terrain as the action, or the sandstorm. Empty otherwise.
    // Not while a Fire & Ice choice is owed first (a priest, a token, the Acolytes' steps for Volcano).
    private static ValueList<int> BuildOfferHexes(GameState state) =>
        state.Phase == Phase.Playing && state.SpadesPending && state.ActivePlayer is Faction active && state[active].SpadesOwed == 0
            && !OwedChoiceWaiting(state)
            ? Rules.SpadedBuildTargets(state, active)
            : ValueList<int>.Empty;

    private Node2D _buildOfferSlot; // the lit slot's holder on the building board

    // The holder (made once) on the board slot of the dwelling offered (the leftmost, or the Changelings' 9th), and the
    // slot's well round it; (null, _) without a board slot.
    private (Node2D Holder, Rect2 Well) BuildOfferSlot(GameState state, Faction faction)
    {
        if (NextDwelling(state, faction) is not Piece dwelling || PanelFor(faction) is not PlayerPanel { IsCompact: false } panel
            || panel.Board is not BuildingBoard board)
            return (null, default);
        PlayerState player = state[faction];
        int slot = player.ExtraDwelling == dwelling ? Buildings.PerPlayer(PieceKind.Dwelling) : player.DwellingBank.IndexOf(dwelling);
        if (slot < 0 || board.Slot(PieceKind.Dwelling, slot) is not Node2D marker || board.SlotRect(PieceKind.Dwelling, slot) is not Rect2 well)
            return (null, default);
        if (marker.GetNodeOrNull<Node2D>("BuildOfferLight") is not Node2D holder)
        {
            holder = new Node2D { Name = "BuildOfferLight" };
            marker.AddChild(holder, false, InternalMode.Back);
        }
        return (holder, well);
    }

    // The offer: the leftmost dwelling and each hex it could go on, lit white with BuildOfferGlints glints (the
    // dwelling's light is off while it is held).
    private void RenderBuildOffer(GameState state)
    {
        ValueList<int> dwellingHexes = BuildOfferHexes(state);
        ValueList<int> hexes = new(dwellingHexes.Union(LandscapeOfferHexes(state))); // the landscape's too (MainLandscapes.cs)
        Sprite2D sprite = dwellingHexes.Count > 0 && NextDwelling(state, state.ActivePlayer!.Value) is Piece dwelling
            && _dragged != dwelling && _pieceSprites.TryGetValue(dwelling, out Sprite2D found)
            ? found
            : null;
        if (_buildOfferSprite is not null && _buildOfferSprite != sprite && IsInstanceValid(_buildOfferSprite))
            EdgeHighlight.SetOnArea(_buildOfferSprite, false, default);
        _buildOfferSprite = sprite;
        // On the board: the dwelling's slot lit, its well's size and place, under the dwelling (the board is under the
        // Pieces layer). Without a board slot (no building board), round the dwelling itself.
        (Node2D slotHolder, Rect2 well) = sprite is not null && state.ActivePlayer is Faction builder
            ? BuildOfferSlot(state, builder)
            : ((Node2D)null, default(Rect2));
        if (_buildOfferSlot is not null && _buildOfferSlot != slotHolder && IsInstanceValid(_buildOfferSlot))
            EdgeHighlight.SetOnArea(_buildOfferSlot, false, default);
        _buildOfferSlot = slotHolder;
        if (slotHolder is not null)
            EdgeHighlight.SetOnArea(slotHolder, true, well.Grow(BuildOfferMargin * 0.5f), BuildOfferCornerRadius, BuildOfferGlints,
                HighlightArt.Colour.White, BuildOfferHighlightScale);
        else if (sprite is not null)
        {
            Rect2 rect = Arrangement.VisibleRect(sprite).Grow(BuildOfferMargin);
            EdgeHighlight.SetOnArea(sprite, true, rect, BuildOfferCornerRadius, BuildOfferGlints, HighlightArt.Colour.White,
                BuildOfferHighlightScale);
            if (sprite.GetNodeOrNull<Node2D>(EdgeHighlight.NodeName) is Node2D highlight)
                highlight.ZIndex = 1; // over the dwellings beside it
        }

        foreach (int hex in _buildOfferHexesLit.Except(hexes).ToList())
            _map.ShowHexHighlight(hex, false);
        foreach (int hex in hexes)
            _map.ShowHexHighlight(hex, true, BuildOfferGlints);
        _buildOfferHexesLit.Clear();
        _buildOfferHexesLit.UnionWith(hexes);
    }

    // While the offer stands: a lit hex builds the dwelling there; pressing the lit dwelling picks it up (dropped on a
    // lit hex, it is built there); the special landscape, lit too if it can go there (LandscapeOfferHexes), is laid on
    // the hex if there is one, else picked up; any other click declines (Done). The history buttons and Done work as
    // usual; any other button (e.g. End turn) declines first, then does its own job. True if the offer took the click.
    private bool OnPressBuildOffer(Vector2 mouse)
    {
        GameState state = _history.Current;
        ValueList<int> hexes = BuildOfferHexes(state);
        ValueList<int> landscapeHexes = LandscapeOfferHexes(state); // MainLandscapes.cs
        if (hexes.Count == 0 && landscapeHexes.Count == 0)
            return false;
        Faction builder = state.ActivePlayer!.Value;
        Piece? next = NextDwelling(state, builder);
        BaseButton button = GetViewport().GuiGetHoveredControl() as BaseButton;
        if (button is not null && (IsHistoryButton(button) || button == _stepButton))
            return true; // the button's own signal does it
        if (button is null)
        {
            GetViewport().SetInputAsHandled();
            if (_map.HexAt(mouse) is int hex && hexes.Contains(hex) && next is Piece dwelling)
            {
                Submit(new BuildOnSpadedHex(dwelling, hex)); // the dwelling slides onto it
                return true;
            }
            if (_map.HexAt(mouse) is int landHex && landscapeHexes.Contains(landHex))
            {
                Submit(new PlaceLandscape(builder, landHex)); // lit for the landscape only (no dwelling can go there)
                return true;
            }
            if (next is Piece held && PieceUnder(mouse) == held && hexes.Count > 0)
            {
                TryStartDrag(held, mouse); // only the lit hexes take it (Rules.MoveTo: BuildOnSpadedHex)
                return true;
            }
            if (landscapeHexes.Count > 0 && PieceUnder(mouse) == LandscapePiece(builder))
            {
                if (landscapeHexes.Count == 1)
                    Submit(new PlaceLandscape(builder, landscapeHexes[0])); // the token slides onto the hex
                else
                    TryStartDrag(LandscapePiece(builder), mouse);
                return true;
            }
        }
        Submit(new FinishSpades(builder)); // no dwelling
        return true;
    }

    // The leftmost dwelling still in this faction's bank, if any.
    // What a faction places before round 1: its next dwelling, or the Atlanteans' stronghold (fan factions).
    private static Piece? StartingPiece(GameState state, Faction faction) =>
        faction == Faction.Atlanteans ? state[faction].NextBuilding(PieceKind.Stronghold) : NextDwelling(state, faction);

    private static Piece? NextDwelling(GameState state, Faction faction)
    {
        PlayerState player = state[faction];
        Piece? next = player.NextBuilding(PieceKind.Dwelling);
        // Fan factions: the Changelings' 9th dwelling (beside their stronghold) when it, not the row's, matches the hex
        // just made ready for a dwelling.
        if (faction == Faction.Changelings && player.StrongholdBuilt && player.ExtraDwelling is Piece extra && state.SpadedHexes.Count > 0)
        {
            bool Fits(Piece? dwelling) => dwelling is Piece d
                && state.SpadedHexes.Any(hex => state.HexTerrain[hex] == Rules.DwellingTerrain(state, d));
            if (!Fits(next) && Fits(extra))
                return extra;
        }
        return next;
    }

    // While a dwelling is held: over each hex in range that is not yet home terrain, what the spades to make it so
    // cost (e.g. 3 workers). Nothing for home terrain, and nothing for other buildings (their cost never varies).
    private void ShowTransformCosts(Piece piece, IEnumerable<Destination> destinations)
    {
        GameState state = _history.Current;
        PlayerState player = state[piece.Owner];
        if (piece.Kind == PieceKind.Landscape)
        {
            ShowLandscapeCosts(state, piece.Owner, destinations); // MainLandscapes.cs
            return;
        }
        if (piece.Kind != PieceKind.Dwelling || player.BuildTerrain is not Terrain build || state.Phase == Phase.PlacingDwellings)
            return; // the Riverwalkers never transform; starting dwellings turn their hex for nothing (ice, volcano)
        foreach (HexDestination destination in destinations.OfType<HexDestination>())
        {
            Cost reach = Rules.ReachCost(state, piece.Owner, Rules.ReachOf(state, piece.Owner, destination.Hex, allowSkip: true));
            // Home terrain: only the carpet flight or tunnel, if that is how the hex is reached (the Fakirs, the Dwarves).
            if (player.CanBuildOn(state.HexTerrain[destination.Hex]))
            {
                if (reach != default)
                    _map.ShowCost(destination.Hex, Amounts.From(reach));
                continue;
            }
            // The spades (or a volcano faction's price), and the carpet flight or tunnel if that is how the hex is reached.
            Amounts change = TransformAmounts(state, piece.Owner, destination.Hex, build);
            _map.ShowCost(destination.Hex, change is null ? null : change + Amounts.From(reach));
        }
    }
}
