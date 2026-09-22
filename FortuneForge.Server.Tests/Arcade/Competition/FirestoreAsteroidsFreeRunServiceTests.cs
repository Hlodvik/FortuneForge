using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreAsteroidsFreeRunServiceTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreAsteroidsFreeRunServiceTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task StartPersistsAnAbandonableUserBoundRunAndExactRetryKeepsTheCommittedServerSeed()
    {
        var (database, service) = CreateService(42, 99);

        var first = await service.StartAsync("asteroids-start-0001", "player-1", default);
        var retry = await service.StartAsync("asteroids-start-0001", "player-1", default);

        Assert.False(first.WasAlreadyStarted);
        Assert.True(retry.WasAlreadyStarted);
        Assert.Equal(42UL, first.Run.Seed);
        Assert.Equal(first.Run, retry.Run);
        var document = Assert.Single((await database.Collection(FirestoreAsteroidsFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal(first.Run.RunId, Field<string>(document, "runId"));
        Assert.Equal("000000000000002a", Field<string>(document, "seedHex"));
        Assert.Equal("player-1", Field<string>(document, "playerId"));
        Assert.Equal("started", Field<string>(document, "status"));
        Assert.True(document.ContainsField("startedAt"));
        Assert.False(document.ContainsField("completedAt"));
        Assert.Empty((await database.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CompletionPersistsCanonicalAuthoritativeResultIsIdempotentAndRejectsConflicts()
    {
        var (database, service) = CreateService(42);
        var started = await service.StartAsync("asteroids-start-0002", "player-1", default);
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, []);
        var expected = AsteroidsReplayEvaluator.Evaluate(started.Run, replay);

        var first = await service.CompleteAsync(started.Run.RunId, "player-1", replay, default);
        var retry = await service.CompleteAsync(started.Run.RunId, "player-1", replay, default);

        Assert.False(first.WasAlreadyCompleted);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(expected.Score, first.Score);
        Assert.Equal(expected.Terminal, first.Terminal);
        Assert.Equal(first.Score, retry.Score);
        Assert.Equal(first.CompletedAtUtc, retry.CompletedAtUtc);
        var document = Assert.Single((await database.Collection(FirestoreAsteroidsFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal("completed", Field<string>(document, "status"));
        Assert.Equal(AsteroidsReplayEvaluator.CanonicalizeReplay(replay), Field<string>(document, "replayCanonical"));
        Assert.Matches("^[0-9a-f]{64}$", Field<string>(document, "replayDigest"));
        Assert.Equal(expected.Score, Field<long>(document, "score"));
        Assert.Equal(expected.Terminal.ToString().ToLowerInvariant(), Field<string>(document, "terminal"));
        Assert.True(document.ContainsField("startedAt"));
        Assert.True(document.ContainsField("completedAt"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId, "player-2", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId,
            "player-1",
            new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [new AsteroidsInputCommand(0, AsteroidsInput.Fire)]),
            default));
    }

    private (FirestoreDb Database, FirestoreAsteroidsFreeRunService Service) CreateService(params ulong[] seeds)
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-asteroids-free-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var index = 0;
        return (database, new FirestoreAsteroidsFreeRunService(
            database,
            new FixedTimeProvider(Start),
            () => seeds[Math.Min(index++, seeds.Length - 1)]));
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
}
