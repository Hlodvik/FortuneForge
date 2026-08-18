using FortuneForge.Server.Cards.Bots;

namespace FortuneForge.Server.Cards.TexasHoldem.Bots;

public sealed record TexasHoldemBotPracticeResponse(
    string ContractVersion,
    string Kind,
    CardBotQueueDto? Queue,
    TexasHoldemPracticeTableDto? Table);

public sealed record HoldemCardDto(string? Rank, string? Suit, bool Hidden);

public sealed record TexasHoldemPracticeSeatDto(
    CardBotSeatDto Player,
    IReadOnlyList<HoldemCardDto> HoleCards,
    int Stack,
    int Committed,
    string Status,
    string? HandName,
    int Payout);

public sealed record TexasHoldemPracticeTableDto(
    string MatchId,
    string Status,
    string Street,
    int Version,
    int DealerSeat,
    int ActiveSeat,
    int Pot,
    int CurrentBet,
    int MinimumRaiseTo,
    IReadOnlyList<HoldemCardDto> CommunityCards,
    IReadOnlyList<TexasHoldemPracticeSeatDto> Seats,
    IReadOnlyList<CardBotPublicEventDto> Events,
    IReadOnlyList<string> LegalActions,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc);

internal sealed class TexasHoldemState
{
    public required string MatchId { get; init; }
    public required ulong Seed { get; init; }
    public required IReadOnlyList<string> Deck { get; init; }
    public required List<TexasHoldemPlayer> Players { get; init; }
    public required List<string> Community { get; init; }
    public required List<CardBotDomainEvent> Events { get; init; }
    public required HashSet<string> IdempotencyKeys { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; set; }
    public required DateTime NextBotActionAtUtc { get; set; }
    public int NextCardIndex { get; set; }
    public int Version { get; set; } = 1;
    public int DealerSeat { get; set; }
    public int ActiveSeat { get; set; }
    public int CurrentBet { get; set; }
    public int MinimumRaise { get; set; } = 20;
    public string Street { get; set; } = "preflop";
    public string Status { get; set; } = "active";
}

internal sealed class TexasHoldemPlayer
{
    public required QueueSeat Seat { get; init; }
    public required List<string> HoleCards { get; init; }
    public int Stack { get; set; } = 1000;
    public int CommittedRound { get; set; }
    public int CommittedHand { get; set; }
    public int Payout { get; set; }
    public bool HasActed { get; set; }
    public bool RevealAtShowdown { get; set; }
    public string Status { get; set; } = "active";
}
