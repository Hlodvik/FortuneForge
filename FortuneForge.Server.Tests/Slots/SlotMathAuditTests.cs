using System.Text.Json;
using FortuneForge.Server.Controllers;
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
using Xunit.Abstractions;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotMathAuditTests(ITestOutputHelper output)
{
    private const int PaidSpinsPerGame = 5_000;
    private const long WagerPoints = 100;

    [Fact]
    [Trait("Category", "MathAudit")]
    public void FeaturedGames_ProduceFiniteRtpAndDistinctVolatilityMeasurements()
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(ReadOptions()));
        var measurements = FeaturedGameIds
            .Select((gameId, index) => Simulate(provider, gameId, 9_001 + index))
            .ToArray();

        Assert.Equal(FeaturedGameIds.Length, measurements.Length);
        Assert.All(measurements, measurement =>
        {
            Assert.True(double.IsFinite(measurement.Rtp));
            Assert.True(double.IsFinite(measurement.Volatility));
            Assert.InRange(measurement.Rtp, 0d, 10d);
            Assert.True(measurement.Volatility > 0d);
        });
        Assert.True(measurements.Select(measurement => measurement.Volatility).Distinct().Count() > 1);

        foreach (var measurement in measurements)
        {
            output.WriteLine(
                $"{measurement.GameId}: RTP {measurement.Rtp:P2}; hit rate {measurement.HitRate:P2}; " +
                $"volatility {measurement.Volatility:F3}; max cycle {measurement.MaxCycleReturn:F2}x; " +
                $"spins {measurement.TotalSpins:N0}.");
        }
    }

    private MathAuditMeasurement Simulate(
        OptionsSlotsDefinitionProvider provider,
        string gameId,
        int seed)
    {
        var random = new SeededRandomIndexSource(seed);
        var controller = new DemoSlotsController(
            new SpinService(
                provider,
                new CryptoReelGenerator(random),
                new CombinationEvaluator(),
                new PayoutCalculator(),
                random),
            NullLogger<DemoSlotsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var state = new AuditState([], 0, null, null);
        var cycleReturns = new double[PaidSpinsPerGame];
        var totalSpins = 0;

        for (var paidSpin = 0; paidSpin < PaidSpinsPerGame; paidSpin++)
        {
            var cyclePayout = 0L;
            var response = Spin(controller, gameId, state, useFreeSpin: false);
            state = UpdateState(response);
            cyclePayout = checked(cyclePayout + response.Payout.TotalPoints);
            totalSpins++;

            var freeSpinSafetyLimit = 1_000;
            while (state.FreeSpinsRemaining > 0)
            {
                Assert.True(freeSpinSafetyLimit-- > 0, $"{gameId} generated an unbounded free-spin cycle.");
                response = Spin(controller, gameId, state, useFreeSpin: true);
                state = UpdateState(response);
                cyclePayout = checked(cyclePayout + response.Payout.TotalPoints);
                totalSpins++;
            }

            cycleReturns[paidSpin] = cyclePayout / (double)WagerPoints;
        }

        var averageReturn = cycleReturns.Average();
        var variance = cycleReturns.Select(value => Math.Pow(value - averageReturn, 2)).Average();
        return new MathAuditMeasurement(
            gameId,
            averageReturn,
            cycleReturns.Count(value => value > 0d) / (double)cycleReturns.Length,
            Math.Sqrt(variance),
            cycleReturns.Max(),
            totalSpins);
    }

    private static SpinResult Spin(
        DemoSlotsController controller,
        string gameId,
        AuditState state,
        bool useFreeSpin)
    {
        var wager = useFreeSpin ? state.FreeSpinWagerPoints ?? WagerPoints : WagerPoints;
        var action = controller.Spin(new DemoSpinRequest(
            gameId,
            wager,
            useFreeSpin,
            state.FreeSpinsRemaining,
            state.FreeSpinWagerPoints,
            state.EnergyBalance,
            state.Collections,
            state.FreeSpinFeatureMode));
        return Assert.IsType<SpinResult>(Assert.IsType<OkObjectResult>(action).Value);
    }

    private static AuditState UpdateState(SpinResult result) => new(
        result.SealCollections,
        result.EnergyBalance,
        result.FreeSpinWagerPoints,
        result.FreeSpinFeatureMode,
        result.FreeSpinsRemaining);

    private static SlotsOptions ReadOptions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Slots")
                   .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Slot options are missing from appsettings.json.");
    }

    private sealed class SeededRandomIndexSource(int seed) : IRandomIndexSource
    {
        private readonly Random _random = new(seed);

        public int Next(int maximumExclusive) => _random.Next(maximumExclusive);
    }

    private sealed record AuditState(
        IReadOnlyList<SlotSealCollection> Collections,
        long EnergyBalance,
        long? FreeSpinWagerPoints,
        string? FreeSpinFeatureMode,
        int FreeSpinsRemaining = 0);

    private sealed record MathAuditMeasurement(
        string GameId,
        double Rtp,
        double HitRate,
        double Volatility,
        double MaxCycleReturn,
        int TotalSpins);

    private static readonly string[] FeaturedGameIds =
    [
        SlotSpecialRoundProfiles.CosmicFortuneGameId,
        SlotSpecialRoundProfiles.HighNoonFortuneGameId,
        SlotSpecialRoundProfiles.GodsOfOlympusGameId,
        SlotSpecialRoundProfiles.PiratesFortuneGameId,
        SlotSpecialRoundProfiles.RoyalDrawGameId,
        SlotSpecialRoundProfiles.SamuraiFortuneGameId,
        SlotSpecialRoundProfiles.RobotRevolutionGameId,
        SlotSpecialRoundProfiles.PhantomManorGameId,
        SlotSpecialRoundProfiles.OceanOdysseyGameId,
        SlotSpecialRoundProfiles.DragonHoardGameId,
        SlotSpecialRoundProfiles.JungleJackpotGameId,
        SlotSpecialRoundProfiles.CandyCarnivalGameId,
        SlotSpecialRoundProfiles.DesertTreasuresGameId,
        SlotSpecialRoundProfiles.NeonNightsGameId,
        SlotSpecialRoundProfiles.NordicLegendsGameId,
        SlotSpecialRoundProfiles.ReelRichesGameId,
        SlotSpecialRoundProfiles.ArcaneArchivesGameId,
        SlotSpecialRoundProfiles.DinoDominionGameId
    ];
}
