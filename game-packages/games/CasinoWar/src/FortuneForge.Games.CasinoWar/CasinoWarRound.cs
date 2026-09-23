using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.CasinoWar;

public enum CasinoWarRoundPhase
{
    AwaitingTieDecision,
    Completed,
}

public enum CasinoWarTieDecision
{
    Surrender,
    GoToWar,
}

public enum CasinoWarSettlementDisposition
{
    Win,
    Loss,
    Surrender,
}

public enum CasinoWarRoundOutcome
{
    PlayerOpeningWin,
    DealerOpeningWin,
    PlayerSurrendered,
    PlayerWarWin,
    DealerWarWin,
    PlayerWarTieWin,
}

public sealed record CasinoWarSettlement(
    CasinoWarSettlementDisposition Disposition,
    CasinoWarRoundOutcome Outcome,
    decimal TotalWagered,
    decimal TotalReturn)
{
    public decimal Profit => TotalReturn - TotalWagered;
}

public sealed record CasinoWarRound
{
    internal CasinoWarRound(
        ImmutableArray<PlayingCard> orderedShoe,
        decimal primaryStake,
        CasinoWarOpeningResult opening,
        PlayingCard? playerWarCard,
        PlayingCard? dealerWarCard,
        CasinoWarTieDecision? tieDecision,
        CasinoWarRoundPhase phase,
        CasinoWarSettlement? settlement,
        ImmutableArray<PlayingCard> consumedCards)
    {
        OrderedShoe = orderedShoe;
        PrimaryStake = primaryStake;
        Opening = opening;
        PlayerWarCard = playerWarCard;
        DealerWarCard = dealerWarCard;
        TieDecision = tieDecision;
        Phase = phase;
        Settlement = settlement;
        ConsumedCards = consumedCards;
    }

    internal ImmutableArray<PlayingCard> OrderedShoe { get; }

    public decimal PrimaryStake { get; }

    public CasinoWarOpeningResult Opening { get; }

    public PlayingCard PlayerOpeningCard => Opening.PlayerCard;

    public PlayingCard DealerOpeningCard => Opening.DealerCard;

    public PlayingCard? PlayerWarCard { get; }

    public PlayingCard? DealerWarCard { get; }

    public CasinoWarTieDecision? TieDecision { get; }

    public CasinoWarRoundPhase Phase { get; }

    public CasinoWarSettlement? Settlement { get; }

    public ImmutableArray<PlayingCard> ConsumedCards { get; }

    public int CardsConsumed => ConsumedCards.Length;
}

public static class CasinoWarRoundEngine
{
    public static CasinoWarRound Start(IReadOnlyList<PlayingCard> orderedShoe, decimal primaryStake)
    {
        ArgumentNullException.ThrowIfNull(orderedShoe);
        if (primaryStake <= 0m)
            throw new ArgumentOutOfRangeException(nameof(primaryStake), "A Casino War primary stake must be positive.");

        var shoe = orderedShoe.ToImmutableArray();
        var opening = CasinoWarOpeningDealer.Deal(shoe);
        var settlement = OpeningSettlement(opening.Outcome, primaryStake);

        return new CasinoWarRound(
            shoe,
            primaryStake,
            opening,
            playerWarCard: null,
            dealerWarCard: null,
            tieDecision: null,
            settlement is null ? CasinoWarRoundPhase.AwaitingTieDecision : CasinoWarRoundPhase.Completed,
            settlement,
            opening.ConsumedCards);
    }

    public static CasinoWarRound Decide(CasinoWarRound round, CasinoWarTieDecision decision)
    {
        ArgumentNullException.ThrowIfNull(round);
        ValidateDecision(decision);
        if (round.Phase != CasinoWarRoundPhase.AwaitingTieDecision || round.Opening.Outcome != CasinoWarOpeningOutcome.TieDecisionRequired)
            throw new InvalidOperationException("A Casino War tie decision is only valid once after an opening tie.");

        return decision switch
        {
            CasinoWarTieDecision.Surrender => CompleteSurrender(round),
            CasinoWarTieDecision.GoToWar => CompleteWar(round),
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
    }

    private static CasinoWarSettlement? OpeningSettlement(CasinoWarOpeningOutcome outcome, decimal stake) => outcome switch
    {
        CasinoWarOpeningOutcome.PlayerWin => new(
            CasinoWarSettlementDisposition.Win,
            CasinoWarRoundOutcome.PlayerOpeningWin,
            stake,
            stake * 2m),
        CasinoWarOpeningOutcome.DealerWin => new(
            CasinoWarSettlementDisposition.Loss,
            CasinoWarRoundOutcome.DealerOpeningWin,
            stake,
            0m),
        CasinoWarOpeningOutcome.TieDecisionRequired => null,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static CasinoWarRound CompleteSurrender(CasinoWarRound round) => new(
        round.OrderedShoe,
        round.PrimaryStake,
        round.Opening,
        playerWarCard: null,
        dealerWarCard: null,
        CasinoWarTieDecision.Surrender,
        CasinoWarRoundPhase.Completed,
        new CasinoWarSettlement(
            CasinoWarSettlementDisposition.Surrender,
            CasinoWarRoundOutcome.PlayerSurrendered,
            round.PrimaryStake,
            round.PrimaryStake / 2m),
        round.ConsumedCards);

    private static CasinoWarRound CompleteWar(CasinoWarRound round)
    {
        const int WarCardsRequired = 10;
        if (round.OrderedShoe.Length < WarCardsRequired)
            throw new ArgumentException($"The ordered shoe requires at least {WarCardsRequired} cards to go to war.", nameof(round));

        var playerWarCard = round.OrderedShoe[5];
        var dealerWarCard = round.OrderedShoe[9];
        var settlement = WarSettlement(playerWarCard, dealerWarCard, round.PrimaryStake);

        return new CasinoWarRound(
            round.OrderedShoe,
            round.PrimaryStake,
            round.Opening,
            playerWarCard,
            dealerWarCard,
            CasinoWarTieDecision.GoToWar,
            CasinoWarRoundPhase.Completed,
            settlement,
            [.. round.OrderedShoe.Take(WarCardsRequired)]);
    }

    private static CasinoWarSettlement WarSettlement(PlayingCard playerCard, PlayingCard dealerCard, decimal stake) =>
        CasinoWarRankStrength.Value(playerCard).CompareTo(CasinoWarRankStrength.Value(dealerCard)) switch
        {
            > 0 => new(
                CasinoWarSettlementDisposition.Win,
                CasinoWarRoundOutcome.PlayerWarWin,
                stake * 2m,
                stake * 3m),
            < 0 => new(
                CasinoWarSettlementDisposition.Loss,
                CasinoWarRoundOutcome.DealerWarWin,
                stake * 2m,
                0m),
            _ => new(
                CasinoWarSettlementDisposition.Win,
                CasinoWarRoundOutcome.PlayerWarTieWin,
                stake * 2m,
                stake * 4m),
        };

    private static void ValidateDecision(CasinoWarTieDecision decision)
    {
        if (decision is not CasinoWarTieDecision.Surrender and not CasinoWarTieDecision.GoToWar)
            throw new ArgumentOutOfRangeException(nameof(decision));
    }
}
