using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Admin.Operations;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.RoundAudit;

public sealed record GameRoundAuditPage(IReadOnlyList<RoundAuditSummary> Items, string? NextCursor);
public sealed record GameRoundAuditDetail(RoundAuditSummary Summary, string? PayloadJson, string RetentionState, string? RetentionNote);
internal interface IGameRoundAuditStore
{
    Task<GameRoundAuditPage> ListAsync(string? gameId, string? status, int limit, string? cursor, CancellationToken ct);
    Task<GameRoundAuditDetail?> ReadAsync(string id, CancellationToken ct);
}

internal sealed class FirestoreGameRoundAuditStore(FirestoreDb database, IOptions<AdminOperationsOptions> options) : IGameRoundAuditStore
{
    private readonly AdminOperationsCursor cursors = new(options.Value.CursorSigningKey);
    public async Task<GameRoundAuditPage> ListAsync(string? gameId, string? status, int limit, string? cursor, CancellationToken ct)
    {
        if (limit is < 1 or > 100) throw new AdminOperationsQueryException("Choose a page size from 1 to 100.");
        if (gameId is not null && !FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.GameIds.Contains(gameId) &&
            gameId is not ("asteroids" or "flappy" or "horse-flight"))
            throw new AdminOperationsQueryException("Choose a catalogue game.");
        if (status is not null && status is not ("passed" or "flagged" or "incomplete" or "pending"))
            throw new AdminOperationsQueryException("Choose passed, flagged, incomplete, or pending.");
        Query query = database.Collection(RoundAuditDocuments.Summaries);
        if (gameId is not null) query = query.WhereEqualTo("gameId", gameId);
        if (status is not null) query = query.WhereEqualTo("auditStatus", status);
        query = query.OrderByDescending("completedAtUtc").OrderByDescending(FieldPath.DocumentId);
        var scope = $"round-audits:{gameId}:{status}";
        if (cursor is not null)
        {
            var after = cursors.Decode(scope, cursor);
            ValidateId(after.Id);
            query = query.StartAfter(Timestamp.FromDateTime(after.OccurredAtUtc), after.Id);
        }
        var documents = (await query.Limit(limit + 1).GetSnapshotAsync(ct)).Documents;
        var page = documents.Take(limit).Select(Summary).ToArray();
        return new(page, documents.Count > limit ? cursors.Encode(scope, page[^1].CompletedAtUtc, page[^1].Id) : null);
    }

    public async Task<GameRoundAuditDetail?> ReadAsync(string id, CancellationToken ct)
    {
        ValidateId(id);
        var documents = await Task.WhenAll(
            database.Collection(RoundAuditDocuments.Summaries).Document(id).GetSnapshotAsync(ct),
            database.Collection(RoundAuditDocuments.Evidence).Document(id).GetSnapshotAsync(ct));
        if (!documents[0].Exists) return null;
        return new(Summary(documents[0]), documents[1].Exists ? Text(documents[1], "payloadJson") : null,
            Text(documents[0], "retentionState", "unreviewed"), documents[0].TryGetValue<string>("retentionNote", out var note) ? note : null);
    }

    internal static void ValidateId(string id)
    {
        if (id.Length != 64 || id.Any(value => !char.IsAsciiHexDigit(value))) throw new AdminOperationsQueryException("Enter a valid audit identifier.");
    }
    internal static string Text(DocumentSnapshot doc, string key, string fallback = "") => doc.TryGetValue<string>(key, out var value) ? value : fallback;
    internal static DateTime? Time(DocumentSnapshot doc, string key) => doc.TryGetValue<Timestamp?>(key, out var value) ? value?.ToDateTime() : null;
    internal static RoundAuditSummary Summary(DocumentSnapshot doc) => new(
        doc.Id, Text(doc, "gameId"), Text(doc, "roundId"), Text(doc, "tableId"), checked((int)doc.GetValue<long>("roundNumber")),
        Time(doc, "startedAtUtc")!.Value, Time(doc, "completedAtUtc")!.Value, Text(doc, "auditStatus"),
        Maps(doc, "findings").Select(value => new RoundAuditFinding(String(value, "code"), String(value, "severity"), String(value, "message"))).ToArray(),
        Maps(doc, "participants").Select(value => new RoundAuditParticipant(String(value, "actorId"), String(value, "displayName"),
            value.GetValueOrDefault("isAccountBacked") is true, Number(value, "wagerCents"), Number(value, "payoutCents"), Number(value, "netCents"))).ToArray(),
        doc.TryGetValue<bool>("evidencePresent", out var present) && present, Time(doc, "evidenceDeleteAfterUtc"),
        Text(doc, "auditorVersion"), Time(doc, "reviewedAtUtc"), Text(doc, "evidenceSha256"), Text(doc, "evidenceKind"));
    private static IEnumerable<Dictionary<string, object>> Maps(DocumentSnapshot doc, string key) =>
        doc.TryGetValue<object[]>(key, out var values) ? values.OfType<Dictionary<string, object>>() : [];
    private static string String(Dictionary<string, object> value, string key) => value.GetValueOrDefault(key) as string ?? "";
    private static long Number(Dictionary<string, object> value, string key) => Convert.ToInt64(value.GetValueOrDefault(key) ?? 0, CultureInfo.InvariantCulture);
}

public sealed class GameRoundRetentionOptions
{
    public bool DeleteCleanEvidence { get; set; } = true;
    public int CleanEvidenceDays { get; set; } = 7;
    public int BatchSize { get; set; } = 50;
    public int PollSeconds { get; set; } = 300;
}

internal sealed record EvidenceIntegrityResult(bool Valid, string Code, string Message);
internal static class GameRoundEvidenceIntegrity
{
    internal static EvidenceIntegrityResult Check(RoundAuditSummary summary, string? payload, string? storedDigest)
    {
        if (payload is null) return new(false, "missing-evidence", "Expected private round evidence is missing.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        if (digest != summary.EvidenceSha256 || digest != storedDigest)
            return new(false, "evidence-changed", "Private round evidence differs from the settlement fingerprint.");
        if (summary.Participants.Any(player => player.WagerCents < 0 || player.PayoutCents < 0 || player.NetCents != player.PayoutCents - player.WagerCents))
            return new(false, "invalid-money-summary", "Participant wager, return, and net amounts do not reconcile.");
        return new(true, "verified", "Evidence fingerprint and participant accounting are consistent.");
    }
}

internal sealed class GameRoundRetentionService(FirestoreDb database, IOptions<GameRoundRetentionOptions> options, TimeProvider clock)
{
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var policy = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await database.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("retentionState", "unreviewed")
            .WhereLessThanOrEqualTo("retentionReviewNextAttemptAtUtc", Timestamp.FromDateTime(now))
            .Limit(policy.BatchSize).GetSnapshotAsync(ct);
        foreach (var document in pending.Documents) await VerifyAsync(document.Id, now, policy, ct);
        if (!policy.DeleteCleanEvidence) return pending.Count;
        var due = await database.Collection(RoundAuditDocuments.Summaries).WhereIn("retentionState", new[] { "eligible", "eligible-resolved" })
            .WhereLessThanOrEqualTo("evidenceDeleteAfterUtc", Timestamp.FromDateTime(now)).Limit(policy.BatchSize).GetSnapshotAsync(ct);
        foreach (var document in due.Documents) await DeleteIfStillEligibleAsync(document.Id, now, ct);
        return pending.Count + due.Count;
    }

    private Task VerifyAsync(string id, DateTime now, GameRoundRetentionOptions policy, CancellationToken ct) => database.RunTransactionAsync(async transaction =>
    {
        var summaryRef = database.Collection(RoundAuditDocuments.Summaries).Document(id);
        var reads = await transaction.GetAllSnapshotsAsync([summaryRef, database.Collection(RoundAuditDocuments.Evidence).Document(id)], ct);
        if (!reads[0].Exists || FirestoreGameRoundAuditStore.Text(reads[0], "retentionState") != "unreviewed") return false;
        var summary = FirestoreGameRoundAuditStore.Summary(reads[0]);
        if (HasRiskHold(reads[0]))
        {
            transaction.Update(summaryRef, new Dictionary<string, object>
            { ["retentionReviewNextAttemptAtUtc"] = Timestamp.FromDateTime(now.AddMinutes(5)) });
            return false;
        }
        var check = GameRoundEvidenceIntegrity.Check(summary, reads[1].Exists ? FirestoreGameRoundAuditStore.Text(reads[1], "payloadJson") : null,
            reads[1].Exists ? FirestoreGameRoundAuditStore.Text(reads[1], "sha256") : null);
        var updates = new Dictionary<string, object> { ["reviewedAtUtc"] = Timestamp.FromDateTime(now), ["retentionNote"] = check.Message };
        if (!check.Valid)
        {
            updates["auditStatus"] = "flagged";
            updates["findings"] = FieldValue.ArrayUnion(new Dictionary<string, object> { ["code"] = check.Code, ["severity"] = "error", ["message"] = check.Message });
        }
        var clean = check.Valid && summary.AuditStatus == "passed";
        updates["retentionState"] = clean ? "eligible" : "held";
        // Eligibility is recorded even with deletion disabled; flipping the policy later
        // still rechecks every candidate transactionally before removing only its payload.
        updates["evidenceDeleteAfterUtc"] = clean ? Timestamp.FromDateTime(now.AddDays(policy.CleanEvidenceDays)) : null!;
        transaction.Update(summaryRef, updates);
        return true;
    }, cancellationToken: ct);

    private Task DeleteIfStillEligibleAsync(string id, DateTime now, CancellationToken ct) => database.RunTransactionAsync(async transaction =>
    {
        var summaryRef = database.Collection(RoundAuditDocuments.Summaries).Document(id);
        var evidenceRef = database.Collection(RoundAuditDocuments.Evidence).Document(id);
        var reads = await transaction.GetAllSnapshotsAsync([summaryRef, evidenceRef], ct);
        if (!options.Value.DeleteCleanEvidence || !reads[0].Exists) return false;
        var summary = FirestoreGameRoundAuditStore.Summary(reads[0]);
        var state = FirestoreGameRoundAuditStore.Text(reads[0], "retentionState");
        if (state is not ("eligible" or "eligible-resolved") || summary.EvidenceDeleteAfterUtc is null || summary.EvidenceDeleteAfterUtc > now)
            return false;
        if (HasRiskHold(reads[0]))
        {
            transaction.Update(summaryRef, new Dictionary<string, object>
            { ["evidenceDeleteAfterUtc"] = Timestamp.FromDateTime(now.AddHours(1)) });
            return false;
        }
        if (state == "eligible" && summary.AuditStatus != "passed" || !reads[1].Exists)
        {
            transaction.Update(summaryRef, new Dictionary<string, object>
            {
                ["auditStatus"] = "flagged", ["retentionState"] = "held", ["evidenceDeleteAfterUtc"] = null!,
                ["retentionNote"] = !reads[1].Exists ? "Expected private evidence is missing." : "A nonpassing audit cannot enter clean retention."
            });
            return false;
        }
        var riskRetention = FirestoreGameRoundAuditStore.Time(reads[0], "riskRetainUntilUtc");
        if (riskRetention > now)
        {
            transaction.Update(summaryRef, new Dictionary<string, object>
            { ["evidenceDeleteAfterUtc"] = Timestamp.FromDateTime(riskRetention.Value) });
            return false;
        }
        var integrity = GameRoundEvidenceIntegrity.Check(summary, FirestoreGameRoundAuditStore.Text(reads[1], "payloadJson"), FirestoreGameRoundAuditStore.Text(reads[1], "sha256"));
        if (!integrity.Valid)
        {
            transaction.Update(summaryRef, new Dictionary<string, object> { ["auditStatus"] = "flagged", ["retentionState"] = "held", ["evidenceDeleteAfterUtc"] = null!, ["retentionNote"] = integrity.Message });
            return false;
        }
        transaction.Delete(evidenceRef);
        transaction.Update(summaryRef, new Dictionary<string, object>
        {
            ["evidencePresent"] = false, ["retentionState"] = "deleted", ["evidenceDeletedAtUtc"] = Timestamp.FromDateTime(now),
            ["rawEvidencePurgePending"] = true, ["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(now),
            ["rawRequestCursor"] = ""
        });
        return true;
    }, cancellationToken: ct);

    internal static bool HasRiskHold(DocumentSnapshot summary) =>
        summary.TryGetValue<object[]>("investigationHoldIds", out var investigations) && investigations.Length > 0 ||
        summary.TryGetValue<bool>("securityAuditPending", out var pendingAudit) && pendingAudit ||
        summary.TryGetValue<bool>("preserveIndefinitely", out var permanent) && permanent ||
        summary.TryGetValue<object[]>("riskHoldIncidentIds", out var open) && open.Length > 0 ||
        summary.TryGetValue<object[]>("riskPreservedIncidentIds", out var upheld) && upheld.Length > 0;
}

internal sealed class GameRoundRetentionWorker(GameRoundRetentionService retention, IOptions<GameRoundRetentionOptions> options,
    ILogger<GameRoundRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await retention.SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Round evidence verification failed; evidence was retained."); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public static class GameRoundAuditConfiguration
{
    public static IServiceCollection AddGameRoundAuditing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GameRoundRetentionOptions>().Bind(configuration.GetSection("GameRoundRetention"))
            .Validate(value => value.CleanEvidenceDays is >= 1 and <= 365 && value.BatchSize is >= 1 and <= 100 && value.PollSeconds is >= 30 and <= 3600)
            .ValidateOnStart();
        services.AddSingleton<IGameRoundAuditStore, FirestoreGameRoundAuditStore>();
        services.AddSingleton<GameRoundRetentionService>();
        services.AddHostedService<GameRoundRetentionWorker>();
        return services;
    }
}
