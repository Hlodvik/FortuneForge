using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class FifthSunMathTests
{
    [Fact]
    public void QualifyOnlyNewPaidCyclesAtMinimumAndMaximumWithBothEarnedBoostPolicies()
    {
        var cycles = int.TryParse(Environment.GetEnvironmentVariable("FIFTH_SUN_MATH_CYCLES"), out var configured) ? configured : 5000;
        var samples = new List<object>();
        var highestUpper = 0d;
        foreach (var wager in new long[] { 12, 300 })
        foreach (var useBoosts in new[] { false, true })
        {
            var engine = new RobotRevolutionEngine(new FifthSunTests.StableRandom(762951), RobotRevolutionEngine.HistoricalFifthSunEngineVersion);
            var payout = 0L;
            var wheel = 0L;
            var pity = 0;
            var points = 0;
            var spins = 0;
            var freeSpins = 0;
            var boosted = 0;
            var featureHits = 0;
            var eclipses = 0;
            var batchReturns = new List<double>();
            var batchPayout = 0L;
            var batchSize = Math.Max(1, Math.Min(1000, cycles / 100));
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                var pending = 0;
                var first = true;
                do
                {
                    var free = !first;
                    first = false;
                    if (free) { pending--; freeSpins++; }
                    var boost = useBoosts && points >= 8;
                    if (boost) { points -= 8; boosted++; }
                    var outcome = engine.Spin(wager, "fifth-sun-qualification", boost,
                        featureMode: free ? RobotRevolutionEngine.EclipseFeatureMode : null,
                        persistedPityMisses: pity, isFreeSpin: free);
                    Assert.Equal(RobotRevolutionEngine.HistoricalFifthSunEngineVersion, outcome.Audit.EngineVersion);
                    Assert.DoesNotContain(outcome.Reels.SelectMany(column => column), symbol => symbol is "BANANA" or "FREE" or "POWER" or "ACE");
                    pity = outcome.ConsecutiveFiveMisses;
                    points = checked(points + outcome.SpecialPointsAwarded);
                    pending = checked(pending + outcome.FreeSpinsAwarded);
                    spins++;
                    if (spins > cycles * 20L) throw new InvalidOperationException("The bonus continuation exceeded its bounded qualification budget.");
                    payout += outcome.Payout.TotalPoints;
                    batchPayout += outcome.Payout.TotalPoints;
                    var feature = RobotRevolutionEngine.DescribeFeature(outcome)!;
                    Assert.Equal(feature.TotalAwardPoints, outcome.Payout.Paylines.Where(line => line.PaylineId is >= 1001 and <= 1010).Sum(line => line.AmountPoints));
                    Assert.InRange(feature.TotalAwardPoints, 0, wager * (free ? 40 : 20));
                    wheel += feature.TotalAwardPoints;
                    if (feature.TotalAwardPoints > 0) featureHits++;
                    if (feature.EclipseTriggered) eclipses++;
                } while (pending > 0);
                if ((cycle + 1) % batchSize == 0)
                {
                    batchReturns.Add(batchPayout / (double)(batchSize * wager));
                    batchPayout = 0;
                }
            }
            var estimatedReturn = payout / (decimal)(cycles * wager);
            var mean = batchReturns.Average();
            var variance = batchReturns.Sum(value => Math.Pow(value - mean, 2)) / (batchReturns.Count - 1);
            var halfWidth = 1.96 * Math.Sqrt(variance / batchReturns.Count);
            highestUpper = Math.Max(highestUpper, mean + halfWidth);
            samples.Add(new { wagerPoints = wager, spendEarnedBoosts = useBoosts, paidCycles = cycles, freeSpins,
                totalSpins = spins, boostedSpins = boosted, payoutPoints = payout, estimatedReturn,
                featurePoints = wheel, featureReturn = wheel / (decimal)(cycles * wager), featureHits, eclipses,
                approximate95BatchMeanLower = mean - halfWidth, approximate95BatchMeanUpper = mean + halfWidth });
        }
        FifthSunTests.WriteArtifact($"math-{cycles}.json", new { engineVersion = RobotRevolutionEngine.HistoricalFifthSunEngineVersion,
            mathVersion = FifthSunWheel.MathVersion, multiplier = RobotRevolutionEngine.HistoricalFifthSunProfile.PayoutMultiplier, seed = 762951,
            qualification = "Native deterministic new-version-only paid cycles with all earned free spins/retriggers, retained pity and earned boost state. Approximate normal batch-mean intervals; local review evidence, not certified RTP. No historical baseline was repeated.", samples });
        Assert.InRange(highestUpper, 0d, 0.95d);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureOneGenuineSettledPaidAndExpandedFreeResponse(bool free)
    {
        const long wager = 12;
        const string account = "fifth-sun-local-visual";
        var random = new FifthSunTests.StableRandom(free ? 762952 : 762951);
        for (var attempt = 0; attempt < 5000; attempt++)
        {
            var before = free ? FifthSunTests.Empty() with { NativeRemaining = 2, Carry = new(2, wager, RobotRevolutionEngine.EclipseFeatureMode) }
                : FifthSunTests.Empty();
            var started = DateTime.UtcNow;
            var (historicalOutcome, draws) = ClassicSlotReplayEvidence.Record(random, recording =>
                new RobotRevolutionEngine(recording, RobotRevolutionEngine.HistoricalFifthSunEngineVersion).Spin(wager, account,
                    featureMode: free ? RobotRevolutionEngine.EclipseFeatureMode : null, persistedPityMisses: before.Pity, isFreeSpin: free));
            var progress = RobotRevolutionRoundPolicy.Advance(before, historicalOutcome, false);
            var round = new RobotRevolutionPreparedRound(Guid.NewGuid(), account, new($"fifth-sun-visual-{free}", wager, free), before,
                new(historicalOutcome, draws), progress, RobotRevolutionRoundPolicy.OutcomeDigest(historicalOutcome), free ? 0 : wager * 25,
                RobotRevolutionRoundPolicy.ToCents(historicalOutcome.Payout.TotalPoints, 25), false, started, null);
            var feature = RobotRevolutionEngine.DescribeFeature(round.Outcome)!;
            if (!free && (feature.TotalAwardPoints == 0 ||
                !round.Outcome.Payout.Paylines.Any(line => line.PaylineId <= 23 && line.AmountPoints > 0) ||
                !round.Outcome.Reels.SelectMany(column => column).Contains("ECLIPSE"))) continue;
            var id = round.Outcome.SpinId.ToString("N");
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, account, id),
                free ? null : $"{id}-wager", round.PayoutCents > 0 ? $"{id}-payout" : null,
                50_000, 50_000 - round.WagerCents, 50_000 - round.WagerCents + round.PayoutCents,
                DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
            var settled = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
            { Round = round, ChargedWagerCents = round.WagerCents };
            var report = RobotRevolutionSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
            using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
            Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, proof.RootElement.GetProperty("data")));
            FifthSunTests.WriteArtifact(free ? "native-free-spin.json" : "native-paid-spin.json", RobotRevolutionSpinResponse.From(settled));
            FifthSunTests.WriteArtifact(free ? "native-free-origin.json" : "native-paid-origin.json", new
            {
                source = "Native engine, recorded-draw replay, verified report and in-memory receipt fixture. Not a persisted wallet transaction.",
                seed = free ? 762952 : 762951, attempt = attempt + 1, round.Outcome.Audit.EngineVersion,
                wagerPoints = wager, startingState = RobotRevolutionRoundStateReply.From(before, balanceCents: 50_000),
                noProductionEndpointOrAccount = true
            });
            return;
        }
        throw new InvalidOperationException("No qualifying native visual response within the bounded fixture search.");
    }
}
