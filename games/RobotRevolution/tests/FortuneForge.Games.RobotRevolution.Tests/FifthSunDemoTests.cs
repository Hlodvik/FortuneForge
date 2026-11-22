using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class FifthSunDemoTests
{
    [Theory]
    [InlineData(FifthSunTotalityDefinitions.SolarMode, FifthSunTotalityDefinitions.SolarSymbol)]
    [InlineData(FifthSunTotalityDefinitions.LunarMode, FifthSunTotalityDefinitions.LunarSymbol)]
    public void OneCentreSymbolStartsAndCompletesTheSameFivePaidDemoRounds(string mode, string symbol)
    {
        var centre = RobotRevolutionEngine.CreateReelSet().Reels[2];
        var trigger = FifthSunDemo.Spin(12, random: new CentreStopRandom(centre.IndexOf(symbol)));
        Assert.Equal(new FifthSunPaidMode(mode, 5, 12), trigger.PaidMode);
        Assert.Equal(300, trigger.ChargedWagerCents);
        Assert.Null(trigger.SlotsCreditsBalance);
        var carry = trigger.PaidMode;
        for (var remaining = 5; remaining > 0; remaining--)
        {
            var response = FifthSunDemo.Spin(12, JsonSerializer.SerializeToElement(carry, RoundAuditCapture.Json),
                new FifthSunTests.StableRandom(remaining));
            Assert.False(response.IsFreeSpin);
            Assert.Equal(300, response.ChargedWagerCents);
            Assert.Equal(0, response.FreeSpinsRemaining);
            Assert.Equal(0, response.FreeSpinsAwarded);
            Assert.Null(response.SlotsCreditsBalance);
            Assert.Equal(3, response.SpinFeature!.Alignments!.Count);
            Assert.Equal(mode == FifthSunTotalityDefinitions.SolarMode ? "solar" : "lunar", response.SpinFeature.EclipseMode);
            Assert.All(response.Reels, reel => Assert.Equal(4, reel.Count));
            carry = response.PaidMode;
            Assert.Equal(remaining - 1, carry?.Remaining ?? 0);
        }
        Assert.Null(carry);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"foreign\",\"remaining\":5,\"wagerPoints\":12}")]
    [InlineData("{\"mode\":\"solar-eclipse\",\"remaining\":6,\"wagerPoints\":12}")]
    [InlineData("{\"mode\":\"solar-eclipse\",\"remaining\":5,\"wagerPoints\":13}")]
    [InlineData("{\"mode\":\"solar-eclipse\",\"remaining\":5,\"wagerPoints\":12,\"extra\":true}")]
    public void MalformedDemoCarryUsesNativeValidationAndProducesAnArgumentError(string json)
    {
        using var state = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => FifthSunDemo.Spin(12, state.RootElement, new FifthSunTests.StableRandom(1)));
    }

    [Fact]
    public void DemoModeCannotRaiseItsSavedWager()
    {
        var carry = JsonSerializer.SerializeToElement(new FifthSunPaidMode(FifthSunTotalityDefinitions.SolarMode, 5, 12), RoundAuditCapture.Json);
        Assert.Throws<ArgumentException>(() => FifthSunDemo.Spin(300, carry, new FifthSunTests.StableRandom(1)));
    }

    private sealed class CentreStopRandom(int centreStop) : IGameRandomSource
    {
        private int call;
        public int Next(int maximum) => ++call == 3 ? centreStop % maximum : 0;
    }
}
