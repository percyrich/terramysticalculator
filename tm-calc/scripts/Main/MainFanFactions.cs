using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the fan factions' clicks and pictures (GameOptions.FanFactions): books and stronghold octagons. A file of the same class
// (partial), only to keep Main.cs a manageable size.
//   Books (PlayerPanel's book, Art.Book): the Djinni's lamp (click it, then two tracks: the second legal one swaps
//     them), the Goblins' treasure (click it, then a lit row of their building board), Chash Dallah's income track
//     (click: the next step), the Engineers', Architects' and Atlanteans' bridge (BuildBridge).
//   The Treasurers' Safe: what they may put in hovers above it; a click on a resource there puts one in, on one in the
//     Safe takes it out again (only what is going in now), All puts everything in (each a ChooseForSafe move: logged
//     and undone like any other, merged with the tick's StoreInTreasury into one record); their tick settles it. With their
//     stronghold, conversions may come first: what they give joins what hovers (Core: TreasurersConverting). Their panel
//     opens while they choose; everything else waits (Core).
//   The Djinni's starting steps: a click on a lit track.
//   Stronghold octagons: Time Travellers (at once: as many tokens as bowl I has, up to 4), Prospectors and Enlightened
//     (at once); Architects (a lit bridge of theirs clicked or dragged, then a lit spot: click-click slides it);
//     Conspirators (a lit favour of theirs, then a lit one in the bank).
//   Wisps' free Lake dwelling: the empty Lake hexes lit; a click (or the dwelling dragged) places it; the tick gives
//     it up.
//   Chash Dallah's stronghold: a power action they could pay either way shows two chips over it (power, coins).
public partial class Main
{
    private bool _djinniSwapping;      // the lamp clicked: two tracks to come
    private Track? _djinniFirst;
    private bool _goblinsChoosing;     // the treasure clicked: a row of the building board to come
    private Bridge? _bridgeMoving;     // the Architects' stronghold: the bridge chosen
    private bool _bridgeDragging;
    private Vector2 _bridgePress;
    private readonly List<((int HexA, int HexB) Edge, Sprite2D Sprite)> _moveSpots = new();
    private (Vector2 Position, float Rotation, Bridge To)? _bridgeSlide; // where the moved bridge's picture was, and where to
    private int? _swapReturn;          // the Conspirators' stronghold: the favour going back
    private int? _chashAction;         // Chash Dallah: the power action whose two chips show
    private PowerOfferChip _chashPowerChip, _chashCoinChip;
    private readonly HashSet<int> _fanHexesLit = new();
    private readonly HashSet<Node2D> _fanNodesLit = new();

    // Whatever is being chosen goes (EndBonus: a move made, another octagon clicked).
    private void EndFanChoices()
    {
        _djinniSwapping = false;
        _djinniFirst = null;
        _goblinsChoosing = false;
        _bridgeMoving = null;
        _bridgeDragging = false;
        _swapReturn = null;
        _chashAction = null;
        _riverFirst = null;
        EndFlow();
        _returnChoice = null;
    }

    // ---------- Clicks ----------

    // Early in OnPress: the Safe, the Djinni's tracks, the Goblins' rows, the Wisps' Lake, Chash Dallah's chips.
    private bool OnPressFanFactions(Vector2 mouse)
    {
        GameState state = _history.Current;
        if (!state.Options.FanFactions)
            return false;
        if (OnPressFanFactionsOnMap(mouse)) // the pyro, the Atlanteans' key, the Snow Shamans' masses (MainFanFactionsOnMap.cs)
            return true;

        if (OnPressReturnChoice(mouse)) // the Changelings' returning dwelling (MainFanFactionsEmberGeologistsChangelings.cs)
            return true;

        // The Treasurers' Safe offer (their panel is open for it).
        if (state.TreasuryOffer is Income offer && PanelFor(Faction.Treasurers) is PlayerPanel safe && safe.Visible)
        {
            Income chosen = SafeChosen(state);
            Income left = Minus(offer, chosen);
            // Each choice is a move (ChooseForSafe), merged with the others and the tick into one record.
            void Choose(Income pick) => Submit(new ChooseForSafe(Faction.Treasurers, pick.Coins, pick.Workers, pick.Priests));
            if (safe.IsOverSafeAll(mouse))
            {
                if (chosen != offer)
                    Choose(offer);
                return Handled();
            }
            if (safe.SafeOfferKindAt(mouse) is ResourceKind into && Count(left, into) > 0)
            {
                Choose(chosen + One(into));
                return Handled();
            }
            if (safe.BookKindAt(mouse) is ResourceKind outOf && Count(chosen, outOf) > 0)
            {
                Choose(Minus(chosen, One(outOf)));
                return Handled();
            }
        }

        // Chash Dallah's two chips over a power action.
        if (_chashAction is int action && state.ActivePlayer is Faction chash)
        {
            if (_chashPowerChip?.IsOver(mouse) == true || _chashCoinChip?.IsOver(mouse) == true)
            {
                bool coins = _chashCoinChip?.IsOver(mouse) == true;
                _chashAction = null;
                Submit(new TakePowerAction(chash, action, coins));
                return Handled();
            }
            _chashAction = null;
            RenderFanFactions(state);
        }

        // The Djinni's starting steps (after the starting buildings).
        if (Rules.IsStartingStepsTurn(state) && state[state.ActivePlayer!.Value] is PlayerState djinni && TrackAt(mouse) is Track start)
        {
            Submit(new TakeStartingSteps(djinni.Faction, start));
            return Handled();
        }

        if (state.ActivePlayer is not Faction active)
            return false;

        // The Djinni's lamp: two tracks.
        if (_djinniSwapping)
        {
            if (TrackAt(mouse) is Track track)
            {
                if (_djinniFirst is Track first && first != track)
                {
                    if (Rules.Apply(state, new SwapTracks(active, first, track)) is { Accepted: true })
                        Submit(new SwapTracks(active, first, track));
                    else
                        ShowIssues(Rules.Apply(state, new SwapTracks(active, first, track)).Issues);
                }
                else if (_djinniFirst == track)
                    _djinniFirst = null; // unchosen
                else if (SwapPartners(state, active, track).Count > 0)
                {
                    _djinniFirst = track;
                    ShowPrompt("Click the track to swap it with (Esc to cancel).");
                }
                Render(animate: true);
                return Handled();
            }
            if (PanelFor(active)?.IsOverBookAction(mouse) != true)
            {
                _djinniSwapping = false;
                _djinniFirst = null;
                Render(animate: true); // and the click carries on
            }
        }

        // The Goblins' treasure: a row of their building board.
        if (_goblinsChoosing)
        {
            if (PanelFor(active)?.Board is BuildingBoard board && board.RowAt(mouse) is PieceKind row)
            {
                _goblinsChoosing = false;
                Submit(new SpendTreasure(active, row)); // refused with a reason (none of them on the map)
                return Handled();
            }
            if (PanelFor(active)?.IsOverBookAction(mouse) != true)
            {
                _goblinsChoosing = false;
                Render(animate: true);
            }
        }

        // The Wisps' free Lake dwelling: a lit hex.
        if (state[active].LakeDwellingOwed && _map.HexAt(mouse) is int lake && _fanHexesLit.Contains(lake)
            && NextDwelling(state, active) is Piece dwelling)
        {
            Submit(new PlaceFreeDwelling(dwelling, lake));
            return Handled();
        }

        // Chash Dallah with their stronghold: a power action paid in power or in coins.
        if (active == Faction.ChashDallah && state[active].StrongholdBuilt && PowerActionUnder(mouse) is int taken)
        {
            bool power = Rules.Apply(state, new TakePowerAction(active, taken)).Accepted;
            bool coins = Rules.Apply(state, new TakePowerAction(active, taken, PayWithCoins: true)).Accepted;
            if (power && coins)
            {
                _chashAction = taken;
                RenderFanFactions(state);
            }
            else
                Submit(new TakePowerAction(active, taken, PayWithCoins: coins && !power));
            return Handled();
        }
        return false;
    }

    private bool Handled()
    {
        GetViewport().SetInputAsHandled();
        return true;
    }

    private static Income Minus(Income a, Income b) =>
        new(a.Coins - b.Coins, a.Workers - b.Workers, a.Priests - b.Priests, a.Power - b.Power);

    private static int Count(Income income, ResourceKind kind) => kind switch
    {
        ResourceKind.Coin => income.Coins,
        ResourceKind.Worker => income.Workers,
        ResourceKind.Priest => income.Priests,
        _ => 0,
    };

    private static Income One(ResourceKind kind) => kind switch
    {
        ResourceKind.Coin => new Income(Coins: 1, Workers: 0, Priests: 0, Power: 0),
        ResourceKind.Worker => new Income(Coins: 0, Workers: 1, Priests: 0, Power: 0),
        ResourceKind.Priest => new Income(Coins: 0, Workers: 0, Priests: 1, Power: 0),
        _ => default,
    };

    // What is going into the Safe now (GameState.TreasuryChosen): kept while the offer changes (a conversion's gain
    // joining it, the Treasurers with their stronghold), as far as the new offer goes; nothing once it is settled.
    private static Income SafeChosen(GameState state) => state.TreasuryChosen ?? default; // (Core keeps it within the offer)

    // The tracks a first track could swap with (SwapTracks accepted).
    private static List<Track> SwapPartners(GameState state, Faction faction, Track first) =>
        AllTracks.Where(other => other != first && Rules.Apply(state, new SwapTracks(faction, first, other)).Accepted).ToList();

    // A click on the book (MainFactions' OnPressFactionAbilities).
    private void OnBookClicked(Faction active)
    {
        GameState state = _history.Current;
        switch (active)
        {
            case Faction.Djinni:
                bool swapping = !_djinniSwapping;
                EndBonus();
                if (swapping && !AllTracks.Any(track => SwapPartners(state, active, track).Count > 0))
                {
                    ShowIssues(Rules.Apply(state, new SwapTracks(active, Track.Fire, Track.Water)).Issues);
                    return;
                }
                _djinniSwapping = swapping;
                ShowPrompt(swapping ? "Click two tracks to swap the Djinni's markers on them (Esc to cancel)." : "");
                Render(animate: true);
                return;
            case Faction.Goblins:
                bool choosing = !_goblinsChoosing;
                EndBonus();
                if (choosing && TreasureRows(state, active).Count == 0)
                {
                    ShowIssues(Rules.Apply(state, new SpendTreasure(active, PieceKind.Dwelling)).Issues);
                    return;
                }
                _goblinsChoosing = choosing;
                ShowPrompt(choosing ? "Click a row of the Goblins' board to spend a treasure on it (Esc to cancel)." : "");
                Render(animate: true);
                return;
            case Faction.ChashDallah:
                Submit(new AdvanceIncomeTrack(active));
                return;
            case Faction.Treasurers:
                return; // the Safe: only what goes into it is clicked (OnPressFanFactions)
            default:
                Submit(new BuildBridge(active)); // the Engineers, Architects and Atlanteans; refused with a reason
                return;
        }
    }

    // The building board's rows a treasure could be spent on now (the stronghold's and sanctuary's as one).
    private static List<PieceKind> TreasureRows(GameState state, Faction faction) =>
        new[] { PieceKind.Stronghold, PieceKind.TradingPost, PieceKind.Temple, PieceKind.Dwelling }
            .Where(kind => Rules.Apply(state, new SpendTreasure(faction, kind)).Accepted).ToList();

    // The tick on a panel (its player button): the Treasurers' Safe settled; the Wisps' free dwelling given up.
    private bool OnFanFactionsTick(Faction faction)
    {
        GameState state = _history.Current;
        if (OnFanFactionsTickOnMap(faction))
            return true;
        if (faction == Faction.Treasurers && state.TreasuryOffer is not null)
        {
            Income chosen = SafeChosen(state);
            Submit(new StoreInTreasury(faction, chosen.Coins, chosen.Workers, chosen.Priests));
            return true;
        }
        if (state.IsInGame(faction) && (state[faction].LakeDwellingOwed || state[faction].IceMassesOwed is { Count: > 0 }))
        {
            Submit(new SkipFreeDwelling(faction)); // Done: the rest declined
            return true;
        }
        return false;
    }

    // What the player button shows for these (null: as usual).
    private static PlayerButton.Mode? FanPlayerButtonMode(GameState state, Faction faction)
    {
        if (state.TreasuryOffer is not null)
            return faction == Faction.Treasurers ? PlayerButton.Mode.Confirm
                : state.ActivePlayer == faction ? PlayerButton.Mode.Waiting : PlayerButton.Mode.Hidden;
        if (state.Phase == Phase.Playing && state.ActivePlayer == faction
            && (state[faction].LakeDwellingOwed || state[faction].IceMassesOwed is { Count: > 0 }))
            return PlayerButton.Mode.Confirm;
        return null;
    }

    // ---------- Stronghold octagons ----------

    // The fan factions' stronghold actions (OnBonusActionClicked); false for the others.
    private bool OnFanBonusActionClicked(Faction faction, BonusActionId bonus)
    {
        GameState state = _history.Current;
        switch (bonus.Action)
        {
            case BonusAction.TokensToBowlThree:
                Submit(new BonusTokensToBowlThree(faction, bonus.Source,
                    System.Math.Min(Config.TimeTravellersTokensToBowlThree, System.Math.Max(1, state[faction].Power.Bowl1))));
                return true;
            case BonusAction.CoinsForTradingPosts:
                Submit(new BonusCoinsForTradingPosts(faction, bonus.Source));
                return true;
            case BonusAction.FourPower:
                Submit(new BonusFourPower(faction, bonus.Source));
                return true;
            case BonusAction.MoveBridge or BonusAction.SwapFavour:
                if (CheckBonusIssues(state, faction, bonus) is { Count: > 0 } issues)
                {
                    ShowIssues(issues);
                    return true;
                }
                _bonus = (faction, bonus);
                _bonusOptions = new PieceOptions(ValueList<Destination>.Empty, null);
                ShowPrompt((bonus.Action == BonusAction.MoveBridge
                    ? "Click or drag a highlighted bridge, then a highlighted spot."
                    : "Click a highlighted favour to hand back, then one to take.") + " (Esc or click the octagon again to cancel.)");
                Render(animate: true);
                return true;
            case BonusAction.MoveRock:
                return StartRockBonus(faction, bonus); // the wheel (MainFanFactionsEmberGeologistsChangelings.cs)
            default:
                return OnFanBonusActionClickedOnMap(faction, bonus); // the Wyrm, the Selkies (MainFanFactionsOnMap.cs)
        }
    }

    // Why the octagon can't be used now (used this round, not their turn...), from a trial of its move.
    private static IReadOnlyList<Issue> CheckBonusIssues(GameState state, Faction faction, BonusActionId bonus)
    {
        Move trial = bonus.Action == BonusAction.MoveBridge
            ? state.Bridges.Where(b => b.Owner == faction).Select(b => (Move)new BonusMoveBridge(faction, bonus.Source, b.HexA, b.HexB, b.HexA, b.HexB)).FirstOrDefault()
            : state[faction].FavourTiles.Select(t => (Move)new BonusSwapFavour(faction, bonus.Source, t.Id, t.Id)).FirstOrDefault();
        if (trial is null)
            return new[] { new Issue(Severity.Error, bonus.Action == BonusAction.MoveBridge ? "They have no bridge to move." : "They hold no favour to hand back.") };
        IReadOnlyList<Issue> issues = Rules.Apply(state, trial).Issues;
        // the trial's own refusal (same spot, same favour) means the octagon itself is usable
        return issues.Any(issue => issue.Message is "Move the bridge somewhere else." or "Take a different favour.") ? new List<Issue>() : issues;
    }

    // A click while one of these octagons waits (OnBonusTargetClicked); false for the others.
    private bool OnFanBonusTargetClicked(Vector2 mouse)
    {
        if (_bonus is not { } waiting)
            return false;
        GameState state = _history.Current;
        var (faction, bonus) = waiting;
        if (bonus.Action == BonusAction.MoveBridge)
        {
            if (_bridgeMoving is Bridge moving && MoveSpotAt(mouse) is { } spot)
            {
                SlideBridgeFrom(moving, faction, spot);
                Submit(new BonusMoveBridge(faction, bonus.Source, moving.HexA, moving.HexB, spot.HexA, spot.HexB));
                return Handled();
            }
            if (OwnBridgeNear(state, faction, mouse) is Bridge bridge && MoveTargets(state, faction, bonus, bridge).Count > 0)
            {
                _bridgeMoving = bridge;
                _bridgeDragging = true;
                _bridgePress = mouse;
                if (_bridgeSprites.TryGetValue(bridge, out Sprite2D sprite))
                    sprite.ZIndex = 100;
                RenderFanFactions(state);
                return Handled();
            }
            return _map.HexAt(mouse) is not null && Handled(); // the board waits for a bridge or a spot
        }
        if (bonus.Action == BonusAction.SwapFavour)
        {
            if (_swapReturn is int giving && FavourUnder(mouse) is int taking)
            {
                if (Rules.Apply(state, new BonusSwapFavour(faction, bonus.Source, giving, taking)) is { Accepted: true })
                    Submit(new BonusSwapFavour(faction, bonus.Source, giving, taking));
                else
                    ShowIssues(Rules.Apply(state, new BonusSwapFavour(faction, bonus.Source, giving, taking)).Issues);
                return Handled();
            }
            if (OwnFavourUnder(state, faction, mouse) is int own)
            {
                _swapReturn = _swapReturn == own ? null : own;
                RenderFanFactions(state);
                return Handled();
            }
            return FavourUnder(mouse) is not null && Handled();
        }
        return false;
    }

    // Where the Architects' bridge could go (each a BonusMoveBridge accepted).
    private static List<(int HexA, int HexB)> MoveTargets(GameState state, Faction faction, BonusActionId bonus, Bridge bridge)
    {
        GameState lifted = state with { Bridges = state.Bridges.RemoveAt(state.Bridges.IndexOf(bridge)) };
        return Rules.BridgeSpots(lifted, faction)
            .Where(edge => Rules.Apply(state, new BonusMoveBridge(faction, bonus.Source, bridge.HexA, bridge.HexB, edge.HexA, edge.HexB)).Accepted)
            .ToList();
    }

    private (int HexA, int HexB)? MoveSpotAt(Vector2 point) =>
        _moveSpots.Where(spot => spot.Sprite.GlobalPosition.DistanceTo(point) <= SnapRadius)
            .OrderBy(spot => spot.Sprite.GlobalPosition.DistanceTo(point))
            .Select(spot => ((int HexA, int HexB)?)spot.Edge)
            .FirstOrDefault();

    // The moved bridge's picture slides from where this one's is (RenderFanFactions).
    private void SlideBridgeFrom(Bridge bridge, Faction owner, (int HexA, int HexB) to)
    {
        if (_bridgeSprites.TryGetValue(bridge, out Sprite2D sprite))
            _bridgeSlide = (sprite.GlobalPosition, sprite.GlobalRotation,
                new Bridge(owner, System.Math.Min(to.HexA, to.HexB), System.Math.Max(to.HexA, to.HexB)));
    }

    // Mouse motion (MoveKeyOrBridgeDrag): the Architects' bridge follows the mouse while dragged.
    private bool MoveFanDrag(Vector2 mouse)
    {
        if (!_bridgeDragging || _bridgeMoving is not Bridge moving || !_bridgeSprites.TryGetValue(moving, out Sprite2D sprite))
            return false;
        if (mouse.DistanceTo(_bridgePress) > ClickTolerance)
            sprite.GlobalPosition = mouse;
        return true;
    }

    // The release (EndKeyOrBridgeDrag): on a lit spot the bridge moves there; where it was pressed, it stays chosen
    // (a click: the next click on a spot moves it); anywhere else it goes back.
    private bool EndFanDrag()
    {
        if (!_bridgeDragging || _bridgeMoving is not Bridge moving || _bonus is not { } waiting)
            return false;
        _bridgeDragging = false;
        Vector2 mouse = GetGlobalMousePosition();
        if (mouse.DistanceTo(_bridgePress) <= ClickTolerance)
        {
            Render(animate: true); // back in place, chosen
            return true;
        }
        if (MoveSpotAt(mouse) is { } spot)
        {
            SlideBridgeFrom(moving, waiting.Faction, spot);
            Submit(new BonusMoveBridge(waiting.Faction, waiting.Bonus.Source, moving.HexA, moving.HexB, spot.HexA, spot.HexB));
        }
        else
        {
            _bridgeMoving = null;
            Render(animate: true);
        }
        return true;
    }

    // ---------- Drawing ----------

    // The tracks lit for these (RenderFactionAbilities adds them): the Djinni's starting steps; their lamp (every
    // track that can swap, then the first one's partners).
    private IEnumerable<Track> FanTracksLit(GameState state)
    {
        if (!state.Options.FanFactions)
            return Enumerable.Empty<Track>();
        if (Rules.IsStartingStepsTurn(state) && state[state.ActivePlayer!.Value] is PlayerState djinni)
            return AllTracks.Where(track => Rules.Apply(state, new TakeStartingSteps(djinni.Faction, track)).Accepted).ToList();
        if (_djinniSwapping && state.ActivePlayer is Faction active)
            return _djinniFirst is Track first ? SwapPartners(state, active, first)
                : AllTracks.Where(track => SwapPartners(state, active, track).Count > 0).ToList();
        return Enumerable.Empty<Track>();
    }

    // The book (with what is going into the Safe), and the Safe's offer above it (PlayerPanel).
    private BookView FanBook(GameState state, PlayerState player)
    {
        BookView book = Art.Book(player);
        if (book is null)
            return null;
        if (player.Faction == Faction.Treasurers && state.TreasuryOffer is not null)
            book = book with { Content = book.Content + Amounts.From(SafeChosen(state)) };
        return book;
    }

    private Amounts SafeLeft(GameState state, PlayerState player) =>
        player.Faction == Faction.Treasurers && state.TreasuryOffer is Income offer
            ? Amounts.From(Minus(offer, SafeChosen(state)))
            : null;

    // After RenderFactionAbilities: the Goblins' rows, the Wisps' Lake, the Architects' bridges and spots, the
    // Conspirators' favours, Chash Dallah's chips, the moved bridge's slide.
    private void RenderFanFactions(GameState state)
    {
        Faction? active = state.Phase == Phase.Playing ? state.ActivePlayer : null;

        RenderReturnChoice(state); // MainFanFactionsEmberGeologistsChangelings.cs

        // The Goblins' rows.
        foreach (PlayerPanel panel in _panels)
            if (panel.Board is BuildingBoard board)
                board.HighlightRows(_goblinsChoosing && active is Faction goblins && panel.ShownFaction == goblins && IsInteractive(panel)
                    ? TreasureRows(state, goblins) : new List<PieceKind>());

        // The Wisps' free dwelling: every Lake hex it could go on.
        var hexes = new HashSet<int>(FanHexesLit(state)); // the Wyrm's rivers, the Snow Shamans' masses
        if (active is Faction wisps && state.IsInGame(wisps) && state[wisps].LakeDwellingOwed && NextDwelling(state, wisps) is Piece dwelling)
            hexes.UnionWith(Enumerable.Range(0, state.Layout.HexCount)
                .Where(hex => state.HexTerrain[hex] == Terrain.Lake && Rules.Apply(state, new PlaceFreeDwelling(dwelling, hex)).Accepted));
        foreach (int hex in _fanHexesLit.Except(hexes).ToList())
            _map.ShowHexHighlight(hex, false);
        foreach (int hex in hexes)
            _map.ShowHexHighlight(hex, true, glints: 1);
        _fanHexesLit.Clear();
        _fanHexesLit.UnionWith(hexes);

        // Bridges and favours lit, and the spots a chosen bridge could go.
        var nodes = new HashSet<Node2D>();
        foreach (var (_, spot) in _moveSpots)
            spot.QueueFree();
        _moveSpots.Clear();
        if (_bonus is { } waiting && waiting.Bonus.Action == BonusAction.MoveBridge)
        {
            if (_bridgeMoving is Bridge moving)
                foreach ((int HexA, int HexB) edge in MoveTargets(state, waiting.Faction, waiting.Bonus, moving))
                {
                    var spot = new Sprite2D { Texture = Art.BridgeSpotTexture(), ZIndex = MapTopDepth(state) + 1 };
                    _pieceLayer.AddChild(spot);
                    PlaceAcross(spot, edge.HexA, edge.HexB);
                    _moveSpots.Add((edge, spot));
                }
            else
                foreach (Bridge bridge in state.Bridges.Where(b => b.Owner == waiting.Faction))
                    if (_bridgeSprites.TryGetValue(bridge, out Sprite2D sprite) && MoveTargets(state, waiting.Faction, waiting.Bonus, bridge).Count > 0)
                        nodes.Add(sprite);
        }
        if (_bonus is { } swapping && swapping.Bonus.Action == BonusAction.SwapFavour)
        {
            Faction faction = swapping.Faction;
            if (_swapReturn is int giving)
            {
                foreach (var group in _favourSprites.Where(pair => _bankFavours.Contains(pair.Key)).GroupBy(pair => pair.Key.Id))
                    if (Rules.Apply(state, new BonusSwapFavour(faction, swapping.Bonus.Source, giving, group.Key)).Accepted)
                        nodes.Add(group.OrderByDescending(pair => pair.Value.ZIndex).First().Value);
                if (state[faction].FavourTiles.FirstOrDefault(t => t.Id == giving) is var held && _favourSprites.TryGetValue(held, out Node2D chosen))
                    nodes.Add(chosen);
            }
            else
                foreach (FavourTile tile in state[faction].FavourTiles)
                    if (_favourSprites.TryGetValue(tile, out Node2D own))
                        nodes.Add(own);
        }
        foreach (Node2D gone in _fanNodesLit.Except(nodes).ToList())
            if (IsInstanceValid(gone))
                EdgeHighlight.Set(gone, false, HighlightArt.Colour.Yellow);
        foreach (Node2D node in nodes)
            EdgeHighlight.Set(node, true, _swapReturn is int g && node is not null && IsReturning(state, node, g)
                ? HighlightArt.Colour.White : HighlightArt.Colour.Yellow, glints: 1);
        _fanNodesLit.Clear();
        _fanNodesLit.UnionWith(nodes);

        // The moved bridge slides to its new spot.
        if (_bridgeSlide is { } from)
        {
            _bridgeSlide = null;
            if (_bridgeSprites.TryGetValue(from.To, out Sprite2D sprite) && BridgeSlideSeconds > 0)
            {
                Vector2 end = sprite.Position;
                float turn = sprite.Rotation;
                sprite.GlobalPosition = from.Position;
                sprite.GlobalRotation = from.Rotation;
                Tween tween = CreateTween().SetParallel();
                tween.TweenProperty(sprite, "position", end, BridgeSlideSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                tween.TweenProperty(sprite, "rotation", Mathf.Wrap(turn - sprite.Rotation, -Mathf.Pi, Mathf.Pi) + sprite.Rotation, BridgeSlideSeconds)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }

        // Chash Dallah's chips: power and coins, over the power action.
        if (_chashAction is int action && active == Faction.ChashDallah)
        {
            Node2D node = PowerActionNode(action);
            int cost = Rules.PowerActionCost(state, Faction.ChashDallah, action);
            Color colour = Art.PieceColour(state[Faction.ChashDallah].Colour);
            _chashPowerChip ??= AddChip("ChashPowerChip");
            _chashCoinChip ??= AddChip("ChashCoinChip");
            foreach (var (chip, side, kind) in new[] { (_chashPowerChip, -1, ResourceKind.PowerSpend), (_chashCoinChip, 1, ResourceKind.Coin) })
            {
                chip.GlobalPosition = node.GlobalPosition + ChashDallahChipOffset + new Vector2(side * ChashDallahChipSpacing / 2, 0);
                chip.ShowContent(Amounts.Of(kind, cost), colour, ChashDallahChipRadius, Mathf.RoundToInt(ChashDallahChipRadius * 1.2f),
                    Mathf.RoundToInt(ChashDallahChipRadius * 0.9f));
            }
        }
        else
        {
            _chashPowerChip?.HideOffer();
            _chashCoinChip?.HideOffer();
        }
    }

    private bool IsReturning(GameState state, Node2D node, int favour) =>
        _favourSprites.Any(pair => pair.Value == node && pair.Key.Id == favour && !_bankFavours.Contains(pair.Key));

    private PowerOfferChip AddChip(string name)
    {
        var chip = new PowerOfferChip { Name = name, Visible = false };
        _board.AddChild(chip);
        chip.ZIndex = 60;
        return chip;
    }
}
