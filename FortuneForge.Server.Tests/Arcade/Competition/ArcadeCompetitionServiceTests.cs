using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionServiceTests
{
    [Fact]
    public void CurrentIdentityUsesJohannesburgDailyAndWeeklyWindows()
    {
        var service = Service(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        var daily = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Daily);
        var weekly = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Weekly);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero), daily.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero), weekly.StartsAtUtc);
        Assert.Equal(weekly.StartsAtUtc.AddDays(7), weekly.EndsAtUtc);
    }

    [Fact]
    public async Task SnapshotIncludesCompleteAndIncompleteEntriesInRankingAndJackpot()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeStore();
        var service = Service(now, store);
        var competition = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Daily);
        _ = await service.StartAttemptAsync("asteroids", ArcadeCompetitionWindowKind.Daily, "attempt-alpha", "alpha", default);
        _ = await service.StartAttemptAsync("asteroids", ArcadeCompetitionWindowKind.Daily, "attempt-bravo", "bravo", default);
        _ = await service.CompleteAttemptAsync(competition, "attempt-bravo", "bravo", 50, default);

        var snapshot = await service.GetSnapshotAsync(competition, default);

        Assert.True(snapshot.IsAcceptingAttempts);
        Assert.Equal(200, snapshot.TotalEntryFeesCents);
        Assert.Equal(200, snapshot.VisibleJackpotCents);
        var placement = Assert.Single(snapshot.PrizePlacements);
        Assert.Equal("bravo", placement.PlayerId);
        Assert.Equal(50, placement.Score);
        Assert.Equal(200, Assert.Single(snapshot.PrizeAllocations).AmountCents);
    }

    [Fact]
    public async Task StoredSettlementLocksNewStartsAndMakesTheSnapshotReadOnly()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeStore();
        var service = Service(now, store);
        var competition = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Daily);
        _ = await store.MarkSettlementCompletedAsync(competition, now, default);

        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() => service.StartAttemptAsync(
            "asteroids", ArcadeCompetitionWindowKind.Daily, "attempt-1", "player-1", default));
        var snapshot = await service.GetSnapshotAsync(competition, default);
        Assert.False(snapshot.IsAcceptingAttempts);
        Assert.True(snapshot.SettlementState.IsCompleted);
    }

    [Fact]
    public async Task StartReadsTheClockOnceAndUsesTheSameInstantAcrossJohannesburgMidnight()
    {
        var beforeMidnight = new DateTimeOffset(2026, 9, 3, 21, 59, 59, TimeSpan.Zero);
        var atMidnight = new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
        var clock = new SequencedTimeProvider(beforeMidnight, atMidnight);
        var service = new ArcadeCompetitionService(new FakeStore(), clock);

        var started = await service.StartAttemptAsync("asteroids", ArcadeCompetitionWindowKind.Daily, "boundary-attempt", "player-1", default);

        Assert.Equal(1, clock.ReadCount);
        Assert.Equal(beforeMidnight, started.Attempt.EnteredAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 22, 0, 0, TimeSpan.Zero), started.Attempt.Competition.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero), started.Attempt.Competition.EndsAtUtc);
    }

    [Fact]
    public async Task CompletionUsesTheSuppliedAttemptIdentityInsteadOfResolvingTheCurrentWindow()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeStore();
        var service = Service(now, store);
        var prior = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Weekly);
        var enteredAt = prior.StartsAtUtc.AddMinutes(1);
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart("prior-attempt", prior, "player-1", 100, enteredAt), default);

        var result = await service.CompleteAttemptAsync(prior, "prior-attempt", "player-1", 12, default);

        Assert.Equal(prior.DocumentId, result.Attempt.Competition.DocumentId);
        Assert.True(result.Attempt.IsCompleted);
    }

    [Fact]
    public async Task AllTimeUsesBestCompletedScoresExcludesIncompleteEntriesAndOrdersTiesByPlayerId()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new FakeStore();
        var service = Service(now, store);
        var firstWindow = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Daily);
        var secondWindow = new ArcadeCompetitionIdentity("asteroids", ArcadeCompetitionWindowKind.Daily, firstWindow.StartsAtUtc.AddDays(1), firstWindow.EndsAtUtc.AddDays(1));
        await AddCompleted(store, firstWindow, "alpha-old", "alpha", 40, firstWindow.StartsAtUtc.AddMinutes(1));
        await AddCompleted(store, secondWindow, "alpha-best", "alpha", 90, secondWindow.StartsAtUtc.AddMinutes(1));
        await AddCompleted(store, firstWindow, "bravo", "bravo", 90, firstWindow.StartsAtUtc.AddMinutes(2));
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart("charlie-open", firstWindow, "charlie", 100, firstWindow.StartsAtUtc.AddMinutes(3)), default);

        var allTime = await service.GetAllTimeSnapshotAsync("asteroids", 100, default);

        Assert.Equal(new[] { "alpha", "bravo" }, allTime.Leaderboard.Select(place => place.PlayerId));
        Assert.Equal(new long[] { 90, 90 }, allTime.Leaderboard.Select(place => place.Score));
    }

    [Fact]
    public async Task AllTimeCapsTheLeaderboardAtOneHundredPlaces()
    {
        var store = new FakeStore();
        var service = Service(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero), store);
        var competition = service.ResolveCurrentCompetition("asteroids", ArcadeCompetitionWindowKind.Daily);
        for (var index = 1; index <= 101; index++)
            await AddCompleted(store, competition, $"attempt-{index}", $"player-{index:D3}", index, competition.StartsAtUtc.AddMinutes(index));

        var allTime = await service.GetAllTimeSnapshotAsync("asteroids", 100, default);

        Assert.Equal(100, allTime.Leaderboard.Length);
        Assert.Equal("player-101", allTime.Leaderboard[0].PlayerId);
        Assert.Equal("player-002", allTime.Leaderboard[^1].PlayerId);
    }

    private static ArcadeCompetitionService Service(DateTimeOffset now, FakeStore? store = null) =>
        new(store ?? new FakeStore(), new FakeTimeProvider(now));

    private static async Task AddCompleted(FakeStore store, ArcadeCompetitionIdentity competition, string attemptId, string playerId, long score, DateTimeOffset atUtc)
    {
        _ = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart(attemptId, competition, playerId, 100, atUtc), default);
        _ = await store.CompleteAttemptAsync(new ArcadeCompetitionAttemptCompletion(attemptId, competition, playerId, score, atUtc), default);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequencedTimeProvider(DateTimeOffset first, DateTimeOffset second) : TimeProvider
    {
        public int ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => ++ReadCount == 1 ? first : second;
    }

    private sealed class FakeStore : IArcadeCompetitionStore
    {
        private readonly Dictionary<string, ArcadeCompetitionAttemptRecord> attempts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArcadeCompetitionSettlementState> settlements = new(StringComparer.Ordinal);

        public Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(ArcadeCompetitionAttemptStart attempt, CancellationToken cancellationToken)
        {
            var record = ArcadeCompetitionAttemptRecord.Start(attempt);
            if (attempts.TryGetValue(record.DocumentId, out var existing))
            {
                if (existing.PlayerId != record.PlayerId || existing.EntryFeeCents != record.EntryFeeCents || existing.EnteredAtUtc != record.EnteredAtUtc)
                    throw new InvalidOperationException();
                return Task.FromResult(new ArcadeCompetitionStartAttemptResult(existing, true));
            }
            attempts.Add(record.DocumentId, record);
            return Task.FromResult(new ArcadeCompetitionStartAttemptResult(record, false));
        }

        public Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(ArcadeCompetitionAttemptCompletion completion, CancellationToken cancellationToken)
        {
            var documentId = ArcadeCompetitionFirestoreDocuments.AttemptDocumentId(completion.Competition, completion.AttemptId);
            var existing = attempts[documentId];
            if (existing.IsCompleted)
            {
                if (existing.Score != completion.Score || existing.CompletedAtUtc != completion.CompletedAtUtc) throw new InvalidOperationException();
                return Task.FromResult(new ArcadeCompetitionCompleteAttemptResult(existing, true));
            }
            var completed = existing.Complete(completion);
            attempts[documentId] = completed;
            return Task.FromResult(new ArcadeCompetitionCompleteAttemptResult(completed, false));
        }

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionAttemptRecord>>(attempts.Values.Where(attempt => attempt.Competition.DocumentId == competition.DocumentId).ToArray());

        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionAttemptRecord>>(attempts.Values.Where(attempt => attempt.Competition.GameId == gameId).ToArray());

        public Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionIdentity>>([]);

        public Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken) =>
            Task.FromResult(settlements.TryGetValue(competition.DocumentId, out var state) ? state : new ArcadeCompetitionSettlementState(competition, null));

        public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(ArcadeCompetitionPayoutPlan plan, DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
        {
            var state = new ArcadeCompetitionSettlementState(plan.Competition, plan, createdAtUtc, createdAtUtc, null);
            settlements[plan.Competition.DocumentId] = state;
            return Task.FromResult(state);
        }

        public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(ArcadeCompetitionIdentity competition, DateTimeOffset completedAtUtc, CancellationToken cancellationToken)
        {
            if (settlements.TryGetValue(competition.DocumentId, out var existing) && existing.IsCompleted) return Task.FromResult(existing);
            var state = new ArcadeCompetitionSettlementState(competition, completedAtUtc);
            settlements[competition.DocumentId] = state;
            return Task.FromResult(state);
        }
    }
}
