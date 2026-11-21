using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: the game screen, and the chosen final scoring tile on the board. A file of the same class
// (partial), only to keep Main.cs a manageable size.
//   The game: the Terra Mystica symbol on a blue square (a round tile's right-hand panel, as on round 6's tile),
//     GameSymbolScale times the others' size, centred on GameChoiceCentre (the screen's middle).
//     Selected to start with (white edge highlight, no glints); clicking it deselects
//     it (and again selects it); the step button's tick chooses the game with the ticked options (ChooseGame).
//   The options under it (GameOptionsOffset from the square's bottom middle): one framed stone panel in the top panel's
//     style (TopPanelArt.DrawerBacking, GameOptionsScale as the top panel's 0.6), a stone tick box a row
//     (TopPanelCheck): Variable turn order, Mini-expansions, Landscapes, Fire & Ice, Fan factions, Set up a game in
//     progress, Calculator (Strict beside it).
//   Under Fire & Ice: its four final scoring tiles, each its icon on the same panel narrowed to FinalTileWidthRatio of
//     its height, side by side (GameOptionIndent in). Dimmed and not clickable while Fire & Ice is unticked; otherwise
//     one or none is selected: clicking one selects it instead of any other, clicking the selected one deselects it.
//     Unticking Fire & Ice drops the choice. Strict likewise is dimmed (and unticked) while Calculator is unticked.
//   On the board: the chosen tile's icon on its rectangle at %FinalScoring (a Marker2D above the round tiles; at scale
//     1 the rectangle is round 6's right-hand panel's height there, FinalTileWidthRatio of it across).
public partial class Main
{
    private Node2D _gameChoice; // the Terra Mystica square
    private readonly Dictionary<FinalScoringTile, Node2D> _finalTileChoices = new();
    private bool _gameChosen = true; // Terra Mystica selected to start with
    private FinalScoringTile? _finalTileChosen;
    private Node2D _finalTileShown; // the chosen tile on the board

    private static readonly FinalScoringTile[] FinalTiles =
        { FinalScoringTile.StrongholdSanctuary, FinalScoringTile.Distance, FinalScoringTile.Clusters, FinalScoringTile.Edges };

    public static Icon FinalTileIcon(FinalScoringTile tile) => tile switch
    {
        FinalScoringTile.StrongholdSanctuary => Icon.ScoringStrongholdSanctuary,
        FinalScoringTile.Distance => Icon.ScoringDistance,
        FinalScoringTile.Clusters => Icon.ScoringClusters,
        _ => Icon.ScoringEdge,
    };

    // Round tile 6's right-hand panel's side on the board, in screen pixels (the round tiles' Background is drawn at
    // RoundTileScale of the tile's natural size).
    private const float RoundTileScale = 0.64f; // round_tile.tscn's Background TileScale
    private float ChoiceSide => IconArt.RoundTilePanel(1).Size.X * RoundTileScale * _roundTileRow.GlobalScale.X * GameChoiceScale;

    private float GameSymbolSide => ChoiceSide * GameSymbolScale;
    private Vector2 FinalTileSize => new(ChoiceSide * FinalTileWidthRatio, ChoiceSide);

    // A panel with an icon on it, `side` pixels tall and widthShare of that across, centred on its node.
    private Node2D ChoiceSquare(string name, Icon icon, float side, float widthShare = 1f)
    {
        var square = new Node2D { Name = name };
        square.AddChild(new Sprite2D { Name = "Panel", Texture = IconArt.RoundTilePanelTexture(side, widthShare) });
        square.AddChild(new Sprite2D { Name = "Icon", Texture = IconArt.Texture(icon, Mathf.Max(1, Mathf.RoundToInt(side * GameChoiceIconScale))) });
        return square;
    }

    private static bool IsOverSquare(Node2D square, Vector2 size, Vector2 globalPoint) =>
        square.IsVisibleInTree() && new Rect2(-size / 2, size).HasPoint(square.ToLocal(globalPoint));

    private void ShowSelected(Node2D square, Vector2 size, bool on) =>
        EdgeHighlight.SetOnArea(square, on, new Rect2(-size / 2, size),
            IconArt.RoundTilePanelRadius * size.Y / IconArt.RoundTilePanel(1).Size.Y, 0, HighlightArt.Colour.White, GameChoiceHighlightScale);

    // Made once (from ReadyGameOptions): the game's square in the middle of the screen, and the final scoring tiles,
    // the first one's centre at firstTile (screen pixels, under Fire & Ice in the options).
    private void ReadyGameChoices(Vector2 firstTile)
    {
        float side = ChoiceSide, big = GameSymbolSide;
        Vector2 tile = FinalTileSize;
        _gameChoice = ChoiceSquare("TerraMysticaChoice", Icon.TerraMysticaSymbol, big);
        _gameChoice.Position = GameChoiceCentre;
        _gameSelect.AddChild(_gameChoice);

        for (var i = 0; i < FinalTiles.Length; i++)
        {
            Node2D square = ChoiceSquare($"{FinalTiles[i]}Choice", FinalTileIcon(FinalTiles[i]), side, FinalTileWidthRatio);
            square.Position = firstTile + new Vector2(i * (tile.X + FinalTileGap), 0);
            _gameSelect.AddChild(square); // (after the options' panel: drawn over it)
            _finalTileChoices[FinalTiles[i]] = square;
        }
        _fireAndIce.Toggled += _ =>
        {
            if (!_fireAndIce.ButtonPressed)
                _finalTileChosen = null;
            RenderGameSelect();
        };
    }

    // A click on the game screen: the game's square or a final scoring tile toggles; true if it took the click.
    private bool OnPressGameSelect(Vector2 mouse)
    {
        if (_gameChoice is not null && IsOverSquare(_gameChoice, Vector2.One * GameSymbolSide, mouse))
        {
            _gameChosen = !_gameChosen;
            RenderGameSelect();
            RenderControls(_history.Current); // the step button: a tick once the game is selected
            return true;
        }
        foreach (var (tile, square) in _finalTileChoices)
            if (_fireAndIce.ButtonPressed && IsOverSquare(square, FinalTileSize, mouse))
            {
                _finalTileChosen = _finalTileChosen == tile ? null : tile;
                RenderGameSelect();
                return true;
            }
        return false;
    }

    // The highlights; the tiles dimmed while Fire & Ice is unticked.
    private void RenderGameSelect()
    {
        if (_gameChoice is null)
            return;
        ShowSelected(_gameChoice, Vector2.One * GameSymbolSide, _gameChosen);
        bool open = _fireAndIce.ButtonPressed;
        foreach (var (tile, square) in _finalTileChoices)
        {
            square.Modulate = open ? Colors.White : FinalTileDisabledTint;
            ShowSelected(square, FinalTileSize, open && _finalTileChosen == tile);
        }
    }

    // The chosen final scoring tile above the round tiles (%FinalScoring), if the game has one and the marker exists.
    private void RenderFinalTile(GameState state)
    {
        if (GetNodeOrNull<Node2D>("%FinalScoring") is not Node2D marker)
            return;
        FinalScoringTile? tile = state.Phase > Phase.ChoosingGame ? state.Options.FinalScoring : null;
        string name = tile?.ToString();
        if (_finalTileShown is not null && (tile is null || _finalTileShown.Name.ToString() != name))
        {
            _finalTileShown.QueueFree();
            _finalTileShown = null;
        }
        if (tile is FinalScoringTile chosen && _finalTileShown is null)
        {
            // In the marker's pixels: round 6's panel's height in the round tiles' pixels (the marker's scale on top),
            // narrowed as on the game screen.
            float side = IconArt.RoundTilePanel(1).Size.Y * RoundTileScale;
            _finalTileShown = ChoiceSquare(name, FinalTileIcon(chosen), side, FinalTileWidthRatio);
            marker.AddChild(_finalTileShown);
        }
    }

    // ---------- The game screen's options ----------

    // One panel under the game's square (see the notes at the top), built in panel pixels and scaled by
    // GameOptionsScale. Ticked to start with: Variable turn order and Mini-expansions. Landscapes and Fan factions
    // can't both be ticked (ticking one unticks the other).
    private TopPanelCheck _variableTurnOrder, _miniExpansion, _fireAndIce, _landscapes, _fanFactions, _setUpInProgress, _calculator;
    private TopPanelCheck _calculatorStrict; // beside Calculator, dimmed while it is unticked
    private Control _gameOptions;

    private void ReadyGameOptions()
    {
        Node parent = _gameSelect;
        float k = Mathf.Max(0.01f, GameOptionsScale), pad = TopPanelArt.Frame + TopPanelArt.Margin;
        _gameOptions = new Control { Name = "GameOptions", Scale = Vector2.One * k, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(_gameOptions);
        var backing = new TextureRect
        {
            Name = "Backing", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _gameOptions.AddChild(backing);

        float y = pad, right = 0;
        TopPanelCheck Check(string name, string text, bool on, string tip = "", float x = 0, bool newRow = true)
        {
            var check = new TopPanelCheck
            {
                Name = name, Text = text, ButtonPressed = on, TooltipText = tip,
                FontSize = GameOptionFontSize, BoxSize = GameOptionBoxSize,
            };
            _gameOptions.AddChild(check);
            Vector2 size = check.GetCombinedMinimumSize();
            check.Size = new Vector2(size.X, Mathf.Max(size.Y, GameOptionRowHeight));
            check.Position = new Vector2(pad + x, y);
            right = Mathf.Max(right, check.Position.X + check.Size.X);
            if (newRow)
                y += GameOptionRowHeight;
            return check;
        }
        _variableTurnOrder = Check("VariableTurnOrderCheck", "Variable turn order", true);
        _miniExpansion = Check("MiniExpansionCheck", "Mini-expansions", true);
        _landscapes = Check("LandscapesCheck", "Landscapes", false);
        _fireAndIce = Check("FireAndIceCheck", "Fire & Ice", false);
        // Its final scoring tiles' row (the tiles themselves are made by ReadyGameChoices, over the panel).
        Vector2 tile = FinalTileSize;
        float tilesTop = y + FinalTilesGap / k;
        right = Mathf.Max(right, pad + GameOptionIndent + (FinalTiles.Length * tile.X + (FinalTiles.Length - 1) * FinalTileGap) / k);
        y = tilesTop + tile.Y / k + FinalTilesGap / k;
        _fanFactions = Check("FanFactionsCheck", "Fan factions", false);
        _landscapes.Toggled += on => { if (on) _fanFactions.ButtonPressed = false; };
        _fanFactions.Toggled += on => { if (on) _landscapes.ButtonPressed = false; };
        _setUpInProgress = Check("SetUpInProgressCheck", "Set up a game in progress", false);
        _calculator = Check("CalculatorCheck", "Calculator", false, "One faction against a neutral opponent, always in Free mode", newRow: false);
        _calculator.Toggled += _ => RenderTopPanel(); // the rules slider hides
        _calculatorStrict = Check("CalculatorStrictCheck", "Strict", false, "Debt is refused, and the +1 panel gives only power",
            x: _calculator.Size.X + CalculatorStrictGap);
        _calculatorStrict.Disabled = true;
        _calculator.Toggled += on =>
        {
            _calculatorStrict.Disabled = !on;
            if (!on)
                _calculatorStrict.ButtonPressed = false;
        };

        var size = new Vector2(right + pad, y + pad); // the last row's bottom, then the margin
        backing.Size = size;
        backing.Texture = TopPanelArt.DrawerBacking(size, IconArt.PowerPurple);
        _gameOptions.Size = size;
        // Centred under the game's square.
        Vector2 squareBottom = GameChoiceCentre + new Vector2(0, GameSymbolSide / 2);
        _gameOptions.Position = squareBottom + GameOptionsOffset - new Vector2(size.X * k / 2, 0);

        Vector2 firstTile = _gameOptions.Position + new Vector2((pad + GameOptionIndent) * k + tile.X / 2, tilesTop * k + tile.Y / 2);
        ReadyGameChoices(firstTile);
    }

    private GameOptions ChosenOptions() =>
        new(_variableTurnOrder.ButtonPressed, _miniExpansion.ButtonPressed, _fireAndIce.ButtonPressed,
            _fireAndIce.ButtonPressed ? _finalTileChosen : null, _startingMode, _setUpInProgress.ButtonPressed,
            _calculator.ButtonPressed, _landscapes.ButtonPressed, _fanFactions.ButtonPressed,
            _calculator.ButtonPressed && _calculatorStrict.ButtonPressed);
}
