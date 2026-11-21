using System.Security.Cryptography;

namespace FortuneForge.Games.SicBo.Server;

public sealed class SicBoService(ISicBoStore store, TimeProvider timeProvider, Func<SicBoRoll>? createRoll = null)
{
    private readonly Func<SicBoRoll> createRoll = createRoll ?? (() => new SicBoRoll(RandomNumberGenerator.GetInt32(1, 7), RandomNumberGenerator.GetInt32(1, 7), RandomNumberGenerator.GetInt32(1, 7)));

    public async Task<SicBoRoundResponse> StartAsync(string userId, CreateSicBoRoundRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateKey(idempotencyKey);
        return ToResponse(await store.StartAsync(userId, idempotencyKey, SicBoMoney.ParseBets(request.Bets), createRoll(), timeProvider.GetUtcNow(), cancellationToken));
    }

    public async Task<SicBoRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new SicBoRoundNotFoundException());
    }

    public static SicBoRoundResponse ToResponse(SicBoStoreResult result)
    {
        var settled = result.Round.Bets.Select((bet, index) => (Bet: bet, Settlement: SicBoPaytable.Settle(SicBoMoney.ToDomain(bet), result.Round.Roll), Index: index)).ToArray();
        var totalStake = result.Round.Bets.Sum(bet => SicBoMoney.ToRand(bet.StakeCents));
        var totalReturn = settled.Sum(item => item.Settlement.TotalReturn);
        return new SicBoRoundResponse(result.Round.RoundId, SicBoMoney.ToRand(result.BalanceCents), "settled", result.Round.Roll.Values.ToArray(), result.Round.Roll.Total, result.Round.Roll.IsTriple, totalStake, totalReturn, totalReturn - totalStake,
            settled.Select(item => new SicBoSettlementResponse(item.Index, item.Bet.Kind, SicBoMoney.ToRand(item.Bet.StakeCents), item.Bet.Face, item.Bet.Total, item.Bet.FirstFace, item.Bet.SecondFace, item.Settlement.Won, item.Settlement.ProfitOdds, item.Settlement.Profit, item.Settlement.TotalReturn)).ToArray());
    }

    private static void ValidateKey(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(value)); }
    private static void ValidateRoundId(string value) { if (value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("The Sic Bo round identifier is invalid.", nameof(value)); }
}
