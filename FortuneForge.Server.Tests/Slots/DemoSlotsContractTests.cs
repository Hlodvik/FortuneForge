using FortuneForge.Server.Controllers;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class DemoSlotsContractTests
{
    [Fact]
    public void DemoController_IsPublicRateLimitedAndUsesTheSharedEngineWithAnAuditStore()
    {
        var route = Assert.Single(typeof(DemoSlotsController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/slots/demo", route.Template);

        var status = typeof(DemoSlotsController).GetMethod(nameof(DemoSlotsController.Status));
        Assert.NotNull(status);
        Assert.Equal("status", Assert.Single(status
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
            .Cast<HttpGetAttribute>()).Template);

        var spin = typeof(DemoSlotsController).GetMethod(nameof(DemoSlotsController.Spin));
        Assert.NotNull(spin);
        Assert.Equal("spins", Assert.Single(spin
            .GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
            .Cast<HttpPostAttribute>()).Template);
        Assert.Equal("slot-spins", Assert.Single(spin
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()).PolicyName);

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

    private static DemoSlotsController CreateController(IDemoSlotAuditStore? auditStore = null) => new(
            new SpinService(
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
