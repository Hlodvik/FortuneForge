namespace FortuneForge.Server.Games.Craps;

public sealed record StartCrapsRoundRequest(decimal Stake, IReadOnlyList<CrapsExtraBetRequest>? ExtraBets = null);
public sealed record CrapsExtraBetRequest(string Kind, decimal Stake);
public sealed record PlaceCrapsOddsRequest(decimal Stake);
public sealed record CrapsStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, string Mode);
public sealed record CrapsRoundResponse(Guid RoundId, decimal Stake, string Phase, int? Point, IReadOnlyList<CrapsRollResponse> Rolls, CrapsOutcomeResponse? LastOutcome, IReadOnlyList<CrapsExtraBetResponse> ExtraBets);
public sealed record CrapsExtraBetResponse(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn);
public sealed record CrapsRollResponse(int RollNumber, int First, int Second, int Total, string? Result);
public sealed record CrapsOutcomeResponse(int First, int Second, int Total, string Result, bool IsTerminal, decimal? TotalReturn);
public sealed record CrapsErrorResponse(string Code, string Message);
