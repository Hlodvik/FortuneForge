using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

/// <summary>Every active and historical Midnight Vault rules snapshot.</summary>
public static class MidnightVaultSlotDefinitions
{
    public const string GameId = "dragon-hoard-v1";
    public const string SymbolSetId = "midnight-vault-symbols-v1";
    public const string PaytableId = "midnight-vault-paytable-v3";
    public const string PreviousPaytableId = "midnight-vault-paytable-v2";
    public const string LegacyPaytableId = "midnight-vault-paytable-v1";
    public const string AdditionalOrdinarySymbolId = "8";
    public const string CollectionFeatureMode = "rows-paw";
    public const long MinimumWagerPoints = 2;
    public const long MaximumWagerPoints = 2000;
    public const decimal TargetHitRate = 0.21m;
    public const int BonusRoundSpins = 10;
    public const int LegacyScatterAwardedSpins = 6;
    public const int LegacyCollectionAwardedSpins = 7;
    public static IReadOnlyList<string> LegacyFeatureModes { get; } = ["sync", "rows", "paw", "rand"];

    private static readonly (string Symbol, long Three, long Five)[] LegacyPayouts =
    [
        ("2", 1, 4), ("3", 1, 2), ("4", 1, 7), ("5", 2, 6), ("6", 2, 8),
        ("7", 3, 11), ("ACE", 5, 18), ("POWER", 2, 4), ("8", 4, 14)
    ];

    private static readonly (string Symbol, long Three, long Five)[] V3Payouts =
    [
        ("2", 1, 2), ("3", 1, 1), ("4", 1, 4), ("5", 1, 4), ("6", 1, 4),
        ("7", 2, 6), ("ACE", 3, 10), ("POWER", 1, 2), ("8", 2, 8)
    ];

    private static readonly List<List<int>> Paylines =
    [
        [0, 0, 0, 0, 0], [1, 1, 1, 1, 1], [2, 2, 2, 2, 2], [3, 3, 3, 3, 3],
        [0, 1, 2, 1, 0], [1, 2, 3, 2, 1], [3, 2, 1, 2, 3], [2, 1, 0, 1, 2],
        [0, 1, 1, 1, 0], [1, 2, 2, 2, 1], [2, 3, 3, 3, 2], [3, 2, 2, 2, 3],
        [2, 1, 1, 1, 2], [0, 0, 1, 2, 3], [3, 3, 2, 1, 0], [0, 1, 2, 3, 3],
        [3, 2, 1, 0, 0], [0, 1, 0, 1, 0], [1, 2, 1, 2, 1], [2, 3, 2, 3, 2],
        [1, 0, 1, 0, 1], [2, 1, 2, 1, 2], [3, 2, 3, 2, 3]
    ];

    public static GameDefinition CreateGame() => CreateGameForPaytable(PaytableId);

    public static GameDefinition CreateGameForPaytable(string paytableId)
    {
        var (reelSetId, multiplier, hitRate, awardedSpins) = paytableId switch
        {
            LegacyPaytableId => (MidnightVaultBaseReelSet.Id, 1.09m, 0.30m, LegacyScatterAwardedSpins),
            PreviousPaytableId => (MidnightVaultBaseReelSet.Id, 1m, 0.30m, BonusRoundSpins),
            PaytableId => (MidnightVaultBaseReelSet.CurrentId, 1m, TargetHitRate, BonusRoundSpins),
            _ => throw new ArgumentException("Unknown Midnight Vault paytable version.", nameof(paytableId))
        };

        return new GameDefinition
        {
            Id = GameId,
            Layout = new GameLayoutDefinition { ReelCount = 5, VisibleRows = 4, PaylineCount = 23 },
            Symbols = CreateSymbolRules(),
            Matching = new GameMatchingRules { MinimumRunLength = 3, AllowMultipleRunsPerPayline = true },
            Math = new GameMathDefinition
            {
                ReelSetId = reelSetId,
                PaytableId = paytableId,
                PayoutMultiplier = multiplier,
                PaylinePayoutSteps = Enumerable.Repeat(0, 23).ToList(),
                FiveMatchPityMissLimit = 12,
                Targets = new GameMathTargets { Rtp = 0.90m, HitRate = hitRate }
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = 25m,
                MinimumWagerPoints = MinimumWagerPoints,
                MaximumWagerPoints = MaximumWagerPoints,
                WagerIncrementPoints = 2
            },
            FreeGames = new GameFreeGamesDefinition
            {
                SymbolId = "FREE",
                RequiredSymbols = 3,
                AwardedSpins = awardedSpins,
                VisibleFrequencyDivisor = 2
            },
            SpecialPoints = new GameSpecialPointsDefinition
            {
                SymbolId = "POWER",
                ThreeMatchPoints = 1,
                FiveMatchPoints = 2,
                ActivationCost = 8,
                CommonSymbolIds = ["2", "3", "4"]
            },
            Energy = null,
            Paylines = Paylines.Select(line => line.ToList()).ToList()
        };
    }

    // Compatibility overload for callers replaying a previously captured host definition.
    public static GameDefinition CreateGameForPaytable(GameDefinition active, string paytableId)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (active.Id != GameId)
            throw new ArgumentException("Only Midnight Vault has these paytable versions.", nameof(active));
        var owned = CreateGameForPaytable(paytableId);
        return new GameDefinition
        {
            Id = active.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = active.Layout.ReelCount,
                VisibleRows = active.Layout.VisibleRows,
                PaylineCount = active.Layout.PaylineCount
            },
            Symbols = new GameSymbolRules
            {
                SymbolSetId = SymbolSetId,
                WildSymbolId = active.Symbols.WildSymbolId,
                PaylineSymbolIds = active.Symbols.PaylineSymbolIds is null ? null : [.. active.Symbols.PaylineSymbolIds],
                NativeWildMatchLengths = [.. active.Symbols.NativeWildMatchLengths],
                WildSubstitutionMatchLengths = [.. active.Symbols.WildSubstitutionMatchLengths]
            },
            Matching = new GameMatchingRules
            {
                MinimumRunLength = active.Matching.MinimumRunLength,
                AllowMultipleRunsPerPayline = active.Matching.AllowMultipleRunsPerPayline
            },
            Math = new GameMathDefinition
            {
                ReelSetId = owned.Math.ReelSetId,
                PaytableId = paytableId,
                PayoutMultiplier = owned.Math.PayoutMultiplier,
                PaylinePayoutSteps = [.. active.Math.PaylinePayoutSteps],
                FiveMatchPityMissLimit = active.Math.FiveMatchPityMissLimit,
                Targets = new GameMathTargets
                {
                    Rtp = active.Math.Targets.Rtp,
                    HitRate = owned.Math.Targets.HitRate,
                    Volatility = active.Math.Targets.Volatility
                }
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = active.Wagering.PointValueInCents,
                MinimumWagerPoints = active.Wagering.MinimumWagerPoints,
                MaximumWagerPoints = active.Wagering.MaximumWagerPoints,
                WagerIncrementPoints = active.Wagering.WagerIncrementPoints,
                AllowedWagerPoints = [.. active.Wagering.AllowedWagerPoints]
            },
            FreeGames = active.FreeGames is null ? null : new GameFreeGamesDefinition
            {
                SymbolId = active.FreeGames.SymbolId,
                RequiredSymbols = active.FreeGames.RequiredSymbols,
                AwardedSpins = owned.FreeGames!.AwardedSpins,
                VisibleFrequencyDivisor = active.FreeGames.VisibleFrequencyDivisor
            },
            SpecialPoints = active.SpecialPoints is null ? null : new GameSpecialPointsDefinition
            {
                SymbolId = active.SpecialPoints.SymbolId,
                ThreeMatchPoints = active.SpecialPoints.ThreeMatchPoints,
                FiveMatchPoints = active.SpecialPoints.FiveMatchPoints,
                ActivationCost = active.SpecialPoints.ActivationCost,
                CommonSymbolIds = [.. active.SpecialPoints.CommonSymbolIds]
            },
            Energy = active.Energy is null ? null : new GameEnergyDefinition
            {
                SymbolId = active.Energy.SymbolId,
                PointsPerVisibleSymbol = active.Energy.PointsPerVisibleSymbol
            },
            Paylines = active.Paylines.Select(line => line.ToList()).ToList()
        };
    }

    public static GameSymbolRules CreateSymbolRules() => new()
    {
        SymbolSetId = SymbolSetId,
        WildSymbolId = "ACE",
        NativeWildMatchLengths = [3, 5],
        WildSubstitutionMatchLengths = [5]
    };

    public static GameSymbolRules CreateSymbolRules(GameSymbolRules source) => new()
    {
        SymbolSetId = SymbolSetId,
        WildSymbolId = source.WildSymbolId,
        NativeWildMatchLengths = [.. source.NativeWildMatchLengths],
        WildSubstitutionMatchLengths = [.. source.WildSubstitutionMatchLengths]
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = SymbolSetId,
        Symbols = new[]
        {
            "2", "3", "4", "5", "6", "7", AdditionalOrdinarySymbolId,
            "ACE", "FREE", "POWER", "BANANA",
            "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"
        }.Select(id => new SymbolDefinition { Id = id }).ToList()
    };

    public static PaytableDefinition CreatePaytable() => CreatePaytable(PaytableId);
    public static PaytableDefinition CreatePreviousPaytable() => CreatePaytable(PreviousPaytableId);
    public static PaytableDefinition CreateLegacyPaytable() => CreatePaytable(LegacyPaytableId);

    public static PaytableDefinition CreatePaytable(string id) => new()
    {
        Id = id,
        SymbolSetId = SymbolSetId,
        Rules = (id == PaytableId ? V3Payouts : id is PreviousPaytableId or LegacyPaytableId
            ? LegacyPayouts : throw new ArgumentException("Unknown Midnight Vault paytable version.", nameof(id)))
            .SelectMany(row => new[]
            {
                new PayoutRule { SymbolId = row.Symbol, MatchLength = 3, Multiplier = row.Three },
                new PayoutRule
                {
                    SymbolId = row.Symbol,
                    MatchLength = 5,
                    Multiplier = id == PreviousPaytableId && row.Symbol == "5" ? 7 : row.Five
                }
            }).ToList()
    };

    public static MidnightVaultProfile CreateProfile(string paytableId = PaytableId)
    {
        var game = CreateGameForPaytable(paytableId);
        return new(GameId, 3, game.FreeGames!.AwardedSpins, 30,
            paytableId == LegacyPaytableId ? LegacyCollectionAwardedSpins : BonusRoundSpins,
            true, false, false, null, CollectionFeatureMode, game.Math.ReelSetId,
            paytableId == PaytableId ? TargetHitRate : null, game.Math.PayoutMultiplier);
    }
}
