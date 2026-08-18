using System.Security.Cryptography;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed class CreditHoldemService(ICreditHoldemStore store)
{
    public Task<CreditHoldemStoreResult> GetSessionAsync(
        string userId,
        CancellationToken cancellationToken) =>
        store.GetSessionAsync(userId, DateTime.UtcNow, cancellationToken);

    public Task<CreditHoldemStoreResult> JoinAsync(
        string userId,
        string displayName,
        JoinCreditHoldemQueueRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        if (request.ExpectedVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedVersion), "ExpectedVersion cannot be negative.");
        var tableRule = CreditHoldemTableRules.Resolve(request.TableRuleId);
        return store.JoinAsync(
            userId,
            displayName,
            request.ExpectedVersion,
            idempotencyKey,
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
            DateTime.UtcNow,
            cancellationToken,
            tableRule.Id);
    }

    public Task<CreditHoldemStoreResult> CancelAsync(
        string userId,
        string ticketId,
        CreditHoldemVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(ticketId, "ticket");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.CancelAsync(
            userId, ticketId, request.ExpectedVersion, idempotencyKey, DateTime.UtcNow, cancellationToken);
    }

    public Task<CreditHoldemStoreResult> ActionAsync(
        string userId,
        string matchId,
        CreditHoldemActionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.Type)) throw new ArgumentException("An action type is required.", nameof(request.Type));
        return store.ActionAsync(
            userId,
            matchId,
            request with { Type = request.Type.Trim().ToLowerInvariant() },
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<CreditHoldemStoreResult> NextHandAsync(
        string userId,
        string matchId,
        CreditHoldemVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.NextHandAsync(
            userId,
            matchId,
            request.ExpectedVersion,
            idempotencyKey,
            BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<CreditHoldemStoreResult> LeaveAsync(
        string userId,
        string matchId,
        CreditHoldemVersionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateExpectedVersion(request.ExpectedVersion);
        ValidateIdempotencyKey(idempotencyKey);
        return store.LeaveAsync(
            userId, matchId, request.ExpectedVersion, idempotencyKey, DateTime.UtcNow, cancellationToken);
    }

    public Task<CreditHoldemHistoryResponse> HistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken) =>
        store.HistoryAsync(userId, Math.Clamp(limit, 1, 50), cancellationToken);

    public Task<CreditHoldemHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string eventId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(eventId, "history event");
        return store.MarkHistorySeenAsync(userId, eventId, DateTime.UtcNow, cancellationToken);
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
            throw new ArgumentException($"The Hold'em {label} identifier is invalid.", nameof(value));
    }
}
