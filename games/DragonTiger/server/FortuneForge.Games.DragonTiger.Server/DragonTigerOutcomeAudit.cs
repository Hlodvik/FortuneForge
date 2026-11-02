namespace FortuneForge.Games.DragonTiger.Server;

public static class DragonTigerOutcomeAudit
{
    public const string Version = "dragon-tiger-outcome-v1";
    public static bool Verify(DragonTigerStoredRound stored, long actualWagerCents, long actualReturnCents)
    {
        try
        {
            var dragon = stored.Round.DragonCard; var tiger = stored.Round.TigerCard;
            if (!Enum.IsDefined(dragon.Rank) || !Enum.IsDefined(dragon.Suit) || !Enum.IsDefined(tiger.Rank) || !Enum.IsDefined(tiger.Suit)) return false;
            var wagers = new[] { stored.Wagers.DragonCents, stored.Wagers.TigerCents, stored.Wagers.TieCents };
            if (wagers.Any(wager => wager < 0 || wager % 2 != 0) || checked(wagers.Sum()) != actualWagerCents) return false;
            var comparison = ((int)dragon.Rank).CompareTo((int)tiger.Rank);
            var expected = comparison > 0 ? checked(wagers[0] * 2) : comparison < 0 ? checked(wagers[1] * 2)
                : checked(wagers[0] / 2 + wagers[1] / 2 + wagers[2] * 9);
            return actualReturnCents == expected && stored.ShoeCardsUsed is >= 2 and <= 312 && stored.ShoeCardsUsed % 2 == 0
                && stored.ShoeCardsRemaining == 416 - stored.ShoeCardsUsed;
        }
        catch (ArithmeticException) { return false; }
    }
}
