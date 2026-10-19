using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.Tests;

public sealed class SolitaireScoreRulesTests
{
    [Theory]
    [InlineData(0, 200)]
    [InlineData(1, 200)]
    [InlineData(2_999, 200)]
    [InlineData(3_000, 199)]
    [InlineData(599_999, 1)]
    [InlineData(600_000, 0)]
    [InlineData(900_000, 0)]
    [InlineData(-1, 200)]
    public void WinBonusDropsOnePointForEachFullThreeSecondsOfActivePlay(long elapsedMilliseconds, int expected)
    {
        Assert.Equal(expected, SolitaireScoreRules.WinBonus(elapsedMilliseconds));
    }

    [Fact]
    public void FinalScoreAddsTimeBonusOnlyForAWinAndDoesNotMutateCardScore()
    {
        var inProgress = SolitaireEngine.CreateGame(42) with { Score = 250 };
        var won = inProgress with
        {
            Foundations = [
                Foundation("clubs"),
                Foundation("diamonds"),
                Foundation("hearts"),
                Foundation("spades")
            ]
        };

        Assert.Equal(250, SolitaireScoreRules.FinalScore(inProgress, 0));
        Assert.Equal(450, SolitaireScoreRules.FinalScore(won, 0));
        Assert.Equal(350, SolitaireScoreRules.FinalScore(won, 300_000));
        Assert.Equal(250, SolitaireScoreRules.FinalScore(won, 600_000));
        Assert.Equal(250, won.Score);
    }

    private static IReadOnlyList<SolitaireCard> Foundation(string suit) =>
        Enumerable.Range(1, 13)
            .Select(rank => new SolitaireCard($"{suit}-{rank}", suit, rank, true))
            .ToArray();
}
