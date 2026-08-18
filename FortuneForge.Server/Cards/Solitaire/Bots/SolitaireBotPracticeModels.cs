using FortuneForge.Server.Cards.Bots;

namespace FortuneForge.Server.Cards.Solitaire.Bots;

public sealed record SolitaireBotPracticeResponse(
    string ContractVersion,
    string Kind,
    CardBotQueueDto? Queue,
    SolitaireBotPracticeMatchDto? Match,
    SolitaireBotPracticeResultDto? Result);

public sealed record SolitaireBotPracticeMatchDto(
    string MatchId,
    int Version,
    DateTime StartedAtUtc,
    DateTime DeadlineAtUtc,
    long RemainingMilliseconds,
    SolitaireGameResponse Game,
    IReadOnlyList<CardBotSeatDto> Seats);

public sealed record SolitaireBotPracticeStandingDto(
    int Rank,
    CardBotSeatDto Player,
    int Score,
    int Moves,
    string Status);

public sealed record SolitaireBotPracticeResultDto(
    string MatchId,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<SolitaireBotPracticeStandingDto> Standings);

internal sealed class SolitaireBotMatchState
{
    public required string MatchId { get; init; }
    public required ulong DecisionSeed { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime DeadlineAtUtc { get; init; }
    public required List<SolitaireBotPlayerState> Players { get; init; }
    public DateTime? CompletedAtUtc { get; set; }
}

internal sealed class SolitaireBotPlayerState
{
    public required QueueSeat Seat { get; init; }
    public required SolitaireGameState Game { get; set; }
    public required DateTime NextActionAtUtc { get; set; }
    public required HashSet<string> IdempotencyKeys { get; init; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "playing";
}
