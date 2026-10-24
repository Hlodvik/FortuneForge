using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.AndarBahar;

public sealed class AndarBaharHostRegistrationTests
{
    [Fact]
    public void Integrated_game_is_enabled_in_Production_and_Development()
    {
        var serverRoot = Path.Combine(RepositoryRoot(), "FortuneForge.Server");
        var production = new ConfigurationBuilder().SetBasePath(serverRoot).AddJsonFile("appsettings.json").Build();
        var development = new ConfigurationBuilder().SetBasePath(serverRoot)
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json").Build();
        Assert.True(production.GetValue("Features:AndarBaharEnabled", false));
        Assert.True(development.GetValue("Features:AndarBaharEnabled", false));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Host_registers_authenticated_account_scoped_practice_and_rate_limited_rounds(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(FortuneForgeStartup).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(RepositoryRoot(), "FortuneForge.Server"),
        });
        // Inspect registration only: do not start hosted workers or access any wallet.
        builder.Configuration["GoogleCloud:ProjectId"] = "demo-andar-bahar-host-registration";
        builder.Configuration["Payments:Provider"] = "mock";
        builder.AddFortuneForge();
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();

        var options = app.Services.GetRequiredService<IOptions<AndarBaharServerOptions>>().Value;
        Assert.False(options.ForcePractice);
        Assert.True(options.AllowPracticeRequests);
        Assert.Equal(RateLimitPolicies.SlotReads, options.ReadRateLimitPolicy);
        Assert.Equal(RateLimitPolicies.SlotSpins, options.WriteRateLimitPolicy);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(options.PracticeStatePath));
        Assert.Same(app.Services.GetRequiredService<PracticeAndarBaharStore>(), app.Services.GetRequiredService<PracticeAndarBaharStore>());

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/games/andar-bahar", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(4, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Equal(
            endpoint.RoutePattern.RawText == "/api/games/andar-bahar/rounds" ? RateLimitPolicies.SlotSpins : RateLimitPolicies.SlotReads,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.Server", "FortuneForge.Server.csproj"))) return directory.FullName;
        throw new InvalidOperationException("The test source repository could not be located.");
    }
}
