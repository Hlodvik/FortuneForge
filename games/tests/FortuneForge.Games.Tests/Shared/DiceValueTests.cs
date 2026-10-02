using FortuneForge.Games.Dice;

namespace FortuneForge.Games.Tests;

public sealed class DiceValueTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void StandardDieRejectsValuesOutsideOneThroughSix(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DieValue(value));
    }

    [Fact]
    public void DicePairExposesTotalAndPairStatus()
    {
        var dice = new DicePair(new DieValue(4), new DieValue(4));

        Assert.Equal(8, dice.Total);
        Assert.True(dice.IsPair);
    }

    [Fact]
    public void DefaultStructValuesFailBoundaryValidation()
    {
        Assert.Throws<InvalidOperationException>(() => default(DieValue).Validate());
        Assert.Throws<InvalidOperationException>(() => default(DicePair).Validate());
    }
}
