using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService(
    ISlotsDefinitionProvider definitions,
    IReelGenerator reelGenerator,
    ICombinationEvaluator combinationEvaluator,
    IPayoutCalculator payoutCalculator,
    IRandomIndexSource random)
{
    private const int MaximumPityGenerationAttempts = 10_000;
    private const string WukongGameId = "classic-demo-v1";
    private const string MonkeyPawSymbolId = "PAW";
    private const string BananaSymbolId = "BANANA";
    private const string PawBoostFeatureMode = "paw";
    private const string RandColumnFeatureMode = "rand";
    private const string SyncedReelsFeatureMode = "sync";
    private const string ExtraRowsFeatureMode = "rows";
    private static readonly string[] CommonReplacementSymbols = ["2", "3", "4", "5", "6"];
    private static readonly string[] RandSymbolIds =
    [
        "RAND_05",
        "RAND_1",
        "RAND_15",
        "RAND_2",
        "RAND_3",
        "RAND_4",
        "RAND_5"
    ];
    private static readonly string[] SealSymbolIds =
    [
        "SEAL_SYNC",
        "SEAL_ROWS",
        "SEAL_PAW",
        "SEAL_RAND"
    ];
    private readonly ConcurrentDictionary<string, PityState> _pityStates = new(StringComparer.Ordinal);

    public void ValidateGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game id is required.", nameof(gameId));
        }

        _ = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
    }

    public void ValidateRequest(string gameId, long wagerPoints)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game id is required.", nameof(gameId));
        }

        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        ValidateWager(game, wagerPoints);
    }

    public int GetSpecialBoostCost(string gameId)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        return game.SpecialPoints?.ActivationCost
            ?? throw new ArgumentException(
                $"Game '{game.Id}' does not support a special-point boost.",
                nameof(gameId));
    }

}
