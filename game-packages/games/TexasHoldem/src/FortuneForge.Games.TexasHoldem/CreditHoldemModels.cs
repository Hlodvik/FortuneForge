using System.Text.Json.Serialization;

namespace FortuneForge.Games.TexasHoldem;

public static class CreditHoldemContract
{
    public const string Version = "cards.texas-holdem.credit.v2";
}

public static class CreditHoldemSessionKinds
{
    public const string Idle = "idle";
    public const string Queue = "queue";
    public const string Match = "match";
    public const string Result = "result";
}

public static class CreditHoldemActions
{
    public const string Fold = "fold";
    public const string Check = "check";
    public const string Call = "call";
    public const string Raise = "raise";
}

public sealed record JoinCreditHoldemQueueRequest(int ExpectedVersion, string TableRuleId = "standard");
public sealed record CreditHoldemVersionRequest(int ExpectedVersion);

public sealed record CreditHoldemActionRequest(
    string Type,
    int ExpectedVersion,
    int? RaiseTo = null);

public sealed record CreditHoldemCardResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rank,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Suit,
    bool Hidden);

public sealed record CreditHoldemSeatResponse(
    string SeatId,
    string DisplayName,
    int Seat,
    int StartingStack,
    int Stack,
    int Committed,
    int CommittedRound,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastAction,
    IReadOnlyList<CreditHoldemCardResponse> HoleCards,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HandName,
    bool IsCurrentPlayer);

[JsonDerivedType(typeof(CreditHoldemIdleSessionResponse))]
[JsonDerivedType(typeof(CreditHoldemQueueSessionResponse))]
[JsonDerivedType(typeof(CreditHoldemMatchSessionResponse))]
[JsonDerivedType(typeof(CreditHoldemResultSessionResponse))]
public abstract record CreditHoldemSessionResponse(string ContractVersion, string Kind, int Version);

public sealed record CreditHoldemIdleSessionResponse() :
    CreditHoldemSessionResponse(CreditHoldemContract.Version, CreditHoldemSessionKinds.Idle, 0);

public sealed record CreditHoldemQueueSessionResponse(
    string TicketId,
    int Position,
    DateTime JoinedAtUtc,
    DateTime HumanGraceEndsAtUtc,
    IReadOnlyList<CreditHoldemSeatResponse> Players,
    [property: JsonIgnore] int StateVersion,
    CreditHoldemTableRuleResponse? TableRule = null) :
    CreditHoldemSessionResponse(CreditHoldemContract.Version, CreditHoldemSessionKinds.Queue, StateVersion);

public sealed record CreditHoldemTableResponse(
    string MatchId,
    string Status,
    string Street,
    int HandNumber,
    int DealerSeat,
    int ActiveSeat,
    int Pot,
    int CurrentBet,
    int MinimumRaiseTo,
    int MaximumRaiseTo,
    int? ShortAllInRaiseTo,
    IReadOnlyList<CreditHoldemCardResponse> CommunityCards,
    IReadOnlyList<CreditHoldemSeatResponse> Seats,
    IReadOnlyList<string> LegalActions,
    IReadOnlyList<string> WinningSeatIds,
    int WinningAmount,
    DateTime StartedAtUtc,
    DateTime MatchDeadlineAtUtc,
    DateTime? ActionDeadlineAtUtc,
    long RemainingActionMilliseconds,
    CreditHoldemTableRuleResponse? TableRule = null);

public sealed record CreditHoldemMatchSessionResponse(
    CreditHoldemTableResponse Table,
    [property: JsonIgnore] int StateVersion) :
    CreditHoldemSessionResponse(CreditHoldemContract.Version, CreditHoldemSessionKinds.Match, StateVersion);

public sealed record CreditHoldemStandingResponse(
    int Rank,
    string SeatId,
    string DisplayName,
    int FinalStack,
    string Status,
    decimal PayoutCredits,
    bool IsCurrentPlayer);

public sealed record CreditHoldemResultSessionResponse(
    string MatchId,
    int HandNumber,
    decimal HumanCommittedCredits,
    decimal HumanPayoutCredits,
    decimal HouseNetCredits,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<CreditHoldemStandingResponse> Standings,
    CreditHoldemTableResponse FinalTable,
    [property: JsonIgnore] int StateVersion) :
    CreditHoldemSessionResponse(CreditHoldemContract.Version, CreditHoldemSessionKinds.Result, StateVersion);

public sealed record CreditHoldemMutationResponse(CreditHoldemSessionResponse Session, decimal BalanceCredits);

public sealed record CreditHoldemStatusResponse(
    bool Available,
    int MinimumStartPlayers,
    int MaximumSeats,
    int MinimumRealPlayers,
    decimal SmallBlindCredits,
    decimal BigBlindCredits,
    int ActionDeadlineSeconds,
    int MatchDeadlineSeconds,
    IReadOnlyList<CreditHoldemTableRuleResponse>? TableRules = null);

public sealed record CreditHoldemTableRuleResponse(
    string Id,
    string Name,
    string Description,
    decimal SmallBlindCredits,
    decimal BigBlindCredits,
    decimal AnteCredits,
    decimal MaximumTableStackCredits);

public sealed record CreditHoldemHistoryItemResponse(
    string EventId,
    string MatchId,
    int HandNumber,
    string Status,
    bool Seen,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    decimal CommittedCredits,
    decimal PayoutCredits);

public sealed record CreditHoldemHistoryResponse(IReadOnlyList<CreditHoldemHistoryItemResponse> Items);

internal sealed record CreditHoldemStoreResult(CreditHoldemSessionResponse Session, long BalanceCents);

internal sealed record CreditHoldemTicket(
    string TicketId,
    string UserId,
    string PublicSeatId,
    string DisplayName,
    string PartitionKey,
    string Status,
    int Version,
    DateTime JoinedAtUtc,
    DateTime GraceEndsAtUtc,
    string? MatchId,
    string TableRuleId = "standard");

internal sealed class CreditHoldemMatch
{
    public required string MatchId { get; init; }
    public required ulong DealSeed { get; init; }
    public required IReadOnlyList<string> Deck { get; init; }
    public required List<CreditHoldemPlayer> Players { get; init; }
    public required List<string> Community { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime MatchDeadlineAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public required string PartitionKey { get; init; }
    public string TableRuleId { get; init; } = CreditHoldemTableRules.StandardId;
    public required List<CreditHoldemTicket> PendingTakeovers { get; init; }
    // These collections were introduced after the first credit-table documents
    // were persisted. Keep absent JSON fields compatible with those matches.
    public HashSet<string> LeavingActorIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> HumanPayoutsCents { get; set; } = new(StringComparer.Ordinal);
    public DateTime? ActionDeadlineAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int NextCardIndex { get; set; }
    public int Version { get; set; } = 1;
    public int DealerSeat { get; set; }
    public int ActiveSeat { get; set; }
    public int CurrentBet { get; set; }
    public int MinimumRaise { get; set; }
    public string Street { get; set; } = "preflop";
    public string Status { get; set; } = "active";
    public bool AccountingSettled { get; set; }
    public int HandNumber { get; set; } = 1;
    public long HumanCommittedCents { get; set; }
    public long HumanPayoutCents { get; set; }
    public long HouseNetCents { get; set; }
}

internal sealed class CreditHoldemPlayer
{
    public required string ActorId { get; init; }
    public required string PublicSeatId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsBot { get; init; }
    public required int? BotSkillLevel { get; init; }
    public required int Seat { get; init; }
    public required int StartingStack { get; init; }
    public required List<string> HoleCards { get; init; }
    public int Stack { get; set; }
    public int CommittedRound { get; set; }
    public int CommittedHand { get; set; }
    public bool HasActed { get; set; }
    public bool CanRaise { get; set; } = true;
    public int BetWhenLastActed { get; set; }
    public int ReopenRaiseIncrement { get; set; }
    public bool RevealAtShowdown { get; set; }
    public string Status { get; set; } = "active";
    public string? LastAction { get; set; }
    public int WonHandChips { get; set; }
    public long AccountPayoutCents { get; set; }
}

internal sealed record CreditHoldemFinancialSettlement(
    long HumanCommittedCents,
    IReadOnlyDictionary<string, long> HumanPayoutsCents,
    long HumanPayoutCents,
    long HouseNetCents);

internal sealed record CreditHoldemHistoryRecord(
    string EventId,
    string UserId,
    string MatchId,
    int HandNumber,
    string Status,
    bool Seen,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long CommittedCents,
    long PayoutCents);

internal static class CreditHoldemMoney
{
    public const long CentsPerCredit = 100;
    public const int MinimumStartPlayers = 3;
    public const int MaximumSeats = 5;
    public const int SmallBlindCents = 50;
    public const int BigBlindCents = 100;
    public const string OpenPartition = "open-table-v3-standard";

    public static decimal ToCredits(long cents) => cents / (decimal)CentsPerCredit;
    public static int StackFromBalance(long cents, int maximumStackCents = 10_000) =>
        checked((int)Math.Clamp(cents, 0, maximumStackCents));
}

internal sealed record CreditHoldemTableRule(
    string Id,
    string Name,
    string Description,
    int SmallBlindCents,
    int BigBlindCents,
    int MaximumStackCents)
{
    public CreditHoldemTableRuleResponse Public => new(
        Id,
        Name,
        Description,
        CreditHoldemMoney.ToCredits(SmallBlindCents),
        CreditHoldemMoney.ToCredits(BigBlindCents),
        0,
        CreditHoldemMoney.ToCredits(MaximumStackCents));
}

internal static class CreditHoldemTableRules
{
    public const string FriendlyId = "friendly";
    public const string StandardId = "standard";
    public const string BoldId = "bold";

    public static readonly IReadOnlyList<CreditHoldemTableRule> All =
    [
        new(FriendlyId, "Friendly", "Automatic R0.25/R0.50 blinds · no ante", 25, 50, 2_500),
        new(StandardId, "Standard", "Automatic R0.50/R1.00 blinds · no ante", 50, 100, 10_000),
        new(BoldId, "Bold", "Automatic R2.00/R5.00 blinds · no ante", 200, 500, 50_000)
    ];

    public static CreditHoldemTableRule Resolve(string? value) => All.FirstOrDefault(
        rule => string.Equals(rule.Id, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Choose a valid Hold'em table.", nameof(value));

    public static string Partition(string tableRuleId) => $"open-table-v3-{Resolve(tableRuleId).Id}";
}

internal sealed class CreditHoldemConflictException(string message) : Exception(message);
internal sealed class CreditHoldemNotFoundException(string message) : Exception(message);
internal sealed class CreditHoldemIllegalActionException(string message) : Exception(message);
internal sealed class CreditHoldemInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{CreditHoldemMoney.ToCredits(availableCents):0.00}, but the next Hold'em commitment requires R{CreditHoldemMoney.ToCredits(requiredCents):0.00}.")
{
    public decimal Available { get; } = CreditHoldemMoney.ToCredits(availableCents);
    public decimal Required { get; } = CreditHoldemMoney.ToCredits(requiredCents);
}
