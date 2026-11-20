using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.NeonNights;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class NeonDiscoWagerTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(10000)]
    public async Task DemoRejectsChangedDiscoWagersBeforeGeneratingAnOutcome(long requestedWager)
    {
        var (controller, random, store) = CreateController();
        var request = Request(requestedWager, 12, 300);
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(request, default));
        Assert.Equal(0, random.DrawCount);
        Assert.Null(store.Capture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(300L)]
    public async Task DemoReturnsAndReplaysItsSavedOrAdoptedDiscoWager(long? savedWager)
    {
        var (controller, _, store) = CreateController();
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(Request(300, 12, savedWager), default)).Value);
        Assert.Equal(11, result.NeonDiscoSpinsRemaining);
        Assert.Equal(300, result.NeonDiscoWagerPoints);
        Assert.Equal(300, result.ChargedWagerCents);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));
        var json = JsonSerializer.Serialize(result, RoundAuditCapture.Json);
        Assert.Equal(300, JsonSerializer.Deserialize<SpinResult>(json, RoundAuditCapture.Json)!.NeonDiscoWagerPoints);

        var capture = JsonNode.Parse(store.Capture.PayloadJson)!.AsObject();
        capture["responseResult"]!["neonDiscoWagerPoints"] = 500;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(store.Capture.Summary, capture.ToJsonString()),
            finding => finding.Code == "slot-neon-disco-wager-mismatch");
        capture["request"]!.AsObject().Remove("neonDiscoWagerPoints");
        capture["responseResult"]!.AsObject().Remove("neonDiscoWagerPoints");
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture.Summary, capture.ToJsonString()));
    }

    [Fact]
    public async Task LastDiscoPlayClearsItsWagerLock()
    {
        var (controller, _, store) = CreateController();
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(Request(300, 1, 300), default)).Value);
        Assert.Equal(0, result.NeonDiscoSpinsRemaining);
        Assert.Null(result.NeonDiscoWagerPoints);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));
    }

    [Fact]
    public async Task CarPlayDoesNotReplaceAQueuedDiscoWagerWithItsOwnWager()
    {
        var (controller, _, store) = CreateController();
        var request = Request(500, 12, 300) with
        {
            UseFreeSpin = true, FreeSpinsRemaining = 7, FreeSpinWagerPoints = 500,
            FreeSpinFeatureMode = NeonArcadeBonus.PaidCarFeatureMode
        };
        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request, default)).Value);
        Assert.Equal(12, result.NeonDiscoSpinsRemaining);
        Assert.Equal(300, result.NeonDiscoWagerPoints);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(store.Capture!.Summary, store.Capture.PayloadJson));
    }

    private static DemoSpinRequest Request(long wager, int spins, long? locked) =>
        new(NeonArcadeSlotDefinitions.GameId, wager, false, 0, null, 0,
            NeonDiscoSpinsRemaining: spins, NeonDiscoWagerPoints: locked);

    private static (DemoSlotsController Controller, Draws Random, Audit Store) CreateController()
    {
        var random = new Draws();
        var engine = new SpinService(NeonArcadeTests.Definitions(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var store = new Audit();
        var controller = new DemoSlotsController(engine, store, NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        return (controller, random, store);
    }

    private sealed class Draws : IRandomIndexSource
    {
        private readonly Random random = new(73);
        public int DrawCount { get; private set; }
        public int Next(int maximumExclusive)
        {
            DrawCount++;
            return random.Next(maximumExclusive);
        }
    }

    private sealed class Audit : IDemoSlotAuditStore
    {
        public RoundAuditCapture? Capture { get; private set; }
        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        {
            Capture = DemoSlotRoundRecording.Capture(entry);
            return Task.CompletedTask;
        }
    }
}
