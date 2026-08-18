namespace FortuneForge.Server.Cards.Blackjack;

public sealed record BlackjackStartRequest(decimal Wager);

public sealed record BlackjackActionRequest(string Action, int ExpectedVersion);

public sealed record BlackjackStatusResponse(
    bool Available,
    decimal MinimumWager,
    decimal MaximumWager,
    decimal WagerIncrement,
    string DealerRule,
    string BlackjackPayout,
    bool DoubleAllowed,
    bool SplitAllowed,
    bool InsuranceAllowed);

public sealed record BlackjackCardResponse(
    string? Rank,
    string? Suit,
    bool Hidden);

public sealed record BlackjackHandResponse(
    IReadOnlyList<BlackjackCardResponse> Cards,
    int? Score,
    bool Soft,
    bool Blackjack,
    bool Bust);

public sealed record BlackjackGameResponse(
    string GameId,
    string Status,
    string? Outcome,
    string Message,
    decimal Wager,
    decimal TotalWager,
    decimal Payout,
    decimal? Balance,
    BlackjackHandResponse Player,
    BlackjackHandResponse Dealer,
    bool CanHit,
    bool CanStand,
    bool CanDouble,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

internal sealed record BlackjackStoreResult(BlackjackGame Game, long BalanceCents);

internal sealed class BlackjackNotFoundException() : Exception("Blackjack game not found.");

internal sealed class BlackjackInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception(
        $"This account has R{BlackjackMoney.ToRand(availableCents):0.00}, but the wager requires R{BlackjackMoney.ToRand(requiredCents):0.00}.")
{
    public decimal Available { get; } = BlackjackMoney.ToRand(availableCents);
    public decimal Required { get; } = BlackjackMoney.ToRand(requiredCents);
}

internal static class BlackjackMoney
{
    public const long CentsPerRand = 100;
    public const long MinimumWagerCents = 50;
    public const long MaximumWagerCents = 10_000;
    public const long WagerIncrementCents = 50;

    public static decimal ToRand(long cents) => cents / (decimal)CentsPerRand;

    public static long ToWagerCents(decimal wager)
    {
        var cents = checked(wager * CentsPerRand);
        if (cents != decimal.Truncate(cents))
        {
            throw new ArgumentOutOfRangeException(nameof(wager), "A Blackjack wager cannot include a fraction of a cent.");
        }

        var value = checked((long)cents);
        if (value < MinimumWagerCents || value > MaximumWagerCents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wager),
                $"Choose a Blackjack wager from R{ToRand(MinimumWagerCents):0.00} to R{ToRand(MaximumWagerCents):0.00}.");
        }
        if (value % WagerIncrementCents != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wager),
                $"Blackjack wagers must use R{ToRand(WagerIncrementCents):0.00} increments.");
        }
        return value;
    }
}
