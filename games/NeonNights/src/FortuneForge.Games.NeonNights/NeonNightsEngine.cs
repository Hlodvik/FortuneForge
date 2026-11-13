using System.Collections.Concurrent;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

// Neon owns its grid, feature transformations, bonus awards, and payout assembly.
// The only shared dependencies are data contracts and an injected random source.
public sealed class NeonNightsEngine(ISlotDefinitionSource definitions, IGameRandomSource random)
{
    private readonly NeonReelGenerator reelGenerator = new(random);
    private readonly NeonCombinationEvaluator combinationEvaluator = new();
    private readonly NeonPayoutCalculator payoutCalculator = new();

    public NeonNightsEngine(IGameRandomSource random) : this(new NeonDefinitionSource(), random) { }

    private const int MaximumPityGenerationAttempts = 10_000;
    private static readonly string[] ReplacementSymbols = ["2", "3", "4", "5", "6"];
    private readonly ConcurrentDictionary<string, PityState> pityStates = new(StringComparer.Ordinal);

    public void ValidateRequest(long wagerPoints, bool usesAwardedPlay = false)
    {
        if (usesAwardedPlay) NeonArcadeSlotDefinitions.ValidateCarryWager(wagerPoints);
        else ValidateWager(definitions.GetGame(NeonArcadeSlotDefinitions.GameId)
            ?? throw new KeyNotFoundException("The Neon Nights definition was not found."), wagerPoints);
    }

    public NeonSpinOutcome Spin(
        long wagerPoints,
        string playerId,
        bool specialBoostApplied,
        long currentEnergyBalance,
        string? featureMode,
        int? persistedPityMisses,
        bool isFreeSpin,
        bool isNeonDiscoPlay = false,
        int neonDiscoMultiplier = NeonDiscoCascade.InitialMultiplier)
    {
        if (isNeonDiscoPlay && (isFreeSpin || featureMode is not null || specialBoostApplied))
            throw new ArgumentException("A Disco cascade requires its own paid play state.", nameof(isNeonDiscoPlay));
        if (!NeonDiscoCascade.IsValidMultiplier(neonDiscoMultiplier) ||
            !isNeonDiscoPlay && neonDiscoMultiplier != NeonDiscoCascade.InitialMultiplier)
            throw new ArgumentOutOfRangeException(nameof(neonDiscoMultiplier));
        var game = definitions.GetGame(NeonArcadeSlotDefinitions.GameId)
            ?? throw new KeyNotFoundException($"Game '{NeonArcadeSlotDefinitions.GameId}' was not found.");
        if (!NeonArcadeSlotDefinitions.IsModel(game))
            throw new InvalidOperationException("Neon Nights requires its versioned reel model.");
        if (isFreeSpin)
            NeonArcadeSlotDefinitions.ValidateCarryWager(wagerPoints);
        else
            ValidateWager(game, wagerPoints);
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("An authenticated player id is required.", nameof(playerId));

        var effectiveGame = CreateEffectiveGame(game, featureMode, isFreeSpin);
        var symbolSet = definitions.GetSymbolSet(effectiveGame.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{effectiveGame.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(effectiveGame.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{effectiveGame.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(effectiveGame.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{effectiveGame.Math.PaytableId}' was not found.");
        if (!string.Equals(paytable.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Paytable '{paytable.Id}' is not compatible with symbol set '{symbolSet.Id}'.");
        var effectiveReelSet = specialBoostApplied
            ? NeonSpecialPoints.CreateBoostedReelSet(effectiveGame, reelSet)
            : reelSet;
        var pity = pityStates.GetOrAdd(playerId, static _ => new PityState());
        lock (pity.SyncRoot)
        {
            if (persistedPityMisses is { } persisted)
            {
                if (persisted < 0)
                    throw new ArgumentOutOfRangeException(nameof(persistedPityMisses));
                pity.ConsecutiveFiveMisses = persisted;
            }
            return SpinLocked(game, effectiveGame, effectiveReelSet, symbolSet, paytable, wagerPoints,
                pity, specialBoostApplied, currentEnergyBalance, featureMode, isFreeSpin, isNeonDiscoPlay,
                neonDiscoMultiplier);
        }
    }

    private NeonSpinOutcome SpinLocked(
        GameDefinition game,
        GameDefinition effectiveGame,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet,
        PaytableDefinition paytable,
        long wagerPoints,
        PityState pity,
        bool specialBoostApplied,
        long currentEnergyBalance,
        string? featureMode,
        bool isFreeSpin,
        bool isNeonDiscoPlay,
        int neonDiscoMultiplier)
    {
        if (isNeonDiscoPlay)
            return SpinDiscoLocked(game, wagerPoints, pity, currentEnergyBalance, neonDiscoMultiplier);

        var usesNitroWild = NeonNitroWild.IsCarPlay(isFreeSpin, featureMode);
        var pityMissesBefore = pity.ConsecutiveFiveMisses;
        var pityTriggered = string.IsNullOrWhiteSpace(featureMode) &&
            game.Math.FiveMatchPityMissLimit is { } pityLimit &&
            pityMissesBefore >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        ReelOutcome? rawOutcome = null;
        ReelOutcome? frequencyOutcome = null;
        ReelOutcome? featureOutcome = null;
        IReadOnlyList<IReadOnlyList<string>>? finalReels = null;
        NeonNitroWildOutcome? nitroWild = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;
        var generationAttempts = 0;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            generationAttempts++;
            rawOutcome = reelGenerator.Generate(effectiveGame, reelSet, symbolSet);
            frequencyOutcome = NeonFreeGameFrequency.Apply(rawOutcome, effectiveGame, random);
            featureOutcome = ApplyFeatureSymbols(frequencyOutcome, featureMode);
            (finalReels, nitroWild) = usesNitroWild
                ? NeonNitroWild.Resolve(featureOutcome.VisibleReels, effectiveGame.Symbols.WildSymbolId,
                    NeonArcadeSlotDefinitions.EngineVersion)
                : (featureOutcome.VisibleReels, null);
            evaluations = combinationEvaluator.Evaluate(finalReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable))
                break;
        }

        if (rawOutcome is null || frequencyOutcome is null || featureOutcome is null ||
            finalReels is null || evaluations is null ||
            pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable))
            throw new InvalidOperationException("Neon Nights could not generate a five-match pity outcome.");

        pity.ConsecutiveFiveMisses = HasPayingFullMatch(evaluations, effectiveGame, paytable)
            ? 0 : checked(pity.ConsecutiveFiveMisses + 1);
        var linePayout = payoutCalculator.Calculate(evaluations, effectiveGame, paytable, wagerPoints);
        // Landed side awards remain payable even when Nitro covers one of their symbols.
        var (payout, bonus, sevenPoints) = CalculatePayout(linePayout, featureOutcome.VisibleReels, wagerPoints,
            isFreeSpin, NeonArcadeSlotDefinitions.EngineVersion);
        if (nitroWild?.RequiresChoice == true) payout = new(0, []);
        var specialPointsAwarded = NeonSpecialPoints.CalculateAward(evaluations, effectiveGame);
        var energyAwarded = game.Energy is null ? 0 :
            finalReels.Sum(reel => reel.Count(symbol =>
                string.Equals(symbol, game.Energy.SymbolId, StringComparison.Ordinal))) *
                game.Energy.PointsPerVisibleSymbol;

        return new NeonSpinOutcome(
            Guid.NewGuid(), game.Id, reelSet.Id, symbolSet.Id, paytable.Id,
            wagerPoints, game.Wagering.PointValueInCents, featureOutcome.StopIndexes,
            finalReels, payout, pity.ConsecutiveFiveMisses, pityTriggered,
            bonus.FreeSpinsAwarded, specialPointsAwarded, energyAwarded, specialBoostApplied)
        {
            IsFreeSpin = isFreeSpin,
            BananaBonusPoints = sevenPoints,
            NeonArcade = bonus.Evidence,
            NeonNitroWild = nitroWild,
            EngineAudit = new NeonEngineAudit(
                NeonArcadeSlotDefinitions.EngineVersion, DateTime.UtcNow, game, effectiveGame,
                reelSet, symbolSet, paytable, pityMissesBefore,
                currentEnergyBalance, featureMode, generationAttempts,
                rawOutcome.VisibleReels, frequencyOutcome.VisibleReels, bonus.Evidence, null)
        };
    }

    public static NeonSpinOutcome ResolveNitroChoice(NeonSpinOutcome pending, int selectedReel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedReel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedReel, pending.Reels.Count);
        if (pending.NeonNitroWild?.RequiresChoice != true ||
            NeonOutcomeReplay.Check(pending, pending.Payout).Count != 0)
            throw new InvalidOperationException("The Nitro reel choice does not match a pending spin.");

        var audit = pending.EngineAudit;
        var game = audit.EffectiveGame;
        var landedReels = audit.ReelsAfterFreeGameFrequency;
        var (reels, nitro) = NeonNitroWild.Resolve(landedReels, game.Symbols.WildSymbolId, audit.EngineVersion, selectedReel);
        var evaluations = new NeonCombinationEvaluator().Evaluate(reels, game, audit.SymbolSet, audit.EngineVersion);
        var linePayout = new NeonPayoutCalculator().Calculate(evaluations, game, audit.Paytable, pending.WagerPoints);
        var (payout, bonus, sevens) = CalculatePayout(
            linePayout, landedReels, pending.WagerPoints, pending.IsFreeSpin, audit.EngineVersion);
        var misses = HasPayingFullMatch(evaluations, game, audit.Paytable) ? 0 : checked(audit.PityMissesBefore + 1);
        return pending with
        {
            Reels = reels,
            NeonNitroWild = nitro,
            Payout = payout,
            NeonArcade = bonus.Evidence,
            BananaBonusPoints = sevens,
            FreeSpinsAwarded = bonus.FreeSpinsAwarded,
            SpecialPointsAwarded = NeonSpecialPoints.CalculateAward(evaluations, game),
            ConsecutiveFiveMisses = misses,
            EngineAudit = audit with { NeonArcade = bonus.Evidence }
        };
    }

    private NeonSpinOutcome SpinDiscoLocked(GameDefinition baseGame, long wagerPoints,
        PityState pity, long currentEnergyBalance, int startingMultiplier)
    {
        var game = NeonDiscoDefinitions.CreateGame(baseGame);
        var symbols = definitions.GetSymbolSet(NeonDiscoDefinitions.SymbolSetId)
            ?? NeonDiscoDefinitions.CreateSymbolSet();
        var reels = definitions.GetReelSet(NeonDiscoDefinitions.ReelSetId)
            ?? NeonDiscoDefinitions.CreateReelSet();
        var paytable = definitions.GetPaytable(NeonDiscoDefinitions.PaytableId)
            ?? NeonDiscoDefinitions.CreatePaytable();
        var initial = reelGenerator.Generate(game, reels, symbols);
        var draws = new List<int>();
        int Draw(int maximum)
        {
            var value = random.Next(maximum);
            draws.Add(value);
            return value;
        }
        var cascade = NeonDiscoCascade.Resolve(initial.VisibleReels, game, reels,
            symbols, paytable, wagerPoints, combinationEvaluator, payoutCalculator, Draw, out _, startingMultiplier);
        var payout = new SpinPayout(cascade.TotalPoints, []);
        var bonus = NeonArcadeBonus.Apply(new(0, []), initial.VisibleReels);
        return new(Guid.NewGuid(), baseGame.Id, reels.Id, symbols.Id, paytable.Id,
            wagerPoints, game.Wagering.PointValueInCents, initial.StopIndexes,
            initial.VisibleReels, payout, pity.ConsecutiveFiveMisses, false, 0, 0, 0, false)
        {
            NeonArcade = bonus.Evidence,
            NeonDiscoCascade = cascade,
            EngineAudit = new(NeonArcadeSlotDefinitions.EngineVersion, DateTime.UtcNow,
                baseGame, game, reels, symbols, paytable, pity.ConsecutiveFiveMisses,
                currentEnergyBalance, null, 1, initial.VisibleReels, initial.VisibleReels,
                bonus.Evidence, new NeonDiscoCascadeAudit(draws) { StartingMultiplier = startingMultiplier })
        };
    }

    internal static GameDefinition CreateEffectiveGame(GameDefinition game, string? featureMode, bool usesAwardedPlay)
    {
        if (!NeonPlayPolicy.HasFeatureMode(featureMode, "rows")) return game;

        const int addedRows = 3;
        var visibleRows = checked(game.Layout.VisibleRows + addedRows);
        var extraPaylines = Enumerable.Range(game.Layout.VisibleRows, addedRows)
            .Select(row => Enumerable.Repeat(row, game.Layout.ReelCount).ToList())
            .ToList();
        var extraPayoutSteps = Enumerable.Repeat(0, extraPaylines.Count).ToList();
        var usesNitroWild = NeonNitroWild.IsCarPlay(usesAwardedPlay, featureMode);
        if (usesNitroWild)
        {
            var knownRoutes = game.Paylines.Concat(extraPaylines).Select(rows => string.Join(',', rows)).ToHashSet();
            for (var route = 0; route < game.Paylines.Count; route++)
            {
                var rows = game.Paylines[route];
                var bottom = rows.Max();
                // Add each existing shape where it reaches the expanded area.
                for (var offset = game.Layout.VisibleRows - bottom; offset < visibleRows - bottom; offset++)
                {
                    var shifted = rows.Select(row => row + offset).ToList();
                    if (!knownRoutes.Add(string.Join(',', shifted))) continue;
                    extraPaylines.Add(shifted);
                    extraPayoutSteps.Add(game.Math.PaylinePayoutSteps[route]);
                }
            }
        }
        return new GameDefinition
        {
            Id = game.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = game.Layout.ReelCount,
                VisibleRows = visibleRows,
                PaylineCount = checked(game.Layout.PaylineCount + extraPaylines.Count)
            },
            Symbols = usesNitroWild ? new()
            {
                SymbolSetId = NeonArcadeSlotDefinitions.CarSymbolSetId,
                WildSymbolId = game.Symbols.WildSymbolId,
                NativeWildMatchLengths = game.Symbols.NativeWildMatchLengths,
                WildSubstitutionMatchLengths = game.Symbols.WildSubstitutionMatchLengths,
                PaylineSymbolIds = game.Symbols.PaylineSymbolIds
            } : game.Symbols,
            Matching = game.Matching,
            Math = new GameMathDefinition
            {
                ReelSetId = usesNitroWild ? NeonArcadeSlotDefinitions.CarReelSetId : game.Math.ReelSetId,
                PaytableId = usesNitroWild ? NeonArcadeSlotDefinitions.CarPaytableId : game.Math.PaytableId,
                PayoutMultiplier = usesNitroWild ? NeonArcadeSlotDefinitions.CarPayoutMultiplier : game.Math.PayoutMultiplier,
                PaylinePayoutSteps = [.. game.Math.PaylinePayoutSteps, .. extraPayoutSteps],
                FiveMatchPityMissLimit = game.Math.FiveMatchPityMissLimit,
                Targets = game.Math.Targets
            },
            Wagering = game.Wagering,
            FreeGames = game.FreeGames,
            SpecialPoints = game.SpecialPoints,
            Energy = game.Energy,
            Paylines = [.. game.Paylines, .. extraPaylines]
        };
    }

    private ReelOutcome ApplyFeatureSymbols(ReelOutcome outcome, string? featureMode)
    {
        var reels = outcome.VisibleReels.Select(reel => reel.ToArray()).ToArray();
        if (NeonPlayPolicy.HasFeatureMode(featureMode, "sync"))
        {
            var source = random.Next(reels.Length);
            var destination = (source + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destination] = reels[source].ToArray();
        }
        if (NeonPlayPolicy.HasFeatureMode(featureMode, "rand"))
        {
            var reel = reels[random.Next(reels.Length)];
            for (var row = 0; row < reel.Length; row++) reel[row] = "ACE";
        }
        if (NeonPlayPolicy.HasFeatureMode(featureMode, "paw"))
        {
            for (var index = 0; index < 2; index++)
            {
                var candidates = reels.SelectMany((reel, reelIndex) => reel.Select((symbol, row) =>
                        (Symbol: symbol, Position: new GridPosition(reelIndex, row))))
                    .Where(cell => ReplacementSymbols.Contains(cell.Symbol, StringComparer.Ordinal))
                    .Select(cell => cell.Position).ToArray();
                if (candidates.Length == 0) break;
                var position = candidates[random.Next(candidates.Length)];
                reels[position.Reel][position.Row] = "ACE";
            }
        }
        return outcome with { VisibleReels = reels.Select(reel => (IReadOnlyList<string>)reel).ToArray() };
    }

    public static IReadOnlyList<PaylinePayout> CalculateTripleSevenPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Max(reel => reel.Count);
        var paylineId = 801;

        void AddPattern(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => position.Reel < 0 || position.Reel >= reels.Count ||
                position.Row < 0 || position.Row >= reels[position.Reel].Count ||
                reels[position.Reel][position.Row] != "BANANA")) return;
            var key = string.Join('|', positions.OrderBy(position => position.Reel)
                .ThenBy(position => position.Row)
                .Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key)) return;
            var amount = checked(wagerPoints * NeonArcadeSlotDefinitions.TripleSevenPatternMultiplier);
            var currentPaylineId = paylineId++;
            payouts.Add(new PaylinePayout(currentPaylineId, amount,
                [new PaidMatch(
                    new SymbolMatch(currentPaylineId, "BANANA", 3, positions.ToArray(), []),
                    NeonArcadeSlotDefinitions.TripleSevenPatternMultiplier, amount)]));
        }

        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                AddPattern([new(reel, row), new(reel, row + 1), new(reel, row + 2)]);
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                AddPattern([new(reel, row), new(reel + 1, row), new(reel + 2, row)]);
        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
                AddPattern([new(reel, row), new(reel + 1, row + 1), new(reel + 2, row + 2)]);
            for (var row = 2; row < rows; row++)
                AddPattern([new(reel, row), new(reel + 1, row - 1), new(reel + 2, row - 2)]);
        }
        return payouts;
    }

    internal static (SpinPayout Payout, NeonArcadeSettlement Bonus, long SevenPoints) CalculatePayout(
        SpinPayout linePayout, IReadOnlyList<IReadOnlyList<string>> landedReels, long wagerPoints,
        bool isFreeSpin, string engineVersion)
    {
        var bonus = NeonArcadeBonus.Apply(linePayout, landedReels, isFreeSpin: isFreeSpin, engineVersion: engineVersion);
        var sevenPaylines = CalculateTripleSevenPayouts(landedReels, wagerPoints);
        var sevenPoints = sevenPaylines.Sum(line => line.AmountPoints);
        var payout = sevenPaylines.Count == 0 ? bonus.Payout : new SpinPayout(
            checked(bonus.Payout.TotalPoints + sevenPoints), [.. bonus.Payout.Paylines, .. sevenPaylines]);
        return (payout, bonus, sevenPoints);
    }

    private static bool HasPayingFullMatch(
        IReadOnlyList<PaylineEvaluation> evaluations, GameDefinition game, PaytableDefinition paytable) =>
        evaluations.Any(evaluation => evaluation.Candidates.Any(candidate =>
            candidate.Matches.Any(match =>
                match.MatchLength == game.Layout.ReelCount &&
                paytable.Rules.Any(rule =>
                    rule.SymbolId == match.SymbolId && rule.MatchLength == match.MatchLength &&
                    rule.Multiplier > 0))));

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints),
                $"Game '{game.Id}' requires at least {game.Wagering.MinimumWagerPoints} points per spin.");
        if (game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints),
                $"Game '{game.Id}' allows at most {maximum} points per spin.");
        var allowedWagers = game.Wagering.AllowedWagerPoints;
        var matchesIncrement = game.Wagering.WagerIncrementPoints is { } increment &&
            (wagerPoints - game.Wagering.MinimumWagerPoints) % increment == 0;
        if (allowedWagers.Count > 0 ? !allowedWagers.Contains(wagerPoints) : !matchesIncrement)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints),
                $"{wagerPoints} is not an available wager for game '{game.Id}'.");
    }

    private sealed class PityState
    {
        public object SyncRoot { get; } = new();
        public int ConsecutiveFiveMisses { get; set; }
    }
}
