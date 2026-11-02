using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.Bots.Blackjack;
using FortuneForge.Server.Bots.ThreeCardPoker;

namespace FortuneForge.Server.RoundAudit;

internal static class TableRoundAudit
{
    internal static RoundAuditCapture Capture(BlackjackTableState table, DateTime completedAtUtc)
    {
        var participants = table.Players.Where(player => player.TotalWagerCents > 0).Select(player =>
        {
            var wager = BlackjackTableEngine.TotalCommitted(player);
            return new RoundAuditParticipant(player.ActorId, player.DisplayName, !BlackjackManagedSeat.IsManaged(player),
                wager, player.PayoutCents, player.PayoutCents - wager);
        }).ToArray();
        return Snapshot("blackjack", table.TableId, table.RoundNumber, table.RoundStartedAtUtc, completedAtUtc,
            new { table.TableId, table.RoundNumber, table.RoundSeed, table.RoundStartedAtUtc, table.Deck,
                table.NextCardIndex, table.DealerCards, table.Players, table.RoundAccountingSettled }, participants);
    }

    internal static RoundAuditCapture Capture(ThreeCardPokerTableState table, DateTime completedAtUtc)
    {
        var participants = table.Players.Where(player => player.TotalWagerCents > 0).Select(player =>
        {
            var wager = ThreeCardPokerTableEngine.TotalCommitted(player);
            return new RoundAuditParticipant(player.ActorId, player.DisplayName, !ThreeCardPokerManagedSeat.IsManaged(player),
                wager, player.PayoutCents, player.PayoutCents - wager);
        }).ToArray();
        return Snapshot("three-card-poker", table.TableId, table.RoundNumber, table.RoundStartedAtUtc, completedAtUtc,
            new { table.TableId, table.RoundNumber, table.RoundSeed, table.RoundStartedAtUtc, table.Deck,
                table.NextCardIndex, table.DealerCards, table.Players, table.RoundAccountingSettled }, participants);
    }

    private static RoundAuditCapture Snapshot(string gameId, string tableId, int roundNumber,
        DateTime? startedAtUtc, DateTime completedAtUtc, object payload, IReadOnlyList<RoundAuditParticipant> participants)
    {
        var findings = new List<RoundAuditFinding>
        {
            new("table-action-replay-pending", "warning", "The final cards and payout are retained with timestamped intermediate engine observations, but an independent legal-action replay has not yet been completed. This is incomplete coverage, not evidence of misconduct.")
        };
        if (startedAtUtc is null)
            findings.Add(new("round-start-unavailable", "warning", "This pre-existing hand did not record a distinct round start; no play duration is inferred from the table creation time."));
        if (participants.Any(player => player.WagerCents < 0 || player.PayoutCents < 0))
            findings.Add(new("negative-settlement", "error", "A recorded wager or payout is negative."));
        return RoundAuditCapture.Create(gameId, $"{tableId}-round-{roundNumber}", tableId, roundNumber,
            startedAtUtc ?? completedAtUtc, completedAtUtc, payload, $"{gameId}-final-cards-v1",
            "table-snapshot-v1", participants, findings);
    }
}
