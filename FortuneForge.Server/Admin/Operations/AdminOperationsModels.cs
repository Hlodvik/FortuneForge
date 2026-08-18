namespace FortuneForge.Server.Admin.Operations;

public sealed record AdminOperationsRange(DateTime FromUtc, DateTime ToUtc);

public sealed record AdminGameFinancials(
    decimal WageredCredits,
    decimal PaidCredits,
    decimal HouseNetCredits,
    int CompletedEvents);

public sealed record AdminPoolGameFinancials(
    decimal GrossPoolCredits,
    decimal WinnerPayoutCredits,
    decimal PlatformFeeCredits,
    int SettledRealHumanPoolMatches);

public sealed record AdminFundingFlows(
    decimal CompletedPurchaseCredits,
    int CompletedPurchases,
    decimal CompletedWithdrawalCredits,
    int CompletedWithdrawals);

public sealed record AdminOperationsOverviewResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    AdminGameFinancials Slots,
    AdminGameFinancials Blackjack,
    AdminPoolGameFinancials Solitaire,
    AdminPoolGameFinancials TexasHoldem,
    decimal HouseGamingNetCredits,
    AdminFundingFlows Funding,
    bool Complete,
    IReadOnlyList<string> Limitations);

public sealed record AdminOperationsActivityItem(
    string EventId,
    string Category,
    string Game,
    string Status,
    DateTime OccurredAtUtc,
    decimal? WageredCredits,
    decimal? PaidCredits,
    decimal? HouseNetCredits);

public sealed record AdminOperationsPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);

public sealed record AdminOperationsQueueItem(
    string QueueId,
    string Game,
    string Status,
    int RequiredPlayers,
    int QueuedPlayers,
    decimal EntryCredits,
    DateTime UpdatedAtUtc);

public sealed record AdminOperationsMatchItem(
    string MatchId,
    string Game,
    string Status,
    int PlayerCount,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    decimal WageredCredits,
    decimal PaidCredits,
    decimal HouseNetCredits);

public sealed record AdminOperationsIntegrityCheck(
    string Id,
    string Status,
    string Summary,
    long RecordsChecked,
    long Findings);

public sealed record AdminOperationsIntegrityResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<AdminOperationsIntegrityCheck> Checks,
    bool Complete,
    IReadOnlyList<string> Limitations);

public sealed record AdminOperationsBotGameTelemetry(
    string Game,
    bool Enabled,
    int RecentLeaseAttempts,
    int CompletedTurns,
    int ActiveLeases);

public sealed record AdminOperationsBotsResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<AdminOperationsBotGameTelemetry> Games,
    string FinancialTreatment);

internal sealed record AdminOperationsFinancialRecord(
    string Id,
    string Category,
    string Game,
    string Status,
    DateTime OccurredAtUtc,
    decimal WageredCredits,
    decimal PaidCredits,
    decimal HouseNetCredits,
    decimal GrossPoolCredits = 0,
    decimal PlatformFeeCredits = 0);

internal sealed record AdminOperationsFundingRecord(
    string Id,
    string Category,
    DateTime OccurredAtUtc,
    decimal Credits);

internal sealed record AdminOperationsBotLeaseRecord(
    string Id,
    string Game,
    DateTime UpdatedAtUtc,
    DateTime ExpiresAtUtc,
    bool Completed);

internal sealed record AdminOperationsSourceFinding(
    string Id,
    string Summary,
    long RecordsChecked,
    long Findings);

internal sealed record AdminOperationsSnapshot(
    IReadOnlyList<AdminOperationsFinancialRecord> Financial,
    IReadOnlyList<AdminOperationsFundingRecord> Funding,
    IReadOnlyList<AdminOperationsQueueItem> Queues,
    IReadOnlyList<AdminOperationsMatchItem> Matches,
    IReadOnlyList<AdminOperationsBotLeaseRecord> BotLeases,
    IReadOnlyList<AdminOperationsSourceFinding> SourceFindings,
    bool Complete,
    IReadOnlyList<string> Limitations);
