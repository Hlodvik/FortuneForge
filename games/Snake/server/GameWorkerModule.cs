using FortuneForge.Games.Snake.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "snake";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<FirestoreSnakeGameService>();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapSnake(resolveUserId.Invoke, GameWorkerAuthentication.GetVerifiedBodySha256);
}
