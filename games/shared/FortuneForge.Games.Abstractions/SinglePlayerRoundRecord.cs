using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.Abstractions.Analytics;

/// <summary>A settlement receipt and its private evidence, written atomically with the game's wallet settlement.</summary>
public sealed record SinglePlayerRoundRecord(GameAnalyticsOutboxDocument Settlement, RoundAuditCapture Audit)
{
    public static SinglePlayerRoundRecord Create(string gameId, string roundId, string userId,
        long wagerCents, long payoutCents, DateTime? startedAtUtc, DateTime completedAtUtc,
        string sourceCollection, string sourceDocumentId, object payload, string evidenceKind,
        bool? settlementVerified = null, string? outcomeAuditorVersion = null,
        IReadOnlyList<RoundAuditFinding>? additionalFindings = null)
    {
        var settlement = GameAnalyticsOutbox.Create(new(gameId, roundId, userId, "paid", true,
            completedAtUtc, startedAtUtc, wagerCents, payoutCents, sourceCollection, sourceDocumentId))!;
        var findings = new List<RoundAuditFinding>();
        if (settlementVerified is false)
            findings.Add(new("settlement-mismatch", "error", "The recorded settlement differs from the game rule calculation."));
        if (settlementVerified is null)
            findings.Add(new("outcome-replay-pending", "warning", "The private settlement receipt is retained, but an independent game outcome audit has not yet verified it."));
        if (additionalFindings is not null)
            findings.AddRange(additionalFindings);
        findings.Add(new("audit-scope", "info", "Evidence integrity and any named outcome checks do not certify absence of collusion, RNG bias, or account abuse."));
        var audit = RoundAuditCapture.Create(gameId, roundId, roundId, 1,
            startedAtUtc ?? completedAtUtc, completedAtUtc,
            new { sourceCollection, sourceDocumentId, wagerCents, payoutCents, netCents = checked(payoutCents - wagerCents), data = payload },
            evidenceKind, outcomeAuditorVersion ?? "server-settlement-receipt-v1", [new(userId, userId, true, wagerCents, payoutCents, checked(payoutCents - wagerCents))], findings);
        return new(settlement, audit);
    }
}
