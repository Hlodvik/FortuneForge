using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.GameCompute.Server;

public sealed class GameBackendPackage : IGameBackendPackage
{
    public string GameId => GameComputeModule.Descriptor.Id;

    public WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = WebApplication.CreateBuilder(options);
        configure(builder);
        var descriptor = GameComputeModule.Descriptor;
        var app = builder.Build();
        app.MapGet("/healthz", () => Results.Ok(new { gameId = descriptor.Id, engineVersion = descriptor.PackageVersion }));
        app.UseGameWorkerAuthentication(descriptor.Id);
        GameComputeModule.Map(app);
        return app;
    }
}
