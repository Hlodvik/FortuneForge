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

public sealed class MidnightVaultDemoRoundTests
{
    [Theory]
    [InlineData("midnight-vault-paytable-v1")]
    [InlineData("midnight-vault-paytable-v2")]
    [InlineData("midnight-vault-paytable-v3")]
    public async Task LastSpinAdvancesToTheQueuedRoundsOwnOddWagerAndModeAndReplays(string model)
    {
        var (controller, audit) = Controller();
        MidnightVaultBonusRound[] rounds = [new("first", model.EndsWith("v1", StringComparison.Ordinal) ? 6 : 10, 1, 3, null, model),
            new("second", 10, 10, 7, "rows-paw", MidnightVaultSlotDefinitions.PaytableId)];
        var state = new MidnightVaultDemoState(rounds, new Dictionary<string, long>(), new Dictionary<string, long>());
        var first = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            new(SlotSpecialRoundProfiles.DragonHoardGameId, 3, true, 1, 3, 0, MidnightBonusState: state), default)).Value);
        Assert.Equal(3, first.WagerPoints);
        Assert.Equal(model, first.PaytableId);
        if (model != MidnightVaultSlotDefinitions.PaytableId)
            MidnightVaultHistoricalFixtures.AssertHistoricalReels(audit.Last!.EngineResult.EngineAudit!.ReelSet);
        Assert.Equal("second", first.BonusRound?.Id);
        Assert.Equal(7, first.FreeSpinWagerPoints);
        Assert.Equal(10, first.FreeSpinsRemaining);
        Assert.Equal("rows-paw", first.FreeSpinFeatureMode);
        Assert.All(first.Reels, reel => Assert.Equal(4, reel.Count));
        AssertReplay(audit);

        var next = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            new(SlotSpecialRoundProfiles.DragonHoardGameId, 7, true, 10, 7, 0,
                first.SealCollections, first.FreeSpinFeatureMode, first.MidnightBonusState), default)).Value);
        Assert.Equal(7, next.WagerPoints);
        Assert.Equal(MidnightVaultSlotDefinitions.PaytableId, next.PaytableId);
        Assert.Equal("midnight-vault-reels-v2", next.ReelSetId);
        Assert.Equal(9, next.FreeSpinsRemaining);
        Assert.Equal("second", next.BonusRound?.Id);
        Assert.All(next.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Null(next.SlotsCreditsBalance);
        AssertReplay(audit);
    }

    [Fact]
    public async Task CashCannotReplacePendingDemoRoundAndWrongLockedWagerIsRejected()
    {
        var (controller, _) = Controller();
        var state = new MidnightVaultDemoState([new("held", 10, 5, 3, "rows-paw", MidnightVaultSlotDefinitions.PaytableId)],
            new Dictionary<string, long>(), new Dictionary<string, long>());
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(new(SlotSpecialRoundProfiles.DragonHoardGameId,
            2, false, 5, 3, 0, MidnightBonusState: state), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(new(SlotSpecialRoundProfiles.DragonHoardGameId,
            4, true, 5, 3, 0, MidnightBonusState: state), default));
        Assert.Equal(5, state.Rounds[0].RemainingSpins);
    }

    [Fact]
    public async Task OlderLiveDemoCarryWithoutVersionedStateKeepsV2StripsAndRates()
    {
        var (controller, audit) = Controller();
        var response = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            new(SlotSpecialRoundProfiles.DragonHoardGameId, 3, true, 5, 3, 0), default)).Value);
        Assert.Equal(MidnightVaultSlotDefinitions.PreviousPaytableId, response.PaytableId);
        MidnightVaultHistoricalFixtures.AssertHistoricalReels(audit.Last!.EngineResult.EngineAudit!.ReelSet);
        AssertReplay(audit);
    }

    [Theory]
    [InlineData("midnight-vault-paytable-v2")]
    [InlineData("midnight-vault-paytable-v3")]
    public async Task QueuedDemoReplayUsesItsCapturedModelWhenTheOlderRequestHasNoState(string model)
    {
        var (controller, audit) = Controller();
        var state = new MidnightVaultDemoState([new("demo-carry:0", 10, 5, 3, null, model)],
            new Dictionary<string, long>(), new Dictionary<string, long>());
        Assert.IsType<OkObjectResult>(await controller.Spin(new(SlotSpecialRoundProfiles.DragonHoardGameId,
            3, true, 5, 3, 0, MidnightBonusState: state), default));
        // Preserve the real generated outcome/response and exercise the equivalent
        // older request encoding, whose model must come from captured evidence.
        var entry = audit.Last! with { Request = audit.Last!.Request with { MidnightBonusState = null } };
        var captured = DemoSlotRoundRecording.Capture(entry);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(captured.Summary, captured.PayloadJson));
    }

    [Fact]
    public void ExplicitVersionedDemoQueueIsAuthoritativeOverTheOptionalCompatibilityHint()
    {
        var state = new MidnightVaultDemoState([new("explicit", 10, 2, 3, "rows-paw", MidnightVaultSlotDefinitions.PaytableId)],
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var request = new DemoSpinRequest(SlotSpecialRoundProfiles.DragonHoardGameId, 3, true, 2, 3, 0, MidnightBonusState: state);
        Assert.Same(state, DemoSlotsController.CreateMidnightState(request, MidnightVaultSlotDefinitions.PreviousPaytableId));
        Assert.Same(state, DemoSlotsController.CreateMidnightState(request, "ignored-compatibility-hint"));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void V1AggregateCarryWithoutItsOriginalRoundStateFailsClosed(int remaining)
    {
        var request = new DemoSpinRequest(SlotSpecialRoundProfiles.DragonHoardGameId, 3, true, remaining, 3, 0);
        Assert.Throws<ArgumentException>(() => DemoSlotsController.CreateMidnightState(request, MidnightVaultSlotDefinitions.LegacyPaytableId));
        Assert.Throws<ArgumentException>(() => DemoSlotsController.CreateMidnightState(request, "unknown-model"));
    }

    [Fact]
    public void OldV1DemoReceiptFormatStillReplaysWithoutFabricatingTenSpinRounds()
    {
        var frozen = MidnightVaultHistoricalFixtures.V1Audit();
        var path = Path.Combine(Path.GetDirectoryName(SourcePath())!, "__fixtures__", "midnight-vault-paid-v1", "v1-paid-win.json");
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        using var payload = JsonDocument.Parse(saved.RootElement.GetProperty("audit").GetProperty("payloadJson").GetString()!);
        var engine = payload.RootElement.GetProperty("data").GetProperty("result").Deserialize<SpinResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            with { EngineAudit = frozen };
        Assert.Equal(0, engine.FreeSpinsAwarded);
        var progress = SlotSpecialRoundProfiles.SettleDemo(frozen.FeatureProfile!, [], engine.SealsAwarded, engine.WagerPoints, false, engine.SpinId);
        var response = engine with { SlotsCreditsBalance = null, IsFreeSpin = false, FreeSpinsRemaining = 0,
            FreeSpinWagerPoints = null, SpecialPointsBalance = 0, SealCollections = progress.Collections, MidnightBonusState = null };
        var request = new DemoSpinRequest(engine.GameId, engine.WagerPoints, false, 0, null, 0);
        var entry = new DemoSlotAuditEntry(request, engine, response, frozen.GeneratedAtUtc.AddMilliseconds(-1), frozen.GeneratedAtUtc.AddMilliseconds(1));
        var capture = DemoSlotRoundRecording.Capture(entry);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static void AssertReplay(AuditStore audit)
    {
        var capture = DemoSlotRoundRecording.Capture(audit.Last!);
        Assert.DoesNotContain(capture.Summary.Findings, finding => finding.Severity == "error");
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));
    }

    private static (DemoSlotsController, AuditStore) Controller()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(document.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        var random = new SeededRandom();
        var engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
        var audit = new AuditStore();
        return (new(engine, audit, NullLogger<DemoSlotsController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } }, audit);
    }
    private sealed class SeededRandom : IRandomIndexSource
    {
        private readonly Random random = new(20261102);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
    private sealed class AuditStore : IDemoSlotAuditStore
    {
        public DemoSlotAuditEntry? Last { get; private set; }
        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        { Last = entry; return Task.CompletedTask; }
    }
}
