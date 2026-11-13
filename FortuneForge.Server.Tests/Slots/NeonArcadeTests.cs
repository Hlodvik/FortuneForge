using System.Text.Json;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class NeonArcadeTests
{
    [Fact]
    public void NeonOwnsItsRolesWeightsAndCentUnitsWithoutChangingAnotherTheme()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        Assert.Equal(1m, game.Wagering.PointValueInCents);
        Assert.Equal(50, game.Wagering.MinimumWagerPoints);
        Assert.Equal(10_000, game.Wagering.MaximumWagerPoints);
        Assert.Equal(50, game.Wagering.WagerIncrementPoints);
        Assert.Null(game.FreeGames);
        Assert.Null(game.Energy);
        Assert.Null(game.SpecialPoints);
        Assert.Null(game.Math.Targets.Rtp);
        Assert.Equal(0.765m, game.Math.PayoutMultiplier);
        Assert.Equal(new[] { 3, 5 }, game.Symbols.WildSubstitutionMatchLengths);
        Assert.Equal(25m, provider.GetGame(SlotSpecialRoundProfiles.OceanOdysseyGameId)!.Wagering.PointValueInCents);
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        Assert.All(strips, strip =>
        {
            Assert.Equal(4096, strip.Count);
            Assert.Equal(928, strip.Count(symbol => symbol is "2" or "4" or "5" or "7"));
            Assert.Equal(927, strip.Count(symbol => symbol == "3"));
            Assert.Equal(836, strip.Count(symbol => symbol == "SEAL_ROWS"));
            Assert.Equal(1860, strip.Count(symbol => symbol is "SEAL_SYNC" or "SEAL_ROWS" or "SEAL_PAW"));
            Assert.Equal(16, strip.Count(symbol => symbol == "FREE"));
            Assert.Equal(2, strip.Count(symbol => symbol == "6"));
            Assert.Single(strip, symbol => symbol == "POWER");
            Assert.DoesNotContain("SEAL_RAND", strip);
        });
        Assert.False(strips[0].SequenceEqual(strips[1]));
        Assert.Equal(strips[0], provider.GetReelSet(game.Math.ReelSetId)!.Reels[0]);
    }

    [Fact]
    public void BonusRoundsOnceToWholeCentsAndKeepsEveryNestedAwardBalanced()
    {
        var first = Paid(1, 50);
        var second = Paid(2, 50);
        IReadOnlyList<IReadOnlyList<string>> oneBonus = [["FREE"], ["3"]];
        var single = NeonArcadeBonus.Apply(new(50, [first]), oneBonus);
        Assert.Equal(63, single.Payout.TotalPoints); // 50c × 1.25 = 62.5c → 63c.
        var combined = NeonArcadeBonus.Apply(new(100, [first, second]), oneBonus);
        Assert.Equal(125, combined.Payout.TotalPoints); // Not two separately rounded 63c awards.
        Assert.Equal(new long[] { 63, 62 }, combined.Payout.Paylines.Select(line => line.AmountPoints));
        Assert.Equal(25, combined.Evidence.PaylineBoostPoints);
        Assert.All(combined.Payout.Paylines, line => Assert.Equal(line.AmountPoints, line.Matches.Sum(match => match.AmountPoints)));
        var stacked = NeonArcadeBonus.Apply(new(50, [first]), [["FREE", "FREE"]]);
        Assert.Equal(1.5m, stacked.Evidence.PaylineBoostMultiplier);
        Assert.Equal(75, stacked.Payout.TotalPoints);
        Assert.Equal(0, NeonArcadeBonus.Apply(new(0, []), oneBonus).Payout.TotalPoints);
    }

    [Fact]
    public void DiscoBallDoesNotAwardCarFreeSpins()
    {
        var disco = NeonArcadeBonus.Apply(new(0, []), [["POWER"]]);
        Assert.Equal(0, disco.FreeSpinsAwarded);
        Assert.Contains(disco.Evidence.Activators, activator => activator.Kind == "disco-ball");
        var car = NeonArcadeBonus.Apply(new(0, []), [["6"]]);
        Assert.Equal(NeonArcadeSlotDefinitions.SpecialPlaySpins, car.FreeSpinsAwarded);
    }

    [Fact]
    public void DiscoPlayCascadesPayingSymbolsAndReplaysEveryRefill()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = Enumerable.Range(0, strips.Count)
            .Select(reel => Start(strips[reel], "3")).ToArray();
        var random = new StopsThenZero(stops);
        var engine = new SpinService(provider, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);

        var result = engine.Spin(game.Id, 50, "neon-disco-test", false,
            isNeonDiscoPlay: true);
        var cascade = Assert.IsType<NeonDiscoCascadeOutcome>(result.NeonDiscoCascade);
        Assert.NotEmpty(cascade.Steps);
        Assert.Equal(result.Reels.SelectMany(reel => reel), cascade.InitialReels.SelectMany(reel => reel));
        Assert.Equal(cascade.Steps.Sum(step => step.Payout.TotalPoints), cascade.TotalPoints);
        Assert.Equal(cascade.TotalPoints, result.Payout.TotalPoints);
        Assert.Empty(result.Payout.Paylines);
        Assert.All(cascade.Steps, step =>
        {
            Assert.NotEmpty(step.Cleared);
            Assert.Equal(game.Layout.ReelCount, step.Reels.Count);
            Assert.Equal(game.Layout.ReelCount, step.DropRows.Count);
            Assert.Equal(step.Payout.TotalPoints, step.Payout.Paylines.Sum(line => line.AmountPoints));
        });
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout, isPaid: true));

        var altered = result with
        {
            NeonDiscoCascade = cascade with { TotalPoints = cascade.TotalPoints + 1 }
        };
        Assert.Contains(SlotRoundAudit.Check(altered, altered.Payout, isPaid: true),
            finding => finding.Code == "slot-neon-disco-steps-mismatch");
    }

    [Fact]
    public async Task DiscoDemoCarriesThePaidPlayAndReplaysItsCascadeReceipt()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = Enumerable.Range(0, strips.Count)
            .Select(reel => Start(strips[reel], reel == 0 ? "POWER" : "3")).ToArray();
        var random = new StopsThenZero(stops);
        var engine = new SpinService(provider, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var store = new DemoAudit();
        var controller = new DemoSlotsController(engine, store, NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var request = new DemoSpinRequest(game.Id, 50, false, 0, null, 0);
        var trigger = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request, CancellationToken.None)).Value);
        Assert.Equal(NeonArcadeSlotDefinitions.DiscoPlaySpins, trigger.NeonDiscoSpinsRemaining);
        Assert.Null(trigger.NeonDiscoCascade);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));

        var play = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request with
            {
                NeonDiscoSpinsRemaining = trigger.NeonDiscoSpinsRemaining
            }, CancellationToken.None)).Value);
        Assert.NotNull(play.NeonDiscoCascade);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));
    }

    [Fact]
    public void CommonPropsPayThroughTheEngineAndOneRareSymbolAwardsSpecialPlays()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = new[] { Start(strips[0], "SEAL_SYNC"), Start(strips[1], "SEAL_SYNC"),
            Start(strips[2], "SEAL_SYNC"), Start(strips[3], "FREE"), Start(strips[4], "6") };
        var random = new Stops(stops);
        var engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
        var result = engine.Spin(game.Id, 50, "neon-local-test", false);
        Assert.Contains(result.Payout.Paylines.SelectMany(line => line.Matches), match => match.Match.SymbolId == "SEAL_SYNC");
        Assert.Equal(7, result.FreeSpinsAwarded);
        Assert.Equal(0, result.SpecialPointsAwarded);
        Assert.Empty(result.SealsAwarded);
        Assert.False(result.NeonArcade!.Calibrated);
        Assert.True(result.NeonArcade.FeatureGamesEnabled);
        Assert.Contains(result.NeonArcade.Activators, role => role.Kind == "car");
        Assert.DoesNotContain(result.Payout.Paylines.SelectMany(line => line.Matches), match => match.Match.SymbolId is "6" or "POWER" or "SEAL_RAND" or "FREE");
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
        var v7Evidence = result.NeonArcade with { ModelId = NeonArcadeSlotDefinitions.PreviousDiscoModelId };
        var v7Round = result with
        {
            NeonArcade = v7Evidence,
            EngineAudit = result.EngineAudit! with
            {
                EngineVersion = NeonArcadeSlotDefinitions.PreviousDiscoEngineVersion,
                NeonArcade = v7Evidence
            }
        };
        Assert.Empty(SlotRoundAudit.Check(v7Round, v7Round.Payout));
        Assert.Contains(SlotRoundAudit.Check(result with { NeonArcade = result.NeonArcade with { PaylineBoostPoints = 999 } }, result.Payout),
            finding => finding.Code == "slot-neon-arcade-mismatch");

        var previousEvidence = result.NeonArcade with
        {
            ModelId = NeonArcadeSlotDefinitions.PreviousModelId,
            FeatureGamesEnabled = false
        };
        var previous = result with
        {
            FreeSpinsAwarded = 0,
            NeonArcade = previousEvidence,
            EngineAudit = result.EngineAudit! with
            {
                EngineVersion = NeonArcadeSlotDefinitions.LegacyEngineVersion,
                NeonArcade = previousEvidence
            }
        };
        Assert.Empty(SlotRoundAudit.Check(previous, previous.Payout));
    }

    [Fact]
    public void TripleSevensPayTwelveTimesTheWagerWhileEarlierRoundsStillReplayAtThreeTimes()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = new[] { Start(strips[0], "BANANA"), Start(strips[1], "BANANA"),
            Start(strips[2], "BANANA"), Start(strips[3], "2"), Start(strips[4], "3") };
        var random = new Stops(stops);
        var engine = new SpinService(provider, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        const long wagerPoints = 50;
        var result = engine.Spin(game.Id, wagerPoints, "neon-sevens-test", false);
        var sevenLines = result.Payout.Paylines.Where(line => line.PaylineId is >= 801 and < 901).ToArray();

        Assert.NotEmpty(sevenLines);
        Assert.All(sevenLines, line =>
        {
            Assert.Equal(wagerPoints * NeonArcadeSlotDefinitions.TripleSevenPatternMultiplier, line.AmountPoints);
            Assert.Single(line.Matches);
            Assert.Equal(NeonArcadeSlotDefinitions.TripleSevenPatternMultiplier, line.Matches[0].Multiplier);
        });
        Assert.Equal(sevenLines.Sum(line => line.AmountPoints), result.BananaBonusPoints);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));

        var oldUnit = wagerPoints * 3;
        var oldSevenPoints = sevenLines.Length * oldUnit;
        var oldLines = result.Payout.Paylines.Select(line => line.PaylineId is >= 801 and < 901
            ? line with { AmountPoints = oldUnit, Matches = line.Matches.Select(match =>
                match with { Multiplier = 3, AmountPoints = oldUnit }).ToArray() }
            : line).ToArray();
        var oldPayout = result.Payout with
        {
            TotalPoints = result.Payout.TotalPoints - result.BananaBonusPoints + oldSevenPoints,
            Paylines = oldLines
        };
        var oldEvidence = result.NeonArcade! with { ModelId = NeonArcadeSlotDefinitions.PreviousModelId };
        var oldResult = result with
        {
            Payout = oldPayout,
            BananaBonusPoints = oldSevenPoints,
            NeonArcade = oldEvidence,
            EngineAudit = result.EngineAudit! with
            {
                EngineVersion = NeonArcadeSlotDefinitions.PreviousEngineVersion,
                NeonArcade = oldEvidence
            }
        };
        Assert.Empty(SlotRoundAudit.Check(oldResult, oldPayout));
    }

    [Fact]
    public async Task SpecialPlaysRetriggerOnceAtTheLockedWagerAndContinuePastTheOldDemoLimit()
    {
        var provider = Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = new[] { Start(strips[0], "6"), Start(strips[1], "POWER"),
            Start(strips[2], "3"), Start(strips[3], "SEAL_ROWS"), Start(strips[4], "SEAL_PAW") };
        var random = new Stops([.. stops, .. stops, .. stops, .. stops, .. stops]);
        var engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
        var store = new DemoAudit();
        var controller = new DemoSlotsController(engine, store, NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var start = new DemoSpinRequest(game.Id, 150, false, 0, 50, 0);
        var award = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(start, CancellationToken.None)).Value);
        Assert.Equal(2, award.NeonArcade!.Activators.Count);
        Assert.Equal(7, award.FreeSpinsAwarded);
        Assert.Equal(7, award.FreeSpinsRemaining);
        Assert.Equal(150, award.FreeSpinWagerPoints);
        Assert.Equal(NeonArcadeBonus.PaidCarFeatureMode, award.FreeSpinFeatureMode);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));

        var continuation = start with
        {
            UseFreeSpin = true, FreeSpinsRemaining = 7, FreeSpinWagerPoints = 150,
            FreeSpinFeatureMode = award.FreeSpinFeatureMode
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(
            continuation with { WagerPoints = 200 }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(
            continuation with { UseFreeSpin = false }, CancellationToken.None));
        var consumed = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(continuation, CancellationToken.None)).Value);
        Assert.True(consumed.IsFreeSpin);
        Assert.Equal(2, consumed.NeonArcade!.Activators.Count);
        Assert.Equal(7, consumed.FreeSpinsAwarded);
        Assert.Equal(13, consumed.FreeSpinsRemaining);
        Assert.Equal(150, consumed.FreeSpinWagerPoints);
        Assert.Equal(NeonArcadeBonus.PaidCarFeatureMode, consumed.FreeSpinFeatureMode);
        Assert.Equal(150, consumed.ChargedWagerCents);
        Assert.All(consumed.Reels, reel => Assert.Equal(7, reel.Count));
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));

        var legacy = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            continuation with { FreeSpinFeatureMode = null }, CancellationToken.None)).Value);
        var baseOnlyEvidence = legacy.NeonArcade! with { ModelId = NeonArcadeSlotDefinitions.PreviousModelId };
        var baseOnlyAudit = legacy.EngineAudit! with
        {
            EngineVersion = NeonArcadeSlotDefinitions.BaseOnlyEngineVersion,
            NeonArcade = baseOnlyEvidence
        };
        var baseOnlyResult = legacy with { EngineAudit = baseOnlyAudit, NeonArcade = baseOnlyEvidence };
        Assert.Empty(SlotRoundAudit.Check(baseOnlyResult with { FreeSpinsAwarded = 0 }, legacy.Payout));
        Assert.Contains(SlotRoundAudit.Check(baseOnlyResult, legacy.Payout),
            finding => finding.Code == "slot-free-spins-mismatch");

        var aboveLimit = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            continuation with { FreeSpinsRemaining = 1000 }, CancellationToken.None)).Value);
        Assert.Equal(1006, aboveLimit.FreeSpinsRemaining);
        var continuedAboveLimit = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            continuation with { FreeSpinsRemaining = aboveLimit.FreeSpinsRemaining }, CancellationToken.None)).Value);
        Assert.Equal(1012, continuedAboveLimit.FreeSpinsRemaining);
        Assert.Equal(150, continuedAboveLimit.FreeSpinWagerPoints);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));
    }

    [Fact]
    public void RemovingFreshCollectionAwardsDoesNotChangeLegacyFeatureModeSupport()
    {
        SlotSpecialRoundProfiles.TryGet(SlotSpecialRoundProfiles.NeonNightsGameId, out var profile);
        var settled = SlotSpecialRoundProfiles.SettleDemo(profile,
            [new SlotSealCollection("sync", 25, 50, 26)],
            new Dictionary<string, int> { ["SEAL_SYNC"] = 20 }, 50, false);
        Assert.Equal(0, settled.FreeSpinsAwarded);
        Assert.Empty(settled.Collections);
        Assert.True(SlotSpecialRoundProfiles.IsFeatureMode("sync"));
        Assert.True(SlotSpecialRoundProfiles.HasFeatureMode("sync-rows", "rows"));
    }

    private static PaylinePayout Paid(int id, long cents) => new(id, cents,
        [new(new(id, "3", 3, [new(0, 0), new(1, 0), new(2, 0)], []), 1, cents)]);

    private static int Start(List<string> strip, string symbol) => strip.IndexOf(symbol);

    internal static OptionsSlotsDefinitionProvider Definitions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return new(Options.Create(document.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
    }

    private sealed class Stops(IReadOnlyList<int> values) : IRandomIndexSource
    {
        private int cursor;
        public int Next(int maximumExclusive) => values[cursor++] % maximumExclusive;
    }

    private sealed class StopsThenZero(IReadOnlyList<int> stops) : IRandomIndexSource
    {
        private int cursor;
        public int Next(int maximumExclusive) => cursor < stops.Count
            ? stops[cursor++] % maximumExclusive : 0;
    }

    private sealed class DemoAudit : IDemoSlotAuditStore
    {
        public FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture? Capture { get; private set; }

        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        {
            Capture = DemoSlotRoundRecording.Capture(entry);
            return Task.CompletedTask;
        }
    }
}
