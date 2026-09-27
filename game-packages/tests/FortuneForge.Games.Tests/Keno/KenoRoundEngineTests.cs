using FortuneForge.Games.Keno;

namespace FortuneForge.Games.Tests.Keno;

public sealed class KenoRoundEngineTests
{
    [Theory]
    [InlineData(new[] { 1, 1 })]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 81 })]
    public void TicketRejectsDuplicateOrOutOfRangeNumbers(int[] numbers)
    {
        Assert.ThrowsAny<ArgumentException>(() => new KenoTicket(numbers));
    }

    [Fact]
    public void TicketRejectsMoreThanTenNumbers()
    {
        Assert.Throws<ArgumentException>(() => new KenoTicket(Enumerable.Range(1, 11)));
    }

    [Fact]
    public void DrawProducesTwentyUniqueNumbersInTheValidRange()
    {
        var draw = KenoRoundEngine.Draw(new Random(12345));

        Assert.Equal(KenoDraw.DrawCount, draw.Numbers.Length);
        Assert.Equal(KenoDraw.DrawCount, draw.Numbers.Distinct().Count());
        Assert.All(draw.Numbers, number => Assert.InRange(number, 1, KenoTicket.MaximumNumber));
    }

    [Fact]
    public void PlayUsesTheInjectedDrawDeterministically()
    {
        var ticket = new KenoTicket([3, 7, 15]);
        var draw = new KenoDraw([3, 7, .. Enumerable.Range(21, 18)]);

        var round = KenoRoundEngine.Play(ticket, draw);

        Assert.Same(ticket, round.Result.Ticket);
        Assert.Same(draw, round.Result.Draw);
        Assert.Equal(2, round.Result.HitCount);
    }

    [Fact]
    public void PlayCountsEveryTicketNumberPresentInTheDraw()
    {
        var ticket = new KenoTicket([1, 20, 40, 60, 80]);
        var draw = new KenoDraw([1, 20, 40, 60, .. Enumerable.Range(2, 16)]);

        var round = KenoRoundEngine.Play(ticket, draw);

        Assert.Equal(4, round.Result.HitCount);
    }

    [Theory]
    [InlineData(5, 2, 0)]
    [InlineData(5, 4, 15)]
    [InlineData(10, 0, 5)]
    [InlineData(10, 3, 0)]
    [InlineData(10, 5, 2)]
    [InlineData(10, 10, 200_000)]
    [InlineData(8, 3, 0)]
    public void StandardPaytableReturnsThePublishedMultiplier(int spots, int hits, int expectedMultiplier)
    {
        var ticket = new KenoTicket(Enumerable.Range(1, spots));

        var outcome = StandardKenoPaytable.Instance.Evaluate(ticket, hits);

        Assert.Equal(StandardKenoPaytable.Id, outcome.PaytableId);
        Assert.Equal((decimal)expectedMultiplier, outcome.Value);
    }

    [Fact]
    public void OneSpotUsesThePublishedFractionalMultiplier()
    {
        var outcome = StandardKenoPaytable.Instance.Evaluate(new KenoTicket([1]), 1);

        Assert.Equal(2.5m, outcome.Value);
    }

    [Fact]
    public void StandardPaytableMatchesAConventionalLotteryKenoRiskProfile()
    {
        for (var spots = KenoTicket.MinimumNumbers; spots <= KenoTicket.MaximumNumbers; spots++)
        {
            var probabilities = Enumerable.Range(0, spots + 1)
                .ToDictionary(hits => hits, hits => ProbabilityOfHits(spots, hits));
            var tiers = StandardKenoPaytable.Tiers.Where(tier => tier.Spots == spots).ToArray();
            var returnRate = tiers.Sum(tier => probabilities[tier.Hits] * (double)tier.Multiplier);
            var paidRoundRate = tiers.Sum(tier => probabilities[tier.Hits]);

            Assert.InRange(returnRate, 0.62, 0.67);
            Assert.InRange(paidRoundRate, 0.06, 0.26);
        }

        var tenSpotProbabilities = Enumerable.Range(0, 11)
            .ToDictionary(hits => hits, hits => ProbabilityOfHits(10, hits));
        var tenSpotPaidRate = StandardKenoPaytable.Tiers
            .Where(tier => tier.Spots == 10)
            .Sum(tier => tenSpotProbabilities[tier.Hits]);

        Assert.InRange(1 / tenSpotPaidRate, 9.05, 9.06);
        Assert.InRange(1 / tenSpotProbabilities[10], 8_911_710, 8_911_712);
    }

    [Fact]
    public void LegacyPaytableRemainsAvailableForPreviouslySettledRounds()
    {
        Assert.True(StandardKenoPaytable.Supports(StandardKenoPaytable.PriorId));
        Assert.True(StandardKenoPaytable.Supports(StandardKenoPaytable.LegacyId));
        Assert.Equal(1m, StandardKenoPaytable.MultiplierFor(StandardKenoPaytable.PriorId, 10, 3));
        Assert.Equal(5m, StandardKenoPaytable.MultiplierFor(StandardKenoPaytable.LegacyId, 10, 0));
        Assert.Equal(2m, StandardKenoPaytable.MultiplierFor(StandardKenoPaytable.LegacyId, 1, 1));
    }

    private static double ProbabilityOfHits(int spots, int hits) =>
        Choose(spots, hits) * Choose(KenoTicket.MaximumNumber - spots, KenoDraw.DrawCount - hits) /
        Choose(KenoTicket.MaximumNumber, KenoDraw.DrawCount);

    private static double Choose(int count, int selected)
    {
        if (selected < 0 || selected > count) return 0;
        selected = Math.Min(selected, count - selected);
        var result = 1d;
        for (var index = 1; index <= selected; index++)
            result = result * (count - selected + index) / index;
        return result;
    }
}
