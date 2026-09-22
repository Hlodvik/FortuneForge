using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreArcadeCompetitionPaidEntryCoordinatorTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreArcadeCompetitionPaidEntryCoordinatorTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task PaidEntryDebitsOnceAndCreatesTheCompetitionAndStartedAttemptAtomically()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 350);

        var result = await coordinator.StartPaidAttemptAsync(Request("attempt-1", "player-1"), default);

        Assert.False(result.WasAlreadyRecorded);
        Assert.False(result.Attempt.IsCompleted);
        Assert.Equal(100, result.Attempt.EntryFeeCents);
        Assert.Equal(250, await ReadBalanceCentsAsync(database, "player-1"));
        var balance = await database.Collection("userBalances").Document("player-1_slotsCredits").GetSnapshotAsync();
        Assert.Equal("player-1", Field<string>(balance, "userId"));
        Assert.Equal("slotsCredits", Field<string>(balance, "currencyId"));
        Assert.Equal(0L, Field<long>(balance, "reserved"));
        Assert.Equal(2L, Field<long>(balance, "version"));
        var competition = Assert.Single((await database.Collection("arcadeCompetitions").GetSnapshotAsync()).Documents);
        Assert.Equal("pending", Field<string>(competition, "settlementStatus"));
        Assert.Equal(2, Field<long>(competition, "schemaVersion"));
        Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        var ledger = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal("slotsCredits", Field<string>(ledger, "currencyId"));
        Assert.Equal(-1d, Field<double>(ledger, "amount"));
        Assert.Equal(2.5d, Field<double>(ledger, "balanceAfter"));
        Assert.Equal("arcade-competition-entry", Field<string>(ledger, "type"));
    }

    [Fact]
    public async Task ExactRetryReturnsTheOriginalStartedAttemptWithoutAnotherDebit()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var request = Request("attempt-1", "player-1");

        var first = await coordinator.StartPaidAttemptAsync(request, default);
        var replay = await coordinator.StartPaidAttemptAsync(request, default);

        Assert.False(first.WasAlreadyRecorded);
        Assert.True(replay.WasAlreadyRecorded);
        Assert.Equal(first.Attempt, replay.Attempt);
        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task ConflictingAttemptRetryFailsWithoutAnotherDebit()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        _ = await coordinator.StartPaidAttemptAsync(Request("attempt-1", "player-1"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartPaidAttemptAsync(
            Request("attempt-1", "player-2"), default));

        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task InsufficientBalanceLeavesBalanceAndCompetitionDocumentsUntouched()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 99);

        var exception = await Assert.ThrowsAsync<ArcadeCompetitionInsufficientCreditsException>(() =>
            coordinator.StartPaidAttemptAsync(Request("attempt-1", "player-1"), default));

        Assert.Equal(99, exception.AvailableCents);
        Assert.Equal(100, exception.RequiredCents);
        Assert.Equal(99, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Empty((await database.Collection("arcadeCompetitions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CompletedSettlementRejectsPaidEntryWithoutChangingTheBalanceOrCreatingEntryDocuments()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var competition = Identity();
        await database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(competition))
            .SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(
                new ArcadeCompetitionSettlementState(competition, Start.AddDays(1))));

        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() =>
            coordinator.StartPaidAttemptAsync(Request("attempt-1", "player-1"), default));

        Assert.Equal(200, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Empty((await database.Collection("arcadeCompetitions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AsteroidsPaidEntryAtomicallyDebitsAndCreatesTheBoundRun()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var request = Request("attempt-asteroids", "player-1");

        var result = await coordinator.StartAsteroidsPaidAttemptAsync(request, Run(request, 42), default);

        Assert.False(result.WasAlreadyRecorded);
        Assert.Equal(42UL, result.Run.Run.Seed);
        Assert.Equal(result.Attempt.DocumentId, result.Run.Attempt.DocumentId);
        var run = Assert.Single((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        Assert.Equal("000000000000002a", Field<string>(run, "seedHex"));
        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AsteroidsExactRetryReturnsTheCommittedSeedWithoutAnotherDebit()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var request = Request("attempt-asteroids", "player-1");

        var first = await coordinator.StartAsteroidsPaidAttemptAsync(request, Run(request, 42), default);
        var replay = await coordinator.StartAsteroidsPaidAttemptAsync(request, Run(request, 99), default);

        Assert.False(first.WasAlreadyRecorded);
        Assert.True(replay.WasAlreadyRecorded);
        Assert.Equal(42UL, replay.Run.Run.Seed);
        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Single((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AsteroidsInsufficientOrClosedEntryCreatesNoRun()
    {
        var (insufficientDatabase, insufficientCoordinator) = CreateCoordinator();
        await SeedBalanceAsync(insufficientDatabase, "player-1", 99);
        var insufficientRequest = Request("attempt-insufficient", "player-1");
        await Assert.ThrowsAsync<ArcadeCompetitionInsufficientCreditsException>(() =>
            insufficientCoordinator.StartAsteroidsPaidAttemptAsync(insufficientRequest, Run(insufficientRequest, 42), default));
        Assert.Empty((await insufficientDatabase.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);

        var (closedDatabase, closedCoordinator) = CreateCoordinator();
        await SeedBalanceAsync(closedDatabase, "player-1", 200);
        var competition = Identity();
        await closedDatabase.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(competition))
            .SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(
                new ArcadeCompetitionSettlementState(competition, Start.AddDays(1))));
        var closedRequest = Request("attempt-closed", "player-1");
        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() =>
            closedCoordinator.StartAsteroidsPaidAttemptAsync(closedRequest, Run(closedRequest, 42), default));
        Assert.Empty((await closedDatabase.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        Assert.Equal(200, await ReadBalanceCentsAsync(closedDatabase, "player-1"));
    }

    [Fact]
    public async Task AsteroidsEntryRejectsAnExistingAttemptWithMissingOrCorruptRunWithoutAnotherDebit()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 300);
        var missingRunRequest = Request("attempt-missing", "player-1");
        _ = await coordinator.StartPaidAttemptAsync(missingRunRequest, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsteroidsPaidAttemptAsync(missingRunRequest, Run(missingRunRequest, 42), default));

        var corruptRequest = Request("attempt-corrupt", "player-1");
        var created = await coordinator.StartAsteroidsPaidAttemptAsync(corruptRequest, Run(corruptRequest, 42), default);
        await database.Collection("asteroidsRuns").Document(created.Attempt.DocumentId).UpdateAsync("seedHex", "2A");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsteroidsPaidAttemptAsync(corruptRequest, Run(corruptRequest, 99), default));

        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task AsteroidsReplayCompletionAtomicallyRecordsAuthoritativeScoreAndIsIdempotent()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var request = Request("attempt-complete", "player-1");
        var started = await coordinator.StartAsteroidsPaidAttemptAsync(request, Run(request, 42), default);
        var completion = Completion(started.Run.Run.RunId, "player-1", TerminalReplay());

        var first = await coordinator.CompleteAsteroidsReplayAsync(completion, default);
        var replay = await coordinator.CompleteAsteroidsReplayAsync(completion, default);

        Assert.Equal(AsteroidsTerminalState.GameOver, first.Terminal);
        Assert.False(first.WasAlreadyCompleted);
        Assert.True(replay.WasAlreadyCompleted);
        var attempt = Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Equal("completed", Field<string>(attempt, "status"));
        Assert.Equal(first.Score, Field<long>(attempt, "score"));
    }

    [Fact]
    public async Task AsteroidsReplayCompletionRejectsConflictWrongPlayerAndNonterminalWithoutWrites()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var request = Request("attempt-reject", "player-1");
        var started = await coordinator.StartAsteroidsPaidAttemptAsync(request, Run(request, 42), default);
        var runId = started.Run.Run.RunId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteAsteroidsReplayAsync(
            Completion(runId, "other-player", TerminalReplay()), default));
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.CompleteAsteroidsReplayAsync(
            Completion(runId, "player-1", new AsteroidsReplay(1, [])), default));
        var attempt = Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Equal("started", Field<string>(attempt, "status"));
    }

    [Fact]
    public async Task PlannedV2SettlementBlocksNewEntryAndReplayCompletionWhilePendingOrCompleted()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 400);
        var startedRequest = Request("attempt-before-plan", "player-1");
        var started = await coordinator.StartAsteroidsPaidAttemptAsync(
            startedRequest,
            Run(startedRequest, 42),
            default);
        var store = new FirestoreArcadeCompetitionStore(database);
        _ = await store.StoreSettlementPlanAsync(Plan(), Start.AddHours(2), default);

        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() =>
            coordinator.StartPaidAttemptAsync(Request("attempt-after-plan", "player-1"), default));
        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() =>
            coordinator.CompleteAsteroidsReplayAsync(
                Completion(started.Run.Run.RunId, "player-1", TerminalReplay()),
                default));

        Assert.Equal(300, await ReadBalanceCentsAsync(database, "player-1"));
        var pendingAttempt = Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Equal("started", Field<string>(pendingAttempt, "status"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        _ = await store.MarkSettlementCompletedAsync(Identity(), Start.AddHours(4), default);
        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() =>
            coordinator.StartPaidAttemptAsync(Request("attempt-after-completion", "player-1"), default));
        Assert.Equal(300, await ReadBalanceCentsAsync(database, "player-1"));
    }

    [Fact]
    public async Task LegacyPendingSettlementStillAllowsEntryAndReplayCompletion()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 300);
        await database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(Identity()))
            .SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(
                new ArcadeCompetitionSettlementState(Identity(), null)));

        var paidEntry = await coordinator.StartPaidAttemptAsync(
            Request("legacy-pending-entry", "player-1"),
            default);
        var asteroidRequest = Request("legacy-pending-run", "player-1");
        var asteroid = await coordinator.StartAsteroidsPaidAttemptAsync(
            asteroidRequest,
            Run(asteroidRequest, 42),
            default);
        var completion = await coordinator.CompleteAsteroidsReplayAsync(
            Completion(asteroid.Run.Run.RunId, "player-1", TerminalReplay()),
            default);

        Assert.False(paidEntry.WasAlreadyRecorded);
        Assert.False(asteroid.WasAlreadyRecorded);
        Assert.False(completion.WasAlreadyCompleted);
        Assert.Equal(100, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Contains(
            (await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents,
            attempt => Field<string>(attempt, "status") == "completed");
    }

    [Fact]
    public async Task MalformedV2SettlementIdentityIsRejectedRatherThanAccepted()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 200);
        var store = new FirestoreArcadeCompetitionStore(database);
        _ = await store.StoreSettlementPlanAsync(Plan(), Start.AddHours(2), default);
        await database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(Identity()))
            .UpdateAsync("gameId", "tampered-game");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartPaidAttemptAsync(Request("malformed-settlement", "player-1"), default));

        Assert.Equal(200, await ReadBalanceCentsAsync(database, "player-1"));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AsteroidsReplayCompletionRejectsClosedSettlementAndCutoffWithoutCompletingAttempt()
    {
        var (database, coordinator) = CreateCoordinator();
        await SeedBalanceAsync(database, "player-1", 300);
        var firstRequest = Request("attempt-settled", "player-1");
        var first = await coordinator.StartAsteroidsPaidAttemptAsync(firstRequest, Run(firstRequest, 42), default);
        var competition = Identity();
        await database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(competition))
            .SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(new ArcadeCompetitionSettlementState(competition, Start.AddHours(4))));
        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() => coordinator.CompleteAsteroidsReplayAsync(
            Completion(first.Run.Run.RunId, "player-1", TerminalReplay()), default));

        var (cutoffDatabase, cutoffCoordinator) = CreateCoordinator();
        await SeedBalanceAsync(cutoffDatabase, "player-1", 200);
        var cutoffRequest = Request("attempt-cutoff", "player-1");
        var cutoff = await cutoffCoordinator.StartAsteroidsPaidAttemptAsync(cutoffRequest, Run(cutoffRequest, 42), default);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cutoffCoordinator.CompleteAsteroidsReplayAsync(new AsteroidsReplayCompletionRequest(
            cutoff.Run.Run.RunId, Identity(), "player-1", TerminalReplay(), Identity().EndsAtUtc), default));
        var attempt = Assert.Single((await cutoffDatabase.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Equal("started", Field<string>(attempt, "status"));
    }

    [Fact]
    public async Task CompletedReplayRetryRejectsDivergentAttemptOrCorruptRun()
    {
        var (attemptDatabase, attemptCoordinator) = CreateCoordinator();
        await SeedBalanceAsync(attemptDatabase, "player-1", 200);
        var attemptRequest = Request("attempt-divergent", "player-1");
        var attemptStarted = await attemptCoordinator.StartAsteroidsPaidAttemptAsync(attemptRequest, Run(attemptRequest, 42), default);
        var replay = TerminalReplay();
        var completion = Completion(attemptStarted.Run.Run.RunId, "player-1", replay);
        _ = await attemptCoordinator.CompleteAsteroidsReplayAsync(completion, default);
        await attemptDatabase.Collection("arcadeCompetitionAttempts").Document(attemptStarted.Attempt.DocumentId).UpdateAsync("score", 99L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attemptCoordinator.CompleteAsteroidsReplayAsync(completion, default));

        var (runDatabase, runCoordinator) = CreateCoordinator();
        await SeedBalanceAsync(runDatabase, "player-1", 200);
        var runRequest = Request("attempt-corrupt-complete", "player-1");
        var runStarted = await runCoordinator.StartAsteroidsPaidAttemptAsync(runRequest, Run(runRequest, 42), default);
        var runCompletion = Completion(runStarted.Run.Run.RunId, "player-1", replay);
        _ = await runCoordinator.CompleteAsteroidsReplayAsync(runCompletion, default);
        await runDatabase.Collection("asteroidsRuns").Document(runStarted.Attempt.DocumentId).UpdateAsync("terminal", "active");
        await Assert.ThrowsAsync<InvalidOperationException>(() => runCoordinator.CompleteAsteroidsReplayAsync(runCompletion, default));
    }

    private (FirestoreDb Database, FirestoreArcadeCompetitionPaidEntryCoordinator Coordinator) CreateCoordinator()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-competition-entry-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        return (database, new FirestoreArcadeCompetitionPaidEntryCoordinator(database));
    }

    private static ArcadeCompetitionPaidEntryRequest Request(string attemptId, string playerId) =>
        new(attemptId, Identity(), playerId, 100, Start.AddMinutes(1));

    private static AsteroidsRunIdentity Run(ArcadeCompetitionPaidEntryRequest request, ulong seed)
    {
        var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            request.AttemptId, request.Competition, request.AuthenticatedPlayerId, request.EntryFeeCents, request.EnteredAtUtc));
        return new AsteroidsRunIdentity(FirestoreArcadeCompetitionPaidEntryCoordinator.AsteroidsRunId(attempt), seed);
    }

    private static AsteroidsReplayCompletionRequest Completion(string runId, string playerId, AsteroidsReplay replay) =>
        new(runId, Identity(), playerId, replay, Start.AddMinutes(3));

    private static AsteroidsReplay TerminalReplay() =>
        new(AsteroidsReplayEvaluator.MaximumReplaySteps, []);

    private static ArcadeCompetitionPayoutPlan Plan()
    {
        var competition = Identity();
        var result = ArcadeCompetitionRules.Evaluate(
            new ArcadeCompetitionWindow(
                competition.WindowKind,
                competition.StartsAtUtc,
                competition.EndsAtUtc),
            [
                new ArcadeCompetitionAttempt("player-1", 200),
                new ArcadeCompetitionAttempt("player-2", 100),
            ]);
        return ArcadeCompetitionPayoutPlan.Create(competition, result);
    }

    private static async Task SeedBalanceAsync(FirestoreDb database, string userId, long cents) =>
        await database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["currencyId"] = "slotsCredits",
            ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100,
            ["reserved"] = 0L,
            ["version"] = 1L,
            ["createdAt"] = Timestamp.FromDateTime(Start.UtcDateTime),
            ["updatedAt"] = Timestamp.FromDateTime(Start.UtcDateTime),
        });

    private static async Task<long> ReadBalanceCentsAsync(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return checked(Field<long>(snapshot, "available") * 100 + Field<long>(snapshot, "availableFractionalCents"));
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");

    private static readonly DateTimeOffset Start = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
    private static ArcadeCompetitionIdentity Identity() => new("asteroids", ArcadeCompetitionWindowKind.Daily, Start, Start.AddDays(1));
}
