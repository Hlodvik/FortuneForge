using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.CasinoWar;

public sealed record CasinoWarComputeStartRequest(IReadOnlyList<PlayingCard> PrivateCards, decimal PrimaryStake, decimal TieStake);
public sealed record CasinoWarComputeDecisionRequest(IReadOnlyList<PlayingCard> PrivateCards, decimal PrimaryStake, CasinoWarTieDecision Decision);
public sealed record CasinoWarComputeStartResult(CasinoWarComputeRound Round, CasinoWarTieBetSettlement? TieSettlement);

public sealed record CasinoWarComputeRound(decimal PrimaryStake, CasinoWarOpeningResult Opening,
    PlayingCard? PlayerWarCard, PlayingCard? DealerWarCard, CasinoWarTieDecision? TieDecision,
    CasinoWarRoundPhase Phase, CasinoWarSettlement? Settlement, IReadOnlyList<PlayingCard> ConsumedCards)
{
    internal static CasinoWarComputeRound FromRound(CasinoWarRound round) => new(round.PrimaryStake,
        round.Opening, round.PlayerWarCard, round.DealerWarCard, round.TieDecision, round.Phase,
        round.Settlement, round.ConsumedCards);

    public CasinoWarRound VerifyAndRestore(IReadOnlyList<PlayingCard> privateCards, decimal stake, CasinoWarTieDecision? decision)
    {
        var expected = CasinoWarRoundEngine.Start(privateCards, stake);
        if (decision is { } selected) expected = CasinoWarRoundEngine.Decide(expected, selected);
        if (PrimaryStake != expected.PrimaryStake || Opening is null || Opening.ConsumedCards.IsDefault ||
            Opening.PlayerCard != expected.Opening.PlayerCard || Opening.DealerCard != expected.Opening.DealerCard ||
            Opening.Outcome != expected.Opening.Outcome || !Opening.ConsumedCards.SequenceEqual(expected.Opening.ConsumedCards) ||
            PlayerWarCard != expected.PlayerWarCard || DealerWarCard != expected.DealerWarCard ||
            TieDecision != expected.TieDecision || Phase != expected.Phase || Settlement != expected.Settlement ||
            ConsumedCards is null || !ConsumedCards.SequenceEqual(expected.ConsumedCards))
            throw new ArgumentException("The Casino War outcome does not match its admitted cards and wager.");

        return new CasinoWarRound(privateCards.ToImmutableArray(), PrimaryStake, Opening, PlayerWarCard,
            DealerWarCard, TieDecision, Phase, Settlement, ConsumedCards.ToImmutableArray());
    }
}

public static class CasinoWarCompute
{
    public const string StartRoute = "/internal/v1/rounds/start";
    public const string DecideRoute = "/internal/v1/rounds/decide";

    public static CasinoWarComputeStartResult Start(CasinoWarComputeStartRequest request)
    {
        ValidatePrivateCards(request.PrivateCards);
        ArgumentOutOfRangeException.ThrowIfNegative(request.TieStake);
        var round = CasinoWarRoundEngine.Start(request.PrivateCards, request.PrimaryStake);
        return new(CasinoWarComputeRound.FromRound(round), request.TieStake == 0 ? null :
            CasinoWarTieBetPaytable.Settle(round.Opening, request.TieStake));
    }

    public static CasinoWarComputeRound Decide(CasinoWarComputeDecisionRequest request)
    {
        ValidatePrivateCards(request.PrivateCards);
        var round = CasinoWarRoundEngine.Start(request.PrivateCards, request.PrimaryStake);
        if (round.Phase != CasinoWarRoundPhase.AwaitingTieDecision)
            throw new ArgumentException("A Casino War decision requires an opening tie.");
        return CasinoWarComputeRound.FromRound(CasinoWarRoundEngine.Decide(round, request.Decision));
    }

    private static void ValidatePrivateCards(IReadOnlyList<PlayingCard> privateCards)
    {
        ArgumentNullException.ThrowIfNull(privateCards);
        if (privateCards.Count != CasinoWarRoundEngine.WarCardsRequired ||
            privateCards.Any(card => !Enum.IsDefined(card.Rank) || !Enum.IsDefined(card.Suit)))
            throw new ArgumentException("The admitted ten-card Casino War sequence is required.");
    }
}
