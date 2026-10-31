using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;

namespace FortuneForge.Server.RoundAudit;

// Runs against the frozen rules used by the spin. Random draws cannot be reconstructed
// from a CSPRNG result, but the resulting reels, awards, and settlement can be checked.
internal static class SlotRoundAudit
{
    internal const string AuditorVersion = "slot-outcome-auditor-v1";

    internal static IReadOnlyList<RoundAuditFinding> Check(SpinResult result, SpinPayout settledPayout)
    {
        if (result.EngineAudit is not { } audit)
            return [new("slot-engine-evidence-missing", "error",
                "The engine's rules and generated reel evidence were not captured.")];
        return Check(result, audit.BaseGame, audit.EffectiveGame, audit.ReelSet,
            audit.SymbolSet, audit.Paytable, settledPayout, audit.EnergyBalanceBefore,
            audit.PityMissesBefore, audit.FreeSpinFeatureMode, audit.RawGeneratedReels,
            audit.ReelsAfterFreeGameFrequency);
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(
        SpinResult result,
        GameDefinition baseGame,
        GameDefinition effectiveGame,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet,
        PaytableDefinition paytable,
        SpinPayout settledPayout,
        long preSpinEnergy,
        int preSpinPityMisses,
        string? activeFeatureMode,
        IReadOnlyList<IReadOnlyList<string>>? rawGeneratedReels = null,
        IReadOnlyList<IReadOnlyList<string>>? reelsAfterFrequency = null)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));

        if (result.GameId != baseGame.Id || result.GameId != effectiveGame.Id ||
            result.SymbolSetId != symbolSet.Id || result.ReelSetId != reelSet.Id ||
            result.PaytableId != paytable.Id ||
            reelSet.SymbolSetId != symbolSet.Id || paytable.SymbolSetId != symbolSet.Id)
            Flag("slot-rules-mismatch", "The spin identifiers do not match the captured rules.");
        if (result.PointValueInCents != baseGame.Wagering.PointValueInCents ||
            !ValidWager(result.WagerPoints, baseGame.Wagering))
            Flag("slot-wager-invalid", "The wager or point value does not match the captured rules.");

        if (result.Reels.Count != effectiveGame.Layout.ReelCount ||
            result.Reels.Any(reel => reel.Count != effectiveGame.Layout.VisibleRows) ||
            result.ReelStops.Count != effectiveGame.Layout.ReelCount ||
            reelSet.Reels.Count != effectiveGame.Layout.ReelCount)
        {
            Flag("slot-grid-invalid", "The reel window, stop count, or reel set has an invalid shape.");
            return findings;
        }

        var validSymbols = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Reels.SelectMany(reel => reel).Any(symbol => !validSymbols.Contains(symbol)))
            Flag("slot-symbol-invalid", "The visible window contains a symbol outside the captured symbol set.");
        for (var reel = 0; reel < result.ReelStops.Count; reel++)
        {
            var playable = effectiveGame.Energy is null
                ? reelSet.Reels[reel].Where(symbol => symbol != "BOLT").ToArray()
                : reelSet.Reels[reel].ToArray();
            var playableCount = playable.Length;
            if (result.ReelStops[reel] < 0 || result.ReelStops[reel] >= playableCount)
                Flag("slot-stop-invalid", $"Reel {reel + 1} has a stop outside its captured strip.");
            else if (rawGeneratedReels is not null && rawGeneratedReels.Count == result.Reels.Count &&
                rawGeneratedReels[reel].Count == effectiveGame.Layout.VisibleRows &&
                rawGeneratedReels[reel].Where((symbol, row) =>
                    symbol != playable[(result.ReelStops[reel] + row) % playableCount]).Any())
                Flag("slot-raw-reel-mismatch", $"The generated window for reel {reel + 1} does not match its stop and strip.");
        }
        if (rawGeneratedReels is not null &&
            (rawGeneratedReels.Count != result.Reels.Count ||
             rawGeneratedReels.Any(reel => reel.Count != effectiveGame.Layout.VisibleRows)))
            Flag("slot-raw-reel-mismatch", "The generated reel window has an invalid shape.");
        if (rawGeneratedReels is not null && reelsAfterFrequency is not null &&
            !ValidFrequencyStage(rawGeneratedReels, reelsAfterFrequency, effectiveGame))
            Flag("slot-frequency-stage-mismatch", "The scatter frequency stage changed an unexpected symbol.");

        try
        {
            var evaluations = new CombinationEvaluator().Evaluate(result.Reels, effectiveGame, symbolSet);
            var basePayout = new PayoutCalculator().Calculate(
                evaluations, baseGame, paytable, result.WagerPoints);
            var recordedBaseLines = result.Payout.Paylines.Where(line => line.PaylineId < 801).ToArray();
            if (!LinesEqual(recordedBaseLines, basePayout.Paylines))
                Flag("slot-base-payout-mismatch", "The base payline breakdown does not replay from the visible reels.");

            var expectedFreeSpins = effectiveGame.FreeGames is { } freeGames &&
                result.Reels.SelectMany(reel => reel).Count(symbol => symbol == freeGames.SymbolId) >=
                freeGames.RequiredSymbols ? freeGames.AwardedSpins : 0;
            if (result.FreeSpinsAwarded != expectedFreeSpins)
                Flag("slot-free-spins-mismatch", "The free-spin award does not match the visible symbols.");
            if (result.SpecialPointsAwarded != SpecialPointBonus.CalculateAward(evaluations, effectiveGame))
                Flag("slot-special-points-mismatch", "The special-point award does not match the visible symbols.");

            var energyAward = effectiveGame.Energy is { } energy
                ? result.Reels.SelectMany(reel => reel).Count(symbol => symbol == energy.SymbolId) *
                  energy.PointsPerVisibleSymbol : 0;
            if (result.EnergyAwarded != energyAward)
                Flag("slot-energy-award-mismatch", "The energy award does not match the visible symbols.");
            var seals = result.Reels.SelectMany(reel => reel)
                .Where(symbol => symbol is "SEAL_SYNC" or "SEAL_ROWS" or "SEAL_PAW" or "SEAL_RAND")
                .GroupBy(symbol => symbol, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            if (seals.Count != result.SealsAwarded.Count ||
                seals.Any(pair => !result.SealsAwarded.TryGetValue(pair.Key, out var count) || count != pair.Value))
                Flag("slot-seals-mismatch", "The collection symbol counts do not match the visible reels.");

            var fullMatch = evaluations.Any(evaluation => evaluation.Candidates.Any(candidate =>
                candidate.Matches.Any(match => match.MatchLength == effectiveGame.Layout.ReelCount &&
                    paytable.Rules.Any(rule => rule.SymbolId == match.SymbolId &&
                        rule.MatchLength == match.MatchLength && rule.Multiplier > 0))));
            var expectedPity = string.IsNullOrWhiteSpace(activeFeatureMode) &&
                baseGame.Math.FiveMatchPityMissLimit is { } limit && preSpinPityMisses >= limit;
            if (result.FiveMatchPityTriggered != expectedPity ||
                result.ConsecutiveFiveMisses != (fullMatch ? 0 : checked(preSpinPityMisses + 1)) ||
                (expectedPity && !fullMatch))
                Flag("slot-pity-state-mismatch", "The pity state transition does not match the spin outcome.");

            var pawCount = result.Reels.SelectMany(reel => reel).Count(symbol => symbol == "PAW");
            var moneyMultiplier = result.Reels.SelectMany(reel => reel).Sum(RandMultiplier);
            var moneyPoints = pawCount > 0 && moneyMultiplier > 0
                ? Scale(RoundPoints(result.WagerPoints * moneyMultiplier * (pawCount >= 2 ? 2 : 1)),
                    baseGame.Math.PayoutMultiplier) : 0;
            var bananaPatterns = CountBananaPatterns(result.Reels);
            var bananaUnit = Scale(checked(result.WagerPoints * 3), baseGame.Math.PayoutMultiplier);
            var bananaPoints = checked(bananaPatterns * bananaUnit);
            if (result.MonkeyPawCount != pawCount || result.MoneyGrabPoints != moneyPoints ||
                result.BananaBonusPoints != bananaPoints)
                Flag("slot-feature-award-mismatch", "The feature awards do not match the visible reels.");
            var moneyLines = result.Payout.Paylines.Where(line => line.PaylineId == 901).ToArray();
            var bananaLines = result.Payout.Paylines.Where(line => line.PaylineId is >= 801 and < 901).ToArray();
            if (moneyLines.Length != (moneyPoints > 0 ? 1 : 0) ||
                moneyLines.Sum(line => line.AmountPoints) != moneyPoints ||
                bananaLines.Length != bananaPatterns ||
                bananaLines.Sum(line => line.AmountPoints) != bananaPoints ||
                bananaLines.Any(line => line.AmountPoints != bananaUnit) ||
                !bananaLines.Select(line => line.PaylineId)
                    .SequenceEqual(Enumerable.Range(801, bananaLines.Length)) ||
                result.Payout.Paylines.Any(line => line.PaylineId >= 901 && line.PaylineId != 901))
                Flag("slot-feature-payout-mismatch", "The feature payline breakdown does not match its award.");
            var expectedTotal = checked(basePayout.TotalPoints + moneyPoints + bananaPoints);
            if (result.Payout.TotalPoints != expectedTotal ||
                result.Payout.TotalPoints != result.Payout.Paylines.Sum(line => line.AmountPoints) ||
                result.Payout.Paylines.Any(line => line.AmountPoints != line.Matches.Sum(match => match.AmountPoints)))
                Flag("slot-payout-total-mismatch", "The outcome payout does not balance with its paylines.");

            var fullEnergy = Math.Min(100, checked(Math.Clamp(preSpinEnergy, 0, 100) + energyAward));
            var multiply = fullEnergy >= 100 && result.Payout.TotalPoints > 0;
            var expectedSettledLines = multiply
                ? result.Payout.Paylines.Select(line => line with
                {
                    Matches = line.Matches.Select(match => match with
                    {
                        AmountPoints = Scale(match.AmountPoints, 1.5m)
                    }).ToArray(),
                    AmountPoints = line.Matches.Sum(match => Scale(match.AmountPoints, 1.5m))
                }).ToArray()
                : result.Payout.Paylines;
            if (!LinesEqual(settledPayout.Paylines, expectedSettledLines) ||
                settledPayout.TotalPoints != expectedSettledLines.Sum(line => line.AmountPoints))
                Flag("slot-settlement-mismatch", "The settled payout does not match the energy multiplier rules.");
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or
            InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException)
        {
            Flag("slot-replay-failed", "The captured spin could not be replayed under its captured rules.");
        }

        return findings;
    }

    private static bool ValidWager(long wager, GameWageringDefinition rules) =>
        wager >= rules.MinimumWagerPoints && wager > 0 &&
        (rules.MaximumWagerPoints is null || wager <= rules.MaximumWagerPoints) &&
        (rules.AllowedWagerPoints.Count > 0
            ? rules.AllowedWagerPoints.Contains(wager)
            : rules.WagerIncrementPoints is { } increment && increment > 0 &&
              (wager - rules.MinimumWagerPoints) % increment == 0);

    private static bool ValidFrequencyStage(
        IReadOnlyList<IReadOnlyList<string>> raw,
        IReadOnlyList<IReadOnlyList<string>> after,
        GameDefinition game)
    {
        if (raw.Count != after.Count) return false;
        for (var reel = 0; reel < raw.Count; reel++)
        {
            if (raw[reel].Count != after[reel].Count) return false;
            for (var row = 0; row < raw[reel].Count; row++)
            {
                if (raw[reel][row] == after[reel][row]) continue;
                if (game.FreeGames is not { VisibleFrequencyDivisor: > 1 } freeGames ||
                    raw[reel][row] != freeGames.SymbolId ||
                    game.SpecialPoints?.CommonSymbolIds.Contains(after[reel][row]) != true)
                    return false;
            }
        }
        return true;
    }

    private static decimal RandMultiplier(string symbol) => symbol switch
    {
        "RAND_05" => .5m, "RAND_1" => 1m, "RAND_15" => 1.5m,
        "RAND_2" => 2m, "RAND_3" => 3m, "RAND_4" => 4m, "RAND_5" => 5m, _ => 0m
    };

    private static long RoundPoints(decimal points) =>
        checked((long)Math.Round(points, MidpointRounding.AwayFromZero));

    private static long Scale(long points, decimal multiplier) =>
        RoundPoints(points * multiplier);

    private static int CountBananaPatterns(IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        void Include(params (int Reel, int Row)[] cells)
        {
            if (cells.All(cell => cell.Reel >= 0 && cell.Reel < reels.Count &&
                cell.Row >= 0 && cell.Row < reels[cell.Reel].Count &&
                reels[cell.Reel][cell.Row] == "BANANA"))
                patterns.Add(string.Join('|', cells.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row)
                    .Select(cell => $"{cell.Reel}.{cell.Row}")));
        }
        var rows = reels.Max(reel => reel.Count);
        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                Include((reel, row), (reel, row + 1), (reel, row + 2));
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                Include((reel, row), (reel + 1, row), (reel + 2, row));
        for (var reel = 0; reel <= reels.Count - 3; reel++)
            for (var row = 0; row <= rows - 3; row++)
                Include((reel, row), (reel + 1, row + 1), (reel + 2, row + 2));
        for (var reel = 0; reel <= reels.Count - 3; reel++)
            for (var row = 2; row < rows; row++)
                Include((reel, row), (reel + 1, row - 1), (reel + 2, row - 2));
        return patterns.Count;
    }

    private static bool LinesEqual(IReadOnlyList<PaylinePayout> actual, IReadOnlyList<PaylinePayout> expected)
    {
        if (actual.Count != expected.Count) return false;
        for (var index = 0; index < actual.Count; index++)
        {
            var left = actual[index];
            var right = expected[index];
            if (left.PaylineId != right.PaylineId || left.AmountPoints != right.AmountPoints ||
                left.Matches.Count != right.Matches.Count) return false;
            for (var matchIndex = 0; matchIndex < left.Matches.Count; matchIndex++)
            {
                var a = left.Matches[matchIndex];
                var b = right.Matches[matchIndex];
                if (a.AmountPoints != b.AmountPoints || a.Multiplier != b.Multiplier ||
                    a.Match.PaylineId != b.Match.PaylineId || a.Match.SymbolId != b.Match.SymbolId ||
                    a.Match.MatchLength != b.Match.MatchLength ||
                    !a.Match.Positions.SequenceEqual(b.Match.Positions) ||
                    !a.Match.WildPositions.SequenceEqual(b.Match.WildPositions)) return false;
            }
        }
        return true;
    }
}
