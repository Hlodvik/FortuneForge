using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.Games.Hearts;

internal static class PaidHeartsRoundAudit
{
    internal const string Version = "paid-hearts-settlement-v1";

    internal static RoundAuditCapture Capture(PaidHeartsMatch match, bool abandoned, DateTime completedAtUtc)
    {
        var findings = new List<RoundAuditFinding>
        {
            new("hearts-command-replay-pending", "warning", "All available engine states are journaled with server times, but an independent full-match card/action replay has not yet been completed.")
        };
        var expected = new Dictionary<string, long>(StringComparer.Ordinal);
        var eligible = match.Players.Where(player => !player.HasLeft || !player.IsAccountBacked).ToArray();
        if (!abandoned && eligible.Length > 0 && match.State is { } state)
        {
            var lowest = eligible.Min(player => state.Score.For(player.Seat));
            var winners = eligible.Where(player => state.Score.For(player.Seat) == lowest)
                .OrderBy(player => PaidHeartsIds.Hash($"tie\n{match.Id}\n{player.UserId}"), StringComparer.Ordinal).ToArray();
            for (var index = 0; index < winners.Length; index++)
                expected[winners[index].UserId] = checked(match.PotCents / winners.Length +
                    (index < match.PotCents % winners.Length ? 1 : 0));
        }
        else if (!abandoned)
            findings.Add(new("hearts-final-state-missing", "error", "A paid Hearts result has no final match score or eligible participant."));

        if (match.Players.Any(player => player.PayoutCents != expected.GetValueOrDefault(player.UserId)))
            findings.Add(new("hearts-award-mismatch", "error", "A recorded player or managed-seat award differs from the independent score and tie split."));
        var returned = match.Players.Sum(player => player.PayoutCents);
        if (returned + (abandoned ? match.PotCents : 0L) != match.PotCents)
            findings.Add(new("hearts-pool-mismatch", "error", "The recorded awards and forfeiture do not account for the entire prize pool."));
        var participants = match.Players.Select(player =>
        {
            var wager = player.IsAccountBacked ? match.EntryCents : 0L;
            return new RoundAuditParticipant(player.UserId, player.DisplayName, player.IsAccountBacked,
                wager, player.PayoutCents, checked(player.PayoutCents - wager));
        }).ToArray();
        var payload = new
        {
            match, abandoned, financialClassification = "house-funded-hearts-pool-v1",
            sourceCollection = "heartsPaidMatches", sourceDocumentId = match.Id,
            humanEntryCents = match.Players.Count(player => player.IsAccountBacked) * match.EntryCents,
            houseFundedEntryCents = match.PotCents - match.Players.Count(player => player.IsAccountBacked) * match.EntryCents,
            forfeitedPoolCents = abandoned ? match.PotCents : 0L,
            revenueDocumentId = match.Id,
            payoutLedgerIds = match.Players.Where(player => player.IsAccountBacked && player.PayoutCents > 0)
                .Select(player => $"{match.Id}-payout-{PaidHeartsIds.Hash(player.UserId)}").ToArray(),
            engineVersion = typeof(FortuneForge.Games.Hearts.HeartsMatchEngine).Assembly.GetName().Version?.ToString()
        };
        return RoundAuditCapture.Create("hearts", match.Id, match.Id, match.State?.RoundNumber ?? 1,
            match.CreatedAtUtc, completedAtUtc, payload, "paid-hearts-match-v1", Version, participants, findings);
    }
}
