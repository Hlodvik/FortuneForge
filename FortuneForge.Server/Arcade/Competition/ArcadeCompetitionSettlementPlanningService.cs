namespace FortuneForge.Server.Arcade.Competition;

internal sealed class ArcadeCompetitionSettlementPlanningService
{
    private readonly IArcadeCompetitionStore store;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;

    public ArcadeCompetitionSettlementPlanningService(
        IArcadeCompetitionStore store,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions? rulesOptions = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? new ArcadeCompetitionRulesOptions();
    }

    public async Task<ArcadeCompetitionPayoutPlan> PlanAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        var nowUtc = timeProvider.GetUtcNow();
        if (nowUtc < competition.EndsAtUtc)
            throw new InvalidOperationException("An arcade competition settlement cannot be planned before its window ends.");

        var settlement = await store.ReadSettlementStateAsync(competition, cancellationToken);
        if (settlement.PayoutPlan is not null) return settlement.PayoutPlan;
        if (settlement.IsCompleted)
            throw new InvalidOperationException("A completed legacy settlement without a stored payout plan cannot be planned.");

        var attempts = await store.LoadAttemptsAsync(competition, cancellationToken);
        var rulesResult = ArcadeCompetitionRules.Evaluate(
            new ArcadeCompetitionWindow(
                competition.WindowKind,
                competition.StartsAtUtc,
                competition.EndsAtUtc),
            attempts.Select(attempt => new ArcadeCompetitionAttempt(attempt.PlayerId, attempt.Score)),
            rulesOptions);
        var plan = ArcadeCompetitionPayoutPlan.Create(competition, rulesResult);
        var stored = await store.StoreSettlementPlanAsync(plan, nowUtc, cancellationToken);
        return stored.PayoutPlan
            ?? throw new InvalidOperationException("The arcade competition store did not return the stored payout plan.");
    }
}
