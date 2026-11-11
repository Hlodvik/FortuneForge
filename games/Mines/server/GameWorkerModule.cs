using FortuneForge.Games.Mines.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "mines";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddMinesServer(options =>
        options.AllowedOrigins = builder.Configuration.GetSection("GameWorker:AllowedOrigins").Get<string[]>() ?? []);

    internal static void Map(WebApplication app, DemoGameAccount? account) =>
        app.MapMines((_, _) => Task.FromResult<MinesAccount?>(
            account is null ? null : new MinesAccount(account.UserId)));
}
