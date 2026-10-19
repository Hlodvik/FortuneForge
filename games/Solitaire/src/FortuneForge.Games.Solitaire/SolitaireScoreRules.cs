namespace FortuneForge.Games.Solitaire;

internal static class SolitaireScoreRules
{
    private const long BonusWindowMilliseconds = 10 * 60 * 1000;
    private const long MillisecondsPerBonusPoint = 3 * 1000;

    public static int WinBonus(long elapsedMilliseconds)
    {
        var activeMilliseconds = Math.Clamp(elapsedMilliseconds, 0, BonusWindowMilliseconds);
        return (int)((BonusWindowMilliseconds - activeMilliseconds + MillisecondsPerBonusPoint - 1)
            / MillisecondsPerBonusPoint);
    }

    public static int FinalScore(SolitaireGameState game, long elapsedMilliseconds) =>
        SolitaireEngine.IsWon(game)
            ? checked(game.Score + WinBonus(elapsedMilliseconds))
            : game.Score;
}
