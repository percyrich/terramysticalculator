using System.Text;
using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// Converter, continued: turns and rounds.
public sealed partial class Converter
{
    // ---------- Turns and rounds ----------

    // Before a move of f's: the offers are answered, round-end spades given up by those who didn't use them, and the
    // turns before f's ended (spades not spent are given up, then End turn). action: the move is the turn's action, so
    // f's own turn ends first if f has already acted (f again after everyone else has passed).
    private void EnsureActive(Faction f, bool action, Note n)
    {
        ResolveOffers(); // the offers are part of the action: answered before its rewards and the next move (Andrew 2026-11-23)
        if (S.Phase == Phase.RoundEndSpades)
            FinishRoundEndSpades(n);
        for (var guard = 0; guard < 12 && !_stopped; guard++)
        {
            var s = S;
            if (s.Phase != Phase.Playing || s.ActivePlayer is not Faction active)
                return;
            if (active == f && !(action && s.ActionTaken))
                return;
            if (s.SpadesPending)
            {
                Do(new FinishSpades(active), n);
                continue;
            }
            if (s[active].TokenOfferOwed) // the Shapeshifters' token: BGA's newPowerTokens would have come by now
            {
                Do(new AnswerTokenOffer(active, false), n);
                continue;
            }
            if (s[active].LandscapeOffers != LandscapeOffer.None) // what the landscape offered, not taken on BGA
            {
                Do(new DeclineLandscapeBonus(active), n);
                continue;
            }
            if (s[active].PriestChoicesOwed > 0) // the Riverwalkers keep the priest (an unlock would have come by now)
            {
                Do(new ChoosePriest(active, null), n);
                continue;
            }
            if (s[active].BridgesOwed > 0 && s[active].BridgesOptional) // free bridges not taken
            {
                Do(new SkipBridges(active), n);
                continue;
            }
            if (_deferred.Count > 0)
            {
                FlushDeferred();
                if (_deferred.Count > 0)
                {
                    Stop(n, $"Conversions held back from the {Name(_deferred[0].Player)}' action could not be made before their turn ended.");
                    return;
                }
                continue;
            }
            if (!s.ActionTaken)
            {
                Problem(n, $"BGA has the {Factions.Name(f)} act next, but in Core it is the {Factions.Name(active)}' turn and they have not acted");
                return; // the move itself will be refused and say why
            }
            if (!Do(new EndTurn(active), n))
                return;
        }
    }

    // The round's last turn ends (everyone has passed): spades given up, offers answered, End turn.
    private void EndOfRound(Note? n)
    {
        var round = S.Round;
        ResolveOffers();
        for (var guard = 0; guard < 12 && !_stopped; guard++)
        {
            var s = S;
            if (s.Phase != Phase.Playing || s.ActivePlayer is not Faction active || !s.ActionTaken)
                break;
            if (s.SpadesPending)
                Do(new FinishSpades(active), n);
            else if (s[active].TokenOfferOwed)
                Do(new AnswerTokenOffer(active, false), n);
            else if (s[active].LandscapeOffers != LandscapeOffer.None)
                Do(new DeclineLandscapeBonus(active), n);
            else if (s[active].PriestChoicesOwed > 0)
                Do(new ChoosePriest(active, null), n);
            else if (_deferred.Count > 0)
            {
                FlushDeferred();
                if (_deferred.Count > 0)
                    Stop(n, $"Conversions held back from the {Name(_deferred[0].Player)}' action could not be made before the round ended.");
            }
            else
                Do(new EndTurn(active), n);
        }
        if (!_stopped && S.SafeOpeningHeld && n is not null)
            SettleTreasury(n); // the Treasurers' cult reward into the Safe before it opens, then the next round
        if (!_stopped && S.Phase == Phase.Playing && S.Round == round && n is not null)
            Problem(n, $"BGA ends the round, but in Core the {Name(S.ActivePlayer)} still have a turn (passed so far: {string.Join(", ", S.PassOrder.Select(Factions.Name))})");
    }

    // Round-end spades not used in BGA are given up in Core.
    private void FinishRoundEndSpades(Note? n)
    {
        for (var guard = 0; guard < 12 && !_stopped && S.Phase == Phase.RoundEndSpades && S.ActivePlayer is Faction active; guard++)
            if (!TakeOwedCultSteps(active, n))
                Do(new FinishSpades(active), n);
    }

    // Between rounds: the spenders before this faction are done.
    private void RoundEndSpender(Faction f, Note n)
    {
        if (S.Phase == Phase.RoundEndSpades)
            SpenderIs(f, n);
    }

    private void SpenderIs(Faction f, Note n)
    {
        for (var guard = 0; guard < 12 && !_stopped && S.Phase == Phase.RoundEndSpades && S.ActivePlayer is Faction active && active != f; guard++)
            if (!TakeOwedCultSteps(active, n))
                Do(new FinishSpades(active), n);
    }

    // The Acolytes' spades are cult steps (CultStepsOwed): BGA logs them as advanceCultTrack, read in order. False if
    // none are owed.
    private bool TakeOwedCultSteps(Faction f, Note? n)
    {
        if (S[f].CultStepsOwed == 0)
            return false;
        var at = n?.Index ?? Math.Min(_pos, _g.Notes.Count - 1);
        var packet = _g.Notes[at].Packet;
        var id = _faction.FirstOrDefault(kv => kv.Value == f).Key;
        var step = _g.Notes.Where(m => m.Packet >= packet - 1 && m.Packet <= packet + 1)
            .FirstOrDefault(m => m.Type == "advanceCultTrack" && m.Str("player_id") == id && !_usedNotes.Contains(m.Index));
        if (step is null)
        {
            Stop(n, $"The {Name(f)} owe a cult step, but BGA logs none for them here.");
            return true;
        }
        // One note may hold several steps on one track (inc_value 2: 920031063 #310, 2 round-end spades): used up step
        // by step.
        var taken = _cultStepsTaken.GetValueOrDefault(step.Index) + 1;
        _cultStepsTaken[step.Index] = taken;
        if (taken >= Math.Max(1, step.Int("inc_value") ?? 1))
            _usedNotes.Add(step.Index);
        Do(new TakeCultStep(f, Tables.ToTrack(step.Str("cult_id") ?? step.Str("cult_name") ?? "")), step);
        return true;
    }

    private readonly Dictionary<int, int> _cultStepsTaken = new();
    private int? _treasuryOfferPacket;                             // the packet whose move made the Treasurers' Safe offer // advanceCultTrack note → steps taken from it so far

    // A game still being played on BGA: no final scoring in its log.
    private bool InProgress => !_g.Notes.Any(m => m.Type == "finalScoring");

    private void Finish()
    {
        EndOfRound(null); // a game in progress: the last turn ends too (BGA has moved on to the next player by then)
        if (S.Phase != Phase.Finished && !InProgress)
            Problem(null, $"After the last BGA notification Core's game has not ended: phase {S.Phase}, {Name(S.ActivePlayer)} to act.");
    }
}
