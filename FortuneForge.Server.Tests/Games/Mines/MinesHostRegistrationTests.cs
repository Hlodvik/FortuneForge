using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Mines;

public sealed class MinesHostRegistrationTests
{
    [Fact]
    public void Integrated_release_enables_api_but_never_paid_play_in_checked_in_settings()
    {
        var serverRoot = Path.Combine(RepositoryRoot(), "FortuneForge.Server");
        foreach (var development in new[] { false, true })
        {
            var builder = new ConfigurationBuilder().SetBasePath(serverRoot).AddJsonFile("appsettings.json");
            if (development) builder.AddJsonFile("appsettings.Development.json");
            var configuration = builder.Build();
            Assert.True(configuration.GetValue("Features:MinesEnabled", false));
            Assert.False(configuration.GetValue("Features:MinesPaidEnabled", false));
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Host_registers_authenticated_rate_limited_game_without_any_practice_entry(string environment)
    {
        var builder = CreateBuilder(environment);
        builder.Configuration["Mines:AllowedOrigins:0"] = "https://approved.example.test";
        builder.AddFortuneForge();
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();
        var options = app.Services.GetRequiredService<IOptions<MinesServerOptions>>().Value;
        Assert.False(options.ForcePractice);
        Assert.False(options.AllowPracticeRequests);
        Assert.Equal(RateLimitPolicies.SlotReads, options.ReadRateLimitPolicy);
        Assert.Equal(RateLimitPolicies.SlotSpins, options.WriteRateLimitPolicy);
        Assert.Contains("https://fortuneforgegame.web.app", options.AllowedOrigins);
        Assert.Contains("https://fortuneforgegame.firebaseapp.com", options.AllowedOrigins);
        Assert.Contains("https://approved.example.test", options.AllowedOrigins);
        Assert.Empty(options.PracticeOrigins);
        Assert.Same(app.Services.GetRequiredService<FileMinesPracticeStore>(), app.Services.GetRequiredService<FileMinesPracticeStore>());
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/games/mines", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(4, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Equal(
            endpoint.RoutePattern.RawText == "/api/games/mines/session" ? RateLimitPolicies.SlotReads : RateLimitPolicies.SlotSpins,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    internal static WebApplicationBuilder CreateBuilder(string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(FortuneForgeStartup).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(RepositoryRoot(), "FortuneForge.Server"),
        });
        builder.Configuration["GoogleCloud:ProjectId"] = "demo-mines-host-" + Guid.NewGuid().ToString("N");
        builder.Configuration["Payments:Provider"] = "mock";
        return builder;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.Server", "FortuneForge.Server.csproj"))) return directory.FullName;
        throw new InvalidOperationException("The test source repository could not be located.");
    }
}
