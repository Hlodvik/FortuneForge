using System.Collections.Concurrent;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry;

/// <summary>Complete Gold Foundry outcome engine with no host or persistence dependency.</summary>
public sealed class GoldFoundryEngine(ISlotDefinitionSource definitions, IGameRandomSource random)
{
    public const string GameId = GoldFoundrySlotDefinitions.GameId;
    public const string Version = "gold-foundry-engine-v2";
    public const string LegacyVersion = "gold-foundry-reel-collector-v1";

    private readonly ConcurrentDictionary<string, PityState> pityStates = new(StringComparer.Ordinal);
    private readonly GoldFoundryReelGenerator reelGenerator = new(random);
    private readonly GoldFoundryCombinationEvaluator evaluator = new();
    private readonly GoldFoundryPayoutCalculator payoutCalculator = new();

    public GoldFoundryEngine(IGameRandomSource random) : this(new GoldFoundryDefinitionSource(), random) { }

    public static GoldFoundryProfile Profile => GoldFoundrySlotDefinitions.Profile;
    public static GameDefinition CreateGame() => GoldFoundrySlotDefinitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => GoldFoundrySlotDefinitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => GoldFoundrySlotDefinitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => GoldFoundrySlotDefinitions.CreatePaytable();

    public void ValidateRequest(long wagerPoints, bool usesAwardedPlay = false)
    {
        GoldFoundrySlotDefinitions.ValidateSpinRequest(usesAwardedPlay);
        ValidateWager(GoldFoundrySlotDefinitions.CreateGame(), wagerPoints);
    }

    public GoldFoundrySpinOutcome Spin(long wagerPoints, string playerId, bool specialBoostApplied = false,
        long currentEnergyBalance = 0, string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        GoldFoundrySlotDefinitions.ValidateSpinRequest(isFreeSpin);
        if (specialBoostApplied)
            throw new ArgumentException("Gold Foundry does not support a special-point boost.",
                nameof(specialBoostApplied));
        if (featureMode is not null)
            throw new ArgumentException("Gold Foundry does not support a feature mode.", nameof(featureMode));
        if (currentEnergyBalance < 0)
            throw new ArgumentOutOfRangeException(nameof(currentEnergyBalance));

        var game = definitions.GetGame(GameId)
            ?? throw new KeyNotFoundException($"Game '{GameId}' was not found.");
        if (!GoldFoundrySlotDefinitions.IsModel(game))
            throw new InvalidOperationException("Gold Foundry requires its versioned game model.");
        ValidateWager(game, wagerPoints);
        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{game.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{game.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{game.Math.PaytableId}' was not found.");
        if (paytable.SymbolSetId != symbolSet.Id || reelSet.SymbolSetId != symbolSet.Id)
            throw new InvalidOperationException("Gold Foundry's reel set and paytable must use its symbol set.");

        var pity = pityStates.GetOrAdd(playerId, static _ => new PityState());
        lock (pity.SyncRoot)
        {
            if (persistedPityMisses is { } persisted)
            {
                if (persisted < 0) throw new ArgumentOutOfRangeException(nameof(persistedPityMisses));
                pity.ConsecutiveFiveMisses = persisted;
            }
            return SpinLocked(game, reelSet, symbolSet, paytable, wagerPoints, currentEnergyBalance, pity);
        }
    }

    private GoldFoundrySpinOutcome SpinLocked(GameDefinition game, ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet, PaytableDefinition paytable, long wagerPoints,
        long currentEnergyBalance, PityState pity)
    {
        var pityBefore = pity.ConsecutiveFiveMisses;
        var outcome = reelGenerator.Generate(game, reelSet, symbolSet);
        var evaluations = evaluator.Evaluate(outcome.VisibleReels, game, symbolSet);
        pity.ConsecutiveFiveMisses = HasPayingFullMatch(evaluations, game, paytable)
            ? 0 : checked(pity.ConsecutiveFiveMisses + 1);

        var payout = payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints);
        var (collectorCount, collectorPoints) = GoldFoundryCollector.Calculate(outcome.VisibleReels, wagerPoints);
        var hammers = GoldFoundryCollector.CalculateHammerPayouts(outcome.VisibleReels, wagerPoints);
        payout = new SpinPayout(
            checked(payout.TotalPoints + collectorPoints + hammers.Sum(line => line.AmountPoints)),
            [.. payout.Paylines, .. hammers]);
        var energy = outcome.VisibleReels.Sum(reel => reel.Count(symbol =>
            symbol == game.Energy!.SymbolId)) * game.Energy!.PointsPerVisibleSymbol;
        var audit = new GoldFoundryEngineAudit(
            Version, DateTime.UtcNow, game, game, reelSet, symbolSet, paytable, Profile,
            pityBefore, currentEnergyBalance, null, 1, outcome.VisibleReels, outcome.VisibleReels,
            false, GoldFoundryCrucibleRules.RuleVersion);
        return new(Guid.NewGuid(), game.Id, reelSet.Id, symbolSet.Id, paytable.Id,
            wagerPoints, game.Wagering.PointValueInCents, outcome.StopIndexes, outcome.VisibleReels,
            payout, pity.ConsecutiveFiveMisses, false, 0, 0, energy, false, false,
            collectorCount, collectorPoints, hammers.Sum(line => line.AmountPoints),
            new Dictionary<string, int>(StringComparer.Ordinal), audit);
    }

    internal static IReadOnlyList<PaylineEvaluation> Evaluate(IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game, SymbolSetDefinition symbols) =>
        new GoldFoundryCombinationEvaluator().Evaluate(reels, game, symbols);

    internal static SpinPayout CalculatePayout(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable, long wagerPoints) =>
        new GoldFoundryPayoutCalculator().Calculate(evaluations, game, paytable, wagerPoints);

    internal static bool HasPayingFullMatch(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable) => evaluations.Any(evaluation =>
        evaluation.Candidates.Any(candidate => candidate.Matches.Any(match =>
            match.MatchLength == game.Layout.ReelCount && paytable.Rules.Any(rule =>
                rule.SymbolId == match.SymbolId && rule.MatchLength == match.MatchLength && rule.Multiplier > 0))));

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints ||
            game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "The Gold Foundry wager is out of range.");
        var allowed = game.Wagering.AllowedWagerPoints;
        var matchesIncrement = game.Wagering.WagerIncrementPoints is { } increment &&
            (wagerPoints - game.Wagering.MinimumWagerPoints) % increment == 0;
        if (allowed.Count > 0 ? !allowed.Contains(wagerPoints) : !matchesIncrement)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "The Gold Foundry wager is unavailable.");
    }

    private sealed class PityState
    {
        internal object SyncRoot { get; } = new();
        internal int ConsecutiveFiveMisses { get; set; }
    }
}
