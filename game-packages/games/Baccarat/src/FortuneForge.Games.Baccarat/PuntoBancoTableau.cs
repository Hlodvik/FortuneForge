namespace FortuneForge.Games.Baccarat;

public static class PuntoBancoTableau
{
    public static bool ShouldPlayerDraw(BaccaratHand playerInitialHand, BaccaratHand bankerInitialHand)
    {
        ValidateInitialHands(playerInitialHand, bankerInitialHand);
        return !HasNatural(playerInitialHand, bankerInitialHand) && playerInitialHand.Total <= 5;
    }

    public static bool ShouldBankerDraw(
        BaccaratHand playerInitialHand,
        BaccaratHand bankerInitialHand,
        int? playerThirdCardPoint = null)
    {
        ValidateInitialHands(playerInitialHand, bankerInitialHand);
        if (playerThirdCardPoint is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(playerThirdCardPoint), "A Baccarat card point must be from zero through nine.");
        if (HasNatural(playerInitialHand, bankerInitialHand))
        {
            if (playerThirdCardPoint is not null)
                throw new ArgumentException("A player third-card point is invalid when either initial hand is natural.", nameof(playerThirdCardPoint));

            return false;
        }

        if (playerInitialHand.Total is 6 or 7)
        {
            if (playerThirdCardPoint is not null)
                throw new ArgumentException("A player third-card point is invalid when the player stands.", nameof(playerThirdCardPoint));

            return bankerInitialHand.Total <= 5;
        }

        if (playerThirdCardPoint is null)
            throw new ArgumentException("A player third-card point is required when the player draws.", nameof(playerThirdCardPoint));

        return bankerInitialHand.Total switch
        {
            <= 2 => true,
            3 => playerThirdCardPoint != 8,
            4 => playerThirdCardPoint is >= 2 and <= 7,
            5 => playerThirdCardPoint is >= 4 and <= 7,
            6 => playerThirdCardPoint is 6 or 7,
            7 => false,
            _ => throw new ArgumentOutOfRangeException(nameof(bankerInitialHand), "A non-natural Baccarat banker total must be from zero through seven."),
        };
    }

    private static void ValidateInitialHands(BaccaratHand playerInitialHand, BaccaratHand bankerInitialHand)
    {
        ArgumentNullException.ThrowIfNull(playerInitialHand);
        ArgumentNullException.ThrowIfNull(bankerInitialHand);
        if (playerInitialHand.Cards.Length != 2)
            throw new ArgumentException("The player initial hand must contain exactly two cards.", nameof(playerInitialHand));
        if (bankerInitialHand.Cards.Length != 2)
            throw new ArgumentException("The banker initial hand must contain exactly two cards.", nameof(bankerInitialHand));
    }

    private static bool HasNatural(BaccaratHand playerInitialHand, BaccaratHand bankerInitialHand) =>
        playerInitialHand.IsNatural || bankerInitialHand.IsNatural;
}
