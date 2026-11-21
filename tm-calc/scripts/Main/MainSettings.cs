using Core;
using Godot;

namespace TMCalc;

// Everything of Main that can be set in the Godot editor (the Inspector, with Main selected), in the order and groups
// shown there. A file of the same class (partial): the code using each setting is in the Main file its group names.
public partial class Main
{
    [ExportGroup("Board")]
    // Pixels (in the Pieces layer) between trackers sharing a step; they sit centred on the step.
    [Export] public float TrackerSpacing { get; set; } = 22f; // five fit across a track (CultArt)
    // Opacity of a building or X marker under the mouse, so what is underneath shows through.
    [Export] public float HoverOpacity { get; set; } = 0.125f;
    [Export] public int ColourWheelZIndex = 90;       // for a wheel made in code: over the pieces and raised panels
    // Between the bottom of the tracks and the top of the priest zones when they are small, in the board's pixels.
    [Export] public float PriestZoneGap { get; set; } = 10f;
    // Between one track's column of steps and the next, in the Tracks node's pixels. The tracks are lined up from
    // the first (fire), which stays where it is in the scene; the small priest zones follow, so they keep the gap.
    [Export] public float TrackGap { get; set; } = 2f;

    [ExportGroup("Background")]
    [Export] public Color BackgroundColour { get; set; } = new("4d4d4d"); // Godot's default clear colour
    [Export] public bool ShowBackgroundPicker { get; set; } = true;
    [Export] public int BackgroundLayer { get; set; } = -100;
    [Export] public int BackgroundPickerLayer { get; set; } = 100;
    [Export] public Vector2 BackgroundPickerSize { get; set; } = new(44, 28);
    [Export] public float BackgroundPickerMargin { get; set; } = 8f;          // from the screen's bottom left corner

    [ExportGroup("Top panel")]
    [Export] public float TopPanelTop { get; set; } = 4f;           // screen pixels from the top
    [Export] public float NoteSeconds { get; set; } = 5f;           // how long a note replaces the top line
    [Export] public Color LogDimColour { get; set; } = new(0, 0, 0, 0.45f);
    [Export] public Color TooltipColour { get; set; } = new(0.08f, 0.09f, 0.11f, 0.96f); // every tooltip's panel, nearly opaque
    [Export] public Color TooltipEdgeColour { get; set; } = new(0.55f, 0.6f, 0.68f);

    [ExportGroup("History")]
    [Export] public int HistoryLayer { get; set; } = 10; // the CanvasLayer the top panel, the log and the map are on
    [Export] public int ModalLayer { get; set; } = 20;   // the message boxes' (TopPanelModal), above the top panel and the map

    [ExportGroup("Game screen")]
    // The options' panel (MainGameSelect.cs): its top middle GameOptionsOffset from the square's bottom middle (screen
    // pixels); built in panel pixels (the rest of these) and scaled by GameOptionsScale (the top panel's 0.6).
    [Export] public Vector2 GameOptionsOffset { get; set; } = new(0, 16);
    [Export] public float GameOptionsScale { get; set; } = 0.6f;
    [Export] public float GameOptionRowHeight { get; set; } = 54f;
    [Export] public int GameOptionFontSize { get; set; } = 30;
    [Export] public float GameOptionBoxSize { get; set; } = 36f;    // a tick box's side
    [Export] public float GameOptionIndent { get; set; } = 56f;     // the final scoring tiles, in from the boxes
    [Export] public float CalculatorStrictGap { get; set; } = 40f; // from Calculator's words to Strict's box
    [Export] public float GameChoiceScale { get; set; } = 1f;       // the squares, of round 6's panel on the board
    [Export] public float GameChoiceIconScale { get; set; } = 0.9f; // an icon's size, of its square's (rectangle's) height
    [Export] public Vector2 GameChoiceCentre { get; set; } = new(960, 540); // screen pixels: the Terra Mystica square's centre
    [Export] public float GameSymbolScale { get; set; } = 3f;       // the Terra Mystica square, of the others' size
    [Export] public float FinalTileWidthRatio { get; set; } = 0.8f; // a final scoring tile's width, of its height (also on the board)
    [Export] public float FinalTileGap { get; set; } = 12f;         // between the final scoring tiles' squares
    [Export] public float FinalTilesGap { get; set; } = 8f;         // screen pixels above and below the tiles' row
    [Export] public Color FinalTileDisabledTint { get; set; } = new(0.55f, 0.55f, 0.55f, 0.55f); // while Fire & Ice is unticked
    [Export] public float GameChoiceHighlightScale { get; set; } = 0.5f;

    [ExportGroup("Faction selection")]
    [Export] public Vector2 FactionGridStep { get; set; } = new(250, 330);  // from one column / row to the next
    [Export] public float FactionPortraitScale { get; set; } = 0.85f;
    [Export] public float TurnOrderSpacing { get; set; } = 300f;            // between the chosen factions in their row
    [Export] public int FactionNameFontSize { get; set; } = 22;
    [Export] public Vector2 FactionNameOffset { get; set; } = new(0, 145);  // the name's centre, from the portrait's
    [Export] public float FireAndIceColumnGap { get; set; } = 60f;          // extra space before the ice column
    // The starting-terrain wheel: its size and nudge from the portrait it is over, and the disc behind it.
    [Export] public float FactionWheelScale { get; set; } = 0.8f;
    [Export] public Vector2 FactionWheelOffset { get; set; }
    [Export] public Color FactionWheelBackingColour { get; set; } = new(0.08f, 0.08f, 0.08f, 0.88f);
    // The chosen terrain's badge on the portrait: its size, and where it sits from the portrait's centre (portrait pixels).
    [Export] public float FactionBadgeScale { get; set; } = 0.6f;
    [Export] public Vector2 FactionBadgeOffset { get; set; } = new(80, -110);
    // While the turn order is chosen: each chosen faction's starting VP above its portrait (choice pixels), clickable:
    // left +1, right −1, Shift ×10 (MainModes.OnModeInput).
    [Export] public Vector2 StartingVictoryPointsOffset { get; set; } = new(0, -175);
    [Export] public int StartingVictoryPointsIconSize { get; set; } = 72;
    [Export] public int StartingVictoryPointsFontSize { get; set; } = 30;
    // The Ice Maidens' favour: the tiles laid out as in the bank (a column per track), this far apart, this size,
    // nudged from the screen's centre, over a dim of this colour.
    [Export] public Vector2 FavourOverlayStep { get; set; } = new(240, 140);
    [Export] public float FavourOverlayScale { get; set; } = 1f;
    [Export] public Vector2 FavourOverlayOffset { get; set; }
    [Export] public Color FavourOverlayDimColour { get; set; } = new(0, 0, 0, 0.6f);
    // A chosen faction's white edge highlight (ShowChosenFaction).
    [Export] public float FactionHighlightMargin { get; set; }          // how far it stands out from the portrait (choice pixels)
    [Export] public float FactionHighlightCornerRadius { get; set; } = 16f;
    [Export] public float FactionHighlightScale { get; set; } = 1f;      // its line widths
    // With the fan factions (four rows: the published factions above, the fan factions below), the portraits and the
    // rows' spacing are this much of their usual size.
    [Export] public float FanFactionGridScale { get; set; } = 0.62f;

    [ExportGroup("Map choice")]
    [Export] public Vector2 MapChoicesCentre { get; set; } = new(960, 470); // screen pixels: the middle of the grid
    // The "Custom map" key (MainCustomMap.cs): its middle from the grid's middle, its size, its words' size (screen pixels).
    [Export] public Vector2 CustomMapButtonOffset { get; set; } = new(0, 480);
    [Export] public Vector2 CustomMapButtonSize { get; set; } = new(300, 72);
    [Export] public int CustomMapButtonFontSize { get; set; } = 28;

    [ExportGroup("Placing on a transformed hex")]
    // A piece put on a hex as it is transformed (see HoldThenPlace). In map pixels (a 74 px hex radius).
    [Export] public float HoldHeight { get; set; } = 60f;     // how high above the hex it waits while the hex turns over
    [Export] public float DwellingSpeed { get; set; } = 500f; // how fast it moves to that point (taking at most MoveSeconds, as
                                                              // any other piece), and then down onto the hex

    [ExportGroup("Building after spades")]
    // The offer to build after spades (RenderBuildOffer): the leftmost dwelling's white highlight, in its own pixels.
    [Export] public int BuildOfferGlints { get; set; } = 1;              // on the dwelling and on each hex
    [Export] public float BuildOfferMargin { get; set; } = 6f;           // how far it stands out from the dwelling's drawing
    [Export] public float BuildOfferCornerRadius { get; set; } = 8f;
    [Export] public float BuildOfferHighlightScale { get; set; } = 1f;   // its line widths

    [ExportGroup("Upgrade choice")]
    // In the Pieces layer's pixels (a building on the map is drawn at scale 1 there).
    [Export] public Vector2 UpgradeChoiceOffset { get; set; } = new(0, -120);   // from the hex to the choices' middle
    [Export] public float UpgradeChoiceSpacing { get; set; } = 110f;             // from one choice to the next
    [Export] public float UpgradeChoiceScale { get; set; } = 1.2f;               // of a building on the map

    [ExportGroup("Panel shuffle")]
    // The Move step's easing (Main.SlideTransition, SlideEase).
    [Export] public Tween.TransitionType ShuffleTransition { get; set; } = Tween.TransitionType.Sine;
    [Export] public Tween.EaseType ShuffleEase { get; set; } = Tween.EaseType.InOut;

    [ExportGroup("Victory point lights")]
    [Export] public float PointsIntervalSeconds { get; set; } = 0.15f;                 // seconds between points
    [Export] public Color PointsLossColour { get; set; } = new(1f, 0.25f, 0.2f); // a point lost
    [Export] public float PointsTileCornerRadius { get; set; } = 12f;           // tiles and panel areas (board pixels)

    [ExportGroup("Modes")]
    [Export] public Color PriestDebtColour { get; set; } = new("e0343a");
    [Export] public int PriestDebtZIndex { get; set; } = 30;

    [ExportGroup("Calculator")]
    [Export] public float CalculatorGainHeight { get; set; } = 96f;  // the bar's height, in a board's pixels
    [Export] public float CalculatorGainGap { get; set; } = 6f;      // between the bar and the board under it
    [Export] public Vector2 NeutralBankOffset { get; set; } // from where the second player's board would be

    [ExportGroup("Landscapes")]
    [Export] public float TownBonusRise { get; set; } = 1.4f;        // the Witches' two discs: their middle, this many hex radii above the key's hex
    [Export] public float TownBonusChipRadius { get; set; } = 0.5f;  // each disc's radius, in hex radii
    [Export] public float TownBonusGap { get; set; } = 0.3f;         // between the two discs, in hex radii

    [ExportGroup("Fan factions")]
    [Export] public float ArchivistsPassTileScale { get; set; } = 0.6f; // their two pass tiles, each in a corner
    [Export] public float ChashDallahChipRadius { get; set; } = 30f;
    [Export] public Vector2 ChashDallahChipOffset { get; set; } = new(0, -70);   // from the power action to the point between
    [Export] public float ChashDallahChipSpacing { get; set; } = 70f;
    [Export] public float BridgeSlideSeconds { get; set; } = 0.35f;

    [ExportGroup("Saving")]
    [Export] public float AutosaveSeconds { get; set; } = 2f;
}
