using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionSettlementPlanningServiceTests
{
    [Fact]
    public async Task ExactCutoffIsEligibleForPlanning()
    {
        var competition = Identity();
        var store = new FakeStore(competition, []);
        var service = Service(store, competition.EndsAtUtc);

        var plan = await service.PlanAsync(competition, default);

        Assert.Empty(plan.Instructions);
        Assert.Equal(1, store.ReadCount);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(1, store.StoreCount);
        Assert.Equal(competition.EndsAtUtc, store.State.CreatedAtUtc);
    }

    [Fact]
    public async Task BeforeCutoffIsRejectedWithoutReadingOrWritingSettlementState()
    {
        var competition = Identity();
        var store = new FakeStore(competition, []);
        var service = Service(store, competition.EndsAtUtc.AddTicks(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PlanAsync(competition, default));

        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.StoreCount);
    }

    [Fact]
    public async Task SolePlayerAttemptsMaterializeACompleteRefundPlan()
    {
        var competition = Identity();
        var store = new FakeStore(competition, [
            CompletedAttempt(competition, "attempt-1", "solo", 10, 1),
            CompletedAttempt(competition, "attempt-2", "solo", 30, 3),
            CompletedAttempt(competition, "attempt-3", "solo", 20, 5),
        ]);

        var plan = await Service(store, competition.EndsAtUtc).PlanAsync(competition, default);

        var refund = Assert.Single(plan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Refund, refund.Kind);
        Assert.Equal("solo", refund.PlayerId);
        Assert.Equal(300, refund.AmountCents);
        Assert.Equal(300, plan.VisibleJackpotCents);
        Assert.Equal(0, plan.HouseCutCents);
    }

    [Fact]
    public async Task MultiplayerAttemptsMaterializeTheRulesPrizePlan()
    {
        var competition = Identity();
        var store = new FakeStore(competition, [
            CompletedAttempt(competition, "attempt-1", "alpha", 100, 1),
            CompletedAttempt(competition, "attempt-2", "bravo", 500, 3),
            CompletedAttempt(competition, "attempt-3", "charlie", 400, 5),
            CompletedAttempt(competition, "attempt-4", "delta", 300, 7),
            CompletedAttempt(competition, "attempt-5", "echo", 200, 9),
        ]);

        var plan = await Service(store, competition.EndsAtUtc).PlanAsync(competition, default);

        Assert.Equal(2, plan.Instructions.Length);
        Assert.Equal(new[] { "bravo", "charlie" }, plan.Instructions.Select(instruction => instruction.PlayerId));
        Assert.All(plan.Instructions, instruction => Assert.Equal(ArcadeCompetitionPayoutKind.Prize, instruction.Kind));
        Assert.Equal(plan.VisibleJackpotCents, plan.TotalCreditsCents);
        Assert.Equal(500, plan.VisibleJackpotCents + plan.HouseCutCents);
    }

    [Fact]
    public async Task RetryReturnsTheAlreadyStoredPlanWithoutReevaluatingAttempts()
    {
        var competition = Identity();
        var store = new FakeStore(competition, [
            CompletedAttempt(competition, "attempt-1", "alpha", 200, 1),
            CompletedAttempt(competition, "attempt-2", "bravo", 100, 3),
        ]);
        var service = Service(store, competition.EndsAtUtc);

        var first = await service.PlanAsync(competition, default);
        store.Attempts.Add(CompletedAttempt(competition, "late-attempt", "charlie", 1_000, 5));
        var retry = await service.PlanAsync(competition, default);

        Assert.Same(first, retry);
        Assert.Equal(2, store.ReadCount);
        Assert.Equal(1, store.LoadCount);
        Assert.Equal(1, store.StoreCount);
    }

    [Fact]
    public async Task LegacyCompletedSettlementWithoutAPlanCannotBePlanned()
    {
        var competition = Identity();
        var store = new FakeStore(competition, [])
        {
            State = new ArcadeCompetitionSettlementState(competition, competition.EndsAtUtc)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, competition.EndsAtUtc).PlanAsync(competition, default));

        Assert.Equal(1, store.ReadCount);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.StoreCount);
    }

    private static ArcadeCompetitionSettlementPlanningService Service(FakeStore store, DateTimeOffset nowUtc) =>
        new(store, new FixedTimeProvider(nowUtc));

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

    private sealed class FakeStore : IArcadeCompetitionStore
    {
        private readonly ArcadeCompetitionIdentity competition;

        public FakeStore(
            ArcadeCompetitionIdentity competition,
            IEnumerable<ArcadeCompetitionAttemptRecord> attempts)
        {
            this.competition = competition;
            Attempts = attempts.ToList();
            State = new ArcadeCompetitionSettlementState(competition, null);
        }

        public List<ArcadeCompetitionAttemptRecord> Attempts { get; }
        public ArcadeCompetitionSettlementState State { get; set; }
        public int ReadCount { get; private set; }
        public int LoadCount { get; private set; }
        public int StoreCount { get; private set; }

        public Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(
            ArcadeCompetitionIdentity requested,
            CancellationToken cancellationToken)
        {
            Assert.Equal(competition, requested);
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(
            ArcadeCompetitionIdentity requested,
            CancellationToken cancellationToken)
        {
            Assert.Equal(competition, requested);
            LoadCount++;
            return Task.FromResult<IReadOnlyList<ArcadeCompetitionAttemptRecord>>(Attempts.ToArray());
        }

        public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(
            ArcadeCompetitionPayoutPlan plan,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken)
        {
            StoreCount++;
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

        public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(
            ArcadeCompetitionIdentity requested,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
