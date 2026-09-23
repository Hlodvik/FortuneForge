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
}
