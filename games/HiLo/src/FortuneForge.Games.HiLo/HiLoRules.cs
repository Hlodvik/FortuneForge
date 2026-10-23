using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo;

public static class HiLoRules
{
    public const decimal MinimumWager = 0.10m;
    public const decimal MaximumWager = 1000m;
    public const decimal HouseEdgePerChoice = 0.01m;
    public const decimal MaximumMultiplier = 1000m;
    public const decimal MaximumPayout = 100000m;
    public const int MaximumSkips = 52;
    public const int MaximumStreak = 100;
    public const int DeckSize = 52;
    public const int RankCount = 13;

    public static int RankValue(PlayingCard card)
    {
        if (!Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit))
            throw new ArgumentException("Hi-Lo requires a standard playing card.", nameof(card));
        return (int)card.Rank;
    }

    public static void ValidateWager(decimal wager)
    {
        if (wager < MinimumWager || wager > MaximumWager || decimal.Round(wager, 2) != wager)
            throw new HiLoRuleException("invalid_wager", "Wagers must be 0.10–1000.00 credits in whole cents.");
    }

    public static HiLoOdds GetOdds(CardRank rank, HiLoDirection direction)
    {
        if (!Enum.IsDefined(rank)) throw new ArgumentOutOfRangeException(nameof(rank));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        var winningRanks = direction == HiLoDirection.Higher ? RankCount - (int)rank : (int)rank - 1;
        // Algebraically (1 - 0.01) / (winningRanks / 13). Do not divide by a rounded probability.
        return new(direction, winningRanks, winningRanks / 13m,
            winningRanks == 0 ? 0m : 12.87m / winningRanks, winningRanks != 0);
    }

    public static decimal MultiplierLimit(decimal wager)
    {
        ValidateWager(wager);
        return Math.Min(MaximumMultiplier, MaximumPayout / wager);
    }

    public static decimal Return(decimal wager, decimal multiplier)
    {
        ValidateWager(wager);
        if (multiplier < 0m || multiplier > MultiplierLimit(wager))
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        // A recurring decimal representation of MaximumPayout / wager must still pay the exact cap.
        var credits = multiplier == MultiplierLimit(wager)
            ? Math.Min(MaximumPayout, wager * MaximumMultiplier)
            : Math.Min(MaximumPayout, wager * multiplier);
        return decimal.Floor(credits * 100m) / 100m;
    }

    public static HiLoOffer Quote(HiLoRound round, HiLoDirection direction)
    {
        ArgumentNullException.ThrowIfNull(round);
        var odds = GetOdds(round.CurrentCard.Rank, direction);
        if (round.Status != HiLoRoundStatus.Active || !odds.Available)
            return new(odds with { Available = false }, 0m, 0m, 0m, false, false);
        var raw = round.Multiplier * odds.RawMultiplier;
        var limit = MultiplierLimit(round.Wager);
        var next = Math.Min(raw, limit);
        return new(odds, next, Return(round.Wager, next), next / round.Multiplier,
            raw > limit, next >= limit || round.Streak + 1 >= MaximumStreak);
    }
}

public static class HiLoModule
{
    public static GameDescriptor Descriptor { get; } = new(
        "hi-lo", "Hi-Lo", GameCategory.Casino, "0.1.0", "/games/hi-lo", "/api/games/hi-lo",
        GameCapability.Credits | GameCapability.FreePlay | GameCapability.History);
}
