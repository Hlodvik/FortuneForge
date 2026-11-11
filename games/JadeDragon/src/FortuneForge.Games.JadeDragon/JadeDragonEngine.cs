using System.Text.Json;
using FortuneForge.Games.JadeDragon.Bonuses;
using FortuneForge.Games.JadeDragon.Configuration;
using FortuneForge.Games.JadeDragon.Evaluation;
using FortuneForge.Games.JadeDragon.Models;
using FortuneForge.Games.JadeDragon.Payouts;
using FortuneForge.Games.JadeDragon.Reels;
using FortuneForge.Games.JadeDragon.Spins;

namespace FortuneForge.Games.JadeDragon;

/// <summary>Jade Dragon math with all inputs bundled in this assembly.</summary>
/// <remarks>
/// The caller owns balances, entitlements and pity state. Production randomness
/// uses RandomNumberGenerator; supply IRandomIndexSource only for reproducible tests.
/// </remarks>
public sealed class JadeDragonEngine
{
    public const string GameId = "empty-slots-v1";
    public const string SourceCommit = "ae40278";
    public const string Version = "empty-slots-engine-v1";
    private readonly IRandomIndexSource random;
    private readonly FrozenDefinitions definitions = new();

    public JadeDragonEngine(IGameRandomSource? random = null) =>
        this.random = random is null ? new CryptoRandomIndexSource() : new RandomAdapter(random);

    // Return separate snapshots so caller edits cannot change live engine math.
    public GameDefinition Definition => Snapshot(definitions.Game);
    public ReelSetDefinition ReelSet => Snapshot(definitions.ReelSet);
    public SymbolSetDefinition SymbolSet => Snapshot(definitions.SymbolSet);
    public PaytableDefinition Paytable => Snapshot(definitions.Paytable);
    public IReadOnlyList<JadeDragonSymbolMetadata> SymbolMetadata =>
        JadeDragonSymbols.CreateMetadata(definitions.Game, definitions.SymbolSet,
            definitions.ReelSet, definitions.Paytable);
    public GameDefinition GetEffectiveDefinition(string? mode)
    {
        ValidateFeatureMode(mode);
        return Snapshot(SpinService.CreateEffectiveSpinGame(definitions.Game, mode));
    }
    public ReelSetDefinition GetEffectiveReelSet(bool boost) => Snapshot(boost
        ? SpecialPointBonus.CreateBoostedReelSet(definitions.Game, definitions.ReelSet)
        : definitions.ReelSet);
    public SlotSpecialRoundProfile Profile => JadeDragonRules.TryGet(GameId, out var profile)
        ? profile : throw new InvalidOperationException("Jade Dragon profile is unavailable.");

    public void ValidateWager(long wagerPoints) => CreateSpinService().ValidateRequest(GameId, wagerPoints);

    public SpinResult Spin(long wagerPoints, string playerId, bool specialBoostApplied = false,
        int pityMisses = 0, string? freeSpinFeatureMode = null, bool isFreeSpin = false,
        long currentEnergyBalance = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pityMisses);
        if (freeSpinFeatureMode is not null &&
            (!isFreeSpin || freeSpinFeatureMode != Profile.CollectionFeatureMode))
            throw new ArgumentException("Only an awarded Jade Dragon collection feature may supply a feature mode.",
                nameof(freeSpinFeatureMode));
        // A fresh service makes the supplied persisted pity count authoritative
        // without retaining an unbounded player dictionary in the outcome engine.
        return CreateSpinService().Spin(GameId, wagerPoints, playerId, specialBoostApplied,
            currentEnergyBalance: currentEnergyBalance, freeSpinFeatureMode: freeSpinFeatureMode,
            persistedPityMisses: pityMisses, isFreeSpin: isFreeSpin);
    }

    public SlotSpecialRoundProgress SettleCollections(IReadOnlyList<SlotCollectionProgress> current,
        IReadOnlyDictionary<string, int> awarded, long wagerPoints) =>
        JadeDragonRules.SettleDemo(Profile, current, awarded, wagerPoints, energyCompleted: false);

    public JadeDragonEvaluation EvaluateOutcome(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, string? freeSpinFeatureMode = null)
    {
        ValidateFeatureMode(freeSpinFeatureMode);
        return CreateSpinService().EvaluateOutcome(reels, wagerPoints, freeSpinFeatureMode);
    }

    private void ValidateFeatureMode(string? mode)
    {
        if (mode is not null && mode != Profile.CollectionFeatureMode)
            throw new ArgumentException("Unknown Jade Dragon feature mode.", nameof(mode));
    }

    private SpinService CreateSpinService() => new(definitions, new CryptoReelGenerator(random),
        new CombinationEvaluator(), new PayoutCalculator(), random);

    private static T Snapshot<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private sealed class RandomAdapter(IGameRandomSource source) : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => source.Next(maximumExclusive);
    }

    private sealed class FrozenDefinitions : ISlotsDefinitionProvider
    {
        public GameDefinition Game { get; }
        public SymbolSetDefinition SymbolSet { get; }
        public ReelSetDefinition ReelSet { get; }
        public PaytableDefinition Paytable { get; }

        public FrozenDefinitions()
        {
            using var stream = typeof(JadeDragonEngine).Assembly.GetManifestResourceStream(
                "FortuneForge.Games.JadeDragon.Data.jade-dragon.json")
                ?? throw new InvalidOperationException("The frozen Jade Dragon math is missing.");
            var frozen = JsonSerializer.Deserialize<FrozenData>(stream)
                ?? throw new InvalidOperationException("The frozen Jade Dragon math is invalid.");
            Game = frozen.Game;
            SymbolSet = frozen.SymbolSet;
            ReelSet = frozen.ReelSet;
            Paytable = frozen.Paytable;
        }

        public GameDefinition? GetGame(string id) => id == GameId ? Game : null;
        public SymbolSetDefinition? GetSymbolSet(string id) => id == SymbolSet.Id ? SymbolSet : null;
        public ReelSetDefinition? GetReelSet(string id) => id == ReelSet.Id ? ReelSet : null;
        public PaytableDefinition? GetPaytable(string id) => id == Paytable.Id ? Paytable : null;
    }

    private sealed record FrozenData(GameDefinition Game, SymbolSetDefinition SymbolSet,
        ReelSetDefinition ReelSet, PaytableDefinition Paytable);
}
