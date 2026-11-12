using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Games.Abstractions.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class DemoSlotsContractTests
{
    [Fact]
    public void LegacyDemoReplayHarness_IsNotAnHttpController()
    {
        Assert.Single(typeof(DemoSlotsController).GetCustomAttributes(typeof(NonControllerAttribute), inherit: true));

        var dependencies = Assert.Single(typeof(DemoSlotsController).GetConstructors())
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Contains(typeof(SpinService), dependencies);
        Assert.Contains(typeof(IDemoSlotAuditStore), dependencies);
        Assert.DoesNotContain(dependencies, dependency =>
            dependency.Namespace?.Contains("Accounts", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Status_ReturnsNoContentForKnownGamesAndNotFoundForUnknownGames()
    {
        var controller = CreateController();

        Assert.IsType<NoContentResult>(controller.Status("demo-contract"));
        Assert.IsType<NotFoundObjectResult>(controller.Status("missing-game"));
        Assert.IsType<BadRequestObjectResult>(controller.Status(""));
    }

    [Fact]
    public async Task Spin_ReturnsResultWithoutAnAccountBalanceAndPersistsNonfinancialEvidence()
    {
        var store = new RecordingDemoAuditStore();
        var controller = CreateController(store);
        using var scope = GameSecurityContext.Push(new GameRequestObservation(
            "demo-request-1", "public-demo", null, "203.0.113.3",
            DateTime.UtcNow, "POST", "/api/slots/demo/spins", "demo-contract",
            null, null, null, null, "sanitized", "connection"));

        var response = await controller.Spin(new DemoSpinRequest(
            "demo-contract",
            2,
            UseFreeSpin: false,
            FreeSpinsRemaining: 0,
            FreeSpinWagerPoints: null,
            EnergyBalance: 0), default);

        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Null(result.SlotsCreditsBalance);
        Assert.Equal(2, result.WagerPoints);
        Assert.Equal(25, result.PointValueInCents);
        Assert.False(result.IsFreeSpin);
        var entry = Assert.IsType<DemoSlotAuditEntry>(store.Entry);
        Assert.Equal(result.SpinId, entry.EngineResult.SpinId);
        Assert.NotNull(entry.EngineResult.EngineAudit);
        var capture = DemoSlotRoundRecording.Capture(entry);
        Assert.Equal("demo-request-1", capture.Request?.RequestId);
        Assert.Equal("slot-demo-nonfinancial-v1", capture.Summary.EvidenceKind);
        Assert.False(Assert.Single(capture.Summary.Participants).IsAccountBacked);
        Assert.All(capture.Summary.Participants, participant =>
        {
            Assert.Equal(0, participant.WagerCents);
            Assert.Equal(0, participant.PayoutCents);
        });
        Assert.Contains("\"financial\":false", capture.PayloadJson);
        Assert.Contains("\"engineAudit\"", capture.PayloadJson);
    }

    private static DemoSlotsController CreateController(IDemoSlotAuditStore? auditStore = null, SpinService? spinService = null) => new(
            spinService ?? new SpinService(
                new Definitions(),
                new Reels(),
                new Evaluator(),
                new Payouts(),
                new Random()),
            auditStore ?? new RecordingDemoAuditStore(),
            NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    [Fact]
    public async Task OlympusTumbleShowcaseReturnsBothPayoutsAndReplaysAsNonfinancial()
    {
        var auditStore = new RecordingDemoAuditStore();
        var controller = CreateController(auditStore, CreateConfiguredSpinService());
        var request = new DemoSpinRequest(SlotSpecialRoundProfiles.GodsOfOlympusGameId,
            2, false, 0, null, 0, OlympusTumbleShowcase: true);

        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(
            await controller.Spin(request, default)).Value);
        var tumble = Assert.IsType<OlympusTumbleOutcome>(result.OlympusTumble);
        Assert.Equal(OlympusTumbleDemo.EngineVersion, result.ReelSetId);
        Assert.Equal(new long[] { 1, 1 }, tumble.Steps.Select(step => step.AmountPoints));
        Assert.Equal(2, result.Payout.TotalPoints);
        Assert.Null(result.SlotsCreditsBalance);

        var entry = Assert.IsType<DemoSlotAuditEntry>(auditStore.Entry);
        var capture = DemoSlotRoundRecording.Capture(entry);
        Assert.DoesNotContain(capture.Summary.Findings, finding => finding.Severity == "error");
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));

        var altered = DemoSlotRoundRecording.Capture(entry with
        {
            ResponseResult = result with { OlympusTumble = tumble with { TotalPoints = 3 } }
        });
        Assert.NotEmpty(SlotEvidenceReplayAudit.Inspect(altered.Summary, altered.PayloadJson));
    }

    [Fact]
    public void OlympusTumbleReplaysSavedVersionOneRandomDraws()
    {
        const string legacyVersion = "olympus-tumble-demo-v1";
        var template = CreateConfiguredSpinService().SpinOlympusTumbleDemo(2, showcase: true);
        var request = new DemoSpinRequest(template.GameId, 2, false, 0, null, 0, OlympusTumbleShowcase: false);
        // In v1, index 12 draws a "3" and eight of them pay 1.5 points rounded up.
        int[] drawIndexes =
        [
            12, 12, 12, 12, 12,
            12, 12, 12, 0, 24,
            0, 24, 36, 48, 60,
            0, 24, 36, 48, 60,
            36, 48, 60, 72, 73,
            74, 36, 48, 60, 72,
            0, 24, 36, 48, 60, 72, 73, 74
        ];
        IReadOnlyList<IReadOnlyList<string>> initialReels =
        [
            ["3", "3", "3", "3", "3"],
            ["3", "3", "3", "2", "4"],
            ["2", "4", "5", "6", "7"],
            ["2", "4", "5", "6", "7"],
            ["5", "6", "7", "8", "A"],
            ["J", "5", "6", "7", "8"]
        ];
        GridPosition[] cleared =
        [
            new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(0, 4),
            new(1, 0), new(1, 1), new(1, 2)
        ];
        var step = new OlympusTumbleStep([new("3", 2, cleared)], cleared,
            [["2", "4", "5", "6", "7"], ["8", "A", "J", "2", "4"], .. initialReels.Skip(2)],
            [[5, 5, 5, 5, 5], [3, 3, 3, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0]],
            2);
        var audit = Assert.IsType<SlotSpinEngineAudit>(template.EngineAudit) with
        {
            EngineVersion = legacyVersion,
            RawGeneratedReels = initialReels,
            ReelsAfterFreeGameFrequency = initialReels,
            OlympusTumble = new(false, drawIndexes)
        };
        var legacyResult = template with
        {
            ReelSetId = legacyVersion,
            PaytableId = legacyVersion,
            Reels = initialReels,
            Payout = new(2, []),
            OlympusTumble = new(initialReels, [step], 2),
            EngineAudit = audit
        };
        var now = DateTime.UtcNow;
        var capture = DemoSlotRoundRecording.Capture(new(request, legacyResult, legacyResult, now, now));
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));

        var tampered = JsonNode.Parse(capture.PayloadJson)!;
        tampered["engineAudit"]!["olympusTumble"]!["drawIndexes"]![0] = 0;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(capture.Summary, tampered.ToJsonString()), finding =>
            finding.Code == "slot-olympus-tumble-outcome-mismatch");
    }

    [Fact]
    public async Task OlympusTumbleRejectsLegacyBonusCarryAndInvalidWagers()
    {
        var auditStore = new RecordingDemoAuditStore();
        var controller = CreateController(auditStore, CreateConfiguredSpinService());
        var request = new DemoSpinRequest(SlotSpecialRoundProfiles.GodsOfOlympusGameId,
            2, false, 0, null, 0, OlympusTumbleShowcase: false);
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(request with { UseFreeSpin = true }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(request with { WagerPoints = 3 }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Spin(request with { GameId = "demo-contract" }, default));
        Assert.Null(auditStore.Entry);
    }

    private static SpinService CreateConfiguredSpinService()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var options = settings.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var definitions = new OptionsSlotsDefinitionProvider(Options.Create(options));
        var random = new CryptoRandomIndexSource();
        return new(definitions, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
    }

    [Fact]
    public async Task Spin_AcceptsHalfRandStepsAndRejectsQuarterRandWagers()
    {
        var controller = CreateController();

        var halfRand = await controller.Spin(new DemoSpinRequest(
            "demo-contract", 2, false, 0, null, 0), default);
        var oneRand = await controller.Spin(new DemoSpinRequest(
            "demo-contract", 4, false, 0, null, 0), default);
        var quarterStep = await controller.Spin(new DemoSpinRequest(
            "demo-contract", 3, false, 0, null, 0), default);

        Assert.IsType<OkObjectResult>(halfRand);
        Assert.IsType<OkObjectResult>(oneRand);
        Assert.IsType<BadRequestObjectResult>(quarterStep);
    }

    [Fact]
    public async Task Spin_DoesNotReturnAnUnauditedOutcomeWhenStorageFails()
    {
        var controller = CreateController(new RecordingDemoAuditStore { Fail = true });
        await Assert.ThrowsAsync<IOException>(() => controller.Spin(new DemoSpinRequest(
            "demo-contract", 2, false, 0, null, 0), default));
    }

    private sealed class RecordingDemoAuditStore : IDemoSlotAuditStore
    {
        public DemoSlotAuditEntry? Entry { get; private set; }
        public bool Fail { get; init; }
        public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("audit storage unavailable");
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class Definitions : ISlotsDefinitionProvider
    {
        private static readonly GameDefinition Game = new()
        {
            Id = "demo-contract",
            Layout = new GameLayoutDefinition { ReelCount = 1, VisibleRows = 1, PaylineCount = 1 },
            Symbols = new GameSymbolRules { SymbolSetId = "symbols", WildSymbolId = "ACE" },
            Matching = new GameMatchingRules { MinimumRunLength = 1 },
            Math = new GameMathDefinition
            {
                ReelSetId = "reels",
                PaytableId = "paytable",
                Targets = new GameMathTargets()
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = 25,
                MinimumWagerPoints = 2,
                MaximumWagerPoints = 2_000,
                WagerIncrementPoints = 2
            },
            Paylines = [[0]]
        };
        private static readonly SymbolSetDefinition Symbols = new()
        {
            Id = "symbols",
            Symbols = [new SymbolDefinition { Id = "2" }, new SymbolDefinition { Id = "ACE" }]
        };
        private static readonly ReelSetDefinition ReelSet = new()
        {
            Id = "reels",
            SymbolSetId = "symbols",
            Reels = [["2"]]
        };
        private static readonly PaytableDefinition Paytable = new()
        {
            Id = "paytable",
            SymbolSetId = "symbols"
        };

        public GameDefinition? GetGame(string id) => id == Game.Id ? Game : null;
        public SymbolSetDefinition? GetSymbolSet(string id) => id == Symbols.Id ? Symbols : null;
        public ReelSetDefinition? GetReelSet(string id) => id == ReelSet.Id ? ReelSet : null;
        public PaytableDefinition? GetPaytable(string id) => id == Paytable.Id ? Paytable : null;
    }

    private sealed class Reels : IReelGenerator
    {
        public ReelOutcome Generate(
            GameDefinition game,
            ReelSetDefinition reelSet,
            SymbolSetDefinition symbolSet) => new([0], [["2"]]);
    }

    private sealed class Evaluator : ICombinationEvaluator
    {
        public IReadOnlyList<PaylineEvaluation> Evaluate(
            IReadOnlyList<IReadOnlyList<string>> reels,
            GameDefinition game,
            SymbolSetDefinition symbolSet) => [];
    }

    private sealed class Payouts : IPayoutCalculator
    {
        public SpinPayout Calculate(
            IReadOnlyList<PaylineEvaluation> evaluations,
            GameDefinition game,
            PaytableDefinition paytable,
            long wagerPoints) => new(0, []);
    }

    private sealed class Random : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => 0;
    }
}
