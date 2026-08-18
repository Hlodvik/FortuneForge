using FortuneForge.Server.Cards.Bots;

namespace FortuneForge.Server.Cards.Blackjack.Bots;

public static class BlackjackBotPracticeKinds
{
    public const string Queue = "queue";
    public const string Match = "match";
}

public sealed record BlackjackBotPracticeResponse(
    string ContractVersion,
    string Kind,
    CardBotQueueDto? Queue,
    BlackjackPracticeTableDto? Table);

public sealed record BlackjackPracticeTableDto(
    string MatchId,
    string Status,
    int Version,
    int ActiveSeat,
    BlackjackPracticeHandDto Dealer,
    IReadOnlyList<BlackjackPracticeSeatDto> Seats,
    IReadOnlyList<CardBotPublicEventDto> Events,
    IReadOnlyList<string> LegalActions,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record BlackjackPracticeSeatDto(
    CardBotSeatDto Player,
    BlackjackPracticeHandDto Hand,
    string? Outcome,
    int VirtualWagerUnits);

public sealed record BlackjackPracticeHandDto(
    IReadOnlyList<BlackjackCardResponse> Cards,
    int? Score,
    bool Soft,
    bool Blackjack,
    bool Bust);

internal sealed class BlackjackPracticeState
{
    public required string MatchId { get; init; }
    public required ulong Seed { get; init; }
    public required IReadOnlyList<string> Deck { get; init; }
    public required List<BlackjackPracticePlayer> Players { get; init; }
    public required List<string> DealerCards { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public required DateTime NextBotActionAtUtc { get; set; }
    public required List<CardBotDomainEvent> Events { get; init; }
    public required HashSet<string> IdempotencyKeys { get; init; }
    public int NextCardIndex { get; set; }
    public int ActiveSeat { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "active";
}

internal sealed class BlackjackPracticePlayer
{
    public required QueueSeat Seat { get; init; }
    public required List<string> Cards { get; init; }
    public string Status { get; set; } = "playing";
    public string? Outcome { get; set; }
    public int VirtualWagerUnits { get; set; } = 10;
}
