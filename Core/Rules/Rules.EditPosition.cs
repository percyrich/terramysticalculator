namespace Core;

// Setting up a game in progress (Phase.SettingUp): every value is set directly. Nothing costs and nothing triggers
// anything else, except what follows from the pieces: priests in zones and favours carry their cult steps, keys on
// the map give their faction its keys, and towns stay consistent with the keys (KeepTownsConsistent).
// (Choosing the game, factions and map before round 1 is Rules.GameSetup.cs.)
public static partial class Rules
{
    private static MoveResult? CheckEditing(GameState state, Faction? faction = null)
    {
        if (state.Phase != Phase.SettingUp)
            return MoveResult.Rejected(state, "Values are set directly only while a game in progress is set up.");
        if (faction is Faction f && !state.IsInGame(f))
            return MoveResult.Rejected(state, $"The {Name(f)} are not in the game.");
        return null;
    }

    // An edit made: the towns and keys are brought in line with it, and each faction's favours owed (shown as blank
    // favours) are those still to claim (FavoursToClaim).
    private static MoveResult Edited(GameState next, List<GameEvent>? events = null)
    {
        var all = events ?? new List<GameEvent>();
        next = KeepTownsConsistent(next, all);
        foreach (var player in next.Players)
            if (FavoursToClaim(next, player.Faction) is var owed && owed != player.FavoursOwed)
                next = next.WithPlayer(player with { FavoursOwed = owed });
        return MoveResult.Ok(next, all, MoveResult.NoIssues);
    }

    // ---------- Resources, power, priests ----------

    private static MoveResult ApplyEditResource(GameState state, EditResource move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        var current = move.Resource switch
        {
            Resource.VictoryPoints => player.VictoryPoints,
            Resource.Coins => player.Coins,
            _ => player.Workers,
        };
        var value = current + move.Delta;
        if (move.Resource != Resource.VictoryPoints && value < 0)
            value = 0; // only VP go below 0
        if (value == current)
            return MoveResult.Rejected(state, move.Delta == 0 ? "Nothing to change." : $"The {Name(move.Player)} have none to take away.");
        var edited = move.Resource switch
        {
            Resource.VictoryPoints => player with { VictoryPoints = value },
            Resource.Coins => player with { Coins = value },
            _ => player with { Workers = value },
        };
        return Edited(state.WithPlayer(edited));
    }

    private static MoveResult ApplyEditPowerToken(GameState state, EditPowerToken move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.FromBowl is not (null or >= 1 and <= 3) || move.ToBowl is not (null or >= 1 and <= 3))
            return MoveResult.Rejected(state, "There is no such bowl.");
        if (move.FromBowl == move.ToBowl)
            return MoveResult.Rejected(state, "Nothing to change.");
        var power = state[move.Player].Power;
        if (move.FromBowl is int from)
        {
            if (Bowl(power, from) == 0)
                return MoveResult.Rejected(state, $"Bowl {from} is empty.");
            power = from switch
            {
                1 => power with { Bowl1 = power.Bowl1 - 1 },
                2 => power with { Bowl2 = power.Bowl2 - 1 },
                _ => power with { Bowl3 = power.Bowl3 - 1 },
            };
        }
        if (move.ToBowl is int to)
            power = power.AddTokens(to, 1);
        return Edited(state.WithPlayer(state[move.Player] with { Power = power }));
    }

    private static int Bowl(Power power, int bowl) => bowl switch { 1 => power.Bowl1, 2 => power.Bowl2, _ => power.Bowl3 };

    private static MoveResult ApplyEditGainPriest(GameState state, EditGainPriest move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.PriestSupply.Count == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no priest left in their supply.");
        return Edited(state.WithPlayer(player with
        {
            PriestSupply = player.PriestSupply.RemoveAt(0),
            Priests = player.Priests.Add(player.PriestSupply[0]),
        }));
    }

    // The Treasurers' Safe: coins and workers set directly; priests from the front of the supply in, the last in back.
    private static MoveResult ApplyEditSafe(GameState state, EditSafe move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Treasurers)
            return MoveResult.Rejected(state, "Only the Treasurers have a Safe.");
        var player = state[move.Player];
        int coins = Math.Max(0, player.SafeCoins + move.Coins), workers = Math.Max(0, player.SafeWorkers + move.Workers);
        var safe = player.SafePriestList.ToList();
        var supply = player.PriestSupply.ToList();
        for (var i = 0; i < move.Priests && supply.Count > 0; i++)
        {
            safe.Add(supply[0]);
            supply.RemoveAt(0);
        }
        for (var i = 0; i < -move.Priests && safe.Count > 0; i++)
        {
            supply.Insert(0, safe[^1]);
            safe.RemoveAt(safe.Count - 1);
        }
        if (coins == player.SafeCoins && workers == player.SafeWorkers && safe.Count == player.SafePriestList.Count)
            return MoveResult.Rejected(state, move.Priests > 0 && supply.Count == 0
                ? $"The {Name(move.Player)} have no priest left in their supply."
                : "There is none of that in the Safe to take out.");
        return Edited(state.WithPlayer(player with
        {
            SafeCoins = coins,
            SafeWorkers = workers,
            SafePriests = safe.Count == 0 ? null : new ValueList<Piece>(safe),
            PriestSupply = new ValueList<Piece>(supply),
        }));
    }

    private static MoveResult ApplyEditReturnPriest(GameState state, EditReturnPriest move)
    {
        var priest = move.Priest;
        if (CheckEditing(state, priest.Owner) is { } refusal)
            return refusal;
        var player = state[priest.Owner];
        if (player.Priests.Contains(priest))
            return Edited(state.WithPlayer(player with
            {
                Priests = player.Priests.RemoveAt(player.Priests.IndexOf(priest)),
                PriestSupply = player.PriestSupply.Insert(0, priest),
            }));
        if (FindPriest(state, priest) is not (Track track, int slot))
            return MoveResult.Rejected(state, "That priest is neither in the hand nor in a priest zone.");

        var events = new List<GameEvent>();
        var next = state with
        {
            PriestZones = state.PriestZones.SetItem((int)track, state.PriestZones[(int)track].SetItem(slot, null)),
        };
        next = next.WithPlayer(next[priest.Owner] with { PriestSupply = next[priest.Owner].PriestSupply.Insert(0, priest) });
        next = DescendTrack(next, priest.Owner, track, PriestSlotSteps(state, priest.Owner, slot), events);
        return Edited(next, events);
    }

    private static (Track, int)? FindPriest(GameState state, Piece priest)
    {
        for (var track = 0; track < state.PriestZones.Count; track++)
        {
            var slot = state.PriestZones[track].IndexOf(priest);
            if (slot >= 0)
                return ((Track)track, slot);
        }
        return null;
    }

    private static MoveResult ApplyEditPlacePriest(GameState state, EditPlacePriest move)
    {
        var priest = move.Priest;
        if (priest.Kind != PieceKind.Priest)
            return MoveResult.Rejected(state, "That piece is not a priest.");
        if (CheckEditing(state, priest.Owner) is { } refusal)
            return refusal;
        if (move.Slot < 0 || move.Slot >= Config.PriestSlotsPerZone)
            return MoveResult.Rejected(state, "There is no such slot.");
        if (Config.PriestSlots[move.Slot].ReturnsAtEndOfTurn)
            return MoveResult.Rejected(state, "A priest in that slot goes back at the end of the turn: only the slots that keep their priest can be set up.");
        var zone = state.PriestZones[(int)move.Track];
        if (state.IsSlotTaken(move.Track, move.Slot))
            return MoveResult.Rejected(state, "That slot is already taken.");

        var player = state[priest.Owner];
        PlayerState taken;
        if (player.PriestSupply.Contains(priest))
            taken = player with { PriestSupply = player.PriestSupply.RemoveAt(player.PriestSupply.IndexOf(priest)) };
        else if (player.Priests.Contains(priest))
            taken = player with { Priests = player.Priests.RemoveAt(player.Priests.IndexOf(priest)) };
        else
            return MoveResult.Rejected(state, "Only a priest from the supply or the hand can be placed.");

        var events = new List<GameEvent>();
        var next = state.WithPlayer(taken) with { PriestZones = state.PriestZones.SetItem((int)move.Track, zone.SetItem(move.Slot, priest)) };
        next = ClimbTrack(next, priest.Owner, move.Track, PriestSlotSteps(state, priest.Owner, move.Slot), events, out _);
        return Edited(next, events);
    }

    // ---------- Tracks, favours, levels ----------

    private static MoveResult ApplyEditTrack(GameState state, EditTrack move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var before = state[move.Player].TrackSteps[(int)move.Track];
        var events = new List<GameEvent>();
        if (move.Delta > 0)
        {
            var next = ClimbTrack(state, move.Player, move.Track, move.Delta, events, out var after);
            if (after == before)
                return MoveResult.Rejected(state, before == Config.TrackMax
                    ? $"The {Name(move.Player)} are already at the top of {move.Track}."
                    : $"The top of {move.Track} needs a key, and only one faction may be there.");
            return Edited(next, events);
        }
        if (move.Delta == 0 || before == 0)
            return MoveResult.Rejected(state, move.Delta == 0 ? "Nothing to change." : $"The {Name(move.Player)} are already at the bottom of {move.Track}.");
        return Edited(DescendTrack(state, move.Player, move.Track, -move.Delta, events), events);
    }

    // The favours a faction's buildings give: one per temple and one for the sanctuary (the Chaos Magicians two each),
    // the sanctuary counting the temple it was upgraded from too; one more for the Auren's stronghold, and the Ice
    // Maidens' starting favour.
    public static int FavoursDue(GameState state, Faction faction) =>
        state.HexesBuiltOnBy(faction).Sum(hex => state.Map[hex]!.Value.Kind switch
        {
            PieceKind.Sanctuary => Factions.FavoursFor(faction, PieceKind.Sanctuary) + Factions.FavoursFor(faction, PieceKind.Temple),
            var kind => Factions.FavoursFor(faction, kind),
        })
        + (faction == Faction.Auren && state[faction].StrongholdBuilt ? 1 : 0)
        + (faction == Faction.IceMaidens ? 1 : 0);

    // Favours still to be taken (blank favours on the board): no more than there are favours left that the faction can
    // take (the others could never have been taken).
    public static int FavoursToClaim(GameState state, Faction faction)
    {
        var blank = FavoursDue(state, faction) - state[faction].FavourTiles.Count;
        var takeable = Enumerable.Range(0, Favours.Count).Count(id => CanTakeFavour(state, faction, id));
        return Math.Max(0, Math.Min(blank, takeable));
    }

    // Favours held beyond what the buildings give (outlined in red until resolved).
    public static int FavoursOver(GameState state, Faction faction) =>
        Math.Max(0, state[faction].FavourTiles.Count - FavoursDue(state, faction));

    private static MoveResult ApplyEditFavour(GameState state, EditFavour move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Favour < 0 || move.Favour >= Favours.Count)
            return MoveResult.Rejected(state, "There is no such favour.");
        var player = state[move.Player];
        var favour = Favours.All[move.Favour];
        var events = new List<GameEvent>();
        if (move.Take)
        {
            if (player.HoldsFavour(move.Favour))
                return MoveResult.Rejected(state, $"The {Name(move.Player)} already have that favour.");
            var stack = state.FavourBank[move.Favour];
            if (stack.Count == 0)
                return MoveResult.Rejected(state, "That favour has run out.");
            var due = FavoursDue(state, move.Player);
            if (player.FavourTiles.Count >= due)
                return MoveResult.Rejected(state, $"The {Name(move.Player)} have no favour to take: their buildings give {due}.");
            var tile = stack[stack.Count - 1];
            var next = state.WithPlayer(player with { FavourTiles = player.FavourTiles.Add(tile) }) with
            {
                FavourBank = state.FavourBank.SetItem(move.Favour, stack.RemoveAt(stack.Count - 1)),
            };
            if (favour.Steps > 0)
                next = ClimbTrack(next, move.Player, favour.Track, favour.Steps, events, out _);
            return Edited(next, events);
        }

        var index = player.FavourTiles.ToList().FindIndex(tile => tile.Id == move.Favour);
        if (index < 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} don't have that favour.");
        var returned = player.FavourTiles[index];
        var back = state.WithPlayer(player with { FavourTiles = player.FavourTiles.RemoveAt(index) }) with
        {
            FavourBank = state.FavourBank.SetItem(move.Favour, state.FavourBank[move.Favour].Add(returned)),
        };
        if (favour.Steps > 0)
            back = DescendTrack(back, move.Player, favour.Track, favour.Steps, events);
        return Edited(back, events);
    }

    private static MoveResult ApplyEditLevel(GameState state, EditLevel move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (move.Level == Level.Shipping)
        {
            if (!Factions.HasShipping(move.Player))
                return MoveResult.Rejected(state, move.Player == Faction.Fakirs
                    ? "The Fakirs have no shipping: their carpet range follows from their stronghold and shipping keys."
                    : $"The {Name(move.Player)} have no shipping.");
            var shipping = Math.Clamp(player.Shipping + move.Delta, Factions.StartingShipping(move.Player), Factions.MaxShipping(move.Player));
            if (shipping == player.Shipping)
                return MoveResult.Rejected(state, $"The {Name(move.Player)}' shipping can't go {(move.Delta > 0 ? "higher" : "lower")}.");
            return Edited(state.WithPlayer(player with { Shipping = shipping }));
        }
        if (Factions.MaxSpadeUpgrades(move.Player) == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} cannot upgrade their spades.");
        var upgrades = Math.Clamp(player.SpadeUpgrades + move.Delta, 0, Factions.MaxSpadeUpgrades(move.Player));
        if (upgrades == player.SpadeUpgrades)
            return MoveResult.Rejected(state, $"The {Name(move.Player)}' spades can't go {(move.Delta > 0 ? "higher" : "lower")}.");
        return Edited(state.WithPlayer(player with { SpadeUpgrades = upgrades }));
    }

    // ---------- The map ----------

    private static MoveResult ApplyEditBridge(GameState state, EditBridge move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var (a, b) = move.HexA < move.HexB ? (move.HexA, move.HexB) : (move.HexB, move.HexA);
        var bridge = new Bridge(move.Player, a, b);
        if (!move.Place)
        {
            if (!state.Bridges.Contains(bridge))
                return MoveResult.Rejected(state, $"The {Name(move.Player)} have no bridge there.");
            return Edited(state with { Bridges = state.Bridges.RemoveAt(state.Bridges.IndexOf(bridge)) });
        }
        if (!BridgeEdges(state).Contains((a, b)) && !LandBridgeEdges(state, move.Player).Contains((a, b)))
            return MoveResult.Rejected(state, "A bridge can't go there.");
        if (state.IsBridged(a, b))
            return MoveResult.Rejected(state, "There is already a bridge there.");
        if (state.BridgesLeft(move.Player) == 0)
            return MoveResult.Rejected(state, $"The {Name(move.Player)} have no bridge left.");
        return Edited(state with { Bridges = state.Bridges.Add(bridge) });
    }

    private static MoveResult ApplyEditBuilding(GameState state, EditBuilding move)
    {
        var piece = move.Building;
        if (piece.Kind is PieceKind.Priest or PieceKind.Landscape)
            return MoveResult.Rejected(state, "That piece is not a building.");
        if (CheckEditing(state, piece.Owner) is { } refusal)
            return refusal;
        var owner = piece.Owner;
        var player = state[owner];
        var bank = player.Bank(piece.Kind);
        var fromHex = state.Map.IndexOf(piece);
        var fromSlot = bank.IndexOf(piece);
        if (fromHex < 0 && fromSlot < 0)
            return MoveResult.Rejected(state, "There is no such building.");
        var events = new List<GameEvent>();

        // Back to its board: the rightmost empty slot of its kind.
        if (move.Hex is not int hex)
        {
            if (fromHex < 0)
                return MoveResult.Rejected(state, "That building is already on its board.");
            return Edited(ReturnToBoard(state with { Map = state.Map.SetItem(fromHex, null) }, piece), events);
        }

        if (fromSlot >= 0 && CheckLeftmostInBank(state, bank, piece, Buildings.Noun(piece.Kind)) is { } bankRefusal)
            return bankRefusal;
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.HexTerrain[hex] == Terrain.River)
            return MoveResult.Rejected(state, "Rivers cannot be built on.");
        if (state.Neutrals.Hexes.Contains(hex))
            return MoveResult.Rejected(state, "A neutral dwelling stands there.");
        if (fromHex == hex && move.Terrain is null)
            return MoveResult.Rejected(state, "That building is already there.");

        // The terrain the hex ends up with.
        Terrain terrain;
        if (owner == Faction.Riverwalkers)
        {
            terrain = move.Terrain ?? state.HexTerrain[hex];
            if (!Terrains.IsBasic(terrain))
                return MoveResult.Rejected(state, "The Riverwalkers build only on the basic terrains.");
            if (!state.Layout.Neighbours(hex).Any(n => state.HexTerrain[n] == Terrain.River))
                return MoveResult.Rejected(state, "The Riverwalkers build only beside a river.");
        }
        else if (move.Terrain is Terrain chosen)
        {
            if (owner != Faction.Shapeshifters)
                return MoveResult.Rejected(state, "Only the Shapeshifters and the Riverwalkers choose the terrain under a building.");
            if (!Terrains.IsBasic(chosen) || state.TakenTerrains(except: owner).Contains(chosen))
                return MoveResult.Rejected(state, $"The Shapeshifters can't stand on {chosen}: it is another faction's.");
            terrain = chosen;
        }
        else if (player.BuildTerrain is Terrain buildTerrain)
            terrain = buildTerrain;
        else
            return MoveResult.Rejected(state, $"The {Name(owner)} have no terrain to build on yet.");

        var next = state;
        if (fromHex >= 0)
            next = next with { Map = next.Map.SetItem(fromHex, null) };
        else
            next = next.WithPlayer(player.WithBank(piece.Kind, bank.SetItem(fromSlot, null)));
        if (next.Map[hex] is Piece old && old != piece)
            next = ReturnToBoard(next with { Map = next.Map.SetItem(hex, null) }, old);
        next = ChangeTerrain(next, hex, terrain, events);
        next = next with { Map = next.Map.SetItem(hex, piece) };
        if (owner == Faction.Riverwalkers)
            next = UnlockOnWheel(next, terrain);
        return Edited(next, events);
    }

    // A building off the map, back to the rightmost empty slot of its own bank.
    private static GameState ReturnToBoard(GameState state, Piece piece)
    {
        if (piece.Kind == PieceKind.Landscape) // no board: it is simply off the map
            return state;
        var player = state[piece.Owner];
        var bank = player.Bank(piece.Kind);
        var slot = PlayerState.RightmostEmpty(bank)!.Value; // it came out of this bank, so a slot is free
        return state.WithPlayer(player.WithBank(piece.Kind, bank.SetItem(slot, piece)));
    }

    // Riverwalkers: a terrain their building stands on is unlocked; its wheel priest goes to the supply.
    private static GameState UnlockOnWheel(GameState state, Terrain terrain)
    {
        var player = state[Faction.Riverwalkers];
        var index = player.WheelPriests.ToList().FindIndex(locked => locked.Terrain == terrain);
        if (index < 0)
            return state;
        return state.WithPlayer(player with
        {
            WheelPriests = player.WheelPriests.RemoveAt(index),
            PriestSupply = player.PriestSupply.Add(player.WheelPriests[index].Priest),
        });
    }

    private static MoveResult ApplyEditTerrain(GameState state, EditTerrain move)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        if (!state.Layout.Contains(move.Hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.HexTerrain[move.Hex] == Terrain.River || move.To == Terrain.River)
            return MoveResult.Rejected(state, "Rivers stay rivers, and land stays land.");
        if (state.IsOccupied(move.Hex))
            return MoveResult.Rejected(state, "Only an empty hex's terrain can be changed.");
        var allowed = Terrains.IsBasic(move.To)
            || (move.To == Terrain.Ice && state.Players.Any(p => p.Kind == FactionKind.Ice))
            || (move.To == Terrain.Volcano && state.Players.Any(p => p.Kind == FactionKind.Volcano));
        if (!allowed)
            return MoveResult.Rejected(state, $"No faction in this game makes {move.To}.");
        if (state.HexTerrain[move.Hex] == move.To)
            return MoveResult.Rejected(state, $"That hex is already {move.To}.");
        var events = new List<GameEvent>();
        return Edited(ChangeTerrain(state, move.Hex, move.To, events), events);
    }

    private static MoveResult ApplyEditLandscape(GameState state, EditLandscape move)
    {
        var faction = move.Player;
        if (CheckEditing(state, faction) is { } refusal)
            return refusal;
        if (!state.Options.Landscapes)
            return MoveResult.Rejected(state, "The special landscapes are not in this game.");
        var piece = new Piece(faction, PieceKind.Landscape, 0);
        var from = state.LandscapeHex(faction);
        var events = new List<GameEvent>();
        if (move.Hex is not int hex)
        {
            if (from is not int on)
                return MoveResult.Rejected(state, $"The {Name(faction)}' {Landscapes.Name(faction)} is not on the map.");
            return Edited(state with { Map = state.Map.SetItem(on, null) }, events);
        }
        if (!state.Layout.Contains(hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (from == hex)
            return MoveResult.Rejected(state, "It is already there.");
        if (state.Neutrals.Hexes.Contains(hex))
            return MoveResult.Rejected(state, "A neutral dwelling stands there.");
        if (state.PlacedKeys.Any(key => key.Hex == hex))
            return MoveResult.Rejected(state, "A key lies on that hex.");
        var river = state.HexTerrain[hex] == Terrain.River;
        if (Landscapes.OnRiver(faction) != river)
            return MoveResult.Rejected(state, river
                ? $"The {Name(faction)}' {Landscapes.Name(faction)} goes on land."
                : $"The {Name(faction)}' {Landscapes.Name(faction)} goes on a river hex.");

        var next = from is int old ? state with { Map = state.Map.SetItem(old, null) } : state;
        if (next.Map[hex] is Piece there)
            next = ReturnToBoard(next with { Map = next.Map.SetItem(hex, null) }, there);
        if (!river && faction != Faction.ChaosMagicians && next[faction].BuildTerrain is Terrain build)
            next = ChangeTerrain(next, hex, build, events);
        else if (faction == Faction.ChaosMagicians)
            next = ChangeTerrain(next, hex, Terrain.Wasteland, events);
        next = next with { Map = next.Map.SetItem(hex, piece) };
        events.Add(new LandscapePlaced(piece, hex));
        return Edited(next, events);
    }

    // ---------- Keys and towns ----------

    private static MoveResult ApplyEditPlaceKey(GameState state, EditPlaceKey move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Token < 0 || move.Token >= KeyTiles.Count)
            return MoveResult.Rejected(state, "There is no such key token.");
        var stack = state.KeyBank[move.Token];
        if (stack.Count == 0)
            return MoveResult.Rejected(state, "That key token has run out.");
        if (!state.Layout.Contains(move.Hex))
            return MoveResult.Rejected(state, "There is no such hex.");
        if (state.PlacedKeys.Any(key => key.Hex == move.Hex))
            return MoveResult.Rejected(state, "There is already a key on that hex.");
        var onRiver = state.HexTerrain[move.Hex] == Terrain.River;
        if (onRiver && !(move.Player == Faction.Mermaids && state.Layout.Neighbours(move.Hex).Any(n => OwnsBuildingAt(state, move.Player, n))))
            return MoveResult.Rejected(state, "Only the Mermaids place a key on a river hex, beside their buildings.");
        if (!onRiver && !OwnsBuildingAt(state, move.Player, move.Hex))
            return MoveResult.Rejected(state, $"A key goes on one of the {Name(move.Player)}' buildings.");

        var tile = stack[stack.Count - 1];
        var player = state[move.Player];
        var next = state.WithPlayer(player with { Keys = player.Keys + KeyTiles.All[move.Token].Keys }) with
        {
            KeyBank = state.KeyBank.SetItem(move.Token, stack.RemoveAt(stack.Count - 1)),
            PlacedKeys = state.PlacedKeys.Add(new PlacedKey(tile, move.Player, move.Hex)),
        };
        var group = TownGroups(next, move.Player).First(g => g.Contains(move.Hex));
        var keyHexes = KeyHexesIn(next, move.Player, group);
        if (!KeysFit(next, move.Player, group, keyHexes))
            return MoveResult.Rejected(state, keyHexes.Count == 1
                ? $"Those buildings are not a town: it needs {TownPowerNeeded(state, move.Player)} power and {Config.TownBuildings} buildings ({Config.TownBuildingsWithSanctuary} with the sanctuary)."
                : $"Those buildings can't hold another key: they could not have been formed as {keyHexes.Count} separate towns.");
        return Edited(next);
    }

    private static MoveResult ApplyEditRemoveKey(GameState state, EditRemoveKey move)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        var index = state.PlacedKeys.ToList().FindIndex(key => key.Hex == move.Hex);
        if (index < 0)
            return MoveResult.Rejected(state, "There is no key on that hex.");
        var key = state.PlacedKeys[index];
        return Edited(RemoveKey(state, index));
    }

    // The key back on top of its stack in the bank; its faction loses the keys it gave.
    private static GameState RemoveKey(GameState state, int index)
    {
        var key = state.PlacedKeys[index];
        var owner = state[key.Owner];
        return state.WithPlayer(owner with { Keys = owner.Keys - KeyTiles.All[key.Tile.Id].Keys }) with
        {
            PlacedKeys = state.PlacedKeys.RemoveAt(index),
            KeyBank = state.KeyBank.SetItem(key.Tile.Id, state.KeyBank[key.Tile.Id].Add(key.Tile)),
        };
    }

    // ---------- Round, pass tiles, marks ----------

    private static MoveResult ApplySetRound(GameState state, SetRound move)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        if (move.Round < 1 || move.Round > Config.Rounds)
            return MoveResult.Rejected(state, "There is no such round.");
        if (move.Round == state.Round)
            return MoveResult.Rejected(state, $"Round {move.Round} is already the current round.");
        return Edited(state with { Round = move.Round });
    }

    private static MoveResult ApplyEditPassTile(GameState state, EditPassTile move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var slot = state.PassTileBank.IndexOf(move.Tile);
        if (slot < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");
        var player = state[move.Player];
        return Edited(state.WithPlayer(player with { PassTile = move.Tile }) with
        {
            PassTileBank = state.PassTileBank.SetItem(slot, player.PassTile),
        });
    }

    private static MoveResult ApplyEditPassTileCoins(GameState state, EditPassTileCoins move)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        if (state.PassTileBank.IndexOf(move.Tile) < 0)
            return MoveResult.Rejected(state, "That pass tile is not in the bank.");
        var coins = Math.Max(0, state.PassTileCoins[move.Tile] + move.Delta);
        if (coins == state.PassTileCoins[move.Tile])
            return MoveResult.Rejected(state, move.Delta == 0 ? "Nothing to change." : "That pass tile has no coins on it.");
        return Edited(state with { PassTileCoins = state.PassTileCoins.SetItem(move.Tile, coins) });
    }

    private static MoveResult ApplyMarkPassed(GameState state, MarkPassed move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Passed == state.HasPassed(move.Player))
            return MoveResult.Rejected(state, $"The {Name(move.Player)} are already {(move.Passed ? "passed" : "still in the round")}.");
        var order = move.Passed
            ? state.PassOrder.Add(move.Player)
            : state.PassOrder.RemoveAt(state.PassOrder.IndexOf(move.Player));
        return Edited(state with { PassOrder = order });
    }

    private static MoveResult ApplyTogglePowerAction(GameState state, TogglePowerAction move)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        if (move.Action < 0 || move.Action >= PowerActions.Count)
            return MoveResult.Rejected(state, "There is no such power action.");
        var taken = state.PowerActionsTaken.Contains(move.Action)
            ? state.PowerActionsTaken.RemoveAt(state.PowerActionsTaken.IndexOf(move.Action))
            : state.PowerActionsTaken.Add(move.Action);
        return Edited(state with { PowerActionsTaken = taken });
    }

    private static MoveResult ApplyToggleBonusAction(GameState state, ToggleBonusAction move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        var player = state[move.Player];
        if (player.BonusActionsUsed.Contains(move.Bonus))
            return Edited(state.WithPlayer(player with
            {
                BonusActionsUsed = player.BonusActionsUsed.RemoveAt(player.BonusActionsUsed.IndexOf(move.Bonus)),
            }));
        if (BonusActionProblem(state, move.Player, move.Bonus) is string problem)
            return MoveResult.Rejected(state, problem);
        return Edited(state.WithPlayer(player with { BonusActionsUsed = player.BonusActionsUsed.Add(move.Bonus) }));
    }

    // Why this bonus action can't have been used this round, or null: the faction must have it, the Witches' stronghold
    // action needs at least 3 buildings, and the Swarmlings' can't have been used with just one dwelling and the
    // stronghold.
    private static string? BonusActionProblem(GameState state, Faction faction, BonusActionId bonus)
    {
        if (!state[faction].HasUnlocked(bonus))
            return $"The {Name(faction)} don't have that bonus action.";
        if (bonus.Source != BonusSource.Stronghold)
            return null;
        var kinds = state.HexesBuiltOnBy(faction).Select(hex => state.Map[hex]!.Value.Kind).ToList();
        if (faction == Faction.Witches && kinds.Count < 3)
            return "The Witches' stronghold action needs at least 3 of their buildings on the map.";
        if (faction == Faction.Swarmlings && kinds.Count == 2 && kinds.Contains(PieceKind.Dwelling) && kinds.Contains(PieceKind.Stronghold))
            return "The Swarmlings' stronghold action can't have been used with only one dwelling and their stronghold.";
        return null;
    }

    // ---------- Wheels ----------

    private static MoveResult ApplyEditWheel(GameState state, EditWheel move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Riverwalkers)
            return MoveResult.Rejected(state, "Only the Riverwalkers have a terrain wheel to lock and unlock.");
        if (!Terrains.IsBasic(move.Terrain))
            return MoveResult.Rejected(state, "Only the basic terrains are on the wheel.");
        var player = state[move.Player];
        if (player.WheelPriests.Any(locked => locked.Terrain == move.Terrain))
            return Edited(UnlockOnWheel(state, move.Terrain));
        if (state.HexesBuiltOnBy(move.Player).Any(hex => state.HexTerrain[hex] == move.Terrain))
            return MoveResult.Rejected(state, $"The Riverwalkers have a building on {move.Terrain}, so it stays unlocked.");
        if (player.PriestSupply.Count == 0)
            return MoveResult.Rejected(state, "The Riverwalkers have no priest in their supply to lock it with.");
        var priest = player.PriestSupply[player.PriestSupply.Count - 1];
        return Edited(state.WithPlayer(player with
        {
            PriestSupply = player.PriestSupply.RemoveAt(player.PriestSupply.Count - 1),
            WheelPriests = player.WheelPriests.Add(new WheelPriest(move.Terrain, priest)),
        }));
    }

    private static MoveResult ApplyEditHome(GameState state, EditHome move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Shapeshifters)
            return MoveResult.Rejected(state, "Only the Shapeshifters move their home terrain.");
        if (!Terrains.IsBasic(move.Terrain) || state.TakenTerrains(except: move.Player).Contains(move.Terrain))
            return MoveResult.Rejected(state, $"{move.Terrain} is another faction's terrain.");
        if (state[move.Player].ShapeshifterHome == move.Terrain)
            return MoveResult.Rejected(state, $"The Shapeshifters' home is already {move.Terrain}.");
        return Edited(state.WithPlayer(state[move.Player] with { ShapeshifterHome = move.Terrain }));
    }

    // ---------- Fan factions ----------

    private static MoveResult ApplyEditLava(GameState state, EditLava move)
    {
        if (CheckEditing(state, Faction.KingdomOfEmber) is { } refusal)
            return refusal;
        if (!state.Layout.Contains(move.Hex) || !OwnsBuildingAt(state, Faction.KingdomOfEmber, move.Hex))
            return MoveResult.Rejected(state, $"Lava goes under one of the {Name(Faction.KingdomOfEmber)}' structures.");
        var lava = state.Lava ?? ValueList<int>.Repeat(0, state.Layout.HexCount);
        return Edited(state with { Lava = lava.SetItem(move.Hex, lava[move.Hex] + 1) });
    }

    private static MoveResult ApplyEditWyrmToken(GameState state, EditWyrmToken move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.ChildrenOfTheWyrm)
            return MoveResult.Rejected(state, "Only the Children of the Wyrm put power tokens on rivers.");
        if (!state.Layout.Contains(move.Hex) || state.HexTerrain[move.Hex] != Terrain.River)
            return MoveResult.Rejected(state, "Power tokens go on river hexes.");
        if (state.WyrmTokenHexes.Contains(move.Hex))
            return MoveResult.Rejected(state, "A power token already lies there.");
        var power = state[move.Player].Power;
        if (move.FromBowl is < 1 or > 3 || Bowl(power, move.FromBowl) == 0)
            return MoveResult.Rejected(state, "That bowl is empty.");
        power = move.FromBowl switch
        {
            1 => power with { Bowl1 = power.Bowl1 - 1 },
            2 => power with { Bowl2 = power.Bowl2 - 1 },
            _ => power with { Bowl3 = power.Bowl3 - 1 },
        };
        return Edited(state.WithPlayer(state[move.Player] with { Power = power }) with { WyrmTokens = state.WyrmTokenHexes.Add(move.Hex) });
    }

    private static MoveResult ApplyEditRock(GameState state, EditRock move)
    {
        if (CheckEditing(state, move.Player) is { } refusal)
            return refusal;
        if (move.Player != Faction.Geologists)
            return MoveResult.Rejected(state, "Only the Geologists have glowing rocks.");
        var events = new List<GameEvent>();
        if (UseRockSpade(state, move.Player, move.Terrain, events) is not GameState next)
            return MoveResult.Rejected(state, RockRule);
        return Edited(next, events);
    }

    // ---------- Done ----------

    private static MoveResult ApplyFinishSetup(GameState state)
    {
        if (CheckEditing(state) is { } refusal)
            return refusal;
        var issues = SetupIssues(state);
        if (issues.Count > 0)
            return new MoveResult(state, Array.Empty<GameEvent>(), issues.Select(issue => new Issue(Severity.Error, issue)).ToList());
        var events = new List<GameEvent>();
        return MoveResult.Ok(BeginOrdering(state, events, out var warnings), events, warnings);
    }
}
