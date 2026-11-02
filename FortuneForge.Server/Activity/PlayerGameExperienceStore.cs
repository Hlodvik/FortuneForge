using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Caching.Memory;

namespace FortuneForge.Server.Activity;

internal sealed record GameExperienceLegacySource(string Collection, string OwnerField, string Kind);

internal sealed class FirestorePlayerGameExperienceStore(
    FirestoreDb database, TimeProvider clock, ILogger<FirestorePlayerGameExperienceStore> logger,
    IConfiguration? configuration = null)
    : IPlayerGameExperienceStore, IDisposable
{
    internal const int LegacyPageSize = 32;
    internal const int LegacySourcesPerRead = 3;
    internal static readonly IReadOnlyList<GameExperienceLegacySource> LegacySources =
    [
        new("gameAnalyticsSettlementOutbox", "userId", "outbox"),
        new("cardGameResults", "userId", "card"),
        new("slotSpinResults", "userId", "slot"),
        new("arcadeCompetitionAttempts", "playerId", "attempt"),
        new("asteroidsFreeRuns", "playerId", "asteroids"),
        new("flappyFreeRuns", "playerId", "flappy"),
        new("asteroidsRuns", "playerId", "asteroids"),
        new("flappyRuns", "playerId", "flappy"),
        new("horseFlightRuns", "userId", "horse-flight"),
        new("snakeGames", "userId", "snake"),
        new("solitaireFreeRuns", "userId", "solitaire"),
        new("heartsPaidHistory", "userId", "hearts"),
        new("baccaratRounds", "userId", "baccarat-round"),
        new("casinoWarRounds", "userId", "casino-war-round"),
        new("videoPokerRounds", "userId", "video-poker-round"),
        new("kenoRounds", "userId", "keno-round"),
        new("sicBoRounds", "userId", "sic-bo-round"),
    ];

    private readonly MemoryCache accepted = new(new MemoryCacheOptions { SizeLimit = 20_000 });
    private DocumentReference Account(string userId) => database.Collection("catalogPlayerActivity")
        .Document(AcceptedGameActivityReader.Hash(userId));

    public async Task RecordAcceptedAsync(string userId, AcceptedGameActivity activity, CancellationToken ct)
    {
        ValidateUser(userId);
        if (!PlayerGameExperienceProjection.GameIds.Contains(activity.GameId)) return;
        var key = AcceptedGameActivityReader.Hash(userId + "\n" + activity.GameId);
        if (accepted.TryGetValue(key, out _)) return;
        await PersistAsync(userId, [new(activity.GameId, "accepted-participation")], ct);
        // A failed write never suppresses the next genuine accepted action.
        accepted.Set(key, true, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(1) });
    }

    public async Task<PlayerGameExperienceResponse> ReadAsync(string userId, CancellationToken ct)
    {
        ValidateUser(userId);
        var account = Account(userId);
        var activityTask = ReadSafe(() => account.Collection("games").Limit(128).GetSnapshotAsync(ct), "accepted-activity", ct);
        var analyticsTask = ReadSafe(() => database.Collection(FirestoreGameAnalyticsStore.Buckets)
            .Document(GameAnalyticsOutbox.Hash(userId) + ":all").GetSnapshotAsync(ct), "analytics-completion", ct);
        var progressTask = ReadSafe(() => account.GetSnapshotAsync(ct), "legacy-progress", ct);
        var authoritativeTask = ReadSafe(() => new AuthoritativeGameExperienceCoverage(database, clock, logger, configuration)
            .ReadAsync(userId, ct), "authoritative-slot-coverage", ct);
        var continuousTask = ReadSafe(async () => new ContinuousRead(await new ContinuousGameExperienceCoverage(database, clock, configuration)
            .ReadAsync(userId, ct)), "continuous-game-coverage", ct);
        await Task.WhenAll(activityTask, analyticsTask, progressTask, authoritativeTask, continuousTask);
        var evidence = new List<PlayerGameExperienceEvidence>();
        var existing = new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal);
        if (activityTask.Result.Value is { } activity)
            foreach (var item in activity.Documents)
            {
                var fields = item.ToDictionary();
                existing[item.Id] = fields;
                evidence.AddRange(ReadActivity(item.Id, fields));
            }
        if (analyticsTask.Result.Value is { Exists: true } analytics)
            evidence.AddRange(ReadAnalytics(analytics.ToDictionary()));
        if (authoritativeTask.Result.Value is { } authoritative)
            evidence.AddRange(authoritative.Evidence);

        var progress = progressTask.Result.Value is { Exists: true } saved
            ? Map(saved.ToDictionary(), "experienceBackfill") : new Dictionary<string, object>();
        var index = (int)Math.Clamp(Number(progress, "sourceIndex") ?? 0, 0, LegacySources.Count - 1);
        var sources = Enumerable.Range(0, LegacySourcesPerRead)
            .Select(offset => LegacySources[(index + offset) % LegacySources.Count]).ToArray();
        var pages = await Task.WhenAll(sources.Select(source => ReadLegacyPage(userId, source, progress, ct)));
        var derived = pages.SelectMany(page => page.Evidence).ToArray();
        evidence.AddRange(derived);
        // Same durable per-game documents as accepted activity. Backfill never
        // increments popularity or invents Home recency from an aggregate.
        var missing = MissingEvidence(evidence, existing);
        var complete = new List<GameExperienceCoverageProof>(authoritativeTask.Result.Value?.Complete ?? []);
        if (activityTask.Result.Value is { Count: < 128 } activityRead && continuousTask.Result.Value?.Eligibility is { } eligible)
        {
            var asOf = activityRead.ReadTime.ToDateTime();
            var guard = authoritativeTask.Result.Value;
            if (guard?.AsOfUtc is { } guardedAt && guardedAt < asOf) asOf = guardedAt;
            complete.AddRange(eligible.Proofs(asOf, clock.GetUtcNow().UtcDateTime).Where(proof =>
                !AcceptedGameActivityReader.SlotIds.Values.Contains(proof.GameId) || guard?.AbsentSlots.Contains(proof.GameId) == true));
        }
        IReadOnlyList<GameExperienceCoverageProof> persisted = [];
        if (progressTask.Result.Available)
        {
            var commit = await ReadSafe(async () =>
            {
                // Positive witnesses and page progress commit together. A failed
                // merge must never skip evidence on the next read or restart.
                return new BackfillCommit(await SaveProgress(account, index, pages, missing, complete, ct));
            }, "experience-backfill", ct);
            persisted = commit.Value?.Complete ?? [];
        }
        else if (missing.Count > 0)
            await ReadSafe(async () => { await PersistAsync(userId, missing, ct); return new object(); }, "experience-backfill", ct);
        return PlayerGameExperienceProjection.Create(userId, clock.GetUtcNow().UtcDateTime, evidence,
            activityTask.Result.Available || analyticsTask.Result.Available || pages.Any(page => page.Available),
            activityTask.Result.Value is { Count: < 128 } && analyticsTask.Result.Available ? persisted : []);
    }

    private async Task PersistAsync(string userId, IEnumerable<PlayerGameExperienceEvidence> evidence, CancellationToken ct)
    {
        var batch = database.StartBatch();
        var count = 0;
        foreach (var group in evidence.Where(item => PlayerGameExperienceProjection.GameIds.Contains(item.GameId))
                     .GroupBy(item => item.GameId, StringComparer.Ordinal))
        {
            batch.Set(Account(userId).Collection("games").Document(group.Key), EvidenceFields(group), SetOptions.MergeAll);
            count++;
        }
        if (count > 0) await batch.CommitAsync(ct);
    }

    private Dictionary<string, object> EvidenceFields(IEnumerable<PlayerGameExperienceEvidence> evidence) => new()
    {
        ["hasPlayed"] = true,
        ["experienceSchemaVersion"] = 1L,
        ["experienceProvenance"] = FieldValue.ArrayUnion(evidence.Select(item => (object)item.Provenance).Distinct().ToArray()),
        ["experienceObservedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
    };

    internal static IReadOnlyList<PlayerGameExperienceEvidence> MissingEvidence(
        IEnumerable<PlayerGameExperienceEvidence> evidence,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>> existing)
    {
        var missing = new List<PlayerGameExperienceEvidence>();
        foreach (var group in evidence.Where(item => PlayerGameExperienceProjection.GameIds.Contains(item.GameId))
                     .GroupBy(item => item.GameId, StringComparer.Ordinal))
        {
            if (!existing.TryGetValue(group.Key, out var fields) || !Boolean(fields, "hasPlayed") ||
                Number(fields, "experienceSchemaVersion") != 1)
            {
                missing.AddRange(group.Distinct());
                continue;
            }
            var tags = fields.TryGetValue("experienceProvenance", out var raw) && raw is IEnumerable<object> values
                ? values.OfType<string>().ToHashSet(StringComparer.Ordinal) : [];
            missing.AddRange(group.Where(item => !tags.Contains(item.Provenance)).Distinct());
        }
        return missing;
    }

    private sealed record LegacyPage(GameExperienceLegacySource Source, string PriorCursor, string NextCursor,
        bool Available, IReadOnlyList<PlayerGameExperienceEvidence> Evidence);

    private async Task<LegacyPage> ReadLegacyPage(string userId, GameExperienceLegacySource source,
        IReadOnlyDictionary<string, object> progress, CancellationToken ct)
    {
        var cursor = Text(Map(progress, source.Collection), "cursor") ?? "";
        var result = await ReadSafe(() =>
        {
            Query query = database.Collection(source.Collection).WhereEqualTo(source.OwnerField, userId)
                .OrderBy(FieldPath.DocumentId).Limit(LegacyPageSize + 1);
            if (cursor.Length > 0) query = query.StartAfter(cursor);
            return query.GetSnapshotAsync(ct);
        }, source.Collection, ct);
        if (result.Value is not { } snapshot) return new(source, cursor, cursor, false, []);
        var page = snapshot.Documents.Take(LegacyPageSize).ToArray();
        return new(source, cursor, snapshot.Count > LegacyPageSize ? page[^1].Id : "", true,
            page.Select(item => ReadLegacy(source, userId, item.ToDictionary())).OfType<PlayerGameExperienceEvidence>().ToArray());
    }

    private sealed record ContinuousRead(ContinuousGameExperienceEligibility? Eligibility);
    private sealed record BackfillCommit(IReadOnlyList<GameExperienceCoverageProof> Complete);

    private Task<IReadOnlyList<GameExperienceCoverageProof>> SaveProgress(DocumentReference account, int expectedIndex,
        IReadOnlyList<LegacyPage> pages, IReadOnlyList<PlayerGameExperienceEvidence> missing,
        IReadOnlyList<GameExperienceCoverageProof> complete, CancellationToken ct) =>
        database.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(account, ct);
            var current = snapshot.Exists ? Map(snapshot.ToDictionary(), "experienceBackfill") : new Dictionary<string, object>();
            var changes = new Dictionary<string, object>();
            if ((Number(current, "sourceIndex") ?? 0) == expectedIndex)
                changes["sourceIndex"] = (long)((expectedIndex + LegacySourcesPerRead) % LegacySources.Count);
            foreach (var page in pages.Where(page => page.Available))
                if ((Text(Map(current, page.Source.Collection), "cursor") ?? "") == page.PriorCursor)
                    changes[page.Source.Collection] = new Dictionary<string, object>
                    {
                        ["cursor"] = page.NextCursor,
                        ["checkedAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
                        // Even an exhausted source does not prove unrecorded
                        // legacy free play or an auxiliary write never occurred.
                        ["coverage"] = "partial",
                    };
            foreach (var group in missing.GroupBy(item => item.GameId, StringComparer.Ordinal))
                tx.Set(account.Collection("games").Document(group.Key), EvidenceFields(group), SetOptions.MergeAll);
            var metadata = new Dictionary<string, object>();
            var persisted = new List<GameExperienceCoverageProof>();
            if (changes.Count > 0) metadata["experienceBackfill"] = changes;
            var receipts = new Dictionary<string, object>();
            foreach (var group in complete.GroupBy(proof => proof.ReceiptKey, StringComparer.Ordinal))
            {
                var proof = group.First();
                var priorCoverage = snapshot.Exists ? Map(Map(snapshot.ToDictionary(), "experienceCoverage"), proof.ReceiptKey) : new();
                if (!priorCoverage.TryGetValue("asOfUtc", out var priorTime) || priorTime is not Timestamp at || at.ToDateTime() <= proof.AsOfUtc)
                {
                    persisted.AddRange(group);
                    var receipt = new Dictionary<string, object>
                    {
                        ["sourceVersion"] = proof.SourceVersion ?? AuthoritativeGameExperienceCoverage.SourceVersion,
                        ["sourceProofSha256"] = proof.SourceProofSha256 ?? AuthoritativeGameExperienceCoverage.SourceProofSha256,
                        ["activationId"] = proof.ActivationId, ["asOfUtc"] = Timestamp.FromDateTime(proof.AsOfUtc),
                        ["validUntilUtc"] = Timestamp.FromDateTime(proof.ValidUntilUtc),
                        ["apiRevision"] = proof.ApiRevision, ["sourceRevision"] = proof.SourceRevision,
                        ["gameIds"] = group.Select(item => item.GameId).Order(StringComparer.Ordinal).ToArray(),
                    };
                    if (proof.OriginId is { } origin) receipt["originId"] = origin;
                    else receipt["sourceOrigin"] = AuthoritativeGameExperienceCoverage.SourceOrigin;
                    receipts[proof.ReceiptKey] = receipt;
                }
            }
            if (receipts.Count > 0) metadata["experienceCoverage"] = receipts;
            if (metadata.Count > 0) tx.Set(account, metadata, SetOptions.MergeAll);
            return (IReadOnlyList<GameExperienceCoverageProof>)persisted;
        }, cancellationToken: ct);

    private async Task<(bool Available, T? Value)> ReadSafe<T>(Func<Task<T>> read, string source, CancellationToken ct) where T : class
    {
        try { return (true, await read()); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Player experience source {Source} is unavailable; missing evidence remains unknown", source);
            return (false, null);
        }
    }

    internal static IEnumerable<PlayerGameExperienceEvidence> ReadActivity(string gameId, IReadOnlyDictionary<string, object> fields)
    {
        if (!PlayerGameExperienceProjection.GameIds.Contains(gameId)) yield break;
        var recent = fields.TryGetValue("playedAt", out var at) && at is Timestamp;
        if (!recent && !Boolean(fields, "hasPlayed")) yield break;
        if (recent) yield return new(gameId, "accepted-activity");
        var captured = Text(fields, "experienceCaptureVersion") == AcceptedGameParticipationDocuments.CaptureVersion;
        if (captured) yield return new(gameId, AcceptedGameParticipationDocuments.CaptureVersion);
        var tags = fields.TryGetValue("experienceProvenance", out var sources) && sources is IEnumerable<object> values
            ? values.OfType<string>().Where(source => source.Length is > 0 and <= 80).ToArray() : [];
        foreach (var source in tags) yield return new(gameId, source);
        if (!recent && !captured && tags.Length == 0) yield return new(gameId, "accepted-participation");
    }

    internal static IEnumerable<PlayerGameExperienceEvidence> ReadAnalytics(IReadOnlyDictionary<string, object> fields)
    {
        foreach (var (game, value) in Map(fields, "games"))
            if (PlayerGameExperienceProjection.GameIds.Contains(game) && value is Dictionary<string, object> metrics && Number(metrics, "humanRounds") is > 0)
                yield return new(game, "analytics-completion");
    }

    internal static PlayerGameExperienceEvidence? ReadLegacy(GameExperienceLegacySource source, string userId,
        IReadOnlyDictionary<string, object> fields)
    {
        if (Text(fields, source.OwnerField) != userId) return null;
        var hasDate = new[] { "createdAt", "createdAtUtc", "startedAt", "startedAtUtc", "completedAt", "completedAtUtc", "settledAt", "recognizedAt", "updatedAtUtc" }
            .Any(name => fields.TryGetValue(name, out var value) && value is Timestamp)
            || source.Kind == "hearts" && TimestampField(fields, "updatedAt")
            || source.Kind == "attempt" && (TimestampField(fields, "enteredAt") || TimestampField(fields, "submittedAt"));
        if (!hasDate) return null;
        string? game;
        switch (source.Kind)
        {
            case "outbox":
                if (!Boolean(fields, "isHuman") || !TimestampField(fields, "completedAt") || Text(fields, "mode") is not ("paid" or "practice" or "free")) return null;
                game = Text(fields, "gameId"); break;
            case "card":
                if (Text(fields, "status") is "queue" or "queued" or "cancelled") return null;
                game = Text(fields, "game"); break;
            case "slot":
                if (Text(fields, "spinId") is null || Text(fields, "gameId") is not { } runtime ||
                    !AcceptedGameActivityReader.SlotIds.TryGetValue(runtime, out game)) return null;
                break;
            case "attempt":
                if (Text(fields, "status") != "completed" && Number(fields, "entryFeeCents") is not > 0) return null;
                game = Text(fields, "gameId");
                if (game is not ("asteroids" or "flappy" or "horse-flight")) return null;
                break;
            case "solitaire":
                if (Text(fields, "status") != "completed" || Number(fields, "moves") is not > 0) return null;
                game = Text(fields, "gameId") ?? "solitaire";
                if (game is not ("solitaire" or "free-solitaire")) return null;
                break;
            case "snake":
                if (Text(fields, "stateJson") is not { } stateJson || !SnakeHasMoves(stateJson)) return null;
                game = "snake"; break;
            case "hearts":
                game = "hearts"; break;
            case "baccarat-round":
            case "casino-war-round":
            case "video-poker-round":
            case "keno-round":
            case "sic-bo-round":
                var (roundGame, stakeField) = source.Kind switch
                {
                    "baccarat-round" => ("baccarat", "stakeCents"),
                    "casino-war-round" => ("casino-war", "primaryStakeCents"),
                    "video-poker-round" => ("video-poker", "coinsWagered"),
                    "keno-round" => ("keno", "wagerCents"),
                    _ => ("sic-bo", "totalStakeCents"),
                };
                if (Text(fields, "roundId") is null || Number(fields, stakeField) is not > 0) return null;
                game = roundGame; break;
            default:
                if (!TimestampField(fields, "completedAt")) return null;
                game = source.Kind; break;
        }
        return game is not null && PlayerGameExperienceProjection.GameIds.Contains(game)
            ? new(game, "legacy-" + source.Collection) : null;
    }

    private static bool SnakeHasMoves(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var state = doc.RootElement.EnumerateObject().FirstOrDefault(item => item.Name.Equals("state", StringComparison.OrdinalIgnoreCase)).Value;
            return state.ValueKind == JsonValueKind.Object && state.EnumerateObject()
                .Any(item => item.Name.Equals("moves", StringComparison.OrdinalIgnoreCase) && item.Value.TryGetInt64(out var moves) && moves > 0);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static void ValidateUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 512 || userId.Any(char.IsControl))
            throw new ArgumentException("An authenticated player is required.", nameof(userId));
    }
    private static Dictionary<string, object> Map(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out var value) && value is Dictionary<string, object> map ? map : new();
    private static string? Text(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value as string : null;
    private static bool Boolean(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out var value) && value is true;
    private static bool TimestampField(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.TryGetValue(name, out var value) && value is Timestamp;
    private static double? Number(IReadOnlyDictionary<string, object> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value)) return null;
        var number = value switch { long integer => (double)integer, int integer => integer, double real => real, _ => double.NaN };
        return double.IsFinite(number) ? number : null;
    }
    public void Dispose() => accepted.Dispose();
}
