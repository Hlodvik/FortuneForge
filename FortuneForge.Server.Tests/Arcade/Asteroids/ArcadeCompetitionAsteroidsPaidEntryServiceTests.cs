using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Asteroids;

public sealed class ArcadeCompetitionAsteroidsPaidEntryServiceTests
{
    [Fact]
    public async Task UsesInjectedNonZeroEntropyBeforeDelegatingTheBoundRun()
    {
        var coordinator = new RecordingCoordinator();
        var service = new ArcadeCompetitionAsteroidsPaidEntryService(
            coordinator,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero)),
            new ArcadeCompetitionRulesOptions(),
            () => 77);

        var result = await service.StartAttemptAsync(
            "attempt-asteroids", "player-1", ArcadeCompetitionWindowKind.Daily, default);

        Assert.Equal(77UL, result.Run.Run.Seed);
        Assert.Equal("asteroids", result.Attempt.Competition.GameId);
        Assert.Equal(result.Attempt.DocumentId, result.Run.Attempt.DocumentId);
    }

    private sealed class RecordingCoordinator : IArcadeCompetitionPaidEntryCoordinator
    {
        public Task<ArcadeCompetitionPaidEntryResult> StartPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAsteroidsPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            AsteroidsRunIdentity proposedRun,
            CancellationToken cancellationToken)
        {
            var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
                request.AttemptId, request.Competition, request.AuthenticatedPlayerId, request.EntryFeeCents, request.EnteredAtUtc));
            return Task.FromResult(new ArcadeCompetitionAsteroidsPaidEntryResult(
                attempt,
                false,
                new AsteroidsRunRecord(proposedRun, attempt)));
        }

        public Task<AsteroidsReplayCompletionResult> CompleteAsteroidsReplayAsync(AsteroidsReplayCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
