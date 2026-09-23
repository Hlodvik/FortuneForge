using System.Text.Json.Serialization;

namespace FortuneForge.Games.Solitaire;

public static class SolitaireSessionKinds
{
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Match = "match";
    public const string Result = "result";
}

public static class SolitairePlayerStatuses
{
    public const string Open = "open";
    public const string Queued = "queued";
    public const string Playing = "playing";
    public const string Finished = "finished";
    public const string Forfeited = "forfeited";
    public const string IntegrityFailed = "integrity-failed";
}

public sealed record JoinSolitaireQueueRequest(
    int PlayerCount,
    decimal BuyInCredits,
    string IdempotencyKey,
    int DrawCount = 3);

public sealed record SolitairePlayerResponse(
    string PlayerId,
    string DisplayName,
    int Seat,
    DateTime JoinedAtUtc,
    string Status,
    bool IsCurrentPlayer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Score = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Moves = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ElapsedSeconds = null);

[JsonDerivedType(typeof(SolitaireIdleSessionResponse))]
[JsonDerivedType(typeof(SolitaireQueueSessionResponse))]
[JsonDerivedType(typeof(SolitaireMatchSessionResponse))]
[JsonDerivedType(typeof(SolitaireResultSessionResponse))]
public abstract record SolitaireSessionResponse(string Kind);

public sealed record SolitaireIdleSessionResponse() :
    SolitaireSessionResponse(SolitaireSessionKinds.Idle);

public sealed record SolitaireQueueSessionResponse(
    string TicketId,
    int PlayerCount,
    decimal BuyInCredits,
    decimal PrizePoolCredits,
    decimal WinnerPayoutCredits,
    int Position,
    DateTime JoinedAtUtc,
    IReadOnlyList<SolitairePlayerResponse> Players) :
    SolitaireSessionResponse(SolitaireSessionKinds.Queued);

public sealed record SolitaireMatchSessionResponse(
    string MatchId,
    int PlayerCount,
    decimal BuyInCredits,
    decimal PrizePoolCredits,
    decimal WinnerPayoutCredits,
    DateTime StartedAtUtc,
    DateTime DeadlineAtUtc,
    int Version,
    int Score,
    int Moves,
    long RemainingMilliseconds,
    SolitaireGameResponse Game,
    IReadOnlyList<SolitairePlayerResponse> Players) :
    SolitaireSessionResponse(SolitaireSessionKinds.Match)
{
    public bool IsPaused { get; init; }
    public long PauseRemainingMilliseconds { get; init; }
    public bool CanUndo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SolitaireIntegrityWarningResponse? IntegrityWarning { get; init; }
}

public sealed record SolitaireIntegrityWarningResponse(
    string WarningId,
    string Reason,
    string Purpose,
    DateTime OccurredAtUtc,
    bool Acknowledged);

public sealed record SolitaireStandingResponse(
    int Rank,
    string PlayerId,
    string DisplayName,
    int Score,
    int Moves,
    int ElapsedSeconds,
    string Status,
    decimal PayoutCredits,
    bool IsCurrentPlayer);

public sealed record SolitaireResultSessionResponse(
    string MatchId,
    int PlayerCount,
    decimal BuyInCredits,
    decimal PrizePoolCredits,
    decimal WinnerPayoutCredits,
    decimal PlatformFeeCredits,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<SolitaireStandingResponse> Standings) :
    SolitaireSessionResponse(SolitaireSessionKinds.Result)
{
    public string ClaimStatus { get; init; } = SolitaireClaimStatuses.Unclaimed;
    public bool CanClaim { get; init; }
}

public static class SolitaireClaimStatuses
{
    public const string Unclaimed = "unclaimed";
    public const string Completed = "completed";
}

public sealed record SolitaireMutationResponse(
    SolitaireSessionResponse Session,
    decimal BalanceCredits);

public sealed record SolitaireHistoryItemResponse(
    string MatchId,
    int PlayerCount,
    decimal BuyInCredits,
    decimal PrizePoolCredits,
    int Placement,
    int Score,
    int ElapsedSeconds,
    decimal PayoutCredits,
    decimal NetCredits,
    DateTime CompletedAtUtc,
    IReadOnlyList<string> Opponents);

internal sealed record SolitaireTicket(
    string TicketId,
    string UserId,
    string DisplayName,
    int PlayerCount,
    long BuyInCents,
    string PartitionKey,
    string Status,
    DateTime JoinedAtUtc,
    string? MatchId)
{
    public int DrawCount { get; init; } = 3;
}

internal sealed record SolitaireMatch(
    string MatchId,
    int PlayerCount,
    long BuyInCents,
    long PrizePoolCents,
    long WinnerPayoutCents,
    long PlatformFeeCents,
    uint DealSeed,
    DateTime StartedAtUtc,
    DateTime DeadlineAtUtc,
    string Status,
    IReadOnlyList<string> PlayerIds,
    IReadOnlyList<string> DisplayNames,
    IReadOnlyList<string> TicketIds,
    IReadOnlyList<DateTime> JoinedAtUtc,
    DateTime? CompletedAtUtc,
    string? WinnerUserId)
{
    public string PartitionKey { get; init; } = string.Empty;
    public DateTime? BotFillEligibleAtUtc { get; init; }
    public bool BotsFilled { get; init; }
    public int DrawCount { get; init; } = 3;
}

internal sealed record SolitairePlayerState(
    string MatchId,
    string UserId,
    string DisplayName,
    int Seat,
    string Status,
    SolitaireGameState Game,
    int Version,
    long? ElapsedMilliseconds,
    DateTime? CompletedAtUtc,
    long PayoutCents,
    bool Acknowledged)
{
    public DateTime StartedAtUtc { get; init; }
    public DateTime DeadlineAtUtc { get; init; }
    public bool IsSynthetic { get; init; }
    public int? SyntheticSkill { get; init; }
    public long PauseUsedMilliseconds { get; init; }
    public DateTime? PausedAtUtc { get; init; }
    public IReadOnlyList<SolitaireGameState> UndoHistory { get; init; } = [];
    public IReadOnlyList<SolitaireIntegrityWarning> IntegrityWarnings { get; init; } = [];
}

internal sealed record SolitaireIntegrityWarning(
    string WarningId,
    string Reason,
    string Purpose,
    DateTime OccurredAtUtc,
    DateTime? AcknowledgedAtUtc);

internal sealed record SolitaireStoreSession(
    SolitaireSessionResponse Session,
    long BalanceCents);

internal sealed class SolitaireConflictException(string message) : Exception(message);

internal sealed class SolitaireNotFoundException(string message) : Exception(message);

internal sealed class SolitaireInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{SolitaireMoney.ToCredits(availableCents):0.00}, but the queue requires R{SolitaireMoney.ToCredits(requiredCents):0.00}.")
{
    public decimal Available { get; } = SolitaireMoney.ToCredits(availableCents);
    public decimal Required { get; } = SolitaireMoney.ToCredits(requiredCents);
}

internal static class SolitaireMoney
{
    public const long CentsPerCredit = 100;
    public static readonly int[] PlayerCounts = [4, 6, 8];
    public static readonly long[] BuyInCents = [500, 1_000, 2_500];

    public static decimal ToCredits(long cents) => cents / (decimal)CentsPerCredit;

    public static long ValidateBuyIn(int playerCount, decimal buyInCredits)
    {
        if (!PlayerCounts.Contains(playerCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCount),
                "Competitive Solitaire supports 4, 6, or 8 players.");
        }
        var centsValue = checked(buyInCredits * CentsPerCredit);
        if (centsValue != decimal.Truncate(centsValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(buyInCredits),
                "The Solitaire buy-in cannot include a fraction of a cent.");
        }
        var cents = checked((long)centsValue);
        if (!BuyInCents.Contains(cents))
        {
            throw new ArgumentOutOfRangeException(
                nameof(buyInCredits),
                "Competitive Solitaire buy-ins are R5, R10, or R25.");
        }
        return cents;
    }

    public static long WinnerPayout(int playerCount, long buyInCents) =>
        checked(playerCount * buyInCents * 90 / 100);

    public static int ValidateDrawCount(int drawCount)
    {
        if (drawCount is not 1 and not 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drawCount),
                "Competitive Solitaire supports draw 1 or draw 3.");
        }
        return drawCount;
    }
}
