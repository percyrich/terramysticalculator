namespace Core;

// The Special Landscapes expansion (GameOptions.Landscapes; Landscapes has the names, prerequisites and numbers).
// A special landscape is a structure with power 1 (PieceKind.Landscape on the map): it counts for reach, towns,
// neighbours' power both ways, networks and the final scoring tiles, but is none of the building kinds and is never
// upgraded. Placing one (PlaceLandscape), what it offers at once (LandscapeOffers), and the faction abilities it brings
// that live here. The others are where they act: SpadesFor (Halflings), ReachOf (Dwarves), PayForReach (Fakirs),
// TrackRewards (Auren), ApplyPayCultSteps (Acolytes), BeginRound (Dragonlords), Upgrade (Ice Maidens), FormTown
// (Witches, Mermaids), FormableTowns (Swarmlings), BridgeSpots (Engineers), NeighboursGainPower (Nomads),
// EndGame.Preview (Cultists). Setting one up directly (EditLandscape) is in Rules.EditPosition.cs.
public static partial class Rules
{
    // ---------- Placing ----------

    private static MoveResult ApplyPlaceLandscape(GameState state, PlaceLandscape move)
    {
        var faction = move.Player;
        var hex = move.Hex;
        if (!state.Options.Landscapes)
            return MoveResult.Rejected(state, "The special landscapes are not in this game.");
        if (!state.IsInGame(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} are not in the game.");
        var player = state[faction];
        var landscape = Landscapes.Name(faction);
        if (player.LandscapeOffers.HasFlag(LandscapeOffer.Portal))
            return PlacePortal(state, faction, hex);
        if (state.Phase == Phase.RoundEndSpades)
            return MoveResult.Rejected(state, "A special landscape can't be placed with spades from the end of a round.");
        if (CheckTurn(state, faction) is { } refusal)
            return refusal;
        if (state.HasLandscape(faction))
            return MoveResult.Rejected(state, $"The {Name(faction)} have already placed their {landscape}.");
        if (faction == Faction.ChaosMagicians)
            return MoveResult.Rejected(state, $"The {Name(faction)} place their {landscape} only as their stronghold is built.");
        if (PrerequisiteMissing(state, faction) is string missing)
            return MoveResult.Rejected(state, missing);
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.IsOccupied(hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        if (state.PlacedKeys.Any(key => key.Hex == hex))
            return MoveResult.Rejected(state, "A key lies on that hex.");
        if (Landscapes.OnRiver(faction))
            return PlaceOnRiver(state, faction, hex);
        if (state.HexTerrain[hex] == Terrain.River)
            return MoveResult.Rejected(state, $"The {Name(faction)}' {landscape} goes on land.");
        if (player.BuildTerrain is not Terrain build)
            return MoveResult.Rejected(state, $"The {Name(faction)} have no home terrain yet.");
        var spending = state.SpadesPending && state.ActivePlayer == faction;
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        GameState next;

        // A hex just transformed into the faction's terrain (a transform, free spades or the sandstorm): laid instead of
        // the dwelling that could now be built there.
        if (spending && state.SpadedHexes.Contains(hex))
        {
            if (!player.CanBuildOn(state.HexTerrain[hex]))
                return MoveResult.Rejected(state, $"The {landscape} can only go on a hex just made {build}.");
            next = Landed(state with { LandscapeBatchHex = hex }, faction, hex, events, warnings);
            if (faction == Faction.Acolytes)
                next = AltarInsteadOfVolcano(next, faction, events);
            if (next[faction].SpadesOwed == 0)
                next = EndSpadingIfNothingLeft(next, faction, events, warnings);
            return MoveResult.Ok(next, events, warnings);
        }
        if (player.CanBuildOn(state.HexTerrain[hex]))
            return MoveResult.Rejected(state,
                $"The {landscape} is laid instead of a terrain tile: only a hex being transformed into {build} can take it.");

        // With free spades: spent as SpendSpades would, into the faction's terrain.
        if (spending && player.SpadesOwed > 0)
        {
            var spent = ApplySpendSpades(state, new SpendSpades(faction, hex, build));
            if (!spent.Accepted)
                return spent;
            events.AddRange(spent.Events);
            warnings.AddRange(spent.Issues);
            next = Landed(spent.State with { LandscapeBatchHex = hex }, faction, hex, events, warnings);
            if (next.SpadesPending && next[faction].SpadesOwed == 0)
                next = EndSpadingIfNothingLeft(next, faction, events, warnings);
            return MoveResult.Ok(next, events, warnings);
        }

        // As the turn's action: the transform paid as usual (the Acolytes 3 steps whatever the terrain).
        if (CheckAction(state, faction) is { } actionRefusal)
            return actionRefusal;
        if (CheckHexTarget(state, faction, hex, allowSkip: true, out var reach) is { } hexRefusal)
            return hexRefusal;
        if (CheckTransformTo(state, faction, hex, build) is { } terrainRefusal)
            return terrainRefusal;
        if (faction == Faction.Dragonlords && CheckVolcanoPrice(state, faction, hex) is { } priceRefusal)
            return priceRefusal;
        if (faction == Faction.Acolytes && TracksThatCanPay(state, faction, Landscapes.AcolytesSteps).Count == 0)
            return MoveResult.Rejected(state, $"The {Name(faction)} need {Landscapes.AcolytesSteps} steps on one track to pay for their {landscape}.");

        next = PayForReach(state, faction, hex, reach, events, warnings);
        if (faction == Faction.Acolytes)
        {
            next = ChangeTerrain(next, hex, Terrain.Volcano, events);
            next = next.WithPlayer(next[faction] with
            {
                CultStepsToPay = next[faction].CultStepsToPay + Landscapes.AcolytesSteps,
                AltarStepsToPay = true,
            });
        }
        else if (faction == Faction.Dragonlords)
            next = MakeVolcano(next, faction, hex, events);
        else
            next = Transform(next, faction, hex, build, freeSpades: 0, events, warnings);
        next = Landed(next with { ActionTaken = true }, faction, hex, events, warnings);
        warnings.AddRange(DebtWarning(next[faction]));
        return MoveResult.Ok(next, events, warnings);
    }

    // The Acolytes' Altar laid on a hex just made Volcano: it costs 3 steps whatever the terrain. Steps still owed for
    // that transform pay for the Altar (no more than 3); steps already paid beyond 3 come back to the track they were
    // paid from (no power for them).
    private static GameState AltarInsteadOfVolcano(GameState state, Faction faction, List<GameEvent> events)
    {
        var player = state[faction];
        if (player.CultStepsToPay > 0)
            return state.WithPlayer(player with
            {
                CultStepsToPay = Math.Min(player.CultStepsToPay, Landscapes.AcolytesSteps),
                AltarStepsToPay = true,
            });
        if (player.VolcanoStepsPaid is not { } paid)
            return state;
        state = state.WithPlayer(player with { VolcanoStepsPaid = null });
        if (paid.Steps <= Landscapes.AcolytesSteps)
            return state;
        var before = state[faction].TrackSteps[(int)paid.Track];
        state = ClimbTrack(state, faction, paid.Track, paid.Steps - Landscapes.AcolytesSteps, events, out var after);
        events.Add(new TrackAdvanced(faction, paid.Track, before, after));
        return state;
    }

    // Witches with Raven's Blunder: the town bonus chosen, 5 VP or 3 steps on a track (with the power the track gives).
    private static MoveResult ApplyChooseTownBonus(GameState state, ChooseTownBonus move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.WitchesTownBonusHex is not int hex)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no town bonus to choose.");
        var events = new List<GameEvent>();
        var next = state.WithPlayer(player with { WitchesTownBonusHex = null });
        if (move.Track is Track track)
        {
            var before = next[move.Player].TrackSteps[(int)track];
            next = ClimbTrack(next, move.Player, track, Landscapes.WitchesTownSteps, events, out var after, mayBorrow: false); // the town's own reward
            events.Insert(0, new TrackAdvanced(move.Player, track, before, after));
            next = TrackRewards(next, move.Player, before, after, events);
        }
        else
            next = GainVictoryPoints(next, move.Player, Factions.TownVictoryPoints(move.Player), events,
                new PointsArea.Portrait(), new PointsArea.KeyOn(hex));
        return MoveResult.Ok(next, events, MoveResult.NoIssues);
    }

    // The Mermaids' and Riverwalkers': a river hex next to one of their structures (not by a bridge), for 1 spade
    // (Mermaids; a free one while spending them) or 1 priest (Riverwalkers).
    private static MoveResult PlaceOnRiver(GameState state, Faction faction, int hex)
    {
        var landscape = Landscapes.Name(faction);
        if (state.HexTerrain[hex] != Terrain.River)
            return MoveResult.Rejected(state, $"The {Name(faction)}' {landscape} goes on a river hex.");
        if (!state.Layout.Neighbours(hex).Any(n => OwnsBuildingAt(state, faction, n)))
            return MoveResult.Rejected(state, $"The {landscape} must go next to one of the {Name(faction)}' structures.");
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        GameState next;
        if (faction == Faction.Mermaids && state.SpadesPending && state.ActivePlayer == faction && state[faction].SpadesOwed > 0)
        {
            events.Add(new SpadesSpent(faction, hex, Terrain.River, Terrain.River, Landscapes.MermaidsSpades, 0));
            next = state.WithPlayer(state[faction] with { SpadesOwed = state[faction].SpadesOwed - Landscapes.MermaidsSpades })
                with { SpadedHexes = state.SpadedHexes.Add(hex), LandscapeBatchHex = hex };
            next = ScoreSpades(next, faction, hex, Landscapes.MermaidsSpades, events);
            next = Landed(next, faction, hex, events, warnings);
            if (next[faction].SpadesOwed == 0)
                next = EndSpadingIfNothingLeft(next, faction, events, warnings);
            return MoveResult.Ok(next, events, warnings);
        }
        if (CheckAction(state, faction) is { } refusal)
            return refusal;
        if (faction == Faction.Mermaids)
        {
            next = PaySpades(state, faction, hex, Landscapes.MermaidsSpades, events, warnings);
            next = ScoreSpades(next, faction, hex, Landscapes.MermaidsSpades, events);
        }
        else
            next = PayPriest(state, faction, events, warnings);
        next = Landed(next with { ActionTaken = true }, faction, hex, events, warnings);
        warnings.AddRange(DebtWarning(next[faction]));
        return MoveResult.Ok(next, events, warnings);
    }

    // The round tile's VP for spades used without a terrain changing (the Coral Reef's spade).
    private static GameState ScoreSpades(GameState state, Faction faction, int hex, int spades, List<GameEvent> events) =>
        GainVictoryPoints(state, faction, spades * RoundTilePoints(state, faction, tile => tile.VictoryPointsPerSpade), events,
            new PointsArea.RoundTile(), new PointsArea.OnHex(hex));

    // The Chaos Magicians' portal, offered as their stronghold is built: on any free Wasteland hex, for nothing.
    private static MoveResult PlacePortal(GameState state, Faction faction, int hex)
    {
        if (CheckTurn(state, faction) is { } refusal)
            return refusal;
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.HexTerrain[hex] != Terrain.Wasteland)
            return MoveResult.Rejected(state, $"The {Landscapes.Name(faction)} goes on a Wasteland hex.");
        if (state.IsOccupied(hex))
            return MoveResult.Rejected(state, "That hex is already occupied.");
        var events = new List<GameEvent>();
        var warnings = new List<Issue>();
        var next = state.WithPlayer(state[faction] with { LandscapeOffers = state[faction].LandscapeOffers & ~LandscapeOffer.Portal });
        next = Landed(next, faction, hex, events, warnings);
        return MoveResult.Ok(next, events, warnings);
    }

    // The landscape is on the hex (already paid for): neighbours are offered power, and what the faction gains at once.
    private static GameState Landed(GameState state, Faction faction, int hex, List<GameEvent> events, List<Issue> warnings)
    {
        var piece = new Piece(faction, PieceKind.Landscape, 0);
        var next = state with { Map = state.Map.SetItem(hex, piece) };
        events.Add(new LandscapePlaced(piece, hex));
        next = NeighboursGainPower(next, faction, hex, events);
        var here = new PointsArea.Landscape(hex);
        var player = next[faction];
        switch (faction)
        {
            case Faction.Darklings: // the Camp counts as a dwelling for the tiles that score dwellings
                next = GainVictoryPoints(next, faction, RoundTilePoints(next, faction, tile => tile.VictoryPointsFor(PieceKind.Dwelling)), events,
                    new PointsArea.RoundTile(), here);
                next = FavourPoints(next, faction, f => f.VictoryPointsPerDwelling, hex, events);
                break;
            case Faction.Alchemists:
                var upgrades = LandscapeOffer.None;
                if (Factions.HasShipping(faction) && player.Shipping < Factions.MaxShipping(faction))
                    upgrades |= LandscapeOffer.Shipping;
                if (player.SpadeUpgrades < Factions.MaxSpadeUpgrades(faction))
                    upgrades |= LandscapeOffer.Spades;
                next = Offer(next, faction, upgrades, events);
                break;
            case Faction.Giants when player.SpadeUpgrades < Factions.MaxSpadeUpgrades(faction):
                next = next.WithPlayer(player with { SpadeUpgrades = player.SpadeUpgrades + 1 });
                next = GainVictoryPoints(next, faction, Factions.SpadeUpgradeVictoryPoints(faction), events, new PointsArea.Spades(), here);
                break;
            case Faction.Yetis:
                next = AddTokens(next, faction, 3, Landscapes.YetisTokens, events);
                break;
            case Faction.Shapeshifters when ShapeshiftTargets(next, faction).Count > 0:
                next = Offer(next, faction, LandscapeOffer.Shapeshift, events);
                break;
        }
        return next;
    }

    private static GameState Offer(GameState state, Faction faction, LandscapeOffer offer, List<GameEvent> events)
    {
        if (offer == LandscapeOffer.None)
            return state;
        events.Add(new LandscapeBonusOffered(faction, offer));
        return state.WithPlayer(state[faction] with { LandscapeOffers = state[faction].LandscapeOffers | offer });
    }

    // The Chaos Magicians' stronghold built: their portal is offered, if it can go anywhere.
    private static GameState OfferPortal(GameState state, Faction faction, List<GameEvent> events)
    {
        if (!state.Options.Landscapes || state.HasLandscape(faction)
            || !Enumerable.Range(0, state.Layout.HexCount).Any(hex => state.HexTerrain[hex] == Terrain.Wasteland && !state.IsOccupied(hex)))
            return state;
        return Offer(state, faction, LandscapeOffer.Portal, events);
    }

    // Why the faction can't place its landscape yet (its prerequisite isn't on the map), or null.
    private static string? PrerequisiteMissing(GameState state, Faction faction)
    {
        if (Landscapes.Prerequisite(faction) is not { } needed)
            return null;
        var have = state.HexesBuiltOnBy(faction).Count(hex => state.Map[hex]!.Value.Kind == needed.Kind);
        return have >= needed.Count
            ? null
            : $"The {Name(faction)} need {Landscapes.PrerequisiteText(faction)} on the map before placing their {Landscapes.Name(faction)}.";
    }

    // Setup's check: a higher building of the same line counts (a sanctuary was a temple; a temple or stronghold was a
    // trading post).
    private static bool PrerequisiteMetInSetup(GameState state, Faction faction)
    {
        if (Landscapes.Prerequisite(faction) is not { } needed)
            return true;
        var kinds = state.HexesBuiltOnBy(faction).Select(hex => state.Map[hex]!.Value.Kind).ToList();
        bool Above(PieceKind kind, PieceKind wanted) =>
            kind == wanted || (Buildings.UpgradedFrom(kind) is PieceKind from && Above(from, wanted));
        return kinds.Count(kind => kind != PieceKind.Landscape && Above(kind, needed.Kind)) >= needed.Count;
    }

    // While free spades are being spent in the main phase: the hexes just transformed that could take the faction's
    // landscape instead of a dwelling.
    public static ValueList<int> SpadedLandscapeTargets(GameState state, Faction faction)
    {
        if (!state.Options.Landscapes || state.Phase != Phase.Playing || !state.IsInGame(faction) || state.HasLandscape(faction)
            || faction == Faction.ChaosMagicians || Landscapes.OnRiver(faction) || PrerequisiteMissing(state, faction) is not null)
            return ValueList<int>.Empty;
        return new(state.SpadedHexes.Where(hex => !state.IsOccupied(hex) && state[faction].CanBuildOn(state.HexTerrain[hex])));
    }

    // Something can still go on a hex just transformed: a dwelling, or the landscape.
    private static bool SpadingOpen(GameState state, Faction faction) =>
        SpadedBuildTargets(state, faction).Count > 0 || SpadedLandscapeTargets(state, faction).Count > 0;

    // ---------- What the landscape offers ----------

    private static MoveResult ApplyDeclineLandscapeBonus(GameState state, DeclineLandscapeBonus move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        if (state[move.Player].LandscapeOffers == LandscapeOffer.None)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have nothing from their landscape to decline.");
        var next = state.WithPlayer(state[move.Player] with { LandscapeOffers = LandscapeOffer.None });
        return MoveResult.Ok(next);
    }

    // Shapeshifters: the Spectral Polestar's free move of their home terrain.
    private static MoveResult ApplyLandscapeShapeshift(GameState state, LandscapeShapeshift move)
    {
        if (CheckTurn(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (!player.LandscapeOffers.HasFlag(LandscapeOffer.Shapeshift))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no free move of their home terrain.");
        if (!Terrains.IsBasic(move.To))
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' home must be one of the seven basic terrains, not {move.To}.");
        if (state.Players.FirstOrDefault(other => other.Home == move.To) is PlayerState owner)
            return MoveResult.Rejected(state, owner.Faction == move.Player
                ? $"{move.To} already is the {Name(move.Player)}' home terrain."
                : $"{move.To} is the {Name(owner.Faction)}' home terrain.");
        var next = state.WithPlayer(player with
        {
            ShapeshifterHome = move.To,
            LandscapeOffers = player.LandscapeOffers & ~LandscapeOffer.Shapeshift,
        });
        return MoveResult.Ok(next);
    }

    // Alchemists: the Gloomy Workshop's shipping upgrade, without the priest and not an action.
    private static MoveResult LandscapeShippingUpgrade(GameState state, Faction faction)
    {
        if (CheckTurn(state, faction) is { } refusal)
            return refusal;
        var player = state[faction];
        var events = new List<GameEvent>();
        var upgradesBefore = player.Shipping - Factions.StartingShipping(faction);
        var paid = player.Pay(Factions.ShippingUpgradeCost(faction)) with
        {
            Shipping = player.Shipping + 1,
            LandscapeOffers = player.LandscapeOffers & ~LandscapeOffer.Shipping,
        };
        events.Add(new ShippingUpgraded(faction, paid.Shipping));
        var next = GainVictoryPoints(state.WithPlayer(paid), faction, Config.ShippingUpgradeVictoryPoints(upgradesBefore), events,
            new PointsArea.Shipping(), new PointsArea.Shipping());
        return MoveResult.Ok(next, events, DebtWarning(paid));
    }

    // Alchemists: the Gloomy Workshop's spade upgrade, without the priest and not an action.
    private static MoveResult LandscapeSpadeUpgrade(GameState state, Faction faction)
    {
        if (CheckTurn(state, faction) is { } refusal)
            return refusal;
        var player = state[faction];
        var events = new List<GameEvent>();
        var paid = player.Pay(Factions.SpadeUpgradeCost(faction)) with
        {
            SpadeUpgrades = player.SpadeUpgrades + 1,
            LandscapeOffers = player.LandscapeOffers & ~LandscapeOffer.Spades,
        };
        var next = GainVictoryPoints(state.WithPlayer(paid), faction, Factions.SpadeUpgradeVictoryPoints(faction), events,
            new PointsArea.Spades(), new PointsArea.Spades());
        return MoveResult.Ok(next, events, DebtWarning(paid));
    }

    // ---------- Nomads: the Trading Camp ----------

    // A build on this hex is next to the Nomads' Trading Camp (as power offers count adjacency: their bridges too).
    private static bool CampNextTo(GameState state, Faction faction, int hex) =>
        faction == Faction.Nomads && state.LandscapeHex(faction) is int camp && AdjacentHexes(state, faction, hex).Contains(camp);

    // The Nomads answer a power offer from a build next to their Trading Camp with exactly 1 coin (no VP paid).
    private static MoveResult ApplyTakeCoinInstead(GameState state, TakeCoinInstead move)
    {
        var player = move.Player;
        if (state.PowerOffers.Count == 0 || state.PowerOffers.All(offer => offer.Player != player))
            return MoveResult.Rejected(state, $"The {Name(player)} have no power offer to answer.");
        var first = state.PowerOffers[0];
        if (first.Player != player)
            return MoveResult.Rejected(state, $"The {Name(first.Player)} answer their power offer first.");
        if (!CampNextTo(state, player, first.Hex))
            return MoveResult.Rejected(state, "Only a build next to the Nomads' Trading Camp can be answered with a coin.");
        var events = new List<GameEvent>();
        var coins = state[player].Coins + 1;
        var next = state.WithPlayer(state[player] with { Coins = coins }) with { PowerOffers = state.PowerOffers.RemoveAt(0) };
        return MoveResult.Ok(ResolveCultists(AcceptFreeOffers(next, events), events), events, MoveResult.NoIssues);
    }

    // ---------- Other abilities ----------

    // Spades this faction needs to turn the hex into `to` (PlayerState.SpadesNeeded), 1 fewer (at least 1) for the
    // Halflings next to their Driller.
    public static int? SpadesFor(GameState state, Faction faction, int hex, Terrain to)
    {
        var spades = state[faction].SpadesNeeded(state.HexTerrain[hex], to);
        if (spades is int needed && needed > 1 && faction == Faction.Halflings
            && state.LandscapeHex(faction) is int driller && state.Layout.Neighbours(driller).Contains(hex))
            return Math.Max(1, needed - Landscapes.HalflingsSpadesSaved);
        return spades;
    }

    // Dwarves with their Great Tunnel: spades between rounds may tunnel (no workers paid).
    private static bool TunnelsBetweenRounds(GameState state, Faction faction) =>
        faction == Faction.Dwarves && state.HasLandscape(faction);

    // Engineers with their Bridge Tower: land bridges from it, along the edge between the two hexes at each of its
    // corners, to the hex beyond (on the map, not a river), whatever those two hexes are. HexA < HexB.
    public static IEnumerable<(int HexA, int HexB)> LandBridgeEdges(GameState state, Faction faction)
    {
        if (faction != Faction.Engineers || !state.IsInGame(faction) || state.LandscapeHex(faction) is not int tower)
            yield break;
        var around = state.Layout.Neighbours(tower).ToList();
        foreach (var a in around)
            foreach (var b in around)
            {
                if (a >= b || !state.Layout.Neighbours(a).Contains(b))
                    continue;
                var beyond = state.Layout.Neighbours(a).Where(n => n != tower && state.Layout.Neighbours(b).Contains(n)).ToList();
                if (beyond.Count == 1 && state.HexTerrain[beyond[0]] != Terrain.River)
                    yield return (Math.Min(tower, beyond[0]), Math.Max(tower, beyond[0]));
            }
    }

    // Acolytes with their Altar: power for every reward step left on the way down a track.
    private static GameState DescentRewards(GameState state, Faction faction, int before, int after, List<GameEvent> events)
    {
        foreach (var (step, power) in Config.TrackPowerRewards)
            if (after < step && step <= before)
                state = GainPower(state, faction, power, PowerSource.Track, events);
        return state;
    }

    // Swarmlings with their Breeding Ground: the town holding it takes another key (city, metropolis, megacity) once its
    // buildings' power reaches the town power times one more than the keys it holds (7: 14, 21, 28).
    private static bool CityGrows(GameState state, Faction faction, IReadOnlyCollection<int> group, int powerNeeded)
    {
        if (faction != Faction.Swarmlings || state.LandscapeHex(faction) is not int ground || !group.Contains(ground))
            return false;
        var keys = state.PlacedKeys.Count(key => key.Owner == faction && group.Contains(key.Hex));
        var power = group.Sum(hex => Factions.BuildingPower(faction, state.Map[hex]!.Value.Kind));
        return keys >= 1 && keys < Landscapes.SwarmlingsMaxKeys && power >= powerNeeded * (keys + 1);
    }

    // Setup: a Swarmlings' group with their Breeding Ground may hold up to 4 keys, each with power for it.
    private static bool CityKeysFit(GameState state, Faction faction, IReadOnlyCollection<int> group, int keys)
    {
        if (faction != Faction.Swarmlings || state.LandscapeHex(faction) is not int ground || !group.Contains(ground)
            || keys < 2 || keys > Landscapes.SwarmlingsMaxKeys)
            return false;
        var buildings = BuildingsIn(state, group);
        var needed = TownPowerNeeded(state, faction);
        var power = buildings.Sum(hex => Factions.BuildingPower(faction, state.Map[hex]!.Value.Kind));
        return IsTown(state, faction, buildings, needed) && power >= needed * keys;
    }
}
