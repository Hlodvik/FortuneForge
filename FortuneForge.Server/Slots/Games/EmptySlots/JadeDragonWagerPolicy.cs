namespace FortuneForge.Server.Slots.Games.EmptySlots;

/// <summary>Host admission cap. Historical free-game wagers retain their admitted value.</summary>
public static class JadeDragonWagerPolicy
{
    public const long MaximumPaidWagerPoints = 400; // 400 × R0.25 = R100.
    public static void Validate(string gameId, long wagerPoints, bool useFreeSpin)
    {
        if (gameId == FortuneForge.Games.EmptySlots.EmptySlotsEngine.GameId &&
            !useFreeSpin && wagerPoints > MaximumPaidWagerPoints)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "Jade Dragon's maximum paid bet is R100.");
    }
}
