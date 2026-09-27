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
    [InlineData(1, 1, 4)]
    [InlineData(5, 2, 2)]
    [InlineData(5, 4, 8)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 3, 1)]
    [InlineData(10, 5, 4)]
    [InlineData(10, 10, 100_000)]
    [InlineData(8, 2, 0)]
    public void StandardPaytableReturnsThePublishedMultiplier(int spots, int hits, int expectedMultiplier)
    {
        var ticket = new KenoTicket(Enumerable.Range(1, spots));

        var outcome = StandardKenoPaytable.Instance.Evaluate(ticket, hits);

        Assert.Equal(StandardKenoPaytable.Id, outcome.PaytableId);
        Assert.Equal(expectedMultiplier, outcome.Value);
    }

    [Fact]
    public void StandardPaytableKeepsReturnRateAndBlankStreaksInThePublishedRange()
    {
        for (var spots = KenoTicket.MinimumNumbers; spots <= KenoTicket.MaximumNumbers; spots++)
        {
            var probabilities = Enumerable.Range(0, spots + 1)
                .ToDictionary(hits => hits, hits => ProbabilityOfHits(spots, hits));
            var tiers = StandardKenoPaytable.Tiers.Where(tier => tier.Spots == spots).ToArray();
            var returnRate = tiers.Sum(tier => probabilities[tier.Hits] * tier.Multiplier);
            var paidRoundRate = tiers.Sum(tier => probabilities[tier.Hits]);

            Assert.InRange(returnRate, 0.89, 1.00);
            Assert.InRange(paidRoundRate, 0.25, 1.00);
            Assert.InRange(Math.Pow(1 - paidRoundRate, 20), 0, 0.004);
        }
    }

    [Fact]
    public void LegacyPaytableRemainsAvailableForPreviouslySettledRounds()
    {
        Assert.True(StandardKenoPaytable.Supports(StandardKenoPaytable.LegacyId));
        Assert.Equal(5, StandardKenoPaytable.MultiplierFor(StandardKenoPaytable.LegacyId, 10, 0));
        Assert.Equal(2, StandardKenoPaytable.MultiplierFor(StandardKenoPaytable.LegacyId, 1, 1));
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
