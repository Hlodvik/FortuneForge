using FortuneForge.Games.HiLo.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "hi-lo";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddHiLoServer(options =>
        options.AllowedOrigins = builder.Configuration.GetSection("GameWorker:AllowedOrigins").Get<string[]>() ?? []);

    internal static void Map(WebApplication app, DemoGameAccount? account) =>
        app.MapHiLo((_, _) => Task.FromResult<HiLoAccount?>(
            account is null ? null : new HiLoAccount(account.UserId)));
}
