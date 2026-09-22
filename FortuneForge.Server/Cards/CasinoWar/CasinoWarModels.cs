using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Cards.CasinoWar;

public sealed record CreateCasinoWarRoundRequest(decimal? PrimaryStake, decimal? TieStake);
public sealed record DecideCasinoWarRoundRequest(string? Decision);
public sealed record CasinoWarStatusResponse(bool Available, decimal MinimumPrimaryStake, decimal MaximumPrimaryStake, decimal StakeIncrement, decimal MaximumTieStake, decimal Balance, string Mode);
public sealed record CasinoWarCardResponse(string Rank, string Suit);
public sealed record CasinoWarPrimarySettlementResponse(string Disposition, string Outcome, decimal TotalWagered, decimal TotalReturn, decimal Profit);
public sealed record CasinoWarTieSettlementResponse(bool Won, string Disposition, string Outcome, decimal Stake, decimal TotalReturn, decimal Profit);
public sealed record CasinoWarRoundResponse(string RoundId, decimal Balance, decimal PrimaryStake, decimal TieStake, string Phase, CasinoWarCardResponse PlayerOpeningCard, CasinoWarCardResponse DealerOpeningCard, string? Decision, CasinoWarCardResponse? PlayerWarCard, CasinoWarCardResponse? DealerWarCard, CasinoWarPrimarySettlementResponse? PrimarySettlement, CasinoWarTieSettlementResponse? TieSettlement);
public sealed record CasinoWarErrorResponse(string Code, string Message);

internal sealed record CasinoWarStoreRound(string RoundId, string UserId, CasinoWarRound Round, CasinoWarTieBetSettlement? TieSettlement, IReadOnlyList<PlayingCard> Deck);
internal sealed record CasinoWarStoreResult(CasinoWarStoreRound Round, long BalanceCents);
internal sealed class CasinoWarRoundNotFoundException() : Exception("Casino War round not found.");
internal sealed class CasinoWarRoundConflictException(string message) : Exception(message);
internal sealed class CasinoWarInsufficientCreditsException(long availableCents, long requiredCents) : Exception($"This account has R{RandMoney.CentsToRand(availableCents):0.00}, but the wager requires R{RandMoney.CentsToRand(requiredCents):0.00}.");

internal static class CasinoWarMoney
{
    public const long MinimumPrimaryStakeCents = 100;
    public const long MaximumPrimaryStakeCents = 10_000;
    public const long MaximumTieStakeCents = 2_500;
    public const long StakeIncrementCents = 100;
    public static long PrimaryCents(decimal? value) => StakeCents(value, MinimumPrimaryStakeCents, MaximumPrimaryStakeCents, "primary");
    public static long TieCents(decimal? value) => StakeCents(value, 0, MaximumTieStakeCents, "Tie");
    public static decimal ToRand(long cents) => RandMoney.CentsToRand(cents);
    private static long StakeCents(decimal? value, long minimum, long maximum, string label)
    {
        if (value is null) throw new ArgumentException($"A {label} stake is required.");
        var cents = checked(value.Value * RandMoney.CentsPerRand);
        if (cents != decimal.Truncate(cents)) throw new ArgumentOutOfRangeException(nameof(value), $"A Casino War {label} stake cannot include a fraction of a cent.");
        var result = checked((long)cents);
        if (result < minimum || result > maximum || result % StakeIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(value), $"The Casino War {label} stake is outside the allowed whole-rand range.");
        return result;
    }
}

internal interface ICasinoWarStore
{
    Task<CasinoWarStoreResult> StartAsync(string userId, string idempotencyKey, long primaryStakeCents, long tieStakeCents, IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<CasinoWarStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
    Task<CasinoWarStoreResult> DecideAsync(string userId, string roundId, string idempotencyKey, CasinoWarTieDecision decision, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

internal sealed class CasinoWarService(ICasinoWarStore store, TimeProvider timeProvider, Func<IReadOnlyList<PlayingCard>>? createShuffledShoe = null)
{
    private readonly Func<IReadOnlyList<PlayingCard>> createShuffledShoe = createShuffledShoe ?? CreateShuffledShoe;
    public async Task<CasinoWarRoundResponse> StartAsync(string userId, CreateCasinoWarRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ValidateKey(idempotencyKey);
        var result = await store.StartAsync(userId, idempotencyKey, CasinoWarMoney.PrimaryCents(request.PrimaryStake), CasinoWarMoney.TieCents(request.TieStake), createShuffledShoe(), timeProvider.GetUtcNow(), cancellationToken);
        return ToResponse(result);
    }
    public async Task<CasinoWarRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId); return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new CasinoWarRoundNotFoundException());
    }
    public async Task<CasinoWarRoundResponse> DecideAsync(string userId, string roundId, DecideCasinoWarRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ValidateRoundId(roundId); ValidateKey(idempotencyKey);
        var result = await store.DecideAsync(userId, roundId, idempotencyKey, ParseDecision(request.Decision), timeProvider.GetUtcNow(), cancellationToken);
        return ToResponse(result);
    }
    internal static CasinoWarRoundResponse ToResponse(CasinoWarStoreResult result)
    {
        var stored = result.Round; var round = stored.Round; var tie = stored.TieSettlement;
        return new CasinoWarRoundResponse(stored.RoundId, CasinoWarMoney.ToRand(result.BalanceCents), round.PrimaryStake, tie?.Stake ?? 0m,
            round.Phase == CasinoWarRoundPhase.AwaitingTieDecision ? "awaiting-tie-decision" : "completed",
            ToCard(round.PlayerOpeningCard), ToCard(round.DealerOpeningCard), round.TieDecision is null ? null : DecisionName(round.TieDecision.Value),
            round.PlayerWarCard is null ? null : ToCard(round.PlayerWarCard.Value), round.DealerWarCard is null ? null : ToCard(round.DealerWarCard.Value),
            round.Settlement is null ? null : ToPrimary(round.Settlement), tie is null ? null : ToTie(tie));
    }
    internal static CasinoWarTieDecision ParseDecision(string? value) => value?.Trim().ToLowerInvariant() switch { "surrender" => CasinoWarTieDecision.Surrender, "go-to-war" => CasinoWarTieDecision.GoToWar, _ => throw new ArgumentException("Choose surrender or go-to-war.", nameof(value)) };
    internal static string DecisionName(CasinoWarTieDecision value) => value == CasinoWarTieDecision.Surrender ? "surrender" : value == CasinoWarTieDecision.GoToWar ? "go-to-war" : throw new ArgumentOutOfRangeException(nameof(value));
    internal static string DispositionName(CasinoWarSettlementDisposition value) => value switch { CasinoWarSettlementDisposition.Win => "win", CasinoWarSettlementDisposition.Loss => "loss", CasinoWarSettlementDisposition.Surrender => "surrender", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    internal static string OpeningOutcomeName(CasinoWarOpeningOutcome value) => value switch { CasinoWarOpeningOutcome.PlayerWin => "player-win", CasinoWarOpeningOutcome.DealerWin => "dealer-win", CasinoWarOpeningOutcome.TieDecisionRequired => "tie-decision-required", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    internal static string RoundOutcomeName(CasinoWarRoundOutcome value) => value switch { CasinoWarRoundOutcome.PlayerOpeningWin => "player-opening-win", CasinoWarRoundOutcome.DealerOpeningWin => "dealer-opening-win", CasinoWarRoundOutcome.PlayerSurrendered => "player-surrendered", CasinoWarRoundOutcome.PlayerWarWin => "player-war-win", CasinoWarRoundOutcome.DealerWarWin => "dealer-war-win", CasinoWarRoundOutcome.PlayerWarTieWin => "player-war-tie-win", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static CasinoWarPrimarySettlementResponse ToPrimary(CasinoWarSettlement settlement) => new(DispositionName(settlement.Disposition), RoundOutcomeName(settlement.Outcome), settlement.TotalWagered, settlement.TotalReturn, settlement.Profit);
    private static CasinoWarTieSettlementResponse ToTie(CasinoWarTieBetSettlement settlement) => new(settlement.Won, DispositionName(settlement.Disposition), OpeningOutcomeName(settlement.OpeningOutcome), settlement.Stake, settlement.TotalReturn, settlement.Profit);
    internal static CasinoWarCardResponse ToCard(PlayingCard card) => new(card.Rank switch { CardRank.Ace => "ace", CardRank.Two => "two", CardRank.Three => "three", CardRank.Four => "four", CardRank.Five => "five", CardRank.Six => "six", CardRank.Seven => "seven", CardRank.Eight => "eight", CardRank.Nine => "nine", CardRank.Ten => "ten", CardRank.Jack => "jack", CardRank.Queen => "queen", CardRank.King => "king", _ => throw new ArgumentOutOfRangeException(nameof(card)) }, card.Suit switch { CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds", CardSuit.Hearts => "hearts", CardSuit.Spades => "spades", _ => throw new ArgumentOutOfRangeException(nameof(card) )});
    private static IReadOnlyList<PlayingCard> CreateShuffledShoe()
    {
        var shoe = Enumerable.Range(0, 6).SelectMany(_ => StandardDeck.Create()).ToArray();
        for (var index = shoe.Length - 1; index > 0; index--) { var replacement = RandomNumberGenerator.GetInt32(index + 1); (shoe[index], shoe[replacement]) = (shoe[replacement], shoe[index]); }
        return shoe;
    }
    private static void ValidateKey(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value)); }
    private static void ValidateRoundId(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("The Casino War round identifier is invalid.", nameof(value)); }
}
