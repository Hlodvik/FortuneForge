namespace FortuneForge.Server.Arcade.Competition;

/// <summary>
/// Internal composition for paid entry only. The coordinator's Firestore transaction remains the
/// authority for settlement state, balance mutation, idempotency, and all concurrency checks.
/// </summary>
public sealed class ArcadeCompetitionPaidEntryService
{
    private readonly IArcadeCompetitionPaidEntryCoordinator coordinator;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;

    internal ArcadeCompetitionPaidEntryService(
        IArcadeCompetitionPaidEntryCoordinator coordinator,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions rulesOptions)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? throw new ArgumentNullException(nameof(rulesOptions));
    }

    internal Task<ArcadeCompetitionPaidEntryResult> StartAttemptAsync(
        string gameId,
        ArcadeCompetitionWindowKind windowKind,
        string attemptId,
        string authenticatedPlayerId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        var competition = new ArcadeCompetitionIdentity(
            gameId,
            windowKind,
            window.StartsAtUtc,
            window.EndsAtUtc);
        return coordinator.StartPaidAttemptAsync(new ArcadeCompetitionPaidEntryRequest(
            attemptId,
            competition,
            authenticatedPlayerId,
            rulesOptions.EntryFeeCents,
            nowUtc), cancellationToken);
    }
}
