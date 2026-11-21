using FortuneForge.Games.Hosting;
using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Cards.Solitaire;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = CompetitiveSolitaireEndpoints.GameId;
    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddCompetitiveSolitaireBackend(builder.Configuration);
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount)
    {
        app.MapCompetitiveSolitaire(resolveAccount.Invoke);
        app.MapGameAction(SolitaireBoardCompute.StartRoute, (SolitaireBoardStartRequest request) =>
            SolitaireBoardCompute.Snapshot(SolitaireEngine.CreateGame(request.Seed ?? CreateSeed(), request.DrawCount)));
        app.MapGameAction(SolitaireBoardCompute.ApplyRoute, (SolitaireBoardApplyRequest request) =>
        {
            try { return SolitaireBoardCompute.Snapshot(SolitaireEngine.Apply(request.State, request.Command)); }
            catch (SolitaireIllegalMoveException exception) { throw new ArgumentException(exception.Message, exception); }
        });
        app.MapGameAction<SolitaireBoardBatchRequest, SolitaireBoardBatchResult>(
            SolitaireBoardCompute.ApplyBatchesRoute, SolitaireBoardCompute.ApplyBatches);
    }
    private static uint CreateSeed() => BitConverter.ToUInt32(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(uint)));
}
