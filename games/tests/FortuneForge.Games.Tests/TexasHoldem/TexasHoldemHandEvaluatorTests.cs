using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Games.Tests;

public sealed class TexasHoldemHandEvaluatorTests
{
    [Fact]
    public void ModuleDescriptorPreservesTheExistingApplicationBoundary()
    {
        Assert.Equal("texas-holdem", TexasHoldemModule.Descriptor.Id);
        Assert.Equal("/cards/texas-holdem", TexasHoldemModule.Descriptor.ClientRoute);
        Assert.Equal("/api/cards/texas-holdem/credit", TexasHoldemModule.Descriptor.ApiBasePath);
    }

    [Fact]
    public void DeterministicDeckPreservesTheExistingStream()
    {
        var first = TexasHoldemRules.CreateDeck(12345);
        var second = TexasHoldemRules.CreateDeck(12345);

        Assert.Equal(first, second);
        Assert.Equal(52, first.Count);
        Assert.Equal(52, first.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EvaluatorPreservesCanonicalServerOrdering()
    {
        var straightFlush = TexasHoldemHandEvaluator.Evaluate(
            ["A|spades", "K|spades", "Q|spades", "J|spades", "10|spades", "2|clubs", "3|diamonds"]);
        var quads = TexasHoldemHandEvaluator.Evaluate(
            ["A|spades", "A|hearts", "A|diamonds", "A|clubs", "K|spades", "2|clubs", "3|diamonds"]);
        var fullHouse = TexasHoldemHandEvaluator.Evaluate(
            ["K|spades", "K|hearts", "K|diamonds", "Q|clubs", "Q|spades", "2|clubs", "3|diamonds"]);

        Assert.Equal((8, "straight-flush"), (straightFlush.Category, straightFlush.Name));
        Assert.Equal((7, "four-of-a-kind"), (quads.Category, quads.Name));
        Assert.Equal((6, "full-house"), (fullHouse.Category, fullHouse.Name));
        Assert.True(straightFlush.Score > quads.Score && quads.Score > fullHouse.Score);
    }

    [Fact]
    public void EvaluatorTreatsAceAsLowOnlyForTheWheel()
    {
        var wheel = TexasHoldemHandEvaluator.Evaluate(
            ["A|spades", "2|hearts", "3|diamonds", "4|clubs", "5|spades", "K|clubs", "Q|diamonds"]);
        var sixHigh = TexasHoldemHandEvaluator.Evaluate(
            ["2|spades", "3|hearts", "4|diamonds", "5|clubs", "6|spades", "K|clubs", "Q|diamonds"]);

        Assert.Equal("straight", wheel.Name);
        Assert.True(sixHigh.Score > wheel.Score);
    }

    [Fact]
    public void EvaluatorRejectsAnInvalidCardCount()
    {
        Assert.Throws<ArgumentException>(() => TexasHoldemHandEvaluator.Evaluate(
            ["A|spades", "K|spades", "Q|spades", "J|spades"]));
    }
}
