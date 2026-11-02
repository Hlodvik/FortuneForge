using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Server.RoundAudit;

internal static class HoldemRoundAudit
{
    internal const string AuditorVersion = "holdem-integrity-v1";

    internal static RoundAuditCapture Capture(CreditHoldemMatch match, DateTime settledAtUtc)
    {
        var findings = Check(match).Concat(FortuneForge.Server.SecurityAudit.HoldemActionReplay.Check(match).Findings).ToArray();
        var participants = match.Players.Select(player => new RoundAuditParticipant(
            player.ActorId, player.DisplayName, player.IsAccountBacked, player.CommittedHand,
            player.IsAccountBacked ? player.AccountPayoutCents : player.WonHandChips,
            (player.IsAccountBacked ? player.AccountPayoutCents : player.WonHandChips) - player.CommittedHand)).ToArray();
        return RoundAuditCapture.Create("texas-holdem", $"{match.MatchId}-hand-{match.HandNumber}", match.MatchId,
            match.HandNumber, match.StartedAtUtc, match.CompletedAtUtc ?? settledAtUtc,
            new
            {
                match.MatchId, match.HandNumber, match.TableRuleId, match.DealSeed, match.Deck,
                engineVersion = CreditHoldemEngine.AuditEngineVersion,
                ruleConfiguration = CreditHoldemTableRules.Resolve(match.TableRuleId),
                match.Version, match.PartitionKey, match.ActiveSeat, match.CurrentBet, match.MinimumRaise,
                match.DealerSeat, match.Community, match.NextCardIndex, match.StartedAtUtc,
                match.MatchDeadlineAtUtc, match.CompletedAtUtc, match.Status, match.Street,
                match.UsesBuyInEscrow, match.AccountingSettled, match.HumanCommittedCents,
                match.HumanPayoutCents, match.HouseNetCents, match.HumanPayoutsCents,
                players = match.Players.Select(player => new
                {
                    player.ActorId, player.PublicSeatId, player.DisplayName, player.IsAccountBacked,
                    player.Seat, player.StartingStack, player.Stack, player.HoleCards,
                    player.CommittedHand, player.CommittedRound, player.Status, player.LastAction,
                    player.WonHandChips, player.AccountPayoutCents, player.EscrowTicketId,
                    player.RevealAtShowdown
                }).ToArray(),
                actions = match.ActionLog.ToArray(),
                match.EventTimelineVersion, events = match.EventTimeline.ToArray(),
                leavingActorIds = match.LeavingActorIds.ToArray(),
                cashedOutReservations = match.EscrowCashedOutTicketIds.ToArray()
            }, "holdem-hand-v1", AuditorVersion, participants, findings);
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(CreditHoldemMatch match)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        if (match.Status != "completed" || !match.AccountingSettled)
            Flag("unsettled-round", "The recorded hand was not fully settled.");
        if (match.CompletedAtUtc is null || match.CompletedAtUtc < match.StartedAtUtc)
            Flag("invalid-round-time", "Round completion is missing or predates the deal.");
        if (match.Players.Select(player => player.ActorId).Distinct().Count() != match.Players.Count ||
            match.Players.Select(player => player.PublicSeatId).Distinct().Count() != match.Players.Count ||
            match.Players.Select(player => player.Seat).Distinct().Count() != match.Players.Count)
            Flag("duplicate-seat", "The hand has duplicate actor, public-seat, or seat identifiers.");
        var expectedDeck = TexasHoldemRules.CreateDeck(match.DealSeed);
        if (!match.Deck.SequenceEqual(expectedDeck))
            Flag("deck-seed-mismatch", "The stored deck does not match its recorded shuffle seed.");
        var validCards = expectedDeck.ToHashSet(StringComparer.Ordinal);
        var visibleCards = match.Players.SelectMany(player => player.HoleCards).Concat(match.Community).ToArray();
        var cardsValid = visibleCards.All(validCards.Contains) && visibleCards.Distinct().Count() == visibleCards.Length;
        if (!cardsValid) Flag("invalid-dealt-cards", "Dealt cards are invalid or duplicated.");
        var ordered = match.Players.OrderBy(player => player.Seat).ToArray();
        var dealerIndex = Array.FindIndex(ordered, player => player.Seat == match.DealerSeat);
        if (ordered.Length == 0 || dealerIndex < 0) Flag("invalid-dealer", "The dealer position is not occupied.");
        else
        {
            var first = ordered.Length == 2 ? dealerIndex : (dealerIndex + 1) % ordered.Length;
            for (var offset = 0; offset < ordered.Length; offset++)
            {
                var player = ordered[(first + offset) % ordered.Length];
                if (player.HoleCards.Count != 2 || offset + ordered.Length >= expectedDeck.Count ||
                    !player.HoleCards.SequenceEqual(new[] { expectedDeck[offset], expectedDeck[offset + ordered.Length] }))
                    Flag("hole-card-deal-mismatch", $"Seat {player.PublicSeatId} does not have the cards from the recorded deal.");
            }
            var next = ordered.Length * 2;
            var expectedCommunity = new List<string>();
            foreach (var count in new[] { 3, 1, 1 })
            {
                if (expectedCommunity.Count >= match.Community.Count) break;
                next++;
                if (next + count > expectedDeck.Count) break;
                expectedCommunity.AddRange(expectedDeck.Skip(next).Take(count));
                next += count;
            }
            if (match.Community.Count is not (0 or 3 or 4 or 5) || !match.Community.SequenceEqual(expectedCommunity) || match.NextCardIndex != next)
                Flag("board-deal-mismatch", "Community cards or deck position do not match the deal and burn-card sequence.");
        }

        if (match.ActionLog.Count == 0)
            findings.Add(new("missing-actions", "warning", "This legacy hand has no complete action log."));
        var seats = match.Players.Select(player => player.PublicSeatId).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < match.ActionLog.Count; index++)
        {
            var action = match.ActionLog[index];
            if (action.Sequence != index + 1 || action.HandNumber != match.HandNumber || !seats.Contains(action.PublicSeatId))
                Flag("invalid-action-order", "An action has an invalid sequence, hand number, or seat reference.");
            if (action.Amount < 0 || action.Action is not ("small-blind" or "big-blind" or "fold" or "check" or "call" or "raise" or "left") ||
                action.Action is "fold" or "check" or "left" && action.Amount != 0)
                Flag("invalid-action-amount", "An action type or committed amount is inconsistent.");
        }
        foreach (var player in match.Players)
        {
            if (match.ActionLog.Count > 0 && match.ActionLog.Where(action => action.PublicSeatId == player.PublicSeatId).Sum(action => (long)action.Amount) != player.CommittedHand)
                Flag("action-wager-mismatch", $"Seat {player.PublicSeatId}'s action amounts do not add up to its wager.");
            if (player.StartingStack < 0 || player.CommittedHand < 0 || player.WonHandChips < 0 || player.Stack < 0 ||
                (long)player.StartingStack - player.CommittedHand + player.WonHandChips != player.Stack)
                Flag("stack-conservation", $"Seat {player.PublicSeatId}'s chips do not reconcile.");
        }
        if (match.Players.Sum(player => (long)player.CommittedHand) != match.Players.Sum(player => (long)player.WonHandChips))
            Flag("pot-conservation", "The chips awarded do not equal all chips committed to the pot.");

        if (cardsValid && match.Players.All(player => player.HoleCards.Count == 2) && match.Community.Count is 0 or 3 or 4 or 5)
        {
            try
            {
                var expected = ExpectedAwards(match, accountFundedOnly: false);
                foreach (var player in match.Players)
                    if (expected.GetValueOrDefault(player.ActorId) != player.WonHandChips)
                        Flag("award-mismatch", $"Seat {player.PublicSeatId}'s award does not match its eligible pot and hand rank.");
                var cash = match.UsesBuyInEscrow ? expected : ExpectedAwards(match, accountFundedOnly: true);
                foreach (var player in match.Players.Where(player => player.IsAccountBacked))
                    if (cash.GetValueOrDefault(player.ActorId) != player.AccountPayoutCents ||
                        match.HumanPayoutsCents.GetValueOrDefault(player.ActorId) != player.AccountPayoutCents)
                        Flag("human-payout-mismatch", $"Seat {player.PublicSeatId}'s recognized payout does not reconcile.");
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
            {
                Flag("unverifiable-payout", "The recorded hand could not be evaluated for pot settlement.");
            }
        }
        var wagers = match.Players.Where(player => player.IsAccountBacked).Sum(player => (long)player.CommittedHand);
        var payouts = match.Players.Where(player => player.IsAccountBacked).Sum(player => player.AccountPayoutCents);
        if (match.HumanCommittedCents != wagers || match.HumanPayoutCents != payouts || match.HouseNetCents != wagers - payouts)
            Flag("accounting-totals", "Recognized human wagers, payouts, and house net do not reconcile.");
        findings.Add(new("audit-scope", "info", "Checks deal provenance, recorded actions and amounts, hand ranks, pot awards, and accounting consistency. It does not determine intent or collusion."));
        return findings;
    }

    private static Dictionary<string, long> ExpectedAwards(CreditHoldemMatch match, bool accountFundedOnly)
    {
        var awards = match.Players.ToDictionary(player => player.ActorId, _ => 0L, StringComparer.Ordinal);
        var contenders = match.Players.Where(player => player.Status != "folded").ToArray();
        if (!accountFundedOnly && contenders.Length == 1 && match.Community.Count < 5)
        {
            awards[contenders[0].ActorId] = match.Players.Sum(player => (long)player.CommittedHand);
            return awards;
        }
        var previous = 0;
        foreach (var level in match.Players.Select(player => player.CommittedHand).Where(amount => amount > 0).Distinct().Order())
        {
            var contributors = match.Players.Where(player => player.CommittedHand >= level).ToArray();
            var pot = (long)(level - previous) * contributors.Count(player => !accountFundedOnly || player.IsAccountBacked);
            previous = level;
            var eligible = contributors.Where(player => player.Status != "folded").ToArray();
            if (eligible.Length == 0) continue;
            var best = eligible.Max(player => match.Community.Count == 5
                ? TexasHoldemRules.Evaluate(player.HoleCards.Concat(match.Community).ToArray()).Score : 0UL);
            var winners = eligible.Where(player => (match.Community.Count == 5
                    ? TexasHoldemRules.Evaluate(player.HoleCards.Concat(match.Community).ToArray()).Score : 0UL) == best)
                .OrderBy(player => (player.Seat - match.DealerSeat - 1 + CreditHoldemMoney.MaximumSeats) % CreditHoldemMoney.MaximumSeats).ToArray();
            for (var index = 0; index < winners.Length; index++)
                awards[winners[index].ActorId] += pot / winners.Length + (index < pot % winners.Length ? 1 : 0);
        }
        return awards;
    }
}
