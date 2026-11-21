using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "hi-lo";

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddHiLoServer(options =>
            options.AllowedOrigins = builder.Configuration.GetSection("GameWorker:AllowedOrigins").Get<string[]>() ?? []);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapHiLo((context, _) => Task.FromResult<HiLoAccount?>(
            resolveUserId(context) is { } userId ? new HiLoAccount(userId) : null));
}
