using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.DragonTiger;

public sealed class DragonTigerHostRegistrationTests
{
    [Fact]
    public void Reviewed_release_enables_the_game_in_Production_and_Development()
    {
        var serverRoot = Path.Combine(RepositoryRoot(), "FortuneForge.Server");
        var production = new ConfigurationBuilder().SetBasePath(serverRoot).AddJsonFile("appsettings.json").Build();
        var development = new ConfigurationBuilder().SetBasePath(serverRoot)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json").Build();

        Assert.True(production.GetValue("Features:DragonTigerEnabled", false));
        Assert.True(development.GetValue("Features:DragonTigerEnabled", false));
    }

    [Fact]
    public async Task Actual_host_registers_private_practice_store_and_rate_limited_endpoints_without_forcing_practice()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ApplicationName = typeof(FortuneForgeStartup).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(RepositoryRoot(), "FortuneForge.Server"),
        });
        // No server is started, and no wallet is resolved. Any later configuration
        // dependency still targets a disposable demo project, never live data.
        builder.Configuration["GoogleCloud:ProjectId"] = "demo-dragon-tiger-host-registration";
        builder.Configuration["Payments:Provider"] = "mock";
        builder.AddFortuneForge();
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();

        var options = app.Services.GetRequiredService<IOptions<DragonTigerServerOptions>>().Value;
        Assert.False(options.ForcePractice);
        Assert.Equal(RateLimitPolicies.SlotReads, options.ReadRateLimitPolicy);
        Assert.Equal(RateLimitPolicies.SlotSpins, options.WriteRateLimitPolicy);
        Assert.Same(app.Services.GetRequiredService<PracticeDragonTigerStore>(), app.Services.GetRequiredService<PracticeDragonTigerStore>());

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/games/dragon-tiger", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(4, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Equal(
            endpoint.RoutePattern.RawText == "/api/games/dragon-tiger/rounds" ? RateLimitPolicies.SlotSpins : RateLimitPolicies.SlotReads,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.Server", "FortuneForge.Server.csproj"))) return directory.FullName;
        throw new InvalidOperationException("The test source repository could not be located.");
    }
}
