using System.Collections.Immutable;

namespace FortuneForge.Server.Arcade.Competition;

internal sealed record ArcadeCompetitionSnapshot(
    ArcadeCompetitionIdentity Competition,
    ArcadeCompetitionSettlementState SettlementState,
    bool IsAcceptingAttempts,
    int TotalUniquePlayers,
    long TotalEntryFeesCents,
    int HouseCutBasisPoints,
    long HouseCutCents,
    long VisibleJackpotCents,
    ImmutableArray<ArcadeCompetitionPlacement> PrizePlacements,
    ImmutableArray<ArcadeCompetitionPrizeAllocation> PrizeAllocations,
    ImmutableArray<ArcadeCompetitionRefund> Refunds);

internal sealed record ArcadeCompetitionAllTimeSnapshot(
    string GameId,
    ImmutableArray<ArcadeCompetitionPlacement> Leaderboard);

internal sealed class ArcadeCompetitionSettlementCompletedException : InvalidOperationException
{
    public ArcadeCompetitionSettlementCompletedException()
        : base("This arcade competition has completed settlement and is read-only.")
    {
    }
}

/// <summary>Unregistered composition service. It records competition intent and scores only; it never moves money.</summary>
public sealed class ArcadeCompetitionService
{
    private readonly IArcadeCompetitionStore store;
    private readonly TimeProvider timeProvider;
    private readonly ArcadeCompetitionRulesOptions rulesOptions;

    internal ArcadeCompetitionService(
        IArcadeCompetitionStore store,
        TimeProvider timeProvider,
        ArcadeCompetitionRulesOptions? rulesOptions = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.rulesOptions = rulesOptions ?? new ArcadeCompetitionRulesOptions();
    }

    internal ArcadeCompetitionIdentity ResolveCurrentCompetition(string gameId, ArcadeCompetitionWindowKind windowKind)
    {
        return ResolveCompetition(gameId, windowKind, timeProvider.GetUtcNow());
    }

    internal ArcadeCompetitionIdentity ResolveCompetitionAt(string gameId, ArcadeCompetitionWindowKind windowKind, DateTimeOffset atUtc) =>
        ResolveCompetition(gameId, windowKind, atUtc);

    private ArcadeCompetitionIdentity ResolveCompetition(string gameId, ArcadeCompetitionWindowKind windowKind, DateTimeOffset nowUtc)
    {
        var window = ArcadeCompetitionRules.GetWindow(windowKind, nowUtc, rulesOptions.TimeZone);
        return new ArcadeCompetitionIdentity(gameId, windowKind, window.StartsAtUtc, window.EndsAtUtc);
    }

    internal async Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(
        string gameId,
        ArcadeCompetitionWindowKind windowKind,
        string attemptId,
        string playerId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var competition = ResolveCompetition(gameId, windowKind, nowUtc);
        var settlement = await store.ReadSettlementStateAsync(competition, cancellationToken);
        if (settlement.IsCompleted) throw new ArcadeCompetitionSettlementCompletedException();

        return await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart(
            attemptId,
            competition,
            playerId,
            rulesOptions.EntryFeeCents,
            nowUtc), cancellationToken);
    }

    internal Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(
        ArcadeCompetitionIdentity competition,
        string attemptId,
        string playerId,
        long score,
        CancellationToken cancellationToken) =>
        store.CompleteAttemptAsync(new ArcadeCompetitionAttemptCompletion(
            attemptId,
            competition,
            playerId,
            score,
            timeProvider.GetUtcNow()), cancellationToken);

    internal Task<ArcadeCompetitionSnapshot> GetCurrentSnapshotAsync(
        string gameId,
        ArcadeCompetitionWindowKind windowKind,
        CancellationToken cancellationToken) =>
        GetSnapshotAsync(ResolveCurrentCompetition(gameId, windowKind), cancellationToken);

    internal async Task<ArcadeCompetitionSnapshot> GetSnapshotAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        var settlement = await store.ReadSettlementStateAsync(competition, cancellationToken);
        var attempts = await store.LoadAttemptsAsync(competition, cancellationToken);
        var result = ArcadeCompetitionRules.Evaluate(
            new ArcadeCompetitionWindow(competition.WindowKind, competition.StartsAtUtc, competition.EndsAtUtc),
            attempts.Select(attempt => new ArcadeCompetitionAttempt(attempt.PlayerId, attempt.Score)),
            rulesOptions);

        return new ArcadeCompetitionSnapshot(
            competition,
            settlement,
            !settlement.IsCompleted,
            result.UniquePlayerCount,
            result.TotalEntryFeesCents,
            result.HouseCutBasisPoints,
            result.HouseCutCents,
            result.VisibleJackpotCents,
            result.PrizePlacements,
            result.PrizeAllocations,
            result.Refunds);
    }

    internal async Task<ArcadeCompetitionAllTimeSnapshot> GetAllTimeSnapshotAsync(
        string gameId,
        int maximumPlaces,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("A competition game id is required.", nameof(gameId));
        if (maximumPlaces is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumPlaces));

        var attempts = await store.LoadAttemptsForGameAsync(gameId, cancellationToken);
        var leaderboard = attempts
            .Where(attempt => attempt.IsCompleted)
            .GroupBy(attempt => attempt.PlayerId, StringComparer.Ordinal)
            .Select(group => new { PlayerId = group.Key, Score = group.Max(attempt => attempt.Score!.Value) })
            .OrderByDescending(player => player.Score)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
            .Take(maximumPlaces)
            .Select((player, index) => new ArcadeCompetitionPlacement(index + 1, player.PlayerId, player.Score))
            .ToImmutableArray();
        return new ArcadeCompetitionAllTimeSnapshot(gameId, leaderboard);
    }
}
