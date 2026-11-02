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
    [Fact]
    public async Task LastSpinAdvancesToTheQueuedRoundsOwnOddWagerAndModeAndReplays()
    {
        var (controller, audit) = Controller();
        MidnightVaultBonusRound[] rounds = [new("first", 10, 1, 3, null, MidnightVaultSlotDefinitions.PaytableId),
            new("second", 10, 10, 7, "rows-paw", MidnightVaultSlotDefinitions.PaytableId)];
        var state = new MidnightVaultDemoState(rounds, new Dictionary<string, long>(), new Dictionary<string, long>());
        var first = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(await controller.Spin(
            new(SlotSpecialRoundProfiles.DragonHoardGameId, 3, true, 1, 3, 0, MidnightBonusState: state), default)).Value);
        Assert.Equal(3, first.WagerPoints);
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
