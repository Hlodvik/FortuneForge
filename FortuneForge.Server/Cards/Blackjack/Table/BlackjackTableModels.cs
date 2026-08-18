using System.Text.Json.Serialization;

namespace FortuneForge.Server.Cards.Blackjack.Table;

public static class BlackjackTableContract
{
    public const string Version = "cards.blackjack.table.v2";
}

public static class BlackjackTableSessionKinds
{
    public const string Idle = "idle";
    public const string Queue = "queue";
    public const string Table = "table";
}

public sealed record JoinBlackjackTableQueueRequest(int ExpectedVersion);

public sealed record BlackjackTableWagerRequest(decimal Wager, int ExpectedVersion);

public sealed record BlackjackTableActionRequest(string Type, int ExpectedVersion);

public sealed record BlackjackTableVersionRequest(int ExpectedVersion);

public sealed record BlackjackTableCardResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Rank,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Suit,
    bool Hidden);

public sealed record BlackjackTableHandResponse(
    IReadOnlyList<BlackjackTableCardResponse> Cards,
    int? Score,
    bool Soft,
    bool Blackjack,
    bool Bust);

public sealed record BlackjackTablePlayerHandResponse(
    int HandNumber,
    BlackjackTableHandResponse Hand,
    decimal Wager,
    decimal TotalWager,
    decimal Payout,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastAction,
    bool Active);

public sealed record BlackjackTableSeatResponse(
    string SeatId,
    string DisplayName,
    int Seat,
    string Status,
    decimal Wager,
    decimal TotalWager,
    decimal Payout,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastAction,
    BlackjackTableHandResponse Hand,
    bool IsCurrentPlayer,
    IReadOnlyList<BlackjackTablePlayerHandResponse> Hands,
    decimal InsuranceWager,
    decimal InsurancePayout);

[JsonDerivedType(typeof(BlackjackTableIdleSessionResponse))]
[JsonDerivedType(typeof(BlackjackTableQueueSessionResponse))]
[JsonDerivedType(typeof(BlackjackTablePlaySessionResponse))]
public abstract record BlackjackTableSessionResponse(string ContractVersion, string Kind, int Version);

public sealed record BlackjackTableIdleSessionResponse() :
    BlackjackTableSessionResponse(BlackjackTableContract.Version, BlackjackTableSessionKinds.Idle, 0);

public sealed record BlackjackTableQueueSessionResponse(
    string TicketId,
    int Position,
    DateTime JoinedAtUtc,
    DateTime HumanGraceEndsAtUtc,
    IReadOnlyList<BlackjackTableSeatResponse> Players,
    [property: JsonIgnore] int StateVersion) :
    BlackjackTableSessionResponse(
        BlackjackTableContract.Version,
        BlackjackTableSessionKinds.Queue,
        StateVersion);

public sealed record BlackjackTableResponse(
    string TableId,
    string Phase,
    int Round,
    BlackjackTableHandResponse Dealer,
    IReadOnlyList<BlackjackTableSeatResponse> Seats,
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

public sealed record BlackjackTablePlaySessionResponse(
    BlackjackTableResponse Table,
    [property: JsonIgnore] int StateVersion) :
    BlackjackTableSessionResponse(
        BlackjackTableContract.Version,
        BlackjackTableSessionKinds.Table,
        StateVersion);

public sealed record BlackjackTableMutationResponse(
    BlackjackTableSessionResponse Session,
    decimal BalanceCredits);

public sealed record BlackjackTableHistoryItemResponse(
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

public sealed record BlackjackTableStatusResponse(
    bool Available,
    decimal MinimumWager,
    decimal MaximumWager,
    decimal WagerIncrement,
    int MinimumStartOccupancy,
    int TableCapacity,
    int HumanGraceSeconds,
    int ActionDeadlineSeconds,
    string DealerRule,
    string BlackjackPayout,
    bool DoubleAllowed,
    bool SplitAllowed,
    bool InsuranceAllowed,
    bool SurrenderAllowed);

internal sealed record BlackjackTableStoreResult(BlackjackTableSessionResponse Session, long BalanceCents);

internal sealed class BlackjackTableLobbyState
{
    public Dictionary<string, BlackjackTableSessionLink> Sessions { get; init; } = new(StringComparer.Ordinal);
    public List<BlackjackTableTicket> Tickets { get; init; } = [];
    public Dictionary<string, BlackjackTableState> Tables { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, BlackjackTableCommandGuard> Guards { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record BlackjackTableSessionLink(string Kind, string? TicketId, string? TableId);

internal sealed record BlackjackTableTicket(
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

internal sealed record BlackjackTableCommandGuard(
    string Operation,
    string Target,
    string Detail,
    DateTime CreatedAtUtc);

internal sealed record BlackjackTableLedgerEntry(
    string Id,
    string UserId,
    long AmountCents,
    long BalanceAfterCents,
    string Type,
    string Reference,
    DateTime CreatedAtUtc);

internal sealed record BlackjackTableRevenueEntry(
    string RoundId,
    string TableId,
    int RoundNumber,
    long HumanWagerCents,
    long HumanPayoutCents,
    int HumanPlayerCount,
    DateTime SettledAtUtc);

internal sealed record BlackjackTableResultEntry(
    string ResultId,
    string UserId,
    string TableId,
    int RoundNumber,
    long WagerCents,
    long PayoutCents,
    DateTime CompletedAtUtc);

internal sealed class BlackjackTableJournal
{
    public List<BlackjackTableLedgerEntry> Ledger { get; } = [];
    public List<BlackjackTableRevenueEntry> Revenue { get; } = [];
    public List<BlackjackTableResultEntry> Results { get; } = [];
}

internal sealed class BlackjackTableInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception(
        $"This account has R{BlackjackMoney.ToRand(availableCents):0.00}, but the wager requires R{BlackjackMoney.ToRand(requiredCents):0.00}.")
{
    public decimal Available { get; } = BlackjackMoney.ToRand(availableCents);
    public decimal Required { get; } = BlackjackMoney.ToRand(requiredCents);
}
