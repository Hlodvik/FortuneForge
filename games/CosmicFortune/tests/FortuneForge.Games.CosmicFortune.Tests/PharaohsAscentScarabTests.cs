using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.CosmicFortune.Tests;

public sealed class PharaohsAscentScarabTests
{
    private const string Account = "pharaoh-scarab-test-account";

    [Theory]
    [InlineData(0, 0)]
    [InlineData(47, 0)]
    [InlineData(48, 1)]
    [InlineData(87, 1)]
    [InlineData(88, 2)]
    [InlineData(95, 2)]
    [InlineData(96, 3)]
    [InlineData(97, 3)]
    [InlineData(98, 4)]
    [InlineData(99, 5)]
    public void InjectionUsesTheGameOwnedDistribution(int roll, int count) =>
        Assert.Equal(count, PharaohsAscentScarabs.CountFromRoll(roll));

    [Fact]
    public void InjectionRejectsOutOfDomainRolls()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PharaohsAscentScarabs.CountFromRoll(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PharaohsAscentScarabs.CountFromRoll(100));
    }

    [Theory]
    [InlineData(3, 3, 257)]
    [InlineData(4, 5, 428)]
    [InlineData(5, 8, 685)]
    [InlineData(6, 8, 685)]
    public void ConnectedGroupsPayTheirTierOnceWithTheProfileScale(int count, long multiplier, long points)
    {
        GridPosition[] connected = [new(0, 0), new(0, 1), new(1, 1), new(1, 2), new(2, 2), new(2, 1)];
        var feature = PharaohsAscentScarabs.Calculate(Grid(connected.Take(count).ToArray()), 100, 0.856m);
        var payline = Assert.Single(feature.Paylines);
        var match = Assert.Single(payline.Matches);
        Assert.Equal(801, payline.PaylineId);
        Assert.Equal(points, feature.PatternBonusPoints);
        Assert.Equal(points, payline.AmountPoints);
        Assert.Equal(points, match.AmountPoints);
        Assert.Equal(multiplier, match.Multiplier);
        Assert.Equal(count, match.Match.MatchLength);
        Assert.Equal(count, match.Match.Positions.Distinct().Count());
        Assert.Equal(PharaohsAscentScarabs.SymbolId, match.Match.SymbolId);
        Assert.Empty(match.Match.WildPositions);
        Assert.Equal(0, feature.CollectorPoints);
        Assert.Equal(0, feature.PawCount);
    }

    [Fact]
    public void SeparateGroupsDoNotCombineOrCountAScarabTwice()
    {
        var feature = PharaohsAscentScarabs.Calculate(Grid(
            new(0, 0), new(0, 1), new(1, 1),
            new(3, 2), new(3, 3), new(4, 3),
            new(2, 0)), 10, 0.856m);
        Assert.Equal(new[] { 801, 802 }, feature.Paylines.Select(line => line.PaylineId));
        Assert.Equal(52, feature.PatternBonusPoints);
        Assert.Equal(6, feature.Paylines.SelectMany(line => line.Matches).SelectMany(match => match.Match.Positions).Distinct().Count());
        Assert.DoesNotContain(feature.Paylines.SelectMany(line => line.Matches).SelectMany(match => match.Match.Positions), position => position == new GridPosition(2, 0));
    }

    [Fact]
    public void DiagonalsAndUnrelatedPairsDoNotPay()
    {
        Assert.Empty(PharaohsAscentScarabs.Calculate(Grid(new(0, 0), new(1, 1), new(2, 2)), 100, 0.856m).Paylines);
        Assert.Empty(PharaohsAscentScarabs.Calculate(Grid(new(0, 0), new(0, 1), new(3, 2), new(4, 2)), 100, 0.856m).Paylines);
    }

    [Fact]
    public void BoundsCannotWrapAcrossRowsOrJaggedReels()
    {
        IReadOnlyList<IReadOnlyList<string>> reels = [
            ["BANANA", "BANANA"], ["2", "2", "BANANA"], [], ["BANANA"]];
        Assert.Empty(PharaohsAscentScarabs.Calculate(reels, 2, 0.856m).Paylines);
        Assert.Empty(PharaohsAscentScarabs.Calculate([], 2, 0.856m).Paylines);
    }

    [Theory]
    [InlineData(96, 3, 3)]
    [InlineData(98, 4, 5)]
    [InlineData(99, 5, 8)]
    public void EveryRewardTierIsReachableThroughTheProductionEngine(int roll, int count, long multiplier)
    {
        var outcome = new CosmicFortuneEngine(new PatternDraws(roll)).Spin(100, Account, persistedPityMisses: 0);
        var feature = PharaohsAscentScarabs.Calculate(outcome.Reels, 100, CosmicFortuneEngine.Profile.PayoutMultiplier);
        Assert.Equal(count, outcome.Reels.SelectMany(reel => reel).Count(symbol => symbol == "BANANA"));
        Assert.Equal(multiplier, Assert.Single(Assert.Single(feature.Paylines).Matches).Multiplier);
        Assert.Equal(feature.PatternBonusPoints, outcome.PatternBonusPoints);
        Assert.Equal(outcome.Payout.TotalPoints, outcome.Payout.Paylines.Sum(line => line.AmountPoints));
        Assert.Equal(CosmicFortuneEngine.EngineVersion, outcome.Audit.EngineVersion);
    }

    [Fact]
    public void HistoricalEngineRetainsItsOldInjectionAndLineBonus()
    {
        var outcome = new CosmicFortuneEngine(new PatternDraws(99), CosmicFortuneEngine.LegacyEngineVersion)
            .Spin(100, Account, persistedPityMisses: 0);
        Assert.Equal(3, outcome.Reels.SelectMany(reel => reel).Count(symbol => symbol == "BANANA"));
        Assert.Equal(257, outcome.PatternBonusPoints);
        Assert.Equal(CosmicFortuneEngine.LegacyEngineVersion, outcome.Audit.EngineVersion);
        Assert.Throws<ArgumentException>(() => new CosmicFortuneEngine(new PatternDraws(99), "unknown-model"));
    }

    [Theory]
    [MemberData(nameof(ReplayVersions))]
    public void PreparedPaidOutcomesReplayAndSettleAtTheirRecordedVersion(string version)
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 100, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var started = DateTime.UtcNow;
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(new PatternDraws(99), random =>
            new CosmicFortuneEngine(random, version).Spin(100, Account, currentEnergyBalance: before.Energy, persistedPityMisses: 0));
        var progress = CosmicFortuneRoundPolicy.Advance(before, outcome, false);
        var round = new CosmicFortunePreparedRound(Guid.NewGuid(), Account, new("pharaoh-replay-version", 100, false),
            before, new(outcome, draws), progress, CosmicFortuneRoundPolicy.OutcomeDigest(outcome), 2_500,
            progress.SettledPayout.TotalPoints * 25, false, started, null);
        CosmicFortuneRoundPolicy.Validate(round);
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(CosmicFortuneEngine.GameId, Account, $"{outcome.SpinId:N}"),
            $"{outcome.SpinId:N}-wager", $"{outcome.SpinId:N}-payout", 50_000, 47_500, 47_500 + round.PayoutCents,
            DateTimeOffset.UtcNow, false, false, LegacyFreeGamesRemaining: 0);
        var result = new CosmicFortuneRoundResult(CosmicFortuneRoundStateReply.From(progress.After), receipt)
        { Round = round, ChargedWagerCents = round.WagerCents };
        var response = CosmicFortuneSpinResponse.From(result);
        var report = CosmicFortuneSpinReport.Capture(round, result, DateTime.UtcNow);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(CosmicFortuneSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        Assert.Equal(progress.SettledPayout.TotalPoints, response.Payout.TotalPoints);
        Assert.Equal(result.Features!.GameFeatureState, response.GameFeatureState);

        if (version == CosmicFortuneEngine.LegacyEngineVersion)
        {
            Assert.Null(response.SpinFeature);
            return;
        }

        var feature = Assert.IsType<PharaohsAscentFeature>(response.SpinFeature);
        Assert.Equal("pharaoh-ascent", feature.Kind);
        Assert.Equal(1.5m, response.PayoutMultiplier);
        var line = Assert.Single(response.Payout.Paylines, line => line.PaylineId == 801);
        var trail = Assert.Single(feature.ScarabTrails);
        Assert.Equal(line.AmountPoints, feature.ScarabTrailPoints);
        Assert.Equal(line.AmountPoints, trail.AmountPoints);
        Assert.Equal(8, trail.Multiplier);
        Assert.Equal(5, trail.SymbolCount);
        Assert.Equal(line.Matches[0].Match.Positions, trail.Positions);

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["result"]!["spinFeature"]!["scarabTrailPoints"] = feature.ScarabTrailPoints + 1;
        Assert.Single(CosmicFortuneSpinEvidence.InspectNative(report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed)));
        changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["result"]!["gameFeatureState"]!["energyBalance"] = 99;
        Assert.Single(CosmicFortuneSpinEvidence.InspectNative(report.Round.Audit.Summary, JsonSerializer.SerializeToElement(changed)));
    }

    public static IEnumerable<object[]> ReplayVersions => new[]
    {
        CosmicFortuneEngine.EngineVersion, CosmicFortuneEngine.PreviousTombEngineVersion,
        CosmicFortuneEngine.PreviousEngineVersion, CosmicFortuneEngine.LegacyEngineVersion,
    }.Select(version => new object[] { version });

    [Theory]
    [InlineData("sync", "SEAL_SYNC")]
    [InlineData("rows", "SEAL_ROWS")]
    [InlineData("paw", "SEAL_PAW")]
    [InlineData("rand", "SEAL_RAND")]
    public void EachCompletedGuardianAwardsItsOwnSevenSpinPower(string mode, string symbol)
    {
        var progress = ClassicSlotCollections.Advance(CosmicFortuneEngine.Profile,
            [new(mode, 23, 2_300)], new Dictionary<string, int> { [symbol] = 1 }, 100, false);
        Assert.Equal(7, progress.FreeSpinsAwarded);
        Assert.Equal(mode, progress.FeatureMode);
        Assert.Equal(100, progress.FreeSpinWagerPoints);
        Assert.Equal(0, progress.Collections.Single(track => track.Mode == mode).Count);
        var historical = ClassicSlotCollections.Advance(CosmicFortuneEngine.ProfileForVersion(CosmicFortuneEngine.LegacyEngineVersion),
            [new(mode, 23, 2_300)], new Dictionary<string, int> { [symbol] = 1 }, 100, false);
        Assert.Equal("sync-rows", historical.FeatureMode);
    }

    [Theory]
    [InlineData("sync", 4, 0)]
    [InlineData("rows", 6, 0)]
    [InlineData("paw", 4, 2)]
    [InlineData("rand", 4, 4)]
    [InlineData("sync-rows", 6, 0)]
    public void AwardedGuardianModesUseTheProductionEffectsAndAcceptHistoricalCombinedCarry(string mode, int rows, int wilds)
    {
        var outcome = new CosmicFortuneEngine(new PatternDraws(0)).Spin(100, Account,
            featureMode: mode, persistedPityMisses: 0, isFreeSpin: true);
        Assert.All(outcome.Reels, reel => Assert.Equal(rows, reel.Count));
        if (mode is "sync" or "sync-rows") Assert.Equal(outcome.Reels[0], outcome.Reels[1]);
        if (mode is "paw" or "rand") Assert.Equal(wilds, outcome.Reels.SelectMany(reel => reel).Count(symbol => symbol == "ACE"));
    }

    private static IReadOnlyList<IReadOnlyList<string>> Grid(params GridPosition[] positions)
    {
        var reels = Enumerable.Range(0, 5).Select(_ => Enumerable.Repeat("2", 4).ToArray()).ToArray();
        foreach (var position in positions) reels[position.Reel][position.Row] = "BANANA";
        return reels;
    }

    private sealed class PatternDraws(int roll) : IGameRandomSource
    {
        private int position;
        private bool patternRolled;

        public int Next(int maximumExclusive)
        {
            if (position++ < 5) return 0;
            if (maximumExclusive != 100) return 0;
            if (patternRolled) return 99;
            patternRolled = true;
            return roll;
        }
    }
}
