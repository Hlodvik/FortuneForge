using FortuneForge.Games.Hosting;
using FortuneForge.Games.PhantomManor;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = PhantomManorEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<PhantomManorRoundStore>();
        builder.Services.AddSingleton<PhantomManorRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapPhantomManorPaidRounds(resolveUserId.Invoke);
        var outcomes = new PhantomManorPaidSlotOutcomes(app.Services.GetRequiredService<IGameRandomSource>());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
