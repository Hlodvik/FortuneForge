using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class FifthSunTotalityTests
{
    [Theory]
    [InlineData(FifthSunTotalityDefinitions.SolarMode, FifthSunTotalityDefinitions.SolarSymbol)]
    [InlineData(FifthSunTotalityDefinitions.LunarMode, FifthSunTotalityDefinitions.LunarSymbol)]
    public void OneCentreEclipseStartsFivePaidLockedRounds(string mode, string symbol)
    {
        var before = Empty();
        var round = Trigger(symbol);
        Assert.Equal(300, round.WagerCents);
        Assert.False(round.Outcome.IsFreeSpin);
        Assert.Equal(0, round.Outcome.FreeSpinsAwarded);
        Assert.Equal(new FifthSunPaidMode(mode, 5, 12), FifthSunPaidModeState.Read(round.Progress.After));
        Assert.Null(round.Progress.After.Carry);
        Assert.Equal(0, round.Progress.After.NativeRemaining);
        Assert.Equal(RobotRevolutionEngine.EngineVersion, round.Outcome.Audit.EngineVersion);
        Assert.Equal(1, round.Outcome.Reels.SelectMany(reel => reel).Count(FifthSunTotalityDefinitions.IsEclipseSymbol));
        RobotRevolutionRoundPolicy.Validate(round);

        var state = round.Progress.After;
        for (var remaining = 5; remaining > 0; remaining--)
        {
            var stored = RobotRevolutionRoundStateCodec.Storage.Write(state);
            var restored = RobotRevolutionRoundStateCodec.Storage.Decode(stored, 0);
            Assert.True(restored.Matches(state));
            Assert.Equal(remaining, FifthSunPaidModeState.Read(restored)!.Remaining);
            Assert.Throws<FifthSunPaidModeWagerLockedException>(() => RobotRevolutionRoundPolicy.Prepare("totality-test",
                new("wrong-wager", 300, false), restored, DateTime.UtcNow, null, new FifthSunTests.StableRandom(9)));
            Assert.Throws<RobotRevolutionFreeSpinsUnavailableException>(() => RobotRevolutionRoundPolicy.Prepare("totality-test",
                new("wrong-free", 12, true), restored, DateTime.UtcNow, null, new FifthSunTests.StableRandom(9)));
            var next = RobotRevolutionRoundPolicy.Prepare("totality-test", new("paid-mode-" + remaining, 12, false), restored,
                DateTime.UtcNow, null, new FifthSunTests.StableRandom(remaining));
            Assert.Equal(300, next.WagerCents);
            Assert.False(next.Outcome.IsFreeSpin);
            Assert.Equal(mode, next.Outcome.Audit.FeatureMode);
            Assert.Equal(0, next.Outcome.FreeSpinsAwarded);
            Assert.All(next.Outcome.Reels, reel => Assert.Equal(4, reel.Count));
            Assert.DoesNotContain(next.Outcome.Reels.SelectMany(reel => reel), FifthSunTotalityDefinitions.IsEclipseSymbol);
            var feature = RobotRevolutionEngine.DescribeFeature(next.Outcome)!;
            Assert.Equal(3, feature.Alignments!.Count);
            Assert.Equal(3, feature.Alignments.Select(alignment => alignment.DialStep).Distinct().Count());
            Assert.Equal(feature.TotalAwardPoints, next.Outcome.Payout.Paylines.Where(line => line.PaylineId >= 1000).Sum(line => line.AmountPoints));
            RobotRevolutionRoundPolicy.Validate(next);
            state = next.Progress.After;
        }
        Assert.Null(FifthSunPaidModeState.Read(state));
    }

    [Fact]
    public void GuardianMemoryLinksLaterAwardsAndTotalityPaysOnce()
    {
        var board = Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("SUNSTONE", 4).ToArray()).ToArray();
        foreach (var mode in new[] { FifthSunTotalityDefinitions.SolarMode, FifthSunTotalityDefinitions.LunarMode })
        {
            var feature = FifthSunTotalityWheel.Resolve(board, 12, mode);
            Assert.Equal(3, feature.AlignedRings);
            Assert.All(feature.Alignments!, alignment => Assert.Equal(3, alignment.AwakenedGuardianIds.Count));
            Assert.Equal((long)(24m * FifthSunTotalityDefinitions.TotalityAwardMultiplier), feature.TotalityAwardPoints!.Value);
            Assert.Equal(feature.Alignments!.Sum(alignment => alignment.TotalAwardPoints) + feature.TotalityAwardPoints, feature.TotalAwardPoints);
            Assert.Equal(1, FifthSunTotalityWheel.CalculatePayout(board, 12, mode).Paylines.Count(line => line.PaylineId == 1010));
        }
    }

    [Fact]
    public void NativePaidStateRejectsTamperingAndSharedCodecsStayOptIn()
    {
        var state = Empty() with { NativeStateJson = FifthSunPaidModeState.Write(new(FifthSunTotalityDefinitions.SolarMode, 5, 12)) };
        Assert.False(state.Matches(state with { NativeStateJson = FifthSunPaidModeState.Write(new(FifthSunTotalityDefinitions.LunarMode, 5, 12)) }));
        Assert.Throws<InvalidOperationException>(() => FifthSunPaidModeState.Read(state with { NativeRemaining = 1, Carry = new(1, 12, "rows") }));
        var codec = new ClassicSlotRoundStateCodec("test", "owner", "test", "state", "Test", false, 25, _ => { });
        Assert.Throws<InvalidOperationException>(() => codec.Write(state));
        var old = Empty();
        var json = JsonSerializer.Serialize(old, RoundAuditCapture.Json);
        Assert.DoesNotContain("nativeStateJson", json);
        var malformed = new[] { "{}", "{\"contractVersion\":2}", "{\"contractVersion\":1,\"paidMode\":{\"mode\":\"solar-eclipse\",\"remaining\":6,\"wagerPoints\":12}}" };
        foreach (var value in malformed) Assert.Throws<InvalidOperationException>(() => FifthSunPaidModeState.Validate(value));
    }

    [Theory]
    [InlineData(FifthSunTotalityDefinitions.SolarMode)]
    [InlineData(FifthSunTotalityDefinitions.LunarMode)]
    public void NativePaidModeProofReplaysAndExportsRealReadOnlyFixtures(string mode)
    {
        var before = Empty() with { NativeStateJson = FifthSunPaidModeState.Write(new(mode, 5, 12)) };
        var round = RobotRevolutionRoundPolicy.Prepare("totality-fixture", new("totality-fixture-" + mode, 12, false), before,
            DateTime.UtcNow, null, new FifthSunTests.StableRandom(mode == FifthSunTotalityDefinitions.SolarMode ? 81451 : 81452));
        Export(mode, round);
        Export(mode + "-trigger", Trigger(mode == FifthSunTotalityDefinitions.SolarMode ? FifthSunTotalityDefinitions.SolarSymbol : FifthSunTotalityDefinitions.LunarSymbol));
        var last = RobotRevolutionRoundPolicy.Prepare("totality-fixture", new("totality-last-" + mode, 12, false),
            before with { NativeStateJson = FifthSunPaidModeState.Write(new(mode, 1, 12)) }, DateTime.UtcNow, null, new FifthSunTests.StableRandom(81453));
        Export(mode + "-last", last);
    }

    private static ClassicSlotRoundState Empty() => RobotRevolutionRoundStateCodec.Storage.Decode(new Dictionary<string, object>(), 0);

    [Theory]
    [InlineData(FifthSunTotalityDefinitions.SolarMode)]
    [InlineData(FifthSunTotalityDefinitions.LunarMode)]
    public void ExportWinningModeFixtureWithGenuineTotalityAndHeart(string mode)
    {
        var before = Empty() with { NativeStateJson = FifthSunPaidModeState.Write(new(mode, 5, 12)) };
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var round = RobotRevolutionRoundPolicy.Prepare("totality-fixture", new("totality-winning-" + mode + "-" + attempt, 12, false),
                before, DateTime.UtcNow, null, new FifthSunTests.StableRandom(425910 + attempt));
            var feature = RobotRevolutionEngine.DescribeFeature(round.Outcome)!;
            if (!feature.EclipseTriggered || !feature.Heart.Lit) continue;
            Assert.Equal(3, feature.Alignments![^1].AwakenedGuardianIds.Count);
            Assert.True(feature.TotalityAwardPoints > 0);
            Export(mode + "-winning", round);
            return;
        }
        throw new InvalidOperationException("No bounded native Totality-with-heart preview board was found.");
    }

    private static RobotRevolutionPreparedRound Trigger(string symbol)
    {
        var strip = RobotRevolutionEngine.CreateReelSet().Reels[2];
        var stop = strip.IndexOf(symbol);
        return RobotRevolutionRoundPolicy.Prepare("totality-fixture", new("totality-trigger-" + symbol, 12, false),
            Empty(), DateTime.UtcNow, null, new CentreStopRandom(stop));
    }

    private static void Export(string name, RobotRevolutionPreparedRound round)
    {
        var id = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, round.AccountId, id),
            id + "-wager", round.PayoutCents > 0 ? id + "-payout" : null, 50_000, 50_000 - round.WagerCents,
            50_000 - round.WagerCents + round.PayoutCents, DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
        var result = new RobotRevolutionRoundResult(RobotRevolutionRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        { Round = round, ChargedWagerCents = round.WagerCents };
        var report = RobotRevolutionSpinReport.Capture(round, result, round.Outcome.Audit.GeneratedAtUtc);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, proof.RootElement.GetProperty("data")));
        FifthSunTests.WriteArtifact("totality-" + name + ".json", RobotRevolutionSpinResponse.From(result));
        FifthSunTests.WriteArtifact("totality-" + name + "-state-before.json", RobotRevolutionRoundStateReply.From(round.Before, balanceCents: receipt.BalanceBeforeCents));
    }

    private sealed class CentreStopRandom(int centreStop) : IGameRandomSource
    {
        private int call;
        public int Next(int maximum) => ++call == 3 ? centreStop % maximum : 0;
    }
}
