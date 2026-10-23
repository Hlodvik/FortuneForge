using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo;

/// <summary>
/// Pure transition engine. The host draws one independent standard card with CSPRNG only after
/// validating the authenticated action. Wallet debits/credits and state must share one transaction.
/// </summary>
public static class HiLoEngine
{
    public static HiLoRound Start(Guid id, decimal wager, PlayingCard initialCard)
    {
        if (id == Guid.Empty) throw new ArgumentException("A round identifier is required.", nameof(id));
        HiLoRules.ValidateWager(wager);
        _ = HiLoRules.RankValue(initialCard);
        return new(id, wager, initialCard, 1m, 0, 0, HiLoRoundStatus.Active, 0m, 0,
            ImmutableArray.Create(new HiLoHistoryEntry(initialCard, HiLoHistoryKind.Initial, null, 1m, 0)));
    }

    public static void EnsureCanGuess(HiLoRound round, HiLoDirection direction)
    {
        EnsureActive(round);
        if (!HiLoRules.GetOdds(round.CurrentCard.Rank, direction).Available)
            throw new HiLoRuleException("impossible_choice", "There is no lower card than Ace or higher card than King.");
    }

    public static void EnsureCanSkip(HiLoRound round)
    {
        EnsureActive(round);
        if (round.Skips >= HiLoRules.MaximumSkips)
            throw new HiLoRuleException("skip_limit", "This round has used all 52 skips. Choose a direction or cash out.");
    }

    public static HiLoRound Guess(HiLoRound round, HiLoDirection direction, PlayingCard nextCard)
    {
        EnsureCanGuess(round, direction);
        var nextRank = HiLoRules.RankValue(nextCard);
        var currentRank = HiLoRules.RankValue(round.CurrentCard);
        var won = direction == HiLoDirection.Higher ? nextRank > currentRank : nextRank < currentRank;
        if (!won)
            return round with
            {
                CurrentCard = nextCard,
                Multiplier = 0m,
                Status = HiLoRoundStatus.Lost,
                Payout = 0m,
                Version = checked(round.Version + 1),
                History = round.History.Add(new(nextCard, HiLoHistoryKind.Loss, direction, 0m, round.Streak)),
            };

        var quote = HiLoRules.Quote(round, direction);
        var streak = checked(round.Streak + 1);
        return round with
        {
            CurrentCard = nextCard,
            Multiplier = quote.Multiplier,
            Streak = streak,
            Status = quote.WillAutoCashOut ? HiLoRoundStatus.AutoCashedOut : HiLoRoundStatus.Active,
            Payout = quote.WillAutoCashOut ? quote.Return : 0m,
            Version = checked(round.Version + 1),
            History = round.History.Add(new(nextCard, HiLoHistoryKind.Win, direction, quote.Multiplier, streak)),
        };
    }

    public static HiLoRound Skip(HiLoRound round, PlayingCard nextCard)
    {
        EnsureCanSkip(round);
        _ = HiLoRules.RankValue(nextCard);
        return round with
        {
            CurrentCard = nextCard,
            Skips = checked(round.Skips + 1),
            Version = checked(round.Version + 1),
            History = round.History.Add(new(nextCard, HiLoHistoryKind.Skip, null, round.Multiplier, round.Streak)),
        };
    }

    public static HiLoRound CashOut(HiLoRound round)
    {
        EnsureActive(round);
        return round with
        {
            Status = HiLoRoundStatus.CashedOut,
            Payout = round.CurrentReturn,
            Version = checked(round.Version + 1),
        };
    }

    private static void EnsureActive(HiLoRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (round.Status != HiLoRoundStatus.Active)
            throw new HiLoRuleException("round_complete", "This round has finished. Start a new round.");
        HiLoRules.ValidateWager(round.Wager);
        _ = HiLoRules.RankValue(round.CurrentCard);
        if (round.Multiplier < 1m || round.Multiplier >= HiLoRules.MultiplierLimit(round.Wager)
            || round.Streak < 0 || round.Streak >= HiLoRules.MaximumStreak
            || round.Skips < 0 || round.Skips > HiLoRules.MaximumSkips
            || round.Version < 0 || round.Payout != 0m || round.Id == Guid.Empty
            || round.History.IsDefaultOrEmpty || round.History[^1].Card != round.CurrentCard)
            throw new HiLoRuleException("invalid_round", "The saved round state is invalid.");
    }
}
