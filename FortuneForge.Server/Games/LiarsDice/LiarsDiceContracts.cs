namespace FortuneForge.Server.Games.LiarsDice;

// Source compatibility for integrations that imported the former host contracts.
public sealed record StartLiarsDiceMatchRequest(uint? Seed, int? DicePerPlayer)
    : FortuneForge.Games.LiarsDice.StartLiarsDiceMatchRequest(Seed, DicePerPlayer);
public sealed record PlaceLiarsDiceBidRequest(int Quantity, int Face)
    : FortuneForge.Games.LiarsDice.PlaceLiarsDiceBidRequest(Quantity, Face);
public sealed record NextLiarsDiceRoundRequest(uint? Seed) : FortuneForge.Games.LiarsDice.NextLiarsDiceRoundRequest(Seed);
public sealed record LiarsDiceErrorResponse(string Code, string Message) : FortuneForge.Games.LiarsDice.LiarsDiceErrorResponse(Code, Message);
public sealed record LiarsDiceMatchResponse(Guid MatchId, string Phase, int RoundNumber, string CurrentPlayerId,
    FortuneForge.Games.LiarsDice.LiarsDiceBidResponse? CurrentBid, string? CurrentBidderId, int TotalDice,
    IReadOnlyList<int> Hand, IReadOnlyList<FortuneForge.Games.LiarsDice.LiarsDicePlayerResponse> Players,
    FortuneForge.Games.LiarsDice.LiarsDiceOutcomeResponse? Outcome, string? Winner, bool OpponentsThinking, string Message)
    : FortuneForge.Games.LiarsDice.LiarsDiceMatchResponse(MatchId, Phase, RoundNumber, CurrentPlayerId, CurrentBid,
        CurrentBidderId, TotalDice, Hand, Players, Outcome, Winner, OpponentsThinking, Message);
