using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Baccarat;

public sealed record BaccaratComputeRequest(IReadOnlyList<PlayingCard> OrderedCards, BaccaratBetSide BetSide,
    decimal Stake, decimal PlayerPairStake, decimal BankerPairStake);

public sealed record BaccaratComputeRound(IReadOnlyList<PlayingCard> PlayerCards, IReadOnlyList<PlayingCard> BankerCards,
    BaccaratRoundOutcome Outcome, bool EndedOnNatural, IReadOnlyList<PlayingCard> ConsumedCards)
{
    public PuntoBancoRoundResult ToRound() => new(new BaccaratHand(PlayerCards), new BaccaratHand(BankerCards),
        Outcome, EndedOnNatural, ConsumedCards.ToImmutableArray());
}

public sealed record BaccaratComputeResult(BaccaratComputeRound Round, BaccaratBetSettlement MainBet,
    BaccaratPairSettlement PlayerPair, BaccaratPairSettlement BankerPair);

public static class BaccaratCompute
{
    public const string ResolveRoute = "/internal/v1/rounds/resolve";

    public static BaccaratComputeResult Resolve(BaccaratComputeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.OrderedCards);
        if (request.OrderedCards.Count is < 4 or > 6 ||
            request.OrderedCards.Any(card => !Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit)))
            throw new ArgumentException("A Baccarat deal requires four to six valid ordered cards.");

        var round = PuntoBancoRoundDealer.Deal(request.OrderedCards);
        return new(new(round.PlayerHand.Cards, round.BankerHand.Cards, round.Outcome, round.EndedOnNatural, round.ConsumedCards),
            PuntoBancoPaytable.Settle(request.BetSide, request.Stake, round),
            PuntoBancoPaytable.SettlePair(request.PlayerPairStake, round.PlayerHand),
            PuntoBancoPaytable.SettlePair(request.BankerPairStake, round.BankerHand));
    }
}
