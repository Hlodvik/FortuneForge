using FortuneForge.Server.Arcade.Competition;

namespace FortuneForge.Server.Arcade.Asteroids;

/// <summary>Internal paid-run composition. It creates entropy before the transaction; the committed run seed is authoritative on every retry.</summary>
public sealed class ArcadeCompetitionAsteroidsPaidEntryService
{
    private readonly IArcadeCompetitionPaidEntryCoordinator coordinator;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;
    private readonly Func<ulong> createSeed;

    internal ArcadeCompetitionAsteroidsPaidEntryService(
        IArcadeCompetitionPaidEntryCoordinator coordinator,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions rulesOptions,
        Func<ulong>? createSeed = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? throw new ArgumentNullException(nameof(rulesOptions));
        this.createSeed = createSeed ?? AsteroidsRunIdentity.CreateSeed;
    }

    internal Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAttemptAsync(
        string attemptId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity(AsteroidsModule.GameId, windowKind, window.StartsAtUtc, window.EndsAtUtc);
        var request = new ArcadeCompetitionPaidEntryRequest(
            attemptId,
            competition,
            authenticatedPlayerId,
            rulesOptions.EntryFeeCents,
            nowUtc);
        var attempt = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            request.AttemptId, competition, authenticatedPlayerId, request.EntryFeeCents, nowUtc));
        var seed = createSeed();
        if (seed == 0) throw new InvalidOperationException("The Asteroids seed source returned zero.");
        return coordinator.StartAsteroidsPaidAttemptAsync(
            request,
            new AsteroidsRunIdentity(AsteroidsPaidRuns.RunId(attempt), seed),
            cancellationToken);
    }

    internal Task<AsteroidsReplayCompletionResult> CompleteAttemptAsync(
        string runId,
        string authenticatedPlayerId,
        ArcadeCompetitionWindowKind windowKind,
        AsteroidsReplay replay,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity(AsteroidsModule.GameId, windowKind, window.StartsAtUtc, window.EndsAtUtc);
        return coordinator.CompleteAsteroidsReplayAsync(new AsteroidsReplayCompletionRequest(
            runId, competition, authenticatedPlayerId, replay, nowUtc), cancellationToken);
    }

}
