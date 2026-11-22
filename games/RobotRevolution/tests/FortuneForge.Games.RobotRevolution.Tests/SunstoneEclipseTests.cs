using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class SunstoneEclipseTests
{
    [Theory]
    [InlineData(false, 2000, 20)]
    [InlineData(true, 4000, 40)]
    public void FullyAlignedSolarHeartPaysTheBoundedEclipse(bool free, long expected, double cap)
    {
        var board = Board("POWER");
        var feature = SunstoneEclipse.Resolve(board, 100, free);
        Assert.Equal(3, feature.AlignedRings);
        Assert.Equal(3, feature.AlignmentMultiplier);
        Assert.True(feature.Heart.Lit);
        Assert.True(feature.EclipseTriggered);
        Assert.All(feature.Rings, ring => Assert.Equal(5, ring.Matches));
        Assert.Equal(expected, feature.TotalAwardPoints);
        Assert.Equal((decimal)cap, feature.MaxAwardMultiplier);
        Assert.Equal(feature.TotalAwardPoints, feature.Rings.Sum(ring => ring.AwardPoints) + feature.EclipseAwardPoints);
        Assert.All(feature.Rings.SelectMany(ring => ring.Path), position =>
        {
            Assert.InRange(position.Reel, 0, 4);
            Assert.InRange(position.Row, 0, 3);
        });
    }

    [Fact]
    public void WildsPowerGuardianRingsButCannotPretendToBeTheSolarHeart()
    {
        var feature = SunstoneEclipse.Resolve(Board("ACE"), 100, false);
        Assert.Equal(3, feature.AlignedRings);
        Assert.False(feature.Heart.Lit);
        Assert.False(feature.EclipseTriggered);
        Assert.Equal(0, feature.EclipseAwardPoints);
        Assert.Equal(1800, feature.TotalAwardPoints);
    }

    [Fact]
    public void RingsFollowTheActualRotatedGeometryRatherThanWholeBoardCounts()
    {
        var board = Board("2").Select(column => column.ToArray()).ToArray();
        var empty = SunstoneEclipse.Resolve(board, 100, false);
        var jaguar = empty.Rings.Single(ring => ring.Id == "jaguar");
        foreach (var position in jaguar.Path.Take(3)) board[position.Reel][position.Row] = "7";
        var feature = SunstoneEclipse.Resolve(board, 100, false);
        Assert.Equal(1, feature.AlignedRings);
        Assert.Equal(50, feature.Rings[0].AwardPoints);
        board[jaguar.Path[0].Reel][jaguar.Path[0].Row] = "2";
        board[0][(jaguar.Path[0].Row + 1) % 4] = "7";
        var moved = SunstoneEclipse.Resolve(board, 100, false);
        Assert.Equal(0, moved.AlignedRings);
        Assert.Equal(0, moved.TotalAwardPoints);
    }

    [Fact]
    public void PointRoundingCannotExceedTheAdvertisedMaximumAtSmallWagers()
    {
        foreach (var wager in new long[] { 2, 3, 4, 6, 10, 12, 100, 2000 })
            foreach (var free in new[] { false, true })
            {
                var feature = SunstoneEclipse.Resolve(Board("POWER"), wager, free);
                Assert.True(feature.TotalAwardPoints <= wager * feature.MaxAwardMultiplier);
            }
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    public void EveryAlignedTierPaysAtTheMinimumTwoPointWager(int matches, long expectedPoints)
    {
        var board = Board("2").Select(column => column.ToArray()).ToArray();
        var path = SunstoneEclipse.Resolve(board, 2, false).Rings[0].Path;
        foreach (var position in path.Take(matches)) board[position.Reel][position.Row] = "7";
        var feature = SunstoneEclipse.Resolve(board, 2, false);
        Assert.Equal(1, feature.AlignedRings);
        Assert.Equal(expectedPoints, feature.Rings[0].AwardPoints);
        Assert.Equal(expectedPoints, feature.TotalAwardPoints);
    }

    [Fact]
    public void SixRowEclipseGeometryStaysInsideTheActualBoard()
    {
        var feature = SunstoneEclipse.Resolve(Board("POWER", 6), 100, true);
        Assert.Equal(6, feature.Rows);
        Assert.Equal(3, feature.Heart.Row);
        Assert.All(feature.Rings.SelectMany(ring => ring.Path), position => Assert.InRange(position.Row, 0, 5));
        Assert.Equal(4000, feature.TotalAwardPoints);
    }

    [Theory]
    [InlineData(RobotRevolutionEngine.SunstoneEngineVersion)]
    [InlineData(RobotRevolutionEngine.LegacyEngineVersion)]
    public void BothNewAndHistoricalDrawsRetainTheirVersionedPayoutMeaning(string version)
    {
        var random = new StableRandom(507);
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random, source =>
            new RobotRevolutionEngine(source, version).Spin(100, "version-replay", persistedPityMisses: 0));
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, draws, source =>
            new RobotRevolutionEngine(source, version).Spin(100, "version-replay", persistedPityMisses: 0));
        Assert.Equal(version == RobotRevolutionEngine.LegacyEngineVersion ? 1.126m : 0.874m,
            outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(RobotRevolutionRoundPolicy.OutcomeDigest(outcome), RobotRevolutionRoundPolicy.OutcomeDigest(reproduced));
        if (version == RobotRevolutionEngine.LegacyEngineVersion)
            Assert.DoesNotContain(outcome.Payout.Paylines, line => line.PaylineId is >= 1001 and <= 1010);
        else
            Assert.Equal(SunstoneEclipse.Resolve(outcome.Reels, 100, false).TotalAwardPoints,
                outcome.Payout.Paylines.Where(line => line.PaylineId is >= 1001 and <= 1010).Sum(line => line.AmountPoints));
    }

    [Theory]
    [InlineData(RobotRevolutionEngine.SunstoneEngineVersion)]
    [InlineData(RobotRevolutionEngine.LegacyEngineVersion)]
    public void SettledFeatureIsVerifiedAndLegacyPreparedProofsRemainValid(string version)
    {
        const string account = "sunstone-versioned-paid-proof";
        var before = new ClassicSlotRoundState(0, 0, null, 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var started = DateTime.UtcNow;
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(new StableRandom(7629), source =>
            new RobotRevolutionEngine(source, version).Spin(100, account, persistedPityMisses: 0));
        var request = new RobotRevolutionRoundRequest("sunstone-prepared-proof", 100, false);
        var progress = RobotRevolutionRoundPolicy.Advance(before, outcome, false);
        var round = new RobotRevolutionPreparedRound(Guid.NewGuid(), account, request, before,
            new(outcome, draws), progress, RobotRevolutionRoundPolicy.OutcomeDigest(outcome), 2500,
            RobotRevolutionRoundPolicy.ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents), false, started, null);
        RobotRevolutionRoundPolicy.Validate(round);
        var spin = outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, account, spin),
            $"{spin}-wager", round.PayoutCents > 0 ? $"{spin}-payout" : null,
            50_000, 47_500, 47_500 + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGamesRemaining: 0);
        var settled = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
        var report = RobotRevolutionSpinReport.Capture(round, settled, outcome.Audit.GeneratedAtUtc);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        if (version == RobotRevolutionEngine.LegacyEngineVersion)
        {
            Assert.False(data.GetProperty("result").TryGetProperty("spinFeature", out _));
            changed["result"]!["spinFeature"] = JsonSerializer.SerializeToNode(SunstoneEclipse.Resolve(outcome.Reels, 100, false), RoundAuditCapture.Json);
        }
        else
        {
            Assert.Equal("sunstone-eclipse", data.GetProperty("result").GetProperty("spinFeature").GetProperty("kind").GetString());
            changed["result"]!["spinFeature"]!["totalAwardPoints"] = 99999;
        }
        Assert.Single(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed)));
    }

    [Fact]
    public void SeededMathSmokeSeparatesFeatureReturnFromHistoricalBaseAndBonusReturn()
    {
        const int paidSpins = 10000;
        const int seed = 762951;
        const long wager = 100;
        object Sample(string version)
        {
            var engine = new RobotRevolutionEngine(new StableRandom(seed), version);
            var paid = 0;
            var free = 0;
            var pending = 0;
            var pity = 0;
            var featureHits = 0;
            var eclipses = 0;
            var payout = 0L;
            var featurePoints = 0L;
            var highestFeatureMultiple = 0m;
            while (paid < paidSpins || pending > 0)
            {
                var isFree = pending > 0;
                if (isFree) { pending--; free++; } else paid++;
                if (free > paidSpins * 10) throw new InvalidOperationException("Bonus continuation did not terminate within the smoke limit.");
                var outcome = engine.Spin(wager, "sunstone-math-smoke", featureMode: isFree
                        ? version == RobotRevolutionEngine.LegacyEngineVersion ? "rows" : RobotRevolutionEngine.SunstoneFeatureMode : null,
                    persistedPityMisses: pity, isFreeSpin: isFree);
                pity = outcome.ConsecutiveFiveMisses;
                pending += outcome.FreeSpinsAwarded;
                payout += outcome.Payout.TotalPoints;
                if (version == RobotRevolutionEngine.LegacyEngineVersion) continue;
                var feature = SunstoneEclipse.Resolve(outcome.Reels, wager, isFree);
                featurePoints += feature.TotalAwardPoints;
                if (feature.TotalAwardPoints > 0) featureHits++;
                if (feature.EclipseTriggered) eclipses++;
                highestFeatureMultiple = Math.Max(highestFeatureMultiple, feature.TotalAwardPoints / (decimal)wager);
                Assert.InRange(feature.TotalAwardPoints, 0, (long)(wager * feature.MaxAwardMultiplier));
                Assert.Equal(feature.TotalAwardPoints,
                    outcome.Payout.Paylines.Where(line => line.PaylineId is >= 1001 and <= 1010).Sum(line => line.AmountPoints));
            }
            return new { engineVersion = version, paidSpins = paid, freeSpins = free, totalSpins = paid + free,
                wagerPoints = paid * wager, payoutPoints = payout, estimatedReturn = payout / (decimal)(paid * wager),
                featurePoints, featureReturn = featurePoints / (decimal)(paid * wager), featureHits, eclipses, highestFeatureMultiple };
        }
        var report = new { mathVersion = SunstoneEclipse.MathVersion, seed, wagerPointsPerPaidSpin = wager,
            qualification = "Deterministic smoke estimate only; not a certified RTP or payout calibration.",
            legacy = Sample(RobotRevolutionEngine.LegacyEngineVersion), sunstone = Sample(RobotRevolutionEngine.SunstoneEngineVersion) };
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../artifacts/backend-completion"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sunstone-math-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Theory]
    [InlineData("rows", RobotRevolutionEngine.LegacyEngineVersion, 1.126)]
    [InlineData(RobotRevolutionEngine.SunstoneFeatureMode, RobotRevolutionEngine.SunstoneEngineVersion, 0.874)]
    public void OwedBonusCarryKeepsItsVersionAndLockedWager(string mode, string version, double scale)
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, mode), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var round = RobotRevolutionRoundPolicy.Prepare("sunstone-bonus-version", new("bonus-version", 3, true),
            before, DateTime.UtcNow, null, new StableRandom(70));
        Assert.Equal(version, round.Outcome.Audit.EngineVersion);
        Assert.Equal((decimal)scale, round.Outcome.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(3, round.Outcome.WagerPoints);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, round.Progress.After.LegacyRemaining);
        Assert.Equal(mode, round.Progress.After.Carry!.Mode);
        Assert.Equal(3, round.Progress.After.Carry.WagerPoints);
        Assert.Equal(version == RobotRevolutionEngine.SunstoneEngineVersion,
            RobotRevolutionSpinResponse.Capture(round).SpinFeature is not null);
        RobotRevolutionRoundPolicy.Validate(round);
    }

    [Fact]
    public void NaturallySampledSettledEclipseExportsItsRealPublicFeatureForVisualVerification()
    {
        const string account = "sunstone-visual-fixture";
        var random = new StableRandom(762951);
        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var before = new ClassicSlotRoundState(0, 0, null, 0, 0, 0,
                new Dictionary<string, long>(), new Dictionary<string, long>());
            var started = DateTime.UtcNow;
            var (outcome, draws) = ClassicSlotReplayEvidence.Record(random, source =>
                new RobotRevolutionEngine(source, RobotRevolutionEngine.SunstoneEngineVersion).Spin(100, account, persistedPityMisses: 0));
            var feature = SunstoneEclipse.Resolve(outcome.Reels, 100, false);
            if (!feature.EclipseTriggered) continue;
            var progress = RobotRevolutionRoundPolicy.Advance(before, outcome, false);
            var round = new RobotRevolutionPreparedRound(Guid.NewGuid(), account, new("visual-native-eclipse", 100, false),
                before, new(outcome, draws), progress, RobotRevolutionRoundPolicy.OutcomeDigest(outcome), 2500,
                RobotRevolutionRoundPolicy.ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents), false, started, null);
            RobotRevolutionRoundPolicy.Validate(round);
            var spin = outcome.SpinId.ToString("N");
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, account, spin),
                $"{spin}-wager", $"{spin}-payout", 50_000, 47_500, 47_500 + round.PayoutCents,
                DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
            var settled = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
            {
                Round = round,
                ChargedWagerCents = round.WagerCents
            };
            var report = RobotRevolutionSpinReport.Capture(round, settled, outcome.Audit.GeneratedAtUtc);
            using var evidence = JsonDocument.Parse(report.Round.Audit.PayloadJson);
            Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, evidence.RootElement.GetProperty("data")));
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../artifacts/sunstone-eclipse"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "native-spin.json"), JsonSerializer.Serialize(RobotRevolutionSpinResponse.From(settled),
                new JsonSerializerOptions(RoundAuditCapture.Json) { WriteIndented = true }));
            File.WriteAllText(Path.Combine(directory, "native-spin-origin.json"), JsonSerializer.Serialize(new
            {
                source = "Native new engine, exact draw replay, valid report and in-memory wallet-receipt fixture; not a production account or persisted production transaction.",
                seed = 762951, attempt = attempt + 1, outcome.Audit.EngineVersion,
                baseScale = outcome.Audit.EffectiveGame.Math.PayoutMultiplier,
                feature = SunstoneEclipse.MathVersion, rootDemoOrProductionEndpointCalled = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        throw new InvalidOperationException("The fixed-seed visual sample did not produce an Eclipse within its bounded search.");
    }

    [Fact]
    public void BonusModeVersionMarkerChangesMetadataButNotTheQualifiedNewMath()
    {
        var payoutMethod = typeof(SunstoneEclipse).GetMethods(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Single(method => method.Name == "CalculatePayout" && method.GetParameters().Length == 4);
        Assert.NotNull(payoutMethod);
        var expectedRandom = new StableRandom(816731);
        var currentRandom = new StableRandom(816731);
        var pity = 0;
        const int frames = 512;
        for (var frame = 0; frame < frames; frame++)
        {
            var boost = frame % 17 == 0;
            var (alias, aliasDraws) = ClassicSlotReplayEvidence.Record(expectedRandom, source =>
                new ClassicSlotEngine(ClassicSlotDefinitionFactory.Create(RobotRevolutionEngine.SunstoneProfile),
                    RobotRevolutionEngine.SunstoneProfile, source, RobotRevolutionEngine.SunstoneEngineVersion,
                    new ClassicSlotEngineOptions
                    {
                        AllowFreeSpinCarryWager = true,
                        FeaturePayoutCalculator = (reels, wager, scale) =>
                            (ClassicFeaturePayout)payoutMethod.Invoke(null, [reels, wager, scale, true])!
                    }).Spin(100, "old-free-alias", boost, featureMode: "rows", persistedPityMisses: pity, isFreeSpin: true));
            var (current, currentDraws) = ClassicSlotReplayEvidence.Record(currentRandom, source =>
                new RobotRevolutionEngine(source, RobotRevolutionEngine.SunstoneEngineVersion).Spin(100, "new-free-marker", boost,
                    featureMode: RobotRevolutionEngine.SunstoneFeatureMode, persistedPityMisses: pity, isFreeSpin: true));
            Assert.Equal(aliasDraws, currentDraws);
            alias = alias with { SpinId = Guid.Empty, Audit = alias.Audit with { GeneratedAtUtc = DateTime.UnixEpoch, FeatureMode = "rows" } };
            current = current with { SpinId = Guid.Empty, Audit = current.Audit with { GeneratedAtUtc = DateTime.UnixEpoch, FeatureMode = "rows" } };
            Assert.Equal(RobotRevolutionRoundPolicy.OutcomeDigest(alias), RobotRevolutionRoundPolicy.OutcomeDigest(current));
            pity = current.ConsecutiveFiveMisses;
        }
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../artifacts/sunstone-eclipse"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "mode-version-equivalence.json"), JsonSerializer.Serialize(new
        {
            status = "passed", pairedFrames = frames, seed = 816731,
            assertion = "Exact draw bytes and complete engine outcome digests match after normalizing only spin identity, generated time and the free-mode audit marker. Includes earned-layout free spins, pity and periodic special boosts.",
            oldAlias = "rows", newVersionMarker = RobotRevolutionEngine.SunstoneFeatureMode,
            newBasePayoutMultiplier = RobotRevolutionEngine.SunstoneProfile.PayoutMultiplier,
            historicalBasePayoutMultiplier = RobotRevolutionEngine.LegacyProfile.PayoutMultiplier
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void NativeFreeEclipseFixtureRetainsSixRowsLockedWagerAndDoubledAward()
    {
        const string account = "sunstone-free-visual-fixture";
        var random = new StableRandom(891);
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var before = new ClassicSlotRoundState(2, 0, new(2, 100, RobotRevolutionEngine.SunstoneFeatureMode), 0, 0, 0,
                new Dictionary<string, long>(), new Dictionary<string, long>());
            var round = RobotRevolutionRoundPolicy.Prepare(account, new("visual-native-free-eclipse", 100, true),
                before, DateTime.UtcNow, null, random);
            var feature = SunstoneEclipse.Resolve(round.Outcome.Reels, 100, true);
            if (feature.TotalAwardPoints == 0 || round.Outcome.FreeSpinsAwarded > 0) continue;
            Assert.Equal(SunstoneEclipse.Resolve(round.Outcome.Reels, 100, false).TotalAwardPoints * 2, feature.TotalAwardPoints);
            Assert.Equal(6, feature.Rows);
            Assert.Equal(0, round.WagerCents);
            var spin = round.Outcome.SpinId.ToString("N");
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, account, spin),
                null, $"{spin}-payout", 50_000, 50_000, 50_000 + round.PayoutCents,
                DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
            var settled = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
            {
                Round = round,
                ChargedWagerCents = 0
            };
            var response = RobotRevolutionSpinResponse.From(settled);
            Assert.True(response.IsFreeSpin);
            Assert.Equal(1, response.FreeSpinsRemaining);
            Assert.Equal(100, response.FreeSpinWagerPoints);
            Assert.Equal(RobotRevolutionEngine.SunstoneFeatureMode, response.FreeSpinFeatureMode);
            var report = RobotRevolutionSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
            using var evidence = JsonDocument.Parse(report.Round.Audit.PayloadJson);
            Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, evidence.RootElement.GetProperty("data")));
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../artifacts/sunstone-eclipse"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "native-free-spin.json"), JsonSerializer.Serialize(response,
                new JsonSerializerOptions(RoundAuditCapture.Json) { WriteIndented = true }));
            File.WriteAllText(Path.Combine(directory, "native-free-spin-origin.json"), JsonSerializer.Serialize(new
            {
                source = "Native new free-spin engine, exact draw replay, valid report and in-memory wallet-receipt fixture; not a production account or persisted production transaction.",
                seed = 891, attempt = attempt + 1, rows = 6, wagerPoints = 100, freeSpinsRemaining = 1,
                chargedWagerCents = 0, feature.TotalAwardPoints, rootDemoOrProductionEndpointCalled = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        throw new InvalidOperationException("The fixed-seed free-spin fixture did not produce a visible Sunwheel award within its bounded search.");
    }

    private static IReadOnlyList<IReadOnlyList<string>> Board(string symbol, int rows = 4) =>
        Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat(symbol, rows).ToArray()).ToArray();

    private sealed class StableRandom(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
