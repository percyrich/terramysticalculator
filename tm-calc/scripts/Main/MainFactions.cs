using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the faction selection screen, and the faction abilities that need a control of their own
// (book actions, the Cultists' cult step, the Alchemists' VP trades, the Darklings' priest exchange, the Mermaids'
// river towns). A file of the same class (partial), only to keep Main.cs a manageable size.
public partial class Main
{
    // ---------- The faction selection screen ----------
    // Built in code from faction_choice.tscn (Highlight, Portrait, OrderLabel), one per faction, under %FactionSelect:
    // while factions are chosen, one column per home terrain in the wheel's order, the two factions of that colour
    // one above the other, then (FireAndIceColumnGap further on) a column each for the ice, volcano and variable
    // factions; a faction that can't be added now is dimmed (its colour taken, a choice owed first, ...). During
    // turn order only the chosen factions show, in a row, numbered as they are clicked.
    // Fire & Ice: a faction that chooses its starting terrain gets a colour wheel over its portrait (ice and variable
    // factions as soon as they are added, volcano factions once Proceed is pressed: Phase.ChoosingVolcanoTerrain), the
    // terrains it may take lit; once chosen, that terrain's circle (with the ice or variable ring) stays on its
    // portrait as a badge. The Ice Maidens then choose their favour from the favour tiles laid over the screen.

    private const string FactionChoiceScene = "res://scenes/faction_choice.tscn";

    private readonly Dictionary<Faction, Node2D> _choices = new();

    private void BuildFactionSelect()
    {
        foreach (Node old in _factionSelect.GetChildren())
        {
            _factionSelect.RemoveChild(old);
            old.QueueFree();
        }
        var scene = GD.Load<PackedScene>(FactionChoiceScene);
        foreach (Faction faction in AllFactions)
        {
            Node2D choice = scene.Instantiate<Node2D>();
            choice.Name = faction.ToString();
            choice.Position = GridPlace(faction);
            _factionSelect.AddChild(choice);
            TextStyle.OutlinedBlack(choice.GetNode<Label>("OrderLabel")); // the place in the turn order
            Sprite2D portrait = choice.GetNode<Sprite2D>("Portrait");
            portrait.Texture = Art.PortraitTexture(faction);
            portrait.Scale = Vector2.One * FactionPortraitScale;

            var name = new Label
            {
                Name = "NameLabel",
                Text = Factions.Name(faction),
                HorizontalAlignment = HorizontalAlignment.Center,
                Size = new Vector2(240, FactionNameFontSize * 1.5f),
                Position = FactionNameOffset * FactionPortraitScale - new Vector2(120, FactionNameFontSize * 0.75f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            name.AddThemeFontSizeOverride("font_size", FactionNameFontSize);
            name.AddThemeConstantOverride("outline_size", 6);
            name.AddThemeColorOverride("font_outline_color", new Color("1b1b1b"));
            choice.AddChild(name);
            var badge = new ColourWheel
            {
                Name = "TerrainBadge", Visible = false, ZIndex = 2,
                Position = FactionBadgeOffset * FactionPortraitScale, Scale = Vector2.One * FactionBadgeScale,
            };
            choice.AddChild(badge);
            badge.Spread = 0f; // one circle
            _choices[faction] = choice;
        }

        _setupWheel = new ColourWheel
        {
            Name = "StartingTerrainWheel", Visible = false, ZIndex = 30,
            Scale = Vector2.One * FactionWheelScale, BackingColour = FactionWheelBackingColour,
        };
        _factionSelect.AddChild(_setupWheel);
        _setupWheel.TerrainChosen += terrain =>
        {
            if (TerrainChooser(_history.Current) is PlayerState chooser)
                Submit(new ChooseStartingTerrain(chooser.Faction, terrain));
            else if (ChangelingsChoosing(_history.Current)) // fan factions: B, then C (MainFanFactionsEmberGeologistsChangelings.cs)
                OnChangelingTerrainChosen(terrain);
        };
        BuildFavourOverlay();
    }

    private ColourWheel _setupWheel;

    // The faction choosing its starting terrain now, if any: an ice or variable faction just added, or a volcano
    // faction once the factions are confirmed.
    private static PlayerState TerrainChooser(GameState state) => state.Phase switch
    {
        Phase.ChoosingFactions => state.Players.FirstOrDefault(p => p.Kind is FactionKind.Ice or FactionKind.Variable && p.StartingTerrain is null),
        // (with the Changelings' B and C, in the order the factions were chosen: Rules.LateTerrainChooser)
        Phase.ChoosingVolcanoTerrain => state.Players.FirstOrDefault(p => p.Kind == FactionKind.Volcano && p.Faction == Rules.LateTerrainChooser(state)),
        _ => null,
    };

    // ---------- The Ice Maidens' favour, chosen on the faction screen ----------

    private Node2D _favourOverlay;
    private readonly Dictionary<int, Node2D> _overlayFavours = new();

    private void BuildFavourOverlay()
    {
        _favourOverlay = new Node2D { Name = "FavourOverlay", Visible = false, ZIndex = 40 };
        _factionSelect.AddChild(_favourOverlay);
        const float far = 5000f; // the dim covers the whole screen, wherever the faction screen is
        _favourOverlay.AddChild(new Polygon2D
        {
            Name = "Dim", Color = FavourOverlayDimColour,
            Polygon = new[] { new Vector2(-far, -far), new Vector2(far, -far), new Vector2(far, far), new Vector2(-far, far) },
        });
        for (var id = 0; id < Favours.Count; id++)
        {
            Node2D tile = FavourTileView.Create(id);
            int place = FavourBankPlace(id); // three per track, as in the bank: a column per track here
            tile.Position = FavourOverlayOffset + new Vector2((place / 3 - 1.5f) * FavourOverlayStep.X, (place % 3 - 1) * FavourOverlayStep.Y);
            tile.Scale = Vector2.One * FavourOverlayScale;
            _favourOverlay.AddChild(tile);
            _overlayFavours[id] = tile;
        }
    }

    // While a faction owes a favour during faction choice: every favour it could take lit yellow, the others dimmed.
    private void RenderFavourOverlay(GameState state)
    {
        PlayerState owing = state.Phase == Phase.ChoosingFactions ? state.Players.FirstOrDefault(p => p.FavoursOwed > 0) : null;
        _favourOverlay.Visible = owing is not null;
        if (owing is null)
            return;
        foreach (var (id, tile) in _overlayFavours)
        {
            bool can = Rules.CanTakeFavour(state, owing.Faction, id);
            EdgeHighlight.Set(tile, can, HighlightArt.Colour.Yellow, glints: 1);
            tile.Modulate = can ? Colors.White : UnchosenTint;
        }
    }

    private int? OverlayFavourUnder(Vector2 point) =>
        _overlayFavours.Where(pair => Hit.IsOver(pair.Value, point)).Select(pair => (int?)pair.Key).FirstOrDefault();

    // Where a faction's portrait goes while factions are chosen: its column, the rows of those shown centred on the
    // grid's middle (two rows; four with the fan factions).
    private Vector2 GridPlace(Faction faction)
    {
        int column = GridColumn(faction);
        GameOptions options = _history.Current.Options;
        List<Faction> inColumn = AllFactions.Where(other => GridColumn(other) == column && Shown(options, other)).ToList();
        int row = Math.Max(0, inColumn.IndexOf(faction));
        float middle = (Math.Max(2, inColumn.Count) - 1) / 2f;
        // Seven colours, then ice, volcano, variable (only the seven without Fire & Ice).
        bool fireAndIce = _history.Current.Options.FireAndIce;
        int columns = fireAndIce ? 10 : 7;
        float gap = fireAndIce ? (column >= 7 ? 1 : -1) * FireAndIceColumnGap / 2 : 0;
        float x = (column - (columns - 1) / 2f) * FactionGridStep.X + gap;
        float rows = options.FanFactions ? FanFactionGridScale : 1f;
        return new Vector2(x, (row - middle) * FactionGridStep.Y * rows);
    }

    // The factions in the grid for these options: Fire & Ice's only with it, the fan factions only with them.
    private static bool Shown(GameOptions options, Faction faction) =>
        (options.FireAndIce || Factions.Kind(faction) == FactionKind.Base) && (options.FanFactions || !Factions.IsFanFaction(faction));

    // The base game's factions by home terrain in the wheel's order (0-6), then the ice (7), volcano (8) and variable (9).
    private static int GridColumn(Faction faction) => Factions.Kind(faction) switch
    {
        FactionKind.Base => Terrains.Wheel.IndexOf(Factions.HomeTerrain(faction)),
        FactionKind.Ice => 7,
        FactionKind.Volcano => 8,
        _ => 9,
    };

    private Node2D Choice(Faction faction) => _choices[faction];
    private Sprite2D ChoicePortrait(Faction faction) => Choice(faction).GetNode<Sprite2D>("Portrait");

    private Faction? ChoiceUnder(Vector2 point) =>
        AllFactions
            .Where(faction => Choice(faction).Visible && IsUnder(ChoicePortrait(faction), point))
            .Select(faction => (Faction?)faction)
            .FirstOrDefault();

    // Choosing factions: every portrait in the grid, the chosen highlighted, those that can't be added now dimmed.
    // Turn order: only the chosen, in a row in the order they were added, numbered once placed.
    private void RenderFactionSelect(GameState state)
    {
        if (!_factionSelect.Visible)
            return; // brought up to date when it shows again
        bool turnOrder = state.Phase == Phase.ChoosingTurnOrder;
        List<Faction> chosen = state.Players.Select(player => player.Faction).ToList();
        foreach (Faction faction in AllFactions)
        {
            Node2D choice = Choice(faction);
            bool inGame = state.IsInGame(faction);
            bool unavailable = !inGame && !turnOrder && !Rules.Apply(state, new AddPlayer(faction)).Accepted;
            ShowChosenFaction(choice, inGame && !turnOrder);
            choice.Modulate = unavailable ? UnchosenTint : Colors.White;
            choice.Visible = (!turnOrder || inGame) && Shown(state.Options, faction);
            choice.Scale = Vector2.One * (state.Options.FanFactions && !turnOrder ? FanFactionGridScale : 1f); // four rows
            ShowTerrainBadge(choice, inGame ? state[faction] : null);

            ShowStartingVictoryPoints(choice, turnOrder && inGame ? Rules.StartingVictoryPoints(state[faction]) : null);

            int place = state.TurnOrder.IndexOf(faction); // -1 if not placed
            choice.GetNode<Label>("OrderLabel").Text = turnOrder && place >= 0 ? (place + 1).ToString() : "";

            Vector2 target = turnOrder && inGame
                ? new Vector2((chosen.IndexOf(faction) - (chosen.Count - 1) / 2f) * TurnOrderSpacing, 0)
                : GridPlace(faction);
            choice.Position = target; // straight there: the portraits never slide about
        }

        // The starting-terrain wheel over the portrait of the faction choosing (the terrains it may take lit).
        PlayerState chooser = state.Phase is Phase.ChoosingFactions or Phase.ChoosingVolcanoTerrain ? TerrainChooser(state) : null;
        _setupWheel.Visible = chooser is not null;
        // Fan factions: the Changelings' B and C once every other terrain is chosen (B lit white once clicked).
        if (chooser is null && ChangelingsChoosing(state))
        {
            _setupWheel.Visible = true;
            _setupWheel.Position = GridPlace(Faction.Changelings) + FactionWheelOffset;
            List<Terrain> open = ChangelingChoices(state);
            _setupWheel.SetDisabled(Terrains.Wheel.Except(open));
            _setupWheel.ShowChoices(Terrains.Wheel[0], open,
                terrain => terrain == _changelingB ? HighlightArt.Colour.White : HighlightArt.Colour.Purple);
        }
        else
            _changelingB = null;
        if (chooser is not null)
        {
            _setupWheel.Position = GridPlace(chooser.Faction) + FactionWheelOffset;
            List<Terrain> open = Terrains.Wheel
                .Where(terrain => Rules.Apply(state, new ChooseStartingTerrain(chooser.Faction, terrain)).Accepted).ToList();
            _setupWheel.SetDisabled(Terrains.Wheel.Except(open));
            _setupWheel.ShowChoices(Terrains.Wheel[0], open);
        }
        RenderFavourOverlay(state);
    }

    // The VP icon with the starting VP on it, above the portrait (null: hidden). Made the first time it is needed.
    private void ShowStartingVictoryPoints(Node2D choice, int? points)
    {
        var icon = choice.GetNodeOrNull<Sprite2D>("StartingVP");
        if (icon is null)
        {
            if (points is null)
                return;
            icon = new Sprite2D { Name = "StartingVP", Texture = Art.ResourceIcon(ResourceKind.VictoryPoint, size: StartingVictoryPointsIconSize) };
            var number = new Label
            {
                Name = "Number", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Size = Vector2.One * StartingVictoryPointsIconSize, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            number.Position = Art.ResourceNumberCentre(ResourceKind.VictoryPoint, size: StartingVictoryPointsIconSize) - number.Size / 2;
            number.AddThemeFontSizeOverride("font_size", StartingVictoryPointsFontSize);
            number.AddThemeConstantOverride("outline_size", 6);
            number.AddThemeColorOverride("font_color", Colors.Black); // black with a white edge
            number.AddThemeColorOverride("font_outline_color", Colors.White);
            icon.AddChild(number);
            choice.AddChild(icon);
        }
        icon.Position = StartingVictoryPointsOffset;
        icon.Visible = points is not null;
        if (points is int shown)
            icon.GetNode<Label>("Number").Text = shown.ToString();
    }

    // The faction whose starting VP icon is under the point (while the turn order is chosen), if any.
    private Faction? StartingVictoryPointsUnder(Vector2 point) =>
        AllFactions
            .Where(faction => Choice(faction).Visible && Choice(faction).GetNodeOrNull<Sprite2D>("StartingVP") is { Visible: true } icon
                && IsUnder(icon, point))
            .Select(faction => (Faction?)faction)
            .FirstOrDefault();

    // A chosen starting terrain on the portrait: its circle, with the ice ring (ice factions) or the variable ring
    // (Riverwalkers, Shapeshifters) over it; volcano factions' just the circle. Nothing before it is chosen.
    private static void ShowTerrainBadge(Node2D choice, PlayerState player)
    {
        if (choice.GetNodeOrNull<ColourWheel>("TerrainBadge") is not ColourWheel badge)
            return;
        badge.Visible = player?.StartingTerrain is not null;
        if (player?.StartingTerrain is not Terrain start)
            return;
        badge.Focus = start;
        badge.Ring = player.Kind is FactionKind.Ice or FactionKind.Variable ? start : null;
        badge.RingIcon = player.Kind == FactionKind.Ice ? Icon.IceRing : Icon.VariableRing;
        badge.ShowStill(start);
    }

    // A chosen faction: a white edge highlight (no glints) round its portrait, as tiles and areas have elsewhere. The
    // same size for every faction: big enough for every portrait's drawing.
    private void ShowChosenFaction(Node2D choice, bool chosen)
    {
        Sprite2D portrait = choice.GetNode<Sprite2D>("Portrait");
        Rect2 painted = StandardPortraitRect();
        var rect = new Rect2(portrait.Position + painted.Position * portrait.Scale, painted.Size * portrait.Scale).Grow(FactionHighlightMargin);
        EdgeHighlight.SetOnArea(choice, chosen, rect, FactionHighlightCornerRadius, 0, HighlightArt.Colour.White, FactionHighlightScale);
    }

    // The part of a portrait that is drawn (its transparent margin left out), in the sprite's own pixels; worked out
    // once per picture.
    private readonly Dictionary<Texture2D, Rect2> _paintedPortraits = new();

    // Every portrait's drawing on the faction screen together (PaintedRect, in a portrait sprite's own pixels, round its
    // centre): the highlight's one size. Worked out once all the choices are made.
    private Rect2? _standardPortrait;

    private Rect2 StandardPortraitRect()
    {
        if (_standardPortrait is Rect2 known)
            return known;
        Rect2? all = null;
        foreach (Node2D choice in _choices.Values)
            if (choice.GetNodeOrNull<Sprite2D>("Portrait") is Sprite2D sprite && sprite.Texture is not null)
            {
                Rect2 painted = PaintedRect(sprite);
                all = all?.Merge(painted) ?? painted;
            }
        Rect2 standard = all ?? new Rect2(-50, -50, 100, 100);
        if (all is not null)
            _standardPortrait = standard;
        return standard;
    }

    private Rect2 PaintedRect(Sprite2D sprite)
    {
        Rect2 whole = sprite.GetRect();
        if (sprite.Texture is not Texture2D texture)
            return whole;
        if (!_paintedPortraits.TryGetValue(texture, out Rect2 used))
        {
            used = new Rect2(0, 0, 1, 1);
            Image image = texture.GetImage();
            if (image is not null && !image.IsEmpty())
            {
                if (image.IsCompressed())
                    image.Decompress();
                Rect2I area = image.GetUsedRect();
                if (area.Size.X > 0 && area.Size.Y > 0)
                {
                    Vector2 size = image.GetSize();
                    used = new Rect2((Vector2)area.Position / size, (Vector2)area.Size / size);
                }
            }
            _paintedPortraits[texture] = used;
        }
        return new Rect2(whole.Position + used.Position * whole.Size, used.Size * whole.Size);
    }

    // ---------- Faction abilities: clicks ----------

    // Handles a press on: a track while the Acolytes pay steps for a Volcano, or while the Cultists (or the Acolytes)
    // owe a cult step; the active player's book action (the Engineers' bridge); the Alchemists' VP (sells 1 for a coin)
    // or coins (picked up, to drop on their VP: 2 for 1 VP).
    // Returns false if the press was on none of these.
    private bool OnPressFactionAbilities(Vector2 mouse)
    {
        GameState state = _history.Current;
        if (state.Phase is not (Phase.Playing or Phase.RoundEndSpades) || state.ActivePlayer is not Faction active)
            return false;

        // The Acolytes paying for a Volcano (from a lit track), or taking the steps their spades became (between rounds too).
        if (state[active].CultStepsToPay > 0 && TrackAt(mouse) is Track paid)
        {
            Submit(new PayCultSteps(active, paid)); // refused with a reason if that track has too few steps
            return true;
        }
        if (state[active].CultStepsOwed > 0 && TrackAt(mouse) is Track track)
        {
            Submit(new TakeCultStep(active, track));
            return true;
        }

        if (state.Phase != Phase.Playing || PanelFor(active) is not PlayerPanel panel)
            return false;

        if (IsInteractive(panel) && panel.IsOverBookAction(mouse))
        {
            OnBookClicked(active); // the bridge, the fan factions' books (MainFanFactions.cs)
            return true;
        }

        if (active == Faction.Alchemists && IsInteractive(panel))
        {
            if (panel.IsOverVictoryPointZone(mouse))
            {
                Submit(new ConvertVictoryPointToCoin(active));
                return true;
            }
            if (panel.IsOverCoinZone(mouse))
            {
                TryStartCoinDrag(panel, active, mouse);
                return true;
            }
        }
        return false;
    }

    // ---------- The Alchemists: dragging coins onto their VP ----------

    private Sprite2D _draggedCoin;
    private PlayerPanel _coinDragPanel;

    private void TryStartCoinDrag(PlayerPanel panel, Faction faction, Vector2 mouse)
    {
        MoveResult trial = Rules.Apply(_history.Current, new ConvertCoinsToVictoryPoint(faction));
        if (!trial.Accepted)
        {
            ShowIssues(trial.Issues); // e.g. "The Alchemists need 2 coins for a victory point."
            return;
        }
        _draggedCoin = new Sprite2D
        {
            Texture = Art.ResourceIcon(ResourceKind.Coin, size: 64),
            ZIndex = 100,
            Scale = _pieceLayer.GlobalScale,
        };
        AddChild(_draggedCoin);
        _draggedCoin.GlobalPosition = mouse;
        _coinDragPanel = panel;
        panel.HighlightVictoryPointZone(true);
        ShowPrompt("Drop on their VP: 2 coins for 1 VP.");
    }

    private void EndCoinDrag()
    {
        PlayerPanel panel = _coinDragPanel;
        bool onVictoryPoints = panel.IsOverVictoryPointZone(GetGlobalMousePosition());
        panel.HighlightVictoryPointZone(false);
        _draggedCoin.QueueFree();
        _draggedCoin = null;
        _coinDragPanel = null;
        ShowNote("");
        if (onVictoryPoints && panel.ShownFaction is Faction faction)
            Submit(new ConvertCoinsToVictoryPoint(faction)); // dropped anywhere else: nothing happens
    }

    // ---------- Faction abilities: drawing ----------

    private readonly HashSet<Track> _cultTracksLit = new();
    private readonly HashSet<int> _riverTownHexesLit = new();

    private void RenderFactionAbilities(GameState state)
    {
        Faction? active = state.Phase is Phase.Playing or Phase.RoundEndSpades ? state.ActivePlayer : null;

        // A cult step owed (the Cultists', the Acolytes'): every track lit yellow, one glint, until one is clicked.
        // The Acolytes paying for a Volcano: only the tracks with enough steps.
        var lit = new HashSet<Track>();
        if (active is Faction stepper)
        {
            PlayerState player = state[stepper];
            if (player.CultStepsToPay > 0)
                lit.UnionWith(Rules.TracksThatCanPay(state, stepper, player.CultStepsToPay));
            else if (player.CultStepsOwed > 0 || (player.WitchesTownBonusHex is not null && _townStepsChosen)) // MainLandscapes.cs
                lit.UnionWith(AllTracks);
        }
        lit.UnionWith(FanTracksLit(state)); // the Djinni's starting steps and lamp (MainFanFactions.cs)
        foreach (Track track in AllTracks)
            if (lit.Contains(track) || _cultTracksLit.Contains(track))
                EdgeHighlight.SetOnCultTrack(_tracks.GetNode<Node2D>(track.ToString()), lit.Contains(track), glints: 1);
        _cultTracksLit.Clear();
        _cultTracksLit.UnionWith(lit);

        // The Mermaids' river towns: each river hex a key could go on, lit white with one glint.
        var rivers = new HashSet<int>(RiverTownsNow(state).Select(river => river.RiverHex));
        foreach (int hex in _riverTownHexesLit.Except(rivers).ToList())
            _map.ShowHexHighlight(hex, false);
        foreach (int hex in rivers)
            _map.ShowHexHighlight(hex, true, glints: 1);
        _riverTownHexesLit.Clear();
        _riverTownHexesLit.UnionWith(rivers);
    }

    // The Mermaids' river towns open to them now: on their turn, while nothing else is under way (spades, a bridge,
    // power offers, a town they must form first, a cult step).
    private static ValueList<RiverTown> RiverTownsNow(GameState state)
    {
        if (state.Phase != Phase.Playing || state.ActivePlayer is not Faction active || active != Faction.Mermaids
            || state.SpadesPending || state[active].BridgesOwed > 0 || state.PowerOffers.Count > 0
            || Rules.FormableTowns(state, active).Count > 0)
            return ValueList<RiverTown>.Empty;
        return Rules.RiverTowns(state, active);
    }

    // ---------- Names in messages ----------

    // "Chaos Magicians" rather than the enum's ChaosMagicians; "nobody" for null.
    private static string NameOf(Faction? faction) => faction is Faction f ? Factions.Name(f) : "nobody";
}
