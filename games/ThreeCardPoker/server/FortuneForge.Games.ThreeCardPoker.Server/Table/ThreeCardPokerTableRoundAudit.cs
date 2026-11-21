using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Bots.ThreeCardPoker;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

public static class ThreeCardPokerTableRoundAudit
{
    public static RoundAuditCapture Capture(ThreeCardPokerTableState table, DateTime completedAtUtc)
    {
        var participants = table.Players.Where(player => player.TotalWagerCents > 0).Select(player =>
        {
            var wager = ThreeCardPokerTableEngine.TotalCommitted(player);
            return new RoundAuditParticipant(player.ActorId, player.DisplayName, !ThreeCardPokerManagedSeat.IsManaged(player),
                wager, player.PayoutCents, checked(player.PayoutCents - wager));
        }).ToArray();
        var findings = new List<RoundAuditFinding>
        {
            new("table-action-replay-pending", "warning", "The final cards and payout are retained with timestamped intermediate engine observations, but an independent legal-action replay has not yet been completed. This is incomplete coverage, not evidence of misconduct.")
        };
        if (table.RoundStartedAtUtc is null)
            findings.Add(new("round-start-unavailable", "warning", "This pre-existing hand did not record a distinct round start; no play duration is inferred from the table creation time."));
        if (participants.Any(player => player.WagerCents < 0 || player.PayoutCents < 0))
            findings.Add(new("negative-settlement", "error", "A recorded wager or payout is negative."));
        return RoundAuditCapture.Create(ThreeCardPokerModule.Descriptor.Id, $"{table.TableId}-round-{table.RoundNumber}", table.TableId, table.RoundNumber,
            table.RoundStartedAtUtc ?? completedAtUtc, completedAtUtc,
            new { table.TableId, table.RoundNumber, table.RoundSeed, table.RoundStartedAtUtc, table.Deck,
                table.NextCardIndex, table.DealerCards, table.Players, table.RoundAccountingSettled },
            "three-card-poker-final-cards-v1", "table-snapshot-v1", participants, findings);
    }
}
