using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Games;
using GoldFoundryHostEngine = FortuneForge.Server.Slots.Games.GoldFoundry.GoldFoundryEngine;
using NeonNightsEngine = FortuneForge.Server.Slots.Games.NeonNights.NeonNightsEngine;
using MidnightVaultEngine = FortuneForge.Server.Slots.Games.MidnightVault.MidnightVaultEngine;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService(
    ISlotsDefinitionProvider definitions,
    IReelGenerator reelGenerator,
    ICombinationEvaluator combinationEvaluator,
    IPayoutCalculator payoutCalculator,
    IRandomIndexSource random,
    StandaloneSlotPackageRegistry standaloneSlotPackages)
{
    public SpinService(
        ISlotsDefinitionProvider definitions,
        IReelGenerator reelGenerator,
        ICombinationEvaluator combinationEvaluator,
        IPayoutCalculator payoutCalculator,
        IRandomIndexSource random)
        : this(definitions, reelGenerator, combinationEvaluator, payoutCalculator,
            random, new StandaloneSlotPackageRegistry(random))
    {
    }

    public const string AuditEngineVersion = "shared-slots-engine-v1";
    private const int MaximumPityGenerationAttempts = 10_000;
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
    // The application provider resolves the package-owned frozen definitions. Custom
    // providers are an intentional compatibility/replay seam and must continue through
    // SpinCore so their supplied definitions, reels, and evaluators are actually used.
    private readonly bool useStandaloneSlotPackages = definitions is OptionsSlotsDefinitionProvider;
    private readonly Games.JadeDragon.JadeDragonEngine jadeDragonEngine = new(random);
    private readonly NeonNightsEngine neonNightsEngine = new(
        definitions, random);
    private readonly MidnightVaultEngine midnightVaultEngine = new(random, reelGenerator);
    private readonly GoldFoundryHostEngine goldFoundryEngine = new(definitions, random);

    public void ValidateGame(string gameId)
    {
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId) return;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game id is required.", nameof(gameId));
        }

        _ = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
    }

    public void ValidateRequest(string gameId, long wagerPoints, bool useFreeSpin = false)
    {
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId)
        {
            Games.JadeDragon.JadeDragonEngine.ValidateWager(wagerPoints);
            return;
        }
        if (gameId == NeonArcadeSlotDefinitions.GameId)
        {
            neonNightsEngine.ValidateRequest(wagerPoints, useFreeSpin);
            return;
        }
        if (gameId == MidnightVaultSlotDefinitions.GameId)
        {
            midnightVaultEngine.ValidateRequest(wagerPoints, useFreeSpin);
            return;
        }
        if (gameId == GoldFoundrySlotDefinitions.GameId)
        {
            goldFoundryEngine.ValidateRequest(wagerPoints, useFreeSpin);
            return;
        }
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game id is required.", nameof(gameId));
        }

        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        if (useFreeSpin && ArcaneLightningCollector.IsModel(game))
            ArcaneLightningCollector.ValidateCarryWager(game, wagerPoints);
        else if (useFreeSpin && gameId == SlotSpecialRoundProfiles.GodsOfOlympusGameId)
            ValidateOlympusCarryWager(game, wagerPoints);
        else
            ValidateWager(game, wagerPoints);
    }

    private static void ValidateOlympusCarryWager(GameDefinition game, long wagerPoints)
    {
        if (!OlympusTumbleDemo.IsLegacyCarryWager(game.Wagering, wagerPoints))
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "The Olympus free-spin wager is out of range.");
    }

    public int GetSpecialBoostCost(string gameId)
    {
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId)
            return Games.JadeDragon.JadeDragonEngine.CreateGame().SpecialPoints!.ActivationCost;
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        return game.SpecialPoints?.ActivationCost
            ?? throw new ArgumentException(
                $"Game '{game.Id}' does not support a special-point boost.",
                nameof(gameId));
    }

    public decimal GetPointValueInCents(string gameId)
    {
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId)
            return Games.JadeDragon.JadeDragonEngine.CreateGame().Wagering.PointValueInCents;
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        return game.Wagering.PointValueInCents;
    }

}
