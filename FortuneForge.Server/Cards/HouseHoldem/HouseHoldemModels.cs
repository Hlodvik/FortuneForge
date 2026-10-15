using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Cards.HouseHoldem;

public sealed record CreateHouseHoldemRoundRequest(decimal Ante, decimal Trips = 0);
public sealed record HouseHoldemActionRequest(string Action);

public sealed record HouseHoldemStatusResponse(
    bool Available,
    decimal Balance,
    decimal MinimumAnte,
    decimal MaximumAnte,
    decimal WagerIncrement,
    decimal MaximumTrips,
    string? ActiveRoundId);

public sealed record HouseHoldemCardResponse(string Rank, string Suit);
public sealed record HouseHoldemPayoutBreakdownResponse(decimal Ante, decimal Blind, decimal Trips, decimal Play);

public sealed record HouseHoldemRoundResponse(
    string RoundId,
    decimal Balance,
    string Phase,
    decimal Ante,
    decimal Blind,
    decimal Trips,
    decimal Play,
    decimal TotalWager,
    decimal Payout,
    IReadOnlyList<HouseHoldemCardResponse> PlayerCards,
    IReadOnlyList<HouseHoldemCardResponse>? DealerCards,
    IReadOnlyList<HouseHoldemCardResponse> Board,
    string? PlayerHand,
    string? DealerHand,
    string? Result,
    IReadOnlyList<string> AllowedActions,
    bool CanAdvance,
    HouseHoldemPayoutBreakdownResponse? PayoutBreakdown);

public sealed record HouseHoldemErrorResponse(string Code, string Message);

internal sealed record HouseHoldemStoreResult(string RoundId, HouseHoldemRound Round, long BalanceCents);

internal sealed class HouseHoldemRoundNotFoundException() : Exception("House Hold'em round not found.");
internal sealed class HouseHoldemRoundConflictException(string message) : Exception(message);
internal sealed class HouseHoldemInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{RandMoney.CentsToRand(availableCents):0.00}, but the wager requires R{RandMoney.CentsToRand(requiredCents):0.00}.");

internal static class HouseHoldemMoney
{
    public static long ToAnteCents(decimal rand) => ToCents(rand, HouseHoldemRules.MaximumAnteCents, optional: false);

    public static long ToTripsCents(decimal rand) => ToCents(rand, HouseHoldemRules.MaximumTripsCents, optional: true);

    private static long ToCents(decimal rand, long maximumCents, bool optional)
    {
        if (optional && rand == 0) return 0;
        if (rand < HouseHoldemRules.MinimumBetCents / 100m ||
            rand > maximumCents / 100m ||
            rand * 100m % HouseHoldemRules.BetIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(rand),
                $"Choose whole-rand wagers between R{HouseHoldemRules.MinimumBetCents / 100} and R{maximumCents / 100}.");
        return checked((long)(rand * 100m));
    }

    public static decimal ToRand(long cents) => RandMoney.CentsToRand(cents);
}

internal interface IHouseHoldemStore
{
    Task<HouseHoldemStoreResult> StartAsync(
        string userId, string idempotencyKey, long anteCents, long tripsCents,
        IReadOnlyList<string> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<HouseHoldemStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);

    Task<HouseHoldemStoreResult> TransitionAsync(
        string userId, string roundId, string idempotencyKey, string command,
        DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
