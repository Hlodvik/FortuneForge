using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Snake.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Snake.Tests;

public sealed class SnakeBackendTests
{
    [FirestoreEmulatorFact]
    public async Task LostResponsesRetryAcrossReplicasWithoutMovingOrResettingTwice()
    {
        var database = Connect();
        var first = new FirestoreSnakeGameService(database, TimeProvider.System, () => 7);
        var replica = new FirestoreSnakeGameService(database, TimeProvider.System, () => 31);
        var startObservation = Observation("start", "/api/games/snake/games");
        SnakeGameResponse started;
        using (GameSecurityContext.Push(startObservation))
            started = await first.StartAsync("alice", new(null, 16, 16), default, new string('a', 64));
        using (GameSecurityContext.Push(startObservation with { RequestId = "lost-start-response" }))
            Assert.Equal(started.GameId, (await replica.StartAsync("alice", new(null, 16, 16), default, new string('a', 64))).GameId);
        await Assert.ThrowsAsync<SnakeAccessException>(() => replica.GetAsync("bob", started.GameId, default));

        var tickObservation = Observation("tick-1", $"/api/games/snake/games/{started.GameId:D}/tick");
        SnakeGameResponse moved;
        using (GameSecurityContext.Push(tickObservation))
            moved = await first.TickAsync("alice", started.GameId, default, new string('b', 64));
        using (GameSecurityContext.Push(tickObservation with { RequestId = "retry-tick" }))
        {
            var recovered = await replica.TickAsync("alice", started.GameId, default, new string('b', 64));
            Assert.Equal(moved.Moves, recovered.Moves);
            Assert.Equal(moved.Body, recovered.Body);
            await Assert.ThrowsAsync<SnakeRuleException>(() => replica.TickAsync("alice", started.GameId, default, new string('c', 64)));
        }
        Assert.Equal(1, (await replica.GetAsync("alice", started.GameId, default)).Moves);
        var beforeReset = await database.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync();
        var resetObservation = Observation("reset-1", $"/api/games/snake/games/{started.GameId:D}/reset");
        using (GameSecurityContext.Push(resetObservation))
            await first.ResetAsync("alice", started.GameId, new(null, 20, 20), default, new string('d', 64));
        var afterReset = await database.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync();
        using (GameSecurityContext.Push(resetObservation with { RequestId = "retry-reset" }))
            await replica.ResetAsync("alice", started.GameId, new(null, 20, 20), default, new string('d', 64));
        var recoveredReset = await database.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync();
        Assert.Equal(afterReset.GetValue<string>("runId"), recoveredReset.GetValue<string>("runId"));
        Assert.NotEqual(beforeReset.GetValue<string>("runId"), recoveredReset.GetValue<string>("runId"));
        var oldSummary = await database.Collection(RoundAuditDocuments.Summaries).Document(
            GameSecurityEventDocuments.Hash("snake\n" + beforeReset.GetValue<string>("runId"))).GetSnapshotAsync();
        Assert.Equal("incomplete", oldSummary.GetValue<string>("auditStatus"));
    }

    [FirestoreEmulatorFact]
    public async Task LegacyStateRestartsWithHistoryAndTerminalReplayIntact()
    {
        var database = Connect();
        var id = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var game = new SnakeStoredGame(id, runId, "alice", DateTime.UtcNow, SnakeEngine.Start(7, 16, 16),
            0, [], 0, "started", 0, "Legacy saved game.");
        // This is the pre-migration schema, not a newly created package document.
        await database.Collection("snakeGames").Document(id.ToString("N")).SetAsync(new Dictionary<string, object>
        {
            ["gameId"] = id.ToString("N"), ["runId"] = runId.ToString("N"), ["userId"] = "alice",
            ["stateJson"] = JsonSerializer.Serialize(game, RoundAuditCapture.Json), ["schemaVersion"] = 1L,
            ["startedAtUtc"] = game.StartedAtUtc, ["updatedAtUtc"] = game.StartedAtUtc,
        });
        var worker = new FirestoreSnakeGameService(database, TimeProvider.System);
        Assert.Equal("Legacy saved game.", (await worker.GetAsync("alice", id, default)).Message);
        SnakeGameResponse current = await worker.GetAsync("alice", id, default);
        for (var index = 0; index < 8; index++) current = await worker.TickAsync("alice", id, default);
        Assert.Equal("lost", current.Phase);
        var auditId = GameSecurityEventDocuments.Hash("snake\n" + runId.ToString("N"));
        var summary = await database.Collection(RoundAuditDocuments.Summaries).Document(auditId).GetSnapshotAsync();
        Assert.Equal("passed", summary.GetValue<string>("auditStatus"));
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(auditId).GetSnapshotAsync();
        var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson")).RootElement;
        var state = payload.GetProperty("state").Deserialize<SnakeState>(RoundAuditCapture.Json)!;
        var actions = payload.GetProperty("actions").Deserialize<string[]>(RoundAuditCapture.Json)!;
        Assert.Empty(SnakeReplayAudit.Check(state, 0, actions, false));
        Assert.Contains(SnakeReplayAudit.Check(state with { Score = state.Score + 10 }, 0, actions, false), item => item.Severity == "error");
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.TickAsync("alice", id, default));
    }

    private static GameRequestObservation Observation(string command, string path) =>
        new(Guid.NewGuid().ToString("N"), "alice", null, null, DateTime.UtcNow, "POST", path,
            "snake", null, command, null, null, "sanitized", "direct");
    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-snake-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for durable Snake verification.";
    }
}
