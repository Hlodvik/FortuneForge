using System.Security.Cryptography;
using FortuneForge.Games.SicBo;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Dice.SicBo;

public sealed record SicBoBetRequest(string? Kind, decimal? Stake, int? Face, int? Total, int? FirstFace, int? SecondFace);
public sealed record CreateSicBoRoundRequest(IReadOnlyList<SicBoBetRequest>? Bets);
public sealed record SicBoStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStakePerBet, decimal StakeIncrement, int MaximumBetsPerRound, decimal Balance, string Mode);
public sealed record SicBoSettlementResponse(int BetIndex, string Kind, decimal Stake, int? Face, int? Total, int? FirstFace, int? SecondFace, bool Won, decimal ProfitOdds, decimal Profit, decimal TotalReturn);
public sealed record SicBoRoundResponse(string RoundId, decimal Balance, string Phase, IReadOnlyList<int> Dice, int Total, bool IsTriple, decimal TotalStaked, decimal TotalReturn, decimal Profit, IReadOnlyList<SicBoSettlementResponse> Settlements);
public sealed record SicBoErrorResponse(string Code, string Message);

internal sealed record SicBoStoredBet(string Kind, long StakeCents, int? Face, int? Total, int? FirstFace, int? SecondFace);
internal sealed record SicBoStoreRound(string RoundId, string UserId, IReadOnlyList<SicBoStoredBet> Bets, SicBoRoll Roll);
internal sealed record SicBoStoreResult(SicBoStoreRound Round, long BalanceCents);
internal sealed class SicBoRoundNotFoundException() : Exception("Sic Bo round not found.");
internal sealed class SicBoRoundConflictException(string message) : Exception(message);
internal sealed class SicBoInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{RandMoney.CentsToRand(availableCents):0.00}, but the bet slip requires R{RandMoney.CentsToRand(requiredCents):0.00}.");

internal static class SicBoMoney
{
    public const long MinimumStakeCents = 100;
    public const long MaximumStakeCents = 10_000;
    public const long StakeIncrementCents = 100;
    public const int MaximumBetsPerRound = 20;

    public static IReadOnlyList<SicBoStoredBet> ParseBets(IReadOnlyList<SicBoBetRequest>? requests)
    {
        if (requests is null || requests.Count is < 1 or > MaximumBetsPerRound)
            throw new ArgumentOutOfRangeException(nameof(requests), $"Submit one through {MaximumBetsPerRound} Sic Bo bets.");
        return requests.Select(ParseBet).ToArray();
    }

    public static SicBoStoredBet ParseBet(SicBoBetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = ParseKind(request.Kind);
        var stakeCents = StakeCents(request.Stake);
        var stored = kind switch
        {
            SicBoBetKind.Small or SicBoBetKind.Big or SicBoBetKind.Odd or SicBoBetKind.Even or SicBoBetKind.AnyTriple
                when request.Face is null && request.Total is null && request.FirstFace is null && request.SecondFace is null => new SicBoStoredBet(KindName(kind), stakeCents, null, null, null, null),
            SicBoBetKind.SingleNumber or SicBoBetKind.SpecificDouble or SicBoBetKind.SpecificTriple
                when request.Face is not null && request.Total is null && request.FirstFace is null && request.SecondFace is null => new SicBoStoredBet(KindName(kind), stakeCents, request.Face, null, null, null),
            SicBoBetKind.Total
                when request.Face is null && request.Total is >= 4 and <= 17 && request.FirstFace is null && request.SecondFace is null => new SicBoStoredBet(KindName(kind), stakeCents, null, request.Total, null, null),
            SicBoBetKind.TwoNumberCombination
                when request.Face is null && request.Total is null && request.FirstFace is not null && request.SecondFace is not null && request.FirstFace != request.SecondFace => new SicBoStoredBet(KindName(kind), stakeCents, null, null, Math.Min(request.FirstFace.Value, request.SecondFace.Value), Math.Max(request.FirstFace.Value, request.SecondFace.Value)),
            _ => throw new ArgumentException("The Sic Bo selections do not match the bet kind.", nameof(request)),
        };
        _ = ToDomain(stored);
        return stored;
    }

    public static SicBoBet ToDomain(SicBoStoredBet bet) => ParseKind(bet.Kind) switch
    {
        SicBoBetKind.Small => SicBoBet.Small(ToRand(StakeCents(ToRand(bet.StakeCents)))),
        SicBoBetKind.Big => SicBoBet.Big(ToRand(StakeCents(ToRand(bet.StakeCents)))),
        SicBoBetKind.Odd => SicBoBet.Odd(ToRand(StakeCents(ToRand(bet.StakeCents)))),
        SicBoBetKind.Even => SicBoBet.Even(ToRand(StakeCents(ToRand(bet.StakeCents)))),
        SicBoBetKind.AnyTriple => SicBoBet.AnyTriple(ToRand(StakeCents(ToRand(bet.StakeCents)))),
        SicBoBetKind.SingleNumber => SicBoBet.SingleNumber(ToRand(StakeCents(ToRand(bet.StakeCents))), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.SpecificDouble => SicBoBet.SpecificDouble(ToRand(StakeCents(ToRand(bet.StakeCents))), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.SpecificTriple => SicBoBet.SpecificTriple(ToRand(StakeCents(ToRand(bet.StakeCents))), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.Total => SicBoBet.ForTotal(ToRand(StakeCents(ToRand(bet.StakeCents))), bet.Total ?? throw CorruptBet()),
        SicBoBetKind.TwoNumberCombination => SicBoBet.TwoNumberCombination(ToRand(StakeCents(ToRand(bet.StakeCents))), bet.FirstFace ?? throw CorruptBet(), bet.SecondFace ?? throw CorruptBet()),
        _ => throw CorruptBet(),
    };

    public static long StakeCents(decimal? stake)
    {
        if (stake is null) throw new ArgumentException("A Sic Bo stake is required.", nameof(stake));
        var cents = checked(stake.Value * RandMoney.CentsPerRand);
        if (cents != decimal.Truncate(cents)) throw new ArgumentOutOfRangeException(nameof(stake), "A Sic Bo stake cannot include a fraction of a cent.");
        var value = checked((long)cents);
        if (value < MinimumStakeCents || value > MaximumStakeCents || value % StakeIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(stake), "Choose a whole-rand Sic Bo stake from R1.00 through R100.00.");
        return value;
    }

    public static decimal ToRand(long cents) => RandMoney.CentsToRand(cents);
    public static SicBoBetKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "small" => SicBoBetKind.Small, "big" => SicBoBetKind.Big, "odd" => SicBoBetKind.Odd, "even" => SicBoBetKind.Even,
        "single-number" => SicBoBetKind.SingleNumber, "total" => SicBoBetKind.Total, "two-number-combination" => SicBoBetKind.TwoNumberCombination,
        "specific-double" => SicBoBetKind.SpecificDouble, "any-triple" => SicBoBetKind.AnyTriple, "specific-triple" => SicBoBetKind.SpecificTriple,
        _ => throw new ArgumentException("The Sic Bo bet kind is not supported.", nameof(value)),
    };
    public static string KindName(SicBoBetKind kind) => kind switch
    {
        SicBoBetKind.Small => "small", SicBoBetKind.Big => "big", SicBoBetKind.Odd => "odd", SicBoBetKind.Even => "even",
        SicBoBetKind.SingleNumber => "single-number", SicBoBetKind.Total => "total", SicBoBetKind.TwoNumberCombination => "two-number-combination",
        SicBoBetKind.SpecificDouble => "specific-double", SicBoBetKind.AnyTriple => "any-triple", SicBoBetKind.SpecificTriple => "specific-triple",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private static InvalidOperationException CorruptBet() => new("The recorded Sic Bo bet is corrupt.");
}

internal interface ISicBoStore
{
    Task<SicBoStoreResult> StartAsync(string userId, string idempotencyKey, IReadOnlyList<SicBoStoredBet> bets, SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
}

internal sealed class SicBoService(ISicBoStore store, TimeProvider timeProvider, Func<SicBoRoll>? createRoll = null)
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

    internal static SicBoRoundResponse ToResponse(SicBoStoreResult result)
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
