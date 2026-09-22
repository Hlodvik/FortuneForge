namespace FortuneForge.Server.Games.LiarsDice;

public sealed record StartLiarsDiceMatchRequest(uint? Seed, int? DicePerPlayer);
public sealed record PlaceLiarsDiceBidRequest(int Quantity, int Face);
public sealed record NextLiarsDiceRoundRequest(uint? Seed);
public sealed record LiarsDiceStatusResponse(bool Available, int StartingDicePerPlayer, string Mode);
public sealed record LiarsDiceMatchResponse(Guid MatchId, string Phase, int RoundNumber, string CurrentPlayerId, LiarsDiceBidResponse? CurrentBid, string? CurrentBidderId, int TotalDice, IReadOnlyList<int> Hand, IReadOnlyList<LiarsDicePlayerResponse> Players, LiarsDiceOutcomeResponse? Outcome, string? Winner, string Message);
public sealed record LiarsDicePlayerResponse(string Id, string DisplayName, int DiceCount, bool Active, bool IsHuman);
public sealed record LiarsDiceBidResponse(int Quantity, int Face);
public sealed record LiarsDiceOutcomeResponse(string ChallengerId, string BidderId, string LoserId, int Quantity, int Face, int MatchingDice);
public sealed record LiarsDiceErrorResponse(string Code, string Message);
