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
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class NeonArcadePaidSpecialPlaysTests
{
    [Fact]
    public async Task ArcadeCabinetAwardsSevenPaidLockedWagerSpinsWithThreePayingRows()
    {
        var provider = NeonArcadeTests.Definitions();
        var game = provider.GetGame(SlotSpecialRoundProfiles.NeonNightsGameId)!;
        var strips = provider.GetReelSet(game.Math.ReelSetId)!.Reels;
        var baseStops = Enumerable.Range(0, 4)
            .Select(reel => StartFor(strips[reel], "3", 0, 4))
            .Append(StartFor(strips[4], "SEAL_RAND", 0, 4))
            .ToArray();
        var bonusStops = Enumerable.Range(0, 5)
            .Select(reel => StartFor(strips[reel], "3", 4, 7))
            .ToArray();
        var random = new Stops([.. baseStops, .. bonusStops]);
        var engine = new SpinService(provider, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var audit = new DemoAudit();
        var controller = new DemoSlotsController(engine, audit, NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var request = new DemoSpinRequest(game.Id, 150, false, 0, null, 0);
        var award = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request, CancellationToken.None)).Value);

        Assert.Equal(7, award.FreeSpinsRemaining);
        Assert.Equal(150, award.FreeSpinWagerPoints);
        Assert.Equal(NeonArcadeBonus.PaidArcadeFeatureMode, award.FreeSpinFeatureMode);
        Assert.Equal(150, award.ChargedWagerCents);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(audit.Capture!.Summary, audit.Capture.PayloadJson));

        var continuation = request with
        {
            UseFreeSpin = true,
            FreeSpinsRemaining = award.FreeSpinsRemaining,
            FreeSpinWagerPoints = award.FreeSpinWagerPoints,
            FreeSpinFeatureMode = award.FreeSpinFeatureMode
        };
        var paidAction = await controller.Spin(continuation, CancellationToken.None);
        Assert.True(paidAction is OkObjectResult,
            System.Text.Json.JsonSerializer.Serialize((paidAction as ObjectResult)?.Value));
        var paidSpin = Assert.IsType<SpinResult>(((OkObjectResult)paidAction).Value);

        Assert.True(paidSpin.IsFreeSpin); // Consumes an awarded play, but still charges the locked wager.
        Assert.Equal(150, paidSpin.ChargedWagerCents);
        Assert.Equal(6, paidSpin.FreeSpinsRemaining);
        Assert.Equal(NeonArcadeBonus.PaidArcadeFeatureMode, paidSpin.FreeSpinFeatureMode);
        Assert.All(paidSpin.Reels, reel => Assert.Equal(7, reel.Count));
        Assert.Contains(paidSpin.Payout.Paylines, line => line.PaylineId == 24 && line.AmountPoints > 0);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(audit.Capture!.Summary, audit.Capture.PayloadJson));
    }

    private static int StartFor(List<string> strip, string symbol, int row, int visibleRows)
    {
        for (var start = 0; start < strip.Count; start++)
        {
            if (strip[(start + row) % strip.Count] != symbol) continue;
            var hasOtherActivator = Enumerable.Range(0, visibleRows).Any(index =>
                index != row && strip[(start + index) % strip.Count] is "6" or "POWER" or "SEAL_RAND");
            if (!hasOtherActivator) return start;
        }
        throw new InvalidOperationException($"No safe stop for {symbol} in row {row}.");
    }

    private sealed class Stops(IReadOnlyList<int> values) : IRandomIndexSource
    {
        private int cursor;
        public int Next(int maximumExclusive) => values[cursor++] % maximumExclusive;
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
