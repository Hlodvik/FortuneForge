using FortuneForge.Games.EmptySlots.Bonuses;
using FortuneForge.Games.EmptySlots.Configuration;
using FortuneForge.Games.EmptySlots.Evaluation;
using FortuneForge.Games.EmptySlots.Models;
using FortuneForge.Games.EmptySlots.Payouts;
using FortuneForge.Games.EmptySlots.Reels;

namespace FortuneForge.Games.EmptySlots.Spins;

internal sealed partial class SpinService(
    ISlotsDefinitionProvider definitions,
    IReelGenerator reelGenerator,
    ICombinationEvaluator combinationEvaluator,
    IPayoutCalculator payoutCalculator,
    IRandomIndexSource random)
{
    public const string AuditEngineVersion = "empty-slots-engine-v1";
    private const int MaximumPityGenerationAttempts = 10_000;
    private const string CollectorSymbolId = "S12";
    private const string PatternSymbolId = "S11";
    private static readonly string[] CommonReplacementSymbols = ["S01", "S02", "S03", "S04", "S05"];
    private static readonly string[] CollectionSymbolIds = ["S20", "S21", "S22", "S23"];

    public void ValidateRequest(string gameId, long wagerPoints) =>
        ValidateWager(definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found."), wagerPoints);
}
