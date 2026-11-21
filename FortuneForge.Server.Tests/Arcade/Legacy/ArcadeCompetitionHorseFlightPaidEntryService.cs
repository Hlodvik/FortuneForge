using FortuneForge.Games.HorseFlight;
using FortuneForge.Games.HorseFlight.Server;
using FortuneForge.Server.Arcade.Competition;

namespace FortuneForge.Server.Arcade.HorseFlight;

/// <summary>Creates and verifies paid Horse Flight runs while the shared coordinator owns admission and balance mutation.</summary>
public sealed class ArcadeCompetitionHorseFlightPaidEntryService
{
    private readonly IArcadeCompetitionHorseFlightPaidEntryCoordinator coordinator;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;
    private readonly HorseFlightPaidRuns runs;

    internal ArcadeCompetitionHorseFlightPaidEntryService(
        IArcadeCompetitionHorseFlightPaidEntryCoordinator coordinator,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions rulesOptions,
        Func<uint>? createSeed = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? throw new ArgumentNullException(nameof(rulesOptions));
        this.runs = new HorseFlightPaidRuns(createSeed);
    }

    internal Task<ArcadeCompetitionHorseFlightPaidEntryResult> StartAttemptAsync(
        string attemptId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity(HorseFlightModule.GameId, windowKind, window.StartsAtUtc, window.EndsAtUtc);
        var request = new ArcadeCompetitionPaidEntryRequest(
            attemptId,
            competition,
            authenticatedPlayerId,
            rulesOptions.EntryFeeCents,
            nowUtc);
        var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            request.AttemptId, competition, authenticatedPlayerId, request.EntryFeeCents, nowUtc));
        var run = runs.PrepareRun(attempt);
        return coordinator.StartHorseFlightPaidAttemptAsync(
            request,
            new HorseFlightPaidRunIdentity(run.RunId, run.Seed),
            cancellationToken);
    }

    internal Task<HorseFlightPaidReplayCompletionResult> CompleteAttemptAsync(
        string runId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        HorseFlightReplay replay,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity(HorseFlightModule.GameId, windowKind, window.StartsAtUtc, window.EndsAtUtc);
        return coordinator.CompleteHorseFlightReplayAsync(new HorseFlightPaidReplayCompletionRequest(
            runId, competition, authenticatedPlayerId, replay, nowUtc), cancellationToken);
    }


}

internal sealed record HorseFlightPaidRunIdentity(string RunId, uint Seed);
