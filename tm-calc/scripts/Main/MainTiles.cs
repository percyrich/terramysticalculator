using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: favour, round and pass tiles: the setup screens that choose them, drawing them on the board,
// and clicking them. A file of the same class (partial).
public partial class Main
{
    // ---------- Pass tiles: click one in the bank to pick it (setup) or to pass with it ----------

    // A pass tile in the bank, or the active player's own tile (the one on their panel).
    private int? PassTileUnder(Vector2 point)
    {
        GameState state = _history.Current;
        int? own = state.ActivePlayer is Faction active ? state[active].PassTile : null;
        int? extra = state.ActivePlayer is Faction holder ? state[holder].ExtraPassTile : null; // the Archivists' second
        return _passTileSprites
            .Where(pair => (_bankPassTiles.Contains(pair.Key) || pair.Key == own || pair.Key == extra) && Hit.IsOver(pair.Value, point))
            .OrderByDescending(pair => pair.Value.ZIndex)
            .Select(pair => (int?)pair.Key)
            .FirstOrDefault();
    }

    // Clicking your own tile passes (its pass bonuses score; in rounds 1-5 the bank then lights up).
    // Clicking a bank tile picks it: during setup, or after passing. The rules refuse anything else.
    private void OnPassTileClicked(int tile)
    {
        GameState state = _history.Current;
        if (state.ActivePlayer is not Faction active)
        {
            ShowNote("Nobody is taking a turn right now.");
            return;
        }
        // (The Archivists pass by clicking either of their two.)
        Submit(state[active].PassTile == tile || state[active].ExtraPassTile == tile ? new Pass(active) : new PickPassTile(active, tile));
    }

    // ---------- Favours: click a tile in the bank to take that favour ----------

    // The favour whose bank tile is under the point (the top tile of a stack wins), if any.
    private int? FavourUnder(Vector2 point) =>
        _favourSprites
            .Where(pair => _bankFavours.Contains(pair.Key) && Hit.IsOver(pair.Value, point))
            .OrderByDescending(pair => pair.Value.ZIndex)
            .Select(pair => (int?)pair.Key.Id)
            .FirstOrDefault();

    private void OnFavourClicked(int favour)
    {
        if (_history.Current.ActivePlayer is not Faction active)
        {
            ShowNote("Nobody is taking a turn right now.");
            return;
        }
        Submit(new TakeFavour(active, favour)); // Render flies the tile to the player's panel
    }

    // Favour tiles: stacked in the bank (one stack per favour, top copy drawn on top), or in a row
    // on their owner's panel. While the active player owes a favour, the top tile of every stack they could take
    // from (not one they already have) has a yellow edge highlight with a glint.
    private void RenderFavours(GameState state, bool animate)
    {
        var placements = new Dictionary<FavourTile, Placement>();
        var onTop = new HashSet<FavourTile>(); // the top tile of each bank stack: the one that can be taken
        bool owed = state.Phase is Phase.Playing or Phase.SettingUp // Setup: favours owed by the selected faction too
            && state.ActivePlayer is Faction active && state[active].FavoursOwed > 0;
        // The favours the player owing one already has: those can't be taken again, so aren't lit.
        var held = new HashSet<int>(state.ActivePlayer is Faction taker
            ? state[taker].FavourTiles.Select(owned => owned.Id)
            : Enumerable.Empty<int>());

        for (var id = 0; id < state.FavourBank.Count; id++)
        {
            ValueList<FavourTile> stack = state.FavourBank[id];
            Node2D anchor = _favourBank.GetNode<Node2D>($"Stack{FavourBankPlace(id)}");
            for (var i = 0; i < stack.Count; i++)
                placements[stack[i]] = new Placement(anchor, FavourStackOffset * i, i, null);
            if (stack.Count > 0)
                onTop.Add(stack[^1]);
        }

        for (var p = 0; p < state.Players.Count; p++)
        {
            PlayerState player = state.Players[p];
            PlayerPanel panel = _panels[p];
            for (var i = 0; i < player.FavourTiles.Count; i++)
                placements[player.FavourTiles[i]] = new Placement(panel.FavourRow,
                    panel.FavourOffset(i, player.FavourTiles.Count + player.FavoursOwed, FavourTileSize()), i, panel); // blanks after them
        }

        _bankFavours.Clear();
        _bankFavours.UnionWith(placements.Where(pair => pair.Value.Panel is null).Select(pair => pair.Key));

        foreach (FavourTile gone in _favourSprites.Keys.Except(placements.Keys).ToList())
        {
            RemoveSprite(_favourSprites[gone]);
            _favourSprites.Remove(gone);
        }

        foreach (var (tile, placement) in placements)
        {
            // Tiles take the size of the marker they sit on; offsets are in that marker's pixels too.
            Vector2 scale = ScaleFor(placement);
            Vector2 target = TargetFor(placement.Panel is null ? placement with { Offset = placement.Offset * scale } : placement);
            Node2D sprite = FavourSpriteFor(tile, target, scale);
            sprite.ZIndex = placement.Depth + RaiseFor(placement.Panel) + (tile == _hoveredFavour ? FavourHoverRaise : 0);
            EdgeHighlight.Set(sprite, owed && placement.Panel is null && onTop.Contains(tile) && !held.Contains(tile.Id),
                HighlightArt.Colour.Yellow, glints: 1);
            // Its bonus action (if it has one), crossed out once its holder has used it this round.
            if (sprite is FavourTileView view)
                view.ShowActionUsed(placement.Panel?.ShownFaction is Faction holder
                    && Favours.All[tile.Id].BonusAction is BonusAction action
                    && state[holder].BonusActionsUsed.Contains(new BonusActionId(BonusSource.Favour, action)));
            bool onPassedPanel = placement.Panel is not null && ShownPassed(state, placement.Panel.ShownFaction!.Value);
            // On a panel, a tile fades with the part it sits on (if that part is hidden in the compact layout).
            bool shown = placement.Panel?.IsShownInLayout(placement.Anchor) ?? true;
            TintAndFade(sprite, onPassedPanel ? PlayerPanel.PassedTint : Colors.White, shown ? 1f : 0f, animate);
            Slide(sprite, target, animate);
            Resize(sprite, scale, animate);
        }

        RenderBlankFavours(state, animate); // favours owed (MainModes.cs)
    }

    // A favour tile's size in its own pixels: favour.tscn's Background (BlankTile.Favour at its TileScale), or the
    // blank tile's natural size without it.
    private Vector2 FavourTileSize()
    {
        if (_favourTileSize is Vector2 known)
            return known;
        FavourTileView probe = FavourTileView.Create(0);
        float scale = probe?.GetNodeOrNull<BlankTileSprite>("Background")?.TileScale ?? 1f;
        probe?.Free();
        _favourTileSize = IconArt.NaturalSize(BlankTile.Favour) * scale;
        return _favourTileSize.Value;
    }
    private Vector2? _favourTileSize;

    // A favour on a player's panel under the mouse comes to the top, so overlapping ones can be read.
    private const int FavourHoverRaise = 30;
    private FavourTile? _hoveredFavour;

    private void UpdateFavourHover(Vector2 mouse)
    {
        FavourTile? over = _board.Visible
            ? _favourSprites
                .Where(pair => !_bankFavours.Contains(pair.Key) && pair.Value.IsVisibleInTree() && Hit.IsOver(pair.Value, mouse))
                .OrderByDescending(pair => pair.Value.ZIndex)
                .Select(pair => (FavourTile?)pair.Key)
                .FirstOrDefault()
            : null;
        if (over == _hoveredFavour)
            return;
        if (_hoveredFavour is FavourTile old && _favourSprites.TryGetValue(old, out Node2D was))
            was.ZIndex -= FavourHoverRaise;
        _hoveredFavour = over;
        if (over is FavourTile now && _favourSprites.TryGetValue(now, out Node2D tile))
            tile.ZIndex += FavourHoverRaise;
    }

    // Where favour `id` sits in the bank: Stack0..Stack11, three per cult in track order (fire, water, earth, air),
    // and within a cult the 3-step favour first, then the 2-step, then the 1-step.
    private static int FavourBankPlace(int id) => (int)Favours.All[id].Track * 3 + (3 - Favours.All[id].Steps);

    // The existing view of this tile, or a new one (favour.tscn) created at its target.
    private Node2D FavourSpriteFor(FavourTile tile, Vector2 target, Vector2 scale)
    {
        if (_favourSprites.TryGetValue(tile, out Node2D sprite))
            return sprite;

        sprite = FavourTileView.Create(tile.Id);
        sprite.Position = target;
        sprite.Scale = scale;
        _pieceLayer.AddChild(sprite);
        _favourSprites[tile] = sprite;
        return sprite;
    }

    // ---------- Setup tile screens ----------

    private static Node2D TileChoice(Node2D screen, int tile) => screen.GetNode<Node2D>($"Tile{tile}");

    // A choice shows the tile's view on its Tile sprite (which draws nothing itself).
    // A chosen tile on a setup screen: a white edge highlight round the tile view (no glint).
    // warn: red instead of white (round tile 4 as round 5 or 6: to be avoided in proper play).
    private static void ShowChosen(Node2D choice, bool chosen, bool warn = false)
    {
        var view = choice.GetNode<Sprite2D>("Tile").GetChild<Node2D>(0);
        EdgeHighlight.Set(view, chosen, warn ? HighlightArt.Colour.Red : HighlightArt.Colour.White, glints: 0);
    }

    private static void SetUpTileChoice(Node2D choice, Node2D view)
    {
        TextStyle.OutlinedBlack(choice.GetNode<Label>("OrderLabel")); // the round number, once chosen
        choice.GetNode<Sprite2D>("Tile").AddChild(view);
    }

    private static int? TileChoiceUnder(Node2D screen, int count, Vector2 point) =>
        Enumerable.Range(0, count)
            .Where(tile => Hit.IsOver(TileChoice(screen, tile).GetNode<Sprite2D>("Tile"), point))
            .Select(tile => (int?)tile)
            .FirstOrDefault();

    private const int SpadeRoundTile = 4; // RoundTiles.All[4]: 2 VP per spade, a coin per earth step

    // Round tiles: highlighted and numbered by round once chosen. Pass tiles: highlighted when in the game.
    private void RenderTileSelects(GameState state)
    {
        for (var tile = 0; tile < RoundTiles.Count; tile++)
        {
            Node2D choice = TileChoice(_roundTileSelect, tile);
            int round = state.ChosenRoundTiles.IndexOf(tile); // -1 if not chosen
            // Round tile 4 (2 VP per spade, a coin per earth step) as the fifth or sixth round: lit red, a reminder that
            // a ruling says to avoid it.
            ShowChosen(choice, round >= 0, warn: tile == SpadeRoundTile && round >= 4);
            choice.Modulate = RoundTiles.All[tile].MiniExpansion && !state.Options.MiniExpansion ? UnchosenTint : Colors.White;
            choice.GetNode<Label>("OrderLabel").Text = round >= 0 ? (round + 1).ToString() : "";
        }
        for (var tile = 0; tile < PassTiles.Count; tile++)
        {
            Node2D choice = TileChoice(_passTileSelect, tile);
            ShowChosen(choice, state.ChosenPassTiles.Contains(tile));
            choice.Modulate = PassTiles.All[tile].MiniExpansion && !state.Options.MiniExpansion ? UnchosenTint : Colors.White;
            choice.GetNode<Label>("OrderLabel").Text = "";
        }
    }

    // ---------- Round tiles and pass tiles on the board ----------

    // The six chosen round tiles beside the map, round 1 at Round1. Rounds already played are dimmed; the current
    // round's tile has a white edge highlight.
    // These never move, so each sprite is simply a child of its marker.
    private void RenderRoundTiles(GameState state)
    {
        for (var round = 1; round <= Config.Rounds; round++)
        {
            int index = round - 1;
            // Calculator: a round left without a tile shows a blank one once the game has started (-1).
            int? tile = round <= state.ChosenRoundTiles.Count ? state.ChosenRoundTiles[index]
                : state.Options.Calculator && state.Phase >= Phase.PlacingDwellings ? -1 : null;
            if (_roundTileShown[index] != tile) // the choice changed (e.g. undone during setup)
            {
                _roundTileSprites[index]?.QueueFree();
                _roundTileSprites[index] = null;
                if (tile == -1)
                {
                    _roundTileSprites[index] = new Sprite2D { Texture = Art.RoundTileTexture() }; // a blank round (Calculator)
                    _roundTileRow.GetNode<Node2D>($"Round{round}").AddChild(_roundTileSprites[index]);
                }
                else if (tile is int chosen)
                {
                    _roundTileSprites[index] = RoundTileView.Create(chosen, endOfRound: round < Config.Rounds);
                    _roundTileRow.GetNode<Node2D>($"Round{round}").AddChild(_roundTileSprites[index]);
                }
                _roundTileShown[index] = tile;
            }
            if (_roundTileSprites[index] is Node2D sprite)
            {
                sprite.Modulate = round < state.Round ? PastRoundTint : Colors.White;
                bool current = round == state.Round
                    && state.Phase is Phase.Playing or Phase.RoundEndSpades or Phase.SettingUp or Phase.OrderingTurns;
                // Fan factions: the Time Travellers' turn (or selected) scores the previous and next round's tiles:
                // their left halves lit yellow, and only the current round's right half (its cult reward) white.
                bool travellers = current && state.ActivePlayer == Faction.TimeTravellers && state.Phase == Phase.Playing;
                EdgeHighlight.Set(sprite, current && !travellers, HighlightArt.Colour.White, glints: 0);
                ShowRoundTileHalf(sprite, 1, travellers, HighlightArt.Colour.White);
                bool scoring = state.ActivePlayer == Faction.TimeTravellers && state.Phase == Phase.Playing
                    && Rules.ScoringTiles(state, Faction.TimeTravellers).Any()
                    && (round == (state.Round == 1 ? Config.Rounds : state.Round - 1) || round == (state.Round == Config.Rounds ? 1 : state.Round + 1));
                ShowRoundTileHalf(sprite, 0, scoring, HighlightArt.Colour.Yellow);
            }
        }
    }

    // A held pass tile's income has been drawn this round: the round is under way and its holder hasn't passed (a tile
    // taken when passing pays next round).
    private static bool IncomeDrawn(GameState state, Faction faction) =>
        state.Phase == Phase.Playing && state.IsInGame(faction) && !state.HasPassed(faction);

    // What the panel's income shows: next round's, so not a held tile whose income has already been drawn.
    // Plus this round's cult reward as the tracks stand (coins, workers, priests, power: Rules.RoundEndIncome).
    private static Income PredictedIncome(GameState state, PlayerState player)
    {
        Income income = player.Income + Rules.RoundEndIncome(state, player.Faction) + SafeIncome(state, player);
        if (!IncomeDrawn(state, player.Faction))
            return income;
        foreach (int tile in new[] { player.PassTile, player.ExtraPassTile }.OfType<int>())
        {
            Income paid = PassTiles.All[tile].Income;
            income = new Income(income.Coins - paid.Coins, income.Workers - paid.Workers, income.Priests - paid.Priests,
                income.Power - paid.Power, income.VictoryPoints - paid.VictoryPoints);
        }
        return income;
    }

    // The Treasurers: their Safe (and what is being chosen for it) comes back doubled as the next round begins, priests
    // as far as the supply goes (Rules.OpenSafe).
    private static Income SafeIncome(GameState state, PlayerState player)
    {
        if (player.Faction != Faction.Treasurers)
            return default;
        Income chosen = SafeChosen(state);
        int priests = player.SafePriestList.Count + chosen.Priests;
        return new Income(2 * (player.SafeCoins + chosen.Coins), 2 * (player.SafeWorkers + chosen.Workers),
            priests + Math.Min(priests, player.PriestSupply.Count), 0);
    }

    // A half of a round tile lit (RoundTileView.HalfRect), on a holder node of its own (one EdgeHighlight per node).
    private static void ShowRoundTileHalf(Node2D tile, int half, bool on, HighlightArt.Colour colour)
    {
        var holder = tile.GetNodeOrNull<Node2D>($"Half{half}");
        if (holder is null)
        {
            if (!on || tile is not RoundTileView view || view.HalfRect(half) is null)
                return;
            holder = new Node2D { Name = $"Half{half}" };
            tile.AddChild(holder, false, InternalMode.Back);
        }
        if (tile is RoundTileView shown && shown.HalfRect(half) is Rect2 rect)
            EdgeHighlight.SetOnArea(holder, on, rect, rect.Size.X * 0.08f, 0, colour);
    }

    // Pass tiles: in their bank slot (with the coins on them), or on their holder's panel.
    // Those in the bank have a white edge highlight while a tile must be picked from it: during setup, and after passing.
    private void RenderPassTiles(GameState state, bool animate)
    {
        bool choosing = state.Phase == Phase.PickingPassTiles
            || state.Phase == Phase.Playing && state.ActivePlayer is Faction active
                && (state[active].PassTileOwed || state[active].ExtraPassTileOwed);
        // Fan factions: the Archivists picking two can't take back the tiles they held when passing (not lit).
        ValueList<int> givenBack = state.ActivePlayer is Faction picker && state.IsInGame(picker)
            ? state[picker].HeldPassTiles ?? ValueList<int>.Empty : ValueList<int>.Empty;
        var placements = new Dictionary<int, Placement>();
        for (var slot = 0; slot < state.PassTileBank.Count; slot++)
            if (state.PassTileBank[slot] is int tile)
                placements[tile] = new Placement(_passTileBank.GetNode<Node2D>($"Slot{slot}"), Vector2.Zero, 0, null);
        // The Archivists holding two (or picking their second): both shrunk (ArchivistsPassTileScale), the first in the
        // top left of the tile's place, the second in the bottom right.
        var small = new HashSet<int>();
        Vector2 corner = Art.PassTileTexture().GetSize() * (1 - ArchivistsPassTileScale) / 2;
        for (var p = 0; p < state.Players.Count; p++)
        {
            PlayerState holder = state.Players[p];
            bool two = holder.Faction == Faction.Archivists && (holder.ExtraPassTile is not null || holder.ExtraPassTileOwed);
            if (holder.PassTile is int held)
                placements[held] = new Placement(_panels[p].PassTileAnchor, two ? -corner : Vector2.Zero, 0, _panels[p]);
            if (holder.ExtraPassTile is int extra)
                placements[extra] = new Placement(_panels[p].PassTileAnchor, corner, 0, _panels[p]);
            if (two)
                small.UnionWith(new[] { holder.PassTile, holder.ExtraPassTile }.OfType<int>());
        }

        _bankPassTiles.Clear();
        _bankPassTiles.UnionWith(placements.Where(pair => pair.Value.Panel is null).Select(pair => pair.Key));

        foreach (int gone in _passTileSprites.Keys.Except(placements.Keys).ToList())
        {
            RemoveSprite(_passTileSprites[gone]);
            _passTileSprites.Remove(gone);
        }

        foreach (var (tile, placement) in placements)
        {
            // On a panel, as every other piece there: it moves and changes size with the panel's collapse and opening.
            Vector2 scale = ScaleFor(placement) * (small.Contains(tile) ? ArchivistsPassTileScale : 1f);
            Vector2 target = TargetFor(placement);
            if (!_passTileSprites.TryGetValue(tile, out Node2D sprite))
            {
                sprite = PassTileView.Create(tile);
                sprite.Position = target;
                sprite.Scale = scale;
                _pieceLayer.AddChild(sprite);
                _passTileSprites[tile] = sprite;
            }

            if (sprite is PassTileView view)
            {
                view.ShowCoins(state.PassTileCoins[tile]);
                view.ShowIncomeDrawn(placement.Panel?.ShownFaction is Faction holding && IncomeDrawn(state, holding));
                // Its bonus action, crossed out once its holder has used it this round.
                Faction? holder = placement.Panel?.ShownFaction;
                bool used = holder is Faction owner && PassTiles.All[tile].BonusAction is BonusAction action
                    && state[owner].BonusActionsUsed.Contains(new BonusActionId(BonusSource.PassTile, action));
                bool selected = _bonus is { } waiting && waiting.Faction == holder
                    && waiting.Bonus.Source == BonusSource.PassTile;
                view.ShowAction(used, selected, PlayerPanel.SelectedTint);
            }

            sprite.ZIndex = RaiseFor(placement.Panel);
            EdgeHighlight.Set(sprite, choosing && placement.Panel is null && !givenBack.Contains(tile), HighlightArt.Colour.White, glints: 0);
            bool onPassedPanel = placement.Panel is not null && ShownPassed(state, placement.Panel.ShownFaction!.Value);
            bool shown = placement.Panel?.IsShownInLayout(placement.Anchor) ?? true;
            TintAndFade(sprite, onPassedPanel ? PlayerPanel.PassedTint : Colors.White, shown ? 1f : 0f, animate);
            Slide(sprite, target, animate);
            Resize(sprite, scale, animate);
        }
    }
}
