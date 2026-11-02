using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Configuration;

/// <summary>
/// Midnight Vault's isolated fifteen-symbol presentation and paying-symbol contract.
/// The inherited POWER boost still reduces only low tiers 2/3/4; the new highest tier 8
/// is protected like existing higher tiers 5/6/7.
/// </summary>
public static class MidnightVaultSlotDefinitions
{
    public const string SymbolSetId = "midnight-vault-symbols-v1";
    public const string PaytableId = "midnight-vault-paytable-v2";
    public const string LegacyPaytableId = "midnight-vault-paytable-v1";
    public const string AdditionalOrdinarySymbolId = "8";
    public const long MinimumWagerPoints = 2;
    public const long MaximumWagerPoints = 2000;
    public const int BonusRoundSpins = 10;
    public const int LegacyScatterAwardedSpins = 6;
    public const int LegacyCollectionAwardedSpins = 7;

    // Version one is frozen here rather than inheriting another title's future table.
    private static readonly (string Symbol, long Three, long Five)[] LegacyPayouts =
    [
        ("2", 1, 4), ("3", 1, 2), ("4", 1, 7), ("5", 2, 6), ("6", 2, 8),
        ("7", 3, 11), ("ACE", 5, 18), ("POWER", 2, 4), ("8", 4, 14)
    ];

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

    public static PaytableDefinition CreateLegacyPaytable() => CreatePaytable(LegacyPaytableId);

    private static PaytableDefinition CreatePaytable(string id) => new()
    {
        Id = id,
        SymbolSetId = SymbolSetId,
        Rules = LegacyPayouts.SelectMany(row => new[]
        {
            new PayoutRule { SymbolId = row.Symbol, MatchLength = 3, Multiplier = row.Three },
            new PayoutRule
            {
                SymbolId = row.Symbol,
                MatchLength = 5,
                Multiplier = id == PaytableId && row.Symbol == "5" ? 7 : row.Five
            }
        }).ToList()
    };

    /// <summary>Resolve a server-selected awarded math model without changing the active game.</summary>
    public static GameDefinition CreateGameForPaytable(GameDefinition active, string paytableId)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (active.Id != SlotSpecialRoundProfiles.DragonHoardGameId)
            throw new ArgumentException("Only Midnight Vault has these paytable versions.", nameof(active));

        var multiplier = paytableId switch
        {
            LegacyPaytableId => 1.09m,
            PaytableId => 1m,
            _ => throw new ArgumentException("Unknown Midnight Vault paytable version.", nameof(paytableId))
        };

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
                SymbolSetId = active.Symbols.SymbolSetId,
                WildSymbolId = active.Symbols.WildSymbolId,
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
                ReelSetId = active.Math.ReelSetId,
                PaytableId = paytableId,
                PayoutMultiplier = multiplier,
                PaylinePayoutSteps = [.. active.Math.PaylinePayoutSteps],
                FiveMatchPityMissLimit = active.Math.FiveMatchPityMissLimit,
                Targets = new GameMathTargets
                {
                    Rtp = active.Math.Targets.Rtp,
                    HitRate = active.Math.Targets.HitRate,
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
            FreeGames = active.FreeGames is { } freeGames ? new GameFreeGamesDefinition
            {
                SymbolId = freeGames.SymbolId,
                RequiredSymbols = freeGames.RequiredSymbols,
                AwardedSpins = paytableId == LegacyPaytableId ? LegacyScatterAwardedSpins : BonusRoundSpins,
                VisibleFrequencyDivisor = freeGames.VisibleFrequencyDivisor
            } : null,
            SpecialPoints = active.SpecialPoints is { } specialPoints ? new GameSpecialPointsDefinition
            {
                SymbolId = specialPoints.SymbolId,
                ThreeMatchPoints = specialPoints.ThreeMatchPoints,
                FiveMatchPoints = specialPoints.FiveMatchPoints,
                ActivationCost = specialPoints.ActivationCost,
                CommonSymbolIds = [.. specialPoints.CommonSymbolIds]
            } : null,
            Energy = active.Energy is { } energy ? new GameEnergyDefinition
            {
                SymbolId = energy.SymbolId,
                PointsPerVisibleSymbol = energy.PointsPerVisibleSymbol
            } : null,
            Paylines = active.Paylines.Select(line => line.ToList()).ToList()
        };
    }
}
