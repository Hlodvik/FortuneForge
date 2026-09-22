using FortuneForge.Server.Arcade.HorseFlight;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new HorseFlightController(null!, null!, new ConfigurationBuilder().Build(), NullLogger<HorseFlightController>.Instance);
        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start("horse-flight-start-0001", CancellationToken.None),
            await controller.Complete(new string('a', 64), new CompleteHorseFlightRunRequest(1, []), "horse-flight-complete-01", CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("horse-flight-disabled", Assert.IsType<HorseFlightErrorResponse>(unavailable.Value).Code);
        });
    }
}
