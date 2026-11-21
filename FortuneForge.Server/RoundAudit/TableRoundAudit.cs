using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;

namespace FortuneForge.Server.RoundAudit;

internal static class TableRoundAudit
{
    internal static RoundAuditCapture Capture(BlackjackTableState table, DateTime completedAtUtc) =>
        BlackjackTableRoundAudit.Capture(table, completedAtUtc);

    internal static RoundAuditCapture Capture(ThreeCardPokerTableState table, DateTime completedAtUtc) =>
        ThreeCardPokerTableRoundAudit.Capture(table, completedAtUtc);
}
