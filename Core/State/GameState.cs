namespace Core;

// The complete game at one instant. Nothing else holds game data.
public sealed record GameState(
    Phase Phase,
    GameKind? Game,                           // null until chosen
    MapId? ChosenMap,                         // null until chosen (Layout etc. hold the base map until then)
    ValueList<int> ChosenRoundTiles,          // round tile ids, round 1 first (6 once setup is done)
    ValueList<int> ChosenPassTiles,           // pass tile ids in this game, in the order clicked
    ValueList<int?> PassTileBank,             // fixed slots, filled at Start; null = empty slot
    ValueList<int> PassTileCoins,             // [pass tile id] = coins lying on that tile
    int Round,                                // 0 before round 1 begins; then 1..Config.Rounds
    ValueList<PlayerState> Players,           // factions in the game, in the order they were added
    ValueList<Faction> TurnOrder,             // this round's order
    ValueList<Faction> PassOrder,             // who has passed this round, in order of passing
    ValueList<Faction> PlacementQueue,        // starting dwellings still to place, in order (PlacingDwellings only)
    Faction? ActivePlayer,                    // null whenever no player is taking a turn
    bool ActionTaken,                         // the active player has used their one action this turn
    MapLayout Layout,                         // the shape of the map
    ValueList<Terrain> HexTerrain,            // one entry per hex: its current terrain
    ValueList<Piece?> Map,                    // one entry per hex: the piece on it; null = empty
    ValueList<int> SpadedHexes,               // hexes the active player has spent free spades on in this batch (or just
                                              // transformed to home terrain, open to a dwelling)
    ValueList<Bridge> Bridges,                // every bridge on the map
    ValueList<PlacedKey> PlacedKeys,          // every key token on the map
    ValueList<ValueList<KeyTile>> KeyBank,    // [token id] = tokens not yet taken: bottom first, top last
    ValueList<PowerOffer> PowerOffers,        // waiting for an answer, in player order (the first is answered first)
    ValueList<int> PowerActionsTaken,         // power action ids taken this round
    ValueList<ValueList<Piece?>> PriestZones, // [(int)Track][slot]; null = empty slot
    ValueList<ValueList<Faction>> TrackArrivals, // [(int)Track] = every faction, the least recent to move on it first
    ValueList<ValueList<FavourTile>> FavourBank, // [favour id] = tiles not yet taken: bottom first, top last
    ValueList<Faction> Winners,               // empty until the game ends; several if tied (no tiebreaker)
    ValueList<FinalScore> FinalScores,        // empty until the game ends; one per player, in player order
    int ActionsOwed = 0,                      // Chaos Magicians' double action: actions still to take this turn (2, then 1)
    CultistReaction? PendingCultistReaction = null, // the Cultists' build waiting for its power offers to be answered
    GameOptions? ChosenOptions = null,        // what the game includes (null: everything); see Options
    bool SkipUsed = false,                    // a carpet flight or tunnel has been used in the action under way (once per action)
    ValueList<TownMaker>? TownMakers = null,  // towns made formable this turn and what made them (null: none); see Makers
    PlayMode Mode = PlayMode.Lenient,         // how strictly the rules apply (SetMode)
    FreePlan? Planning = null,                // Free play under way (Mode Free, from round 1 on); null otherwise
    ValueList<Faction>? StartOrder = null,    // OrderingTurns: the factions clicked so far, first to act first
    NeutralBoard? Neutral = null,             // Calculator: the neutral opponent's pieces (null: none); see Neutrals
    int? LandscapeBatchHex = null,            // the special landscape placed with this batch of free spades: the batch's
                                              // other spades can't use it for range (Rules.PlaceLandscape)
    Income? TreasuryOffer = null,             // Treasurers: coins, workers and priests just received that may go into their
                                              // Safe (StoreInTreasury); gone with the next other move
    int? SpadeAnchorHex = null,               // Wisps: their trading post's hex; the free spade goes next to it, exactly 1
                                              // spade, and nothing is built there after
    ValueList<int>? Lava = null,              // Kingdom of Ember: [hex] = volcano tiles beyond the hex's own (null: none)
    ValueList<int>? WyrmTokens = null,        // Children of the Wyrm: river hexes holding one of their power tokens
    bool ShippingBoost = false,               // Selkies' stronghold action under way: their shipping +1 for its spades
    bool RewardSpades = false,                // the spades pending are a reward after the action (the Halflings'
                                              // stronghold): free actions allowed meanwhile (Rules.MidAction)
    Income? TreasuryChosen = null,            // Treasurers: what of TreasuryOffer is going into the Safe so far
                                              // (ChooseForSafe; null: nothing); settled with StoreInTreasury
    bool SafeOpeningHeld = false)             // Treasurers: the round's end waits for their round-end cult reward's Safe
                                              // choice; the next round (and the Safe's opening) follows StoreInTreasury
{
    // Kingdom of Ember: the lava tiles under this hex beyond its own.
    public int LavaAt(int hex) => Lava is { } lava && hex < lava.Count ? lava[hex] : 0;
    public ValueList<int> WyrmTokenHexes => WyrmTokens ?? ValueList<int>.Empty;

    public NeutralBoard Neutrals => Neutral ?? NeutralBoard.Empty;

    // A building (anyone's) or a neutral dwelling stands on the hex.
    public bool IsOccupied(int hex) => Map[hex] is not null || Neutrals.Hexes.Contains(hex);

    // A bridge (anyone's, the neutral opponent's too) on that spot. HexA < HexB.
    public bool IsBridged(int hexA, int hexB) =>
        Bridges.Any(bridge => bridge.HexA == hexA && bridge.HexB == hexB) || Neutrals.Bridges.Contains((hexA, hexB));

    // A priest (anyone's, or the neutral opponent's) in that slot.
    public bool IsSlotTaken(Track track, int slot) => PriestZones[(int)track][slot] is not null || Neutrals.Priests.Contains((track, slot));

    public GameOptions Options => ChosenOptions ?? GameOptions.All;

    // Free play is under way: the selected faction (ActivePlayer) acts without the turn order.
    public bool IsPlanning => Planning is not null;

    // Somebody is in debt (coins, workers or priests): refused in Strict. Below 0 VP isn't (PlayerState.OwesResources).
    public bool AnyoneInDebt => Players.Any(player => player.OwesResources);
    public ValueList<TownMaker> Makers => TownMakers ?? ValueList<TownMaker>.Empty;

    // The map shown before one is chosen is the base map; ChooseMap replaces it.
    public static readonly GameState Initial = StartingOn(Maps.Base);

    // A fresh game on the given map.
    public static GameState StartingOn(MapDefinition map) => new(
        Phase.ChoosingGame,
        null,
        null,
        ValueList<int>.Empty,
        ValueList<int>.Empty,
        ValueList<int?>.Empty,
        ValueList<int>.Repeat(0, PassTiles.Count),
        0,
        ValueList<PlayerState>.Empty,
        ValueList<Faction>.Empty,
        ValueList<Faction>.Empty,
        ValueList<Faction>.Empty,
        null,
        false,
        map.Layout,
        map.HexTerrain,
        ValueList<Piece?>.Repeat(null, map.Layout.HexCount),
        ValueList<int>.Empty,
        ValueList<Bridge>.Empty,
        ValueList<PlacedKey>.Empty,
        KeyTiles.FullBank(),
        ValueList<PowerOffer>.Empty,
        ValueList<int>.Empty,
        ValueList<ValueList<Piece?>>.Repeat(ValueList<Piece?>.Repeat(null, Config.PriestSlotsPerZone), Config.TrackCount),
        ValueList<ValueList<Faction>>.Repeat(ValueList<Faction>.Empty, Config.TrackCount),
        Favours.FullBank(),
        ValueList<Faction>.Empty,
        ValueList<FinalScore>.Empty);

    public bool IsInGame(Faction faction) => Players.Any(player => player.Faction == faction);

    // Is this terrain the home terrain of a faction in the game other than this one? (Volcano factions pay more to
    // take it; Riverwalkers pay more to unlock it.)
    public bool IsOthersHome(Faction faction, Terrain terrain) =>
        Players.Any(player => player.Faction != faction && player.Home == terrain);

    // The terrains (colours) already taken by the factions in the game: base home terrains and chosen starting
    // terrains. One faction per colour.
    public IEnumerable<Terrain> TakenTerrains(Faction? except = null) =>
        Players.Where(player => player.Faction != except)
            .Select(player => player.Kind == FactionKind.Base ? player.Home : player.StartingTerrain)
            .OfType<Terrain>();

    // state[Faction.Giants] reads the Giants' player state. Throws if the Giants are not in the game.
    public PlayerState this[Faction faction] => Players.First(player => player.Faction == faction);

    // Returns a NEW GameState with that faction's PlayerState replaced; the original is untouched.
    public GameState WithPlayer(PlayerState updated)
    {
        var index = Players.ToList().FindIndex(player => player.Faction == updated.Faction);
        return this with { Players = Players.SetItem(index, updated) };
    }

    public bool HasPassed(Faction faction) => PassOrder.Contains(faction);

    public bool IsTurnOrderComplete => Players.Count > 0 && TurnOrder.Count == Players.Count;

    // Calculator (one faction): as many as a two-player game, at least.
    public int PassTilesNeeded => (Options.Calculator ? Math.Max(2, Players.Count) : Players.Count) + Config.ExtraPassTiles + (IsInGame(Faction.Archivists) ? Config.ArchivistsExtraPassTiles : 0);

    // How far this faction's builds and transforms reach across rivers right now: its own shipping,
    // plus its pass tile's bonus during the main phase.
    // Factions without shipping (Fakirs, Dwarves) reach no further with it, whatever their pass tile says.
    public int ShippingOf(Faction faction)
    {
        if (!Factions.HasShipping(faction))
            return 0;
        var player = this[faction];
        var bonus = Phase != Phase.Playing ? 0
            : (player.PassTile is int tile ? PassTiles.All[tile].ShippingBonus : 0)
              + (player.ExtraPassTile is int extra ? PassTiles.All[extra].ShippingBonus : 0);
        return player.Shipping + bonus + (ShippingBoost && faction == Faction.Selkies ? Config.SelkiesShippingBonus : 0);
    }

    // The factions on this step of this track, in the order they arrived there (first arrival first).
    // Only for showing them: the rules never read the order.
    public ValueList<Faction> OnStep(Track track, int step) =>
        new(TrackArrivals[(int)track].Where(faction => IsInGame(faction) && this[faction].TrackSteps[(int)track] == step));

    public int BridgesLeft(Faction faction) => Config.BridgesPerPlayer - Bridges.Count(bridge => bridge.Owner == faction);

    // The Fakirs' flying carpet: how many hexes it may skip. 1, +1 with their stronghold, +1 for every key token
    // they took that would have upgraded shipping. 0 for every other faction.
    public int CarpetRange(Faction faction)
    {
        if (faction != Faction.Fakirs || !IsInGame(faction))
            return 0;
        var keys = PlacedKeys.Count(key => key.Owner == faction && KeyTiles.All[key.Tile.Id].ShippingUpgrade);
        return Config.FakirsCarpetRange + (this[faction].StrongholdBuilt ? 1 : 0) + keys;
    }

    // The active player is in the middle of spending free spades: some are left, or (main phase) they
    // have spent them all, or transformed a hex to home terrain, and may still build on one of the hexes.
    // Ends with a build or Done.
    public bool SpadesPending =>
        Phase is Phase.Playing or Phase.RoundEndSpades
        && ActivePlayer is Faction active
        && (this[active].SpadesOwed > 0 || SpadedHexes.Count > 0);

    // The tile whose "during the round" scoring is active: this round's, during the main phase (Playing)
    // only. Null at any other time, so nothing done between rounds (round-end transforms) scores it.
    // Its end-of-round reward is separate (see Rules.RoundEndRewards).
    public RoundTileDefinition? CurrentRoundTile =>
        Phase == Phase.Playing && Round >= 1 && Round <= ChosenRoundTiles.Count
            ? Core.RoundTiles.All[ChosenRoundTiles[Round - 1]]
            : null;

    // The active player first (even if they have just passed: it stays their turn until End turn),
    // then everyone else still in this round, in the order they will act.
    // Together with PassOrder this is the whole table, top to bottom. Empty when nobody is acting.
    public ValueList<Faction> TurnQueue
    {
        get
        {
            if (ActivePlayer is not Faction active)
                return ValueList<Faction>.Empty;
            var start = TurnOrder.IndexOf(active);
            var others = Enumerable.Range(1, TurnOrder.Count - 1)
                .Select(offset => TurnOrder[(start + offset) % TurnOrder.Count])
                .Where(faction => !HasPassed(faction));
            return new ValueList<Faction>(others.Prepend(active));
        }
    }

    // The hex of this faction's special landscape, if it has placed it (Landscapes expansion).
    public int? LandscapeHex(Faction faction)
    {
        for (var hex = 0; hex < Map.Count; hex++)
            if (Map[hex] is { Kind: PieceKind.Landscape } piece && piece.Owner == faction)
                return hex;
        return null;
    }

    public bool HasLandscape(Faction faction) => LandscapeHex(faction) is not null;

    // Every hex holding one of this faction's structures: its buildings and its special landscape.
    public IEnumerable<int> HexesBuiltOnBy(Faction faction) =>
        Enumerable.Range(0, Map.Count).Where(hex => Map[hex] is { } piece && piece.Owner == faction);
}
