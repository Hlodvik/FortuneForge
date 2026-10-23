using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Arcade.HorseFlight;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.HorseFlight;

public sealed class ArcadeCompetitionHorseFlightPaidEntryServiceTests
{
    [Fact]
    public async Task UsesInjectedNonZeroEntropyBeforeDelegatingTheBoundRun()
    {
        var coordinator = new RecordingCoordinator();
        var service = new ArcadeCompetitionHorseFlightPaidEntryService(
            coordinator,
            new FixedTimeProvider(Now),
            new ArcadeCompetitionRulesOptions(),
            () => 77);

        var result = await service.StartAttemptAsync(
            "attempt-horse-flight", "player-1", ArcadeCompetitionWindowKind.Daily, default);

        Assert.Equal(77U, result.Run.Run.Seed);
        Assert.Matches("^horse_flight_[0-9a-f]{64}$", result.Run.Run.RunId);
        Assert.Equal("horse-flight", result.Attempt.Competition.GameId);
        Assert.Equal(result.Attempt.DocumentId, result.Run.Attempt.DocumentId);
        Assert.Equal(result.Run.Run, coordinator.ProposedRun);
    }

    [Fact]
    public async Task CompletionDelegatesTheAuthenticatedReplayWithinTheRequestedCompetitionWindow()
    {
        var coordinator = new RecordingCoordinator();
        var service = new ArcadeCompetitionHorseFlightPaidEntryService(
            coordinator,
            new FixedTimeProvider(Now),
            new ArcadeCompetitionRulesOptions());
        var replay = new HorseFlightReplay(37, []);
        var runId = $"horse_flight_{new string('a', 64)}";

        var result = await service.CompleteAttemptAsync(
            runId, "player-1", ArcadeCompetitionWindowKind.Weekly, replay, default);

        var request = Assert.IsType<HorseFlightPaidReplayCompletionRequest>(coordinator.CompletionRequest);
        Assert.Equal(runId, request.RunId);
        Assert.Equal("player-1", request.AuthenticatedPlayerId);
        Assert.Equal("horse-flight", request.Competition.GameId);
        Assert.Equal(ArcadeCompetitionWindowKind.Weekly, request.Competition.WindowKind);
        Assert.Equal(Now, request.CompletedAtUtc);
        Assert.Same(replay, request.Replay);
        Assert.Equal(123, result.Score);
    }

    private sealed class RecordingCoordinator : IArcadeCompetitionHorseFlightPaidEntryCoordinator
    {
        public HorseFlightPaidRunIdentity? ProposedRun { get; private set; }
        public HorseFlightPaidReplayCompletionRequest? CompletionRequest { get; private set; }

        public Task<ArcadeCompetitionHorseFlightPaidEntryResult> StartHorseFlightPaidAttemptAsync(
            ArcadeCompetitionPaidEntryRequest request,
            HorseFlightPaidRunIdentity proposedRun,
            CancellationToken cancellationToken)
        {
            ProposedRun = proposedRun;
            var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
                request.AttemptId,
                request.Competition,
                request.AuthenticatedPlayerId,
                request.EntryFeeCents,
                request.EnteredAtUtc));
            return Task.FromResult(new ArcadeCompetitionHorseFlightPaidEntryResult(
                attempt,
                false,
                new HorseFlightPaidRunRecord(proposedRun, attempt)));
        }

        public Task<HorseFlightPaidReplayCompletionResult> CompleteHorseFlightReplayAsync(
            HorseFlightPaidReplayCompletionRequest request,
            CancellationToken cancellationToken)
        {
            CompletionRequest = request;
            var attemptId = "attempt-horse-flight-complete";
            var started = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
                attemptId,
                request.Competition,
                request.AuthenticatedPlayerId,
                100,
                request.CompletedAtUtc.AddMinutes(-1)));
            var completed = started.Complete(new ArcadeCompetitionAttemptCompletion(
                attemptId,
                request.Competition,
                request.AuthenticatedPlayerId,
                123,
                request.CompletedAtUtc));
            return Task.FromResult(new HorseFlightPaidReplayCompletionResult(
                completed,
                123,
                HorseFlightPhase.Fell,
                request.Replay.TotalTicks,
                request.Replay.JumpTicks,
                request.Replay.RightClickTicks,
                false));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
}
