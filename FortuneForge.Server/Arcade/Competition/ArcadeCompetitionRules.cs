using System.Collections.Immutable;

namespace FortuneForge.Server.Arcade.Competition;

internal enum ArcadeCompetitionWindowKind
{
    Daily,
    Weekly,
}

internal sealed record ArcadeCompetitionWindow(ArcadeCompetitionWindowKind Kind, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);

/// <summary>An entered attempt with no completed score ranks as zero; its paid entry remains included in pool accounting.</summary>
internal sealed record ArcadeCompetitionAttempt
{
    public ArcadeCompetitionAttempt(string playerId, long? score)
    {
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("An arcade competition attempt requires a player id.", nameof(playerId));
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        PlayerId = playerId;
        Score = score;
    }

    public string PlayerId { get; }
    public long? Score { get; }
    public long ScoreForRanking => Score ?? 0;
}

internal sealed record ArcadeCompetitionRulesOptions
{
    public const long DefaultEntryFeeCents = 100;
    public const int DefaultHouseCutCapBasisPoints = 600;

    public long EntryFeeCents { get; init; } = DefaultEntryFeeCents;
    public int HouseCutCapBasisPoints { get; init; } = DefaultHouseCutCapBasisPoints;
    public TimeZoneInfo TimeZone { get; init; } = ArcadeCompetitionRules.JohannesburgTimeZone;
    public ImmutableDictionary<int, ImmutableArray<int>> PrizeSchedules { get; init; } = ArcadeCompetitionRules.DefaultPrizeSchedules;
}

internal sealed record ArcadeCompetitionPlacement(int Position, string PlayerId, long Score);
internal sealed record ArcadeCompetitionRefund(string PlayerId, long AmountCents);
internal sealed record ArcadeCompetitionPrizeAllocation(int Position, string PlayerId, long Score, long AmountCents);

internal sealed record ArcadeCompetitionRulesResult(
    ArcadeCompetitionWindow Window,
    long EntryFeeCents,
    int AttemptCount,
    int UniquePlayerCount,
    long TotalEntryFeesCents,
    int HouseCutBasisPoints,
    long HouseCutCents,
    long VisibleJackpotCents,
    ImmutableArray<ArcadeCompetitionPlacement> PrizePlacements,
    ImmutableArray<ArcadeCompetitionPrizeAllocation> PrizeAllocations,
    ImmutableArray<ArcadeCompetitionRefund> Refunds);

internal static class ArcadeCompetitionRules
{
    private const int HouseCutBeginsAtUniquePlayers = 3;
    private const int HouseCutCapAtUniquePlayers = 15;
    private const int BasisPointsPerWhole = 10_000;

    public static ImmutableDictionary<int, ImmutableArray<int>> DefaultPrizeSchedules { get; } =
        new Dictionary<int, ImmutableArray<int>>
        {
            [1] = [10_000],
            [2] = [7_000, 3_000],
            [3] = [5_000, 3_000, 2_000],
            [10] = [3_000, 1_800, 1_300, 1_000, 800, 600, 500, 400, 300, 300],
            [20] = [2_200, 1_200, 900, 700, 600, 500, 500, 400, 400, 400, 300, 300, 300, 300, 200, 200, 200, 200, 100, 100],
        }.ToImmutableDictionary();

    public static TimeZoneInfo JohannesburgTimeZone { get; } = ResolveJohannesburgTimeZone();

    public static ArcadeCompetitionWindow GetWindow(ArcadeCompetitionWindowKind kind, DateTimeOffset clockUtc, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? JohannesburgTimeZone;
        var localNow = TimeZoneInfo.ConvertTime(clockUtc, zone);
        var localStart = kind switch
        {
            ArcadeCompetitionWindowKind.Daily => localNow.Date,
            ArcadeCompetitionWindowKind.Weekly => localNow.Date.AddDays(-DaysSinceMonday(localNow.DayOfWeek)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var localEnd = kind == ArcadeCompetitionWindowKind.Daily ? localStart.AddDays(1) : localStart.AddDays(7);

        return new ArcadeCompetitionWindow(
            kind,
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)));
    }

    public static ArcadeCompetitionRulesResult Evaluate(
        ArcadeCompetitionWindow window,
        IEnumerable<ArcadeCompetitionAttempt> attempts,
        ArcadeCompetitionRulesOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(attempts);
        options ??= new ArcadeCompetitionRulesOptions();
        ValidateOptions(options);

        var materializedAttempts = attempts.ToArray();
        foreach (var attempt in materializedAttempts)
        {
            ArgumentNullException.ThrowIfNull(attempt);
            if (string.IsNullOrWhiteSpace(attempt.PlayerId))
                throw new ArgumentException("An arcade competition attempt requires a player id.", nameof(attempts));
        }

        var bestAttempts = materializedAttempts
            .GroupBy(attempt => attempt.PlayerId, StringComparer.Ordinal)
            .Select(group => new { PlayerId = group.Key, Score = group.Max(attempt => attempt.ScoreForRanking), AttemptCount = group.Count() })
            .OrderByDescending(player => player.Score)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
            .ToArray();

        var uniquePlayerCount = bestAttempts.Length;
        var totalEntryFeesCents = checked(materializedAttempts.LongLength * options.EntryFeeCents);
        var houseCutBasisPoints = HouseCutBasisPointsFor(uniquePlayerCount, options.HouseCutCapBasisPoints);
        var houseCutCents = checked(totalEntryFeesCents * houseCutBasisPoints / BasisPointsPerWhole);
        var prizePlacementCount = PrizePlacementCountFor(uniquePlayerCount);
        var prizePlacements = bestAttempts.Take(prizePlacementCount)
            .Select((player, index) => new ArcadeCompetitionPlacement(index + 1, player.PlayerId, player.Score))
            .ToImmutableArray();
        var visibleJackpotCents = totalEntryFeesCents - houseCutCents;
        var prizeAllocations = AllocatePrizes(visibleJackpotCents, prizePlacements, options.PrizeSchedules);
        var refunds = uniquePlayerCount == 1
            ? [new ArcadeCompetitionRefund(bestAttempts[0].PlayerId, totalEntryFeesCents)]
            : ImmutableArray<ArcadeCompetitionRefund>.Empty;

        return new ArcadeCompetitionRulesResult(
            window,
            options.EntryFeeCents,
            materializedAttempts.Length,
            uniquePlayerCount,
            totalEntryFeesCents,
            houseCutBasisPoints,
            houseCutCents,
            visibleJackpotCents,
            prizePlacements,
            prizeAllocations,
            refunds);
    }

    public static int PrizePlacementCountFor(int uniquePlayerCount) => uniquePlayerCount switch
    {
        <= 1 => 0,
        <= 4 => 1,
        <= 9 => 2,
        <= 99 => 3,
        <= 999 => 10,
        _ => 20,
    };

    public static int HouseCutBasisPointsFor(int uniquePlayerCount, int capBasisPoints = ArcadeCompetitionRulesOptions.DefaultHouseCutCapBasisPoints)
    {
        if (uniquePlayerCount < 0) throw new ArgumentOutOfRangeException(nameof(uniquePlayerCount));
        if (capBasisPoints < 0) throw new ArgumentOutOfRangeException(nameof(capBasisPoints));
        if (uniquePlayerCount < HouseCutBeginsAtUniquePlayers) return 0;
        if (uniquePlayerCount >= HouseCutCapAtUniquePlayers) return capBasisPoints;

        return (uniquePlayerCount - (HouseCutBeginsAtUniquePlayers - 1)) * capBasisPoints /
            (HouseCutCapAtUniquePlayers - (HouseCutBeginsAtUniquePlayers - 1));
    }

    private static int DaysSinceMonday(DayOfWeek dayOfWeek) => ((int)dayOfWeek + 6) % 7;

    private static void ValidateOptions(ArcadeCompetitionRulesOptions options)
    {
        if (options.EntryFeeCents <= 0) throw new ArgumentOutOfRangeException(nameof(options.EntryFeeCents));
        if (options.HouseCutCapBasisPoints is < 0 or > BasisPointsPerWhole)
            throw new ArgumentOutOfRangeException(nameof(options.HouseCutCapBasisPoints));
        ArgumentNullException.ThrowIfNull(options.TimeZone);
        ValidatePrizeSchedules(options.PrizeSchedules);
    }

    private static ImmutableArray<ArcadeCompetitionPrizeAllocation> AllocatePrizes(
        long visibleJackpotCents,
        ImmutableArray<ArcadeCompetitionPlacement> placements,
        ImmutableDictionary<int, ImmutableArray<int>> schedules)
    {
        if (placements.IsEmpty) return ImmutableArray<ArcadeCompetitionPrizeAllocation>.Empty;

        var schedule = schedules[placements.Length];
        var allocations = new long[placements.Length];
        var allocatedCents = 0L;
        for (var index = 0; index < allocations.Length; index++)
        {
            allocations[index] = checked(visibleJackpotCents * schedule[index] / BasisPointsPerWhole);
            allocatedCents = checked(allocatedCents + allocations[index]);
        }

        // Floor each share, then award every remaining cent in rank order so the full jackpot is allocated deterministically.
        for (var index = 0; allocatedCents < visibleJackpotCents; index = (index + 1) % allocations.Length)
        {
            allocations[index]++;
            allocatedCents++;
        }

        return placements.Select((placement, index) => new ArcadeCompetitionPrizeAllocation(
            placement.Position,
            placement.PlayerId,
            placement.Score,
            allocations[index])).ToImmutableArray();
    }

    private static void ValidatePrizeSchedules(ImmutableDictionary<int, ImmutableArray<int>>? schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        if (!schedules.Keys.ToHashSet().SetEquals(DefaultPrizeSchedules.Keys))
            throw new ArgumentException("Prize schedules must cover exactly the supported placement counts.", nameof(schedules));

        foreach (var (placementCount, schedule) in schedules)
        {
            if (schedule.Length != placementCount || schedule.Any(value => value < 0) || schedule.Sum() != BasisPointsPerWhole)
                throw new ArgumentException("Each prize schedule must match its placement count and total 100 percent.", nameof(schedules));
        }
    }

    private static TimeZoneInfo ResolveJohannesburgTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
    }
}
