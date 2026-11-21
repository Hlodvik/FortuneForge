using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.LiarsDice;
namespace FortuneForge.Server.SecurityAudit;
internal static class LiarsDiceEvidenceReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) => LegacyLiarsDiceEvidenceReplayAudit.Supports(summary) || LiarsDiceSessionReplay.Supports(summary);
    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string? payloadJson,
        IReadOnlyList<string> eventPayloads) => LiarsDiceSessionReplay.Supports(summary) ? LiarsDiceSessionReplay.Inspect(summary, payloadJson)
            : LegacyLiarsDiceEvidenceReplayAudit.Inspect(summary, payloadJson, eventPayloads);
}
