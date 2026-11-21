using Core;
using Godot;

namespace TMCalc;

// Textures chosen by code at runtime. Most are drawn in code: pieces (buildings and priests), bridges and the
// map's hexes (PieceArt, HexArt), pieces and bridges in the player's colour on the board and panels and in white
// where they appear as icons; icons and blank tiles (IconArt). The only image files are the faction portraits,
// res://sprites/prototype/portrait_<faction>.png. Pictures placed in the editor use IconSprite and BlankTileSprite.
public static class Art
{
    private const string Folder = "res://sprites/prototype/";

    public static Texture2D PieceTexture(PieceKind kind, PlayerColour colour) =>
        PieceArt.TextureFor(kind, PieceColour(colour), OutlineFor(colour));

    // The colours buildings are drawn in: the player colours (the faction boards' frame
    // colours, FactionBoardArt.FrameColour), and the near-black outline every piece has (black pieces too).
    // TODO (art): review yellow, brown, black and grey pieces.
    private static readonly Color PieceOutline = new("141414");
    private static readonly Color IconWhite = new("f1f5f8");
    public static Color PieceColour(PlayerColour colour) => colour switch
    {
        PlayerColour.Red => FactionBoardArt.FrameColour(Terrain.Wasteland),
        PlayerColour.Yellow => FactionBoardArt.FrameColour(Terrain.Desert),
        PlayerColour.Brown => FactionBoardArt.FrameColour(Terrain.Plains),
        PlayerColour.Black => FactionBoardArt.FrameColour(Terrain.Swamp),
        PlayerColour.Blue => FactionBoardArt.FrameColour(Terrain.Lake),
        PlayerColour.Green => FactionBoardArt.FrameColour(Terrain.Forest),
        PlayerColour.Grey => FactionBoardArt.FrameColour(Terrain.Mountain),
        PlayerColour.White => FactionBoardArt.FrameColour(Terrain.Ice),
        PlayerColour.Orange => FactionBoardArt.FrameColour(Terrain.Volcano),
        _ => IconWhite,
    };
    public static Color OutlineFor(PlayerColour colour) => PieceOutline; // black for black pieces too

    // A building or priest as an icon: a white piece.
    private static Texture2D BuildingIcon(PieceKind kind) => PieceArt.TextureFor(kind, IconWhite, PieceOutline);

    // Trackers are drawn (CultArt). Still image files: the portraits.
    public static Texture2D TrackerTexture(PlayerColour colour) => CultArt.Tracker(PieceColour(colour));
    public static Texture2D PortraitTexture(Faction faction) => Load($"portrait_{faction}");

    // Drawn in code (IconArt), at the sizes the old pictures had, so layouts made for them still fit.
    public static Texture2D WorkerTexture() => IconArt.Texture(Icon.Worker, 64);
    // Bonus actions: an orange octagon, the action's icon on top, and the crossed-out octagon over both when used.
    public const int OctagonSize = 80;
    public static Texture2D BonusOctagonTexture() => IconArt.Texture(Icon.ActionOctagon, OctagonSize);
    public static Texture2D BonusUsedTexture() => IconArt.Texture(Icon.ActionOctagonUsed, OctagonSize);
    // Icons that are a whole octagon of their own (double action, sandstorm, dwelling to trading post, shapeshift) are made at
    // the octagon's size, so they cover it exactly. Two spades and two cult steps are drawn as amounts instead
    // (BonusActionContent); their icon here is for anything that can only show one picture.
    // owner: whose action it is, for the icons with pieces in it (the Witches' dwelling and its hex, the Swarmlings'
    // buildings), drawn in that faction's colour; null: white.
    public static Texture2D BonusActionIcon(BonusAction action, Faction? owner = null) => action switch
    {
        BonusAction.CultStep or BonusAction.TwoCultSteps => IconArt.Texture(Icon.AnyCultStep, 48),
        BonusAction.OneSpade or BonusAction.TwoSpades => IconArt.Texture(Icon.Spade, 48),
        BonusAction.BuildOnHomeTerrain => InColour(Icon.WhenDwellingBuilt, 56, owner),
        BonusAction.FreeTradingPost => InColour(Icon.DwellingToTradingPost, OctagonSize, owner),
        BonusAction.Sandstorm => IconArt.Texture(Icon.Sandstorm, OctagonSize),
        BonusAction.DoubleAction => IconArt.Texture(Icon.DoubleAction, OctagonSize),
        BonusAction.Shapeshift => IconArt.Texture(Icon.ShiftAction, OctagonSize),
        // fan factions' strongholds: whole octagons
        BonusAction.MoveBridge => IconArt.Texture(Icon.ArchitectsMoveBridge, OctagonSize),
        BonusAction.TokensToBowlThree => IconArt.Texture(Icon.TimeTravellersBowls, OctagonSize),
        BonusAction.CoinsForTradingPosts => IconArt.Texture(Icon.ProspectorsCoins, OctagonSize),
        BonusAction.SwapFavour => IconArt.Texture(Icon.ConspiratorsSwap, OctagonSize),
        BonusAction.RiverTokens => IconArt.Texture(Icon.WyrmTokensToRiver, OctagonSize),
        BonusAction.SelkiesSpade => IconArt.Texture(Icon.SelkiesSpade, OctagonSize),
        BonusAction.MoveRock => IconArt.Texture(Icon.GeologistsRock, OctagonSize),
        BonusAction.FourPower => IconArt.Texture(Icon.PowerGain, 48),
        _ => throw new System.ArgumentOutOfRangeException(nameof(action), action, "No icon for this bonus action."),
    };

    private static Texture2D InColour(Icon icon, int size, Faction? owner) =>
        owner is Faction faction ? IconArt.Texture(icon, size, PieceColour(Factions.ColourOf(faction))) : IconArt.Texture(icon, size);

    // Bonus actions drawn as an amount in the octagon's Content bundle (like the power actions): two spades side by
    // side, a cult step icon with a 2. Null for those drawn with BonusActionIcon.
    public static Amounts BonusActionContent(BonusAction action) => action switch
    {
        BonusAction.TwoSpades => Amounts.Of(ResourceKind.Spade, 2),
        BonusAction.TwoCultSteps => Amounts.Of(ResourceKind.AnyCultStep, 2),
        BonusAction.FourPower => Amounts.Of(ResourceKind.Power, 4), // the Enlightened's stronghold
        _ => null,
    };

    // A book action (repeatable, never crossed out): the book with an octagon panel exactly the size of an action
    // octagon (IconArt.BookScale), and what the action does on the panel. Null for factions without one.
    public static int BookSize => Mathf.RoundToInt(OctagonSize * IconArt.BookScale);
    public static Texture2D BookActionBacking() => IconArt.Texture(Icon.BookAction, BookSize);
    public static Texture2D BookActionIcon(Faction faction) => faction switch
    {
        // the bridge in the faction's colour (the Engineers' grey; the fan factions' Architects and Atlanteans)
        Faction.Engineers or Faction.Architects or Faction.Atlanteans => ColouredBridge(Factions.ColourOf(faction)),
        _ => null,
    };
    // made once per colour, so the panel can tell it is the same picture
    private static readonly System.Collections.Generic.Dictionary<PlayerColour, Texture2D> _bridges = new();
    private static Texture2D ColouredBridge(PlayerColour colour) =>
        _bridges.TryGetValue(colour, out Texture2D known) ? known : _bridges[colour] = BridgeIconTexture(colour);

    // The book as the panel shows it (PlayerPanel.ShowBonusActions): the Engineers', Architects' and Atlanteans'
    // bridge; the Djinni's lamps and the Goblins' treasures, a count; Chash Dallah's wide book with their income now on
    // one hand and the next step's cost under it (none at the top); the Treasurers' Safe with what lies in it. Null for
    // factions without one (passive abilities aren't drawn).
    public static BookView Book(PlayerState player) => player.Faction switch
    {
        Faction.Djinni => new BookView(Icon.BookAction, Content: Amounts.Of(ResourceKind.Lamp, player.Lamps), ContentKinds: "lamps"),
        Faction.Goblins => new BookView(Icon.BookAction, Content: Amounts.Of(ResourceKind.Treasure, player.Treasures), ContentKinds: "treasures"),
        Faction.ChashDallah => new BookView(Icon.WideBook,
            Content: Amounts.From(Factions.IncomeTrackIncome(player.IncomeTrack)), ContentKinds: "workers, coins", Income: true,
            Below: player.IncomeTrack >= Config.ChashDallahIncomeTrackTop ? null : Amounts.From(Config.ChashDallahIncomeStepCost)),
        Faction.Treasurers => new BookView(Icon.Safe,
            Content: SafeContents(player)),
        _ => BookActionIcon(player.Faction) is Texture2D icon ? new BookView(Icon.BookAction, icon) : null,
    };
    // Blank tiles; their text (or icons) are put on in code. Scaled to the old pictures' widths.
    public static Texture2D RoundTileTexture() => IconArt.Texture(BlankTile.Round, 201f / 312f);
    public static Texture2D PassTileTexture() => IconArt.Texture(BlankTile.Pass, 128f / 120f);
    public static Texture2D FavourTexture() => IconArt.Texture(BlankTile.Favour, 173f / 220f);
    public static Texture2D BridgeTexture(PlayerColour colour) => HexArt.Bridge(PieceColour(colour), OutlineFor(colour)); // left to right
    // A bridge in a player's supply (or being dragged from it): turned up to the right, with a thinner outline.
    public static Texture2D BridgeIconTexture(PlayerColour colour) => HexArt.BridgeIcon(PieceColour(colour), OutlineFor(colour));
    // Where a bridge could go: a pale, see-through bridge.
    public static Texture2D BridgeSpotTexture() => HexArt.Bridge(new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0.9f));

    // The icon for an amount of a resource (see ResourceBundle), `size` pixels square; income: the income
    // version (the icon resting on an open hand), where there is one. Buildings are white pieces at their own
    // size (ResourceBundle scales them); bridges a white bridge.
    // `size` is the resource's size, with or without the hand (the hand, and the power icons' arrow, are extra,
    // with the resource kept in the texture's middle; see IconArt.Canvas).
    // priest: the priest's colour, if not white (e.g. the faction's, on a building board's income).
    public static Texture2D ResourceIcon(ResourceKind kind, bool income = false, int size = 48, Color? priest = null) =>
        IconFor(kind, income) is Icon icon
            ? kind == ResourceKind.Priest && priest is Color colour ? IconArt.Texture(icon, size, colour)
            : IconArt.Texture(icon, IsWhenBuilt(kind) ? Mathf.RoundToInt(size * 1.1f) : size) : kind switch
        {
            ResourceKind.Bridge => HexArt.BridgeIcon(IconWhite, PieceOutline),
            ResourceKind.Stronghold => BuildingIcon(PieceKind.Stronghold),
            ResourceKind.TradingPost => BuildingIcon(PieceKind.TradingPost),
            ResourceKind.Dwelling => BuildingIcon(PieceKind.Dwelling),
            ResourceKind.Temple => BuildingIcon(PieceKind.Temple),
            ResourceKind.Sanctuary => BuildingIcon(PieceKind.Sanctuary),
            _ => null,
        };

    // Where a number over that icon is centred, from the icon's centre: over the resource itself (the token of
    // the power icons, the resource on an income icon's hand).
    public static Vector2 ResourceNumberCentre(ResourceKind kind, bool income = false, int size = 48) =>
        IconFor(kind, income) is Icon icon ? IconArt.NumberCentre(icon, size) : Vector2.Zero;

    // The "when this is built" icons are drawn 10% bigger than the rest, beside them in a bundle.
    private static bool IsWhenBuilt(ResourceKind kind) =>
        kind is ResourceKind.WhenDwellingBuilt or ResourceKind.WhenTradingPostBuilt or ResourceKind.WhenTempleBuilt
            or ResourceKind.WhenBigBuildingBuilt;

    // The drawn icon for a kind, or null for those drawn as pieces (buildings, bridges).
    private static Icon? IconFor(ResourceKind kind, bool income) => kind switch
    {
        ResourceKind.Priest => income ? Icon.PriestIncome : Icon.Priest,
        ResourceKind.Worker => income ? Icon.WorkerIncome : Icon.Worker,
        ResourceKind.Coin => income ? Icon.CoinIncome : Icon.Coin,
        ResourceKind.Power => income ? Icon.PowerGainIncome : Icon.PowerGain,
        ResourceKind.PowerSpend => Icon.PowerSpend,
        ResourceKind.Spade => Icon.Spade,
        ResourceKind.Key => Icon.Key,
        ResourceKind.VictoryPoint => income ? Icon.PointsIncome : Icon.Points,
        ResourceKind.PowerValue => Icon.PowerToken,
        ResourceKind.Shipping => Icon.Shipping,
        ResourceKind.WhenDwellingBuilt => Icon.WhenDwellingBuilt,
        ResourceKind.WhenTradingPostBuilt => Icon.WhenTradingPostBuilt,
        ResourceKind.WhenTempleBuilt => Icon.WhenTempleBuilt,
        ResourceKind.WhenBigBuildingBuilt => Icon.WhenBigBuildingBuilt,
        ResourceKind.Town => Icon.Town,
        ResourceKind.FireStep => Icon.FireStep,
        ResourceKind.WaterStep => Icon.WaterStep,
        ResourceKind.EarthStep => Icon.EarthStep,
        ResourceKind.AirStep => Icon.AirStep,
        ResourceKind.EveryCultStep => Icon.EveryCultStep,
        ResourceKind.PriestOnTrack => Icon.PriestToZone, // the Acolytes' stronghold's priest going into a zone
        ResourceKind.AnyCultStep => Icon.AnyCultStep,
        ResourceKind.CarpetRange => Icon.CarpetFlight,
        ResourceKind.Tunnel => Icon.Tunnel,
        ResourceKind.TokenRemoved => Icon.TokenRemoved,
        ResourceKind.Lamp => Icon.Lamp,
        ResourceKind.Treasure => Icon.GoblinsTreasure,
        ResourceKind.GoldenSpade => Icon.GoldenSpade,
        ResourceKind.Pyro => Icon.FlamePiece,
        _ => null,
    };

    // What lies in the Treasurers' Safe.
    public static Amounts SafeContents(PlayerState player) =>
        Amounts.Of((ResourceKind.Priest, player.SafePriestList.Count), (ResourceKind.Worker, player.SafeWorkers),
            (ResourceKind.Coin, player.SafeCoins));

    // GD.Load caches: loading the same file twice returns the same texture, so calling these often is cheap.
    private static Texture2D Load(string name) =>
        GD.Load<Texture2D>($"{Folder}{name.ToLowerInvariant()}.png");
}

// A faction's book as a player panel shows it (Art.Book): Backing, the book (Icon.BookAction), Chash Dallah's wide book
// (Icon.WideBook) or the Treasurers' Safe (Icon.Safe); on it a picture (the bridge) or amounts (Content: a count, an
// income on one hand; ContentKinds: the kinds always written, so a 0 shows); Below: amounts under it (a cost).
public sealed record BookView(Icon Backing, Texture2D Picture = null, Amounts Content = null, string ContentKinds = null,
    bool Income = false, Amounts Below = null);
