using System.Text.Json;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent;

public sealed record PharaohsAscentPaytableEntry(string SymbolId, int MatchLength, long Multiplier);
public sealed record PharaohsAscentMathDefinition(string EngineVersion, string PaytableId,
    decimal PayoutMultiplier, int TombMinimumScarabs, IReadOnlyList<PharaohsAscentPaytableEntry> Payouts, decimal TargetRtp);

/// <summary>The single current game-owned integer paytable and entry policy.</summary>
public static class PharaohsAscentMathV3
{
    public static PharaohsAscentMathDefinition Current { get; } = Load();

    internal static FrozenSlotDefinitions CreateDefinitions(SlotProfile profile)
    {
        var source = ClassicSlotDefinitionFactory.Create(profile);
        var game = source.Game;
        var paytable = new PaytableDefinition { Id = Current.PaytableId, SymbolSetId = source.SymbolSet.Id,
            Rules = Current.Payouts.Select(rule => new PayoutRule { SymbolId = rule.SymbolId,
                MatchLength = rule.MatchLength, Multiplier = rule.Multiplier }).ToList() };
        return source with
        {
            Paytable = paytable,
            Game = new GameDefinition
            {
                Id = game.Id, Layout = game.Layout, Symbols = game.Symbols, Matching = game.Matching,
                Wagering = game.Wagering, FreeGames = game.FreeGames, SpecialPoints = game.SpecialPoints,
                Energy = game.Energy, Paylines = game.Paylines,
                Math = new GameMathDefinition { ReelSetId = game.Math.ReelSetId, PaytableId = paytable.Id,
                    PayoutMultiplier = Current.PayoutMultiplier, PaylinePayoutSteps = game.Math.PaylinePayoutSteps,
                    FiveMatchPityMissLimit = game.Math.FiveMatchPityMissLimit,
                    Targets = new GameMathTargets { Rtp = Current.TargetRtp,
                        HitRate = game.Math.Targets.HitRate, Volatility = game.Math.Targets.Volatility } },
            },
        };
    }

    private static PharaohsAscentMathDefinition Load()
    {
        using var resource = typeof(PharaohsAscentMathV3).Assembly.GetManifestResourceStream(
            "FortuneForge.Games.PharaohsAscent.math-v3.json") ?? throw new InvalidOperationException("The current Pharaoh math contract is missing.");
        var value = JsonSerializer.Deserialize<PharaohsAscentMathDefinition>(resource,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("The current Pharaoh math contract is invalid.");
        var symbols = ClassicSlotDefinitionFactory.CreateClassicSymbolSet().Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value.EngineVersion) || string.IsNullOrWhiteSpace(value.PaytableId) ||
            value.PayoutMultiplier != 1m || value.TargetRtp is <= 0m or >= 1m ||
            value.TombMinimumScarabs < 2 || value.TombMinimumScarabs > 5 || value.Payouts is null ||
            value.Payouts.Count == 0 || value.Payouts.Any(rule => !symbols.Contains(rule.SymbolId) || rule.MatchLength is not (3 or 5) || rule.Multiplier is < 0 or > 1_000) ||
            value.Payouts.Select(rule => (rule.SymbolId, rule.MatchLength)).Distinct().Count() != value.Payouts.Count)
            throw new InvalidOperationException("The current Pharaoh integer paytable is invalid.");
        return value;
    }
}
