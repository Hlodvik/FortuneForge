using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Arcade;
using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.RoundAudit;
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
        Assert.Equal((long)AsteroidsReplayEvaluator.RulesVersion, Field<long>(document, "rulesVersion"));
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

        AsteroidsFreeRunCompletionResult first;
        using (GameSecurityContext.Push(new GameRequestObservation(
            "asteroids-complete-request-0001", "player-1", "session-1", "203.0.113.5",
            Start.UtcDateTime, "POST", $"/api/arcade-competitions/asteroids/free/runs/{started.Run.RunId}/replay", "asteroids",
            started.Run.RunId, "asteroids-complete-0001", null,
            "{\"totalSteps\":1}", "captured", "remote-address")))
        {
            first = await service.CompleteAsync(started.Run.RunId, "player-1", replay, default);
        }
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
        var audit = Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Equal("asteroids", Field<string>(audit, "gameId"));
        Assert.Equal(started.Run.RunId, Field<string>(audit, "roundId"));
        Assert.Equal("incomplete", Field<string>(audit, "auditStatus"));
        Assert.True(Field<bool>(audit, "securityAuditPending"));
        var evidence = Assert.Single((await database.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        Assert.Contains("replayCanonical", evidence.GetValue<string>("payloadJson"));
        Assert.Equal("asteroids-complete-request-0001", Field<string>(evidence, "requestId"));
        Assert.Contains("203.0.113.5", Field<string>(evidence, "requestJson"));
        var securityEvents = (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents;
        Assert.Equal(["run-completed", "run-started"], securityEvents.Select(item => Field<string>(item, "eventKind")).Order().ToArray());
        Assert.Contains(securityEvents, item => Field<string>(item, "eventKind") == "run-completed" &&
            Field<string>(item, "requestId") == "asteroids-complete-request-0001");
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        Assert.Empty(await ArcadeReplayAudit.InspectAsync(database, summary, Field<string>(evidence, "payloadJson"), default));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId, "player-2", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            started.Run.RunId,
            "player-1",
            new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [new AsteroidsInputCommand(0, AsteroidsInput.Fire)]),
            default));
        await document.Reference.UpdateAsync("score", first.Score + 1);
        Assert.Contains(await ArcadeReplayAudit.InspectAsync(database, summary, Field<string>(evidence, "payloadJson"), default),
            finding => finding.Code == "arcade-run-source-mismatch");
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
