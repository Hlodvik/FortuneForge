using FortuneForge.Games.Hosting;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = JadeDragonEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<JadeRoundStore>();
        builder.Services.AddSingleton<JadeRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapJadePaidRounds(resolveUserId.Invoke);
        var outcomes = new JadePaidSlotOutcomes();
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
