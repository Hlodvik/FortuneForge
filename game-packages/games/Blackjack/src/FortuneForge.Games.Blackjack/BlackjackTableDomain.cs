namespace FortuneForge.Games.Blackjack;

public static class BlackjackTablePhases
{
    public const string Betting = "betting";
    public const string Insurance = "insurance";
    public const string Active = "active";
    public const string Dealer = "dealer";
    public const string Settled = "settled";
    public const string Closed = "closed";
}

public sealed class BlackjackTableState
{
    public required string TableId { get; init; }
    public required List<BlackjackTablePlayer> Players { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public string Phase { get; set; } = BlackjackTablePhases.Betting;
    public int Version { get; set; } = 1;
    public int RoundNumber { get; set; }
    public ulong RoundSeed { get; set; }
    public IReadOnlyList<string> Deck { get; set; } = [];
    public int NextCardIndex { get; set; }
    public List<string> DealerCards { get; set; } = [];
    public int? ActiveSeat { get; set; }
    public DateTime? ActionDeadlineAtUtc { get; set; }
    public DateTime? WagerDeadlineAtUtc { get; set; }
    public int? PendingSeat { get; set; }
    public string? Transition { get; set; }
    public DateTime? NextTransitionAtUtc { get; set; }
    public int DealerVisibleCardCount { get; set; }
    public bool RoundAccountingSettled { get; set; } = true;
}

public sealed class BlackjackTablePlayer
{
    public required string ActorId { get; init; }
    public required string PublicSeatId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsBot { get; init; }
    public required int? BotSkillLevel { get; init; }
    public required int Seat { get; init; }
    public required string SessionId { get; init; }
    public required DateTime SessionStartedAtUtc { get; init; }
    public long NextWagerCents { get; set; }
    public long WagerCents { get; set; }
    public long TotalWagerCents { get; set; }
    public long PayoutCents { get; set; }
    public List<string> Cards { get; set; } = [];
    public string Status { get; set; } = "waiting";
    public string? Outcome { get; set; }
    public string? LastAction { get; set; }
    public bool LeavingAfterRound { get; set; }
    public long SessionWagerCents { get; set; }
    public long SessionPayoutCents { get; set; }
    public int SessionRoundsPlayed { get; set; }
    public int ConsecutiveMissedRounds { get; set; }
    public int ConsecutiveMissedActionRounds { get; set; }
    public int LastMissedActionRound { get; set; }
    public BlackjackTableSecondaryHand? SecondaryHand { get; set; }
    public int ActiveHandIndex { get; set; }
    public long InsuranceWagerCents { get; set; }
    public long InsurancePayoutCents { get; set; }
    public bool? InsuranceAccepted { get; set; }
}

public sealed class BlackjackTableSecondaryHand
{
    public List<string> Cards { get; set; } = [];
    public long WagerCents { get; set; }
    public long TotalWagerCents { get; set; }
    public string Status { get; set; } = "waiting";
    public string? Outcome { get; set; }
    public string? LastAction { get; set; }
}

public sealed class BlackjackTableConflictException(string message) : Exception(message);
public sealed class BlackjackTableNotFoundException(string message) : Exception(message);
public sealed class BlackjackTableIllegalActionException(string message) : Exception(message);
