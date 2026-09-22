using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionPaidEntryServiceTests
{
    [Theory]
    [InlineData("daily", "2026-09-03T22:00:00+00:00", "2026-09-04T22:00:00+00:00")]
    [InlineData("weekly", "2026-08-30T22:00:00+00:00", "2026-09-06T22:00:00+00:00")]
    public async Task StartUsesOneClockReadAndPassesTheCurrentJohannesburgCompetition(
        string kindName,
        string expectedStart,
        string expectedEnd)
    {
        var kind = kindName == "daily" ? ArcadeCompetitionWindowKind.Daily : ArcadeCompetitionWindowKind.Weekly;
        var now = new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
        var clock = new CountingTimeProvider(now);
        var coordinator = new RecordingCoordinator();
        var service = new ArcadeCompetitionPaidEntryService(
            coordinator,
            clock,
            new ArcadeCompetitionRulesOptions());

        _ = await service.StartAttemptAsync("asteroids", kind, "attempt-1", "player-1", default);

        var request = Assert.IsType<ArcadeCompetitionPaidEntryRequest>(coordinator.Request);
        Assert.Equal(1, clock.UtcNowReadCount);
        Assert.Equal("asteroids", request.Competition.GameId);
        Assert.Equal(kind, request.Competition.WindowKind);
        Assert.Equal(DateTimeOffset.Parse(expectedStart), request.Competition.StartsAtUtc);
        Assert.Equal(DateTimeOffset.Parse(expectedEnd), request.Competition.EndsAtUtc);
        Assert.Equal(now, request.EnteredAtUtc);
        Assert.Equal(ArcadeCompetitionRulesOptions.DefaultEntryFeeCents, request.EntryFeeCents);
        Assert.Equal("attempt-1", request.AttemptId);
        Assert.Equal("player-1", request.AuthenticatedPlayerId);
    }

    [Fact]
    public async Task StartPassesCoordinatorFailuresThroughUnchanged()
    {
        var failure = new ArcadeCompetitionInsufficientCreditsException(99, 100);
        var service = new ArcadeCompetitionPaidEntryService(
            new FailingCoordinator(failure),
            new CountingTimeProvider(new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero)),
            new ArcadeCompetitionRulesOptions());

        var thrown = await Assert.ThrowsAsync<ArcadeCompetitionInsufficientCreditsException>(() =>
            service.StartAttemptAsync("asteroids", ArcadeCompetitionWindowKind.Daily, "attempt-1", "player-1", default));

        Assert.Same(failure, thrown);
    }

    private sealed class RecordingCoordinator : IArcadeCompetitionPaidEntryCoordinator
    {
        public ArcadeCompetitionPaidEntryRequest? Request { get; private set; }

        public Task<ArcadeCompetitionPaidEntryResult> StartPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
                request.AttemptId,
                request.Competition,
                request.AuthenticatedPlayerId,
                request.EntryFeeCents,
                request.EnteredAtUtc));
            return Task.FromResult(new ArcadeCompetitionPaidEntryResult(attempt, false));
        }

        public Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAsteroidsPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            FortuneForge.Server.Arcade.Asteroids.AsteroidsRunIdentity proposedRun,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AsteroidsReplayCompletionResult> CompleteAsteroidsReplayAsync(AsteroidsReplayCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingCoordinator(Exception failure) : IArcadeCompetitionPaidEntryCoordinator
    {
        public Task<ArcadeCompetitionPaidEntryResult> StartPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            CancellationToken cancellationToken) => Task.FromException<ArcadeCompetitionPaidEntryResult>(failure);

        public Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAsteroidsPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            FortuneForge.Server.Arcade.Asteroids.AsteroidsRunIdentity proposedRun,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AsteroidsReplayCompletionResult> CompleteAsteroidsReplayAsync(AsteroidsReplayCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public int UtcNowReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            UtcNowReadCount++;
            return nowUtc;
        }
    }
}
