using FortuneForge.Games.Hosting;
using FortuneForge.Games.RoyalDraw;
using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = RoyalDrawEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<RoyalDrawRoundStore>();
        builder.Services.AddSingleton<RoyalDrawRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapRoyalDrawPaidRounds(resolveUserId.Invoke);
        var outcomes = new RoyalDrawPaidSlotOutcomes(app.Services.GetRequiredService<IGameRandomSource>());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
