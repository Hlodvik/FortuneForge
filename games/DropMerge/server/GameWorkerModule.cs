using FortuneForge.Games.DropMerge;
using FortuneForge.Games.DropMerge.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = DropMergeSession.GameId;

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddSingleton<DropMergeFirestoreSessions>();

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapDropMerge(resolveUserId.Invoke, GameWorkerAuthentication.GetVerifiedBodySha256);

        // The existing gateway can still use the legacy compute contract for its old sessions.
        app.MapGameAction(DropMergeSession.StartComputeRoute, (DropMergeComputeStart request) => DropMergeEngine.Start(request.Seed));
        app.MapGameAction(DropMergeSession.DropComputeRoute, (DropMergeComputeDrop request) => request.Apply());
    }
}
