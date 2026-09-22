using System.Collections.Immutable;
using FortuneForge.Games.Flappy;
using FortuneForge.Server.Arcade.Flappy;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreFlappyFreeRunServiceTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreFlappyFreeRunServiceTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task StartPersistsAnAbandonableUserBoundRunAndExactRetryKeepsTheCommittedSeed()
    {
        var (database, service) = CreateService(17, 99);

        var first = await service.StartAsync("flappy-start-0001", "player-1", default);
        var retry = await service.StartAsync("flappy-start-0001", "player-1", default);

        Assert.False(first.WasAlreadyStarted);
        Assert.True(retry.WasAlreadyStarted);
        Assert.Equal(17U, first.Run.Seed);
        Assert.Equal(first.Run, retry.Run);
        var document = Assert.Single((await database.Collection(FirestoreFlappyFreeRunService.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(first.Run.RunId, Field<string>(document, "runId"));
        Assert.Equal(17L, Field<long>(document, "seed"));
        Assert.Equal("player-1", Field<string>(document, "playerId"));
        Assert.Equal("started", Field<string>(document, "status"));
        Assert.True(document.ContainsField("startedAt"));
        Assert.False(document.ContainsField("completedAt"));
        Assert.Empty((await database.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CompletionPersistsCanonicalAuthoritativeResultIsIdempotentAndRejectsConflicts()
    {
        var (database, service) = CreateService(17);
        var started = await service.StartAsync("flappy-start-0002", "player-1", default);
        var replay = new FlappyReplay(36, ImmutableArray<int>.Empty);
        var expected = FlappyReplayEvaluator.Evaluate(started.Run.Seed, replay);

        var first = await service.CompleteAsync(started.Run.RunId, "player-1", replay, default);
        var retry = await service.CompleteAsync(started.Run.RunId, "player-1", replay, default);

        Assert.False(first.WasAlreadyCompleted);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(expected.Snapshot.Score, first.Score);
        Assert.Equal("ground-collision", first.Terminal);
        Assert.Equal(first.Score, retry.Score);
        Assert.Equal(first.CompletedAtUtc, retry.CompletedAtUtc);
        var document = Assert.Single((await database.Collection(FirestoreFlappyFreeRunService.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal("completed", Field<string>(document, "status"));
        Assert.Equal(FirestoreFlappyFreeRunService.CanonicalizeReplay(replay), Field<string>(document, "replayCanonical"));
        Assert.Matches("^[0-9a-f]{64}$", Field<string>(document, "replayDigest"));
        Assert.Equal((long)expected.Snapshot.Score, Field<long>(document, "score"));
        Assert.Equal("ground-collision", Field<string>(document, "terminal"));
        Assert.True(document.ContainsField("startedAt"));
        Assert.True(document.ContainsField("completedAt"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId, "player-2", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId, "player-1", new FlappyReplay(37, ImmutableArray<int>.Empty), default));
    }

    private (FirestoreDb Database, FirestoreFlappyFreeRunService Service) CreateService(params uint[] seeds)
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-flappy-free-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var index = 0;
        return (database, new FirestoreFlappyFreeRunService(
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

    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
}
