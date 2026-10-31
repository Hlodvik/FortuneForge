using System.Globalization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record HoldemReplayRules(string EngineContractVersion, int SmallBlindCents,
    int BigBlindCents, int MaximumStackCents, int ActionDeadlineSeconds = 30);

internal sealed record HoldemActionReplayResult(bool Complete, IReadOnlyList<RoundAuditFinding> Findings);

/// <summary>
/// Reconstructs betting from initial stacks and private events without calling
/// the live engine's action, progression, or legal-action implementations.
/// Complete describes coverage, not whether the findings establish misconduct.
/// </summary>
internal static class HoldemActionReplay
{
    internal const string Version = "holdem-action-replay-v1";

    internal static HoldemActionReplayResult Check(CreditHoldemMatch match, HoldemReplayRules? recordedRules = null)
    {
        var findings = new List<RoundAuditFinding>();
        void Error(string code, string message) => findings.Add(new(code, "error", message));
        void Missing(string code, string message) => findings.Add(new(code, "warning", message));
        if (match.EventTimelineVersion != 1 || match.EventTimeline.Count == 0)
        {
            Missing("holdem-replay-timeline-unavailable", "This hand has no supported complete private timeline; prior decisions and their times cannot be reconstructed.");
            return new(false, findings);
        }
        var rules = recordedRules ?? FrozenRules(match.TableRuleId);
        var knownRules = FrozenRules(match.TableRuleId);
        if (rules is null || rules.EngineContractVersion is not ("credit-holdem-v1" or "credit-holdem-engine-v1") ||
            rules.SmallBlindCents <= 0 || rules.BigBlindCents < rules.SmallBlindCents ||
            rules.MaximumStackCents < rules.BigBlindCents || rules.ActionDeadlineSeconds <= 0 || knownRules is null ||
            rules.SmallBlindCents != knownRules.SmallBlindCents || rules.BigBlindCents != knownRules.BigBlindCents ||
            rules.MaximumStackCents != knownRules.MaximumStackCents || rules.ActionDeadlineSeconds != knownRules.ActionDeadlineSeconds)
        {
            Missing("holdem-replay-rules-unavailable", "The recorded engine/rule configuration is unsupported; current live rules were not substituted.");
            return new(false, findings);
        }
        if (match.Players.Count < 2 || !match.Players.Any(player => player.IsAccountBacked) ||
            match.Players.Select(player => player.ActorId).Distinct().Count() != match.Players.Count ||
            match.Players.Select(player => player.PublicSeatId).Distinct().Count() != match.Players.Count ||
            match.Players.Select(player => player.Seat).Distinct().Count() != match.Players.Count)
        {
            Error("holdem-replay-seat-identity", "A unique seated participant set is required for action replay.");
            return new(false, findings);
        }
        var seats = match.Players.OrderBy(player => player.Seat).Select(player => new Seat(player)).ToArray();
        var dealer = Array.FindIndex(seats, seat => seat.Player.Seat == match.DealerSeat);
        if (dealer < 0 || seats.Any(seat => seat.Stack < 0 || seat.Stack > rules.MaximumStackCents))
        {
            Error("holdem-replay-initial-state", "The recorded initial stacks or dealer position are invalid.");
            return new(false, findings);
        }
        var small = seats.Length == 2 ? dealer : (dealer + 1) % seats.Length;
        var big = (small + 1) % seats.Length;
        var active = seats.Length == 2 ? dealer : (big + 1) % seats.Length;
        long currentBet = rules.BigBlindCents;
        long minimumRaise = rules.BigBlindCents;
        var street = "preflop";
        string? expectedStreet = null;
        var runout = false;
        var completed = false;
        var blinds = 0;
        var actionIndex = 0;
        DateTime? turnDeadline = null;
        CreditHoldemAuditEvent? timeout = null;
        var leaveRequests = new HashSet<string>(StringComparer.Ordinal);
        var lastAt = match.StartedAtUtc;
        var sawStart = false;
        var sawCompletion = false;
        var sawSettlement = false;
        var sawMatchDeadline = false;

        for (var index = 0; index < match.EventTimeline.Count; index++)
        {
            var entry = match.EventTimeline[index];
            if (entry.Sequence != index + 1 || entry.HandNumber != match.HandNumber)
                Error("holdem-replay-event-order", "Private event sequence or hand identity is inconsistent.");
            if (entry.OccurredAtUtc.Kind != DateTimeKind.Utc || entry.OccurredAtUtc < lastAt)
                Error("holdem-replay-event-time", "Private event UTC times are missing or move backwards.");
            lastAt = entry.OccurredAtUtc;
            var actor = entry.ActorId is null ? null : seats.SingleOrDefault(seat => seat.Player.ActorId == entry.ActorId);
            if (entry.ActorId is not null && (actor is null || actor.Player.PublicSeatId != entry.PublicSeatId))
                Error("holdem-replay-actor-identity", "A private event does not identify its original seated actor.");
            switch (entry.Kind)
            {
                case "hand-started":
                    if (sawStart || index != 0 || entry.OccurredAtUtc != match.StartedAtUtc)
                        Error("holdem-replay-start", "The hand must have exactly one original start event.");
                    sawStart = true;
                    if (entry.Detail?.StartsWith("credit-holdem-engine-v1;", StringComparison.Ordinal) != true)
                        Missing("holdem-replay-engine-version", "The original engine version is absent or unsupported by this betting replay.");
                    break;
                case "wager-committed":
                {
                    var blind = blinds == 0 ? small : big;
                    var required = blinds == 0 ? rules.SmallBlindCents : rules.BigBlindCents;
                    var name = blinds == 0 ? "small-blind" : "big-blind";
                    if (blinds >= 2 || actor != seats[blind] || entry.Action != name || entry.Source != "server-deal" ||
                        entry.AmountCents != Math.Min(required, seats[blind].Stack))
                    {
                        Error("holdem-replay-blind", "Blind order, seat, or actual commitment differs from the recorded table rules.");
                        return new(false, findings);
                    }
                    Commit(seats[blind], entry.AmountCents!.Value);
                    MatchAction(entry);
                    CheckMoneySnapshot(entry, seats[blind]);
                    blinds++;
                    break;
                }
                case "turn-started":
                    if (blinds != 2 || completed || runout || expectedStreet is not null || actor != seats[active] || entry.Street != street)
                        Error("holdem-replay-turn", "A turn was assigned to the wrong participant or before legal street progression.");
                    if (!DateTime.TryParse(entry.Detail, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline) ||
                        deadline != entry.OccurredAtUtc.AddSeconds(rules.ActionDeadlineSeconds))
                        Error("holdem-replay-deadline", "A turn deadline does not match its server start time and recorded duration.");
                    else turnDeadline = deadline;
                    break;
                case "action-deadline-expired":
                    if (actor != seats[active] || turnDeadline is null || entry.OccurredAtUtc < turnDeadline)
                        Error("holdem-replay-timeout", "An automatic action is attributed to an unexpired or different turn.");
                    timeout = entry;
                    break;
                case "match-deadline-expired":
                    sawMatchDeadline = true;
                    if (entry.OccurredAtUtc < match.MatchDeadlineAtUtc)
                        Error("holdem-replay-match-deadline", "The hand deadline was declared expired before its recorded deadline.");
                    break;
                case "leave-requested":
                    if (actor is null || !actor.Player.IsAccountBacked)
                        Error("holdem-replay-leave", "A leave request is not attributed to an account-backed seated participant.");
                    else leaveRequests.Add(actor.Player.ActorId);
                    break;
                case "action-accepted":
                    if (actor is null || blinds != 2 || completed || runout || expectedStreet is not null || entry.Street != street)
                    {
                        Error("holdem-replay-action-phase", "An action was accepted outside an actionable betting street.");
                        return new(false, findings);
                    }
                    MatchAction(entry);
                    if (entry.Action == "left")
                    {
                        if (!actor.Player.IsAccountBacked || actor.Folded || !leaveRequests.Remove(actor.Player.ActorId) || entry.AmountCents != 0)
                            Error("holdem-replay-leave", "A leave action does not match a seated account's explicit leave request.");
                        var wasActive = actor == seats[active];
                        actor.Folded = true;
                        actor.Acted = true;
                        CheckMoneySnapshot(entry, actor);
                        if (wasActive || Contenders() <= 1 || AllAccountsFolded()) Progress();
                        break;
                    }
                    if (actor != seats[active] || actor.Folded || actor.AllIn)
                        Error("holdem-replay-out-of-turn", "A folded, all-in, or out-of-turn seat made a betting decision.");
                    var call = Math.Max(0, currentBet - actor.Round);
                    if (turnDeadline is null)
                        Missing("holdem-replay-turn-evidence-unavailable", "A betting decision has no original turn-start/deadline event.");
                    if (entry.Source == "server-timeout")
                    {
                        if (timeout is null || timeout.ActorId != entry.ActorId || timeout.OccurredAtUtc != entry.OccurredAtUtc ||
                            entry.Action != (call == 0 ? "check" : "fold"))
                            Error("holdem-replay-timeout-action", "An automatic decision differs from the expired turn's required check or fold.");
                    }
                    else if (entry.Source != (actor.Player.IsAccountBacked ? "account-action" : "automated-seat-action"))
                        Error("holdem-replay-action-source", "Decision provenance differs from the seated actor's account/automatic classification.");
                    if (entry.OccurredAtUtc >= match.MatchDeadlineAtUtc || entry.Source != "server-timeout" && turnDeadline is { } due && entry.OccurredAtUtc >= due)
                        Error("holdem-replay-late-action", "A betting action was accepted after its server deadline.");
                    timeout = null;
                    long committed = 0;
                    switch (entry.Action)
                    {
                        case "fold":
                            if (call == 0) Error("holdem-replay-illegal-fold", "A fold was accepted while the recorded rules permit only a check or raise.");
                            actor.Folded = true;
                            break;
                        case "check":
                            if (call != 0) Error("holdem-replay-illegal-check", "A check was accepted while the participant still owed a call.");
                            break;
                        case "call":
                            if (call == 0) Error("holdem-replay-illegal-call", "A call was accepted without an outstanding wager.");
                            committed = Math.Min(call, actor.Stack);
                            Commit(actor, committed);
                            break;
                        case "raise":
                        {
                            var target = entry.RequestedRaiseTo;
                            var maximum = actor.Round + actor.Stack;
                            var reopened = !actor.Acted || currentBet - actor.BetAtDecision >= actor.RaiseAtDecision;
                            if (!reopened || actor.Stack <= call || target is null || target <= currentBet || target > maximum ||
                                target < currentBet + minimumRaise && target != maximum)
                            {
                                Error("holdem-replay-illegal-raise", "A raise violates turn reopening, minimum increment, available stack, or short all-in rules.");
                                return new(!findings.Any(finding => finding.Severity == "warning"), findings);
                            }
                            var increment = target.Value - currentBet;
                            committed = target.Value - actor.Round;
                            Commit(actor, committed);
                            currentBet = target.Value;
                            if (increment >= minimumRaise)
                            {
                                minimumRaise = increment;
                                foreach (var other in seats.Where(seat => seat != actor && !seat.Folded && !seat.AllIn)) other.Acted = false;
                            }
                            break;
                        }
                        default:
                            Error("holdem-replay-action-type", "The action type is not supported by the recorded engine contract.");
                            return new(false, findings);
                    }
                    if (entry.AmountCents != committed)
                        Error("holdem-replay-commitment", "The accepted wager differs from the amount required by independent betting replay.");
                    actor.Acted = true;
                    actor.BetAtDecision = currentBet;
                    actor.RaiseAtDecision = minimumRaise;
                    CheckMoneySnapshot(entry, actor);
                    Progress();
                    break;
                case "street-started":
                {
                    var next = street switch { "preflop" => "flop", "flop" => "turn", "turn" => "river", _ => null };
                    if (entry.Street != next || !runout && expectedStreet != next)
                        Error("holdem-replay-street", "Community play advanced before betting closed or skipped a street.");
                    street = entry.Street;
                    if (!runout)
                    {
                        foreach (var seat in seats) { seat.Round = 0; seat.Acted = seat.Folded || seat.AllIn; seat.BetAtDecision = 0; seat.RaiseAtDecision = rules.BigBlindCents; }
                        currentBet = 0;
                        minimumRaise = rules.BigBlindCents;
                        active = Next(dealer);
                    }
                    expectedStreet = null;
                    break;
                }
                case "forced-completion":
                    if (entry.Detail == "match-deadline" && (!sawMatchDeadline || entry.OccurredAtUtc < match.MatchDeadlineAtUtc) ||
                        entry.Detail == "all-account-seats-folded" && !AllAccountsFolded())
                        Error("holdem-replay-forced-completion", "The claimed automatic completion reason is inconsistent with replayed state.");
                    if (entry.Detail is not ("match-deadline" or "all-account-seats-folded"))
                        Missing("holdem-replay-external-completion", "The host forced this hand to finish; its authorization requires host evidence outside the betting timeline.");
                    runout = true;
                    expectedStreet = null;
                    break;
                case "showdown-started":
                    if (!runout) Error("holdem-replay-premature-showdown", "Showdown began before legal betting completion or a recorded forced finish.");
                    break;
                case "hand-completed":
                    if (sawCompletion || !runout && Contenders() > 1)
                        Error("holdem-replay-premature-completion", "The hand completed before the replayed betting sequence allowed it.");
                    completed = sawCompletion = true;
                    break;
                case "financial-settlement-completed":
                    if (!completed || sawSettlement) Error("holdem-replay-settlement-order", "Financial settlement preceded hand completion or was recorded more than once.");
                    sawSettlement = true;
                    break;
                case "deck-created": case "hole-card-dealt": case "card-burned": case "community-card-dealt":
                case "showdown-revealed": case "showdown-completed": case "pot-awarded": case "financial-payout-recognized":
                    // Deck provenance, card awards, and cash reconciliation have
                    // independent checks in the surrounding round auditor.
                    break;
                default:
                    Missing("holdem-replay-unknown-event", "The private timeline contains an event outside this replay contract.");
                    break;
            }
        }
        if (!sawStart || !sawCompletion || !sawSettlement || match.Status != "completed" || !match.AccountingSettled)
            Missing("holdem-replay-lifecycle-incomplete", "The original start, hand completion, and financial completion are not all available.");
        if (blinds != 2 || actionIndex != match.ActionLog.Count)
            Error("holdem-replay-action-coverage", "The action log and private timeline do not contain the same complete action sequence.");
        foreach (var seat in seats)
            if (seat.Hand != seat.Player.CommittedHand || seat.Round != seat.Player.CommittedRound ||
                seat.Player.Status is not ("folded" or "active" or "all-in") || seat.Folded != (seat.Player.Status == "folded") ||
                !seat.Folded && seat.AllIn != (seat.Player.Status == "all-in"))
                Error("holdem-replay-final-state", "Replayed commitments or fold state differ from the final hand snapshot.");
        return new(!findings.Any(finding => finding.Severity == "warning"), findings);

        int Contenders() => seats.Count(seat => !seat.Folded);
        bool AllAccountsFolded() => seats.Where(seat => seat.Player.IsAccountBacked).All(seat => seat.Folded);
        int Next(int after)
        {
            for (var offset = 1; offset <= seats.Length; offset++)
            {
                var candidate = (after + offset) % seats.Length;
                if (!seats[candidate].Folded && !seats[candidate].AllIn) return candidate;
            }
            Error("holdem-replay-no-active-seat", "A betting turn requires a participant with chips remaining.");
            return after;
        }
        void Progress()
        {
            turnDeadline = null;
            if (AllAccountsFolded() || Contenders() <= 1) { runout = true; return; }
            var live = seats.Where(seat => !seat.Folded && !seat.AllIn).ToArray();
            if (live.All(seat => seat.Acted && seat.Round == currentBet))
            {
                if (street == "river" || live.Length <= 1) runout = true;
                else expectedStreet = street switch { "preflop" => "flop", "flop" => "turn", "turn" => "river", _ => null };
            }
            else active = Next(active);
        }
        void MatchAction(CreditHoldemAuditEvent entry)
        {
            if (actionIndex >= match.ActionLog.Count) { Error("holdem-replay-action-coverage", "A timeline action has no corresponding action-log entry."); return; }
            var action = match.ActionLog[actionIndex++];
            if (action.Sequence != actionIndex || action.HandNumber != match.HandNumber || action.PublicSeatId != entry.PublicSeatId ||
                action.Street != entry.Street || action.Action != entry.Action || action.Amount != entry.AmountCents ||
                action.OccurredAtUtc != entry.OccurredAtUtc || action.Source != entry.Source)
                Error("holdem-replay-action-log-mismatch", "An action's order, actor, wager, time, or provenance differs between the original logs.");
        }
        void CheckMoneySnapshot(CreditHoldemAuditEvent entry, Seat seat)
        {
            if (entry.StackAfter != seat.Stack || entry.CommittedRoundAfter != seat.Round ||
                entry.CommittedHandAfter != seat.Hand || entry.CurrentBetAfter != currentBet)
                Error("holdem-replay-money-snapshot", "A decision's recorded resulting money state differs from independent replay.");
        }
    }

    private static void Commit(Seat seat, long amount)
    {
        seat.Stack -= amount;
        seat.Round += amount;
        seat.Hand += amount;
        seat.AllIn = seat.Stack == 0;
    }

    private static HoldemReplayRules? FrozenRules(string id) => id switch
    {
        "friendly" => new("credit-holdem-v1", 25, 50, 2_500),
        "standard" => new("credit-holdem-v1", 50, 100, 10_000),
        "bold" => new("credit-holdem-v1", 200, 500, 50_000),
        _ => null
    };

    private sealed class Seat(CreditHoldemPlayer player)
    {
        internal CreditHoldemPlayer Player { get; } = player;
        internal long Stack { get; set; } = player.StartingStack;
        internal long Round { get; set; }
        internal long Hand { get; set; }
        internal bool Folded { get; set; }
        internal bool AllIn { get; set; }
        internal bool Acted { get; set; }
        internal long BetAtDecision { get; set; }
        internal long RaiseAtDecision { get; set; }
    }
}
