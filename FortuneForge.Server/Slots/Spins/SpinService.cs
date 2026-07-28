using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed class SpinService(
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

    public SpinResult Spin(
        string gameId,
        long wagerPoints,
        string playerId,
        bool specialBoostApplied,
        long currentEnergyBalance = 0,
        string? freeSpinFeatureMode = null)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        ValidateWager(game, wagerPoints);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("An authenticated player id is required.", nameof(playerId));
        }

        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{game.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{game.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{game.Math.PaytableId}' was not found.");
        var effectiveReelSet = specialBoostApplied
            ? SpecialPointBonus.CreateBoostedReelSet(game, reelSet)
            : reelSet;

        if (!string.Equals(paytable.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Paytable '{paytable.Id}' is not compatible with symbol set '{symbolSet.Id}'.");
        }

        var pityState = _pityStates.GetOrAdd(
            $"{game.Id}\u001f{playerId}",
            static _ => new PityState());

        lock (pityState.SyncRoot)
        {
            return SpinWithPity(
                game,
                effectiveReelSet,
                symbolSet,
                paytable,
                wagerPoints,
                pityState,
                specialBoostApplied,
                currentEnergyBalance,
                freeSpinFeatureMode);
        }
    }

    private SpinResult SpinWithPity(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet,
        PaytableDefinition paytable,
        long wagerPoints,
        PityState pityState,
        bool specialBoostApplied,
        long currentEnergyBalance,
        string? freeSpinFeatureMode)
    {
        var effectiveGame = CreateEffectiveSpinGame(game, freeSpinFeatureMode);
        var pityTriggered = game.Math.FiveMatchPityMissLimit is { } pityLimit &&
            pityState.ConsecutiveFiveMisses >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        ReelOutcome? outcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            outcome = reelGenerator.Generate(effectiveGame, reelSet, symbolSet);
            outcome = ApplyFeatureSymbols(outcome, effectiveGame, currentEnergyBalance, freeSpinFeatureMode);
            evaluations = combinationEvaluator.Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable))
            {
                break;
            }
        }

        if (outcome is null || evaluations is null ||
            (pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable)))
        {
            throw new InvalidOperationException(
                $"Game '{game.Id}' could not generate a five-match pity outcome.");
        }

        var hasFiveMatch = HasPayingFullMatch(evaluations, effectiveGame, paytable);
        pityState.ConsecutiveFiveMisses = hasFiveMatch
            ? 0
            : checked(pityState.ConsecutiveFiveMisses + 1);
        var payout = payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints);
        var featurePayout = CalculateFeaturePayout(outcome.VisibleReels, wagerPoints);
        payout = AddFeaturePayout(payout, featurePayout);
        var freeSpinsAwarded = CountFreeGameSymbols(outcome.VisibleReels, effectiveGame) >=
            (effectiveGame.FreeGames?.RequiredSymbols ?? int.MaxValue)
                ? effectiveGame.FreeGames?.AwardedSpins ?? 0
                : 0;
        var specialPointsAwarded = SpecialPointBonus.CalculateAward(evaluations, effectiveGame);
        var energyAwarded = CountEnergySymbols(outcome.VisibleReels, effectiveGame) *
            (effectiveGame.Energy?.PointsPerVisibleSymbol ?? 0);

        return new SpinResult(
            Guid.NewGuid(),
            game.Id,
            reelSet.Id,
            symbolSet.Id,
            paytable.Id,
            wagerPoints,
            game.Wagering.PointValueInCents,
            outcome.StopIndexes,
            outcome.VisibleReels,
            payout,
            pityState.ConsecutiveFiveMisses,
            pityTriggered,
            freeSpinsAwarded,
            specialPointsAwarded,
            energyAwarded,
            specialBoostApplied)
        {
            MonkeyPawCount = featurePayout.MonkeyPawCount,
            MoneyGrabPoints = featurePayout.MoneyGrabPoints,
            BananaBonusPoints = featurePayout.BananaBonusPoints,
            SealsAwarded = CountSealSymbols(outcome.VisibleReels)
        };
    }

    private ReelOutcome ApplyFeatureSymbols(
        ReelOutcome outcome,
        GameDefinition game,
        long currentEnergyBalance,
        string? freeSpinFeatureMode)
    {
        if (!string.Equals(game.Id, WukongGameId, StringComparison.Ordinal))
        {
            return outcome;
        }

        var reels = outcome.VisibleReels
            .Select(reel => reel.ToArray())
            .ToArray();

        if (string.Equals(freeSpinFeatureMode, SyncedReelsFeatureMode, StringComparison.Ordinal))
        {
            var sourceReel = random.Next(reels.Length);
            var destinationReel = (sourceReel + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destinationReel] = reels[sourceReel].ToArray();
        }

        if (string.Equals(freeSpinFeatureMode, RandColumnFeatureMode, StringComparison.Ordinal))
        {
            var reel = random.Next(reels.Length);
            for (var row = 0; row < reels[reel].Length; row++)
            {
                reels[reel][row] = PickRandSymbol();
            }
        }
        else
        {
            var moneyCount = RollMoneySymbolCount();
            InjectSymbols(reels, moneyCount, PickRandSymbol);
        }

        InjectSymbols(reels, RollMonkeyPawCount(freeSpinFeatureMode), () => MonkeyPawSymbolId);
        InjectSymbols(reels, RollBananaCount(), () => BananaSymbolId);
        InjectSymbols(reels, RollSealCount(currentEnergyBalance), PickSealSymbol);

        return outcome with
        {
            VisibleReels = reels
                .Select(reel => (IReadOnlyList<string>)reel)
                .ToArray()
        };
    }

    private static GameDefinition CreateEffectiveSpinGame(
        GameDefinition game,
        string? freeSpinFeatureMode)
    {
        if (!string.Equals(freeSpinFeatureMode, ExtraRowsFeatureMode, StringComparison.Ordinal))
        {
            return game;
        }

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

    private int RollMonkeyPawCount(string? freeSpinFeatureMode)
    {
        if (string.Equals(freeSpinFeatureMode, PawBoostFeatureMode, StringComparison.Ordinal))
        {
            return random.Next(6) switch
            {
                0 => 2,
                1 => 1,
                _ => 0
            };
        }

        if (random.Next(777) == 0)
        {
            return 2;
        }

        return random.Next(7) == 0 ? 1 : 0;
    }

    private int RollMoneySymbolCount() =>
        random.Next(100) switch
        {
            < 20 => 0,
            < 80 => 1,
            < 96 => 2,
            _ => 3
        };

    private int RollBananaCount() =>
        random.Next(100) switch
        {
            < 48 => 0,
            < 88 => 1,
            < 98 => 2,
            _ => 3
        };

    private int RollSealCount(long currentEnergyBalance)
    {
        var chance = currentEnergyBalance switch
        {
            >= 75 => 67,
            >= 50 => 50,
            >= 25 => 40,
            _ => 33
        };

        if (random.Next(100) >= chance)
        {
            return 0;
        }

        return random.Next(25) == 0 ? 2 : 1;
    }

    private void InjectSymbols(
        IReadOnlyList<string[]> reels,
        int count,
        Func<string> symbolFactory)
    {
        for (var index = 0; index < count; index++)
        {
            var position = PickReplacementPosition(reels);
            if (position is null)
            {
                return;
            }

            reels[position.Value.Reel][position.Value.Row] = symbolFactory();
        }
    }

    private GridPosition? PickReplacementPosition(IReadOnlyList<string[]> reels)
    {
        var candidates = reels
            .SelectMany((reel, reelIndex) => reel.Select((symbol, rowIndex) =>
                new { Symbol = symbol, Position = new GridPosition(reelIndex, rowIndex) }))
            .Where(candidate => CommonReplacementSymbols.Contains(candidate.Symbol, StringComparer.Ordinal))
            .Select(candidate => candidate.Position)
            .ToArray();

        return candidates.Length == 0 ? null : candidates[random.Next(candidates.Length)];
    }

    private string PickRandSymbol() =>
        random.Next(100) switch
        {
            < 28 => "RAND_05",
            < 55 => "RAND_1",
            < 74 => "RAND_15",
            < 88 => "RAND_2",
            < 96 => "RAND_3",
            < 99 => "RAND_4",
            _ => "RAND_5"
        };

    private string PickSealSymbol() => SealSymbolIds[random.Next(SealSymbolIds.Length)];

    private static FeaturePayout CalculateFeaturePayout(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints)
    {
        var moneyPositions = VisiblePositions(reels)
            .Select(position => new
            {
                Position = position,
                Multiplier = RandMultiplier(reels[position.Reel][position.Row])
            })
            .Where(symbol => symbol.Multiplier > 0)
            .ToArray();
        var pawPositions = VisiblePositions(reels)
            .Where(position => string.Equals(
                reels[position.Reel][position.Row],
                MonkeyPawSymbolId,
                StringComparison.Ordinal))
            .ToArray();
        var moneyGrabPoints = 0L;
        PaylinePayout? moneyGrabPayout = null;
        if (pawPositions.Length > 0 && moneyPositions.Length > 0)
        {
            var multiplier = moneyPositions.Sum(symbol => symbol.Multiplier);
            if (pawPositions.Length >= 2)
            {
                multiplier *= 2;
            }

            moneyGrabPoints = MultiplyWager(wagerPoints, multiplier);
            moneyGrabPayout = new PaylinePayout(
                901,
                moneyGrabPoints,
                [
                    new PaidMatch(
                        new SymbolMatch(
                            901,
                            MonkeyPawSymbolId,
                            pawPositions.Length + moneyPositions.Length,
                            pawPositions.Concat(moneyPositions.Select(symbol => symbol.Position)).ToArray(),
                            []),
                        checked((long)Math.Ceiling(multiplier)),
                        moneyGrabPoints)
                ]);
        }

        var bananaPayouts = CalculateBananaPayouts(reels, wagerPoints);
        return new FeaturePayout(
            pawPositions.Length,
            moneyGrabPoints,
            bananaPayouts.Sum(payout => payout.AmountPoints),
            moneyGrabPayout is null ? bananaPayouts : [moneyGrabPayout, .. bananaPayouts]);
    }

    private static IReadOnlyList<PaylinePayout> CalculateBananaPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Max(reel => reel.Count);
        var paylineId = 801;

        void AddPattern(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => !IsBananaAt(reels, position)))
            {
                return;
            }

            var key = string.Join('|', positions
                .OrderBy(position => position.Reel)
                .ThenBy(position => position.Row)
                .Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key))
            {
                return;
            }

            var amount = checked(wagerPoints * 3);
            var currentPaylineId = paylineId++;
            payouts.Add(new PaylinePayout(
                currentPaylineId,
                amount,
                [
                    new PaidMatch(
                        new SymbolMatch(currentPaylineId, BananaSymbolId, 3, positions.ToArray(), []),
                        3,
                        amount)
                ]));
        }

        for (var reel = 0; reel < reels.Count; reel++)
        {
            for (var row = 0; row <= reels[reel].Count - 3; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel, row + 1),
                    new GridPosition(reel, row + 2)
                ]);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            for (var reel = 0; reel <= reels.Count - 3; reel++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row),
                    new GridPosition(reel + 2, row)
                ]);
            }
        }

        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row + 1),
                    new GridPosition(reel + 2, row + 2)
                ]);
            }

            for (var row = 2; row < rows; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row - 1),
                    new GridPosition(reel + 2, row - 2)
                ]);
            }
        }

        return payouts;
    }

    private static bool IsBananaAt(IReadOnlyList<IReadOnlyList<string>> reels, GridPosition position) =>
        position.Reel >= 0 &&
        position.Reel < reels.Count &&
        position.Row >= 0 &&
        position.Row < reels[position.Reel].Count &&
        string.Equals(reels[position.Reel][position.Row], BananaSymbolId, StringComparison.Ordinal);

    private static SpinPayout AddFeaturePayout(SpinPayout payout, FeaturePayout featurePayout)
    {
        if (featurePayout.Paylines.Count == 0)
        {
            return payout;
        }

        var paylines = payout.Paylines.Concat(featurePayout.Paylines).ToArray();
        return payout with
        {
            TotalPoints = paylines.Sum(payline => payline.AmountPoints),
            Paylines = paylines
        };
    }

    private static IReadOnlyList<GridPosition> VisiblePositions(
        IReadOnlyList<IReadOnlyList<string>> reels) =>
        reels
            .SelectMany((reel, reelIndex) => reel.Select((_, rowIndex) =>
                new GridPosition(reelIndex, rowIndex)))
            .ToArray();

    private static Dictionary<string, int> CountSealSymbols(
        IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var counts = SealSymbolIds.ToDictionary(symbolId => symbolId, _ => 0, StringComparer.Ordinal);
        foreach (var symbol in reels.SelectMany(reel => reel))
        {
            if (counts.ContainsKey(symbol))
            {
                counts[symbol]++;
            }
        }

        return counts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static decimal RandMultiplier(string symbolId) => symbolId switch
    {
        "RAND_05" => 0.5m,
        "RAND_1" => 1m,
        "RAND_15" => 1.5m,
        "RAND_2" => 2m,
        "RAND_3" => 3m,
        "RAND_4" => 4m,
        "RAND_5" => 5m,
        _ => 0m
    };

    private static long MultiplyWager(long wagerPoints, decimal multiplier) =>
        checked((long)Math.Round(wagerPoints * multiplier, MidpointRounding.AwayFromZero));

    private static bool HasPayingFullMatch(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
        PaytableDefinition paytable) =>
        evaluations.Any(evaluation => evaluation.Candidates.Any(candidate =>
            candidate.Matches.Any(match =>
                match.MatchLength == game.Layout.ReelCount &&
                paytable.Rules.Any(rule =>
                    string.Equals(rule.SymbolId, match.SymbolId, StringComparison.Ordinal) &&
                    rule.MatchLength == match.MatchLength &&
                    rule.Multiplier > 0))));

    private static int CountFreeGameSymbols(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game)
    {
        if (game.FreeGames is null)
        {
            return 0;
        }

        return reels.Sum(reel => reel.Count(symbol => string.Equals(
            symbol,
            game.FreeGames.SymbolId,
            StringComparison.Ordinal)));
    }

    private static int CountEnergySymbols(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game)
    {
        if (game.Energy is null)
        {
            return 0;
        }

        return reels.Sum(reel => reel.Count(symbol => string.Equals(
            symbol,
            game.Energy.SymbolId,
            StringComparison.Ordinal)));
    }

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' requires at least {game.Wagering.MinimumWagerPoints} points per spin.");
        }

        if (game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' allows at most {maximum} points per spin.");
        }
        if (!game.Wagering.AllowedWagerPoints.Contains(wagerPoints))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"{wagerPoints} is not an available wager for game '{game.Id}'.");
        }
    }

    private sealed class PityState
    {
        public object SyncRoot { get; } = new();
        public int ConsecutiveFiveMisses { get; set; }
    }

    private sealed record FeaturePayout(
        int MonkeyPawCount,
        long MoneyGrabPoints,
        long BananaBonusPoints,
        IReadOnlyList<PaylinePayout> Paylines);
}
