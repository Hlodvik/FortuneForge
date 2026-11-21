using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Arcade.HorseFlight;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.HorseFlight;

public sealed class HorseFlightContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:HorseFlightEnabled"] = "true" })
            .Build();

        Assert.False(HorseFlightController.IsEnabled(disabled));
        Assert.True(HorseFlightController.IsEnabled(enabled));
    }

    [Fact]
    public void DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new HorseFlightController(new ConfigurationBuilder().Build());
        var responses = new ActionResult[]
        {
            controller.Status(),
            controller.Start(),
            controller.Complete(),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("horse-flight-disabled", Assert.IsType<HorseFlightErrorResponse>(unavailable.Value).Code);
        });
    }
}
