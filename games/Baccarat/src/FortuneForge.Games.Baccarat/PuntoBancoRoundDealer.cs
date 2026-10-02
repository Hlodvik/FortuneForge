using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Baccarat;

public sealed record PuntoBancoRoundResult(
    BaccaratHand PlayerHand,
    BaccaratHand BankerHand,
    BaccaratRoundOutcome Outcome,
    bool EndedOnNatural,
    ImmutableArray<PlayingCard> ConsumedCards)
{
    public int CardsConsumed => ConsumedCards.Length;
}

public static class PuntoBancoRoundDealer
{
    public static PuntoBancoRoundResult Deal(IReadOnlyList<PlayingCard> orderedShoe)
    {
        ArgumentNullException.ThrowIfNull(orderedShoe);
        EnsureAvailable(orderedShoe, 4);

        var playerCards = new List<PlayingCard> { orderedShoe[0], orderedShoe[2] };
        var bankerCards = new List<PlayingCard> { orderedShoe[1], orderedShoe[3] };
        var playerInitialHand = new BaccaratHand(playerCards);
        var bankerInitialHand = new BaccaratHand(bankerCards);
        var nextCardIndex = 4;
        var endedOnNatural = playerInitialHand.IsNatural || bankerInitialHand.IsNatural;

        if (!endedOnNatural && PuntoBancoTableau.ShouldPlayerDraw(playerInitialHand, bankerInitialHand))
        {
            EnsureAvailable(orderedShoe, nextCardIndex + 1);
            playerCards.Add(orderedShoe[nextCardIndex++]);
        }

        if (!endedOnNatural)
        {
            int? playerThirdCardPoint = playerCards.Count == 3
                ? BaccaratCardPoints.Value(playerCards[2])
                : null;
            if (PuntoBancoTableau.ShouldBankerDraw(playerInitialHand, bankerInitialHand, playerThirdCardPoint))
            {
                EnsureAvailable(orderedShoe, nextCardIndex + 1);
                bankerCards.Add(orderedShoe[nextCardIndex++]);
            }
        }

        var playerHand = new BaccaratHand(playerCards);
        var bankerHand = new BaccaratHand(bankerCards);
        return new PuntoBancoRoundResult(
            playerHand,
            bankerHand,
            OutcomeFor(playerHand, bankerHand),
            endedOnNatural,
            [.. orderedShoe.Take(nextCardIndex)]);
    }

    private static void EnsureAvailable(IReadOnlyCollection<PlayingCard> orderedShoe, int requiredCount)
    {
        if (orderedShoe.Count < requiredCount)
            throw new ArgumentException($"The ordered shoe requires at least {requiredCount} cards for this round.", nameof(orderedShoe));
    }

    private static BaccaratRoundOutcome OutcomeFor(BaccaratHand playerHand, BaccaratHand bankerHand) =>
        playerHand.Total.CompareTo(bankerHand.Total) switch
        {
            > 0 => BaccaratRoundOutcome.Player,
            < 0 => BaccaratRoundOutcome.Banker,
            _ => BaccaratRoundOutcome.Tie,
        };
}
