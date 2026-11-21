using System.Text.Json;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DropMerge.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.DropMerge.Tests;

public sealed class DropMergeFirestoreSessionsTests
{
    [FirestoreEmulatorFact]
    public async Task LostStartAndUndoResponsesRecoverExactlyWithoutDiscardingAnotherDrop()
    {
        var database = Connect(NewProject());
        var worker = new DropMergeFirestoreSessions(database);
        var replica = new DropMergeFirestoreSessions(database);
        var digest = new string('C', 64);
        var start = Observation("/api/games/drop-merge/games", "start-drop-merge");
        DropMergeGameResponse opened;
        using (GameSecurityContext.Push(start))
            opened = await worker.StartAsync("alice", 41, default, digest);
        using (GameSecurityContext.Push(start with { RequestId = "lost-start-retry" }))
        {
            var recovered = await replica.StartAsync("alice", 41, default, digest);
            Assert.Equal(opened.GameId, recovered.GameId);
            Assert.Equal(opened.RunId, recovered.RunId);
        }
        Assert.Single((await database.Collection("dropMergeSessions").GetSnapshotAsync()).Documents);
        await worker.DropAsync("alice", opened.GameId, 0, default);
        await worker.DropAsync("alice", opened.GameId, 1, default);
        var undo = Observation($"/api/games/drop-merge/games/{opened.GameId:D}/undo", "undo-drop-merge");
        DropMergeGameResponse undone;
        using (GameSecurityContext.Push(undo))
            undone = await worker.UndoAsync("alice", opened.GameId, default, digest);
        var later = await replica.DropAsync("alice", opened.GameId, 2, default);
        using (GameSecurityContext.Push(undo with { RequestId = "lost-undo-retry" }))
        {
            var recovered = await replica.UndoAsync("alice", opened.GameId, default, digest);
            Assert.Equal(undone.Tiles, recovered.Tiles);
            Assert.Equal(undone.CurrentTile, recovered.CurrentTile);
            Assert.Equal(undone.DropTimerMilliseconds, recovered.DropTimerMilliseconds);
            Assert.Equal(1, recovered.Moves);
        }
        var current = await worker.GetAsync("alice", opened.GameId, default);
        Assert.Equal(later.Tiles, current.Tiles);
        Assert.Equal(2, current.Moves);
        var previous = await worker.UndoAsync("alice", opened.GameId, default);
        Assert.Equal(undone.Tiles, previous.Tiles);
        Assert.Equal(opened.Tiles, (await worker.UndoAsync("alice", opened.GameId, default)).Tiles);
    }

    private static GameRequestObservation Observation(string path, string command) =>
        new(Guid.NewGuid().ToString("N"), "alice", null, null, DateTime.UtcNow, "POST", path,
            DropMergeSession.GameId, null, command, null, "{}", "sanitized", "direct");

    [FirestoreEmulatorFact]
    public async Task ReplicasPreserveTheBoardTimerUndoAndRequestReceipt()
    {
        var project = NewProject();
        var worker = new DropMergeFirestoreSessions(Connect(project));
        var replica = new DropMergeFirestoreSessions(Connect(project));
        var started = await worker.StartAsync("alice", 41, default);
        Assert.True(PuzzleSessionIdentity.IsDurableId(started.GameId));
        await Assert.ThrowsAsync<DropMergeAccessException>(() => replica.GetAsync("bob", started.GameId, default));

        var route = $"/api/games/drop-merge/games/{started.GameId:D}/drop";
        var request = new GameRequestObservation("drop-request", "alice", null, null, DateTime.UtcNow, "POST",
            route, DropMergeSession.GameId, started.GameId.ToString("D"), "drop-1", null, "{\"column\":0}", "sanitized", "direct");
        var firstDigest = new string('A', 64);
        var options = await Task.WhenAll(DropAsync(worker, request, 0, firstDigest),
            DropAsync(replica, request with { RequestId = "drop-retry" }, 0, firstDigest));
        var expected = DropMergeEngine.Drop(DropMergeEngine.Start(41), 0);
        Assert.All(options, result =>
        {
            Assert.Equal(expected.State.Tiles, result.Tiles);
            Assert.Equal(expected.State.DropDurationMilliseconds, result.DropDurationMilliseconds);
            Assert.Equal(expected.State.DropTimerMilliseconds, result.DropTimerMilliseconds);
            Assert.Equal(1, result.Moves);
        });
        await Assert.ThrowsAsync<DropMergeRuleException>(() => DropAsync(replica, request, 1, new string('B', 64)));

        var undone = await replica.UndoAsync("alice", started.GameId, default);
        Assert.Equal(started.Tiles, undone.Tiles);
        Assert.Equal(started.CurrentTile, undone.CurrentTile);
        Assert.Equal(started.NextTile, undone.NextTile);
        Assert.False(undone.CanUndo);
        var reset = await worker.ResetAsync("alice", started.GameId, 73, default);
        Assert.Equal(started.GameId, reset.GameId);
        Assert.NotEqual(started.RunId, reset.RunId);
        Assert.Equal(0, reset.Moves);

        async Task<DropMergeGameResponse> DropAsync(DropMergeFirestoreSessions service, GameRequestObservation observation,
            int column, string digest)
        {
            using var scope = GameSecurityContext.Push(observation);
            return await service.DropAsync("alice", started.GameId, column, default, digest);
        }
    }

    [FirestoreEmulatorFact]
    public async Task LossCommitsAuditAndReopeningPreservesItWithoutDoubleCount()
    {
        var database = Connect(NewProject());
        var worker = new DropMergeFirestoreSessions(database);
        var current = await worker.StartAsync("alice", 41, default);
        var lastColumn = 0;
        for (var move = 0; move < 500 && current.Phase == "playing"; move++)
        {
            lastColumn = move % current.Columns;
            current = await worker.DropAsync("alice", current.GameId, lastColumn, default);
        }

        Assert.Equal("lost", current.Phase);
        var audit = await SummaryAsync(database, current.RunId, 1);
        Assert.Equal("passed", audit.GetValue<string>("auditStatus"));
        Assert.Equal(DropMergeAudit.FirestoreEvidenceKind, audit.GetValue<string>("evidenceKind"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        Assert.Equal(audit.GetValue<string>("evidenceSha256"), evidence.GetValue<string>("sha256"));

        var manifest = await database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(DropMergeSession.GameId, PuzzleSessionIdentity.EpisodeId(current.RunId, 1)))
            .GetSnapshotAsync();
        Assert.Equal(GameSecurityEventDocuments.PagedManifestVersion, manifest.GetValue<long>("schemaVersion"));

        var undone = await worker.UndoAsync("alice", current.GameId, default);
        Assert.Equal("playing", undone.Phase);
        var replay = await worker.DropAsync("alice", current.GameId, lastColumn, default);
        Assert.Equal("lost", replay.Phase);
        Assert.Equal("passed", (await SummaryAsync(database, replay.RunId, 2)).GetValue<string>("auditStatus"));
        var outbox = await database.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync();
        Assert.Equal(audit.Id, Assert.Single(outbox.Documents).GetValue<string>("sourceDocumentId"));
        var journal = await database.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync();
        var observations = journal.Documents.Select(document =>
        {
            using var payload = JsonDocument.Parse(document.GetValue<string>("payloadJson"));
            var root = payload.RootElement;
            return new PuzzleActionObservation(root.GetProperty("version").GetInt64(),
                root.GetProperty("eventKind").GetString()!, root.GetProperty("occurredAtUtc").GetDateTime(),
                root.GetProperty("state").Clone(), null);
        }).OrderBy(observation => observation.Version).ToArray();
        Assert.DoesNotContain(DropMergeActionReplay.InspectCompleteRun(observations), finding => finding.Severity == "error");
    }

    private static Task<DocumentSnapshot> SummaryAsync(FirestoreDb database, Guid runId, int episode) =>
        database.Collection(RoundAuditDocuments.Summaries).Document(
            GameSecurityEventDocuments.Hash($"{DropMergeSession.GameId}\n{PuzzleSessionIdentity.EpisodeId(runId, episode)}")).GetSnapshotAsync();

    private static FirestoreDb Connect(string project) =>
        new FirestoreDbBuilder { ProjectId = project, EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();

    private static string NewProject() => $"demo-drop-merge-{Guid.NewGuid():N}"[..29];
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with the Firestore emulator to verify durable Drop Merge sessions.";
    }
}
