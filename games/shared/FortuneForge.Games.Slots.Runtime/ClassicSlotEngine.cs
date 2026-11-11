using System.Collections.Concurrent;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

/// <summary>Host-independent engine for the frozen classic slot model.</summary>
public sealed partial class ClassicSlotEngine
{
    public const string RuntimeVersion = "classic-slots-runtime-v1";
    private const int MaximumPityGenerationAttempts = 10_000;
    private readonly FrozenSlotDefinitions definitions;
    private readonly SlotProfile profile;
    private readonly IGameRandomSource random;
    private readonly string engineVersion;
    private readonly ClassicSlotEngineOptions options;
    private readonly ConcurrentDictionary<string, PityState> pityStates = new(StringComparer.Ordinal);

    public ClassicSlotEngine(FrozenSlotDefinitions definitions, SlotProfile profile,
        IGameRandomSource random) : this(definitions, profile, random, RuntimeVersion) { }

    public ClassicSlotEngine(FrozenSlotDefinitions definitions, SlotProfile profile,
        IGameRandomSource random, string engineVersion,
        ClassicSlotEngineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        this.definitions = definitions.CreateSnapshot();
        this.profile = profile;
        this.random = random;
        this.engineVersion = engineVersion;
        this.options = options ?? new ClassicSlotEngineOptions();
        ValidateDefinitions(this.definitions, profile);
    }

    public void ValidateWager(long wagerPoints) => ValidateWager(definitions.Game, wagerPoints);

    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (isFreeSpin && options.AllowFreeSpinCarryWager)
            ValidateCarryWager(definitions.Game, wagerPoints);
        else
            ValidateWager(wagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(currentEnergyBalance);
        if (featureMode is not null && !SlotProfile.IsFeatureMode(featureMode))
            throw new ArgumentException("The feature mode is not recognized by the classic slot runtime.",
                nameof(featureMode));

        var pity = pityStates.GetOrAdd(playerId, static _ => new PityState());
        lock (pity.SyncRoot)
        {
            if (persistedPityMisses is { } persisted)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(persisted);
                pity.ConsecutiveFiveMisses = persisted;
            }
            return SpinLocked(wagerPoints, pity, specialBoostApplied, currentEnergyBalance,
                featureMode, isFreeSpin);
        }
    }

    private ClassicSlotSpinOutcome SpinLocked(long wagerPoints, PityState pity,
        bool specialBoostApplied, long currentEnergyBalance, string? featureMode,
        bool isFreeSpin)
    {
        var game = definitions.CreateGame();
        var symbolSet = definitions.CreateSymbolSet();
        var reelSet = definitions.CreateReelSet();
        var paytable = definitions.CreatePaytable();
        var effectiveGame = CreateEffectiveGame(game, featureMode);
        var effectiveReels = specialBoostApplied
            ? CreateBoostedReels(game, reelSet) : reelSet;
        var pityBefore = pity.ConsecutiveFiveMisses;
        var pityTriggered = string.IsNullOrWhiteSpace(featureMode) &&
            game.Math.FiveMatchPityMissLimit is { } limit && pityBefore >= limit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;

        ReelOutcome? raw = null;
        ReelOutcome? frequency = null;
        ReelOutcome? outcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;
        var attempts = 0;
        for (; attempts < maximumAttempts; attempts++)
        {
            raw = Generate(effectiveGame, effectiveReels, symbolSet);
            frequency = ApplyFreeGameFrequency(raw, effectiveGame);
            outcome = ApplyFeatureSymbols(frequency, effectiveGame, currentEnergyBalance, featureMode);
            evaluations = Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable)) break;
        }
        attempts++;

        if (raw is null || frequency is null || outcome is null || evaluations is null ||
            pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable))
            throw new InvalidOperationException("The frozen slot model could not generate its required pity outcome.");

        var hasFiveMatch = HasPayingFullMatch(evaluations, effectiveGame, paytable);
        pity.ConsecutiveFiveMisses = hasFiveMatch ? 0 : checked(pity.ConsecutiveFiveMisses + 1);
        var payout = CalculatePayout(evaluations, effectiveGame, paytable, wagerPoints);
        var feature = options.FeaturePayoutCalculator?.Invoke(
            outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier)
            ?? CalculateFeaturePayout(outcome.VisibleReels, wagerPoints,
                game.Math.PayoutMultiplier);
        payout = AddFeaturePayout(payout, feature);
        var freeSpins = CountSymbols(outcome.VisibleReels, effectiveGame.FreeGames?.SymbolId) >=
            (effectiveGame.FreeGames?.RequiredSymbols ?? int.MaxValue)
                ? effectiveGame.FreeGames?.AwardedSpins ?? 0 : 0;
        var specialPoints = CalculateSpecialPoints(evaluations, effectiveGame);
        var energy = CountSymbols(outcome.VisibleReels, effectiveGame.Energy?.SymbolId) *
            (effectiveGame.Energy?.PointsPerVisibleSymbol ?? 0);
        var collections = CountCollections(outcome.VisibleReels);
        var audit = new ClassicSlotEngineAudit(engineVersion, DateTime.UtcNow,
            SlotDefinitionCloner.Clone(game), SlotDefinitionCloner.Clone(effectiveGame),
            SlotDefinitionCloner.Clone(effectiveReels), SlotDefinitionCloner.Clone(symbolSet),
            SlotDefinitionCloner.Clone(paytable), profile, pityBefore, currentEnergyBalance,
            featureMode, attempts, Freeze(raw.VisibleReels), Freeze(frequency.VisibleReels));

        return new ClassicSlotSpinOutcome(Guid.NewGuid(), game.Id, reelSet.Id, symbolSet.Id,
            paytable.Id, wagerPoints, game.Wagering.PointValueInCents,
            outcome.StopIndexes.ToArray(), Freeze(outcome.VisibleReels), payout,
            pity.ConsecutiveFiveMisses, pityTriggered, freeSpins, specialPoints, energy,
            specialBoostApplied, isFreeSpin, feature.PawCount, feature.CollectorPoints,
            feature.PatternBonusPoints, collections, audit);
    }

    private ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet)
    {
        if (reelSet.SymbolSetId != symbolSet.Id || reelSet.Reels.Count != game.Layout.ReelCount)
            throw new InvalidOperationException("The reel set is incompatible with the frozen game definition.");
        var known = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var stops = new int[game.Layout.ReelCount];
        var reels = new List<IReadOnlyList<string>>(game.Layout.ReelCount);
        for (var reel = 0; reel < game.Layout.ReelCount; reel++)
        {
            IReadOnlyList<string> strip = reelSet.Reels[reel];
            if (game.Energy is null && !profile.PreserveEnergySymbolsWithoutMeter)
                strip = strip.Where(symbol => symbol != "BOLT").ToArray();
            if (strip.Count < game.Layout.VisibleRows || strip.Any(symbol => !known.Contains(symbol)))
                throw new InvalidOperationException("A frozen reel strip is invalid.");
            var stop = Next(strip.Count);
            stops[reel] = stop;
            reels.Add(Enumerable.Range(0, game.Layout.VisibleRows)
                .Select(row => strip[(stop + row) % strip.Count]).ToArray());
        }
        return new(stops, reels);
    }

    private ReelOutcome ApplyFreeGameFrequency(ReelOutcome outcome, GameDefinition game)
    {
        var freeGames = game.FreeGames;
        if (freeGames is null || freeGames.VisibleFrequencyDivisor <= 1) return outcome;
        var replacements = game.SpecialPoints?.CommonSymbolIds;
        if (replacements is null || replacements.Count == 0)
            throw new InvalidOperationException("Free-game frequency requires replacement symbols.");
        var reels = outcome.VisibleReels.Select(reel => (IReadOnlyList<string>)reel.Select(symbol =>
            symbol == freeGames.SymbolId && Next(freeGames.VisibleFrequencyDivisor) != 0
                ? replacements[Next(replacements.Count)] : symbol).ToArray()).ToArray();
        return outcome with { VisibleReels = reels };
    }

    private int Next(int maximumExclusive)
    {
        if (maximumExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        var value = random.Next(maximumExclusive);
        return value >= 0 && value < maximumExclusive
            ? value : throw new InvalidOperationException("The random source returned an invalid index.");
    }

    private static void ValidateDefinitions(FrozenSlotDefinitions value, SlotProfile profile)
    {
        if (value.Game.Id != profile.GameId || value.Game.Symbols.SymbolSetId != value.SymbolSet.Id ||
            value.Game.Math.ReelSetId != value.ReelSet.Id ||
            value.Game.Math.PaytableId != value.Paytable.Id ||
            value.ReelSet.SymbolSetId != value.SymbolSet.Id ||
            value.Paytable.SymbolSetId != value.SymbolSet.Id)
            throw new ArgumentException("The frozen slot bundle is internally inconsistent.", nameof(value));
    }

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints ||
            game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
        var allowed = game.Wagering.AllowedWagerPoints;
        var increment = game.Wagering.WagerIncrementPoints;
        if (allowed.Count > 0 ? !allowed.Contains(wagerPoints) :
            increment is null || (wagerPoints - game.Wagering.MinimumWagerPoints) % increment != 0)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
    }

    private static void ValidateCarryWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints ||
            game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
    }

    private static IReadOnlyList<IReadOnlyList<string>> Freeze(
        IReadOnlyList<IReadOnlyList<string>> reels) =>
        reels.Select(reel => (IReadOnlyList<string>)reel.ToArray()).ToArray();

    private sealed class PityState
    {
        public object SyncRoot { get; } = new();
        public int ConsecutiveFiveMisses { get; set; }
    }
}
