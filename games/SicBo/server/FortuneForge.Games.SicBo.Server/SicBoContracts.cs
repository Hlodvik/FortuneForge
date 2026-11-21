namespace FortuneForge.Games.SicBo.Server;

public sealed record SicBoBetRequest(string? Kind, decimal? Stake, int? Face, int? Total, int? FirstFace, int? SecondFace);
public sealed record CreateSicBoRoundRequest(IReadOnlyList<SicBoBetRequest>? Bets);
public sealed record SicBoStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStakePerBet, decimal StakeIncrement, int MaximumBetsPerRound, decimal Balance, string Mode);
public sealed record SicBoSettlementResponse(int BetIndex, string Kind, decimal Stake, int? Face, int? Total, int? FirstFace, int? SecondFace, bool Won, decimal ProfitOdds, decimal Profit, decimal TotalReturn);
public sealed record SicBoRoundResponse(string RoundId, decimal Balance, string Phase, IReadOnlyList<int> Dice, int Total, bool IsTriple, decimal TotalStaked, decimal TotalReturn, decimal Profit, IReadOnlyList<SicBoSettlementResponse> Settlements);
public sealed record SicBoErrorResponse(string Code, string Message);

public sealed record SicBoStoredBet(string Kind, long StakeCents, int? Face, int? Total, int? FirstFace, int? SecondFace);
public sealed record SicBoStoreRound(string RoundId, string UserId, IReadOnlyList<SicBoStoredBet> Bets, SicBoRoll Roll);
public sealed record SicBoStoreResult(SicBoStoreRound Round, long BalanceCents);
public sealed class SicBoRoundNotFoundException() : Exception("Sic Bo round not found.");
public sealed class SicBoRoundConflictException(string message) : Exception(message);
public sealed class SicBoInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{SicBoMoney.ToRand(availableCents):0.00}, but the bet slip requires R{SicBoMoney.ToRand(requiredCents):0.00}.");

public interface ISicBoStore
{
    Task<SicBoStoreResult> StartAsync(string userId, string idempotencyKey, IReadOnlyList<SicBoStoredBet> bets, SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
}
