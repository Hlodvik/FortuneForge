using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.CasinoWar;

public enum CasinoWarOpeningOutcome
{
    PlayerWin,
    DealerWin,
    TieDecisionRequired,
}

public static class CasinoWarRankStrength
{
    public static int Value(PlayingCard card) => card.Rank switch
    {
        >= CardRank.Two and <= CardRank.King => (int)card.Rank,
        CardRank.Ace => 14,
        _ => throw new ArgumentOutOfRangeException(nameof(card)),
    };
}

public sealed record CasinoWarOpeningResult(
    PlayingCard PlayerCard,
    PlayingCard DealerCard,
    CasinoWarOpeningOutcome Outcome,
    ImmutableArray<PlayingCard> ConsumedCards)
{
    public int CardsConsumed => ConsumedCards.Length;
}

public static class CasinoWarOpeningDealer
{
    public static CasinoWarOpeningResult Deal(IReadOnlyList<PlayingCard> orderedShoe)
    {
        ArgumentNullException.ThrowIfNull(orderedShoe);
        if (orderedShoe.Count < 2)
            throw new ArgumentException("The ordered shoe requires at least two cards for an opening Casino War deal.", nameof(orderedShoe));

        var playerCard = orderedShoe[0];
        var dealerCard = orderedShoe[1];
        return new CasinoWarOpeningResult(
            playerCard,
            dealerCard,
            OutcomeFor(playerCard, dealerCard),
            [playerCard, dealerCard]);
    }

    private static CasinoWarOpeningOutcome OutcomeFor(PlayingCard playerCard, PlayingCard dealerCard) =>
        CasinoWarRankStrength.Value(playerCard).CompareTo(CasinoWarRankStrength.Value(dealerCard)) switch
        {
            > 0 => CasinoWarOpeningOutcome.PlayerWin,
            < 0 => CasinoWarOpeningOutcome.DealerWin,
            _ => CasinoWarOpeningOutcome.TieDecisionRequired,
        };
}
