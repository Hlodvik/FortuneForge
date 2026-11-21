using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Asteroids.Tests;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AsteroidsFreeRunStoreTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LostStartResponseRecoversTheOriginalSeedAcrossServiceInstancesWithoutMoneyWrites()
    {
        var database = Database();
        var first = await Store(database, 42).StartAsync("asteroids-lost-start-0001", "alice", default);
        var retry = await Store(database, 99).StartAsync("asteroids-lost-start-0001", "alice", default);
        var recovered = await Store(database, 71).GetAsync(first.Run.RunId, "alice", default);
        Assert.True(retry.WasAlreadyStarted);
        Assert.Equal(first.Run, retry.Run);
        Assert.Equal("000000000000002a", recovered.SeedHex);
        Assert.Equal("started", recovered.Status);
        Assert.Null(recovered.Replay);
        Assert.Single((await database.Collection(AsteroidsFreeRunStore.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        foreach (var collection in new[] { "userBalances", "balanceTransactions", "arcadeCompetitionAttempts", "asteroidsRuns" })
            Assert.Empty((await database.Collection(collection).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task TerminalCompletionRecoversExactlyAndRejectsDifferentReplayAndDifferentAccount()
    {
        var database = Database();
        var store = Store(database, 42);
        var started = await store.StartAsync("asteroids-lost-result-0001", "alice", default);
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, []);
        var first = await store.CompleteAsync(started.Run.RunId, "alice", replay, default);
        var restarted = Store(database, 99);
        var retry = await restarted.CompleteAsync(started.Run.RunId, "alice", replay, default);
        var recovered = await restarted.GetAsync(started.Run.RunId, "alice", default);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(first with { WasAlreadyCompleted = true }, retry);
        Assert.Equal("completed", recovered.Status);
        Assert.Equal(AsteroidsReplayEvaluator.CanonicalizeReplay(replay), AsteroidsReplayEvaluator.CanonicalizeReplay(recovered.Replay!));
        Assert.Equal(first.Score, recovered.Score);
        Assert.Equal(2, (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await database.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        Assert.Contains(AsteroidsReplayEvaluator.CanonicalizeReplay(replay), evidence.GetValue<string>("payloadJson"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.GetAsync(started.Run.RunId, "bob", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.CompleteAsync(started.Run.RunId, "bob", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.CompleteAsync(started.Run.RunId, "alice",
            new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [new(0, AsteroidsInput.Fire)]), default));
    }

    [Fact]
    public async Task HistoricalHostDocumentCompletesInPlaceAndPreservesItsSeedAndStartTime()
    {
        var database = Database();
        const string key = "asteroids-legacy-start-0001";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\nalice\n{key}")));
        var runId = $"asteroids_free_{id}";
        var originalTime = Now.AddDays(-10);
        var reference = database.Collection("asteroidsFreeRuns").Document(id);
        await reference.CreateAsync(new Dictionary<string, object>
        {
            ["runId"] = runId, ["seedHex"] = "000000010000002a", ["playerId"] = "alice",
            ["startIdempotencyKey"] = key, ["status"] = "started",
            ["startedAt"] = Timestamp.FromDateTime(originalTime.UtcDateTime), ["rulesVersion"] = 2L, ["schemaVersion"] = 1L,
        });
        var store = Store(database, 1);
        var retry = await store.StartAsync(key, "alice", default);
        Assert.True(retry.WasAlreadyStarted);
        Assert.Equal(0x000000010000002aUL, retry.Run.Seed);
        Assert.Equal(originalTime, retry.StartedAtUtc);
        var result = await store.CompleteAsync(runId, "alice", new(AsteroidsReplayEvaluator.MaximumReplaySteps, []), default);
        Assert.False(result.WasAlreadyCompleted);
        var recovered = await store.GetAsync(runId, "alice", default);
        Assert.Equal(originalTime, recovered.StartedAtUtc);
        Assert.Equal("000000010000002a", recovered.SeedHex);
        Assert.Single((await database.Collection("asteroidsFreeRuns").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("score")]
    [InlineData("replayDigest")]
    [InlineData("terminal")]
    [InlineData("replayCanonical")]
    public async Task RecoveryAndExactRetriesRejectCorruptedSavedOutcomes(string field)
    {
        var database = Database();
        var store = Store(database, 42);
        var started = await store.StartAsync("asteroids-corrupt-start-0001", "alice", default);
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, []);
        var completed = await store.CompleteAsync(started.Run.RunId, "alice", replay, default);
        object corrupted = field switch
        {
            "score" => completed.Score + 1,
            "replayDigest" => new string('0', 64),
            "terminal" => "completed",
            _ => "v2|03600|",
        };
        var document = Assert.Single((await database.Collection("asteroidsFreeRuns").GetSnapshotAsync()).Documents);
        await document.Reference.UpdateAsync(field, corrupted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync(started.Run.RunId, "alice", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAsync(started.Run.RunId, "alice", replay, default));
        Assert.Equal(2, (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task NonterminalReplayDoesNotChangeThePersistedRun()
    {
        var database = Database();
        var store = Store(database, 42);
        var started = await store.StartAsync("asteroids-short-start-0001", "alice", default);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CompleteAsync(started.Run.RunId, "alice", new(1, []), default));
        Assert.Equal("started", (await store.GetAsync(started.Run.RunId, "alice", default)).Status);
        Assert.Single((await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-asteroids-package-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static AsteroidsFreeRunStore Store(FirestoreDb database, ulong seed) =>
        new(database, new Clock(), () => seed);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
