using FortuneForge.Games.AndarBahar.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "andar-bahar";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddAndarBaharServer();

    internal static void Map(WebApplication app, DemoGameAccount? account) =>
        app.MapAndarBahar((_, _) => Task.FromResult<AndarBaharAccount?>(
            account is null ? null : new AndarBaharAccount(account.UserId, 0)));
}
