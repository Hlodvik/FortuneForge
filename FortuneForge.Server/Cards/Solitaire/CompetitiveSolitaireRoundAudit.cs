using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Server.Cards.Solitaire;

internal static class CompetitiveSolitaireRoundAudit
{
    internal const string Version = "paid-solitaire-settlement-v1";

    internal static RoundAuditCapture Capture(SolitaireMatch match, IReadOnlyList<SolitairePlayerState> players,
        DateTime completedAtUtc)
    {
        const string houseFundedModel = "house-funded-bot-pool-v1";
        var findings = new List<RoundAuditFinding>
        {
            new("solitaire-command-replay-pending", "warning", "The authoritative boards and intermediate state observations are preserved, but an independent command-by-command replay has not yet been completed."),
            new("solitaire-claimable-payout", "info", "The result payout is the participant's claimable entitlement; the wallet credit occurs on a later claim command.")
        };
        var standings = players.OrderByDescending(player => player.Game.Score)
            .ThenBy(player => player.ElapsedMilliseconds ?? long.MaxValue)
            .ThenBy(player => player.Game.Moves)
            .ThenBy(player => player.CompletedAtUtc ?? DateTime.MaxValue)
            .ThenBy(player => player.UserId, StringComparer.Ordinal).ToArray();
        var independentlyRankedWinner = match.FinancialModel == houseFundedModel
            ? standings.FirstOrDefault()
            : standings.FirstOrDefault(player => player.IsAccountBacked);
        if (independentlyRankedWinner is null || match.WinnerUserId != independentlyRankedWinner.UserId)
            findings.Add(new("solitaire-winner-mismatch", "error", "The recorded winner differs from the independently ranked final boards and completion times."));
        if (players.Any(player => player.PayoutCents != (player.UserId == match.WinnerUserId ? match.WinnerPayoutCents : 0)))
            findings.Add(new("solitaire-payout-mismatch", "error", "A recorded entitlement differs from the single winner payout."));
        if (match.PrizePoolCents != checked(match.WinnerPayoutCents + match.PlatformFeeCents))
            findings.Add(new("solitaire-pool-mismatch", "error", "Winner payout and platform fee do not reconcile to the prize pool."));
        var humanCount = players.Count(player => player.IsAccountBacked);
        if (match.FinancialModel == houseFundedModel &&
            (match.HumanEntryCents != checked(humanCount * match.BuyInCents) ||
             match.HouseFundedEntryCents != checked((players.Count - humanCount) * match.BuyInCents)))
            findings.Add(new("solitaire-entry-funding-mismatch", "error", "Human and house entry funding do not match the participant seats."));

        var participants = players.Select(player =>
        {
            var wager = player.IsAccountBacked ? match.BuyInCents : 0L;
            return new RoundAuditParticipant(player.UserId, player.DisplayName, player.IsAccountBacked,
                wager, player.PayoutCents, checked(player.PayoutCents - wager));
        }).ToArray();
        var privatePlayers = players.Select(player => new
        {
            player.MatchId, player.UserId, player.DisplayName, player.Seat, player.Status,
            player.Game, player.Version, player.ElapsedMilliseconds, player.CompletedAtUtc,
            player.PayoutCents, player.Acknowledged, player.StartedAtUtc, player.DeadlineAtUtc,
            player.IsAccountBacked, player.PauseUsedMilliseconds, player.PausedAtUtc,
            player.IntegrityWarnings, player.HostMetadata
        }).ToArray();
        var payload = new
        {
            match, players = privatePlayers,
            sourceCollection = "solitaireMatches", sourceDocumentId = match.MatchId,
            financialClassification = match.FinancialModel,
            revenueDocumentId = match.FinancialModel == houseFundedModel ? match.MatchId : null,
            ranking = standings.Select(player => player.UserId).ToArray(),
            undoHistoryStoredInEngineJournal = true,
            engineVersion = typeof(SolitaireEngine).Assembly.GetName().Version?.ToString()
        };
        return RoundAuditCapture.Create("solitaire", match.MatchId, match.MatchId, 1,
            match.StartedAtUtc, completedAtUtc, payload, "paid-solitaire-match-v1", Version, participants, findings);
    }
}
