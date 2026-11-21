using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Solitaire;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = SolitaireFreeRunIdentity.StandaloneGameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(provider => new FirestoreSolitaireFreeRunService(
            provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>(),
            provider.GetRequiredService<TimeProvider>()));
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount)
    {
        app.MapFreeSolitaireRuns(resolveAccount.Invoke);
        // Preserve compute contracts while old host configurations are being replaced.
        app.MapGameAction(SolitaireBoardCompute.StartRoute, (SolitaireBoardStartRequest request) =>
            SolitaireBoardCompute.Snapshot(SolitaireEngine.CreateGame(request.Seed ?? FreeSolitaireSeeds.Create(), request.DrawCount)));
        app.MapGameAction(SolitaireBoardCompute.ApplyRoute, (SolitaireBoardApplyRequest request) =>
        {
            try { return SolitaireBoardCompute.Snapshot(SolitaireEngine.Apply(request.State, request.Command)); }
            catch (SolitaireIllegalMoveException exception) { throw new ArgumentException(exception.Message, exception); }
        });
        app.MapGameAction<SolitaireBoardReplayRequest, SolitaireBoardReplayResult>(
            SolitaireBoardCompute.ReplayRoute, SolitaireBoardCompute.Replay);
    }

}
