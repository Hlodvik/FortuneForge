using FortuneForge.Games.Hosting;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = NeonArcadeSlotDefinitions.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<NeonPrimaryRoundStore>();
        builder.Services.AddSingleton<NeonPrimaryRoundService>();
        builder.Services.AddSingleton<NeonCarRespinStore>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapNeonPrimaryRounds(resolveUserId.Invoke);
        app.MapNeonCarRespin((context, _) => Task.FromResult(resolveUserId(context)));
        var outcomes = new NeonPaidSlotOutcomes(app.Services.GetRequiredService<IGameRandomSource>());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
