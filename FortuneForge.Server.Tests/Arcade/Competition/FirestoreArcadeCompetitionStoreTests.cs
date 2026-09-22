using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreArcadeCompetitionStoreTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreArcadeCompetitionStoreTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task StartReplayLoadsInDeterministicOrderAndRejectsConflictingIdempotencyReuse()
    {
        var store = CreateStore();
        var competition = Identity();
        var laterAttempt = StartAttempt("attempt-b", "player-b", Start.AddMinutes(2));
        var firstAttempt = StartAttempt("attempt-a", "player-a", Start.AddMinutes(1));

        var created = await store.StartAttemptAsync(laterAttempt, default);
        var replay = await store.StartAttemptAsync(laterAttempt, default);
        _ = await store.StartAttemptAsync(firstAttempt, default);
        var loaded = await store.LoadAttemptsAsync(competition, default);

        Assert.False(created.WasAlreadyRecorded);
        Assert.True(replay.WasAlreadyRecorded);
        Assert.Equal(new[] { "attempt-a", "attempt-b" }, loaded.Select(attempt => attempt.AttemptId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StartAttemptAsync(
            StartAttempt("attempt-b", "player-b", Start.AddMinutes(3)), default));
    }

    [Fact]
    public async Task CompletionIsIdempotentButRejectsConflictsAndTheCompetitionCutoff()
    {
        var store = CreateStore();
        var started = StartAttempt("attempt-1", "player-1", Start.AddMinutes(1));
        _ = await store.StartAttemptAsync(started, default);
        var completion = CompleteAttempt("attempt-1", "player-1", 42, Start.AddMinutes(2));

        var first = await store.CompleteAttemptAsync(completion, default);
        var replay = await store.CompleteAttemptAsync(completion, default);

        Assert.False(first.WasAlreadyCompleted);
        Assert.True(replay.WasAlreadyCompleted);
        Assert.True(first.Attempt.IsCompleted);
        Assert.Equal(42, first.Attempt.Score);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteAttemptAsync(
            CompleteAttempt("attempt-1", "player-1", 43, Start.AddMinutes(2)), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompleteAttempt("attempt-1", "player-1", 42, Start.AddDays(1)));
    }

    [Fact]
    public async Task SettlementPlanRoundTripsWithIdentityAmountsInstructionsStatusAndTimestamps()
    {
        var store = CreateStore();
        var plan = Plan(
            Attempt("alpha", 500),
            Attempt("bravo", 400),
            Attempt("charlie", 300),
            Attempt("delta", 200),
            Attempt("echo", 100));
        var createdAt = Start.AddDays(1);

        var stored = await store.StoreSettlementPlanAsync(plan, createdAt, default);
        var loaded = await store.ReadSettlementStateAsync(plan.Competition, default);

        Assert.False(stored.IsCompleted);
        Assert.True(loaded.HasStoredPlan);
        Assert.Equal(createdAt, loaded.CreatedAtUtc);
        Assert.Equal(createdAt, loaded.UpdatedAtUtc);
        Assert.Null(loaded.CompletedAtUtc);
        Assert.Equal(plan.Competition, loaded.Competition);
        Assert.Equal(plan.VisibleJackpotCents, loaded.PayoutPlan!.VisibleJackpotCents);
        Assert.Equal(plan.HouseCutCents, loaded.PayoutPlan.HouseCutCents);
        Assert.True(plan.Instructions.SequenceEqual(loaded.PayoutPlan.Instructions));
    }

    [Fact]
    public async Task ExactSettlementPlanRetryReturnsTheExistingImmutablePlan()
    {
        var store = CreateStore();
        var plan = Plan(Attempt("alpha", 200), Attempt("bravo", 100));
        var firstCreatedAt = Start.AddDays(1);

        var first = await store.StoreSettlementPlanAsync(plan, firstCreatedAt, default);
        var replay = await store.StoreSettlementPlanAsync(plan, firstCreatedAt.AddHours(1), default);

        Assert.Equal(first.CreatedAtUtc, replay.CreatedAtUtc);
        Assert.Equal(first.UpdatedAtUtc, replay.UpdatedAtUtc);
        Assert.True(first.PayoutPlan!.HasSameValueAs(replay.PayoutPlan!));
    }

    [Fact]
    public async Task ConflictingSettlementPlanForTheSameCompetitionIsRejectedWithoutOverwrite()
    {
        var store = CreateStore();
        var firstPlan = Plan(Attempt("alpha", 200), Attempt("bravo", 100));
        var conflictingPlan = Plan(Attempt("alpha", 100), Attempt("bravo", 200));
        _ = await store.StoreSettlementPlanAsync(firstPlan, Start.AddDays(1), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StoreSettlementPlanAsync(conflictingPlan, Start.AddDays(1), default));
        var loaded = await store.ReadSettlementStateAsync(firstPlan.Competition, default);

        Assert.True(firstPlan.HasSameValueAs(loaded.PayoutPlan!));
    }

    [Fact]
    public async Task SettlementCompletionRequiresAStoredPlanAndRemainsIdempotent()
    {
        var store = CreateStore();
        var competition = Identity();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.MarkSettlementCompletedAsync(competition, Start.AddDays(1), default));

        var plan = Plan(Attempt("alpha", 200), Attempt("bravo", 100));
        _ = await store.StartAttemptAsync(StartAttempt("settlement-attempt", "alpha", Start.AddMinutes(1)), default);
        _ = await store.StoreSettlementPlanAsync(plan, Start.AddDays(1), default);
        var completed = await store.MarkSettlementCompletedAsync(competition, Start.AddDays(1).AddHours(1), default);
        var replay = await store.MarkSettlementCompletedAsync(competition, Start.AddDays(1).AddHours(2), default);

        Assert.True(completed.IsCompleted);
        Assert.True(completed.HasStoredPlan);
        Assert.Equal(completed.CompletedAtUtc, completed.UpdatedAtUtc);
        Assert.Equal(completed.CompletedAtUtc, replay.CompletedAtUtc);
    }

    [Fact]
    public async Task LegacyPendingAndCompletedSettlementDocumentsRemainReadable()
    {
        var database = CreateDatabase();
        var store = new FirestoreArcadeCompetitionStore(database);
        var competition = Identity();
        var reference = database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(competition));

        await reference.SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(
            new ArcadeCompetitionSettlementState(competition, null)));
        var pending = await store.ReadSettlementStateAsync(competition, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.MarkSettlementCompletedAsync(competition, Start.AddDays(1), default));
        var upgraded = await store.StoreSettlementPlanAsync(
            Plan(Attempt("alpha", 2), Attempt("bravo", 1)),
            Start.AddDays(1),
            default);

        var legacyCompletedAt = Start.AddDays(1);
        await reference.SetAsync(ArcadeCompetitionFirestoreDocuments.SettlementData(
            new ArcadeCompetitionSettlementState(competition, legacyCompletedAt)));
        var completed = await store.ReadSettlementStateAsync(competition, default);
        var replay = await store.MarkSettlementCompletedAsync(competition, legacyCompletedAt.AddHours(1), default);

        Assert.False(pending.HasStoredPlan);
        Assert.False(pending.IsCompleted);
        Assert.True(upgraded.HasStoredPlan);
        Assert.False(upgraded.IsCompleted);
        Assert.False(completed.HasStoredPlan);
        Assert.True(completed.IsCompleted);
        Assert.Equal(legacyCompletedAt, completed.CompletedAtUtc);
        Assert.Equal(legacyCompletedAt, replay.CompletedAtUtc);
    }

    [Fact]
    public async Task LoadAttemptsForGameReadsAcrossWindowsInDeterministicOrder()
    {
        var store = CreateStore();
        var firstWindow = Identity();
        var secondWindow = new ArcadeCompetitionIdentity("asteroids", ArcadeCompetitionWindowKind.Daily, Start.AddDays(1), Start.AddDays(2));
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart("second", secondWindow, "player-2", 100, secondWindow.StartsAtUtc.AddMinutes(1)), default);
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart("first", firstWindow, "player-1", 100, firstWindow.StartsAtUtc.AddMinutes(1)), default);

        var attempts = await store.LoadAttemptsForGameAsync("asteroids", default);

        Assert.Equal(new[] { "first", "second" }, attempts.Select(attempt => attempt.AttemptId));
        Assert.False(attempts[1].IsCompleted);
    }

    [Fact]
    public async Task DueSettlementQueryIncludesEnteredUnplannedAndPlannedWindowsInDeterministicOrder()
    {
        var database = CreateDatabase();
        var store = new FirestoreArcadeCompetitionStore(database);
        var weekly = new ArcadeCompetitionIdentity(
            "weekly-game",
            ArcadeCompetitionWindowKind.Weekly,
            Start.AddDays(-7),
            Start);
        var planned = new ArcadeCompetitionIdentity(
            "planned-game",
            ArcadeCompetitionWindowKind.Daily,
            Start,
            Start.AddDays(1));
        var cutoffEqual = new ArcadeCompetitionIdentity(
            "cutoff-game",
            ArcadeCompetitionWindowKind.Daily,
            Start.AddDays(1),
            Start.AddDays(2));
        var completed = new ArcadeCompetitionIdentity(
            "completed-game",
            ArcadeCompetitionWindowKind.Daily,
            Start,
            Start.AddDays(1));
        var future = new ArcadeCompetitionIdentity(
            "future-game",
            ArcadeCompetitionWindowKind.Weekly,
            Start.AddDays(2),
            Start.AddDays(9));
        var noEntry = new ArcadeCompetitionIdentity(
            "no-entry-game",
            ArcadeCompetitionWindowKind.Daily,
            Start,
            Start.AddDays(1));
        var legacy = new ArcadeCompetitionIdentity(
            "legacy-game",
            ArcadeCompetitionWindowKind.Daily,
            Start,
            Start.AddDays(1));

        await PersistEntryAsync(store, weekly, "weekly-player");
        await PersistEntryAsync(store, planned, "planned-player");
        await PersistEntryAsync(store, cutoffEqual, "cutoff-player");
        await PersistEntryAsync(store, completed, "completed-player");
        await PersistEntryAsync(store, future, "future-player");
        await database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .Document(noEntry.DocumentId)
            .SetAsync(ArcadeCompetitionFirestoreDocuments.CompetitionData(noEntry));
        await database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .Document(legacy.DocumentId)
            .SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = legacy.GameId,
                ["windowKind"] = "daily",
                ["startsAt"] = Timestamp.FromDateTime(legacy.StartsAtUtc.UtcDateTime),
                ["endsAt"] = Timestamp.FromDateTime(legacy.EndsAtUtc.UtcDateTime),
                ["schemaVersion"] = 1L,
            });
        await PersistEntryAsync(store, legacy, "legacy-player");
        _ = await store.StoreSettlementPlanAsync(
            Plan(planned, new ArcadeCompetitionAttempt("planned-player", 1)),
            planned.EndsAtUtc,
            default);
        _ = await store.StoreSettlementPlanAsync(
            Plan(completed, new ArcadeCompetitionAttempt("completed-player", 1)),
            completed.EndsAtUtc,
            default);
        _ = await store.MarkSettlementCompletedAsync(completed, completed.EndsAtUtc, default);
        var completedDocument = await CompetitionDocument(database, completed).GetSnapshotAsync();
        Assert.Equal("completed", Field<string>(completedDocument, "settlementStatus"));
        await completedDocument.Reference.UpdateAsync("gameId", "invalid-completed-record");
        var plannedDocument = await CompetitionDocument(database, planned).GetSnapshotAsync();
        Assert.Equal("pending", Field<string>(plannedDocument, "settlementStatus"));
        Assert.Equal(2, Field<long>(plannedDocument, "schemaVersion"));

        var due = await store.LoadDueSettlementCompetitionsAsync(cutoffEqual.EndsAtUtc, default);

        Assert.Equal(
            new[] { weekly, planned, cutoffEqual },
            due);
        Assert.Contains(due, identity => identity.WindowKind == ArcadeCompetitionWindowKind.Daily);
        Assert.Contains(due, identity => identity.WindowKind == ArcadeCompetitionWindowKind.Weekly);
        Assert.DoesNotContain(completed, due);
        Assert.DoesNotContain(future, due);
        Assert.DoesNotContain(noEntry, due);
        Assert.DoesNotContain(legacy, due);
    }

    [Fact]
    public async Task DueSettlementQueryRejectsInvalidCompetitionDocumentShape()
    {
        var database = CreateDatabase();
        var store = new FirestoreArcadeCompetitionStore(database);
        await database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .Document("invalid-competition-shape")
            .SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = "asteroids",
                ["windowKind"] = "daily",
                ["endsAt"] = Timestamp.FromDateTime(Start.UtcDateTime),
                ["settlementStatus"] = "pending",
                ["schemaVersion"] = 2L,
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LoadDueSettlementCompetitionsAsync(Start, default));
    }

    private FirestoreArcadeCompetitionStore CreateStore()
    {
        return new FirestoreArcadeCompetitionStore(CreateDatabase());
    }

    private FirestoreDb CreateDatabase()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-competition-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static async Task PersistEntryAsync(
        FirestoreArcadeCompetitionStore store,
        ArcadeCompetitionIdentity competition,
        string playerId) =>
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart(
            $"attempt-{playerId}",
            competition,
            playerId,
            100,
            competition.StartsAtUtc.AddMinutes(1)), default);

    private static DocumentReference CompetitionDocument(
        FirestoreDb database,
        ArcadeCompetitionIdentity competition) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .Document(competition.DocumentId);

    private static readonly DateTimeOffset Start = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
    private static ArcadeCompetitionIdentity Identity() => new("asteroids", ArcadeCompetitionWindowKind.Daily, Start, Start.AddDays(1));
    private static ArcadeCompetitionAttemptStart StartAttempt(string attemptId, string playerId, DateTimeOffset enteredAtUtc) =>
        new(attemptId, Identity(), playerId, 100, enteredAtUtc);
    private static ArcadeCompetitionAttemptCompletion CompleteAttempt(string attemptId, string playerId, long score, DateTimeOffset completedAtUtc) =>
        new(attemptId, Identity(), playerId, score, completedAtUtc);
    private static ArcadeCompetitionAttempt Attempt(string playerId, long score) => new(playerId, score);
    private static ArcadeCompetitionPayoutPlan Plan(params ArcadeCompetitionAttempt[] attempts)
        => Plan(Identity(), attempts);

    private static ArcadeCompetitionPayoutPlan Plan(
        ArcadeCompetitionIdentity competition,
        params ArcadeCompetitionAttempt[] attempts)
    {
        var window = new ArcadeCompetitionWindow(
            competition.WindowKind,
            competition.StartsAtUtc,
            competition.EndsAtUtc);
        return ArcadeCompetitionPayoutPlan.Create(
            competition,
            ArcadeCompetitionRules.Evaluate(window, attempts));
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");
}
