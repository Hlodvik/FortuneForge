using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Configuration;

/// <summary>
/// Local, uncalibrated Neon arcade model with standard Special Plays.
/// </summary>
public static class NeonArcadeSlotDefinitions
{
    public const string ModelId = "neon-arcade-local-v2";
    public const string LegacyEngineVersion = "neon-arcade-prototype-v2";
    public const string BaseOnlyEngineVersion = "neon-arcade-special-plays-v3";
    public const string PreviousEngineVersion = "neon-arcade-special-plays-v4";
    public const string PreviousPaidEngineVersion = "neon-arcade-special-plays-v5";
    public const string EngineVersion = "neon-arcade-special-plays-v6";
    public const string SymbolSetId = "neon-arcade-symbols-v2";
    public const string ReelSetId = "neon-arcade-reels-v2";
    public const string PaytableId = "neon-arcade-paytable-v2";
    public const decimal BoostPercentPerBonusSymbol = 25m;
    public const int SpecialPlaySpins = 7;
    public const long TripleSevenPatternMultiplier = 12;
    public const int StopsPerReel = 2048;
    private const int MinimumWagerPoints = 50;
    private const int MaximumWagerPoints = 50_000;

    public static bool SupportsSpecialPlays(string? engineVersion) =>
        engineVersion is BaseOnlyEngineVersion or PreviousEngineVersion or PreviousPaidEngineVersion or EngineVersion;

    public static bool SupportsFreeSpinRetriggers(string? engineVersion) =>
        engineVersion is PreviousEngineVersion or PreviousPaidEngineVersion or EngineVersion;

    public static long TripleSevenMultiplierForEngine(string? engineVersion) =>
        engineVersion is PreviousPaidEngineVersion or EngineVersion ? TripleSevenPatternMultiplier : 3;

    private static readonly (string Symbol, int Count)[] Weights =
    [
        ("2", 116), ("4", 116), ("5", 116), ("7", 116),
        ("3", 462), ("SEAL_SYNC", 256), ("SEAL_ROWS", 418), ("SEAL_PAW", 256),
        ("BANANA", 101), ("ACE", 80), ("FREE", 8),
        ("6", 1), ("POWER", 1), ("SEAL_RAND", 1)
    ];

    private static readonly (string Symbol, long Three, long Five)[] Payouts =
    [
        ("2", 1, 4), ("3", 1, 2), ("4", 1, 7), ("5", 2, 6),
        ("7", 3, 11), ("ACE", 5, 18),
        ("SEAL_SYNC", 1, 4), ("SEAL_ROWS", 1, 5), ("SEAL_PAW", 2, 8)
    ];

    public static bool IsModel(GameDefinition game) => game.Math.PaytableId == PaytableId;

    public static bool IsOrdinaryPayingSymbol(string symbolId) =>
        Payouts.Any(row => row.Symbol == symbolId);

    public static GameSymbolRules CreateSymbolRules(GameSymbolRules source) => new()
    {
        SymbolSetId = SymbolSetId,
        WildSymbolId = source.WildSymbolId,
        NativeWildMatchLengths = [.. source.NativeWildMatchLengths],
        WildSubstitutionMatchLengths = [3, 5]
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = Weights.Select(row => new SymbolDefinition { Id = row.Symbol }).ToList()
    };

    public static PaytableDefinition CreatePaytable() => new()
    {
        Id = PaytableId,
        SymbolSetId = SymbolSetId,
        Rules = Payouts.SelectMany(row => new[]
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
