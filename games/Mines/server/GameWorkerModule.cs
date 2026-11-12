using FortuneForge.Games.Mines.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "mines";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddMinesServer(options =>
        options.AllowedOrigins = builder.Configuration.GetSection("GameWorker:AllowedOrigins").Get<string[]>() ?? []);

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapMines((context, _) => Task.FromResult<MinesAccount?>(
            resolveUserId(context) is { } userId ? new MinesAccount(userId) : null));
}
