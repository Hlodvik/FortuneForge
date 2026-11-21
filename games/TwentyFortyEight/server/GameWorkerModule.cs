using FortuneForge.Games.Hosting;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Games.TwentyFortyEight.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = TwentyFortyEightSession.GameId;

    internal static void Configure(WebApplicationBuilder builder) =>
        builder.Services.AddSingleton<TwentyFortyEightFirestoreSessions>();

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapTwentyFortyEight(resolveUserId.Invoke, GameWorkerAuthentication.GetVerifiedBodySha256);

        // Existing host revisions keep using these stateless compute actions during rollout.
        app.MapGameAction(TwentyFortyEightSession.StartComputeRoute,
            (TwentyFortyEightComputeStart request) => TwentyFortyEightEngine.Start(request.Seed));
        app.MapGameAction(TwentyFortyEightSession.MoveComputeRoute,
            (TwentyFortyEightComputeMove request) => request.Apply());
    }
}
