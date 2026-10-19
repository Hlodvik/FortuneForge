using Google.Api.Gax;
using Google.Cloud.Firestore;
using FortuneForge.Server.Cards.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreSolitaireFreeRunServiceTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreSolitaireFreeRunServiceTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task StartPersistsUserBoundAbandonableRunAndExactRetryKeepsServerIdentity()
    {
        var (database, service, _) = CreateService(42, 99);

        var first = await service.StartAsync("solitaire-free-start-0001", "user-1", 3, default);
        var retry = await service.StartAsync("solitaire-free-start-0001", "user-1", 3, default);

        Assert.False(first.WasAlreadyStarted);
        Assert.True(retry.WasAlreadyStarted);
        Assert.Equal(first, retry with { WasAlreadyStarted = false });
        Assert.Equal(42u, first.Seed);
        var document = Assert.Single((await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal(first.RunId, Field<string>(document, "runId"));
        Assert.Equal("user-1", Field<string>(document, "userId"));
        Assert.Equal(42L, Field<long>(document, "seed"));
        Assert.Equal(3L, Field<long>(document, "drawCount"));
        Assert.Equal("started", Field<string>(document, "status"));
        Assert.True(document.ContainsField("startedAt"));
        Assert.False(document.ContainsField("completedAt"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-start-0001", "user-1", 1, default));
        await AssertFinancialAndCompetitiveCollectionsRemainEmpty(database);
    }

    [Fact]
    public async Task ReplayStartCopiesCompletedDealIntoDistinctRunAndCanCompleteDifferently()
    {
        var (database, service, _) = CreateService(42, 99);
        var original = await service.StartAsync("solitaire-free-source-0001", "user-1", 3, default);
        await service.CompleteAsync(original.RunId, "user-1",
            [new SolitaireFreeReplayCommand("draw", null, null, null, null)], default);

        var replay = await service.StartAsync("solitaire-free-replay-0001", "user-1", 3, default, original.RunId);
        var retry = await service.StartAsync("solitaire-free-replay-0001", "user-1", 3, default, original.RunId);

        Assert.NotEqual(original.RunId, replay.RunId);
        Assert.Equal(original.Seed, replay.Seed);
        Assert.Equal(original.DrawCount, replay.DrawCount);
        Assert.False(replay.WasAlreadyStarted);
        Assert.Equal(replay, retry with { WasAlreadyStarted = false });

        var replayDocument = await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .Document(replay.RunId["solitaire_free_".Length..]).GetSnapshotAsync();
        Assert.Equal(original.RunId, Field<string>(replayDocument, "replayRunId"));
        Assert.Equal("started", Field<string>(replayDocument, "status"));

        var completed = await service.CompleteAsync(replay.RunId, "user-1", [], default);
        Assert.Equal(0, completed.Moves);
        Assert.False(completed.WasAlreadyCompleted);

        var nextGame = await service.StartAsync("solitaire-free-next-0001", "user-1", 3, default);
        Assert.Equal(99u, nextGame.Seed);
        Assert.NotEqual(replay.Seed, nextGame.Seed);
        await AssertFinancialAndCompetitiveCollectionsRemainEmpty(database);
    }

    [Fact]
    public async Task ReplayStartRejectsUnfinishedForeignMissingOrMismatchedDeal()
    {
        var (database, service, _) = CreateService(42, 99);
        var source = await service.StartAsync("solitaire-free-source-0002", "user-1", 1, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-replay-0002", "user-1", 1, default, source.RunId));
        await service.CompleteAsync(source.RunId, "user-1", [], default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-replay-0002", "user-2", 1, default, source.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-replay-0002", "user-1", 3, default, source.RunId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-replay-0002", "user-1", 1, default,
            "solitaire_free_" + new string('0', 64)));

        var replay = await service.StartAsync("solitaire-free-replay-0002", "user-1", 1, default, source.RunId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            "solitaire-free-replay-0002", "user-1", 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(
            "solitaire-free-invalid-0002", "user-1", 1, default, " "));

        Assert.Equal(2, (await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .GetSnapshotAsync()).Count);
        Assert.Equal(source.Seed, replay.Seed);
        await AssertFinancialAndCompetitiveCollectionsRemainEmpty(database);
    }

    [Fact]
    public async Task CompletionReplaysAuthoritativelyPersistsElapsedAndIsExactlyIdempotent()
    {
        var (database, service, clock) = CreateService(42);
        var started = await service.StartAsync("solitaire-free-start-0002", "user-1", 1, default);
        clock.UtcNow = Start.AddSeconds(42);
        var replay = new[] { new SolitaireFreeReplayCommand("draw", null, null, null, null) };

        var first = await service.CompleteAsync(started.RunId, "user-1", replay, default);
        clock.UtcNow = Start.AddMinutes(2);
        var retry = await service.CompleteAsync(started.RunId, "user-1", replay, default);

        Assert.False(first.WasAlreadyCompleted);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(first, retry with { WasAlreadyCompleted = false });
        Assert.Equal(0, first.Score);
        Assert.Equal(1, first.Moves);
        Assert.Equal(42_000, first.ElapsedMilliseconds);
        Assert.Equal("submitted", first.Terminal);

        var document = Assert.Single((await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal("completed", Field<string>(document, "status"));
        Assert.Equal(0L, Field<long>(document, "score"));
        Assert.Equal(1L, Field<long>(document, "moves"));
        Assert.Equal(42_000L, Field<long>(document, "elapsedMilliseconds"));
        Assert.Equal("submitted", Field<string>(document, "terminal"));
        Assert.Matches("^[0-9a-f]{64}$", Field<string>(document, "replayDigest"));
        Assert.True(document.ContainsField("replayCanonical"));
        Assert.True(document.ContainsField("completedAt"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.RunId, "user-2", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.RunId, "user-1", [], default));
        await AssertFinancialAndCompetitiveCollectionsRemainEmpty(database);
    }

    [Fact]
    public async Task PauseAndResumeExcludeOnlyServerRecordedPauseTimeAndAllowCompletionWhilePaused()
    {
        var (database, service, clock) = CreateService(42);
        var started = await service.StartAsync("solitaire-free-pauses-0001", "user-1", 1, default);

        clock.UtcNow = Start.AddSeconds(2);
        var paused = await service.PauseAsync(started.RunId, "user-1", default);
        Assert.True(paused.IsPaused);
        Assert.Equal(2_000, paused.ElapsedMilliseconds);

        clock.UtcNow = Start.AddSeconds(10);
        Assert.Equal(2_000, (await service.PauseAsync(started.RunId, "user-1", default)).ElapsedMilliseconds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(started.RunId, "user-2", default));

        clock.UtcNow = Start.AddSeconds(12);
        var resumed = await service.ResumeAsync(started.RunId, "user-1", default);
        Assert.False(resumed.IsPaused);
        Assert.Equal(2_000, resumed.ElapsedMilliseconds);

        clock.UtcNow = Start.AddSeconds(20);
        Assert.Equal(10_000, (await service.ResumeAsync(started.RunId, "user-1", default)).ElapsedMilliseconds);

        clock.UtcNow = Start.AddSeconds(22);
        Assert.Equal(12_000, (await service.PauseAsync(started.RunId, "user-1", default)).ElapsedMilliseconds);

        clock.UtcNow = Start.AddSeconds(40);
        var completed = await service.CompleteAsync(started.RunId, "user-1", [], default);
        Assert.Equal(12_000, completed.ElapsedMilliseconds);
        Assert.Equal("submitted", completed.Terminal);
        Assert.Equal(0, completed.Score); // A manual non-win never receives a speed bonus.

        var document = Assert.Single((await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal(28_000L, Field<long>(document, "pausedMilliseconds"));
        Assert.False(Field<bool>(document, "isPaused"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PauseAsync(started.RunId, "user-1", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(started.RunId, "user-1", default));
    }

    [Fact]
    public async Task CompletionRejectsIllegalOrNonBoardCommandsWithoutCompletingRun()
    {
        var (database, service, _) = CreateService(7);
        var started = await service.StartAsync("solitaire-free-start-0003", "user-1", 3, default);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CompleteAsync(
            started.RunId,
            "user-1",
            [new SolitaireFreeReplayCommand("submit", null, null, null, null)],
            default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CompleteAsync(
            started.RunId,
            "user-1",
            [new SolitaireFreeReplayCommand("flip", null, null, null, 0)],
            default));

        var document = Assert.Single((await database.Collection(FirestoreSolitaireFreeRunService.CollectionName)
            .GetSnapshotAsync()).Documents);
        Assert.Equal("started", Field<string>(document, "status"));
    }

    private (FirestoreDb Database, FirestoreSolitaireFreeRunService Service, MutableTimeProvider Clock)
        CreateService(params uint[] seeds)
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-free-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var index = 0;
        var clock = new MutableTimeProvider { UtcNow = Start };
        return (database, new FirestoreSolitaireFreeRunService(
            database,
            clock,
            () => seeds[Math.Min(index++, seeds.Length - 1)]), clock);
    }

    private static async Task AssertFinancialAndCompetitiveCollectionsRemainEmpty(FirestoreDb database)
    {
        foreach (var collection in new[]
        {
            "userBalances", "balanceTransactions", "solitaireMatches", "solitaireQueueTickets",
            "solitaireHistory", "arcadeCompetitions", "arcadeCompetitionAttempts",
        })
            Assert.Empty((await database.Collection(collection).GetSnapshotAsync()).Documents);
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");

    private sealed class MutableTimeProvider : TimeProvider
    {
        public required DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
}
