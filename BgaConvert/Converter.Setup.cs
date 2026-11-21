using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// Converter, continued: the setup: the game, factions, turn order, map, tiles, Start.
public sealed partial class Converter
{
    // ---------- Setup: the game, factions, turn order, map, tiles, Start ----------

    private void Setup()
    {
        var notes = _g.Notes;
        string Option(string prefix) =>
            notes.FirstOrDefault(m => m.Type == "justAMessage" && m.Log.StartsWith(prefix)) is Note found
                ? found.Str("board_name") ?? found.Str("option_on") ?? "" : "";
        bool On(string prefix) => Option(prefix).StartsWith("On"); // e.g. "On - no Fire & Ice" (fan factions)
        var board = Option("Game board");
        var mini = On("Mini-expansions");
        var landscapes = On("Landscapes expansion");
        var fireAndIceFactions = On("Fire and Ice - Factions");
        var fan = On("Fan Factions");

        foreach (var (id, node) in (JsonObject)_g.GameData["players"]!)
            if (J.Str(node?["name"]) is string name)
            {
                _idByName[name] = id;
                _nameById[id] = name;
            }

        // The factions, by turn order. (BGA records each player's faction as its board is set: factionBoardSwapped.)
        var choices = notes.Where(n => n.Type == "factionBoardSwapped" && n.Str("player_id") is not null && n.Str("new_faction") is not null)
            .Select(n => (Id: n.Str("player_id")!, Faction: Tables.ToFaction(n.Str("new_faction")!),
                Order: n.Int("turn_order") ?? 0, Start: n.Str("faction_starting_terrain")))
            .GroupBy(c => c.Id).Select(g => g.Last()).OrderBy(c => c.Order).ToList();
        if (choices.Count == 0)
        {
            Stop(null, "The log has no faction choices (factionBoardSwapped).");
            return;
        }
        foreach (var c in choices)
            _faction[c.Id] = c.Faction;

        // Starting VP (auction bids); 20 without an auction.
        var startingVp = new Dictionary<string, int>();
        foreach (var n in notes.Where(n => n.Type == "updateScoreSmallDelay" && n.Args["starting_vp"] is not null && n.Str("player_id") is not null))
            startingVp[n.Str("player_id")!] = n.Amount("starting_vp");
        // Older tables (no auction): BGA sets each faction's starting VP with its board (factionBoardSwapped, then
        // updateScore in the same packet: 45327837 has 27, 20, 20, 22).
        if (startingVp.Count == 0)
            foreach (var swapped in notes.Where(n => n.Type == "factionBoardSwapped" && n.Str("player_id") is not null))
                if (notes.Skip(swapped.Index + 1).TakeWhile(m => m.Packet == swapped.Packet)
                        .FirstOrDefault(m => m.Type == "updateScore" && m.Str("player_id") == swapped.Str("player_id")) is Note score
                    && score.Args["newScore"] is not null)
                    startingVp[swapped.Str("player_id")!] = score.Amount("newScore");

        // The map: the BGA board compared hex by hex with each of Core's premade maps; none: a custom map. Custom boards
        // are also in a newCustomBoard note (move 2), the only copy when the game data leaves them out (585709876).
        var customBoard = notes.FirstOrDefault(m => m.Type == "newCustomBoard");
        var spaces = _g.GameData["board_terrain_spaces"] as JsonObject is { Count: > 0 } fromData ? fromData
            : customBoard?.Args["board"] as JsonObject;
        if (spaces is null || spaces.Count == 0)
        {
            Stop(null, "Neither the game data nor the log has the board.");
            return;
        }
        var bgaHexes = spaces.Select(kv => (Id: kv.Key, Row: kv.Key[0] - 'A', Column: int.Parse(kv.Key[1..]) - 1,
            Terrain: Tables.ToTerrain(J.Str(kv.Value?["bts_terrain"]) ?? ""))).ToList();
        MapId? map = null;
        string? customRows = null;
        foreach (var id in Maps.Premade)
        {
            var definition = Maps.ById(id);
            if (definition.Layout.HexCount == bgaHexes.Count
                && bgaHexes.All(h => definition.Layout.IndexOf(h.Row, h.Column) is var i && i >= 0 && definition.HexTerrain[i] == h.Terrain))
            {
                map = id;
                break;
            }
        }
        if (map is null)
        {
            // Nine rows of 13 and 12 hexes in turn, as Core's custom maps (Maps.CustomLayout).
            var rowLengths = bgaHexes.GroupBy(h => h.Row).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
            var layout = rowLengths.Count > 0 && rowLengths[0] is 12 or 13 ? Maps.CustomLayout(twelveTop: rowLengths[0] == 12) : null;
            if (layout is not null && layout.RowLengths.SequenceEqual(rowLengths)
                && bgaHexes.All(h => layout.IndexOf(h.Row, h.Column) >= 0))
            {
                var terrain = new Terrain[layout.HexCount];
                foreach (var h in bgaHexes)
                    terrain[layout.IndexOf(h.Row, h.Column)] = h.Terrain;
                customRows = Maps.Rows(layout, terrain);
                map = MapId.Custom;
            }
        }

        FinalScoringTile? tile = null;
        var tileText = "off";
        if (J.Str(_g.GameData["fire_ice_final_scoring_active"]) is "true" or "1" && _g.GameData["fire_ice_end_scoring_tile"] is JsonObject scoring)
        {
            var title = J.Str(scoring["info"]?["title"]);
            tile = Tables.ToFinalScoring(J.Str(scoring["index"]), title);
            tileText = $"\"{title}\" (index {J.Str(scoring["index"])}) → {(tile?.ToString() ?? "not recognised")}";
        }

        ReadBridgeSpots(_g.GameData["board_bridge_spaces"] as JsonObject is { Count: > 0 } bridgeData ? bridgeData
            : customBoard?.Args["bridges"] as JsonObject);
        foreach (var (id, node) in (JsonObject)_g.GameData["bonus_cards"]!)
            _passTiles[int.Parse(id)] = Tables.PassTile(J.Int(node?["bonus_type"]) ?? -1);
        // The Archivists' extra bonus card: not in the game data (taken at move 1), added by its own note.
        foreach (var added in notes.Where(m => m.Type == "addNewBonusCard"))
            if (added.Args["new_bonus_card"] is JsonObject card && J.Int(card["bonus_id"]) is int cardId)
                _passTiles[cardId] = Tables.PassTile(J.Int(card["bonus_type"]) ?? -1);
        foreach (var (id, node) in (JsonObject)_g.GameData["town_tiles"]!)
            _keyTiles[int.Parse(id)] = Tables.KeyTile(J.Int(node?["town_type"]) ?? -1);
        var roundTiles = ((JsonObject)_g.GameData["scoring_tiles"]!)
            .Select(kv => (Round: int.Parse(kv.Key), Type: J.Int(kv.Value?["scoring_type"]) ?? -1))
            .OrderBy(t => t.Round).Select(t => Tables.RoundTile(t.Type)).ToList();
        var passTiles = _passTiles.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();

        // Older logs have no option lines: the game data and what is in play tell.
        if (Option("Mini-expansions") == "")
            mini = roundTiles.Any(t => RoundTiles.All[t].MiniExpansion) || passTiles.Any(t => PassTiles.All[t].MiniExpansion)
                || _keyTiles.Values.Any(t => KeyTiles.All[t].MiniExpansion);
        if (Option("Landscapes expansion") == "")
            landscapes = J.Str(_g.GameData["landscape_active"]) is "true" or "1" || notes.Any(m => m.Type == "landscapePlaced");
        if (Option("Fire and Ice - Factions") == "")
            fireAndIceFactions = choices.Any(c => !Factions.IsFanFaction(c.Faction) && Factions.Kind(c.Faction) != FactionKind.Base);
        if (Option("Fan Factions") == "")
            fan = choices.Any(c => Factions.IsFanFaction(c.Faction));

        var header = new StringBuilder();
        header.AppendLine($"Board: {board} → Core map {(map?.ToString() ?? "NONE (no Core map matches this board, and it isn't one of the custom maps' shapes)")}");
        if (customRows is not null)
            header.AppendLine($"Custom map rows: {customRows}");
        header.AppendLine($"Options: mini-expansions {(mini ? "on" : "off")}, landscapes {(landscapes ? "on" : "off")}, " +
            $"Fire & Ice factions {(fireAndIceFactions ? "on" : "off")}, fan factions {(fan ? "on" : "off")}; final scoring tile {tileText}");
        foreach (var c in choices)
            header.AppendLine($"  {c.Order}. {_nameById.GetValueOrDefault(c.Id, c.Id)}: {Factions.Name(c.Faction)}, starting VP {startingVp.GetValueOrDefault(c.Id, Config.StartingVictoryPoints)}");
        header.AppendLine($"Round tiles (Core ids, round 1 first): {string.Join(", ", roundTiles)}");
        header.AppendLine($"Pass tiles (Core ids): {string.Join(", ", passTiles.OrderBy(t => t))}");
        header.AppendLine("Mode: Strict (a move leaving anyone in debt is refused).");
        // BGA's material for each faction (the version this table was played with) against Core's.
        var versionDiffs = _versionDiffs;
        foreach (var c in choices)
            if (MaterialCheck.MaterialOf(_g.GameData, c.Faction) is JsonObject material)
                versionDiffs.AddRange(MaterialCheck.Differences(material, c.Faction).Select(d => $"{Factions.Name(c.Faction)}: {d}"));
        if (versionDiffs.Count > 0)
        {
            header.AppendLine("BGA's faction material differs from Core's (another version of the faction, or a rule read differently):");
            foreach (var d in versionDiffs)
                header.AppendLine($"  {d}");
        }
        _header = header.ToString();
        foreach (var d in versionDiffs)
            Problem(null, $"faction material: {d}");

        if (map is null)
        {
            Stop(null, $"The BGA board \"{board}\" matches none of Core's maps and isn't a custom map's shape (nine rows of 13 and 12 hexes).");
            return;
        }
        if (landscapes && fan)
        {
            Stop(null, "Core can't play the fan factions and the special landscapes together.");
            return;
        }

        var options = new GameOptions(VariableTurnOrder: true, MiniExpansion: mini, FireAndIce: fireAndIceFactions || tile is not null,
            FinalScoring: tile, StartingMode: PlayMode.Strict, Landscapes: landscapes, FanFactions: fan);
        if (!Do(new ChooseGame(GameKind.TerraMystica, options), null))
            return;
        foreach (var c in choices)
        {
            if (!Do(new AddPlayer(c.Faction), null))
                return;
            // The Changelings: BGA has all three terrains at the start ("forest,wasteland,swamp": A, B, C); A now.
            if (Factions.Kind(c.Faction) is FactionKind.Ice or FactionKind.Variable && c.Start is string start)
                if (!Do(new ChooseStartingTerrain(c.Faction, Tables.ToTerrain(start.Split(',')[0])), null))
                    return;
            // The Ice Maidens take their favour now (BGA logs it just after the factions); the Dynion Geifr's Fire
            // favour Core gives them itself.
            if (c.Faction is Faction.IceMaidens or Faction.DynionGeifr
                && notes.FirstOrDefault(m => m.Type == "favorTileChosen" && m.Str("player_id") == c.Id) is Note favour)
            {
                _usedNotes.Add(favour.Index);
                if (c.Faction == Faction.IceMaidens && !Do(new TakeFavour(c.Faction, Tables.Favour(favour.Int("favor_type") ?? -1)), favour))
                    return;
            }
        }
        if (!Do(new ConfirmFactions(), null))
            return;
        // Volcano factions' starting terrains and the Changelings' B and C, in the order the factions were chosen.
        foreach (var c in choices)
            if (Factions.Kind(c.Faction) == FactionKind.Volcano && c.Start is not null)
            {
                if (!Do(new ChooseStartingTerrain(c.Faction, Tables.ToTerrain(c.Start!)), null))
                    return;
            }
            else if (c.Faction == Faction.Changelings && c.Start?.Split(',') is { Length: 3 } abc)
            {
                if (!Do(new ChooseChangelingTerrains(c.Faction, Tables.ToTerrain(abc[1]), Tables.ToTerrain(abc[2])), null))
                    return;
            }
        for (var i = 0; i < choices.Count - 1; i++) // the last is placed automatically
            if (!Do(new ChooseTurnOrder(choices[i].Faction), null))
                return;
        foreach (var c in choices)
            if (startingVp.TryGetValue(c.Id, out var vp) && vp != Config.StartingVictoryPoints)
                if (!Do(new AdjustStartingVictoryPoints(c.Faction, vp - Config.StartingVictoryPoints), null))
                    return;
        if (!Do(new ConfirmTurnOrder(), null)
            || !Do(customRows is not null ? (Move)new ChooseCustomMap(customRows) : new ChooseMap(map.Value), null))
            return;
        foreach (var roundTile in roundTiles)
            if (!Do(new ChooseRoundTile(roundTile), null))
                return;
        if (!Do(new ConfirmRoundTiles(), null))
            return;
        foreach (var passTile in passTiles)
            if (!Do(new AddPassTile(passTile), null))
                return;
        Do(new StartGame(), null);
    }

    private void ReadBridgeSpots(JsonObject? spots)
    {
        if (spots is null)
            return;
        foreach (var (id, node) in spots)
            if (int.TryParse(id, out var bbs) && J.Str(node?["bts_id_up"]) is string up && J.Str(node?["bts_id_down"]) is string down)
                _bridgeSpots[bbs] = (up, down);
    }
}
