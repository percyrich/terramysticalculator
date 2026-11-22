using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// Converter, continued: play: every BGA notification, in order, turned into moves.
public sealed partial class Converter
{
    // ---------- Play: every notification in order ----------

    private void Play()
    {
        var notes = _g.Notes;
        var packetStart = 0;
        for (_pos = 0; _pos < notes.Count && !_stopped; _pos++)
        {
            var n = notes[_pos];
            if (!_consumed.Contains(n.Index))
                Handle(n);
            if (S.Phase != Phase.Finished)
                FlushDeferred();
            var packetEnds = _pos == notes.Count - 1 || notes[_pos + 1].Packet != n.Packet;
            if (packetEnds && !_stopped && S.Phase >= Phase.PlacingDwellings)
                Check(packetStart, _pos);
            if (packetEnds)
                packetStart = _pos + 1;
        }
    }

    // Notifications that need no move: setup (already done), and what Core does by itself (checked, not sent).
    private static readonly HashSet<string> NoMove = new()
    {
        "gameStateChange", "updateReflexionTime", "gameStateMultipleActiveUpdate", "updateCounters", "updateScore",
        "updateScoreSmallDelay", "advanceCultTrack", "incomePhase", "someIncome", "townBonus",
        "coinsOnBonusCards", "tileScored", "returnActionTokens", "finalScoring", "simpleNode", "conversionsAppliedWithDelay",
        "timeoutEndOfArenaSeason", "timeoutEndOfArenaSeasonMore", "factionBoardChosen", "factionBoardSwapped",
        "factionPositionSelected",
        "powerIncome",        // the Prospectors' power for a transform, which Core gives
        "updateExchangeTrack", // the Prospectors' golden spade price after their stronghold, which Core knows
        "undoRestoreDISAB",    // "takes back his move": the replay log already leaves the taken-back move out
        "undoRestorePoint",    // the same in a game in progress's log (notificationHistory.html, history=0)
        "addNewBonusCard",     // the Archivists' extra bonus card: read with the game data
        "newCustomBoard",      // a custom board: read with the game data
        "timeJokerUsed",       // more thinking time
        "wakeupPlayers", "cultBonus", // round-end cult rewards (Core gives them)
        "workersIncome",       // the Swarmlings' 3 workers for a town (Core gives them)
        "tokenPlaced", "favorTileLost", // read with the stronghold action they belong to (Wyrm, Conspirators)
        "startingTerrainChosen", // the volcano factions' terrain (read from factionBoardSwapped)
        "emptyTreasury",       // the Treasurers' Safe opening (Core does it)
        "simpleNote",          // "rematch", "the arena season has ended"
    };

    private void Handle(Note n)
    {
        if (S.Phase == Phase.Finished)
            return; // after the last turn BGA's own final conversions follow; Core's final scoring has done that
        if (_usedNotes.Contains(n.Index))
            return;
        FlushDeferred();
        if (_pyroLater is { } pyroLater && (n.Packet != pyroLater.Note.Packet || !NoMove.Contains(n.Type) && n.Type != "conversionsApplied"))
            FlushPyro(); // the conversion it waited for was not the next note: made now
        if (n.Type != "priestFromTerrainCycle" && !NoMove.Contains(n.Type))
            SettlePriestChoices(n);
        if (n.Type != "updateTreasury" && !NoMove.Contains(n.Type))
            SettleTreasury(n);
        switch (n.Type)
        {
            case "dwellingPlaced": WithPlayer(n, f => Dwelling(n, f)); break;
            case "terrainTransformed": WithPlayer(n, f => { Transformed(n, f); PayAcolyteSteps(n, f); }); break;
            case "landscapePlaced": WithPlayer(n, f => Landscape(n, f)); break;
            case "structureUpgraded": WithPlayer(n, f => Upgraded(n, f)); break;
            case "orderAPriest": WithPlayer(n, f => Priest(n, f)); break;
            case "favorTileChosen": WithPlayer(n, f => { ResolveOffers(); Do(new TakeFavour(f, Tables.Favour(n.Int("favor_type") ?? -1)), n); }); break;
            case "townFounded": WithPlayer(n, f => Town(n, f)); break;
            // The Kingdom of Ember: lavaAdded "place a new volcano" (terrain_bts: the structure's hex) → PlaceLava;
            // lavaFlow (terrains: from, then the path; bridges_bbs: a free bridge across a river crossed) → LetLavaFlow.
            case "lavaAdded": WithPlayer(n, f => { RoundEndSpender(f, n); Do(new PlaceLava(f, Hex(n.Str("terrain_bts"))), n); }); break;
            // The Geologists' spade on their wheel (the stronghold's is read with its actionUsed): "Markers → wasteland"
            // (a rock off), "plains → Markers" (a rock on) → MoveRock.
            case "terrainRingUpdated" when !n.Log.Contains("Stronghold"):
                WithPlayer(n, f =>
                {
                    if (RockTerrain(n) is not Terrain rock)
                    {
                        Stop(n, "A rock moved with no terrain named.");
                        return;
                    }
                    RoundEndSpender(f, n);
                    Do(new MoveRock(f, rock), n);
                });
                break;
            case "lavaFlow": WithPlayer(n, f => LavaFlow(n, f)); break;
            case "advanceShippingTrack" or "advanceExchangeTrack" when n.Log.Contains("Snow Shamans ability"):
                WithPlayer(n, f => ShamansStep(n, f)); break;
            case "advanceShippingTrack": WithPlayer(n, f => Shipping(n, f)); break;
            case "advanceExchangeTrack" when n.Amount("coins_cost") + n.Amount("workers_cost") + n.Amount("priests_cost") == 0
                && _g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
                    .Any(m => m.Type == "landscapePlaced" && m.Str("player_id") == n.Str("player_id"))
                && PlayerOf(n) is Faction byLandscape && !S[byLandscape].LandscapeOffers.HasFlag(LandscapeOffer.Spades):
                break; // a landscape's free spade step, which Core gave with PlaceLandscape
            case "advanceExchangeTrack": WithPlayer(n, f => { EnsureActive(f, !S[f].LandscapeOffers.HasFlag(LandscapeOffer.Spades), n); Do(new UpgradeSpades(f), n); }); break;
            case "powerActionConvert": // with spades_converted: the Prospectors' spades become priests, which Core does itself;
                // with power_moved: the Time Travellers' stronghold action, read with its actionUsed
                if (n.Args["spades_converted"] is null && n.Args["power_moved"] is null && !n.Log.Contains("(Stronghold Action)"))
                    WithPlayer(n, f => PowerAction(n, f));
                break;
            case "powerActionBridge": WithPlayer(n, f => Bridge(n, f)); break;
            case "specialActionUsed": WithPlayer(n, f => SpecialAction(n, f)); break;
            case "updateTreasury": SettleTreasury(n); break;
            case "swapCultTrack": // the Djinni's lamp
                WithPlayer(n, f => { EnsureActive(f, true, n); Do(new SwapTracks(f, Tables.ToTrack(n.Str("cult_id_1") ?? ""), Tables.ToTrack(n.Str("cult_id_2") ?? "")), n); });
                break;
            case "advanceIncomeTrack": // Chash Dallah
                WithPlayer(n, f => { EnsureActive(f, true, n); Do(new AdvanceIncomeTrack(f), n); });
                break;
            case "advanceCultTrack" when (n.Str("addendum") ?? "").Contains("Goblins ability"):
                // The Goblins' treasure for their stronghold and sanctuary: steps BGA puts on one track (token_cost).
                WithPlayer(n, f =>
                {
                    if (S[f].CultStepsOwed == 0)
                    {
                        EnsureActive(f, true, n);
                        var kind = S.HexesBuiltOnBy(f).Any(hex => S.Map[hex]!.Value.Kind == PieceKind.Stronghold) ? PieceKind.Stronghold : PieceKind.Sanctuary;
                        if (!Do(new SpendTreasure(f, kind), n))
                            return;
                    }
                    var track = Tables.ToTrack(n.Str("cult_id") ?? n.Str("cult_name") ?? "");
                    for (var i = 0; i < n.Amount("inc_value") && S[f].CultStepsOwed > 0 && !_stopped; i++)
                        Do(new TakeCultStep(f, track), n);
                });
                break;
            case "gainRessources": // the Goblins' treasure: power per dwelling, coins per trading post, workers per temple
                WithPlayer(n, f =>
                {
                    PieceKind? kind = n.Amount("power_gain") > 0 ? PieceKind.Dwelling : n.Amount("coins_gain") > 0 ? PieceKind.TradingPost
                        : n.Amount("workers_gain") > 0 ? (PieceKind?)PieceKind.Temple : null;
                    if (kind is not PieceKind k)
                    {
                        Stop(n, "A Goblins' treasure with a reward the converter doesn't read yet.");
                        return;
                    }
                    EnsureActive(f, true, n);
                    Do(new SpendTreasure(f, k), n);
                });
                break;
            case "shapeshifted": // the Shapeshifters' Spectral Polestar (their stronghold's is read with its actionUsed)
                WithPlayer(n, f =>
                {
                    var to = Tables.ToTerrain(n.Str("new_terrain") ?? "");
                    if (S[f].LandscapeOffers.HasFlag(LandscapeOffer.Shapeshift))
                    {
                        Do(new LandscapeShapeshift(f, to), n);
                        return;
                    }
                    // Their stronghold action (older logs have no actionUsed for it; newer ones have it after this note).
                    var used = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                        .FirstOrDefault(m => m.Type == "actionUsed" && m.Args["stronghold"] is not null);
                    if (used is not null)
                        _usedNotes.Add(used.Index);
                    EnsureActive(f, true, n);
                    Do(new BonusShapeshift(f, BonusSource.Stronghold, to, n.Args["removed_tokens"] is JsonArray removed && removed.Count > 0), n);
                });
                break;
            case "newPowerTokens": // the Shapeshifters pay 1 VP for a token in bowl 3 after an opponent took their power
                WithPlayer(n, f =>
                {
                    if (S[f].TokenOfferOwed)
                        Do(new AnswerTokenOffer(f, true), n);
                    else if (n.Log.Contains("Enlightened Ability")) // 1 coin a token in bowl 1, a free action (893445224 #226)
                    {
                        EnsureActive(f, StartsNextTurn(f, n), n);
                        // Short of coins: the conversions BGA logs after it in the packet paid for it (615866172 #100,
                        // 1 power to a coin, then 4 tokens with 4 coins): those first.
                        if (S[f].Coins < n.Amount("coins_spent")
                            && _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                                .FirstOrDefault(m => m.Type == "conversionsApplied" && m.Str("player_id") == n.Str("player_id")
                                    && !_usedNotes.Contains(m.Index)) is Note paying)
                        {
                            _usedNotes.Add(paying.Index);
                            Conversions(paying, f);
                        }
                        for (var i = 0; i < n.Amount("coins_spent") && !_stopped; i++)
                            Free(f, new BuyPowerToken(f), n);
                    }
                    else if (n.Log.Contains("Stronghold"))
                    {
                        // The Children of the Wyrm's stronghold tokens, held as a reward: taken where BGA gives them
                        // (717052298 #362, before the town).
                        if (S[f].TokensHeld > 0)
                        {
                            ResolveOffers();
                            Do(new TakeHeldPower(f), n);
                        }
                    }
                    else
                        Problem(n, $"BGA has the {Name(f)} take a power token, but Core offers them none");
                });
                break;
            case "powerIncome" when n.Log.Contains("Alchemists Stronghold"):
                // The Alchemists' 12 power, held as a reward after the action: taken where BGA gives it (241070597 #290,
                // after the town and a conversion). Their 2 power a spade, logged alike, Core gives with the spade.
                WithPlayer(n, f =>
                {
                    if (S[f].PowerHeld > 0 && n.Amount("power_income") == S[f].PowerHeld && S.ActivePlayer == f)
                    {
                        ResolveOffers();
                        Do(new TakeHeldPower(f), n);
                    }
                });
                break;
            case "priestFromTerrainCycle": // the Riverwalkers unlock a terrain with a priest gained
                WithPlayer(n, f =>
                {
                    var title = System.Text.RegularExpressions.Regex.Match(n.Str("terrain") ?? "", "title='([a-z]+)'").Groups[1].Value;
                    if (title.Length == 0)
                        Stop(n, "The Riverwalkers unlock a terrain BGA doesn't name.");
                    else
                        Do(new ChoosePriest(f, Tables.ToTerrain(title)), n);
                });
                break;
            case "actionUsed": // after a power action (read there), or {stronghold: true} for a stronghold action
                if (n.Args["stronghold"] is not null)
                    WithPlayer(n, f => StrongholdAction(n, f));
                else if (n.Int("action_id") is int actionId && !_g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
                             .Any(m => m.Type is "powerActionConvert" or "powerActionBridge"))
                {
                    // No power action note: the Treasurers' gains went into their Safe (updateTreasury "(Power action)").
                    var who = _g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
                        .FirstOrDefault(m => m.Type == "updateTreasury" || m.Type == "justAMessage" && m.Str("player_id") is not null);
                    if (who is not null && PlayerOf(who) is Faction actor)
                    {
                        EnsureActive(actor, true, n);
                        Do(new TakePowerAction(actor, Tables.PowerAction(actionId)), n);
                    }
                    else
                        Problem(n, $"a power action ({actionId}) with no player named");
                }
                break;
            case "playerPassed": case "firstPlayerChanged": WithPlayer(n, f => { EnsureActive(f, true, n); Do(new Pass(f), n); }); break;
            case "bonusCardChosen": WithPlayer(n, f =>
                {
                    ResolveOffers();
                    SettleStartingSteps();
                    if (_atlanteansTown is Note town && S.Phase == Phase.PickingPassTiles)
                    {
                        _atlanteansTown = null;
                        Town(town, Faction.Atlanteans);
                    }
                    if (!_stopped && Do(new PickPassTile(f, PassTileOf(n)), n)
                        && n.Int("bonus_id_2") is int second && second > 0) // the Archivists with their stronghold: two
                        Do(new PickPassTile(f, _passTiles.TryGetValue(second, out var tile) ? tile
                            : throw new InvalidDataException($"Bonus card {second} is not in the game data.")), n);
                });
                break;
            case "conversionsApplied": WithPlayer(n, f => Conversions(n, f)); break;
            case "powerViaStructures": Decision(n); break;
            case "updateCounters" when IsCoinInstead(n): Decision(n); break; // the Nomads' Trading Camp: 1 coin instead
            // "declines building a Dwelling for now": with no dwelling of theirs following, the spaded hex is given up
            // now (Done), so the conversions BGA logs next are made there, after the transform (803258738 #258–259:
            // 3 power to a worker with the Alchemists' 2 spade power).
            case "updateCounters" when n.Log.Contains("declines building a Dwelling"): WithPlayer(n, f => DeclineDwelling(n, f)); break;
            case "updateCounters" when n.Log.Contains("declines advancing on a Cult track"): // the Cultists' step given up
                WithPlayer(n, f =>
                {
                    ResolveOffers();
                    if (S[f].CultStepsOwed > 0)
                        Do(new DeclineCultStep(f), n);
                });
                break;
            case "workersForPriests": WithPlayer(n, f => { ResolveOffers(); Do(new ExchangeWorkersForPriests(f, n.Amount("priests_gain")), n); }); break;
            case "createLandBridges": ReadBridgeSpots(n.Args["board_bridge_spaces"] as JsonObject); break;
            case "justAMessage": Message(n); break;
            default:
                if (!NoMove.Contains(n.Type) && _unknownTypes.Add(n.Type))
                    Problem(n, $"notification type \"{n.Type}\" is not converted yet: {n}");
                break;
        }
    }

    private void Message(Note n)
    {
        var log = n.Log;
        if (log.Contains("sacrificed"))
            WithPlayer(n, f =>
            {
                EnsureActive(f, StartsNextTurn(f, n), n);
                // The Firewalkers' pyro power BGA logs after the burn in the same packet: it came first (it fills bowl 2).
                foreach (var pyro in _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                    .Where(m => m.Type == "justAMessage" && m.Log.Contains("moves their VP marker") && m.Args["vp"] is not null
                        && m.Str("player_id") == n.Str("player_id") && !_consumed.Contains(m.Index)).ToList())
                {
                    _consumed.Add(pyro.Index);
                    Pyro(f, pyro.Amount("vp"), pyro);
                }
                var burns = n.Args["sacrificed_number"] is not null ? n.Amount("sacrificed_number") : n.Amount("power_gained");
                for (var i = 0; i < burns && !_stopped; i++)
                    Free(f, new BurnPower(f), n);
            });
        else if (log.Contains("moves their VP marker") && n.Args["vp"] is not null) // the Firewalkers' pyro, a power each
            WithPlayer(n, f =>
            {
                EnsureActive(f, StartsNextTurn(f, n), n);
                Pyro(f, n.Amount("vp"), n);
            });
        else if (log.Contains("(Alchemists Ability)") && n.Args["vp_spent"] is not null)
            WithPlayer(n, f =>
            {
                EnsureActive(f, StartsNextTurn(f, n), n);
                for (var i = 0; i < n.Amount("vp_spent") && !_stopped; i++)
                    Free(f, new ConvertVictoryPointToCoin(f), n);
            });
        else if (log.Contains("declines getting Power via Structures"))
            Decision(n);
        else if (log.Contains("Cleanup phase") || log.Contains("Final scoring"))
            EndOfRound(n);
        else if (log.Contains("Income phase") || log.Contains("Action phase"))
            FinishRoundEndSpades(n);
    }

    private void Dwelling(Note n, Faction f)
    {
        var hex = Hex(n.Str("bts_id"));
        if (S.Phase == Phase.PlacingDwellings)
        {
            SettleStartingSteps();
            // The Atlanteans start with their stronghold ("places a Faction Stronghold").
            var kind = n.Str("struct_type") is string type ? Tables.ToBuilding(type) : PieceKind.Dwelling;
            if (!_stopped)
                Do(new PlaceStartingDwelling(Next(f, kind), hex), n);
            return;
        }
        ResolveOffers();
        if (S.ActivePlayer == f && (S[f].LakeDwellingOwed || S[f].IceMassesOwed is { Count: > 0 }))
        {
            Do(new PlaceFreeDwelling(Next(f, PieceKind.Dwelling), hex), n); // the Wisps' Lake, the Snow Shamans' Ice
            return;
        }
        if (S.ActivePlayer == f && S.SpadesPending && S.SpadedHexes.Contains(hex))
        {
            if (S[f].SpadesOwed > 0 && !Do(new FinishSpades(f), n)) // the spades not used are given up first
                return;
            Do(new BuildOnSpadedHex(f == Faction.Changelings ? ChangelingDwelling(n, hex, spaded: true) : Next(f, PieceKind.Dwelling), hex), n);
            return;
        }
        EnsureActive(f, true, n);
        Do(new BuildDwelling(f == Faction.Changelings ? ChangelingDwelling(n, hex) : Next(f, PieceKind.Dwelling), hex), n);
    }

    private void DeclineDwelling(Note n, Faction f)
    {
        if (S.Phase != Phase.Playing || S.ActivePlayer != f || !S.SpadesPending || S[f].SpadesOwed > 0)
            return;
        // "For now": a dwelling there later is a new action (713223885 #399: the Dwarves tunnel again; 198809215 #336:
        // the Witches' Ride). In all 253 samples none follows in the same turn.
        Do(new FinishSpades(f), n);
    }

    private void Transformed(Note n, Faction f)
    {
        var hex = Hex(n.Str("bts_id"));
        var to = Tables.ToTerrain(n.Str("terrain_dest") ?? "");
        if (S.Phase == Phase.PlacingDwellings)
            return; // a volcano faction's starting spot: PlaceStartingDwelling makes the Volcano
        if (n.Log.Contains("places his Special Landscape"))
        {
            // The landscape itself (the Mermaids' on a river): landscapePlaced follows. With a pass tile's spade, that
            // action is taken now.
            var spade = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .FirstOrDefault(m => m.Type == "specialActionUsed" && m.Str("player_id") == n.Str("player_id"));
            if (spade is not null)
            {
                _usedNotes.Add(spade.Index);
                ResolveOffers();
                EnsureActive(f, true, n);
                Do(new BonusSpades(f, SourceOf(spade)), n);
            }
            return;
        }
        // The Architects' bridge discount comes only with the build: their dwelling on this hex (same packet) is sent as
        // BuildDwelling, which transforms too.
        if (f == Faction.Architects && S[f].SpadesOwed == 0
            && !_g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet).Any(m => m.Type == "specialActionUsed")
            && _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .Any(m => m.Type == "dwellingPlaced" && m.Str("player_id") == n.Str("player_id") && m.Str("bts_id") == n.Str("bts_id")))
            return;
        if (S.Phase == Phase.RoundEndSpades)
        {
            SpenderIs(f, n);
            Do(new SpendSpades(f, hex, to), n);
            return;
        }
        ResolveOffers();
        if (S.Phase == Phase.Playing && S.ActivePlayer == f && S[f].SpadesOwed > 0)
        {
            Do(new SpendSpades(f, hex, to), n);
            return;
        }
        // A pass tile's spade (or a favour's): BGA logs the transform, then specialActionUsed, in one packet.
        var special = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
            .FirstOrDefault(m => m.Type == "specialActionUsed" && m.Str("player_id") == n.Str("player_id"));
        EnsureActive(f, true, n);
        if (special is not null)
        {
            _usedNotes.Add(special.Index);
            Do(new BonusSpades(f, SourceOf(special)), n);
            if (!_stopped)
                Do(new SpendSpades(f, hex, to), n);
            return;
        }
        Do(new TransformTerrain(f, hex, to), n);
    }

    // The Acolytes pay a Volcano with cult steps: BGA's spent_cults names the track.
    private void PayAcolyteSteps(Note n, Faction f)
    {
        if (_stopped || S[f].CultStepsToPay == 0 || n.Args["spent_cults"] is not JsonObject spent)
            return;
        foreach (var (key, value) in spent)
            if (J.Int(value) is > 0 && S[f].CultStepsToPay > 0)
                Do(new PayCultSteps(f, Tables.ToTrack(key.Replace("spent_", "").Replace("cult", ""))), n);
    }

    private void Landscape(Note n, Faction f)
    {
        var hex = Hex(n.Str("bts_id"));
        ResolveOffers();
        // Laid on a hex just transformed (or with free spades) it is part of that action; otherwise it is the action.
        if (!(S.ActivePlayer == f && S.SpadesPending))
            EnsureActive(f, !S[f].LandscapeOffers.HasFlag(LandscapeOffer.Portal), n);
        Do(new PlaceLandscape(f, hex), n);
    }

    private void Upgraded(Note n, Faction f)
    {
        var kind = Tables.ToBuilding(J.Str(n.Args["new_struct"]?["struct_type"]) ?? "");
        var hex = Hex(J.Str(n.Args["new_struct"]?["bts_id"]));
        EnsureActive(f, true, n);
        // The Changelings with their stronghold may put the dwelling back beside it: BGA's old_struct then has
        // struct_order 9 (941433855 #173).
        var beside = f == Faction.Changelings && J.Str(n.Args["old_struct"]?["struct_type"]) == "dwelling"
            && J.Str(n.Args["old_struct"]?["struct_order"]) == "9";
        Do(new UpgradeBuilding(Next(f, kind), hex, beside), n);
    }

    private void Priest(Note n, Faction f)
    {
        var track = Tables.ToTrack(n.Str("cult_id") ?? n.Str("cult_name") ?? "");
        var slot = Tables.PriestSlot(n.Int("order_space_id") ?? -1);
        EnsureActive(f, true, n);
        if (S[f].TopPriest is not Piece priest)
        {
            Stop(n, $"BGA sends a priest of the {Factions.Name(f)} to a cult, but in Core they have none in hand.");
            return;
        }
        Do(new PlacePriest(priest, track, slot), n);
    }

    private void Town(Note n, Faction f)
    {
        ResolveOffers(); // a town waits for the action's power offers (BGA often forms it first)
        var townId = n.Int("town_id") ?? -1;
        if (!_keyTiles.TryGetValue(townId, out var key))
        {
            Stop(n, $"Town tile {townId} is not in the game data.");
            return;
        }
        if (f == Faction.Atlanteans && S.Phase == Phase.PlacingDwellings)
        {
            _atlanteansTown = n; // their starting town: Core takes it once the starting dwellings are all down
            return;
        }
        // A Mermaids' river town taken at the start of their turn (BGA state 414, 894202965 #263): their turn first.
        if (S.Phase == Phase.Playing && S.ActivePlayer != f)
            EnsureActive(f, false, n);
        var form = new FormTown(f, key, KeyHex(f, Hex(n.Str("bts_id"))));
        // Older BGA tables counted the Mermaids' Coral Reef as an ordinary structure in a town, the key on land
        // (117377020 #336, 117525907 #328). Core's ruling: a town through the Reef has its key on the Reef. If it may go
        // there, it goes there, with a warning. (A key on another river hex of such a town, 129565288 and 134295961,
        // can't be rescued: BGA did not yet keep keys off river hexes other than the Reef in a town holding it.)
        if (f == Faction.Mermaids && !Rules.Apply(S, form).Accepted && S.LandscapeHex(f) is int reef && reef != form.Hex
            && Rules.Apply(S, form with { Hex = reef }).Accepted)
        {
            Problem(n, $"BGA puts the Mermaids' key on {GameLog.HexName(S, form.Hex)}, in a town joined through their Coral Reef "
                + $"(older BGA counted the Reef as an ordinary structure): the key goes on the Reef ({GameLog.HexName(S, reef)}) instead");
            form = form with { Hex = reef };
        }
        if (!Do(form, n))
            return;
        TownTrackSteps(n, f);
        if (S[f].WitchesTownBonusHex is null)
            return;
        // The Witches with Raven's Blunder: BGA logs the 5 VP ("Witches Ability") or a 3-step cult advance.
        var packet = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet <= n.Packet + 1).Where(m => m.Str("player_id") == n.Str("player_id")).ToList();
        var steps = packet.FirstOrDefault(m => m.Type == "advanceCultTrack" && m.Int("inc_value") == 3);
        if (packet.Any(m => m.Type == "updateScore" && m.Log.Contains("Ability") && m.Int("score_inc") == 5) || steps is null)
            Do(new ChooseTownBonus(f, null), n);
        else
            Do(new ChooseTownBonus(f, Tables.ToTrack(steps.Str("cult_id") ?? steps.Str("cult_name") ?? "")), n);
    }

    private Note? _atlanteansTown;

    // A town's steps on every track. Newer BGA logs them after the town, one track per packet in the order the player
    // chose (advanceCultTrack, then specialActionUsed with no favour or pass tile): Core made them with the town, so
    // those notes are used up here. With fewer keys than tracks reaching 10 (TopStepChoiceOwed), the key goes where BGA
    // shows a 9 → 10 (927967865 #357: Air, not Fire); with no such note, the first track that can take it.
    private void TownTrackSteps(Note n, Faction f)
    {
        var id = n.Str("player_id");
        var later = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet <= n.Packet + 6).ToList();
        var steps = later.Where(m => m.Type == "advanceCultTrack" && m.Str("player_id") == id && string.IsNullOrEmpty(m.Str("addendum"))
            && !_usedNotes.Contains(m.Index)).Take(4).ToList();
        var perTrack = steps.Count == 4 && steps.Select(m => m.Str("cult_id") ?? m.Str("cult_name")).Distinct().Count() == 4;
        if (perTrack)
            foreach (var step in steps)
            {
                _usedNotes.Add(step.Index);
                if (later.FirstOrDefault(m => m.Packet == step.Packet && m.Type == "specialActionUsed" && m.Str("player_id") == id
                        && m.Args["favor_id"] is null && m.Args["bonus_id"] is null) is Note used)
                    _usedNotes.Add(used.Index);
            }
        for (var guard = 0; guard < 4 && S[f].TopStepChoiceOwed && !_stopped; guard++)
        {
            var held = S[f].TopStepsHeld ?? ValueList<Track>.Empty;
            var top = (perTrack ? steps : later.Where(m => m.Type == "advanceCultTrack" && m.Str("player_id") == id))
                .Where(m => m.Int("initial_pos") == Config.TrackMax - 1 && m.Int("final_pos") == Config.TrackMax)
                .Select(m => (Track?)Tables.ToTrack(m.Str("cult_id") ?? m.Str("cult_name") ?? ""))
                .FirstOrDefault(track => held.Contains(track!.Value) && S[f].TrackSteps[(int)track.Value] == Config.TrackMax - 1)
                ?? held.First(track => S[f].TrackSteps[(int)track] == Config.TrackMax - 1);
            Do(new TakeTopStep(f, top), n);
        }
    }

    private void Shipping(Note n, Faction f)
    {
        if (n.Log.Contains("for free") || n.Str("priests_cost") is null && n.Str("coins_cost") is null)
            return; // the Mermaids' stronghold: Core moves them up by itself (checked with the counts)
        EnsureActive(f, !S[f].LandscapeOffers.HasFlag(LandscapeOffer.Shipping), n);
        Do(new UpgradeShipping(f), n);
    }

    // The Snow Shamans' free step for passing: BGA logs it before the pass, in the same packet; Core takes it after.
    private void ShamansStep(Note n, Faction f)
    {
        if (!S[f].FreeUpgradeOwed)
        {
            var pass = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .FirstOrDefault(m => m.Type is "playerPassed" or "firstPlayerChanged" && m.Str("player_id") == n.Str("player_id"));
            if (pass is null)
            {
                Stop(n, "The Snow Shamans' free step with no pass after it.");
                return;
            }
            EnsureActive(f, true, pass);
            Do(new Pass(f), pass);
            _usedNotes.Add(pass.Index);
            if (_stopped)
                return;
        }
        Do(n.Type == "advanceShippingTrack" ? new UpgradeShipping(f) : new UpgradeSpades(f), n);
    }

    private void PowerAction(Note n, Faction f)
    {
        var used = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet).FirstOrDefault(m => m.Type == "actionUsed");
        if (used?.Int("action_id") is not int id)
        {
            Stop(n, "A power action without the actionUsed that names it.");
            return;
        }
        EnsureActive(f, true, n);
        // Chash Dallah with their stronghold may pay a power action in coins (all of it): BGA's power_cost icon then says
        // Coins; older logs without it: when the power isn't there.
        var action = new TakePowerAction(f, Tables.PowerAction(id));
        var cost = n.Str("power_cost") ?? "";
        if (f == Faction.ChashDallah && (cost.Contains("title='Coins'")
            || !Rules.Apply(S, action).Accepted && Rules.Apply(S, action with { PayWithCoins = true }).Accepted))
            action = action with { PayWithCoins = true };
        Do(action, n);
    }

    // A pass tile's or favour's action (a stronghold's action may come here too). BGA logs its effect first: the only
    // kind seen so far is the cult step, an advanceCultTrack just before.
    // Notes already turned into moves with another note (a pass tile's specialActionUsed with its transform, a stronghold
    // action's effect, the Ice Maidens' starting favour in setup) or that Core does by itself (Dynion Geifr's favour).
    private readonly HashSet<int> _usedNotes = new();

    // The Treasurers' Safe: what they received waits (GameState.TreasuryOffer) until they say what goes in. BGA's
    // updateTreasury (the same packet or the next) says it; none: nothing goes in.
    private void SettleTreasury(Note n)
    {
        if (_stopped || S.TreasuryOffer is null || !S.IsInGame(Faction.Treasurers))
            return;
        var id = _faction.FirstOrDefault(kv => kv.Value == Faction.Treasurers).Key;
        // Also a little earlier: BGA logs the Safe before the actionUsed that the action is read from (a power action);
        // and from the packet that made the offer on (an undo in between can push the next note further on: 840081092
        // #232, settled only at #235).
        var from = Math.Min(n.Packet, _treasuryOfferPacket ?? n.Packet) - 2;
        var stored = _g.Notes.Where(m => m.Packet >= from && m.Packet <= n.Packet + 1)
            .FirstOrDefault(m => m.Type == "updateTreasury" && m.Str("player_id") == id && !_usedNotes.Contains(m.Index));
        if (stored is null)
        {
            Do(new StoreInTreasury(Faction.Treasurers, 0, 0, 0), n);
            return;
        }
        _usedNotes.Add(stored.Index);
        Do(new StoreInTreasury(Faction.Treasurers, stored.Amount("coins_income"), stored.Amount("workers_income"), stored.Amount("priests_income")), stored);
    }

    // The Riverwalkers decide about each priest gained before anyone acts: an unlock BGA logs a little later
    // (priestFromTerrainCycle) is made now; with none, they keep the priest.
    private void SettlePriestChoices(Note n)
    {
        for (var guard = 0; guard < 8 && !_stopped && S.Players.FirstOrDefault(p => p.PriestChoicesOwed > 0) is PlayerState choosing; guard++)
        {
            var id = _faction.FirstOrDefault(kv => kv.Value == choosing.Faction).Key;
            var unlock = _g.Notes.Skip(n.Index).TakeWhile(m => m.Packet <= n.Packet + 2)
                .FirstOrDefault(m => m.Type == "priestFromTerrainCycle" && m.Str("player_id") == id && !_usedNotes.Contains(m.Index));
            if (unlock is not null)
            {
                _usedNotes.Add(unlock.Index);
                var title = System.Text.RegularExpressions.Regex.Match(unlock.Str("terrain") ?? "", "title='([a-z]+)'").Groups[1].Value;
                Do(new ChoosePriest(choosing.Faction, Tables.ToTerrain(title)), unlock);
            }
            else
                Do(new ChoosePriest(choosing.Faction, null), n);
        }
    }

    // The Djinni's two starting steps: BGA logs them (advanceCultTrack) with the factions, Core takes them in turn with
    // the last starting dwellings. Sent when Core is waiting for them.
    private void SettleStartingSteps()
    {
        if (!Rules.IsStartingStepsTurn(S) || S.ActivePlayer is not Faction active)
            return;
        var id = _faction.FirstOrDefault(kv => kv.Value == active).Key;
        var step = _g.Notes.FirstOrDefault(m => m.Type == "advanceCultTrack" && m.Str("player_id") == id);
        if (step is null)
        {
            Stop(null, $"The {Name(active)} owe their starting steps, but BGA logs no cult advance for them.");
            return;
        }
        Do(new TakeStartingSteps(active, Tables.ToTrack(step.Str("cult_id") ?? step.Str("cult_name") ?? "")), step);
    }

    private static BonusSource SourceOf(Note n) => n.Args["favor_id"] is not null ? BonusSource.Favour
        : n.Args["bonus_id"] is not null ? BonusSource.PassTile : BonusSource.Stronghold;

    private void SpecialAction(Note n, Faction f)
    {
        if (_usedNotes.Contains(n.Index))
            return;
        var source = SourceOf(n);
        // The Prospectors' pass-tile spade, which Core turns into a priest ("gains 1 spades_converted"); the Acolytes',
        // which Core makes a cult step owed: BGA's advanceCultTrack "(Acolyte ability)" after it names the track
        // (941433855 #186).
        if (n.Args["spades_converted"] is not null)
        {
            EnsureActive(f, true, n);
            if (Do(new BonusSpades(f, source), n))
                for (var guard = 0; guard < 6 && S[f].CultStepsOwed > 0 && !_stopped; guard++)
                    TakeOwedCultSteps(f, n);
            return;
        }
        // Neither a pass tile nor a favour, with a cult step owed: the Cultists' step for an opponent taking their power.
        if (source == BonusSource.Stronghold && S[f].CultStepsOwed > 0)
        {
            var owed = _g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
                .FirstOrDefault(m => m.Type == "advanceCultTrack" && m.Str("player_id") == n.Str("player_id"));
            if (owed is null)
                Stop(n, $"The {Name(f)} owe a cult step, but BGA logs no cult advance with it.");
            else
                Do(new TakeCultStep(f, Tables.ToTrack(owed.Str("cult_id") ?? owed.Str("cult_name") ?? "")), n);
            return;
        }
        var step = _g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
            .FirstOrDefault(m => m.Type == "advanceCultTrack" && m.Str("player_id") == n.Str("player_id"));
        if (step is not null && _usedNotes.Contains(step.Index))
            return; // the stronghold action's own (actionUsed {stronghold}): already sent with its cult step
        if (source == BonusSource.Stronghold && _g.Notes.Take(n.Index).Reverse().TakeWhile(m => m.Packet == n.Packet)
                .Any(m => m.Type == "actionUsed" && m.Args["stronghold"] is not null && m.Str("player_id") == n.Str("player_id")))
            return;
        // A spade logged before the transform or landscape that spends it (same packet): the spades now, spent by that note.
        if (step is null && source != BonusSource.Stronghold && _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .Any(m => m.Type is "landscapePlaced" or "terrainTransformed" && m.Str("player_id") == n.Str("player_id")
                    && m.Amount("spades_used") > 0))
        {
            ResolveOffers();
            EnsureActive(f, true, n);
            Do(new BonusSpades(f, source), n);
            return;
        }
        if (step is null)
        {
            Stop(n, $"A {source} action other than a cult step (BGA: favor_id {n.Str("favor_id")}, bonus_id {n.Str("bonus_id")}) is not converted yet.");
            return;
        }
        EnsureActive(f, true, n);
        Do(new BonusCultStep(f, source, Tables.ToTrack(step.Str("cult_id") ?? step.Str("cult_name") ?? "")), n);
    }

    // A stronghold action: BGA logs its effect in the counters, then actionUsed {stronghold: true}. Only the actions that
    // need no choice are converted; the others have not been seen yet.
    private void StrongholdAction(Note n, Faction f)
    {
        // For actions with a choice BGA logs the effect after actionUsed, in the same packet: that note says where.
        Note? Effect(string type)
        {
            var effect = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .FirstOrDefault(m => m.Type == type && m.Str("player_id") == n.Str("player_id"));
            if (effect is not null)
                _usedNotes.Add(effect.Index);
            return effect;
        }
        // A note of this type by this player in this packet (before or after) or the next one; marked used.
        Note? Near(string type, bool nextPacket = false)
        {
            var near = _g.Notes.Where(m => m.Packet >= n.Packet && m.Packet <= n.Packet + (nextPacket ? 1 : 0)
                    && m.Type == type && m.Str("player_id") == n.Str("player_id") && !_usedNotes.Contains(m.Index))
                .OrderBy(m => Math.Abs(m.Index - n.Index)).FirstOrDefault();
            if (near is not null)
                _usedNotes.Add(near.Index);
            return near;
        }
        int? HexOf(Note? effect, string? key = null) =>
            effect is null ? null : Hex(key is null ? effect.Str("bts_id") : J.Str(effect.Args[key]?["bts_id"]));
        var source = BonusSource.Stronghold;
        Move? move = Factions.StrongholdAction(f) switch
        {
            BonusAction.CoinsForTradingPosts => new BonusCoinsForTradingPosts(f, source),
            BonusAction.FourPower => new BonusFourPower(f, source),
            BonusAction.TwoSpades => new BonusSpades(f, source), // the transform follows as its own note (SpendSpades)
            BonusAction.DoubleAction => new BonusDoubleAction(f, source),
            BonusAction.BuildOnHomeTerrain when HexOf(Effect("dwellingPlaced")) is int hex => new BonusBuild(f, source, hex),
            BonusAction.FreeTradingPost when HexOf(Effect("structureUpgraded"), "new_struct") is int hex => new BonusTradingPost(f, source, hex),
            BonusAction.Sandstorm when HexOf(Effect("terrainTransformed")) is int hex => new BonusSandstorm(f, source, hex),
            BonusAction.TwoCultSteps when Effect("advanceCultTrack") is Note step
                => new BonusCultStep(f, source, Tables.ToTrack(step.Str("cult_id") ?? step.Str("cult_name") ?? "")),
            // The Children of the Wyrm: tokenPlaced (before actionUsed) names the river hexes.
            BonusAction.RiverTokens => Near("tokenPlaced") is Note tokens
                ? new BonusRiverTokens(f, source, tokens.Str("bts_id_1") is string a && a != "null" ? Hex(a) : null,
                    tokens.Str("bts_id_2") is string b && b != "null" ? Hex(b) : null)
                : new BonusRiverTokens(f, source, null),
            // The Time Travellers: powerActionConvert "moves N from Bowl I to Bowl III" (before actionUsed).
            // The Selkies: actionUsed, then the transform and the dwelling (one move in Core).
            BonusAction.SelkiesSpade when Effect("dwellingPlaced") is Note built && Effect("terrainTransformed") is var _
                => new BonusSelkiesBuild(Next(f, PieceKind.Dwelling), source, Hex(built.Str("bts_id"))),
            // Only the transform (no dwelling): the spade, spent by the terrainTransformed that follows (SpendSpades).
            BonusAction.SelkiesSpade => new BonusSpades(f, source),
            BonusAction.TokensToBowlThree when Near("powerActionConvert") is Note moved
                => new BonusTokensToBowlThree(f, source, moved.Amount("power_moved")),
            // The Shapeshifters: shapeshifted names the new home; tokens removed = paid with tokens.
            BonusAction.Shapeshift when Near("shapeshifted") is Note shifted
                => new BonusShapeshift(f, source, Tables.ToTerrain(shifted.Str("new_terrain") ?? ""),
                    shifted.Args["removed_tokens"] is JsonArray removed && removed.Count > 0),
            // The Conspirators: favorTileLost, then favorTileChosen (the next packet).
            BonusAction.SwapFavour when Near("favorTileLost") is Note lost && Near("favorTileChosen", nextPacket: true) is Note taken
                => new BonusSwapFavour(f, source, Tables.Favour(lost.Int("favor_type") ?? -1), Tables.Favour(taken.Int("favor_type") ?? -1)),
            // The Geologists: terrainRingUpdated "swamp → Markers" (a rock onto swamp) or "Markers → mountains" (off it).
            BonusAction.MoveRock when Effect("terrainRingUpdated") is Note ring && RockTerrain(ring) is Terrain rock
                => new BonusMoveRock(f, source, rock),
            _ => null,
        };
        if (move is null)
        {
            Stop(n, $"The {Factions.Name(f)}' stronghold action ({Factions.StrongholdAction(f)}) is not converted yet.");
            return;
        }
        EnsureActive(f, true, n);
        Do(move, n);
    }

    // The terrain a Geologists' rock goes onto or comes off: the icon title in the log other than "Markers".
    private static Terrain? RockTerrain(Note ring)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(ring.Log, "title='([^']+)'"))
            if (m.Groups[1].Value != "Markers")
                return Tables.ToTerrain(m.Groups[1].Value);
        return null;
    }

    private readonly Dictionary<string, (int A, int B)> _bridgeById = new(); // BGA bridge_id → where it stands

    // A list in a note's args: a JSON array (["F4", "G5", "G6"]), or the same written as text.
    private static List<string> TextList(JsonNode? node) => node is JsonArray array
        ? array.Select(item => J.Str(item) ?? "").Where(item => item != "").ToList()
        : System.Text.RegularExpressions.Regex.Matches(J.Str(node) ?? "", "['\"]([^'\"]*)['\"]").Select(m => m.Groups[1].Value).ToList();

    private void LavaFlow(Note n, Faction f)
    {
        var hexes = TextList(n.Args["terrains"]).Select(Hex).ToList();
        if (hexes.Count < 2)
        {
            Stop(n, "A lava flow with no path.");
            return;
        }
        int? bridgeA = null, bridgeB = null;
        var spots = TextList(n.Args["bridges_bbs"]);
        if (spots.Count > 1)
        {
            Stop(n, "A lava flow with more than one bridge.");
            return;
        }
        if (spots.Count == 1)
        {
            if (!int.TryParse(spots[0], out var bbs) || !_bridgeSpots.TryGetValue(bbs, out var spot))
            {
                Stop(n, $"Bridge spot {spots[0]} is not in the game data.");
                return;
            }
            (bridgeA, bridgeB) = (Hex(spot.Up), Hex(spot.Down));
            if (TextList(n.Args["bridges"]).FirstOrDefault() is string bridgeId)
                _bridgeById[bridgeId] = (bridgeA.Value, bridgeB.Value);
        }
        EnsureActive(f, true, n);
        Do(new LetLavaFlow(f, hexes[0], new ValueList<int>(hexes.Skip(1)), bridgeA, bridgeB), n);
    }

    private void Bridge(Note n, Faction f)
    {
        var bbs = n.Int("bbs_id") ?? -1;
        if (!_bridgeSpots.TryGetValue(bbs, out var spot))
        {
            Stop(n, $"Bridge spot {bbs} is not in the game data.");
            return;
        }
        int a = Hex(spot.Up), b = Hex(spot.Down);
        var bridgeId = n.Str("bridge_id");
        if (n.Log.Contains("moves a Bridge")) // the Architects' stronghold action; its actionUsed follows
        {
            if (bridgeId is null || !_bridgeById.TryGetValue(bridgeId, out var from))
            {
                Stop(n, $"The Architects move bridge {bridgeId}, which the converter hasn't seen built.");
                return;
            }
            var used = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
                .FirstOrDefault(m => m.Type == "actionUsed" && m.Args["stronghold"] is not null);
            if (used is not null)
                _usedNotes.Add(used.Index);
            EnsureActive(f, true, n);
            if (Do(new BonusMoveBridge(f, BonusSource.Stronghold, from.A, from.B, a, b), n))
                _bridgeById[bridgeId] = (a, b);
            return;
        }
        if (bridgeId is not null)
            _bridgeById[bridgeId] = (a, b);
        if (S.ActivePlayer == f && S[f].BridgesOwed > 0) // owed already (the Riverwalkers' stronghold: up to 2 free)
        {
            Do(new PlaceBridge(f, a, b), n);
            return;
        }
        EnsureActive(f, true, n);
        var byAbility = n.Log.Contains("Ability") && n.Amount("power_cost") == 0;
        if (!Do(byAbility ? new BuildBridge(f) : new TakePowerAction(f, Tables.PowerAction(1)), n))
            return;
        Do(new PlaceBridge(f, Hex(spot.Up), Hex(spot.Down)), n);
    }

    // The Firewalkers' pyro steps, a power each. A step that would gain nothing (bowls 1 and 2 empty) with a power
    // conversion of theirs still to come in the packet came after it on BGA: it and the rest wait for it (_pyroLater).
    private (Faction F, int Steps, Note Note)? _pyroLater;

    // Power-to-coin conversions already made from a conversionsApplied note (by its index), before the Firewalkers' pyro.
    private readonly Dictionary<int, int> _preConverted = new();

    private void Pyro(Faction f, int steps, Note n)
    {
        var conversion = _g.Notes.Skip(n.Index + 1).TakeWhile(m => m.Packet == n.Packet)
            .FirstOrDefault(m => m.Type == "conversionsApplied" && m.Str("player_id") == n.Str("player_id")
                && m.Amount("power_spent") > 0 && !_consumed.Contains(m.Index));
        var conversionFollows = conversion is not null;
        // BGA logs the pyro and the conversions as totals, in no order. As in 526060511 #411 (2/1/0 +
        // 5 power → 6 coins: convert 2, gain 5, convert 4): just enough of the packet's power-to-coin conversions go
        // first for none of the pyro's power to be wasted (each takes a token from bowl 3 to bowl 1: 2 more to gain).
        if (conversion is not null)
        {
            var power = S[f].Power;
            var room = 2 * power.Bowl1 + power.Bowl2;
            var toCoins = Math.Max(0, conversion.Amount("power_spent") - 5 * conversion.Amount("priests_gained")
                - 3 * Math.Max(0, conversion.Amount("workers_gained") - conversion.Amount("priests_spent")))
                - _preConverted.GetValueOrDefault(conversion.Index);
            var first = Math.Min(Math.Min((steps - room + 1) / 2, toCoins), power.Bowl3);
            for (var i = 0; i < first && !_stopped; i++)
            {
                Free(f, new ConvertPowerToCoin(f), conversion);
                _preConverted[conversion.Index] = _preConverted.GetValueOrDefault(conversion.Index) + 1;
            }
        }
        for (var i = 0; i < steps && !_stopped; i++)
        {
            if (conversionFollows && S[f].Power.Bowl1 + S[f].Power.Bowl2 == 0)
            {
                _pyroLater = (f, steps - i, n);
                return;
            }
            Free(f, new AdvancePyro(f), n);
        }
    }

    private void FlushPyro()
    {
        if (_pyroLater is not { } later)
            return;
        _pyroLater = null;
        for (var i = 0; i < later.Steps && !_stopped; i++)
            Free(later.F, new AdvancePyro(later.F), later.Note);
    }

    // BGA's conversions come as totals (spent: power, priests, workers; gained: priests, workers, coins); Core's one at
    // a time. Priests → workers and workers → coins are read from what was spent; the power conversions are the rest.
    private void Conversions(Note n, Faction f)
    {
        int powerSpent = n.Amount("power_spent"), priestsSpent = n.Amount("priests_spent"), workersSpent = n.Amount("workers_spent");
        int priestsGained = n.Amount("priests_gained"), workersGained = n.Amount("workers_gained"), coinsGained = n.Amount("coins_gained");
        if (powerSpent + priestsSpent + workersSpent + priestsGained + workersGained + coinsGained == 0)
            return;
        var victoryPointCoins = AlchemistsVictoryPointCoins(n, f);
        coinsGained -= victoryPointCoins;
        var pre = _preConverted.GetValueOrDefault(n.Index); // made already, before a pyro (Pyro)
        powerSpent -= pre;
        coinsGained -= pre;
        // During the round-end spades Core allows no conversion: made where it last could be, before the player's last
        // End turn (after their pass tile), the moves since made again (742519089 #259).
        if (!_convertingEarlier && S.Phase == Phase.RoundEndSpades && ConvertBeforeLastEndTurn(n, f))
            return;
        if (!_convertingEarlier)
            EnsureActive(f, StartsNextTurn(f, n), n); // first: what a conversion gives is measured with the faction active
        // What one conversion gives this faction now, as Core has it (the Dynion Geifr's priest: 2 workers and 2
        // coins; the Enlightened with their stronghold: double); 1 if Core can't make it at the moment.
        int Gain(Move move, Func<PlayerState, int> what, int otherwise = 1, GameState? from = null)
        {
            var at = from ?? S;
            var tried = Rules.Apply(at, move);
            // Refused here (mid-action: 862476363 #347, the Enlightened with their stronghold getting 2 coins a power):
            // measured before the action instead, with power to spare.
            if (!tried.Accepted && from is null && _actionStart is (_, GameState before) && before.IsInGame(f))
            {
                at = before.WithPlayer(before[f] with { Power = new Power(0, 0, Config.PowerTokens) });
                tried = Rules.Apply(at, move);
            }
            var gained = tried.Accepted ? what(tried.State[f]) - what(at[f]) : 0;
            return gained > 0 ? gained : otherwise;
        }
        var priestToWorker = priestsSpent;
        var workerToCoin = workersSpent;
        // Priests spent that aren't in hand yet come from the power → priest conversions in the same note: those first
        // (761559628 #87: the Dynion Geifr with 1 priest, 5 power and 2 priests → 1 priest, 4 workers, 4 coins).
        var priestsFirst = priestToWorker > S[f].Priests.Count && priestsGained > 0;
        var priestFrom = priestsFirst && Rules.Apply(S, new ConvertPowerToPriest(f)) is { Accepted: true } withPriest ? withPriest.State : S;
        int priestWorkers = priestToWorker > 0 ? Gain(new ConvertPriestToWorker(f), p => p.Workers, from: priestFrom) : 1;
        int priestCoins = priestToWorker > 0 ? Gain(new ConvertPriestToWorker(f), p => p.Coins, 0, priestFrom) : 0;
        int workerCoins = workerToCoin > 0 ? Gain(new ConvertWorkerToCoin(f), p => p.Coins) : 1;
        int powerPriests = priestsGained > 0 ? Gain(new ConvertPowerToPriest(f), p => p.Priests.Count) : 1;
        var powerToPriest = priestsGained / powerPriests;
        var workersLeft = workersGained - priestToWorker * priestWorkers;
        int powerWorkers = workersLeft > 0 ? Gain(new ConvertPowerToWorker(f), p => p.Workers) : 1;
        var powerToWorker = workersLeft / powerWorkers;
        var coinsLeft = coinsGained - workerToCoin * workerCoins - priestToWorker * priestCoins;
        int powerCoins = coinsLeft > 0 ? Gain(new ConvertPowerToCoin(f), p => p.Coins) : 1;
        var powerToCoin = coinsLeft / powerCoins;
        if (powerToWorker < 0 || powerToCoin < 0
            || powerSpent != 5 * powerToPriest + 3 * powerToWorker + powerToCoin)
            Problem(n, $"these conversion totals don't add up as Core's conversions (power {powerSpent}, priests {priestsSpent}, workers {workersSpent} → priests {priestsGained}, workers {workersGained}, coins {coinsGained}); converted as far as they go");
        void Repeat(int times, Func<Move> move)
        {
            for (var i = 0; i < times && !_stopped; i++)
                Free(f, move(), n);
        }
        Repeat(victoryPointCoins, () => new ConvertVictoryPointToCoin(f));
        if (priestsFirst)
            Repeat(powerToPriest, () => new ConvertPowerToPriest(f));
        Repeat(priestToWorker, () => new ConvertPriestToWorker(f));
        if (!priestsFirst)
            Repeat(powerToPriest, () => new ConvertPowerToPriest(f));
        Repeat(Math.Max(0, powerToWorker), () => new ConvertPowerToWorker(f));
        Repeat(Math.Max(0, powerToCoin), () => new ConvertPowerToCoin(f));
        Repeat(workerToCoin, () => new ConvertWorkerToCoin(f));
        if (_pyroLater?.F == f)
            FlushPyro();
    }

    private bool _convertingEarlier;

    private bool ConvertBeforeLastEndTurn(Note n, Faction f)
    {
        var index = _applied.FindLastIndex(m => m is EndTurn e && e.Player == f);
        if (index < 0)
            return false;
        var tail = _applied.Skip(index).ToList();
        for (var i = 0; i < tail.Count; i++)
            if (!_h.Undo())
            {
                Stop(n, "Couldn't go back to the player's last End turn to make a conversion there.");
                return true;
            }
        _applied.RemoveRange(index, tail.Count);
        if (_actionStart is (int started, _) && started >= index)
            _actionStart = null;
        _trace.AppendLine($"#{n.MoveId,-6} (conversion logged during the round-end spades: made before the {Name(f)}' last End turn)");
        _convertingEarlier = true;
        try { Conversions(n, f); }
        finally { _convertingEarlier = false; }
        if (_stopped)
            return true;
        foreach (var t in tail)
        {
            if (!_h.Apply(t).Accepted)
            {
                Stop(n, $"After the conversion before their last End turn, Core refuses {t} again.");
                return true;
            }
            _applied.Add(t);
        }
        return true;
    }

    // Older logs fold the Alchemists' VP → coins into conversionsApplied: the coins that neither power nor workers paid.
    private static int AlchemistsVictoryPointCoins(Note n, Faction f)
    {
        if (f != Faction.Alchemists)
            return 0;
        int powerSpent = n.Amount("power_spent"), workersGained = n.Amount("workers_gained"), priestsGained = n.Amount("priests_gained");
        int priestsSpent = n.Amount("priests_spent"), workersSpent = n.Amount("workers_spent"), coinsGained = n.Amount("coins_gained");
        var powerToCoin = powerSpent - 5 * priestsGained - 3 * Math.Max(0, workersGained - priestsSpent);
        return Math.Max(0, coinsGained - workersSpent - Math.Max(0, powerToCoin));
    }
}
