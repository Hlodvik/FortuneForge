using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Arcade;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Games.Snake;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record GameSecurityIntegrityAssessment(
    IReadOnlyList<RoundAuditFinding> Findings, int JournalEventCount, string JournalCoverage);

/// <summary>Second-pass checks over persisted evidence, independent of a game's settlement path.</summary>
internal static class GameSecurityIntegrityAuditor
{
    internal const string Version = "db-evidence-integrity-v4";
    private const int PageSize = 25;
    private const int MaximumEventsPerInstance = 2_000;

    internal static async Task<GameSecurityIntegrityAssessment> InspectAsync(
        FirestoreDb database, RoundAuditSummary summary, CancellationToken ct, DateTime? observedAtUtc = null)
    {
        var findings = new List<RoundAuditFinding>();
        var original = await database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync(ct);
        var roundPayload = original.Exists ? FirestoreGameRoundAuditStore.Text(original, "payloadJson") : null;
        var check = GameRoundEvidenceIntegrity.Check(summary,
            roundPayload,
            original.Exists ? FirestoreGameRoundAuditStore.Text(original, "sha256") : null);
        if (!check.Valid) findings.Add(new(check.Code, "error", check.Message));
        if (roundPayload is not null)
        {
            findings.AddRange(PracticeRoundReplayAudit.Inspect(summary, roundPayload));
            findings.AddRange(SlotEvidenceReplayAudit.Inspect(summary, roundPayload));
            findings.AddRange(SnakeReplayAudit.Inspect(summary, roundPayload));
            findings.AddRange(TableOutcomeReplayAudit.Inspect(summary, roundPayload));
            findings.AddRange(PaidCardEvidenceReplayAudit.Inspect(summary, roundPayload));
            findings.AddRange(NumericEvidenceReplayAudit.Inspect(summary, roundPayload));
            if (check.Valid)
                findings.AddRange(await PaidCardFinancialAudit.InspectAsync(database, summary, roundPayload, ct));
            findings.AddRange(await ArcadeReplayAudit.InspectAsync(database, summary, roundPayload, ct));
        }
        if (summary.GameId == "texas-holdem")
            findings.AddRange(await HoldemFinancialAudit.InspectAsync(database, summary, ct, roundPayload));
        if (SolitaireFinancialAudit.Supports(summary))
            findings.AddRange(await SolitaireFinancialAudit.InspectAsync(database, summary, roundPayload, ct));
        if (summary.GameId is "keno" or "sic-bo")
            findings.AddRange(await NumericEvidenceReplayAudit.InspectFinancialAsync(database, summary, ct));

        var summaryDocument = await database.Collection(RoundAuditDocuments.Summaries).Document(summary.Id).GetSnapshotAsync(ct);
        var manifestExpected = summaryDocument.Exists &&
            summaryDocument.TryGetValue<bool>("journalManifestExpected", out var expected) && expected;
        var manifest = await database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(summary.GameId, summary.RoundId)).GetSnapshotAsync(ct);
        var count = 0;
        var cursor = "";
        var observedIds = new HashSet<string>(StringComparer.Ordinal);
        var heartsEvents = new List<string>();
        var solitaireEvents = new List<string>();
        var practiceActionEvents = new List<string>();
        var liarsDiceEvents = new List<string>();
        var casinoPracticeEvents = new List<string>();
        var eventCollection = database.Collection(GameSecurityEventDocuments.CollectionName);
        while (true)
        {
            Query query = eventCollection.WhereEqualTo("gameId", summary.GameId)
                .WhereEqualTo("instanceId", summary.RoundId).OrderBy(FieldPath.DocumentId);
            if (cursor.Length > 0) query = query.StartAfter(cursor);
            var page = await query.Limit(PageSize).GetSnapshotAsync(ct);
            var linkedRequests = new Dictionary<string, List<DocumentSnapshot>>(StringComparer.Ordinal);
            foreach (var item in page.Documents)
            {
                count++;
                observedIds.Add(item.Id);
                var payload = FirestoreGameRoundAuditStore.Text(item, "payloadJson");
                if (payload is null)
                {
                    findings.Add(new("engine-journal-changed", "error", $"Engine observation {item.Id} has no private payload."));
                    continue;
                }
                if (summary.GameId == "hearts") heartsEvents.Add(payload);
                if (summary.GameId == "solitaire") solitaireEvents.Add(payload);
                if (summary.GameId is "2048" or "drop-merge") practiceActionEvents.Add(payload);
                if (summary.GameId == "liars-dice") liarsDiceEvents.Add(payload);
                if (summary.GameId is "craps" or "roulette") casinoPracticeEvents.Add(payload);
                var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                if (digest != item.Id || digest != FirestoreGameRoundAuditStore.Text(item, "sha256"))
                    findings.Add(new("engine-journal-changed", "error", $"Engine observation {item.Id} differs from its stored fingerprint."));
                if (!EnvelopeMatches(item, payload, summary))
                    findings.Add(new("engine-journal-envelope-mismatch", "error", $"Engine observation {item.Id} has metadata that differs from its private payload."));
                if (FirestoreGameRoundAuditStore.Time(item, "occurredAtUtc") is not { Kind: DateTimeKind.Utc })
                    findings.Add(new("engine-journal-time-missing", "error", $"Engine observation {item.Id} has no server UTC time."));
                var requestId = FirestoreGameRoundAuditStore.Text(item, "requestId");
                if (requestId.Length > 0)
                {
                    if (!linkedRequests.TryGetValue(requestId, out var linked))
                        linkedRequests[requestId] = linked = [];
                    linked.Add(item);
                }
            }
            // Authenticated commands are recorded before game state can change. A missing intent
            // is a system-integrity gap, never proof that a participant cheated.
            foreach (var (requestId, events) in linkedRequests)
            {
                var intent = await database.Collection(FirestoreGameSecurityRequestStore.Intents)
                    .Document(requestId).GetSnapshotAsync(ct);
                if (!intent.Exists)
                    findings.Add(new("engine-request-intent-missing", "error", $"An engine observation references absent request intent {requestId}."));
                else if (events.Any(item =>
                    FirestoreGameRoundAuditStore.Text(item, "userId") != FirestoreGameRoundAuditStore.Text(intent, "userId") ||
                    FirestoreGameRoundAuditStore.Text(item, "gameId") != FirestoreGameRoundAuditStore.Text(intent, "gameId")))
                    findings.Add(new("engine-request-identity-mismatch", "error", $"Engine observation and request intent {requestId} disagree on account or game."));
                if (intent.Exists)
                {
                    if (!ReceiptDigestMatches(intent, requestId, manifestExpected, isIntent: true))
                        findings.Add(new("engine-request-intent-changed", "error", $"The request intent {requestId} has an absent or inconsistent private fingerprint."));
                    var receivedAtUtc = FirestoreGameRoundAuditStore.Time(intent, "receivedAtUtc");
                    if (receivedAtUtc is null || events.Any(item =>
                        FirestoreGameRoundAuditStore.Time(item, "occurredAtUtc") is { } eventTime && eventTime < receivedAtUtc.Value))
                        findings.Add(new("engine-request-time-mismatch", "error", $"Engine observation {requestId} predates its request intent or the request time is absent."));
                }
                // The HTTP result is recorded after the game transaction. A missing result
                // during the grace period must keep this round queued for a later pass.
                var result = await database.Collection(FirestoreGameSecurityRequestStore.Results)
                    .Document(requestId).GetSnapshotAsync(ct);
                if (!result.Exists)
                    findings.Add(summary.CompletedAtUtc <= (observedAtUtc ?? DateTime.UtcNow).AddMinutes(-2)
                        ? new("engine-request-result-missing", "error", $"Engine observation {requestId} has no durable HTTP result after the reconciliation grace period.")
                        : new("engine-request-result-pending", "warning", $"Engine observation {requestId} is awaiting its separate HTTP result write."));
                else if (!ReceiptDigestMatches(result, requestId, manifestExpected, isIntent: false))
                    findings.Add(new("engine-request-result-changed", "error", $"The HTTP result for request {requestId} has an absent or inconsistent private fingerprint."));
            }
            if (page.Count == 0) break;
            cursor = page.Documents[^1].Id;
            if (count > MaximumEventsPerInstance)
            {
                findings.Add(new("engine-journal-scan-limit", "error", "The engine observation stream exceeded the bounded automatic audit; manual review must inspect the full instance."));
                return new(findings, count, "bounded-scan-exceeded");
            }
            if (page.Count < PageSize) break;
        }
        if (count == 0)
            findings.Add(new("engine-journal-missing", "error", "No engine observations were recorded for this instance."));
        if (!manifest.Exists)
        {
            if (manifestExpected)
                findings.Add(new("engine-journal-manifest-missing", "error", "The committed engine observation manifest is missing for this completed instance."));
        }
        else if (FirestoreGameRoundAuditStore.Text(manifest, "gameId") != summary.GameId ||
            FirestoreGameRoundAuditStore.Text(manifest, "instanceId") != summary.RoundId ||
            !manifest.TryGetValue<List<string>>("eventIds", out var eventIds) ||
            eventIds.Count != eventIds.Distinct(StringComparer.Ordinal).Count())
            findings.Add(new("engine-journal-manifest-invalid", "error", "The engine observation manifest has invalid identity or duplicate/missing event identifiers."));
        else
        {
            var expectedIds = eventIds.ToHashSet(StringComparer.Ordinal);
            if (expectedIds.Except(observedIds).Any())
                findings.Add(new("engine-journal-event-missing", "error", "A committed engine observation is absent from this instance's journal."));
            if (observedIds.Except(expectedIds).Any())
                findings.Add(new("engine-journal-unmanifested-event", "error", "An engine observation is absent from this instance's committed manifest."));
        }
        if (roundPayload is not null)
        {
            findings.AddRange(HeartsEvidenceReplayAudit.Inspect(summary, roundPayload, heartsEvents));
            if (check.Valid)
                findings.AddRange(await HeartsEvidenceReplayAudit.InspectFinancialAsync(database, summary, roundPayload, ct));
        }
        findings.AddRange(SolitaireEvidenceReplayAudit.Inspect(summary, roundPayload, solitaireEvents));
        findings.AddRange(PracticeActionReplayAudit.Inspect(summary, roundPayload, practiceActionEvents));
        findings.AddRange(LiarsDiceEvidenceReplayAudit.Inspect(summary, roundPayload, liarsDiceEvents));
        findings.AddRange(CasinoPracticeActionReplayAudit.Inspect(summary, roundPayload, casinoPracticeEvents));
        return new(findings, count, !manifest.Exists && !manifestExpected ? "legacy-unmanifested" :
            count == 0 ? "no-engine-journal" : "fingerprints-requests-and-manifest-checked");
    }

    private static bool ReceiptDigestMatches(DocumentSnapshot receipt, string requestId, bool digestRequired, bool isIntent)
    {
        if (FirestoreGameRoundAuditStore.Text(receipt, "requestId") != requestId) return false;
        var json = FirestoreGameRoundAuditStore.Text(receipt, "observationJson");
        if (string.IsNullOrWhiteSpace(json)) return false;
        var digest = FirestoreGameRoundAuditStore.Text(receipt, "sha256");
        if (digest.Length == 0 ? digestRequired : digest != GameSecurityEventDocuments.Hash(json)) return false;
        try
        {
            if (isIntent)
            {
                var saved = JsonSerializer.Deserialize<GameRequestObservation>(json, RoundAuditCapture.Json);
                return saved is not null && saved.RequestId == requestId &&
                    FirestoreGameRoundAuditStore.Text(receipt, "userId") == saved.UserId &&
                    FirestoreGameRoundAuditStore.Text(receipt, "gameId") == saved.GameId &&
                    (FirestoreGameRoundAuditStore.Text(receipt, "instanceId") ?? "") == (saved.InstanceId ?? "") &&
                    (FirestoreGameRoundAuditStore.Text(receipt, "sessionId") ?? "") == (saved.SessionId ?? "") &&
                    (FirestoreGameRoundAuditStore.Text(receipt, "ipAddress") ?? "") == (saved.IpAddress ?? "") &&
                    FirestoreGameRoundAuditStore.Text(receipt, "ipSource") == saved.IpSource &&
                    (FirestoreGameRoundAuditStore.Text(receipt, "commandId") ?? "") == (saved.CommandId ?? "") &&
                    FirestoreGameRoundAuditStore.Text(receipt, "requestBodyState") == saved.RequestBodyState &&
                    receipt.TryGetValue<long?>("stateVersion", out var stateVersion) &&
                    stateVersion == saved.StateVersion &&
                    SameReceiptTime(receipt, "receivedAtUtc", saved.ReceivedAtUtc);
            }
            var result = JsonSerializer.Deserialize<GameRequestResult>(json, RoundAuditCapture.Json);
            return result is not null && result.RequestId == requestId &&
                receipt.TryGetValue<long?>("httpStatusCode", out var statusCode) &&
                statusCode == result.HttpStatusCode &&
                FirestoreGameRoundAuditStore.Text(receipt, "outcome") == result.Outcome &&
                receipt.TryGetValue<long>("durationMilliseconds", out var duration) &&
                duration == result.DurationMilliseconds &&
                SameReceiptTime(receipt, "completedAtUtc", result.CompletedAtUtc);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool SameReceiptTime(DocumentSnapshot receipt, string field, DateTime saved) =>
        FirestoreGameRoundAuditStore.Time(receipt, field) is { } indexed &&
        saved.Kind == DateTimeKind.Utc && Math.Abs((indexed - saved).Ticks) < 10;

    private static bool EnvelopeMatches(DocumentSnapshot item, string payload, RoundAuditSummary summary)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var instanceId = root.GetProperty("instanceId").GetString();
            var request = root.GetProperty("request");
            var requestId = request.ValueKind == JsonValueKind.Object ? request.GetProperty("requestId").GetString() ?? "" : "";
            var userId = request.ValueKind == JsonValueKind.Object ? request.GetProperty("userId").GetString() ?? "" : "";
            var participants = root.GetProperty("participants").EnumerateArray().Select(value => value.GetString()).ToArray();
            var indexedParticipants = item.GetValue<string[]>("participantIds");
            var occurred = root.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime();
            var indexedTime = FirestoreGameRoundAuditStore.Time(item, "occurredAtUtc");
            return root.GetProperty("gameId").GetString() == summary.GameId &&
                instanceId == summary.RoundId &&
                FirestoreGameRoundAuditStore.Text(item, "tableId") ==
                    (root.GetProperty("tableId").ValueKind == JsonValueKind.String ? root.GetProperty("tableId").GetString() : instanceId) &&
                root.GetProperty("eventKind").GetString() == FirestoreGameRoundAuditStore.Text(item, "eventKind") &&
                root.GetProperty("version").GetInt64() == item.GetValue<long>("version") &&
                root.GetProperty("contractVersion").GetString() == FirestoreGameRoundAuditStore.Text(item, "contractVersion") &&
                requestId == FirestoreGameRoundAuditStore.Text(item, "requestId") &&
                userId == FirestoreGameRoundAuditStore.Text(item, "userId") &&
                participants.SequenceEqual(indexedParticipants) &&
                indexedTime is { } recorded && Math.Abs((occurred - recorded).Ticks) < 10;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or InvalidCastException)
        {
            return false;
        }
    }
}
