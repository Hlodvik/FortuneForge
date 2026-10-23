using System.Numerics;
using System.Text.Json;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo.Engine.Tests;

public sealed class HiLoEngineTests
{
    private static PlayingCard Card(int rank, CardSuit suit = CardSuit.Spades) => new((CardRank)rank, suit);
    private static HiLoRound Start(int rank = 7, decimal wager = 1m) => HiLoEngine.Start(Guid.NewGuid(), wager, Card(rank));

    [Fact]
    public void EveryRankDirectionAndStandardDrawHasCorrectStrictOutcomeAndOdds()
    {
        foreach (var rank in Enumerable.Range(1, 13))
        foreach (var direction in Enum.GetValues<HiLoDirection>())
        {
            var odds = HiLoRules.GetOdds((CardRank)rank, direction);
            var expectedWinningRanks = direction == HiLoDirection.Higher ? 13 - rank : rank - 1;
            Assert.Equal(expectedWinningRanks, odds.WinningRanks);
            Assert.Equal(expectedWinningRanks / 13m, odds.Probability);
            Assert.Equal(expectedWinningRanks > 0, odds.Available);
            if (!odds.Available)
            {
                Assert.Equal(0m, odds.RawMultiplier);
                Assert.Equal("impossible_choice", Assert.Throws<HiLoRuleException>(
                    () => HiLoEngine.EnsureCanGuess(Start(rank), direction)).Code);
                continue;
            }
            var wins = 0;
            foreach (var card in StandardDeck.Create())
            {
                var round = Start(rank);
                var result = HiLoEngine.Guess(round, direction, card);
                var shouldWin = direction == HiLoDirection.Higher ? (int)card.Rank > rank : (int)card.Rank < rank;
                Assert.Equal(shouldWin ? HiLoRoundStatus.Active : HiLoRoundStatus.Lost, result.Status);
                Assert.Equal(shouldWin ? 1 : 0, result.Streak);
                Assert.Equal(1, result.Version);
                Assert.Equal(card, result.CurrentCard);
                Assert.Equal(2, result.History.Length);
                Assert.Equal(HiLoRoundStatus.Active, round.Status);
                Assert.Single(round.History);
                if (shouldWin) wins++;
            }
            Assert.Equal(expectedWinningRanks * 4, wins);
        }
    }

    [Fact]
    public void AllAvailableChoicesHaveExactlyNinetyNinePercentUncappedConditionalReturnWithinDecimalPrecision()
    {
        foreach (var rank in Enum.GetValues<CardRank>())
        foreach (var direction in Enum.GetValues<HiLoDirection>())
        {
            var odds = HiLoRules.GetOdds(rank, direction);
            if (!odds.Available) continue;
            Assert.InRange(Math.Abs(0.99m - odds.Probability * odds.RawMultiplier), 0m, 0.000000000000000000000000001m);
            Assert.Equal(12.87m / odds.WinningRanks, odds.RawMultiplier);
            var quote = HiLoRules.Quote(Start((int)rank, 100m), direction);
            // Compare integer-rank weights, avoiding rounding a recurring decimal probability.
            Assert.InRange(odds.WinningRanks * quote.Return, 98.99m * 13m, 99m * 13m);
        }
    }

    [Fact]
    public void MiddleCardOddsAreUnequalToCoinFlipAndIncludeTieLoss()
    {
        var higher = HiLoRules.GetOdds(CardRank.Seven, HiLoDirection.Higher);
        var lower = HiLoRules.GetOdds(CardRank.Seven, HiLoDirection.Lower);
        Assert.Equal(6m / 13m, higher.Probability);
        Assert.Equal(2.145m, higher.RawMultiplier);
        Assert.Equal(higher.Probability, lower.Probability);
        Assert.InRange(Math.Abs(1m - 1m / 13m - higher.Probability - lower.Probability), 0m, 0.000000000000000000000000001m);
    }

    [Theory]
    [InlineData(1, HiLoDirection.Lower)]
    [InlineData(13, HiLoDirection.Higher)]
    public void AceAndKingImpossibleChoicesRejectWithoutMutation(int rank, HiLoDirection direction)
    {
        var round = Start(rank);
        var error = Assert.Throws<HiLoRuleException>(() => HiLoEngine.Guess(round, direction, Card(7)));
        Assert.Equal("impossible_choice", error.Code);
        Assert.Equal(0, round.Version);
        Assert.False(HiLoRules.Quote(round, direction).Odds.Available);
    }

    [Theory]
    [InlineData(1, HiLoDirection.Higher)]
    [InlineData(13, HiLoDirection.Lower)]
    [InlineData(7, HiLoDirection.Higher)]
    [InlineData(7, HiLoDirection.Lower)]
    public void EqualRankAlwaysLosesEvenWithDifferentSuit(int rank, HiLoDirection direction)
    {
        var result = HiLoEngine.Guess(Start(rank), direction, Card(rank, CardSuit.Hearts));
        Assert.Equal(HiLoRoundStatus.Lost, result.Status);
        Assert.Equal(0m, result.Multiplier);
        Assert.Equal(0m, result.CurrentReturn);
        Assert.Equal(0m, result.Payout);
        Assert.Equal(HiLoHistoryKind.Loss, result.History[^1].Kind);
    }

    [Fact]
    public void SameExactCardCanRecurBecauseDrawsAreIndependentWithReplacement()
    {
        var round = Start(7);
        var skipped = HiLoEngine.Skip(round, round.CurrentCard);
        Assert.Equal(round.CurrentCard, skipped.CurrentCard);
        Assert.Equal(1, skipped.Skips);
        Assert.Equal(HiLoRoundStatus.Lost, HiLoEngine.Guess(round, HiLoDirection.Higher, round.CurrentCard).Status);
    }

    [Fact]
    public void IntermediateReturnsDoNotRoundBeforeCashOut()
    {
        var round = HiLoEngine.Guess(Start(7, 0.10m), HiLoDirection.Higher, Card(13));
        Assert.Equal(0.21m, round.CurrentReturn);
        round = HiLoEngine.Guess(round, HiLoDirection.Lower, Card(1));
        round = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13));
        Assert.Equal(2.145m * 1.0725m * 1.0725m, round.Multiplier);
        var settled = HiLoEngine.CashOut(round);
        Assert.Equal(0.24m, settled.Payout);
        Assert.Equal(HiLoRoundStatus.CashedOut, settled.Status);
    }

    [Theory]
    [InlineData("0.10")]
    [InlineData("1")]
    [InlineData("300")]
    [InlineData("1000")]
    public void RationalReferenceMatchesCentPayoutForEveryUncappedStep(string wagerText)
    {
        var wager = decimal.Parse(wagerText, System.Globalization.CultureInfo.InvariantCulture);
        var round = Start(1, wager);
        var numerator = BigInteger.One;
        var denominator = BigInteger.One;
        var wagerCents = new BigInteger(wager * 100m);
        for (var index = 0; round.Status == HiLoRoundStatus.Active; index++)
        {
            var direction = round.CurrentCard.Rank == CardRank.Ace ? HiLoDirection.Higher : HiLoDirection.Lower;
            var next = Card(direction == HiLoDirection.Higher ? 13 : 1);
            numerator *= 1287;
            denominator *= 1200;
            round = HiLoEngine.Guess(round, direction, next);
            var exactCents = BigInteger.Min(wagerCents * numerator / denominator,
                BigInteger.Min(wagerCents * 1000, new BigInteger(10000000)));
            Assert.Equal((decimal)exactCents / 100m, round.CurrentReturn);
            Assert.True(index < 100);
        }
        Assert.Equal(HiLoRoundStatus.AutoCashedOut, round.Status);
        Assert.Equal(round.CurrentReturn, round.Payout);
    }

    [Fact]
    public void VariedWinningPathsMatchIndependentRationalCentReference()
    {
        var random = new Random(20261020);
        for (var path = 0; path < 2000; path++)
        {
            var wagerCents = new BigInteger(random.Next(10, 100001));
            var round = Start(random.Next(1, 14), (decimal)wagerCents / 100m);
            var numerator = BigInteger.One;
            var denominator = BigInteger.One;
            while (round.Status == HiLoRoundStatus.Active)
            {
                var rank = (int)round.CurrentCard.Rank;
                var direction = rank == 1 ? HiLoDirection.Higher : rank == 13 ? HiLoDirection.Lower
                    : random.Next(2) == 0 ? HiLoDirection.Higher : HiLoDirection.Lower;
                var winningRanks = direction == HiLoDirection.Higher ? 13 - rank : rank - 1;
                numerator *= 1287;
                denominator *= 100 * winningRanks;
                var nextRank = direction == HiLoDirection.Higher ? random.Next(rank + 1, 14) : random.Next(1, rank);
                round = HiLoEngine.Guess(round, direction, Card(nextRank));
                var exactCents = BigInteger.Min(wagerCents * numerator / denominator,
                    BigInteger.Min(wagerCents * 1000, new BigInteger(10000000)));
                Assert.True((decimal)exactCents / 100m == round.CurrentReturn,
                    $"Rational mismatch on path {path}, wager {wagerCents}, streak {round.Streak}: {exactCents} cents vs {round.CurrentReturn} credits.");
            }
        }
    }

    [Fact]
    public void RareSuccessesReachMultiplierCapAndAutoSettle()
    {
        var round = Start(12, 1m);
        round = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(2));
        Assert.Equal(HiLoRoundStatus.Lost, round.Status);
        round = Start(12, 1m);
        for (var i = 0; i < 3; i++)
        {
            round = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13));
            if (round.Status == HiLoRoundStatus.Active) round = HiLoEngine.Skip(round, Card(12));
        }
        Assert.Equal(HiLoRoundStatus.AutoCashedOut, round.Status);
        Assert.Equal(1000m, round.Multiplier);
        Assert.Equal(1000m, round.Payout);
    }

    [Fact]
    public void AbsoluteCapPaysExactAmountDespiteRecurringDecimalMultiplier()
    {
        var round = Start(12, 300m);
        round = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13));
        round = HiLoEngine.Skip(round, Card(12));
        round = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13));
        round = HiLoEngine.Skip(round, Card(12));
        var quote = HiLoRules.Quote(round, HiLoDirection.Higher);
        Assert.True(quote.IsCapped);
        Assert.True(quote.WillAutoCashOut);
        Assert.Equal(100000m, quote.Return);
        Assert.True(quote.EffectiveStepMultiplier < quote.Odds.RawMultiplier);
        Assert.True(quote.Odds.Probability * quote.EffectiveStepMultiplier < 0.99m);
        var settled = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13));
        Assert.Equal(quote.Multiplier, settled.Multiplier);
        Assert.Equal(100000m, settled.Payout);
    }

    [Fact]
    public void HundredthWinAutoSettlesEvenIfBelowCap()
    {
        var round = Start(1) with { Streak = 99, Multiplier = 2m };
        var quote = HiLoRules.Quote(round, HiLoDirection.Higher);
        Assert.True(quote.WillAutoCashOut);
        var settled = HiLoEngine.Guess(round, HiLoDirection.Higher, Card(2));
        Assert.Equal(100, settled.Streak);
        Assert.Equal(HiLoRoundStatus.AutoCashedOut, settled.Status);
        Assert.Equal(settled.CurrentReturn, settled.Payout);
    }

    [Fact]
    public void FiftyTwoSkipsPreserveFullPrecisionReturnAndStreakThenReject()
    {
        var round = HiLoEngine.Guess(Start(), HiLoDirection.Higher, Card(13));
        var initialMultiplier = round.Multiplier;
        var initialReturn = round.CurrentReturn;
        for (var i = 0; i < 52; i++) round = HiLoEngine.Skip(round, Card(i % 13 + 1));
        Assert.Equal(52, round.Skips);
        Assert.Equal(1, round.Streak);
        Assert.Equal(initialMultiplier, round.Multiplier);
        Assert.Equal(initialReturn, round.CurrentReturn);
        Assert.Equal("skip_limit", Assert.Throws<HiLoRuleException>(() => HiLoEngine.EnsureCanSkip(round)).Code);
        Assert.Equal(initialReturn, HiLoEngine.CashOut(round).Payout);
    }

    [Fact]
    public void CashOutBeforeAnyChoiceReturnsOriginalWager()
    {
        var settled = HiLoEngine.CashOut(Start(7, 13.37m));
        Assert.Equal(13.37m, settled.Payout);
        Assert.Equal(1, settled.Version);
    }

    [Theory]
    [InlineData(HiLoRoundStatus.CashedOut)]
    [InlineData(HiLoRoundStatus.AutoCashedOut)]
    [InlineData(HiLoRoundStatus.Lost)]
    public void EveryTerminalStateRejectsEveryFurtherAction(HiLoRoundStatus status)
    {
        var round = Start() with { Status = status };
        Assert.False(round.CanCashOut);
        Assert.Equal("round_complete", Assert.Throws<HiLoRuleException>(() => HiLoEngine.Guess(round, HiLoDirection.Higher, Card(13))).Code);
        Assert.Equal("round_complete", Assert.Throws<HiLoRuleException>(() => HiLoEngine.Skip(round, Card(3))).Code);
        Assert.Equal("round_complete", Assert.Throws<HiLoRuleException>(() => HiLoEngine.CashOut(round)).Code);
        Assert.False(HiLoRules.Quote(round, HiLoDirection.Higher).Odds.Available);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("0.09")]
    [InlineData("0.101")]
    [InlineData("1000.01")]
    [InlineData("79228162514264337593543950335")]
    public void InvalidWagersRejectBeforeStateExists(string wagerText)
    {
        var wager = decimal.Parse(wagerText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("invalid_wager", Assert.Throws<HiLoRuleException>(() => Start(7, wager)).Code);
    }

    [Theory]
    [InlineData("0.10")]
    [InlineData("1000")]
    public void WagerBoundsAreInclusive(string wagerText)
    {
        var wager = decimal.Parse(wagerText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(wager, Start(7, wager).Wager);
    }

    [Fact]
    public void InvalidCardsAndDirectionsReject()
    {
        Assert.Throws<ArgumentException>(() => HiLoEngine.Start(Guid.NewGuid(), 1m, Card(0)));
        Assert.Throws<ArgumentException>(() => HiLoEngine.Start(Guid.NewGuid(), 1m, Card(14)));
        Assert.Throws<ArgumentException>(() => HiLoEngine.Start(Guid.NewGuid(), 1m, Card(7, (CardSuit)4)));
        Assert.Throws<ArgumentException>(() => HiLoEngine.Guess(Start(), HiLoDirection.Higher, Card(0)));
        Assert.Throws<ArgumentException>(() => HiLoEngine.Skip(Start(), Card(14)));
        Assert.Throws<ArgumentOutOfRangeException>(() => HiLoRules.GetOdds(CardRank.Seven, (HiLoDirection)99));
        Assert.Throws<ArgumentException>(() => HiLoEngine.Start(Guid.Empty, 1m, Card(7)));
    }

    [Fact]
    public void RoundStateResumesViaJsonWithoutSeedsOrUnrevealedCards()
    {
        var round = HiLoEngine.Skip(HiLoEngine.Guess(Start(), HiLoDirection.Higher, Card(13)), Card(2));
        var serialized = JsonSerializer.Serialize(round);
        var restored = JsonSerializer.Deserialize<HiLoRound>(serialized)!;
        Assert.Equal(round.Id, restored.Id);
        Assert.Equal(round.CurrentCard, restored.CurrentCard);
        Assert.True(round.History.SequenceEqual(restored.History));
        Assert.Equal(round.Multiplier, restored.Multiplier);
        Assert.Equal(round.Version, restored.Version);
        Assert.Equal(round.CurrentReturn, HiLoEngine.CashOut(restored).Payout);
        Assert.DoesNotContain("Seed", serialized);
        Assert.DoesNotContain("Future", serialized);
        Assert.DoesNotContain("Deck", serialized);
        Assert.Equal(3, restored.History.Length);
    }

    [Fact]
    public void CorruptSavedScalarStateRejectsBeforeDraw()
    {
        var round = Start();
        foreach (var corrupt in new[]
        {
            round with { Multiplier = 0.5m }, round with { Multiplier = 1000m },
            round with { Streak = -1 }, round with { Streak = 100 },
            round with { Skips = -1 }, round with { Skips = 53 },
            round with { Version = -1 }, round with { Payout = 1m },
            round with { CurrentCard = Card(8) }, round with { Id = Guid.Empty },
        })
            Assert.Equal("invalid_round", Assert.Throws<HiLoRuleException>(() => HiLoEngine.EnsureCanGuess(corrupt, HiLoDirection.Higher)).Code);
    }
}
