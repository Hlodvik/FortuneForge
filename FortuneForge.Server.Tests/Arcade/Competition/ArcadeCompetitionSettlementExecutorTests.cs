using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionSettlementExecutorTests
{
    [Fact]
    public async Task SuccessfulMultiCreditExecutionCompletesOnlyAfterEveryInstruction()
    {
        var competition = Identity();
        var events = new List<string>();
        var store = new FakeStore(competition, MultiplayerAttempts(competition), events);
        var creditor = new FakeCreditor(events);
        var executor = Executor(store, creditor, competition.EndsAtUtc);

        var result = await executor.ExecuteAsync(competition, default);

        Assert.False(result.WasAlreadyCompleted);
        Assert.True(result.SettlementState.IsCompleted);
        Assert.Equal(2, result.PayoutPlan.Instructions.Length);
        Assert.Equal(2, creditor.UniqueCreditCount);
        Assert.Equal(1, store.MarkCompletedCount);
        Assert.Equal(3, events.Count);
        Assert.StartsWith("credit:", events[0], StringComparison.Ordinal);
        Assert.StartsWith("credit:", events[1], StringComparison.Ordinal);
        Assert.Equal("complete", events[2]);
    }

    [Fact]
    public async Task AlreadyCompletedPlannedSettlementReturnsWithoutCreditingAgain()
    {
        var competition = Identity();
        var attempts = MultiplayerAttempts(competition);
        var plan = Plan(competition, attempts);
        var events = new List<string>();
        var store = new FakeStore(competition, attempts, events)
        {
            State = new ArcadeCompetitionSettlementState(
                competition,
                plan,
                competition.EndsAtUtc,
                competition.EndsAtUtc,
                competition.EndsAtUtc)
        };
        var creditor = new FakeCreditor(events);

        var result = await Executor(store, creditor, competition.EndsAtUtc)
            .ExecuteAsync(competition, default);

        Assert.True(result.WasAlreadyCompleted);
        Assert.Same(plan, result.PayoutPlan);
        Assert.Empty(events);
        Assert.Equal(0, store.LoadAttemptsCount);
        Assert.Equal(0, store.StorePlanCount);
        Assert.Equal(0, store.MarkCompletedCount);
        Assert.Equal(0, creditor.TotalCallCount);
    }

    [Fact]
    public async Task PartialFailureLeavesPendingAndRetryCompletesRemainingCreditsIdempotently()
    {
        var competition = Identity();
        var attempts = MultiplayerAttempts(competition);
        var expectedPlan = Plan(competition, attempts);
        var events = new List<string>();
        var store = new FakeStore(competition, attempts, events);
        var creditor = new FakeCreditor(events)
        {
            FailOnceIdempotencyKey = expectedPlan.Instructions[1].IdempotencyKey
        };
        var executor = Executor(store, creditor, competition.EndsAtUtc);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(competition, default));

        Assert.True(store.State.HasStoredPlan);
        Assert.False(store.State.IsCompleted);
        Assert.Equal(1, creditor.UniqueCreditCount);
        Assert.Equal(0, store.MarkCompletedCount);

        var retry = await executor.ExecuteAsync(competition, default);

        Assert.True(retry.SettlementState.IsCompleted);
        Assert.Equal(2, creditor.UniqueCreditCount);
        Assert.Equal(1, store.MarkCompletedCount);
        Assert.Equal(2, creditor.CallCounts[expectedPlan.Instructions[0].IdempotencyKey]);
        Assert.Equal(2, creditor.CallCounts[expectedPlan.Instructions[1].IdempotencyKey]);
    }

    [Fact]
    public async Task LegacyCompletedSettlementWithoutPlanIsRejectedBeforeCredits()
    {
        var competition = Identity();
        var events = new List<string>();
        var store = new FakeStore(competition, [], events)
        {
            State = new ArcadeCompetitionSettlementState(competition, competition.EndsAtUtc)
        };
        var creditor = new FakeCreditor(events);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Executor(store, creditor, competition.EndsAtUtc).ExecuteAsync(competition, default));

        Assert.Empty(events);
        Assert.Equal(0, store.LoadAttemptsCount);
        Assert.Equal(0, store.StorePlanCount);
        Assert.Equal(0, store.MarkCompletedCount);
        Assert.Equal(0, creditor.TotalCallCount);
    }

    private static ArcadeCompetitionSettlementExecutor Executor(
        FakeStore store,
        FakeCreditor creditor,
        DateTimeOffset nowUtc)
    {
        var timeProvider = new FixedTimeProvider(nowUtc);
        var planningService = new ArcadeCompetitionSettlementPlanningService(store, timeProvider);
        return new ArcadeCompetitionSettlementExecutor(store, planningService, creditor, timeProvider);
    }

    private static ArcadeCompetitionPayoutPlan Plan(
        ArcadeCompetitionIdentity competition,
        IEnumerable<ArcadeCompetitionAttemptRecord> attempts)
    {
        var result = ArcadeCompetitionRules.Evaluate(
            new ArcadeCompetitionWindow(
                competition.WindowKind,
                competition.StartsAtUtc,
                competition.EndsAtUtc),
            attempts.Select(attempt => new ArcadeCompetitionAttempt(attempt.PlayerId, attempt.Score)));
        return ArcadeCompetitionPayoutPlan.Create(competition, result);
    }

    private static List<ArcadeCompetitionAttemptRecord> MultiplayerAttempts(ArcadeCompetitionIdentity competition) =>
    [
        CompletedAttempt(competition, "attempt-1", "alpha", 100, 1),
        CompletedAttempt(competition, "attempt-2", "bravo", 500, 3),
        CompletedAttempt(competition, "attempt-3", "charlie", 400, 5),
        CompletedAttempt(competition, "attempt-4", "delta", 300, 7),
        CompletedAttempt(competition, "attempt-5", "echo", 200, 9),
    ];

    private static ArcadeCompetitionAttemptRecord CompletedAttempt(
        ArcadeCompetitionIdentity competition,
        string attemptId,
        string playerId,
        long score,
        int enteredMinute)
    {
        var enteredAt = competition.StartsAtUtc.AddMinutes(enteredMinute);
        var started = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            attemptId,
            competition,
            playerId,
            100,
            enteredAt));
        return started.Complete(new ArcadeCompetitionAttemptCompletion(
            attemptId,
            competition,
            playerId,
            score,
            enteredAt.AddMinutes(1)));
    }

    private static ArcadeCompetitionIdentity Identity() => new(
        "asteroids",
        ArcadeCompetitionWindowKind.Daily,
        new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 4, 22, 0, 0, TimeSpan.Zero));

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class FakeCreditor(List<string> events) : IArcadeCompetitionPayoutCreditor
    {
        private readonly Dictionary<string, ArcadeCompetitionPayoutCreditResult> credits = new(StringComparer.Ordinal);
        private readonly HashSet<string> failedOnce = new(StringComparer.Ordinal);

        public string? FailOnceIdempotencyKey { get; init; }
        public Dictionary<string, int> CallCounts { get; } = new(StringComparer.Ordinal);
        public int UniqueCreditCount => credits.Count;
        public int TotalCallCount => CallCounts.Values.Sum();

        public Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(
            ArcadeCompetitionIdentity competition,
            ArcadeCompetitionCreditInstruction instruction,
            DateTimeOffset creditedAtUtc,
            CancellationToken cancellationToken)
        {
            events.Add($"credit:{instruction.IdempotencyKey}");
            CallCounts[instruction.IdempotencyKey] = CallCounts.GetValueOrDefault(instruction.IdempotencyKey) + 1;
            if (instruction.IdempotencyKey == FailOnceIdempotencyKey && failedOnce.Add(instruction.IdempotencyKey))
                throw new InvalidOperationException("Injected payout failure.");

            if (credits.TryGetValue(instruction.IdempotencyKey, out var existing))
                return Task.FromResult(existing with { WasAlreadyCredited = true });

            var created = new ArcadeCompetitionPayoutCreditResult(
                FirestoreArcadeCompetitionPayoutCreditor.TransactionIdFor(instruction.IdempotencyKey),
                instruction.AmountCents,
                creditedAtUtc,
                WasAlreadyCredited: false);
            credits.Add(instruction.IdempotencyKey, created);
            return Task.FromResult(created);
        }
    }

    private sealed class FakeStore(
        ArcadeCompetitionIdentity competition,
        IEnumerable<ArcadeCompetitionAttemptRecord> attempts,
        List<string> events) : IArcadeCompetitionStore
    {
        private readonly IReadOnlyList<ArcadeCompetitionAttemptRecord> attempts = attempts.ToArray();

        public ArcadeCompetitionSettlementState State { get; set; } = new(competition, null);
        public int LoadAttemptsCount { get; private set; }
        public int StorePlanCount { get; private set; }
        public int MarkCompletedCount { get; private set; }

        public Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(
            ArcadeCompetitionIdentity requested,
            CancellationToken cancellationToken)
        {
            Assert.Equal(competition, requested);
            return Task.FromResult(State);
        }

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(
            ArcadeCompetitionIdentity requested,
            CancellationToken cancellationToken)
        {
            Assert.Equal(competition, requested);
            LoadAttemptsCount++;
            return Task.FromResult(attempts);
        }

        public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(
            ArcadeCompetitionPayoutPlan plan,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken)
        {
            StorePlanCount++;
            if (State.PayoutPlan is not null)
            {
                if (State.PayoutPlan.HasSameValueAs(plan)) return Task.FromResult(State);
                throw new InvalidOperationException("Conflicting plan.");
            }

            State = new ArcadeCompetitionSettlementState(
                competition,
                plan,
                createdAtUtc,
                createdAtUtc,
                null);
            return Task.FromResult(State);
        }

        public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(
            ArcadeCompetitionIdentity requested,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            Assert.Equal(competition, requested);
            if (State.IsCompleted) return Task.FromResult(State);
            if (State.PayoutPlan is null) throw new InvalidOperationException("Missing plan.");
            MarkCompletedCount++;
            events.Add("complete");
            State = new ArcadeCompetitionSettlementState(
                competition,
                State.PayoutPlan,
                State.CreatedAtUtc,
                completedAtUtc,
                completedAtUtc);
            return Task.FromResult(State);
        }

        public Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(
            ArcadeCompetitionAttemptStart attempt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(
            ArcadeCompetitionAttemptCompletion completion,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(
            string gameId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(
            DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
