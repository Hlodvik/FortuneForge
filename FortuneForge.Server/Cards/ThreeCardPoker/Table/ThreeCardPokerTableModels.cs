using System.Text.Json.Serialization;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

public static class ThreeCardPokerTableContract
{
    public const string Version = "cards.three-card-poker.table.v1";
}

public static class ThreeCardPokerTableSessionKinds
{
    public const string Idle = "idle";
    public const string Queue = "queue";
    public const string Table = "table";
}

public sealed record JoinThreeCardPokerTableQueueRequest(int ExpectedVersion);

public sealed record ThreeCardPokerTableWagerRequest(decimal Ante, decimal PairPlus, int ExpectedVersion);

public sealed record ThreeCardPokerTableActionRequest(string Type, int ExpectedVersion);

public sealed record ThreeCardPokerTableVersionRequest(int ExpectedVersion);

public sealed record ThreeCardPokerTableCardResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rank,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Suit,
    bool Hidden);

public sealed record ThreeCardPokerTableHandResponse(
    IReadOnlyList<ThreeCardPokerTableCardResponse> Cards,
    string? Rank);

public sealed record ThreeCardPokerTableSeatResponse(
    string SeatId,
    string DisplayName,
    int Seat,
    string Status,
    decimal Ante,
    decimal PairPlus,
    decimal PlayWager,
    decimal TotalWager,
    decimal Payout,
    string? Outcome,
    string? LastAction,
    ThreeCardPokerTableHandResponse Hand,
    bool IsCurrentPlayer,
    decimal AnteBonusPayout,
    decimal PairPlusPayout);

[JsonDerivedType(typeof(ThreeCardPokerTableIdleSessionResponse))]
[JsonDerivedType(typeof(ThreeCardPokerTableQueueSessionResponse))]
[JsonDerivedType(typeof(ThreeCardPokerTablePlaySessionResponse))]
public abstract record ThreeCardPokerTableSessionResponse(string ContractVersion, string Kind, int Version);

public sealed record ThreeCardPokerTableIdleSessionResponse() :
    ThreeCardPokerTableSessionResponse(ThreeCardPokerTableContract.Version, ThreeCardPokerTableSessionKinds.Idle, 0);

public sealed record ThreeCardPokerTableQueueSessionResponse(
    string TicketId,
    int Position,
    DateTime JoinedAtUtc,
    DateTime HumanGraceEndsAtUtc,
    IReadOnlyList<ThreeCardPokerTableSeatResponse> Players,
    [property: JsonIgnore] int StateVersion) :
    ThreeCardPokerTableSessionResponse(
        ThreeCardPokerTableContract.Version,
        ThreeCardPokerTableSessionKinds.Queue,
        StateVersion);

public sealed record ThreeCardPokerTableResponse(
    string TableId,
    string Phase,
    int Round,
    ThreeCardPokerTableHandResponse Dealer,
    IReadOnlyList<ThreeCardPokerTableSeatResponse> Seats,
    int? ActiveSeat,
    IReadOnlyList<string> LegalActions,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ActionDeadlineAtUtc,
    DateTime? WagerDeadlineAtUtc,
    string? Transition,
    DateTime? NextTransitionAtUtc,
    long RemainingActionMilliseconds,
    long RemainingWagerMilliseconds,
    long RemainingTransitionMilliseconds);

public sealed record ThreeCardPokerTablePlaySessionResponse(
    ThreeCardPokerTableResponse Table,
    [property: JsonIgnore] int StateVersion) :
    ThreeCardPokerTableSessionResponse(
        ThreeCardPokerTableContract.Version,
        ThreeCardPokerTableSessionKinds.Table,
        StateVersion);

public sealed record ThreeCardPokerTableMutationResponse(
    ThreeCardPokerTableSessionResponse Session,
    decimal BalanceCredits);

public sealed record ThreeCardPokerTableHistoryItemResponse(
    string ResultId,
    string Game,
    string Mode,
    string MatchId,
    string TableId,
    int Round,
    decimal WagerCredits,
    decimal PayoutCredits,
    decimal NetCredits,
    string ClaimStatus,
    string SettlementStatus,
    DateTime CompletedAtUtc,
    bool Seen,
    DateTime? SeenAtUtc);

public sealed record ThreeCardPokerTableStatusResponse(
    bool Available,
    decimal MinimumWager,
    decimal MaximumWager,
    decimal WagerIncrement,
    int MinimumStartOccupancy,
    int TableCapacity,
    int HumanGraceSeconds,
    int ActionDeadlineSeconds,
    string DealerRule,
    bool PairPlusAllowed,
    string AnteBonusPaytable,
    string PairPlusPaytable,
    bool PairPlusPaysOnFold);

internal sealed record ThreeCardPokerTableStoreResult(ThreeCardPokerTableSessionResponse Session, long BalanceCents);

internal sealed class ThreeCardPokerTableLobbyState
{
    public Dictionary<string, ThreeCardPokerTableSessionLink> Sessions { get; init; } = new(StringComparer.Ordinal);
    public List<ThreeCardPokerTableTicket> Tickets { get; init; } = [];
    public Dictionary<string, ThreeCardPokerTableState> Tables { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, ThreeCardPokerTableCommandGuard> Guards { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record ThreeCardPokerTableSessionLink(string Kind, string? TicketId, string? TableId);

internal sealed record ThreeCardPokerTableTicket(
    string TicketId,
    string UserId,
    string PublicSeatId,
    string DisplayName,
    string? TargetTableId,
    int EligibleAfterRound,
    string Status,
    int Version,
    DateTime JoinedAtUtc,
    DateTime GraceEndsAtUtc);

internal sealed record ThreeCardPokerTableCommandGuard(
    string Operation,
    string Target,
    string Detail,
    DateTime CreatedAtUtc);

internal sealed record ThreeCardPokerTableLedgerEntry(
    string Id,
    string UserId,
    long AmountCents,
    long BalanceAfterCents,
    string Type,
    string Reference,
    DateTime CreatedAtUtc);

internal sealed record ThreeCardPokerTableRevenueEntry(
    string RoundId,
    string TableId,
    int RoundNumber,
    long HumanWagerCents,
    long HumanPayoutCents,
    int HumanPlayerCount,
    DateTime SettledAtUtc);

internal sealed record ThreeCardPokerTableResultEntry(
    string ResultId,
    string UserId,
    string TableId,
    int RoundNumber,
    long WagerCents,
    long PayoutCents,
    bool IsManagedPlayer,
    DateTime CompletedAtUtc);

internal sealed record ThreeCardPokerManagedPlayerRelease(string ProfileId, string AssignmentId);

internal sealed class ThreeCardPokerTableJournal
{
    public List<ThreeCardPokerTableLedgerEntry> Ledger { get; } = [];
    public List<ThreeCardPokerTableRevenueEntry> Revenue { get; } = [];
    public List<ThreeCardPokerTableResultEntry> Results { get; } = [];
    public List<ThreeCardPokerManagedPlayerRelease> ManagedPlayerReleases { get; } = [];
}

internal sealed class ThreeCardPokerTableInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception(
        $"This account has R{ThreeCardPokerMoney.ToRand(availableCents):0.00}, but the wager requires R{ThreeCardPokerMoney.ToRand(requiredCents):0.00}.")
{
    public decimal Available { get; } = ThreeCardPokerMoney.ToRand(availableCents);
    public decimal Required { get; } = ThreeCardPokerMoney.ToRand(requiredCents);
}
