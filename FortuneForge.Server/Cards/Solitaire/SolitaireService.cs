using System.Security.Cryptography;

namespace FortuneForge.Server.Cards.Solitaire;

internal interface ICompetitiveSolitaireStore
{
    Task<SolitaireStoreSession> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> JoinAsync(
        string userId,
        string displayName,
        int playerCount,
        long buyInCents,
        int drawCount,
        string idempotencyKey,
        uint dealSeed,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> CancelAsync(
        string userId,
        string ticketId,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> CommandAsync(
        string userId,
        string matchId,
        SolitaireCommandRequest command,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> ForfeitAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> DismissAsync(
        string userId,
        string matchId,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<SolitaireStoreSession> ClaimAsync(
        string userId,
        string matchId,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SolitaireHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class CompetitiveSolitaireService(ICompetitiveSolitaireStore store)
{
    public Task<SolitaireStoreSession> GetSessionAsync(
        string userId,
        CancellationToken cancellationToken) =>
        store.GetSessionAsync(userId, DateTime.UtcNow, cancellationToken);

    public Task<SolitaireStoreSession> JoinAsync(
        string userId,
        string displayName,
        JoinSolitaireQueueRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        var buyInCents = SolitaireMoney.ValidateBuyIn(request.PlayerCount, request.BuyInCredits);
        var drawCount = SolitaireMoney.ValidateDrawCount(request.DrawCount);
        Span<byte> seedBytes = stackalloc byte[sizeof(uint)];
        RandomNumberGenerator.Fill(seedBytes);
        var seed = BitConverter.ToUInt32(seedBytes);
        return store.JoinAsync(
            userId,
            displayName,
            request.PlayerCount,
            buyInCents,
            drawCount,
            request.IdempotencyKey,
            seed,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SolitaireStoreSession> CancelAsync(
        string userId,
        string ticketId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(ticketId, "ticket");
        ValidateIdempotencyKey(idempotencyKey);
        return store.CancelAsync(
            userId,
            ticketId,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SolitaireStoreSession> CommandAsync(
        string userId,
        string matchId,
        SolitaireCommandRequest command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateIdempotencyKey(idempotencyKey);
        if (command.ExpectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.ExpectedVersion),
                "The Solitaire state version must be positive.");
        }
        return store.CommandAsync(
            userId,
            matchId,
            command with { Type = command.Type?.Trim().ToLowerInvariant() ?? string.Empty },
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SolitaireStoreSession> ForfeitAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateIdempotencyKey(idempotencyKey);
        if (expectedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedVersion),
                "The Solitaire state version must be positive.");
        }
        return store.ForfeitAsync(
            userId,
            matchId,
            expectedVersion,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SolitaireStoreSession> DismissAsync(
        string userId,
        string matchId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateIdempotencyKey(idempotencyKey);
        return store.DismissAsync(
            userId,
            matchId,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SolitaireStoreSession> ClaimAsync(
        string userId,
        string matchId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(matchId, "match");
        ValidateIdempotencyKey(idempotencyKey);
        return store.ClaimAsync(
            userId,
            matchId,
            idempotencyKey,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<IReadOnlyList<SolitaireHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Choose a history limit from 1 to 100.");
        }
        return store.GetHistoryAsync(userId, limit, DateTime.UtcNow, cancellationToken);
    }

    internal static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length is < 16 or > 128 ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "The idempotency key must contain 16 to 128 letters, digits, hyphens, or underscores.",
                nameof(value));
        }
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException($"The Solitaire {label} identifier is invalid.", nameof(value));
        }
    }
}
