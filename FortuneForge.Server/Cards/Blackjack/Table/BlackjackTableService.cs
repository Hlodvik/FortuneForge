namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed class BlackjackTableService(IBlackjackTableStore store)
{
    public Task<BlackjackTableStoreResult> GetSessionAsync(
        string userId,
        CancellationToken cancellationToken) =>
        store.GetSessionAsync(userId, DateTime.UtcNow, cancellationToken);

    public Task<BlackjackTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        JoinBlackjackTableQueueRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        if (request.ExpectedVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion), "ExpectedVersion cannot be negative.");
        return store.JoinAsync(
            userId, displayName, request.ExpectedVersion, idempotencyKey, DateTime.UtcNow, cancellationToken);
    }

    public Task<BlackjackTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        BlackjackTableVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(ticketId, "queue ticket");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.CancelAsync(
            userId, ticketId, request.ExpectedVersion, idempotencyKey, DateTime.UtcNow, cancellationToken);
    }

    public Task<BlackjackTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        BlackjackTableWagerRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(tableId, "table");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.WagerAsync(
            userId,
            tableId,
            BlackjackMoney.ToWagerCents(request.Wager),
            request.ExpectedVersion,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<BlackjackTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        BlackjackTableActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(tableId, "table");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.Type))
            throw new ArgumentException("A Blackjack action type is required.", nameof(request.Type));
        return store.ActionAsync(
            userId,
            tableId,
            request.Type.Trim().ToLowerInvariant(),
            request.ExpectedVersion,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<BlackjackTableStoreResult> LeaveAsync(
        string userId,
        string tableId,
        BlackjackTableVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(tableId, "table");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.LeaveAsync(
            userId, tableId, request.ExpectedVersion, idempotencyKey, DateTime.UtcNow, cancellationToken);
    }

    public Task<IReadOnlyList<BlackjackTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be from 1 to 50.");
        return store.GetHistoryAsync(userId, limit, cancellationToken);
    }

    public Task<BlackjackTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(resultId, "result");
        return store.MarkHistorySeenAsync(userId, resultId, DateTime.UtcNow, cancellationToken);
    }

    internal static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException(
                "Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.",
                nameof(value));
    }

    private static void ValidateExpectedVersion(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "ExpectedVersion must be positive.");
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException($"The Blackjack {label} identifier is invalid.", nameof(value));
    }
}
