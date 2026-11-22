using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// Converter, continued: power offered to neighbours.
public sealed partial class Converter
{
    // ---------- Power offered to neighbours ----------

    // Answers every power offer Core is waiting on, in Core's order, with what BGA recorded for that player and hex (a
    // little further on in the log, as BGA asks everyone at once).
    private void ResolveOffers()
    {
        for (var guard = 0; guard < 40 && !_stopped && S.PowerOffers.Count > 0; guard++)
        {
            var offer = S.PowerOffers[0];
            var at = GameLog.HexName(S, offer.Hex);
            var found = FindDecision(offer.Player, at, offer.Hex);
            Note? decision = found is int k ? _g.Notes[k] : null;
            if (decision is not null)
                _consumed.Add(decision.Index);
            else
                Problem(_pos < _g.Notes.Count ? _g.Notes[_pos] : null,
                    $"Core offers the {Factions.Name(offer.Player)} {offer.Power} power from the {Factions.Name(offer.Builder)}' build at {at}; BGA records no answer, so it is declined");
            var accept = decision?.Type == "powerViaStructures";
            Do(accept ? new AcceptPower(offer.Player)
                : decision is not null && IsCoinInstead(decision) ? new TakeCoinInstead(offer.Player)
                : new DeclinePower(offer.Player), decision);
        }
    }

    // BGA's names for hexes in notes' "extra" ("E4", river hexes "R~D3"), from the notes giving both it and bts_id.
    private Dictionary<string, int>? _shownHexes;

    private int? ShownHex(string shown)
    {
        if (_shownHexes is null)
        {
            _shownHexes = new Dictionary<string, int>();
            foreach (var m in _g.Notes)
                if (m.Args["extra"] is not null && m.Str("bts_id") is string bts && m.Shown.Length > 0)
                    try { _shownHexes.TryAdd(m.Shown, Hex(bts)); }
                    catch (InvalidDataException) { }
        }
        return _shownHexes.TryGetValue(shown, out var hex) ? hex : null;
    }

    private int? FindDecision(Faction f, string hexName, int hex)
    {
        // Up to 6 packets on, not counting packets of nothing but messages (chat, timeouts: 855425167 #34–36, five of
        // them between the build and the Conspirators' answer).
        var start = Math.Min(_pos, _g.Notes.Count);
        var counted = new HashSet<int>();
        for (var i = start; i < _g.Notes.Count; i++)
        {
            var n = _g.Notes[i];
            if (n.Type != "justAMessage" || n.Str("player_id") is not null)
                counted.Add(n.Packet);
            if (counted.Count > 7)
                break;
            if (_consumed.Contains(i) || !IsDecision(n) || PlayerOf(n) != f)
                continue;
            if (n.Shown == hexName || string.IsNullOrEmpty(n.Shown) // older logs name no hex: the player's next answer
                || ShownHex(n.Shown) == hex) // a river hex ("R~D3")
                return i;
        }
        return null;
    }

    private static bool IsDecision(Note n) =>
        n.Type == "powerViaStructures" || n.Type == "justAMessage" && n.Log.Contains("declines getting Power via Structures")
        || IsCoinInstead(n);

    // The Nomads' Trading Camp: "earns 1 coin instead of taking Power (Special Landscape)".
    private static bool IsCoinInstead(Note n) => n.Type == "updateCounters" && n.Log.Contains("instead of taking Power");

    // A BGA power decision as it is reached in the log: answered with Core's offers if one is waiting; otherwise it
    // should be one Core settled by itself (1 power for nothing is taken without asking).
    private void Decision(Note n)
    {
        ResolveOffers();
        if (_consumed.Contains(n.Index) || _stopped)
            return;
        var f = PlayerOf(n);
        var took = n.Type == "powerViaStructures";
        var free = took && n.Amount("vp_price") == 0 && J.Amount(n.Args["power_income"]) <= 1;
        if (!free)
            Problem(n, took
                ? $"BGA has the {Name(f)} take power at {n.Shown}, but Core made no such offer then: {n}"
                : $"BGA has the {Name(f)} decline power at {n.Shown}; Core made no offer to answer (Core takes 1 power worth no VP without asking, and offers nothing with bowls 1 and 2 empty): {n}");
    }
}
