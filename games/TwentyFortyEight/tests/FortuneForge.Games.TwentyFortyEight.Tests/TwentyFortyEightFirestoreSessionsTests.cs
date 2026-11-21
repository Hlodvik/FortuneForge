using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.TwentyFortyEight.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.TwentyFortyEight.Tests;

public sealed class TwentyFortyEightFirestoreSessionsTests
{
    [FirestoreEmulatorFact]
    public async Task LostStartAndResetResponsesReplayTheOriginalRunAfterSubsequentMoves()
    {
        var database = Connect(NewProjectId());
        var worker = new TwentyFortyEightFirestoreSessions(database);
        var replica = new TwentyFortyEightFirestoreSessions(database);
        var start = Observation("/api/games/2048/games", "start-2048");
        var digest = new string('C', 64);
        TwentyFortyEightGameResponse opened;
        using (GameSecurityContext.Push(start))
            opened = await worker.StartAsync("alice", 32, default, digest);
        await replica.MoveAsync("alice", opened.GameId, "up", default);
        using (GameSecurityContext.Push(start with { RequestId = "lost-start-retry" }))
        {
            var recovered = await replica.StartAsync("alice", 32, default, digest);
            Assert.Equal(opened.GameId, recovered.GameId);
            Assert.Equal(opened.RunId, recovered.RunId);
            Assert.Equal(opened.Tiles, recovered.Tiles);
            Assert.Equal(0, recovered.Moves);
        }
        Assert.Single((await database.Collection("twentyFortyEightSessions").GetSnapshotAsync()).Documents);

        var reset = Observation($"/api/games/2048/games/{opened.GameId:D}/reset", "reset-2048");
        TwentyFortyEightGameResponse restarted;
        using (GameSecurityContext.Push(reset))
            restarted = await worker.ResetAsync("alice", opened.GameId, 73, default, digest);
        await replica.MoveAsync("alice", opened.GameId, "left", default);
        using (GameSecurityContext.Push(reset with { RequestId = "lost-reset-retry" }))
        {
            var recovered = await replica.ResetAsync("alice", opened.GameId, 73, default, digest);
            Assert.Equal(restarted.RunId, recovered.RunId);
            Assert.Equal(restarted.Tiles, recovered.Tiles);
            await Assert.ThrowsAsync<TwentyFortyEightRuleException>(
                () => replica.ResetAsync("alice", opened.GameId, 74, default, new string('D', 64)));
        }
        var current = await replica.GetAsync("alice", opened.GameId, default);
        Assert.Equal(restarted.RunId, current.RunId);
        Assert.Equal(1, current.Moves);
    }

    private static GameRequestObservation Observation(string path, string command) =>
        new(Guid.NewGuid().ToString("N"), "alice", null, null, DateTime.UtcNow, "POST", path,
            TwentyFortyEightSession.GameId, null, command, null, "{}", "sanitized", "direct");

    [FirestoreEmulatorFact]
    public async Task GameplayTransfersBetweenReplicasAndRedeliveryDoesNotAdvanceTheBoardTwice()
    {
        var projectId = NewProjectId();
        var gateway = new TwentyFortyEightFirestoreSessions(Connect(projectId));
        var replica = new TwentyFortyEightFirestoreSessions(Connect(projectId));
        var opened = await gateway.StartAsync("alice", 32, default);

        await Assert.ThrowsAsync<TwentyFortyEightAccessException>(() => replica.GetAsync("bob", opened.GameId, default));
        var fromReplica = await replica.GetAsync("alice", opened.GameId, default);
        Assert.Equal(opened.Tiles, fromReplica.Tiles);
        Assert.Equal(opened.RunId, fromReplica.RunId);
        Assert.Equal(7, opened.GameId.Version);

        var route = $"/api/games/2048/games/{opened.GameId}/move";
        var firstRequest = new GameRequestObservation("move-request", "alice", null, null, DateTime.UtcNow, "POST", route,
            TwentyFortyEightSession.GameId, opened.GameId.ToString("D"), "move-1", null, "{\"direction\":\"up\"}", "sanitized", "direct");
        var retryRequest = firstRequest with { RequestId = "move-request-retry" };
        var bodyDigest = new string('A', 64);
        var conflictingDigest = new string('B', 64);
        var results = await Task.WhenAll(MoveAsync(gateway, firstRequest, "up", bodyDigest),
            MoveAsync(replica, retryRequest, "up", bodyDigest));
        Assert.All(results, result => Assert.Equal(1, result.Moves));
        Assert.Equal(results[0].Tiles, results[1].Tiles);
        await Assert.ThrowsAsync<TwentyFortyEightRuleException>(
            () => MoveAsync(replica, retryRequest, "right", conflictingDigest));

        var undone = await gateway.UndoAsync("alice", opened.GameId, default);
        Assert.Equal(opened.Tiles, undone.Tiles);
        Assert.False(undone.CanUndo);
        await Assert.ThrowsAsync<TwentyFortyEightRuleException>(() => replica.UndoAsync("alice", opened.GameId, default));

        var reset = await replica.ResetAsync("alice", opened.GameId, 73, default);
        Assert.Equal(opened.GameId, reset.GameId);
        Assert.NotEqual(opened.RunId, reset.RunId);
        Assert.Equal(TwentyFortyEightEngine.Start(73).Tiles.ToArray(), reset.Tiles.ToArray());
        Assert.False(reset.CanUndo);
        Assert.Equal(0, (await Manifest(Connect(projectId), reset.RunId).GetSnapshotAsync()).GetValue<long>("firstVersion"));

        async Task<TwentyFortyEightGameResponse> MoveAsync(TwentyFortyEightFirestoreSessions store, GameRequestObservation observation,
            string direction, string digest)
        {
            using var request = GameSecurityContext.Push(observation);
            return await store.MoveAsync("alice", opened.GameId, direction, default, digest);
        }
    }

    [FirestoreEmulatorFact]
    public async Task TerminalEvidenceIsImmutableAcrossUndoAndRecompletion()
    {
        var database = Connect(NewProjectId());
        var worker = new TwentyFortyEightFirestoreSessions(database);
        var current = await worker.StartAsync("alice", 32, default);
        var directions = new[] { "up", "right", "down", "left" };
        var lastDirection = "";
        for (var move = 0; move < 200 && current.Phase == "playing"; move++)
        {
            lastDirection = directions[move % directions.Length];
            current = await worker.MoveAsync("alice", current.GameId, lastDirection, default);
        }

        Assert.Equal("lost", current.Phase);
        var first = await ReadSummaryAsync(database, current.RunId, 1);
        Assert.Equal("passed", first.GetValue<string>("auditStatus"));
        Assert.Equal(TwentyFortyEightAudit.FirestoreEvidenceKind, first.GetValue<string>("evidenceKind"));
        var originalEvidence = await database.Collection(RoundAuditDocuments.Evidence).Document(first.Id).GetSnapshotAsync();
        Assert.Equal(first.GetValue<string>("evidenceSha256"), originalEvidence.GetValue<string>("sha256"));

        var reopened = await worker.UndoAsync("alice", current.GameId, default);
        Assert.Equal("playing", reopened.Phase);
        current = await worker.MoveAsync("alice", current.GameId, lastDirection, default);
        Assert.Equal("lost", current.Phase);
        var second = await ReadSummaryAsync(database, current.RunId, 2);
        Assert.Equal("passed", second.GetValue<string>("auditStatus"));
        Assert.Equal(originalEvidence.GetValue<string>("payloadJson"),
            (await originalEvidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        var settlements = await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync();
        var settlement = Assert.Single(settlements.Documents);
        Assert.Equal(first.Id, settlement.GetValue<string>("sourceDocumentId"));
        var journal = await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync();
        var observations = journal.Documents.Select(document =>
        {
            using var payload = JsonDocument.Parse(document.GetValue<string>("payloadJson"));
            var root = payload.RootElement;
            return new PuzzleActionObservation(root.GetProperty("version").GetInt64(),
                root.GetProperty("eventKind").GetString()!, root.GetProperty("occurredAtUtc").GetDateTime(),
                root.GetProperty("state").Clone(), null);
        }).OrderBy(observation => observation.Version).ToArray();
        Assert.DoesNotContain(TwentyFortyEightActionReplay.InspectCompleteRun(observations), finding => finding.Severity == "error");
    }

    [FirestoreEmulatorFact]
    public async Task LongSessionPagesItsManifestAndCannotAppendToADamagedTail()
    {
        var database = Connect(NewProjectId());
        var worker = new TwentyFortyEightFirestoreSessions(database);
        var possibleStarts = from seed in Enumerable.Range(1, 1000).Select(value => (uint)value)
                             let board = TwentyFortyEightEngine.Start(seed)
                             from direction in Enum.GetValues<TwentyFortyEightDirection>()
                             where TwentyFortyEightEngine.Move(board, direction).Event == TwentyFortyEightEventType.NoMove
                             select (seed, direction);
        var (firstSeed, noMoveDirection) = possibleStarts.First();
        var started = await worker.StartAsync("alice", firstSeed, default);

        var pageSize = GameSecurityEventDocuments.ManifestPageSize;
        for (var move = 0; move <= pageSize; move++)
        {
            var result = await worker.MoveAsync("alice", started.GameId, noMoveDirection.ToString(), default);
            Assert.Equal("no-move", result.LastEvent);
        }

        var manifest = Manifest(database, started.RunId);
        var root = await manifest.GetSnapshotAsync();
        Assert.Equal(GameSecurityEventDocuments.PagedManifestVersion, root.GetValue<long>("schemaVersion"));
        Assert.Equal(pageSize + 2L, root.GetValue<long>("eventCount"));
        Assert.False(root.ContainsField("eventIds"));

        var pages = await manifest.Collection(GameSecurityEventDocuments.ManifestPageCollectionName)
            .OrderBy(FieldPath.DocumentId).GetSnapshotAsync();
        Assert.Equal(2, pages.Count);
        Assert.Equal(pageSize, pages.Documents[0].GetValue<string[]>("eventIds").Length);
        Assert.Equal(2, pages.Documents[1].GetValue<string[]>("eventIds").Length);
        Assert.All(pages.Documents.SelectMany(page => page.GetValue<string[]>("eventIds")),
            id => Assert.True(GameSecurityEventDocuments.IsEventId(id)));

        await pages.Documents[1].Reference.DeleteAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => worker.MoveAsync("alice", started.GameId, noMoveDirection.ToString(), default));
        Assert.Contains("journal manifest cannot be continued", error.Message, StringComparison.Ordinal);
        Assert.Equal(pageSize + 2L, (await manifest.GetSnapshotAsync()).GetValue<long>("eventCount"));
        Assert.False((await pages.Documents[1].Reference.GetSnapshotAsync()).Exists);
    }

    [FirestoreEmulatorFact]
    public async Task StagedLegacyManifestKeepsItsEventsWhenUpgraded()
    {
        var database = Connect(NewProjectId());
        var worker = new TwentyFortyEightFirestoreSessions(database);
        var started = await worker.StartAsync("alice", 32, default);
        var manifest = Manifest(database, started.RunId);
        var page = manifest.Collection(GameSecurityEventDocuments.ManifestPageCollectionName)
            .Document(GameSecurityEventDocuments.ManifestPageId(0));
        var first = Assert.Single((await page.GetSnapshotAsync()).GetValue<string[]>("eventIds"));

        var batch = database.StartBatch();
        batch.Update(database.Collection("twentyFortyEightSessions").Document(started.GameId.ToString("N")),
            new Dictionary<string, object> { ["journalEventCount"] = FieldValue.Delete });
        batch.Set(manifest, new Dictionary<string, object>
        {
            ["gameId"] = "2048", ["instanceId"] = $"{started.RunId:N}-episode-1",
            ["schemaVersion"] = 1L, ["eventIds"] = new[] { first },
        });
        batch.Delete(page);
        await batch.CommitAsync();

        await worker.MoveAsync("alice", started.GameId, "up", default);
        var ids = (await page.GetSnapshotAsync()).GetValue<string[]>("eventIds");
        Assert.Equal(2, ids.Length);
        Assert.Equal(first, ids[0]);
        Assert.Equal(GameSecurityEventDocuments.PagedManifestVersion, (await manifest.GetSnapshotAsync()).GetValue<long>("schemaVersion"));
    }

    private static DocumentReference Manifest(FirestoreDb database, Guid runId) =>
        database.Collection(GameSecurityEventDocuments.ManifestCollectionName).Document(
            GameSecurityEventDocuments.ManifestId(TwentyFortyEightSession.GameId, $"{runId:N}-episode-1"));

    private static Task<DocumentSnapshot> ReadSummaryAsync(FirestoreDb database, Guid runId, int episode) =>
        database.Collection(RoundAuditDocuments.Summaries).Document(
            GameSecurityEventDocuments.Hash($"2048\n{runId:N}-episode-{episode}")).GetSnapshotAsync();

    private static FirestoreDb Connect(string projectId) =>
        new FirestoreDbBuilder { ProjectId = projectId, EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();

    private static string NewProjectId() => $"demo-2048-{Guid.NewGuid():N}"[..29];
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with the Firestore emulator to verify durable 2048 sessions.";
    }
}
