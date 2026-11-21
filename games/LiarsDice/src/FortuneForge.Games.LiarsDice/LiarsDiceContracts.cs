namespace FortuneForge.Games.LiarsDice;

public record StartLiarsDiceMatchRequest(uint? Seed, int? DicePerPlayer);
public record PlaceLiarsDiceBidRequest(int Quantity, int Face);
public record NextLiarsDiceRoundRequest(uint? Seed);
public record LiarsDiceStatusResponse(bool Available, int StartingDicePerPlayer, string Mode);
public record LiarsDiceMatchResponse(Guid MatchId, string Phase, int RoundNumber, string CurrentPlayerId, LiarsDiceBidResponse? CurrentBid, string? CurrentBidderId, int TotalDice, IReadOnlyList<int> Hand, IReadOnlyList<LiarsDicePlayerResponse> Players, LiarsDiceOutcomeResponse? Outcome, string? Winner, bool OpponentsThinking, string Message)
{
    public IReadOnlyList<LiarsDiceRoundHistoryResponse> ResolvedRounds { get; init; } = [];
}
public record LiarsDicePlayerResponse(string Id, string DisplayName, int DiceCount, bool Active, bool IsHuman);
public record LiarsDiceBidResponse(int Quantity, int Face);
public record LiarsDiceOutcomeResponse(string ChallengerId, string BidderId, string LoserId, int Quantity, int Face, int MatchingDice, string CallType);
public record LiarsDiceErrorResponse(string Code, string Message);

public sealed record LiarsDiceRoundHistoryResponse(int RoundNumber, LiarsDiceOutcomeResponse Outcome);
