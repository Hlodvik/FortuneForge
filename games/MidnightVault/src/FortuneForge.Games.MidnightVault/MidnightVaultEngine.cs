using System.Collections.Concurrent;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

/// <summary>Complete Midnight Vault outcome engine, independent of the Fortune Forge host.</summary>
public sealed class MidnightVaultEngine(
    IGameRandomSource random,
    IMidnightVaultReelSource? reelSource = null)
{
    public const string GameId = MidnightVaultSlotDefinitions.GameId;
    public const string Version = "midnight-vault-engine-v1";
    private const int MaximumPityGenerationAttempts = 10_000;
    private static readonly string[] CommonReplacementSymbols = ["2", "3", "4", "5", "6"];
    private static readonly string[] SealSymbolIds = ["SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"];
    private readonly ConcurrentDictionary<string, PityState> pityStates = new(StringComparer.Ordinal);
    private readonly IMidnightVaultReelSource reelGenerator = reelSource ?? new MidnightVaultReelGenerator(random);
    private readonly MidnightVaultCombinationEvaluator evaluator = new();
    private readonly MidnightVaultPayoutCalculator payoutCalculator = new();

    public static GameDefinition CreateGame() => MidnightVaultSlotDefinitions.CreateGame();
    public static GameDefinition CreateGame(string mathModelId) => MidnightVaultSlotDefinitions.CreateGameForPaytable(mathModelId);
    public static SymbolSetDefinition CreateSymbolSet() => MidnightVaultSlotDefinitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet(string mathModelId = MidnightVaultSlotDefinitions.PaytableId) =>
        MidnightVaultBaseReelSet.CreateForPaytable(mathModelId);
    public static PaytableDefinition CreatePaytable(string mathModelId = MidnightVaultSlotDefinitions.PaytableId) =>
        MidnightVaultSlotDefinitions.CreatePaytable(mathModelId);
    public static MidnightVaultProfile Profile { get; } = MidnightVaultSlotDefinitions.CreateProfile();

    public void ValidateRequest(long wagerPoints, bool usesAwardedPlay)
    {
        if (usesAwardedPlay) MidnightVaultBonusPolicy.ValidateDerivedWager(wagerPoints);
        else MidnightVaultBonusPolicy.ValidateCashWager(wagerPoints);
    }

    public MidnightVaultSpinOutcome Spin(long wagerPoints, string playerId, bool specialBoostApplied = false,
        long currentEnergyBalance = 0, string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false, string? mathModelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var model = mathModelId ?? MidnightVaultSlotDefinitions.PaytableId;
        _ = MidnightVaultBonusPolicy.ResolveModel(model);
        MidnightVaultBonusPolicy.ValidateFeatureMode(model, featureMode);
        ValidateRequest(wagerPoints, isFreeSpin);
        if (currentEnergyBalance < 0) throw new ArgumentOutOfRangeException(nameof(currentEnergyBalance));

        var state = pityStates.GetOrAdd($"{GameId}\u001f{playerId}", static _ => new PityState());
        lock (state.SyncRoot)
        {
            if (persistedPityMisses is { } persisted)
            {
                if (persisted < 0) throw new ArgumentOutOfRangeException(nameof(persistedPityMisses));
                state.ConsecutiveFiveMisses = persisted;
            }
            return SpinWithPity(wagerPoints, specialBoostApplied, currentEnergyBalance, featureMode,
                isFreeSpin, model, state);
        }
    }

    private MidnightVaultSpinOutcome SpinWithPity(long wagerPoints, bool specialBoostApplied,
        long currentEnergyBalance, string? featureMode, bool isFreeSpin, string model, PityState pityState)
    {
        var game = MidnightVaultSlotDefinitions.CreateGameForPaytable(model);
        var effectiveGame = CreateEffectiveGame(game, featureMode);
        var symbolSet = MidnightVaultSlotDefinitions.CreateSymbolSet();
        var paytable = MidnightVaultSlotDefinitions.CreatePaytable(model);
        var baseReelSet = MidnightVaultBaseReelSet.CreateForPaytable(model);
        var reelSet = specialBoostApplied
            ? MidnightVaultSpecialPoints.CreateBoostedReelSet(game, baseReelSet) : baseReelSet;
        var pityTriggered = string.IsNullOrWhiteSpace(featureMode) &&
            game.Math.FiveMatchPityMissLimit is { } pityLimit && pityState.ConsecutiveFiveMisses >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        var pityBefore = pityState.ConsecutiveFiveMisses;
        ReelOutcome? raw = null;
        ReelOutcome? frequency = null;
        ReelOutcome? outcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;
        var attempts = 0;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            attempts++;
            raw = reelGenerator.Generate(effectiveGame, reelSet, symbolSet);
            frequency = MidnightVaultFreeGameFrequency.Apply(raw, effectiveGame, random);
            outcome = ApplyFeatureSymbols(frequency, currentEnergyBalance, featureMode);
            evaluations = evaluator.Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable)) break;
        }
        if (raw is null || frequency is null || outcome is null || evaluations is null ||
            pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable))
            throw new InvalidOperationException("Midnight Vault could not generate its pity outcome.");

        var hasFive = HasPayingFullMatch(evaluations, effectiveGame, paytable);
        pityState.ConsecutiveFiveMisses = hasFive ? 0 : checked(pityState.ConsecutiveFiveMisses + 1);
        var payout = payoutCalculator.Calculate(evaluations, effectiveGame, paytable, wagerPoints);
        var bananaPayouts = CalculateBananaPayouts(outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier);
        if (bananaPayouts.Count > 0)
            payout = new SpinPayout(checked(payout.TotalPoints + bananaPayouts.Sum(line => line.AmountPoints)),
                [.. payout.Paylines, .. bananaPayouts]);
        var freeSpins = CountSymbols(outcome.VisibleReels, game.FreeGames!.SymbolId) >= game.FreeGames.RequiredSymbols
            ? game.FreeGames.AwardedSpins : 0;
        var specialPoints = MidnightVaultSpecialPoints.CalculateAward(evaluations, effectiveGame);
        var spinId = Guid.NewGuid();
        var audit = new MidnightVaultEngineAudit(Version, DateTime.UtcNow, game, effectiveGame, reelSet,
            symbolSet, paytable, MidnightVaultSlotDefinitions.CreateProfile(model), pityBefore,
            currentEnergyBalance, featureMode, attempts, raw.VisibleReels, frequency.VisibleReels, isFreeSpin);
        return new MidnightVaultSpinOutcome(spinId, GameId, reelSet.Id, symbolSet.Id, paytable.Id,
            wagerPoints, game.Wagering.PointValueInCents, outcome.StopIndexes, outcome.VisibleReels, payout,
            pityState.ConsecutiveFiveMisses, pityTriggered, freeSpins, specialPoints, 0,
            specialBoostApplied, isFreeSpin, bananaPayouts.Sum(line => line.AmountPoints),
            CountSeals(outcome.VisibleReels), audit);
    }

    internal static GameDefinition CreateEffectiveGame(GameDefinition game, string? featureMode)
    {
        if (!HasMode(featureMode, "rows")) return game;
        return new GameDefinition
        {
            Id = game.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = game.Layout.ReelCount,
                VisibleRows = checked(game.Layout.VisibleRows + 2),
                PaylineCount = game.Layout.PaylineCount
            },
            Symbols = game.Symbols,
            Matching = game.Matching,
            Math = game.Math,
            Wagering = game.Wagering,
            FreeGames = game.FreeGames,
            SpecialPoints = game.SpecialPoints,
            Energy = game.Energy,
            Paylines = game.Paylines
        };
    }

    private ReelOutcome ApplyFeatureSymbols(ReelOutcome outcome, long currentEnergyBalance, string? featureMode)
    {
        var reels = outcome.VisibleReels.Select(reel => reel.ToArray()).ToArray();
        if (HasMode(featureMode, "sync"))
        {
            var source = random.Next(reels.Length);
            var destination = (source + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destination] = reels[source].ToArray();
        }
        if (HasMode(featureMode, "rand")) FillWildReel(reels);
        if (HasMode(featureMode, "paw")) InjectSymbols(reels, 2, () => "ACE");
        InjectSymbols(reels, RollBananaCount(), () => "BANANA");
        InjectSymbols(reels, RollSealCount(currentEnergyBalance), PickSealSymbol);
        return outcome with { VisibleReels = reels.Select(reel => (IReadOnlyList<string>)reel).ToArray() };
    }

    private static bool HasMode(string? featureMode, string mode) => !string.IsNullOrWhiteSpace(featureMode) &&
        featureMode.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(mode, StringComparer.Ordinal);

    private int RollBananaCount() => random.Next(100) switch { < 48 => 0, < 88 => 1, < 98 => 2, _ => 3 };
    private int RollSealCount(long energy)
    {
        var chance = energy switch { >= 75 => 67, >= 50 => 50, >= 25 => 40, _ => 33 };
        if (random.Next(100) >= chance) return 0;
        return random.Next(25) == 0 ? 2 : 1;
    }
    private string PickSealSymbol() => SealSymbolIds[random.Next(SealSymbolIds.Length)];

    private void FillWildReel(IReadOnlyList<string[]> reels)
    {
        var reel = reels[random.Next(reels.Count)];
        for (var row = 0; row < reel.Length; row++) reel[row] = "ACE";
    }

    private void InjectSymbols(IReadOnlyList<string[]> reels, int count, Func<string> symbolFactory)
    {
        for (var index = 0; index < count; index++)
        {
            var candidates = reels.SelectMany((reel, reelIndex) => reel.Select((symbol, rowIndex) =>
                    new { Symbol = symbol, Position = new GridPosition(reelIndex, rowIndex) }))
                .Where(candidate => CommonReplacementSymbols.Contains(candidate.Symbol, StringComparer.Ordinal))
                .Select(candidate => candidate.Position).ToArray();
            if (candidates.Length == 0) return;
            var position = candidates[random.Next(candidates.Length)];
            reels[position.Reel][position.Row] = symbolFactory();
        }
    }

    internal static IReadOnlyList<PaylinePayout> CalculateBananaPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints, decimal payoutMultiplier)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Max(reel => reel.Count);
        var paylineId = 801;
        void Add(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => position.Reel < 0 || position.Reel >= reels.Count ||
                position.Row < 0 || position.Row >= reels[position.Reel].Count ||
                reels[position.Reel][position.Row] != "BANANA")) return;
            var key = string.Join('|', positions.OrderBy(position => position.Reel).ThenBy(position => position.Row)
                .Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key)) return;
            var amount = checked((long)Math.Round(checked(wagerPoints * 3) * payoutMultiplier,
                MidpointRounding.AwayFromZero));
            var current = paylineId++;
            payouts.Add(new(current, amount,
                [new PaidMatch(new SymbolMatch(current, "BANANA", 3, positions.ToArray(), []), 3, amount)]));
        }
        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                Add([new(reel, row), new(reel, row + 1), new(reel, row + 2)]);
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                Add([new(reel, row), new(reel + 1, row), new(reel + 2, row)]);
        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
                Add([new(reel, row), new(reel + 1, row + 1), new(reel + 2, row + 2)]);
            for (var row = 2; row < rows; row++)
                Add([new(reel, row), new(reel + 1, row - 1), new(reel + 2, row - 2)]);
        }
        return payouts;
    }

    internal static bool HasPayingFullMatch(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable) => evaluations.Any(evaluation =>
        evaluation.Candidates.Any(candidate => candidate.Matches.Any(match =>
            match.MatchLength == game.Layout.ReelCount && paytable.Rules.Any(rule =>
                rule.SymbolId == match.SymbolId && rule.MatchLength == match.MatchLength && rule.Multiplier > 0))));

    internal static int CountSymbols(IReadOnlyList<IReadOnlyList<string>> reels, string symbol) =>
        reels.Sum(reel => reel.Count(value => value == symbol));

    internal static IReadOnlyDictionary<string, int> CountSeals(IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var counts = SealSymbolIds.ToDictionary(symbol => symbol, _ => 0, StringComparer.Ordinal);
        foreach (var symbol in reels.SelectMany(reel => reel))
            if (counts.ContainsKey(symbol)) counts[symbol]++;
        return counts.Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    internal static IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game, SymbolSetDefinition symbols) =>
        new MidnightVaultCombinationEvaluator().Evaluate(reels, game, symbols);

    internal static SpinPayout CalculatePayout(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable, long wager) =>
        new MidnightVaultPayoutCalculator().Calculate(evaluations, game, paytable, wager);

    private sealed class PityState
    {
        internal object SyncRoot { get; } = new();
        internal int ConsecutiveFiveMisses { get; set; }
    }
}
