using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

/// <summary>A complete, host-independent definition snapshot.</summary>
public sealed record FrozenSlotDefinitions(
    GameDefinition Game,
    SymbolSetDefinition SymbolSet,
    ReelSetDefinition ReelSet,
    PaytableDefinition Paytable)
{
    public GameDefinition CreateGame() => SlotDefinitionCloner.Clone(Game);
    public SymbolSetDefinition CreateSymbolSet() => SlotDefinitionCloner.Clone(SymbolSet);
    public ReelSetDefinition CreateReelSet() => SlotDefinitionCloner.Clone(ReelSet);
    public PaytableDefinition CreatePaytable() => SlotDefinitionCloner.Clone(Paytable);

    public FrozenSlotDefinitions CreateSnapshot() =>
        new(CreateGame(), CreateSymbolSet(), CreateReelSet(), CreatePaytable());
}

public static class SlotDefinitionCloner
{
    public static GameDefinition Clone(GameDefinition source) => new()
    {
        Id = source.Id,
        Layout = new GameLayoutDefinition
        {
            ReelCount = source.Layout.ReelCount,
            VisibleRows = source.Layout.VisibleRows,
            PaylineCount = source.Layout.PaylineCount
        },
        Symbols = new GameSymbolRules
        {
            SymbolSetId = source.Symbols.SymbolSetId,
            WildSymbolId = source.Symbols.WildSymbolId,
            PaylineSymbolIds = source.Symbols.PaylineSymbolIds is null
                ? null : [.. source.Symbols.PaylineSymbolIds],
            NativeWildMatchLengths = [.. source.Symbols.NativeWildMatchLengths],
            WildSubstitutionMatchLengths = [.. source.Symbols.WildSubstitutionMatchLengths]
        },
        Matching = new GameMatchingRules
        {
            MinimumRunLength = source.Matching.MinimumRunLength,
            AllowMultipleRunsPerPayline = source.Matching.AllowMultipleRunsPerPayline
        },
        Math = new GameMathDefinition
        {
            ReelSetId = source.Math.ReelSetId,
            PaytableId = source.Math.PaytableId,
            PayoutMultiplier = source.Math.PayoutMultiplier,
            PaylinePayoutSteps = [.. source.Math.PaylinePayoutSteps],
            FiveMatchPityMissLimit = source.Math.FiveMatchPityMissLimit,
            Targets = new GameMathTargets
            {
                Rtp = source.Math.Targets.Rtp,
                HitRate = source.Math.Targets.HitRate,
                Volatility = source.Math.Targets.Volatility
            }
        },
        Wagering = new GameWageringDefinition
        {
            PointValueInCents = source.Wagering.PointValueInCents,
            MinimumWagerPoints = source.Wagering.MinimumWagerPoints,
            MaximumWagerPoints = source.Wagering.MaximumWagerPoints,
            WagerIncrementPoints = source.Wagering.WagerIncrementPoints,
            AllowedWagerPoints = [.. source.Wagering.AllowedWagerPoints]
        },
        FreeGames = source.FreeGames is null ? null : new GameFreeGamesDefinition
        {
            SymbolId = source.FreeGames.SymbolId,
            RequiredSymbols = source.FreeGames.RequiredSymbols,
            AwardedSpins = source.FreeGames.AwardedSpins,
            VisibleFrequencyDivisor = source.FreeGames.VisibleFrequencyDivisor
        },
        SpecialPoints = source.SpecialPoints is null ? null : new GameSpecialPointsDefinition
        {
            SymbolId = source.SpecialPoints.SymbolId,
            ThreeMatchPoints = source.SpecialPoints.ThreeMatchPoints,
            FiveMatchPoints = source.SpecialPoints.FiveMatchPoints,
            ActivationCost = source.SpecialPoints.ActivationCost,
            CommonSymbolIds = [.. source.SpecialPoints.CommonSymbolIds]
        },
        Energy = source.Energy is null ? null : new GameEnergyDefinition
        {
            SymbolId = source.Energy.SymbolId,
            PointsPerVisibleSymbol = source.Energy.PointsPerVisibleSymbol
        },
        Paylines = source.Paylines.Select(rows => rows.ToList()).ToList()
    };

    public static SymbolSetDefinition Clone(SymbolSetDefinition source) => new()
    {
        Id = source.Id,
        Symbols = source.Symbols.Select(symbol => new SymbolDefinition { Id = symbol.Id }).ToList()
    };

    public static ReelSetDefinition Clone(ReelSetDefinition source) => new()
    {
        Id = source.Id,
        SymbolSetId = source.SymbolSetId,
        Reels = source.Reels.Select(strip => strip.ToList()).ToList()
    };

    public static PaytableDefinition Clone(PaytableDefinition source) => new()
    {
        Id = source.Id,
        SymbolSetId = source.SymbolSetId,
        Rules = source.Rules.Select(rule => new PayoutRule
        {
            SymbolId = rule.SymbolId,
            MatchLength = rule.MatchLength,
            Multiplier = rule.Multiplier
        }).ToList()
    };
}
