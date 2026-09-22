namespace FortuneForge.Server.Games.Craps;

public sealed record StartCrapsRoundRequest(decimal Stake);
public sealed record CrapsStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, string Mode);
public sealed record CrapsRoundResponse(Guid RoundId, decimal Stake, string Phase, int? Point, IReadOnlyList<CrapsRollResponse> Rolls, CrapsOutcomeResponse? LastOutcome);
public sealed record CrapsRollResponse(int RollNumber, int First, int Second, int Total, string? Result);
public sealed record CrapsOutcomeResponse(int First, int Second, int Total, string Result, bool IsTerminal, decimal? TotalReturn);
public sealed record CrapsErrorResponse(string Code, string Message);
