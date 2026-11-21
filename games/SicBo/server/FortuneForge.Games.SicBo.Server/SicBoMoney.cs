namespace FortuneForge.Games.SicBo.Server;

public static class SicBoMoney
{
    public const long MinimumStakeCents = 100;
    public const long MaximumStakeCents = 1_000;
    // Rounds saved before the R10 table limit must remain readable and auditable.
    public const long LegacyMaximumStakeCents = 10_000;
    public const long StakeIncrementCents = 100;
    public const int MaximumBetsPerRound = 20;

    public static IReadOnlyList<SicBoStoredBet> ParseBets(IReadOnlyList<SicBoBetRequest>? requests)
    {
        if (requests is null || requests.Count is < 1 or > MaximumBetsPerRound)
            throw new ArgumentOutOfRangeException(nameof(requests), $"Submit one through {MaximumBetsPerRound} Sic Bo bets.");
        var bets = requests.Select(ParseBet).ToArray();
        CheckSpotLimit(bets, LegacyMaximumStakeCents);
        return bets;
    }

    public static void ValidateNewBets(IReadOnlyList<SicBoStoredBet> bets) => CheckSpotLimit(bets, MaximumStakeCents);

    private static void CheckSpotLimit(IReadOnlyList<SicBoStoredBet> bets, long maximumStakeCents)
    {
        if (bets.GroupBy(bet => (bet.Kind, bet.Face, bet.Total, bet.FirstFace, bet.SecondFace))
            .Any(spot => spot.Sum(bet => bet.StakeCents) > maximumStakeCents))
            throw new ArgumentOutOfRangeException(nameof(bets), $"A Sic Bo betting spot cannot exceed R{ToRand(maximumStakeCents):0.00}.");
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
        SicBoBetKind.Small => SicBoBet.Small(StoredStakeRand(bet.StakeCents)),
        SicBoBetKind.Big => SicBoBet.Big(StoredStakeRand(bet.StakeCents)),
        SicBoBetKind.Odd => SicBoBet.Odd(StoredStakeRand(bet.StakeCents)),
        SicBoBetKind.Even => SicBoBet.Even(StoredStakeRand(bet.StakeCents)),
        SicBoBetKind.AnyTriple => SicBoBet.AnyTriple(StoredStakeRand(bet.StakeCents)),
        SicBoBetKind.SingleNumber => SicBoBet.SingleNumber(StoredStakeRand(bet.StakeCents), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.SpecificDouble => SicBoBet.SpecificDouble(StoredStakeRand(bet.StakeCents), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.SpecificTriple => SicBoBet.SpecificTriple(StoredStakeRand(bet.StakeCents), bet.Face ?? throw CorruptBet()),
        SicBoBetKind.Total => SicBoBet.ForTotal(StoredStakeRand(bet.StakeCents), bet.Total ?? throw CorruptBet()),
        SicBoBetKind.TwoNumberCombination => SicBoBet.TwoNumberCombination(StoredStakeRand(bet.StakeCents), bet.FirstFace ?? throw CorruptBet(), bet.SecondFace ?? throw CorruptBet()),
        _ => throw CorruptBet(),
    };

    public static long StakeCents(decimal? stake)
    {
        if (stake is null) throw new ArgumentException("A Sic Bo stake is required.", nameof(stake));
        const string invalidStakeMessage = "Choose a whole-rand Sic Bo stake from R1.00 through R10.00.";
        if (stake.Value < ToRand(MinimumStakeCents) || stake.Value > ToRand(LegacyMaximumStakeCents))
            throw new ArgumentOutOfRangeException(nameof(stake), invalidStakeMessage);
        var cents = checked(stake.Value * 100);
        if (cents != decimal.Truncate(cents)) throw new ArgumentOutOfRangeException(nameof(stake), "A Sic Bo stake cannot include a fraction of a cent.");
        var value = checked((long)cents);
        if (value % StakeIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(stake), invalidStakeMessage);
        return value;
    }

    public static decimal ToRand(long cents) => cents / 100m;
    private static decimal StoredStakeRand(long cents) =>
        cents >= MinimumStakeCents && cents <= LegacyMaximumStakeCents && cents % StakeIncrementCents == 0
            ? ToRand(cents)
            : throw CorruptBet();
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
