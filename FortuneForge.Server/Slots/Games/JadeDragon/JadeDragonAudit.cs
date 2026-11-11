using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Slots.Models;
using PackageEngine = FortuneForge.Games.JadeDragon.JadeDragonEngine;
using PackageGenerator = FortuneForge.Games.JadeDragon.Reels.CryptoReelGenerator;
using PackageRandom = FortuneForge.Games.JadeDragon.Reels.IRandomIndexSource;

namespace FortuneForge.Server.Slots.Games.JadeDragon;

// Audits the new package version using Jade Dragon's own policies. Historical shared
// engine evidence continues through the existing versioned auditor.
internal static class JadeDragonAudit
{
    private static readonly PackageEngine Engine = new();

    internal static IReadOnlyList<RoundAuditFinding> Check(SpinResult result, SpinPayout settled)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        try
        {
            var audit = result.EngineAudit ?? throw new InvalidOperationException("Missing Jade Dragon evidence.");
            if (audit.IsDerivedFreeSpin || audit.WukongPawPrototype is not null ||
                audit.NeonArcade is not null || audit.NeonDiscoCascade is not null || audit.OlympusTumble is not null ||
                result.NeonArcade is not null || result.OlympusTumble is not null ||
                result.NeonDiscoCascade is not null || result.NeonDiscoSpinsRemaining != 0 ||
                result.LightningCollection is not null || audit.CrucibleRuleVersion is not null)
                Flag("slot-empty-foreign-evidence", "The outcome contains evidence from an unrelated rules engine.");
            var game = Engine.GetEffectiveDefinition(audit.FreeSpinFeatureMode);
            var baseGame = Engine.Definition;
            var reels = Engine.GetEffectiveReelSet(result.SpecialBoostApplied);
            var symbols = Engine.SymbolSet;
            var paytable = Engine.Paytable;
            if (result.GameId != PackageEngine.GameId || result.ReelSetId != reels.Id ||
                result.SymbolSetId != symbols.Id || result.PaytableId != paytable.Id ||
                result.PointValueInCents != baseGame.Wagering.PointValueInCents ||
                !Same(audit.BaseGame, baseGame) || !Same(audit.EffectiveGame, game) ||
                !Same(audit.ReelSet, reels) || !Same(audit.SymbolSet, symbols) ||
                !Same(audit.Paytable, paytable) || !Same(audit.FeatureProfile, Engine.Profile))
                Flag("slot-empty-rules-mismatch", "The captured rules differ from the versioned Jade Dragon package.");

            Engine.ValidateWager(result.WagerPoints);
            if (result.Reels.Count != game.Layout.ReelCount ||
                result.Reels.Any(reel => reel.Count != game.Layout.VisibleRows) ||
                result.ReelStops.Count != game.Layout.ReelCount)
                throw new ArgumentException("Invalid Jade Dragon grid dimensions.");
            var symbolIds = symbols.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
            if (result.Reels.SelectMany(reel => reel).Any(symbol => !symbolIds.Contains(symbol)))
                Flag("slot-symbol-invalid", "The Jade Dragon grid contains an unknown symbol.");

            var reconstructed = new PackageGenerator(new ReplayStops(result.ReelStops)).Generate(game, reels, symbols);
            if (!Same(reconstructed.VisibleReels, audit.RawGeneratedReels))
                Flag("slot-stop-invalid", "The saved Jade Dragon reel stops do not reconstruct the raw grid.");
            var frequency = audit.ReelsAfterFreeGameFrequency;
            if (frequency.Count != game.Layout.ReelCount || frequency.Any(reel => reel.Count != game.Layout.VisibleRows))
                throw new ArgumentException("Invalid Jade Dragon frequency evidence.");
            for (var reel = 0; reel < frequency.Count; reel++)
            for (var row = 0; row < frequency[reel].Count; row++)
            {
                var raw = reconstructed.VisibleReels[reel][row];
                var after = frequency[reel][row];
                if (raw != after && !(raw == game.FreeGames!.SymbolId &&
                    game.FreeGames.VisibleFrequencyDivisor > 1 && game.SpecialPoints!.CommonSymbolIds.Contains(after)))
                    Flag("slot-frequency-invalid", "Jade Dragon scatter filtering changed an unrelated symbol.");
            }

            var expected = Engine.EvaluateOutcome(result.Reels, result.WagerPoints, audit.FreeSpinFeatureMode);
            if (!Same(result.Payout, expected.Payout))
                Flag("slot-payout-total-mismatch", "Jade Dragon's payout differs from its own evaluator.");
            if (!Same(settled, expected.Payout))
                Flag("slot-settlement-mismatch", "Jade Dragon's settled payout differs from its evaluated payout.");
            if (result.FreeSpinsAwarded != expected.FreeSpinsAwarded ||
                result.SpecialPointsAwarded != expected.SpecialPointsAwarded ||
                result.BananaBonusPoints != expected.PatternPayoutPoints ||
                result.EnergyAwarded != 0 || result.MonkeyPawCount != 0 || result.MoneyGrabPoints != 0 ||
                !Same(result.SealsAwarded, expected.CollectionSymbolsAwarded))
                Flag("slot-empty-awards-mismatch", "Jade Dragon's recorded feature awards differ from the visible grid.");

            var pityTriggered = audit.FreeSpinFeatureMode is null &&
                baseGame.Math.FiveMatchPityMissLimit is { } limit && audit.PityMissesBefore >= limit;
            if (audit.PityMissesBefore < 0 || result.FiveMatchPityTriggered != pityTriggered ||
                result.ConsecutiveFiveMisses != (expected.HasPayingFiveMatch ? 0 : checked(audit.PityMissesBefore + 1)) ||
                pityTriggered && !expected.HasPayingFiveMatch || audit.GenerationAttempts is < 1 or > 10_000 ||
                !pityTriggered && audit.GenerationAttempts != 1)
                Flag("slot-pity-state-mismatch", "Jade Dragon's pity transition differs from the evaluated result.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            OverflowException or IndexOutOfRangeException or KeyNotFoundException or JsonException or NullReferenceException)
        {
            Flag("slot-empty-replay-invalid", "The captured Jade Dragon outcome cannot be evaluated by its package.");
        }
        return findings;
    }

    private static bool Same(object? left, object? right) => JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(left), JsonSerializer.SerializeToNode(right));

    private sealed class ReplayStops(IReadOnlyList<int> stops) : PackageRandom
    {
        private int next;
        public int Next(int maximumExclusive)
        {
            if (next >= stops.Count) throw new ArgumentException("Missing Jade Dragon reel stop.");
            var stop = stops[next++];
            if (stop < 0 || stop >= maximumExclusive) throw new ArgumentOutOfRangeException(nameof(stops));
            return stop;
        }
    }
}
