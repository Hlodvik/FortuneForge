namespace FortuneForge.Server.Games.Craps;

public sealed record StartCrapsRoundRequest(decimal Stake, IReadOnlyList<CrapsExtraBetRequest>? ExtraBets = null, string? OperationId = null);
public sealed record CrapsExtraBetRequest(string Kind, decimal Stake);
public sealed record PlaceCrapsOddsRequest(decimal Stake, string? OperationId = null, long? ExpectedRevision = null, int? ExpectedRollNumber = null);
public sealed record SetCrapsWagerRequest(string Kind, decimal Stake, string? OperationId = null, long? ExpectedRevision = null, int? ExpectedRollNumber = null);
public sealed record CrapsOperationRequest(string? OperationId, long? ExpectedRevision = null, int? ExpectedRollNumber = null);
public sealed record CrapsStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, string Mode, long WalletBalanceCents = 0);
public sealed record CrapsRoundResponse(Guid RoundId, decimal Stake, string Phase, int? Point, IReadOnlyList<CrapsRollResponse> Rolls, CrapsOutcomeResponse? LastOutcome, IReadOnlyList<CrapsExtraBetResponse> ExtraBets, long WalletBalanceCents = 0, long Revision = 0, long TotalWagerCents = 0, long TotalReturnCents = 0);
public sealed record CrapsExtraBetResponse(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn, string? WagerId = null, int? PlacedForRoll = null, int? ResolvedOnRoll = null, bool Cancelled = false);
public sealed record CrapsRollResponse(int RollNumber, int First, int Second, int Total, string? Result);
public sealed record CrapsOutcomeResponse(int First, int Second, int Total, string Result, bool IsTerminal, decimal? TotalReturn);
public sealed record CrapsErrorResponse(string Code, string Message);

public sealed class CrapsAccessException() : InvalidOperationException("That Craps table is not available.");
