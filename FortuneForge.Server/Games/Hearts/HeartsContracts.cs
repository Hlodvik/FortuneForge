namespace FortuneForge.Server.Games.Hearts;

public sealed record StartHeartsMatchRequest(uint? Seed, int? TargetScore);
public sealed record PassHeartsRequest(IReadOnlyList<string>? Cards);
public sealed record PlayHeartsCardRequest(string? Card);
public sealed record NextHeartsRoundRequest(uint? Seed);
public sealed record HeartsStatusResponse(bool Available, int DefaultTargetScore, string Mode);
public sealed record HeartsErrorResponse(string Code, string Message);
public sealed record HeartsMatchResponse(
    Guid MatchId,
    int TargetScore,
    int RoundNumber,
    string Phase,
    string PassDirection,
    string Turn,
    string HumanSeat,
    bool YourTurn,
    bool BotsThinking,
    bool HeartsBroken,
    IReadOnlyList<string> SubmittedPasses,
    IReadOnlyList<HeartsPlayerResponse> Players,
    IReadOnlyList<HeartsCardResponse> Hand,
    IReadOnlyList<HeartsCardResponse> LegalCards,
    HeartsTrickResponse CurrentTrick,
    HeartsRecentTrickResponse? RecentTrick,
    IReadOnlyList<HeartsCompletedTrickResponse> CompletedTricks,
    HeartsScoreResponse Score,
    HeartsScoreResponse RoundScore,
    string? Winner,
    string Message);
public sealed record HeartsPlayerResponse(string Seat, int HandCount, int RoundScore, int MatchScore, bool HasPassed);
public sealed record HeartsCardResponse(string Code, string Rank, string Suit, string Label);
public sealed record HeartsTrickResponse(string Leader, IReadOnlyList<HeartsTrickPlayResponse> Plays);
public sealed record HeartsTrickPlayResponse(string Seat, HeartsCardResponse Card);
public sealed record HeartsRecentTrickResponse(int Number, string Leader, string Winner, int Points, IReadOnlyList<HeartsTrickPlayResponse> Plays);
public sealed record HeartsCompletedTrickResponse(int Number, string Leader, string Winner, int Points);
public sealed record HeartsScoreResponse(int North, int East, int South, int West);
