namespace FortuneForge.Server.Arcade.Competition;

internal sealed record ArcadeCompetitionSettlementExecutionResult(
    ArcadeCompetitionPayoutPlan PayoutPlan,
    ArcadeCompetitionSettlementState SettlementState,
    bool WasAlreadyCompleted);

internal interface IArcadeCompetitionSettlementExecutor
{
    Task<ArcadeCompetitionSettlementExecutionResult> ExecuteAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken);
}

internal sealed class ArcadeCompetitionSettlementExecutor : IArcadeCompetitionSettlementExecutor
{
    private readonly IArcadeCompetitionStore store;
    private readonly ArcadeCompetitionSettlementPlanningService planningService;
    private readonly IArcadeCompetitionPayoutCreditor payoutCreditor;
    private readonly TimeProvider timeProvider;

    public ArcadeCompetitionSettlementExecutor(
        IArcadeCompetitionStore store,
        ArcadeCompetitionSettlementPlanningService planningService,
        IArcadeCompetitionPayoutCreditor payoutCreditor,
        TimeProvider timeProvider)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.planningService = planningService ?? throw new ArgumentNullException(nameof(planningService));
        this.payoutCreditor = payoutCreditor ?? throw new ArgumentNullException(nameof(payoutCreditor));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ArcadeCompetitionSettlementExecutionResult> ExecuteAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        var existing = await store.ReadSettlementStateAsync(competition, cancellationToken);
        if (existing.IsCompleted)
        {
            var existingPlan = existing.PayoutPlan
                ?? throw new InvalidOperationException("A completed legacy settlement without a stored payout plan cannot be executed.");
            return new ArcadeCompetitionSettlementExecutionResult(
                existingPlan,
                existing,
                WasAlreadyCompleted: true);
        }

        var plan = await planningService.PlanAsync(competition, cancellationToken);
        var executionTimeUtc = timeProvider.GetUtcNow();
        foreach (var instruction in plan.Instructions)
        {
            _ = await payoutCreditor.CreditAsync(
                competition,
                instruction,
                executionTimeUtc,
                cancellationToken);
        }

        var completed = await store.MarkSettlementCompletedAsync(
            competition,
            executionTimeUtc,
            cancellationToken);
        return new ArcadeCompetitionSettlementExecutionResult(
            plan,
            completed,
            WasAlreadyCompleted: false);
    }
}
