using FortuneForge.Games.AndarBahar.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "andar-bahar";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddAndarBaharServer();

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapAndarBahar((context, _) => Task.FromResult<AndarBaharAccount?>(
            resolveUserId(context) is { } userId ? new AndarBaharAccount(userId, 0) : null));
}
