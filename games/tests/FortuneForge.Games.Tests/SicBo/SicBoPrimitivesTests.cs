using FortuneForge.Games.SicBo;

namespace FortuneForge.Games.Tests.SicBo;

public sealed class SicBoPrimitivesTests
{
    [Fact]
    public void RollPreservesOrderAndExposesTotalTripleAndFaceOccurrences()
    {
        var roll = new SicBoRoll(4, 4, 4);

        Assert.Equal([4, 4, 4], roll.Values.ToArray());
        Assert.Equal(12, roll.Total);
        Assert.True(roll.IsTriple);
        Assert.Equal(3, roll.OccurrencesOf(4));
        Assert.Equal(0, roll.OccurrencesOf(1));
    }

    [Fact]
    public void RollCanBeNonTripleAndCountASingleFace()
    {
        var roll = new SicBoRoll([1, 3, 6]);

        Assert.False(roll.IsTriple);
        Assert.Equal(10, roll.Total);
        Assert.Equal(1, roll.OccurrencesOf(3));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 7)]
    public void RollRejectsOutOfRangeDieValues(int first, int second, int third) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SicBoRoll(first, second, third));

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 2, 3, 4)]
    public void RollRequiresExactlyThreeValues(params int[] values) =>
        Assert.Throws<ArgumentException>(() => new SicBoRoll(values));

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void OccurrencesRejectsAnInvalidFace(int face) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SicBoRoll(1, 2, 3).OccurrencesOf(face));

    [Theory]
    [InlineData(SicBoBetKind.Small)]
    [InlineData(SicBoBetKind.Big)]
    [InlineData(SicBoBetKind.Odd)]
    [InlineData(SicBoBetKind.Even)]
    [InlineData(SicBoBetKind.AnyTriple)]
    public void NoSelectionBetsCarryOnlyTheirKindAndStake(SicBoBetKind kind)
    {
        var bet = SicBoBet.CreateWithoutSelection(kind, 2.5m);

        Assert.Equal(kind, bet.Kind);
        Assert.Equal(2.5m, bet.Stake);
        Assert.Null(bet.Face);
        Assert.Null(bet.Total);
        Assert.Null(bet.Combination);
    }

    [Theory]
    [InlineData(SicBoBetKind.SingleNumber)]
    [InlineData(SicBoBetKind.SpecificDouble)]
    [InlineData(SicBoBetKind.SpecificTriple)]
    public void FaceBetsCarryOnlyTheirSelectedFace(SicBoBetKind kind)
    {
        var bet = kind switch
        {
            SicBoBetKind.SingleNumber => SicBoBet.SingleNumber(3m, 6),
            SicBoBetKind.SpecificDouble => SicBoBet.SpecificDouble(3m, 6),
            SicBoBetKind.SpecificTriple => SicBoBet.SpecificTriple(3m, 6),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(kind, bet.Kind);
        Assert.Equal(6, bet.Face);
        Assert.Null(bet.Total);
        Assert.Null(bet.Combination);
    }

    [Fact]
    public void TotalBetCarriesOnlyATotalWithinTheAllowedRange()
    {
        var bet = SicBoBet.ForTotal(4m, 17);

        Assert.Equal(SicBoBetKind.Total, bet.Kind);
        Assert.Equal(17, bet.Total);
        Assert.Null(bet.Face);
        Assert.Null(bet.Combination);
    }

    [Fact]
    public void TwoNumberCombinationCanonicalizesDistinctFacesAscending()
    {
        var bet = SicBoBet.TwoNumberCombination(5m, 6, 2);

        Assert.Equal(SicBoBetKind.TwoNumberCombination, bet.Kind);
        Assert.Equal(2, bet.Combination!.Value.FirstFace);
        Assert.Equal(6, bet.Combination.Value.SecondFace);
        Assert.Null(bet.Face);
        Assert.Null(bet.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void BetFactoriesRejectNonPositiveStakes(decimal stake)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.Small(stake));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.SingleNumber(stake, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.ForTotal(stake, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.TwoNumberCombination(stake, 1, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void FaceBetsRejectInvalidFaces(int face)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.SingleNumber(1m, face));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.SpecificDouble(1m, face));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.SpecificTriple(1m, face));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(18)]
    public void TotalBetRejectsOutOfRangeTotals(int total) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.ForTotal(1m, total));

    [Fact]
    public void CombinationRejectsDuplicateAndOutOfRangeFaces()
    {
        Assert.Throws<ArgumentException>(() => SicBoBet.TwoNumberCombination(1m, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.TwoNumberCombination(1m, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.TwoNumberCombination(1m, 2, 7));
    }

    [Fact]
    public void NoSelectionFactoryRejectsKindsThatNeedASelectionAndMalformedKinds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.CreateWithoutSelection(SicBoBetKind.Total, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoBet.CreateWithoutSelection((SicBoBetKind)99, 1m));
    }
}
