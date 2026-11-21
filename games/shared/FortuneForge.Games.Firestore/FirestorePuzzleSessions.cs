using System.Text.Json;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Atomically persists a free-play board, undo state, signed receipt and audit evidence.</summary>
public abstract class FirestorePuzzleSessions<TRun, TState, TResponse>(
    FirestoreDb database, string gameId, string title, string sessionCollection, string receiptCollection)
    where TRun : class, IPuzzleSession<TState>
    where TState : class
    where TResponse : class
{
    protected abstract TResponse ToResponse(TRun run);
    protected abstract (GameSecurityEventDocument Observation, RoundAuditCapture? Completion) Capture(TRun run, DateTime nowUtc);
    protected abstract bool HasPlayed(TRun run);
    protected abstract GameAnalyticsSettlement? Settlement(TRun run, string collection, string id);
    protected abstract Exception GameNotFound();
    protected abstract Exception ConflictingReceipt();

    protected Task<TResponse> StartSessionAsync(TRun run, string? bodySha256, CancellationToken token) =>
        ExecuteAsync(run.UserId, bodySha256, (transaction, _) =>
        {
            Commit(transaction, Session(run.Id), run, run.StartedAtUtc, false, true, 1);
            return Task.FromResult(ToResponse(run));
        }, token);

    protected async Task<TResponse> ReadSessionAsync(string userId, Guid gameId, CancellationToken token) =>
        ToResponse(Open(userId, await Session(gameId).GetSnapshotAsync(token)));

    protected Task<TResponse> ChangeSessionAsync(string userId, Guid id, string? bodySha256,
        Func<TRun, DateTime, TRun> change, CancellationToken token) =>
        UpdateAsync(userId, id, bodySha256, (transaction, document, run, now, _) =>
        {
            var next = change(run, now);
            if (next.RunId == run.RunId && next.UndoDepth > run.UndoDepth)
                transaction.Set(UndoDocument(document, next), Encode(run.State));
            return Task.FromResult(next);
        }, token);

    protected Task<TResponse> UndoSessionAsync(string userId, Guid id, string? bodySha256,
        Action<TRun> requireUndo, Func<TRun, TState, DateTime, TRun> undoBoard, CancellationToken token) =>
        UpdateAsync(userId, id, bodySha256, async (transaction, document, run, now, cancellationToken) =>
        {
            requireUndo(run);
            var undoDocument = UndoDocument(document, run);
            var undo = await transaction.GetSnapshotAsync(undoDocument, cancellationToken);
            if (!undo.Exists)
                throw new FirestorePuzzleStorageException($"The stored {title} undo entry is missing.");
            var next = undoBoard(run, Decode<TState>(undo), now);
            transaction.Delete(undoDocument);
            return next;
        }, token);

    private Task<TResponse> UpdateAsync(string userId, Guid gameId, string? bodySha256,
        Func<Transaction, DocumentReference, TRun, DateTime, CancellationToken, Task<TRun>> update,
        CancellationToken token)
    {
        var document = Session(gameId);
        return ExecuteAsync(userId, bodySha256, async (transaction, cancellationToken) =>
        {
            var snapshot = await transaction.GetSnapshotAsync(document, cancellationToken);
            var run = Open(userId, snapshot);
            var hasPagedManifest = snapshot.TryGetValue<long>("journalEventCount", out var previousCount);
            var tailIsValid = hasPagedManifest && previousCount > 0 &&
                await ValidateManifestTailAsync(transaction, run, previousCount, cancellationToken);
            var legacyIds = hasPagedManifest ? null :
                await ReadLegacyManifestAsync(transaction, run, cancellationToken);
            // On a retried or cross-replica transaction, use at least the preceding command time.
            var now = DateTime.UtcNow;
            var previousTime = snapshot.GetValue<Timestamp>("updatedAtUtc").ToDateTime();
            if (now < previousTime) now = previousTime;
            var next = await update(transaction, document, run, now, cancellationToken);
            var sameRun = next.RunId == run.RunId;
            var sameEpisode = sameRun && next.CompletionEpisode == run.CompletionEpisode;
            var analyticsRecorded = sameRun && snapshot.GetValue<bool>("analyticsRecorded");
            if (sameEpisode && (hasPagedManifest ? !tailIsValid : legacyIds is null))
                throw new FirestorePuzzleStorageException($"The current {title} journal manifest cannot be continued.");
            var eventCount = sameEpisode ? checked((legacyIds?.LongLength ?? previousCount) + 1) : 1;
            Commit(transaction, document, next, now, analyticsRecorded, false, eventCount,
                sameEpisode ? legacyIds : null);
            return ToResponse(next);
        }, token);
    }

    private Task<TResponse> ExecuteAsync(string userId, string? bodySha256,
        Func<Transaction, CancellationToken, Task<TResponse>> operation, CancellationToken token) =>
        FirestoreGameCommands.ExecuteAsync(database, receiptCollection, userId, bodySha256, operation, ConflictingReceipt, token, title);

    private void Commit(Transaction transaction, DocumentReference document,
        TRun run, DateTime nowUtc, bool analyticsRecorded, bool create,
        long eventCount, string[]? legacyIds = null)
    {
        var (observation, completion) = Capture(run, nowUtc);
        transaction.Create(database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        WriteJournalManifest(transaction, run, observation.Id, eventCount, legacyIds);

        if (HasPlayed(run))
        {
            var participant = AcceptedGameParticipationDocuments.Create(run.UserId, gameId, nowUtc);
            participant.Fields["experienceProvenance"] = FieldValue.ArrayUnion("accepted-security-commit");
            transaction.Set(database.Collection("catalogPlayerActivity").Document(participant.DocOwnerKey)
                .Collection("games").Document(participant.GameId), participant.Fields, SetOptions.MergeAll);
        }

        if (completion is not null)
        {
            var auditId = completion.Summary.Id;
            FirestoreGameAudit.WriteCompletion(transaction, database, completion);
            if (!analyticsRecorded &&
                Settlement(run, RoundAuditDocuments.Summaries, auditId) is { } settlement &&
                GameAnalyticsOutbox.Create(settlement) is { } outbox)
            {
                transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(outbox.Id), outbox.Fields);
                analyticsRecorded = true;
            }
        }

        var fields = Encode(run);
        fields["userId"] = run.UserId;
        fields["analyticsRecorded"] = analyticsRecorded;
        fields["journalEventCount"] = eventCount;
        fields["updatedAtUtc"] = nowUtc;
        if (create) transaction.Create(document, fields);
        else transaction.Set(document, fields);
    }

    private async Task<bool> ValidateManifestTailAsync(Transaction transaction, TRun run,
        long count, CancellationToken token)
    {
        var instanceId = PuzzleSessionIdentity.EpisodeId(run.RunId, run.CompletionEpisode);
        var manifest = JournalManifest(instanceId);
        var pageSize = GameSecurityEventDocuments.ManifestPageSize;
        var index = (count - 1) / pageSize;
        var tail = manifest.Collection(GameSecurityEventDocuments.ManifestPageCollectionName)
            .Document(GameSecurityEventDocuments.ManifestPageId(index));
        var snapshots = await transaction.GetAllSnapshotsAsync([manifest, tail], token);
        var root = snapshots[0];
        var page = snapshots[1];
        return root.Exists && page.Exists &&
            root.TryGetValue<long>("schemaVersion", out var version) && version == GameSecurityEventDocuments.PagedManifestVersion &&
            root.TryGetValue<string>("gameId", out var recordedGameId) && recordedGameId == gameId &&
            root.TryGetValue<string>("instanceId", out var recordedInstance) && recordedInstance == instanceId &&
            root.TryGetValue<long>("pageSize", out var recordedSize) && recordedSize == pageSize &&
            root.TryGetValue<long>("eventCount", out var recordedCount) && recordedCount == count &&
            root.TryGetValue<long>("firstVersion", out var firstVersion) && firstVersion >= 0 &&
            firstVersion == run.EventVersion - (count - 1) &&
            page.TryGetValue<long>("pageIndex", out var recordedIndex) && recordedIndex == index &&
            page.TryGetValue<string[]>("eventIds", out var ids) && ids is not null &&
            ids.LongLength == (count - 1) % pageSize + 1 &&
            ids.All(GameSecurityEventDocuments.IsEventId) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length;
    }

    private async Task<string[]?> ReadLegacyManifestAsync(Transaction transaction, TRun run, CancellationToken token)
    {
        var instanceId = PuzzleSessionIdentity.EpisodeId(run.RunId, run.CompletionEpisode);
        var manifest = await transaction.GetSnapshotAsync(JournalManifest(instanceId), token);
        return manifest.Exists && manifest.TryGetValue<long>("schemaVersion", out var schema) && schema == 1 &&
            manifest.TryGetValue<string>("gameId", out var recordedGameId) && recordedGameId == gameId &&
            manifest.TryGetValue<string>("instanceId", out var recordedInstance) && recordedInstance == instanceId &&
            manifest.TryGetValue<string[]>("eventIds", out var ids) && ids is { Length: > 0 } &&
            ids.All(GameSecurityEventDocuments.IsEventId) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length
            ? ids : null;
    }

    private void WriteJournalManifest(Transaction transaction, TRun run, string eventId,
        long count, string[]? legacyIds)
    {
        var instanceId = PuzzleSessionIdentity.EpisodeId(run.RunId, run.CompletionEpisode);
        var manifest = JournalManifest(instanceId);
        var pages = manifest.Collection(GameSecurityEventDocuments.ManifestPageCollectionName);
        var pageSize = GameSecurityEventDocuments.ManifestPageSize;

        transaction.Set(manifest, new Dictionary<string, object>
        {
            ["gameId"] = gameId, ["instanceId"] = instanceId,
            ["schemaVersion"] = GameSecurityEventDocuments.PagedManifestVersion,
            ["eventCount"] = count, ["pageSize"] = pageSize, ["firstVersion"] = run.EventVersion - (count - 1),
        });
        if (legacyIds is not null)
        {
            var pageIndex = 0L;
            foreach (var ids in legacyIds.Append(eventId).Chunk(pageSize))
            {
                transaction.Create(pages.Document(GameSecurityEventDocuments.ManifestPageId(pageIndex)),
                    new Dictionary<string, object> { ["pageIndex"] = pageIndex, ["eventIds"] = ids });
                pageIndex++;
            }
        }
        else
        {
            var pageIndex = (count - 1) / pageSize;
            var page = pages.Document(GameSecurityEventDocuments.ManifestPageId(pageIndex));
            if ((count - 1) % pageSize == 0)
                transaction.Create(page, new Dictionary<string, object> { ["pageIndex"] = pageIndex, ["eventIds"] = new[] { eventId } });
            else
                transaction.Update(page, new Dictionary<string, object> { ["eventIds"] = FieldValue.ArrayUnion(eventId) });
        }
    }

    private DocumentReference JournalManifest(string instanceId) =>
        database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, instanceId));

    private DocumentReference Session(Guid id) => database.Collection(sessionCollection).Document(id.ToString("N"));

    // One document per undo entry avoids a single unbounded history document.
    private static DocumentReference UndoDocument(DocumentReference session, TRun run) =>
        session.Collection("undo").Document($"{run.RunId:N}-{run.UndoDepth}");

    private TRun Open(string userId, DocumentSnapshot snapshot) =>
        snapshot.Exists && string.Equals(snapshot.GetValue<string>("userId"), userId, StringComparison.Ordinal)
            ? Decode<TRun>(snapshot)
            : throw GameNotFound();

    private static Dictionary<string, object> Encode<T>(T value) =>
        new() { ["json"] = JsonSerializer.Serialize(value, RoundAuditCapture.Json) };

    private T Decode<T>(DocumentSnapshot snapshot) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(snapshot.GetValue<string>("json"), RoundAuditCapture.Json)
                ?? throw new FirestorePuzzleStorageException($"A stored {title} document has no state.");
        }
        catch (JsonException exception)
        {
            throw new FirestorePuzzleStorageException($"A stored {title} document has invalid state.", exception);
        }
    }
}

public sealed class FirestorePuzzleStorageException(string message, Exception? inner = null) : Exception(message, inner);
