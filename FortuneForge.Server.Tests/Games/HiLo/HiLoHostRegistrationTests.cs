using FortuneForge.Games.HiLo.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.HiLo;

public sealed class HiLoHostRegistrationTests
{
    [Fact]
    public void Reviewed_release_enables_the_game_in_Production_and_Development()
    {
        var serverRoot = Path.Combine(RepositoryRoot(), "FortuneForge.Server");
        var production = new ConfigurationBuilder().SetBasePath(serverRoot).AddJsonFile("appsettings.json").Build();
        var development = new ConfigurationBuilder().SetBasePath(serverRoot)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json").Build();

        Assert.True(production.GetValue("Features:HiLoEnabled", false));
        Assert.True(development.GetValue("Features:HiLoEnabled", false));
    }

    [Fact]
    public async Task Actual_host_registers_account_scoped_practice_and_rate_limited_endpoints_without_forcing_practice()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ApplicationName = typeof(FortuneForgeStartup).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(RepositoryRoot(), "FortuneForge.Server"),
        });
        // Register without starting a server or touching any production wallet.
        builder.Configuration["GoogleCloud:ProjectId"] = "demo-hi-lo-host-registration";
        builder.Configuration["Payments:Provider"] = "mock";
        builder.AddFortuneForge();
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();

        var options = app.Services.GetRequiredService<IOptions<HiLoServerOptions>>().Value;
        Assert.False(options.ForcePractice);
        Assert.Equal(RateLimitPolicies.SlotReads, options.ReadRateLimitPolicy);
        Assert.Equal(RateLimitPolicies.SlotSpins, options.WriteRateLimitPolicy);
        Assert.Equal(["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com"], options.AllowedOrigins);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(options.PracticeStorePath));
        Assert.Same(app.Services.GetRequiredService<DurablePracticeHiLoStore>(), app.Services.GetRequiredService<DurablePracticeHiLoStore>());

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/games/hi-lo", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(3, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Equal(
            endpoint.RoutePattern.RawText == "/api/games/hi-lo/session" ? RateLimitPolicies.SlotReads : RateLimitPolicies.SlotSpins,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.Server", "FortuneForge.Server.csproj"))) return directory.FullName;
        throw new InvalidOperationException("The test source repository could not be located.");
    }
}
