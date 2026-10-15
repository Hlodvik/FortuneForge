using FortuneForge.Games.Cards;
using FortuneForge.Games.HouseHoldem;

namespace FortuneForge.Server.Cards.HouseHoldem;

internal sealed class HouseHoldemService(
    IHouseHoldemStore store,
    TimeProvider timeProvider,
    Func<IReadOnlyList<string>>? createShuffledDeck = null)
{
    private readonly Func<IReadOnlyList<string>> createShuffledDeck = createShuffledDeck ?? HouseHoldemRules.CreateShuffledDeck;

    public async Task<HouseHoldemRoundResponse> StartAsync(
        string userId, CreateHouseHoldemRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(idempotencyKey);
        var anteCents = HouseHoldemMoney.ToAnteCents(request.Ante);
        var tripsCents = HouseHoldemMoney.ToTripsCents(request.Trips);
        var result = await store.StartAsync(userId, idempotencyKey, anteCents, tripsCents,
            createShuffledDeck(), timeProvider.GetUtcNow(), cancellationToken);
        return ToResponse(result);
    }

    public async Task<HouseHoldemRoundResponse> GetAsync(
        string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        var result = await store.GetAsync(userId, roundId, cancellationToken)
            ?? throw new HouseHoldemRoundNotFoundException();
        return ToResponse(result);
    }

    public async Task<HouseHoldemRoundResponse> ActionAsync(
        string userId, string roundId, HouseHoldemActionRequest request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRoundId(roundId);
        ValidateIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.Action) || !AllActions.Contains(request.Action, StringComparer.Ordinal))
            throw new ArgumentException("Choose a valid House Hold'em action.", nameof(request));
        var result = await store.TransitionAsync(userId, roundId, idempotencyKey,
            request.Action, timeProvider.GetUtcNow(), cancellationToken);
        return ToResponse(result);
    }

    public async Task<HouseHoldemRoundResponse> AdvanceAsync(
        string userId, string roundId, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        ValidateIdempotencyKey(idempotencyKey);
        var result = await store.TransitionAsync(userId, roundId, idempotencyKey,
            HouseHoldemCommands.Advance, timeProvider.GetUtcNow(), cancellationToken);
        return ToResponse(result);
    }

    internal static HouseHoldemRoundResponse ToResponse(HouseHoldemStoreResult result)
    {
        var view = HouseHoldemRules.ToPlayerView(result.Round);
        var settlement = view.Settlement;
        // Only the rules engine's redacted view is used to construct the public response.
        return new HouseHoldemRoundResponse(
            result.RoundId,
            HouseHoldemMoney.ToRand(result.BalanceCents),
            view.Phase,
            HouseHoldemMoney.ToRand(view.AnteCents),
            HouseHoldemMoney.ToRand(view.BlindCents),
            HouseHoldemMoney.ToRand(view.TripsCents),
            HouseHoldemMoney.ToRand(view.PlayWagerCents),
            HouseHoldemMoney.ToRand(view.TotalWagerCents),
            HouseHoldemMoney.ToRand(settlement?.PayoutCents ?? 0),
            view.PlayerCards.Select(card => ToCard(card.Code!)).ToArray(),
            settlement is null ? null : view.DealerCards.Select(card => ToCard(card.Code!)).ToArray(),
            view.CommunityCards.Where(card => !card.Hidden).Select(card => ToCard(card.Code!)).ToArray(),
            settlement?.PlayerHandRank,
            settlement?.DealerHandRank,
            settlement?.Outcome,
            view.LegalActions,
            view.CanAdvance,
            settlement is null ? null : new HouseHoldemPayoutBreakdownResponse(
                HouseHoldemMoney.ToRand(settlement.AntePayoutCents),
                HouseHoldemMoney.ToRand(settlement.BlindPayoutCents),
                HouseHoldemMoney.ToRand(settlement.TripsPayoutCents),
                HouseHoldemMoney.ToRand(settlement.PlayPayoutCents)));
    }

    internal static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException(
                "Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value));
    }

    internal static void ValidateRoundId(string roundId)
    {
        if (roundId is null || roundId.Length != 64 || roundId.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("The House Hold'em round identifier is invalid.", nameof(roundId));
    }

    private static HouseHoldemCardResponse ToCard(string code)
    {
        var card = CardCode.Parse(code);
        return new HouseHoldemCardResponse(
            card.Rank switch
            {
                CardRank.Ace => "ace", CardRank.Two => "two", CardRank.Three => "three",
                CardRank.Four => "four", CardRank.Five => "five", CardRank.Six => "six",
                CardRank.Seven => "seven", CardRank.Eight => "eight", CardRank.Nine => "nine",
                CardRank.Ten => "ten", CardRank.Jack => "jack", CardRank.Queen => "queen",
                CardRank.King => "king", _ => throw new ArgumentOutOfRangeException(nameof(code)),
            },
            card.Suit switch
            {
                CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds",
                CardSuit.Hearts => "hearts", CardSuit.Spades => "spades",
                _ => throw new ArgumentOutOfRangeException(nameof(code)),
            });
    }

    private static readonly string[] AllActions =
    [
        HouseHoldemActions.Check, HouseHoldemActions.Play3X, HouseHoldemActions.Play4X,
        HouseHoldemActions.Play2X, HouseHoldemActions.Play1X, HouseHoldemActions.Fold,
    ];
}

internal static class HouseHoldemCommands
{
    public const string Advance = "advance";
}
