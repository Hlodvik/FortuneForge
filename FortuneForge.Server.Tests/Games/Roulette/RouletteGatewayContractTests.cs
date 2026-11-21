using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.GameWorkers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Roulette;

public sealed class RouletteGatewayContractTests
{
    [Fact]
    public async Task History_route_selects_the_roulette_worker_only_when_forwarding_is_enabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(RouletteController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        var history = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(), endpoint =>
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action &&
            action.ControllerTypeInfo.AsType() == typeof(RouletteController) &&
            action.ActionName == nameof(RouletteController.History));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/games/roulette/rounds/{Guid.NewGuid():D}/history/1";
        context.SetEndpoint(history);

        var configuration = new ConfigurationManager();
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, configuration));

        configuration["GameWorkers:roulette:ForwardPublicRoutes"] = "true";
        var selected = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(context, configuration));
        Assert.Equal("roulette", selected.Id);
        Assert.False(selected.AllowLocalFallback);
    }

    [Theory]
    [InlineData(true, "roulette-worker-unavailable")]
    [InlineData(false, "roulette-disabled")]
    public void Local_actions_fail_closed_when_the_worker_is_not_selected(bool enabled, string code)
    {
        var configuration = new ConfigurationManager { ["Features:RouletteEnabled"] = enabled.ToString() };
        var controller = new RouletteController(configuration);

        Assert.All(new[] { controller.Status(), controller.Start(), controller.History(Guid.NewGuid(), 1) }, result =>
        {
            var response = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
            Assert.Equal(code, Assert.IsType<RouletteErrorResponse>(response.Value).Code);
        });
    }
}
