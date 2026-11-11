using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

/// <summary>
/// Local, uncalibrated Neon arcade model with standard Special Plays.
/// </summary>
public static class NeonArcadeSlotDefinitions
{
    public const string GameId = "neon-nights-v1";
    public const decimal PayoutMultiplier = 0.82m;
    public const string PreviousModelId = "neon-arcade-local-v2";
    public const string PreviousDiscoModelId = "neon-arcade-local-v3";
    public const string PreviousClusterModelId = "neon-arcade-local-v4";
    public const string PreviousCarModelId = "neon-arcade-local-v5";
    public const string ModelId = "neon-arcade-local-v6";
    public const string LegacyEngineVersion = "neon-arcade-prototype-v2";
    public const string BaseOnlyEngineVersion = "neon-arcade-special-plays-v3";
    public const string PreviousEngineVersion = "neon-arcade-special-plays-v4";
    public const string PreviousPaidEngineVersion = "neon-arcade-special-plays-v5";
    public const string PreviousExpandedEngineVersion = "neon-arcade-special-plays-v6";
    public const string PreviousDiscoEngineVersion = "neon-arcade-special-plays-v7";
    public const string PreviousCascadeEngineVersion = "neon-arcade-special-plays-v8";
    public const string PreviousClusterEngineVersion = "neon-arcade-special-plays-v9";
    public const string PreviousCarEngineVersion = "neon-arcade-special-plays-v10";
    public const string PreviousTwelvePlayEngineVersion = "neon-arcade-special-plays-v11";
    public const string PreviousMultiplierCarryEngineVersion = "neon-arcade-special-plays-v12";
    public const string EngineVersion = "neon-arcade-special-plays-v13";
    public const string SymbolSetId = "neon-arcade-symbols-v3";
    public const string ReelSetId = "neon-arcade-reels-v4";
    public const string PreviousPaytableId = "neon-arcade-paytable-v3";
    public const string PaytableId = "neon-arcade-paytable-v4";
    public const decimal BoostPercentPerBonusSymbol = 25m;
    public const int SpecialPlaySpins = 7;
    public const int PreviousDiscoPlaySpins = 10;
    public const int DiscoPlaySpins = 12;
    public const long TripleSevenPatternMultiplier = 12;
    public const int StopsPerReel = 4096;
    private const int MinimumWagerPoints = 50;
    private const int MaximumWagerPoints = 50_000;

    public static GameDefinition CreateGame() => new()
    {
        Id = GameId,
        Layout = new() { ReelCount = 5, VisibleRows = 4, PaylineCount = 23 },
        Symbols = CreateSymbolRules(new()
        {
            SymbolSetId = SymbolSetId, WildSymbolId = "ACE",
            WildSubstitutionMatchLengths = [3, 5]
        }),
        Matching = new() { MinimumRunLength = 3, AllowMultipleRunsPerPayline = true },
        Math = new()
        {
            ReelSetId = ReelSetId, PaytableId = PaytableId,
            PayoutMultiplier = PayoutMultiplier, FiveMatchPityMissLimit = 12,
            PaylinePayoutSteps = Enumerable.Repeat(0, 23).ToList(), Targets = new()
        },
        Wagering = CreateCentWagering(),
        Paylines =
        [
            [0,0,0,0,0], [1,1,1,1,1], [2,2,2,2,2], [3,3,3,3,3],
            [0,1,2,1,0], [1,2,3,2,1], [3,2,1,2,3], [2,1,0,1,2],
            [0,1,1,1,0], [1,2,2,2,1], [2,3,3,3,2], [3,2,2,2,3],
            [2,1,1,1,2], [0,0,1,2,3], [3,3,2,1,0], [0,1,2,3,3],
            [3,2,1,0,0], [0,1,0,1,0], [1,2,1,2,1], [2,3,2,3,2],
            [1,0,1,0,1], [2,1,2,1,2], [3,2,3,2,3]
        ]
    };

    public static bool SupportsSpecialPlays(string? engineVersion) =>
        engineVersion == BaseOnlyEngineVersion || SupportsFreeSpinRetriggers(engineVersion);

    public static bool SupportsFreeSpinRetriggers(string? engineVersion) =>
        engineVersion is PreviousEngineVersion or PreviousPaidEngineVersion || UsesExpandedArcadeGrid(engineVersion);

    public static bool UsesExpandedArcadeGrid(string? engineVersion) =>
        engineVersion is PreviousExpandedEngineVersion or PreviousDiscoEngineVersion || SupportsDiscoCascade(engineVersion);

    public static bool SupportsDiscoCascade(string? engineVersion) =>
        engineVersion == PreviousCascadeEngineVersion || SupportsClusterDisco(engineVersion);

    public static bool SupportsClusterDisco(string? engineVersion) =>
        engineVersion == PreviousClusterEngineVersion || UsesPaidCarPlays(engineVersion);

    public static bool UsesPaidCarPlays(string? engineVersion) =>
        engineVersion == PreviousCarEngineVersion || UsesTwelveDiscoPlays(engineVersion);

    private static bool UsesTwelveDiscoPlays(string? engineVersion) =>
        engineVersion == PreviousTwelvePlayEngineVersion || SupportsDiscoMultiplierCarry(engineVersion);

    public static bool SupportsDiscoMultiplierCarry(string? engineVersion) =>
        engineVersion is PreviousMultiplierCarryEngineVersion or EngineVersion;

    public static bool SupportsPackageReplay(string? engineVersion) =>
        SupportsDiscoCascade(engineVersion);

    public static string ModelIdForEngine(string? engineVersion) =>
        UsesTwelveDiscoPlays(engineVersion) ? ModelId :
        engineVersion == PreviousCarEngineVersion ? PreviousCarModelId :
        engineVersion is PreviousCascadeEngineVersion or PreviousClusterEngineVersion ? PreviousClusterModelId :
        engineVersion == PreviousDiscoEngineVersion ? PreviousDiscoModelId : PreviousModelId;

    public static long TripleSevenMultiplierForEngine(string? engineVersion) =>
        engineVersion == PreviousPaidEngineVersion || UsesExpandedArcadeGrid(engineVersion)
            ? TripleSevenPatternMultiplier : 3;

    private static readonly (string Symbol, int Count)[] Weights =
    [
        ("2", 232), ("4", 232), ("5", 232), ("7", 232),
        ("3", 927), ("SEAL_SYNC", 512), ("SEAL_ROWS", 836), ("SEAL_PAW", 512),
        ("BANANA", 202), ("ACE", 160), ("FREE", 16),
        ("6", 2), ("POWER", 1)
    ];

    private static readonly (string Symbol, long Three, long Five)[] PreviousPayouts =
    [
        ("2", 1, 4), ("3", 1, 2), ("4", 1, 7), ("5", 2, 6),
        ("7", 3, 11), ("ACE", 5, 18),
        ("SEAL_SYNC", 1, 4), ("SEAL_ROWS", 1, 5), ("SEAL_PAW", 2, 8)
    ];

    private static readonly (string Symbol, long Three, long Five)[] Payouts =
        PreviousPayouts.Where(row => row.Symbol != "ACE").ToArray();

    public static bool IsModel(GameDefinition game) => game.Math.PaytableId is PaytableId or PreviousPaytableId;

    public static bool IsOrdinaryPayingSymbol(string symbolId) =>
        Payouts.Any(row => row.Symbol == symbolId);

    public static GameSymbolRules CreateSymbolRules(GameSymbolRules source) => new()
    {
        SymbolSetId = SymbolSetId,
        WildSymbolId = source.WildSymbolId,
        NativeWildMatchLengths = [],
        WildSubstitutionMatchLengths = [3, 5],
        PaylineSymbolIds = Payouts.Select(row => row.Symbol).ToList()
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = Weights.Select(row => new SymbolDefinition { Id = row.Symbol }).ToList()
    };

    public static PaytableDefinition CreatePaytable() => CreatePaytable(PaytableId, Payouts);

    public static PaytableDefinition CreatePreviousPaytable() =>
        CreatePaytable(PreviousPaytableId, PreviousPayouts);

    private static PaytableDefinition CreatePaytable(string id,
        IEnumerable<(string Symbol, long Three, long Five)> payouts) => new()
    {
        Id = id,
        SymbolSetId = SymbolSetId,
        Rules = payouts.SelectMany(row => new[]
        {
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 3, Multiplier = row.Three },
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 5, Multiplier = row.Five }
        }).ToList()
    };

    public static bool IsValidCarryWager(long wagerPoints) =>
        wagerPoints is >= MinimumWagerPoints and <= MaximumWagerPoints;

    public static void ValidateCarryWager(long wagerPoints)
    {
        if (!IsValidCarryWager(wagerPoints))
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "The Neon bonus wager is outside the supported range.");
    }

    public static GameWageringDefinition CreateCentWagering() => new()
    {
        PointValueInCents = 1m,
        MinimumWagerPoints = MinimumWagerPoints,
        MaximumWagerPoints = MaximumWagerPoints,
        WagerIncrementPoints = 50
    };

    public static ReelSetDefinition CreateReelSet() => new()
    {
        Id = ReelSetId,
        SymbolSetId = SymbolSetId,
        Reels = Enumerable.Range(0, 5).Select(CreateStrip).ToList()
    };

    private static List<string> CreateStrip(int reel)
    {
        var strip = Weights.SelectMany(row => Enumerable.Repeat(row.Symbol, row.Count)).ToList();
        if (strip.Count != StopsPerReel) throw new InvalidOperationException("Invalid Neon reel weights.");
        // Versioned, deterministic authoring permutation. Actual stop selection
        // still uses the engine's cryptographic random source.
        var state = unchecked(0x4e454f4eu + (uint)reel * 0x9e3779b9u);
        for (var index = strip.Count - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            var other = (int)(state % (uint)(index + 1));
            (strip[index], strip[other]) = (strip[other], strip[index]);
        }
        return strip;
    }
}
