namespace FortuneForge.Games.Keno.Server;

public sealed record KenoTicketRequest(IReadOnlyList<int>? Numbers);
public sealed record CreateKenoRoundRequest(KenoTicketRequest? Ticket, decimal Wager);
public sealed record KenoPrizeTierResponse(int Spots, int Hits, decimal Multiplier);
public sealed record KenoStatusResponse(bool Available, decimal MinimumWager, decimal MaximumWager, decimal WagerIncrement, decimal Balance, string Mode, IReadOnlyList<KenoPrizeTierResponse> Paytable);
public sealed record KenoTicketResponse(IReadOnlyList<int> Numbers);
public sealed record KenoDrawResponse(IReadOnlyList<int> Numbers);
public sealed record KenoRoundResponse(string RoundId, decimal Balance, string Phase, KenoTicketResponse Ticket, KenoDrawResponse Draw, int HitCount, decimal Wager, decimal Payout, decimal Net, string Outcome);
public sealed record KenoErrorResponse(string Code, string Message);

public sealed record KenoStoreRound(string RoundId, string UserId, KenoTicket Ticket, KenoDraw Draw, int HitCount, long WagerCents, long PayoutCents, string PaytableId);
public sealed record KenoStoreResult(KenoStoreRound Round, long BalanceCents);
public sealed class KenoRoundNotFoundException() : Exception("Keno round not found.");
public sealed class KenoRoundConflictException(string message) : Exception(message);
public sealed class KenoInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{KenoMoney.ToRand(availableCents):0.00}, but the Keno wager requires R{KenoMoney.ToRand(requiredCents):0.00}.");

public interface IKenoStore
{
    Task<KenoStoreResult> StartAsync(string userId, string idempotencyKey, KenoTicket ticket, long wagerCents, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<KenoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
}
