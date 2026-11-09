using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed record NeonReplayFinding(string Code, string Message);

// Replays frozen Neon evidence without calling another game's evaluator or policy.
public static class NeonOutcomeReplay
{
    public static IReadOnlyList<NeonReplayFinding> Check(NeonSpinOutcome result, SpinPayout settledPayout)
    {
        var findings = new List<NeonReplayFinding>();
        void Flag(string code, string message) => findings.Add(new(code, message));
        var audit = result.EngineAudit;
        var game = audit.EffectiveGame;
        if (audit.EngineVersion != NeonArcadeSlotDefinitions.EngineVersion ||
            result.GameId != NeonArcadeSlotDefinitions.GameId ||
            audit.BaseGame.Id != result.GameId || game.Id != result.GameId ||
            result.ReelSetId != audit.ReelSet.Id || result.PaytableId != audit.Paytable.Id ||
            result.SymbolSetId != audit.SymbolSet.Id ||
            audit.ReelSet.SymbolSetId != audit.SymbolSet.Id || audit.Paytable.SymbolSetId != audit.SymbolSet.Id)
            Flag("slot-rules-mismatch", "The Neon identifiers do not match the captured rules.");
        if (result.PointValueInCents != audit.BaseGame.Wagering.PointValueInCents ||
            result.PointValueInCents != 1m ||
            !(result.IsFreeSpin ? NeonArcadeSlotDefinitions.IsValidCarryWager(result.WagerPoints) :
                ValidWager(result.WagerPoints, audit.BaseGame.Wagering)))
            Flag("slot-wager-invalid", "The Neon wager does not match the captured rules.");
        if (result.Reels.Count != game.Layout.ReelCount || result.Reels.Any(reel => reel.Count != game.Layout.VisibleRows) ||
            result.ReelStops.Count != game.Layout.ReelCount || audit.ReelSet.Reels.Count != game.Layout.ReelCount)
            return [.. findings, new("slot-grid-invalid", "The captured Neon grid has an invalid shape.")];
        var symbols = audit.SymbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Reels.SelectMany(reel => reel).Any(symbol => !symbols.Contains(symbol)))
            Flag("slot-symbol-invalid", "The Neon window contains a symbol outside its captured symbol set.");
        if (audit.RawGeneratedReels.Count != result.Reels.Count ||
            audit.RawGeneratedReels.Any(reel => reel.Count != game.Layout.VisibleRows))
            Flag("slot-raw-reel-mismatch", "The captured generated Neon window has an invalid shape.");
        else
            for (var reel = 0; reel < result.Reels.Count; reel++)
            {
                var strip = NeonReelGenerator.GetPlayableStrip(game, audit.ReelSet.Reels[reel]);
                var stop = result.ReelStops[reel];
                if (stop < 0 || stop >= strip.Count)
                    Flag("slot-stop-invalid", "A Neon stop lies outside its captured strip.");
                else if (audit.RawGeneratedReels[reel].Where((symbol, row) =>
                    symbol != strip[(stop + row) % strip.Count]).Any())
                    Flag("slot-raw-reel-mismatch", "A generated Neon window differs from its captured stop.");
            }
        if (!Same(audit.RawGeneratedReels, audit.ReelsAfterFreeGameFrequency))
            Flag("slot-frequency-stage-mismatch", "Neon's inactive scatter-frequency stage changed the window.");
        try
        {
            var evaluator = new NeonCombinationEvaluator();
            var calculator = new NeonPayoutCalculator();
            var evaluations = evaluator.Evaluate(result.Reels, game, audit.SymbolSet);
            var bonus = NeonArcadeBonus.Apply(calculator.Calculate(evaluations, game, audit.Paytable,
                result.WagerPoints), result.Reels, isFreeSpin: result.IsFreeSpin);
            if (!Same(bonus.Evidence, result.NeonArcade) || !Same(audit.NeonArcade, result.NeonArcade))
                Flag("slot-neon-arcade-mismatch", "The Neon boost or activators do not replay from the visible reels.");
            if (result.FreeSpinsAwarded != bonus.FreeSpinsAwarded)
                Flag("slot-free-spins-mismatch", "The Neon awarded plays differ from the visible activators.");
            if (result.SpecialPointsAwarded != NeonSpecialPoints.CalculateAward(evaluations, game) || result.EnergyAwarded != 0)
                Flag("slot-special-points-mismatch", "The Neon outcome contains an unexpected legacy award.");
            var fullMatch = evaluations.Any(line => line.Candidates.Any(candidate => candidate.Matches.Any(match =>
                match.MatchLength == game.Layout.ReelCount && audit.Paytable.Rules.Any(rule =>
                    rule.SymbolId == match.SymbolId && rule.MatchLength == match.MatchLength && rule.Multiplier > 0))));
            var expectedPity = string.IsNullOrWhiteSpace(audit.FreeSpinFeatureMode) &&
                audit.BaseGame.Math.FiveMatchPityMissLimit is { } limit && audit.PityMissesBefore >= limit;
            if (result.FiveMatchPityTriggered != expectedPity ||
                result.ConsecutiveFiveMisses != (fullMatch ? 0 : checked(audit.PityMissesBefore + 1)) ||
                expectedPity && !fullMatch)
                Flag("slot-pity-state-mismatch", "The Neon pity transition differs from the captured outcome.");
            var sevenLines = NeonNightsEngine.CalculateTripleSevenPayouts(result.Reels, result.WagerPoints);
            var sevenPoints = sevenLines.Sum(line => line.AmountPoints);
            var expected = new SpinPayout(checked(bonus.Payout.TotalPoints + sevenPoints),
                [.. bonus.Payout.Paylines, .. sevenLines]);
            if (result.NeonDiscoCascade is not null || audit.NeonDiscoCascade is not null)
            {
                if (result.IsFreeSpin || audit.FreeSpinFeatureMode is not null ||
                    game.Layout.VisibleRows != audit.BaseGame.Layout.VisibleRows || audit.NeonDiscoCascade is null)
                    Flag("slot-neon-disco-mode-mismatch", "A Disco cascade requires its own paid Neon state.");
                else
                {
                    var draws = audit.NeonDiscoCascade.DrawIndexes;
                    var cursor = 0;
                    int Draw(int maximum)
                    {
                        if (cursor >= draws.Count || draws[cursor] < 0 || draws[cursor] >= maximum)
                            throw new ArgumentException("Invalid captured Disco refill draw.");
                        return draws[cursor++];
                    }
                    var cascade = NeonDiscoCascade.Resolve(result.Reels, game, audit.ReelSet, audit.SymbolSet,
                        audit.Paytable, result.WagerPoints, evaluator, calculator, Draw, out sevenPoints);
                    if (cursor != draws.Count || !Same(cascade, result.NeonDiscoCascade))
                        Flag("slot-neon-disco-steps-mismatch", "The Disco boards or refill draws do not replay.");
                    expected = new(cascade.TotalPoints, []);
                }
            }
            if (!Same(expected, result.Payout) || result.BananaBonusPoints != sevenPoints)
                Flag("slot-payout-total-mismatch", "The Neon payout does not reconcile with its replayed awards.");
            if (!Same(expected, settledPayout))
                Flag("slot-settlement-mismatch", "The settled Neon payout differs from its replayed award.");
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or
            InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException)
        {
            Flag("slot-replay-failed", "The captured Neon outcome could not be replayed.");
        }
        return findings;
    }

    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static bool ValidWager(long wager, GameWageringDefinition rules) =>
        wager > 0 && wager >= rules.MinimumWagerPoints &&
        (rules.MaximumWagerPoints is null || wager <= rules.MaximumWagerPoints) &&
        (rules.AllowedWagerPoints.Count > 0 ? rules.AllowedWagerPoints.Contains(wager) :
            rules.WagerIncrementPoints is > 0 && (wager - rules.MinimumWagerPoints) % rules.WagerIncrementPoints == 0);
}
