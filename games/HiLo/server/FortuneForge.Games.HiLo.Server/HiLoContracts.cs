using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo.Server;

public sealed record HiLoStartRequest(string OperationId, long WagerCents);
public sealed record HiLoActionRequest(string OperationId, long ExpectedRevision, string Choice);
public sealed record HiLoCardResponse(string Rank, string Suit);
public sealed record HiLoHistoryResponse(HiLoCardResponse Card, string Choice, bool? Won);
public sealed record HiLoOfferResponse(decimal Probability, decimal Multiplier, long ProjectedReturnCents, bool Available);
public sealed record HiLoLimits(long MinWagerCents = 10, long MaxWagerCents = 100_000,
    long MaximumReturnCents = 10_000_000, decimal MaxMultiplier = 1000, int MaxSkips = 52, int MaxStreak = 100, decimal HouseEdge = .01m);
public sealed record HiLoRoundResponse(string RoundId, long Revision, string Status, long WagerCents,
    HiLoCardResponse CurrentCard, IReadOnlyList<HiLoHistoryResponse> History, long CurrentReturnCents,
    decimal Multiplier, int Streak, int Skips, HiLoOfferResponse Higher, HiLoOfferResponse Lower, bool CanCashOut, bool CanSkip);
public sealed record HiLoSessionResponse(bool Practice, long WalletBalanceCents, HiLoLimits Limits, HiLoRoundResponse? Round, bool Enabled = true);
public sealed record HiLoErrorResponse(string Code, string Message);
public sealed class HiLoException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Only revealed cards are persisted. The RNG is invoked after command validation inside the store's atomic unit.</summary>
public sealed record HiLoStoredState(long BalanceCents, HiLoRound? Round);
public sealed record HiLoCommand(string OperationId, string Kind, long WagerCents = 0, string? RoundId = null,
    long ExpectedRevision = 0, string? Choice = null);
public sealed record HiLoOperation(string Fingerprint, HiLoSessionResponse Response);
public sealed record HiLoMutation(HiLoStoredState State, HiLoSessionResponse Response, long WalletDeltaCents, string? LedgerType);

public interface IHiLoStore
{
    bool Practice { get; }
    Task<HiLoSessionResponse> GetSessionAsync(string userId, CancellationToken cancellationToken = default);
    Task<HiLoSessionResponse> ExecuteAsync(string userId, HiLoCommand command, Func<PlayingCard> draw,
        DateTimeOffset now, CancellationToken cancellationToken = default, bool allowNewMutations = true);
}
