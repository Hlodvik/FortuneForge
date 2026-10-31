using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace FortuneForge.Games.Abstractions.Auditing;

public sealed record RoundAuditFinding(string Code, string Severity, string Message);

public sealed record RoundAuditParticipant(
    string ActorId, string DisplayName, bool IsAccountBacked,
    long WagerCents, long PayoutCents, long NetCents);

public sealed record RoundAuditSummary(
    string Id, string GameId, string RoundId, string TableId, int RoundNumber,
    DateTime StartedAtUtc, DateTime CompletedAtUtc, string AuditStatus,
    IReadOnlyList<RoundAuditFinding> Findings, IReadOnlyList<RoundAuditParticipant> Participants,
    bool EvidencePresent, DateTime? EvidenceDeleteAfterUtc, string AuditorVersion,
    DateTime? ReviewedAtUtc, string EvidenceSha256, string EvidenceKind);

// Only the server and privileged admin APIs may read this payload. It can contain
// folded/private cards and the server's complete shuffled deck.
public sealed record RoundAuditCapture(RoundAuditSummary Summary, string PayloadJson, GameRequestObservation? Request = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RoundAuditCapture Create(
        string gameId, string roundId, string tableId, int roundNumber,
        DateTime startedAtUtc, DateTime completedAtUtc, object payload,
        string evidenceKind, string auditorVersion,
        IReadOnlyList<RoundAuditParticipant> participants, IReadOnlyList<RoundAuditFinding> findings)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{gameId}\n{roundId}")));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var status = findings.Any(finding => finding.Severity == "error") ? "flagged"
            : findings.Any(finding => finding.Severity == "warning") ? "incomplete" : "passed";
        return new(new(id, gameId, roundId, tableId, roundNumber, startedAtUtc, completedAtUtc,
            status, findings.ToArray(), participants.ToArray(), true, null, auditorVersion, null, digest, evidenceKind), json, GameSecurityContext.Current);
    }
}

public static class RoundAuditDocuments
{
    public const string Summaries = "gameRoundAudits";
    public const string Evidence = "gameRoundEvidence";

    // Called inside the same transaction that recognizes the original settlement.
    // CREATE protects the first evidence snapshot from later retries or table resets.
    public static (Dictionary<string, object> Summary, Dictionary<string, object> Evidence) Create(RoundAuditCapture capture)
    {
        var value = capture.Summary;
        var summary = new Dictionary<string, object>
        {
            ["id"] = value.Id, ["gameId"] = value.GameId, ["roundId"] = value.RoundId,
            ["tableId"] = value.TableId, ["roundNumber"] = value.RoundNumber,
            ["startedAtUtc"] = value.StartedAtUtc,
            ["completedAtUtc"] = value.CompletedAtUtc,
            ["auditStatus"] = value.AuditStatus,
            ["findings"] = value.Findings.Select(finding => new Dictionary<string, object>
            {
                ["code"] = finding.Code, ["severity"] = finding.Severity, ["message"] = finding.Message
            }).ToArray(),
            ["participants"] = value.Participants.Select(participant => new Dictionary<string, object>
            {
                ["actorId"] = participant.ActorId, ["displayName"] = participant.DisplayName,
                ["isAccountBacked"] = participant.IsAccountBacked, ["wagerCents"] = participant.WagerCents,
                ["payoutCents"] = participant.PayoutCents, ["netCents"] = participant.NetCents
            }).ToArray(),
            ["evidencePresent"] = true, ["evidenceDeleteAfterUtc"] = null!,
            ["auditorVersion"] = value.AuditorVersion, ["reviewedAtUtc"] = null!,
            ["evidenceSha256"] = value.EvidenceSha256, ["evidenceKind"] = value.EvidenceKind,
            ["schemaVersion"] = 1L, ["retentionState"] = "unreviewed",
            ["retentionReviewNextAttemptAtUtc"] = value.CompletedAtUtc,
            ["journalManifestExpected"] = true,
            ["securityAuditPending"] = true,
            ["securityAuditNextAttemptAtUtc"] = value.CompletedAtUtc
        };
        var evidence = new Dictionary<string, object>
        {
            ["id"] = value.Id, ["gameId"] = value.GameId, ["roundId"] = value.RoundId,
            ["payloadJson"] = capture.PayloadJson, ["sha256"] = value.EvidenceSha256,
            ["evidenceKind"] = value.EvidenceKind,
            ["capturedAtUtc"] = value.CompletedAtUtc, ["schemaVersion"] = 1L,
            ["requestId"] = capture.Request?.RequestId ?? "",
            ["requestJson"] = capture.Request is null ? null! : JsonSerializer.Serialize(capture.Request, RoundAuditCapture.Json)
        };
        return (summary, evidence);
    }
}
