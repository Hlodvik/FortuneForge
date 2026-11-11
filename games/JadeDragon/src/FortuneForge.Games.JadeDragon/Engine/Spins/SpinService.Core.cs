using FortuneForge.Games.JadeDragon.Bonuses;
using FortuneForge.Games.JadeDragon.Configuration;
using FortuneForge.Games.JadeDragon.Evaluation;
using FortuneForge.Games.JadeDragon.Models;
using FortuneForge.Games.JadeDragon.Payouts;
using FortuneForge.Games.JadeDragon.Reels;

namespace FortuneForge.Games.JadeDragon.Spins;

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
