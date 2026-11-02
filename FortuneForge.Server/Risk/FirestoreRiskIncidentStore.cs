using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Risk;

internal sealed class FirestoreRiskIncidentStore(FirestoreDb database, TimeProvider clock) : IRiskStateReader
{
    public const string Incidents = "playerRiskIncidents";
    public const string States = "playerRiskStates";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static DocumentReference StateReference(FirestoreDb db, string userId) =>
        db.Collection(States).Document(GameAnalyticsOutbox.Hash(userId));
    private DocumentReference State(string userId) => StateReference(database, userId);
    private DocumentReference Incident(string incidentId) => database.Collection(Incidents).Document(incidentId);

    public async Task<PlayerRiskState> GetStateAsync(string userId, CancellationToken cancellationToken)
    {
        RequireText(userId, nameof(userId), 512);
        return ReadState(await State(userId).GetSnapshotAsync(cancellationToken), userId, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<RiskIncident?> GetIncidentAsync(string id, CancellationToken cancellationToken)
    {
        RequireDocumentId(id, nameof(id));
        var snapshot = await Incident(id).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? ReadIncident(snapshot) : null;
    }

    public async Task<IReadOnlyList<RiskIncident>> ListAsync(string? userId, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        Query query = database.Collection(Incidents);
        if (userId is not null) query = query.WhereEqualTo("userId", userId);
        var snapshot = await query.OrderByDescending("createdAtUtc").Limit(limit).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(ReadIncident).ToArray();
    }

    public async Task<IReadOnlyList<RiskIncident>> ListContributingAsync(string userId, int limit, CancellationToken cancellationToken)
    {
        RequireText(userId, nameof(userId), 512);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var snapshot = await database.Collection(Incidents).WhereEqualTo("userId", userId)
            .WhereEqualTo("contributesToRestrictions", true).OrderByDescending("createdAtUtc")
            .Limit(limit).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(ReadIncident).ToArray();
    }

    public Task<RiskIncidentMutation> CreateAsync(RiskIncidentDraft draft, CancellationToken cancellationToken)
    {
        Validate(draft);
        // A rule-version rerun or severity retry is the SAME incident, not another strike.
        var id = GameAnalyticsOutbox.Hash($"{draft.UserId}\n{draft.SourceKind}\n{draft.SourceIncidentId}");
        var now = clock.GetUtcNow().UtcDateTime;
        return database.RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(Incident(id), cancellationToken),
                transaction.GetSnapshotAsync(State(draft.UserId), cancellationToken));
            var state = ReadState(initial[1], draft.UserId, now);
            if (initial[0].Exists)
            {
                var previous = ReadIncident(initial[0]);
                if (!SameAttribution(previous, draft))
                    throw new RiskIncidentConflictException("This source incident already exists with different attribution or severity. Review the existing incident instead of adding another strike.");
                return new RiskIncidentMutation(previous, state, false);
            }

            var evidenceIds = draft.EvidenceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var summaries = await Task.WhenAll(evidenceIds.Select(evidenceId => transaction.GetSnapshotAsync(
                database.Collection(RoundAuditDocuments.Summaries).Document(evidenceId), cancellationToken)));
            var evidence = await Task.WhenAll(evidenceIds.Select(evidenceId => transaction.GetSnapshotAsync(
                database.Collection(RoundAuditDocuments.Evidence).Document(evidenceId), cancellationToken)));
            for (var index = 0; index < summaries.Length; index++)
            {
                var summary = summaries[index];
                if (!summary.Exists || !evidence[index].Exists ||
                    !summary.TryGetValue<bool>("evidencePresent", out var present) || !present ||
                    !summary.TryGetValue<List<Dictionary<string, object>>>("participants", out var participants) ||
                    !participants.Any(participant => participant.GetValueOrDefault("actorId") as string == draft.UserId &&
                        participant.GetValueOrDefault("isAccountBacked") is true))
                    throw new RiskIncidentConflictException("Each attributed incident requires retained round evidence identifying that human participant.");
            }
            if (!summaries.Any(summary => summary.GetValue<string>("gameId") == draft.GameId &&
                summary.GetValue<string>("roundId") == draft.RoundId))
                throw new RiskIncidentConflictException("The incident's game and round must match its retained evidence.");

            var incident = new RiskIncident(id, draft.UserId, draft.GameId, draft.RoundId, draft.Severity,
                draft.SourceKind, draft.SourceIncidentId, draft.RuleId, draft.RuleVersion,
                draft.AttributionBasis, evidenceIds, draft.CreatedBy, now, "open", null, null, null, null);
            var counts = ChangeCount(state.CountsByGame, incident.GameId, incident.Severity, 1);
            var updated = PlayerRiskPolicy.EvaluateCounts(draft.UserId, counts, checked(state.Version + 1), now);
            transaction.Create(Incident(id), IncidentData(incident));
            transaction.Set(State(draft.UserId), StateData(updated));
            foreach (var summary in summaries)
                transaction.Update(summary.Reference, new Dictionary<string, object>
                {
                    ["riskHoldIncidentIds"] = FieldValue.ArrayUnion(id),
                    ["evidenceDeleteAfterUtc"] = null!, ["retentionState"] = "held-risk"
                });
            return new RiskIncidentMutation(incident, updated, true);
        }, cancellationToken: cancellationToken);
    }

    public Task<RiskIncidentMutation> ResolveAsync(string id, string reviewedBy, string outcome, string reason,
        CancellationToken cancellationToken, string? reviewedSeverity = null)
    {
        RequireDocumentId(id, nameof(id));
        RequireText(reviewedBy, nameof(reviewedBy), 512);
        RequireText(reason, nameof(reason), 4_000);
        if (outcome is not ("cleared" or "upheld")) throw new ArgumentException("Choose cleared or upheld.", nameof(outcome));
        if (reviewedSeverity is not (null or RiskLevels.Possible or RiskLevels.High or RiskLevels.Critical))
            throw new ArgumentException("Choose possible, high, or critical.", nameof(reviewedSeverity));
        var now = clock.GetUtcNow().UtcDateTime;
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(Incident(id), cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("The risk incident was not found.");
            var incident = ReadIncident(snapshot);
            var severity = reviewedSeverity ?? incident.Severity;
            var state = ReadState(await transaction.GetSnapshotAsync(State(incident.UserId), cancellationToken), incident.UserId, now);
            if (incident.Status == "resolved" && incident.ResolutionOutcome == outcome && incident.Severity == severity)
                return new RiskIncidentMutation(incident, state, false);
            var summaries = await Task.WhenAll(incident.EvidenceIds.Select(evidenceId => transaction.GetSnapshotAsync(
                database.Collection(RoundAuditDocuments.Summaries).Document(evidenceId), cancellationToken)));
            if (outcome == "upheld")
            {
                var evidence = await Task.WhenAll(incident.EvidenceIds.Select(evidenceId => transaction.GetSnapshotAsync(
                    database.Collection(RoundAuditDocuments.Evidence).Document(evidenceId), cancellationToken)));
                if (summaries.Any(summary => !summary.Exists || !summary.TryGetValue<bool>("evidencePresent", out var present) || !present)
                    || evidence.Any(value => !value.Exists))
                    throw new RiskIncidentConflictException("An incident cannot be upheld without its retained detailed evidence.");
            }
            // A corrected decision must remain reviewable. It changes the same
            // incident, never creates a fresh strike or erases the prior review.
            var reviews = (incident.Reviews ?? []).Append(new RiskIncidentReview(
                now, reviewedBy, incident.ResolutionOutcome, outcome, reason, incident.Severity, severity)).ToArray();
            var resolved = incident with
            {
                Status = "resolved", ResolvedAtUtc = now, ResolvedBy = reviewedBy,
                ResolutionOutcome = outcome, ResolutionReason = reason, Reviews = reviews, Severity = severity
            };
            var countedBefore = PlayerRiskPolicy.CountsForRestrictions(incident);
            var countedAfter = PlayerRiskPolicy.CountsForRestrictions(resolved);
            var severityChanged = severity != incident.Severity;
            var counts = state.CountsByGame;
            if (countedBefore && (!countedAfter || severityChanged)) counts = ChangeCount(counts, incident.GameId, incident.Severity, -1);
            if (countedAfter && (!countedBefore || severityChanged)) counts = ChangeCount(counts, incident.GameId, severity, 1);
            var updated = PlayerRiskPolicy.EvaluateCounts(incident.UserId, counts, checked(state.Version + 1), now);
            transaction.Set(Incident(id), IncidentData(resolved));
            transaction.Set(State(incident.UserId), StateData(updated));
            foreach (var summary in summaries.Where(summary => summary.Exists))
            {
                var holds = ReadIds(summary, "riskHoldIncidentIds").Where(value => value != id).ToArray();
                var preserved = ReadIds(summary, "riskPreservedIncidentIds").Where(value => value != id).ToList();
                if (outcome == "upheld") preserved.Add(id);
                DateTime? retainUntil = summary.TryGetValue<Timestamp?>("riskRetainUntilUtc", out var existing)
                    ? existing?.ToDateTime() : null;
                if (outcome == "cleared" && (retainUntil is null || retainUntil < now.AddDays(30)))
                    retainUntil = now.AddDays(30);
                var permanent = preserved.Count > 0 || summary.TryGetValue<bool>("preserveIndefinitely", out var preserve) && preserve;
                var retentionState = permanent ? "preserved" : holds.Length > 0 ? "held-risk" : "eligible-resolved";
                transaction.Update(summary.Reference, new Dictionary<string, object>
                {
                    ["riskHoldIncidentIds"] = holds,
                    ["riskPreservedIncidentIds"] = preserved.ToArray(),
                    ["riskResolvedAtUtc"] = Timestamp.FromDateTime(now),
                    ["riskRetainUntilUtc"] = retainUntil is { } deadline ? Timestamp.FromDateTime(deadline) : null!,
                    ["retentionState"] = retentionState,
                    ["evidenceDeleteAfterUtc"] = retentionState == "eligible-resolved" && retainUntil is { } expires
                        ? Timestamp.FromDateTime(expires) : null!
                });
            }
            return new RiskIncidentMutation(resolved, updated, true);
        }, cancellationToken: cancellationToken);
    }

    internal static RiskIncident ReadIncident(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<RiskIncident>(snapshot.GetValue<string>("incidentJson"), Json)
        ?? throw new InvalidOperationException("Stored risk incident is invalid.");

    private static IReadOnlyList<string> ReadIds(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<List<string>>(field, out var values) ? values ?? [] : [];

    internal static PlayerRiskState ReadState(DocumentSnapshot snapshot, string userId, DateTime now) => snapshot.Exists
        ? JsonSerializer.Deserialize<PlayerRiskState>(snapshot.GetValue<string>("stateJson"), Json)
            ?? throw new InvalidOperationException("Stored risk state is invalid.")
        : PlayerRiskPolicy.EvaluateCounts(userId, new Dictionary<string, RiskLevelCounts>(), 0, now);

    private static Dictionary<string, object> IncidentData(RiskIncident value) => new()
    {
        ["id"] = value.Id, ["userId"] = value.UserId, ["gameId"] = value.GameId,
        ["roundId"] = value.RoundId, ["severity"] = value.Severity, ["status"] = value.Status,
        ["contributesToRestrictions"] = PlayerRiskPolicy.CountsForRestrictions(value),
        ["evidenceIds"] = value.EvidenceIds.ToArray(), ["createdAtUtc"] = Timestamp.FromDateTime(value.CreatedAtUtc),
        ["resolvedAtUtc"] = value.ResolvedAtUtc is { } resolved ? Timestamp.FromDateTime(resolved) : null!,
        ["incidentJson"] = JsonSerializer.Serialize(value, Json), ["schemaVersion"] = 1L,
        ["investigationPending"] = true,
        ["investigationNextAttemptAtUtc"] = Timestamp.FromDateTime(value.CreatedAtUtc)
    };

    private static Dictionary<string, object> StateData(PlayerRiskState value) => new()
    {
        ["userId"] = value.UserId, ["accountRestricted"] = value.AccountRestricted,
        ["withdrawalsBlocked"] = value.WithdrawalsBlocked, ["blockedGameIds"] = value.BlockedGameIds.ToArray(),
        ["version"] = value.Version, ["updatedAtUtc"] = Timestamp.FromDateTime(value.UpdatedAtUtc),
        ["stateJson"] = JsonSerializer.Serialize(value, Json), ["schemaVersion"] = 1L
    };

    private static IReadOnlyDictionary<string, RiskLevelCounts> ChangeCount(IReadOnlyDictionary<string, RiskLevelCounts> prior,
        string game, string severity, int delta)
    {
        var result = new Dictionary<string, RiskLevelCounts>(prior, StringComparer.Ordinal);
        var count = result.GetValueOrDefault(game) ?? new RiskLevelCounts(0, 0, 0);
        var updated = severity switch
        {
            RiskLevels.Possible => count with { Possible = checked(count.Possible + delta) },
            RiskLevels.High => count with { High = checked(count.High + delta) },
            RiskLevels.Critical => count with { Critical = checked(count.Critical + delta) },
            _ => throw new ArgumentException("Invalid risk severity.", nameof(severity))
        };
        if (updated.Possible < 0 || updated.High < 0 || updated.Critical < 0)
            throw new RiskIncidentConflictException("Risk counters require reconciliation before this review can be saved.");
        if (updated is { Possible: 0, High: 0, Critical: 0 }) result.Remove(game);
        else result[game] = updated;
        return result;
    }

    private static bool SameAttribution(RiskIncident value, RiskIncidentDraft draft) =>
        value.UserId == draft.UserId && value.GameId == draft.GameId && value.RoundId == draft.RoundId &&
        value.Severity == draft.Severity && value.SourceKind == draft.SourceKind && value.SourceIncidentId == draft.SourceIncidentId &&
        value.RuleId == draft.RuleId && value.RuleVersion == draft.RuleVersion && value.AttributionBasis == draft.AttributionBasis &&
        value.EvidenceIds.Order(StringComparer.Ordinal).SequenceEqual(draft.EvidenceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static void Validate(RiskIncidentDraft draft)
    {
        RequireText(draft.UserId, nameof(draft.UserId), 512);
        RequireText(draft.RoundId, nameof(draft.RoundId), 512);
        RequireText(draft.SourceIncidentId, nameof(draft.SourceIncidentId), 512);
        RequireText(draft.RuleId, nameof(draft.RuleId), 200);
        RequireText(draft.RuleVersion, nameof(draft.RuleVersion), 100);
        RequireText(draft.AttributionBasis, nameof(draft.AttributionBasis), 4_000);
        RequireText(draft.CreatedBy, nameof(draft.CreatedBy), 512);
        if (!GameAnalyticsOutbox.GameIds.Contains(draft.GameId)) throw new ArgumentException("A canonical game ID is required.");
        if (draft.Severity is not (RiskLevels.Possible or RiskLevels.High or RiskLevels.Critical)) throw new ArgumentException("Choose possible, high, or critical.");
        if (draft.SourceKind is not ("admin-review" or "attributed-detector")) throw new ArgumentException("An explicitly attributed review or detector is required; generic integrity findings are not player incidents.");
        if (draft.EvidenceIds is null || draft.EvidenceIds.Count is < 1 or > 20) throw new ArgumentException("Retained round evidence is required (1–20 references).");
        foreach (var id in draft.EvidenceIds) RequireDocumentId(id, nameof(draft.EvidenceIds));
    }

    private static void RequireDocumentId(string value, string name)
    {
        RequireText(value, name, 512);
        if (value.Contains('/')) throw new ArgumentException("A document ID cannot contain a slash.", name);
    }

    private static void RequireText(string value, string name, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new ArgumentException("A bounded, nonempty value is required.", name);
    }
}
