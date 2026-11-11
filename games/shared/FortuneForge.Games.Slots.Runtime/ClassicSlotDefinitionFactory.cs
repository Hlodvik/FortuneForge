using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

/// <summary>
/// Frozen classic-v4 source data. Game packages call this factory at build/runtime
/// instead of reading FortuneForge.Server configuration.
/// </summary>
public static class ClassicSlotDefinitionFactory
{
    public const string SymbolSetId = "classic-symbols-v3";
    public const string ReelSetId = "classic-reels-v4";
    public const string PaytableId = "classic-paytable-v4";

    public static FrozenSlotDefinitions Create(SlotProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.GameId);
        var allPaylines = CreatePaylines();
        var indexes = profile.PaylinePatternIds is null
            ? Enumerable.Range(0, allPaylines.Count).ToArray()
            : profile.PaylinePatternIds.Select(patternId => patternId - 1).ToArray();
        if (indexes.Length == 0 || indexes.Any(index => index < 0 || index >= allPaylines.Count))
            throw new ArgumentOutOfRangeException(nameof(profile), "Payline pattern IDs must reference the frozen classic layout.");

        var reelSetId = profile.BaseReelSetId ?? ReelSetId;
        var symbolSet = CreateClassicSymbolSet();
        var reelSet = CreateClassicReelSet(reelSetId);
        var paytable = CreateClassicPaytable();
        var game = new GameDefinition
        {
            Id = profile.GameId,
            Layout = new GameLayoutDefinition
            {
                ReelCount = 5,
                VisibleRows = 4,
                PaylineCount = indexes.Length
            },
            Symbols = new GameSymbolRules
            {
                SymbolSetId = SymbolSetId,
                WildSymbolId = "ACE",
                NativeWildMatchLengths = [3, 5],
                WildSubstitutionMatchLengths = [5]
            },
            Matching = new GameMatchingRules
            {
                MinimumRunLength = 3,
                AllowMultipleRunsPerPayline = true
            },
            Math = new GameMathDefinition
            {
                ReelSetId = reelSetId,
                PaytableId = PaytableId,
                PayoutMultiplier = profile.PayoutMultiplier,
                PaylinePayoutSteps = indexes.Select(_ => 0).ToList(),
                FiveMatchPityMissLimit = 12,
                Targets = new GameMathTargets
                {
                    Rtp = 0.90m,
                    HitRate = profile.TargetHitRate ?? 0.30m
                }
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = 25m,
                MinimumWagerPoints = 2,
                MaximumWagerPoints = 2_000,
                WagerIncrementPoints = 2
            },
            FreeGames = profile.CreateFreeGames(),
            SpecialPoints = new GameSpecialPointsDefinition
            {
                SymbolId = "POWER",
                ThreeMatchPoints = 1,
                FiveMatchPoints = 2,
                ActivationCost = 8,
                CommonSymbolIds = ["2", "3", "4"]
            },
            Energy = profile.UsesEnergy ? new GameEnergyDefinition
            {
                SymbolId = "BOLT",
                PointsPerVisibleSymbol = 1
            } : null,
            Paylines = indexes.Select(index => allPaylines[index].ToList()).ToList()
        };
        return new FrozenSlotDefinitions(game, symbolSet, reelSet, paytable);
    }

    public static SymbolSetDefinition CreateClassicSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = new[]
        {
            "2", "3", "4", "5", "6", "7", "ACE", "FREE", "POWER", "BOLT",
            "BANANA", "PAW", "RAND_05", "RAND_1", "RAND_15", "RAND_2", "RAND_3",
            "RAND_4", "RAND_5", "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"
        }.Select(id => new SymbolDefinition { Id = id }).ToList()
    };

    public static ReelSetDefinition CreateClassicReelSet(string id = ReelSetId) => new()
    {
        Id = id,
        SymbolSetId = SymbolSetId,
        Reels =
        [
            ["6","4","4","3","7","ACE","4","POWER","FREE","4","2","6","5","2","3","3","4","3","POWER","4","2","3","4","6","4","3","5","3","3","2","3","POWER","FREE","6","4","2","6","2","2","POWER","3","5","3","2","5","5","7","2","7","3","3","4","POWER","FREE","POWER","POWER","BOLT","2","POWER","3","6","2","5","3"],
            ["5","7","POWER","4","2","3","3","6","POWER","FREE","3","2","3","2","2","3","4","6","2","3","4","3","3","ACE","POWER","5","4","7","6","3","3","3","6","FREE","6","5","3","4","POWER","4","7","5","2","POWER","POWER","4","4","5","2","POWER","2","4","2","POWER","4","BOLT","6","5","FREE","3","2","2","3","3"],
            ["4","7","3","2","3","5","FREE","POWER","4","4","5","2","3","2","4","2","6","2","ACE","POWER","2","5","7","3","POWER","3","2","4","3","3","FREE","POWER","6","6","2","3","6","3","2","5","POWER","5","6","3","3","2","4","7","POWER","POWER","4","3","6","4","3","4","BOLT","FREE","2","4","3","5","POWER","3"],
            ["7","5","4","2","2","3","3","4","4","6","4","5","6","4","FREE","4","6","2","POWER","ACE","POWER","7","2","POWER","6","6","2","3","2","7","3","3","POWER","6","3","FREE","POWER","5","3","3","POWER","2","4","5","3","3","5","2","POWER","2","3","2","3","5","4","FREE","POWER","2","BOLT","3","4","3","4","3"],
            ["2","5","4","3","3","5","7","FREE","2","POWER","4","ACE","6","6","2","5","2","2","4","FREE","2","4","3","4","3","5","5","POWER","3","4","2","7","POWER","3","3","3","5","3","4","POWER","3","3","2","2","4","3","6","3","6","2","6","BOLT","POWER","7","POWER","FREE","2","4","POWER","4","6","POWER","3","3"]
        ]
    };

    public static PaytableDefinition CreateClassicPaytable() => new()
    {
        Id = PaytableId,
        SymbolSetId = SymbolSetId,
        Rules = new (string Symbol, int Length, long Multiplier)[]
        {
            ("2",3,1), ("2",5,4), ("3",3,1), ("3",5,2),
            ("4",3,1), ("4",5,7), ("5",3,2), ("5",5,6),
            ("6",3,2), ("6",5,8), ("7",3,3), ("7",5,11),
            ("ACE",3,5), ("ACE",5,18), ("POWER",3,2), ("POWER",5,4)
        }.Select(rule => new PayoutRule
        {
            SymbolId = rule.Symbol,
            MatchLength = rule.Length,
            Multiplier = rule.Multiplier
        }).ToList()
    };

    public static List<List<int>> CreatePaylines() =>
    [
        [0,0,0,0,0], [1,1,1,1,1], [2,2,2,2,2], [3,3,3,3,3],
        [0,1,2,1,0], [1,2,3,2,1], [3,2,1,2,3], [2,1,0,1,2],
        [0,1,1,1,0], [1,2,2,2,1], [2,3,3,3,2], [3,2,2,2,3],
        [2,1,1,1,2], [0,0,1,2,3], [3,3,2,1,0], [0,1,2,3,3],
        [3,2,1,0,0], [0,1,0,1,0], [1,2,1,2,1], [2,3,2,3,2],
        [1,0,1,0,1], [2,1,2,1,2], [3,2,3,2,3]
    ];
}
