namespace FortuneForge.Server.Admin.Players;

public sealed record AdminPlayerItem(string UserId, string PlayerName, string? Email, string Role,
    string Status, bool Deactivated, bool IsBot, DateTime? CreatedAtUtc, DateTime? LastActiveAtUtc);
public sealed record AdminPlayerBalances(decimal? SlotsCredits, long? FreeGames, long? SpecialPoints, long? Energy);
public sealed record AdminPlayerSummary(decimal PurchaseCredits, decimal GamingWageredCredits,
    decimal GamingPaidCredits, decimal GamingNetCredits, int RecordsRead, string Scope,
    bool Complete, IReadOnlyList<string> Limitations, decimal GamingRefundedCredits = 0, decimal MockPurchaseCredits = 0);
public sealed record AdminPlayerCoverage(bool Complete, IReadOnlyList<string> Sources, IReadOnlyList<string> Limitations);
public sealed record AdminPlayerDetail(AdminPlayerItem Player, AdminPlayerBalances Balances,
    AdminPlayerSummary Summary, AdminPlayerCoverage Coverage);
public sealed record AdminPlayerPage<T>(IReadOnlyList<T> Items, string? NextCursor,
    bool Complete, IReadOnlyList<string> Limitations);
public sealed record AdminPlayerPurchase(string CheckoutId, string UserId, string? InvoiceId,
    string? ProviderId, bool IsMock, string Status, string Currency, long? AmountMinor,
    decimal? Credits, DateTime? CreatedAtUtc, DateTime? StatusUpdatedAtUtc, bool MockClassificationKnown = true);
public sealed record AdminPlayerGamingEvent(string EventId, string UserId, string Game, string? MatchId,
    string Type, string Status, DateTime? OccurredAtUtc, decimal? WageredCredits, decimal? PaidCredits,
    decimal? NetCredits, decimal? AmountCredits, decimal? BalanceAfterCredits, string Source, decimal RefundedCredits = 0);
public sealed record AdminPlayerMatchParticipant(string? UserId, string? DisplayName, bool? IsBot);
public sealed record AdminPlayerMatch(string MatchId, string Game, string Status,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, decimal? WageredCredits,
    decimal? PaidCredits, decimal? NetCredits, IReadOnlyList<AdminPlayerMatchParticipant>? Players = null);

internal interface IAdminPlayersStore
{
    Task<AdminPlayerPage<AdminPlayerItem>> ListAsync(string? query, int limit, string? cursor, CancellationToken ct);
    Task<AdminPlayerDetail?> DetailAsync(string userId, CancellationToken ct);
    Task<AdminPlayerPage<AdminPlayerPurchase>> PurchasesAsync(string userId, int limit, string? cursor, CancellationToken ct);
    Task<AdminPlayerPage<AdminPlayerGamingEvent>> GamingAsync(string userId, int limit, string? cursor, CancellationToken ct);
    Task<AdminPlayerPage<AdminPlayerMatch>> MatchesAsync(string userId, int limit, string? cursor, CancellationToken ct);
}
