using FortuneForge.Server.Controllers;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class DemoSlotsContractTests
{
    [Fact]
    public void DemoController_IsPublicRateLimitedAndHasNoAccountOrStoreDependency()
    {
        var route = Assert.Single(typeof(DemoSlotsController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/slots/demo", route.Template);

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
        Assert.DoesNotContain(dependencies, dependency =>
            dependency.Namespace?.Contains("Accounts", StringComparison.Ordinal) == true ||
            dependency.Name.Contains("Store", StringComparison.Ordinal));
    }

    [Fact]
    public void Spin_ReturnsResultWithoutAnAccountBalanceOrStorageDependency()
    {
        var controller = new DemoSlotsController(
            new SpinService(
                new Definitions(),
                new Reels(),
                new Evaluator(),
                new Payouts(),
                new Random()),
            NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var response = controller.Spin(new DemoSpinRequest(
            "demo-contract",
            2,
            UseFreeSpin: false,
            FreeSpinsRemaining: 0,
            FreeSpinWagerPoints: null,
            EnergyBalance: 0));

        var result = Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Null(result.SlotsCreditsBalance);
        Assert.Equal(2, result.WagerPoints);
        Assert.Equal(25, result.PointValueInCents);
        Assert.False(result.IsFreeSpin);
    }

    [Fact]
    public void Spin_AcceptsHalfRandStepsAndRejectsQuarterRandWagers()
    {
        var controller = new DemoSlotsController(
            new SpinService(
                new Definitions(),
                new Reels(),
                new Evaluator(),
                new Payouts(),
                new Random()),
            NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var halfRand = controller.Spin(new DemoSpinRequest(
            "demo-contract", 2, false, 0, null, 0));
        var oneRand = controller.Spin(new DemoSpinRequest(
            "demo-contract", 4, false, 0, null, 0));
        var quarterStep = controller.Spin(new DemoSpinRequest(
            "demo-contract", 3, false, 0, null, 0));

        Assert.IsType<OkObjectResult>(halfRand);
        Assert.IsType<OkObjectResult>(oneRand);
        Assert.IsType<BadRequestObjectResult>(quarterStep);
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
