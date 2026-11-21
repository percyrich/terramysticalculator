using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The drawn icons. Appended at the end only: scenes store an icon by its number.
public enum Icon
{
    FireStep, WaterStep, EarthStep, AirStep, AnyCultStep, EveryCultStep,
    Coin, Worker, Priest, Key, Points, Spade, Shipping,
    PowerToken, PowerGain, PowerSpend,
    ActionOctagon, ActionOctagonUsed, TriggerArrow,
    AnyCultStepIncome, CoinIncome, WorkerIncome, PriestIncome, PointsIncome, PowerGainIncome,
    OnPass, WhenDwellingBuilt, WhenTradingPostBuilt, WhenTempleBuilt, WhenSanctuaryBuilt, WhenStrongholdBuilt,
    WhenBigBuildingBuilt, EndOfRoundBrace, Town,
    PassTileRule, PassTileRuleUpright, PassTileRuleWhite, PassTileRuleWhiteUpright,
    IncomeHand, // the income icons' hand on its own (for one hand under a whole bundle), centred
    ThreeHexTown, // added last: scenes store icons by number
    BookAction, DoubleAction, Sandstorm, DwellingToTradingPost, CarpetFlight, Tunnel, // faction actions and abilities (keep new ones at the end)
    BridgeBetween, PowerCross, IceRing, VariableRing, Lamp, Safe, FlamePiece, TerraMysticaSymbol,
    PlayersToBowl, PriestToZone, ShiftAction, PowerActionForever,
    CultScoring, Network, ScoringStrongholdSanctuary, ScoringEdge, ScoringClusters, ScoringDistance, // endgame scoring
    TempleOnIce, TokenRemoved,
    // fan factions
    ArchitectsMoveBridge, ArchivistsTwoPassTiles, TimeTravellersBowls, GoldenSpade, ProspectorsCoins,
    WyrmTokensToBowl, WyrmTokensToRiver, AtlanteansHexes, DwellingOnLake, ChashPowerAction, WideBook,
    ConspiratorsSwap, SelkiesSpade, SnowShamansIce, EnlightenedConversions, FirewalkersNeighbourhoods,
    LavaToken, EmberLavaUnder, GlowingRock, GeologistsRock, GoblinsTreasure,
}

// The blank tiles, ready for icons and text.
public enum BlankTile { Round, Pass, KeyToken, PowerAction, Favour }

// Every icon and blank tile, drawn in code. Each is written as SVG text (through Svg, whose calls read like canvas
// drawing code) and Godot turns that into a texture of the size asked for:
//   IconArt.Texture(Icon.Coin, 48)        48 × 48 pixels, the coin in the middle
//   IconArt.Texture(BlankTile.Pass)       the blank pass tile at its natural size, 120 × 375
//   IconArt.Texture(BlankTile.Pass, 0.5f) the same at half size
// An icon fills about 6/7 of its square, leaving room for outlines and shadows. Its lines are
// worked out for the size asked for (thin lines stay at least a pixel wide), so ask for the size it is shown at
// rather than scaling a big one down.
public static partial class IconArt
{
    // Pixels made per pixel of size: 2 keeps textures sharp on high-density screens and when zoomed in twice.
    private const float Detail = 2f;

    // `size` is the size of the thing the icon shows (the resource, the token); a few icons are bigger than that
    // (see Canvas), with that thing still in the middle.
    public static Texture2D Texture(Icon icon, int size) =>
        Cached((icon, size), () => Rasterise(SvgText(icon, size), CanvasSize(icon, size), 1f));

    // The same icon with its pieces in another colour (e.g. a faction's), for the icons that have any: the "when
    // built" icons (the building and its ground plate), the Swarmlings' dwelling to trading post and the priest (plain
    // and income: a building board's incomes).
    public static Texture2D Texture(Icon icon, int size, Color pieces) =>
        Cached((icon, size, pieces), () => Rasterise(SvgText(icon, size, pieces), CanvasSize(icon, size), 1f));

    // How much bigger than size × size an icon's texture is, across and down. Some icons have a part that is
    // allowed outside the resource's square, and get room for it on both sides, so the resource stays in the
    // middle (where a number goes, and level with the plain icons in a row):
    //   power gain and spend: the arrow above the token;
    //   income icons: the hand below the resource (and for power, the arrow above too).
    private static Vector2 Canvas(Icon icon) => icon switch
    {
        Icon.PowerGain or Icon.PowerSpend => new Vector2(1f, 1.3f),
        _ when IsIncome(icon) => new Vector2(1.35f, 1.85f),
        Icon.IncomeHand => new Vector2(1.35f, 0.6f),
        Icon.Safe => new Vector2(2f, 1f), // twice as wide as tall
        Icon.ArchivistsTwoPassTiles or Icon.AtlanteansHexes or Icon.ChashPowerAction or Icon.FirewalkersNeighbourhoods => new Vector2(2f, 1f),
        Icon.WideBook => new Vector2(WideBookCanvas, 1f),
        _ => Vector2.One,
    };

    private static Vector2I CanvasSize(Icon icon, int size) =>
        new(Mathf.RoundToInt(Canvas(icon).X * Enlarged(icon) * size), Mathf.RoundToInt(Canvas(icon).Y * Enlarged(icon) * size));

    // Icons drawn this many times bigger than the size asked for, round the same centre, on a canvas grown to match:
    // they look bigger wherever they are used, and nothing beside them moves (the Fakirs' carpet, the Dwarves' tunnel).
    private static float Enlarged(Icon icon) => icon is Icon.CarpetFlight or Icon.Tunnel ? 2f : 1f;

    public static Texture2D Texture(BlankTile tile, float scale = 1f)
    {
        Vector2 size = NaturalSize(tile) * scale;
        return Cached((tile, scale), () => Rasterise(SvgText(tile), new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y)), scale));
    }

    // Where a number written over an icon should be centred, from the texture's centre, in pixels. Every icon keeps
    // the thing a number counts (a resource, a token) in the middle, so this is always the centre; kept so a future
    // icon can say otherwise.
    public static Vector2 NumberCentre(Icon icon, int size) => Vector2.Zero;

    // The icons with the hand under the resource.
    public static bool IsIncome(Icon icon) => icon is Icon.AnyCultStepIncome or Icon.CoinIncome or Icon.WorkerIncome
        or Icon.PriestIncome or Icon.PointsIncome or Icon.PowerGainIncome;

    // The SVG text itself, e.g. to save as a file.
    public static string SvgText(Icon icon, int size, Color? pieces = null)
    {
        Vector2I canvas = CanvasSize(icon, size);
        Vector2 middle = new(canvas.X / 2f, canvas.Y / 2f); // icons are drawn centred on (0, 0)
        if (CentredOnDrawing(icon))
            middle -= DrawnMiddle(icon, size, pieces, canvas);
        return Drawn(icon, size, pieces, canvas, middle);
    }

    // The icon drawn on a canvas `canvas` pixels across, its (0, 0) at `origin`.
    private static string Drawn(Icon icon, int size, Color? pieces, Vector2 canvas, Vector2 origin)
    {
        var svg = new Svg(canvas.X, canvas.Y);
        svg.Translate(origin.X, origin.Y);
        Drawing(icon, pieces)(svg, size * 6f / 7f * Enlarged(icon));
        return svg.Text();
    }

    // Icons whose drawing is not laid out round (0, 0): each is moved so the middle of what it draws (its bounding box,
    // outlines included) is the canvas's middle. The endgame scoring tiles' icons.
    private static bool CentredOnDrawing(Icon icon) => icon is Icon.ScoringStrongholdSanctuary or Icon.ScoringEdge
        or Icon.ScoringClusters or Icon.ScoringDistance;

    // The middle of what an icon draws, from its (0, 0), in pixels: it is drawn on a canvas twice the size (nothing cut
    // off) and the pixels it covers are measured.
    private static Vector2 DrawnMiddle(Icon icon, int size, Color? pieces, Vector2I canvas)
    {
        var image = new Image();
        if (image.LoadSvgFromString(Drawn(icon, size, pieces, canvas * 2, canvas), Detail) != Error.Ok)
            return Vector2.Zero;
        Rect2I used = image.GetUsedRect();
        if (used.Size == Vector2I.Zero)
            return Vector2.Zero;
        return (used.Position + (Vector2)used.Size / 2) / Detail - canvas;
    }

    public static string SvgText(BlankTile tile)
    {
        Vector2 size = NaturalSize(tile);
        var svg = new Svg(size.X, size.Y);
        Action<Svg, float, float> draw = tile switch
        {
            BlankTile.Round => RoundTile,
            BlankTile.Pass => PassTile,
            BlankTile.KeyToken => KeyToken,
            BlankTile.PowerAction => PowerAction,
            _ => Favour,
        };
        draw(svg, size.X, size.Y);
        return svg.Text();
    }

    internal static Vector2 NaturalSize(BlankTile tile) => tile switch
    {
        BlankTile.Round => new Vector2(312, 164),
        BlankTile.Pass => new Vector2(120, 375),
        BlankTile.KeyToken => new Vector2(150, 172),
        BlankTile.PowerAction => new Vector2(247, 127),
        _ => new Vector2(220, 112),
    };

    // Each icon's drawing: centred on (0, 0), about s across. pieces: the colour of its pieces, where it has any
    // (see Texture(icon, size, pieces)); null for the usual.
    private static Action<Svg, float> Drawing(Icon icon, Color? pieces = null) => icon switch
    {
        Icon.FireStep => (svg, s) => CultDisc(svg, s, Track.Fire),
        Icon.WaterStep => (svg, s) => CultDisc(svg, s, Track.Water),
        Icon.EarthStep => (svg, s) => CultDisc(svg, s, Track.Earth),
        Icon.AirStep => (svg, s) => CultDisc(svg, s, Track.Air),
        Icon.AnyCultStep => AnyCult,
        Icon.EveryCultStep => EveryCult,
        Icon.Coin => Coin,
        Icon.Worker => Worker,
        Icon.Priest => (svg, s) => Priest(svg, s, pieces ?? White),
        Icon.Key => (svg, s) => Key(svg, s * KeyScale),
        Icon.Points => Points,
        Icon.Spade => Spade,
        Icon.Shipping => Ship,
        Icon.PowerToken => PowerToken,
        Icon.PowerGain => PowerGain,
        Icon.PowerSpend => PowerSpend,
        Icon.ActionOctagon => (svg, s) => Octagon(svg, s, used: false),
        Icon.ActionOctagonUsed => (svg, s) => Octagon(svg, s, used: true),
        Icon.TriggerArrow => TriggerArrow,
        Icon.AnyCultStepIncome => Income(AnyCult),
        Icon.CoinIncome => Income(Coin),
        Icon.WorkerIncome => Income(Worker),
        Icon.PriestIncome => Income((svg, s) => Priest(svg, s, pieces ?? White)),
        Icon.PointsIncome => Income(Points),
        Icon.PowerGainIncome => Income(PowerGain),
        Icon.IncomeHand => (svg, s) => Hand(svg, s, 0),
        Icon.OnPass => OnPass,
        Icon.WhenDwellingBuilt => (svg, s) => WhenBuilt(svg, s, PieceKind.Dwelling, pieces),
        Icon.WhenTradingPostBuilt => (svg, s) => WhenBuilt(svg, s, PieceKind.TradingPost, pieces),
        Icon.WhenTempleBuilt => (svg, s) => WhenBuilt(svg, s, PieceKind.Temple, pieces),
        Icon.WhenSanctuaryBuilt => (svg, s) => WhenBuilt(svg, s, PieceKind.Sanctuary, pieces),
        Icon.WhenStrongholdBuilt => (svg, s) => WhenBuilt(svg, s, PieceKind.Stronghold, pieces),
        Icon.WhenBigBuildingBuilt => (svg, s) => WhenBuilt(svg, s, null, pieces),
        Icon.EndOfRoundBrace => Brace,
        Icon.Town => TownWithKey,
        Icon.ThreeHexTown => ThreeHexTown,
        Icon.BookAction => BookAction,
        Icon.DoubleAction => DoubleAction,
        Icon.Sandstorm => Sandstorm,
        Icon.DwellingToTradingPost => (svg, s) => DwellingToTradingPost(svg, s, pieces ?? White),
        Icon.CarpetFlight => CarpetFlight,
        Icon.Tunnel => Tunnel,
        Icon.BridgeBetween => BridgeBetween,
        Icon.PowerCross => PowerCross,
        Icon.IceRing => IceRing,
        Icon.VariableRing => VariableRing,
        Icon.Lamp => Lamp,
        Icon.Safe => Safe,
        Icon.FlamePiece => FlamePiece,
        Icon.TerraMysticaSymbol => TerraMysticaSymbol,
        Icon.PlayersToBowl => PlayersToBowl,
        Icon.PriestToZone => PriestToZone,
        Icon.ShiftAction => ShiftAction,
        Icon.PowerActionForever => PowerActionForever,
        Icon.CultScoring => CultScoring,
        Icon.Network => Network,
        Icon.ScoringStrongholdSanctuary => ScoringStrongholdSanctuary,
        Icon.ScoringEdge => ScoringEdge,
        Icon.ScoringClusters => ScoringClusters,
        Icon.ScoringDistance => ScoringDistance,
        Icon.TempleOnIce => (svg, s) => TempleOnIce(svg, s, pieces ?? White),
        Icon.TokenRemoved => TokenRemoved,
        Icon.ArchitectsMoveBridge => ArchitectsMoveBridge,
        Icon.ArchivistsTwoPassTiles => ArchivistsTwoPassTiles,
        Icon.TimeTravellersBowls => TimeTravellersBowls,
        Icon.GoldenSpade => GoldenSpade,
        Icon.ProspectorsCoins => ProspectorsCoins,
        Icon.WyrmTokensToBowl => WyrmTokensToBowl,
        Icon.WyrmTokensToRiver => WyrmTokensToRiver,
        Icon.AtlanteansHexes => AtlanteansHexes,
        Icon.DwellingOnLake => (svg, s) => WhenBuilt(svg, s, PieceKind.Dwelling, pieces ?? HexArt.TerrainColour(Terrain.Lake)),
        Icon.ChashPowerAction => ChashPowerAction,
        Icon.WideBook => (svg, s) => BookAction(svg, s, s * WideBookExtra),
        Icon.ConspiratorsSwap => ConspiratorsSwap,
        Icon.SelkiesSpade => SelkiesSpade,
        Icon.SnowShamansIce => SnowShamansIce,
        Icon.EnlightenedConversions => EnlightenedConversions,
        Icon.FirewalkersNeighbourhoods => FirewalkersNeighbourhoods,
        Icon.LavaToken => (svg, s) => LavaDisc(svg, s, 0, s * 0.05f, s * LavaIconDiameter),
        Icon.EmberLavaUnder => (svg, s) => EmberLavaUnder(svg, s, pieces ?? FactionBoardArt.FrameColour(Terrain.Volcano)),
        Icon.GlowingRock => (svg, s) => RockToken(svg, new Vector2(0, 0), s * RockIconRadius),
        Icon.GeologistsRock => GeologistsRock,
        Icon.GoblinsTreasure => GoblinsTreasure,
        Icon.PassTileRule => (svg, s) => PassRule(svg, s, upright: false, white: false),
        Icon.PassTileRuleUpright => (svg, s) => PassRule(svg, s, upright: true, white: false),
        Icon.PassTileRuleWhite => (svg, s) => PassRule(svg, s, upright: false, white: true),
        Icon.PassTileRuleWhiteUpright => (svg, s) => PassRule(svg, s, upright: true, white: true),
        _ => throw new ArgumentException($"{icon} has no drawing"),
    };

    // ---------- Turning SVG into textures ----------

    // Godot reads the SVG and draws it into an image `scale` × Detail times its size; the texture then reports
    // `size`, so it is placed and scaled like any other texture of that size, with the extra pixels for sharpness.
    internal static Texture2D Rasterise(string svg, Vector2I size, float scale)
    {
        var image = new Image();
        Error error = image.LoadSvgFromString(svg, scale * Detail);
        if (error != Error.Ok)
        {
            GD.PushError($"IconArt: Godot couldn't read an icon's SVG ({error}).");
            return null;
        }
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        texture.SetSizeOverride(size);
        return texture;
    }

    // At run time each texture is made once and reused. In the editor a new one is made each time: Godot objects
    // kept in a static field can stop the editor reloading changed scripts (as in PieceArt).
    private static readonly Dictionary<object, Texture2D> Textures = new();

    internal static Texture2D Cached(object key, Func<Texture2D> make)
    {
        if (Engine.IsEditorHint())
            return make();
        if (!Textures.TryGetValue(key, out Texture2D texture))
            Textures[key] = texture = make();
        return texture;
    }

    // ---------- Helpers ----------

    private const float Tau = Mathf.Tau;
    private static readonly Color Out = new("1b1b1b");   // the outline round everything
    private static readonly Color White = new("f1f5f8"); // white pieces
    private static readonly Color Purple = new("8f3d9a"); // power tokens
    public static Color PowerPurple => Purple;

    private static float Ow(float s) => Mathf.Max(1f, s * 0.04f); // the outline's width for an icon of size s

    // A point `radius` from (0, 0) at `angle` (radians, clockwise from the right).
    private static Vector2 At(float angle, float radius) => Vector2.FromAngle(angle) * radius;

    // Fills a path, then draws a line round it (round corners): the outline colour, Ow(s) wide, unless given.
    private static void Outlined(Svg svg, SvgPath path, Paint fill, float s, Color? line = null, float? width = null)
    {
        svg.Fill(path, fill);
        svg.Stroke(path, line ?? Out, width ?? Ow(s));
    }

    // A thick line with an outline: drawn twice, wide in the outline colour, then narrower in its own colour.
    private static void Band(Svg svg, SvgPath path, float w, Color colour, float s, string cap = "round", Color? line = null)
    {
        svg.Stroke(path, line ?? Out, w + 2 * Ow(s), "round", cap);
        svg.Stroke(path, colour, w, "round", cap);
    }

    // A colour moved towards white (a > 0) or black (a < 0) by |a|.
    internal static Color Shade(Color colour, float a) => colour.Lerp(a < 0 ? Colors.Black : Colors.White, Mathf.Abs(a));
}
