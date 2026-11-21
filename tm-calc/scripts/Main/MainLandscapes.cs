using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the Special Landscapes expansion (Core: Rules.Landscapes.cs). A file of the same class (partial),
// only to keep Main.cs a manageable size.
//   The token: each faction's landscape is a piece (PieceKind.Landscape) drawn with LandscapeArt. Until it is placed it
//     sits in the well of its board's dwelling row (BuildingBoard.LandscapeSlot), at BuildingBoard.LandscapeTokenSize;
//     on the map it fills its hex (LandscapeArt.TokenDiameterOnHex), over the terrain tile. Dragged onto a hex it is
//     placed there (Rules.MoveTo: PlaceLandscape).
//   After a transform to home terrain (the build offer: Main.RenderBuildOffer), the token is lit with the dwelling when
//     it could go on the hex too: clicking it lays it there (or picks it up, with several hexes lit).
//   The Chaos Magicians' portal, offered with their stronghold: the token and every free Wasteland hex lit.
//   The Alchemists' upgrades without the priest: the shipping and spade boxes lit until used.
//   The Shapeshifters' free move: their wheel opens as for their stronghold's shift (MainFireAndIce.cs).
//   Whatever a landscape offers, the tick on the player's panel gives up (and ends the turn, if it then can).
//   The Witches' town bonus: two discs over the key's hex, 5 VP and 3 cult steps; the steps light every track to click.
//   The Nomads' coin: a disc beside the power offer's on the map (MapView.ShowPowerOffers; clicked in Main.OnPress).
public partial class Main
{
    private PowerOfferChip _townPointsChip, _townStepsChip; // the Witches' town bonus choices, while owed
    private bool _townStepsChosen;                           // the 3 steps chosen: a track to click next
    private Sprite2D _landscapeOfferSprite;                  // the landscape token lit, if any
    private readonly HashSet<int> _portalHexesLit = new();

    private static Piece LandscapePiece(Faction faction) => new(faction, PieceKind.Landscape, 0);

    // The size a landscape token's texture is made at: as big as it is on a map hex, in the Pieces layer's pixels.
    private int LandscapeTokenPixels =>
        Mathf.Max(8, Mathf.RoundToInt(LandscapeArt.TokenDiameterOnHex(_map.HexRadius) * _map.GlobalScale.Y
            / Mathf.Max(0.0001f, _pieceLayer.GlobalScale.Y)));

    // ---------- What can happen now ----------

    // After a transform to home terrain (as BuildOfferHexes): the hexes the active player's landscape could go on.
    private static ValueList<int> LandscapeOfferHexes(GameState state) =>
        state.Phase == Phase.Playing && state.SpadesPending && state.ActivePlayer is Faction active && state[active].SpadesOwed == 0
            && !OwedChoiceWaiting(state)
            ? Rules.SpadedLandscapeTargets(state, active)
            : ValueList<int>.Empty;

    // The Chaos Portal offered: every hex it could go on.
    private static ValueList<int> PortalHexes(GameState state) =>
        state.Phase == Phase.Playing && state.ActivePlayer is Faction active && state[active].LandscapeOffers.HasFlag(LandscapeOffer.Portal)
            ? new(Enumerable.Range(0, state.Layout.HexCount).Where(hex => Rules.Apply(state, new PlaceLandscape(active, hex)).Accepted))
            : ValueList<int>.Empty;

    // A landscape's offers waiting, and nothing else in the way of ending the turn (or, in Calculator, the next action).
    private static bool OnlyLandscapeOffersWaiting(GameState state, Faction faction) =>
        state.Phase == Phase.Playing && state.ActivePlayer == faction && state.IsInGame(faction)
        && state[faction].LandscapeOffers != LandscapeOffer.None
        && !Rules.IsMidAction(state.WithPlayer(state[faction] with { LandscapeOffers = LandscapeOffer.None }), faction);

    // The tick with a landscape's offers waiting: they are given up, then the turn ends if it can (not in Calculator
    // before passing). False if nothing was waiting.
    private bool DeclineLandscapeOffers(Faction faction)
    {
        GameState state = _history.Current;
        if (!state.IsInGame(faction) || state[faction].LandscapeOffers == LandscapeOffer.None)
            return false;
        Submit(new DeclineLandscapeBonus(faction));
        GameState after = _history.Current;
        if ((!after.Options.Calculator || after.HasPassed(faction)) && Rules.Apply(after, new EndTurn(faction)).Accepted)
            Submit(new EndTurn(faction));
        return true;
    }

    // ---------- Clicks ----------

    // The Witches' town bonus (a disc; with the steps chosen, a track; any other click on the board goes back to the
    // discs) and the Chaos Portal (a lit hex). True if the press was taken.
    private bool OnPressLandscapes(Vector2 mouse)
    {
        GameState state = _history.Current;
        if (state.Phase != Phase.Playing || state.ActivePlayer is not Faction active)
            return false;
        PlayerState me = state[active];
        if (me.WitchesTownBonusHex is not null)
        {
            if (GetViewport().GuiGetHoveredControl() is BaseButton || _map.IsOverOfferChip(mouse) || _map.IsOverCoinChip(mouse))
                return false; // buttons and power offers work as usual
            GetViewport().SetInputAsHandled();
            if (_townPointsChip?.IsOver(mouse) == true)
            {
                _townStepsChosen = false;
                Submit(new ChooseTownBonus(active, null));
            }
            else if (_townStepsChip?.IsOver(mouse) == true)
            {
                _townStepsChosen = true;
                Render(animate: true);
            }
            else if (_townStepsChosen && TrackAt(mouse) is Track track)
            {
                _townStepsChosen = false;
                Submit(new ChooseTownBonus(active, track));
            }
            else if (_townStepsChosen)
            {
                _townStepsChosen = false;
                Render(animate: true);
            }
            return true;
        }
        if (me.LandscapeOffers.HasFlag(LandscapeOffer.Portal) && _map.HexAt(mouse) is int hex && PortalHexes(state).Contains(hex))
        {
            GetViewport().SetInputAsHandled();
            Submit(new PlaceLandscape(active, hex));
            return true;
        }
        return false;
    }

    // ---------- Drawing ----------

    private void RenderLandscapes(GameState state)
    {
        RenderTownBonus(state);

        // The token lit: it can go on a hex just transformed (those hexes are lit by RenderBuildOffer), or the Chaos
        // Portal is offered (every free Wasteland lit here).
        ValueList<int> portal = PortalHexes(state);
        Sprite2D token = state.ActivePlayer is Faction owner && (LandscapeOfferHexes(state).Count > 0 || portal.Count > 0)
            && _dragged != LandscapePiece(owner) && _pieceSprites.TryGetValue(LandscapePiece(owner), out Sprite2D found)
            ? found
            : null;
        if (_landscapeOfferSprite is not null && _landscapeOfferSprite != token && IsInstanceValid(_landscapeOfferSprite))
            EdgeHighlight.SetOnArea(_landscapeOfferSprite, false, default);
        _landscapeOfferSprite = token;
        if (token is not null)
        {
            Rect2 rect = Arrangement.VisibleRect(token).Grow(BuildOfferMargin);
            EdgeHighlight.SetOnArea(token, true, rect, Mathf.Min(rect.Size.X, rect.Size.Y) / 2, BuildOfferGlints,
                HighlightArt.Colour.White, BuildOfferHighlightScale);
        }
        foreach (int hex in _portalHexesLit.Except(portal).ToList())
            _map.ShowHexHighlight(hex, false);
        foreach (int hex in portal)
            _map.ShowHexHighlight(hex, true, BuildOfferGlints);
        _portalHexesLit.Clear();
        _portalHexesLit.UnionWith(portal);

        // The Alchemists' upgrades without the priest: their boxes lit while offered.
        for (var i = 0; i < _panels.Length && i < state.Players.Count; i++)
        {
            PlayerState player = state.Players[i];
            bool offering = state.Phase == Phase.Playing && state.ActivePlayer == player.Faction && IsInteractive(_panels[i]);
            // (Fan factions: the Snow Shamans' free step after passing too, each that can still go up.)
            bool shamans = offering && player.FreeUpgradeOwed;
            _panels[i].HighlightUpgrades(offering && player.LandscapeOffers.HasFlag(LandscapeOffer.Shipping)
                    || shamans && Rules.Apply(state, new UpgradeShipping(player.Faction)).Accepted,
                offering && player.LandscapeOffers.HasFlag(LandscapeOffer.Spades)
                    || shamans && Rules.Apply(state, new UpgradeSpades(player.Faction)).Accepted);
        }
    }

    // The Witches' town bonus while owed: two discs in their colour over the key's hex, 5 VP and 3 cult steps (the VP
    // greyed while a track is being chosen for the steps).
    private void RenderTownBonus(GameState state)
    {
        int? owed = state.Phase == Phase.Playing && state.ActivePlayer is Faction witches ? state[witches].WitchesTownBonusHex : null;
        if (owed is not int at || state.ActivePlayer is not Faction active)
        {
            _townStepsChosen = false;
            _townPointsChip?.HideOffer();
            _townStepsChip?.HideOffer();
            return;
        }
        if (_townPointsChip is null || !IsInstanceValid(_townPointsChip))
        {
            _townPointsChip = new PowerOfferChip { Name = "TownBonusPoints" };
            _townStepsChip = new PowerOfferChip { Name = "TownBonusSteps" };
            _pieceLayer.AddChild(_townPointsChip);
            _pieceLayer.AddChild(_townStepsChip);
        }
        _townPointsChip.ZIndex = _townStepsChip.ZIndex = ColourWheelZIndex; // over the pieces and the key
        float hexRadius = _map.HexRadius * _map.GlobalScale.Y / Mathf.Max(0.0001f, _pieceLayer.GlobalScale.Y);
        float r = hexRadius * TownBonusChipRadius;
        Vector2 middle = _pieceLayer.ToLocal(_map.Hex(at).GlobalPosition) - new Vector2(0, hexRadius * TownBonusRise);
        var apart = new Vector2(r + hexRadius * TownBonusGap / 2, 0);
        Color colour = Art.PieceColour(state[active].Colour);
        int iconSize = Mathf.RoundToInt(r * 1.3f), fontSize = Mathf.RoundToInt(r * 0.95f);
        _townPointsChip.Position = middle - apart;
        _townStepsChip.Position = middle + apart;
        _townPointsChip.ShowContent(Amounts.Of(ResourceKind.VictoryPoint, Config.WitchesTownVictoryPoints), colour, r, iconSize, fontSize,
            enabled: !_townStepsChosen);
        _townStepsChip.ShowContent(Amounts.Of(ResourceKind.AnyCultStep, Landscapes.WitchesTownSteps), colour, r, iconSize, fontSize);
    }

    // While a landscape token is held: over each hex it could go on that isn't just transformed, what laying it there
    // costs: the transform (or the Acolytes' 3 steps, the Mermaids' spade, the Riverwalkers' priest; the portal nothing),
    // and the carpet flight or tunnel if that is how the hex is reached.
    private void ShowLandscapeCosts(GameState state, Faction faction, IEnumerable<Destination> destinations)
    {
        foreach (HexDestination destination in destinations.OfType<HexDestination>())
        {
            int hex = destination.Hex;
            if (state.SpadedHexes.Contains(hex) || state[faction].LandscapeOffers.HasFlag(LandscapeOffer.Portal))
                continue;
            Amounts cost = faction switch
            {
                Faction.Acolytes => Amounts.Of(ResourceKind.AnyCultStep, Landscapes.AcolytesSteps),
                Faction.Mermaids => Amounts.From(state[faction].SpadeCost),
                Faction.Riverwalkers => Amounts.Of(ResourceKind.Priest, 1),
                _ => state[faction].BuildTerrain is Terrain build ? TransformAmounts(state, faction, hex, build) : null,
            };
            if (cost is null)
                continue;
            Cost reach = Landscapes.OnRiver(faction) ? default : Rules.ReachCost(state, faction, Rules.ReachOf(state, faction, hex, allowSkip: true));
            _map.ShowCost(hex, cost + Amounts.From(reach));
        }
    }

    // ---------- Messages ----------

    // The top line while a landscape asks something of the active player (null otherwise).
    private string LandscapeInstruction(GameState state, PlayerState me)
    {
        string who = NameOf(me.Faction), name = Landscapes.Name(me.Faction);
        LandscapeOffer offers = me.LandscapeOffers;
        if (me.WitchesTownBonusHex is not null)
            return _townStepsChosen
                ? $"{who}: click a lit track for {Landscapes.WitchesTownSteps} steps (anywhere else: back)"
                : $"{who} must choose their town bonus: click {Config.WitchesTownVictoryPoints} VP or {Landscapes.WitchesTownSteps} cult steps over the key";
        if (offers.HasFlag(LandscapeOffer.Portal))
            return $"{who} may place their {name} on any free Wasteland: click a lit hex (or click confirm to skip)";
        if (offers.HasFlag(LandscapeOffer.Shapeshift))
            return $"{who} may move their home for free ({name}): click a lit terrain on their wheel (or click confirm to skip)";
        if (offers.HasFlag(LandscapeOffer.Shipping) || offers.HasFlag(LandscapeOffer.Spades))
        {
            string what = offers.HasFlag(LandscapeOffer.Shipping) && offers.HasFlag(LandscapeOffer.Spades) ? "shipping and spades"
                : offers.HasFlag(LandscapeOffer.Shipping) ? "shipping" : "spades";
            return $"{who} may upgrade {what} without a priest ({name}): click a lit box (or click confirm to skip)";
        }
        if (LandscapeOfferHexes(state).Count > 0)
            return $"{who} may build a dwelling or lay their {name}: click a highlighted hex, or the lit piece (anywhere else: no)";
        return null;
    }
}
