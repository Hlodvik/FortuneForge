using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Tests;

public sealed class PlayingCardTests
{
    [Fact]
    public void StandardDeckContainsEveryDistinctCard()
    {
        var deck = StandardDeck.Create();

        Assert.Equal(52, deck.Count);
        Assert.Equal(52, deck.Distinct().Count());
    }

    [Theory]
    [InlineData("A|spades")]
    [InlineData("10|diamonds")]
    [InlineData("K|clubs")]
    public void ExistingWireCodesRoundTrip(string code)
    {
        Assert.Equal(code, CardCode.Parse(code).Code);
    }
}
