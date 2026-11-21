using FortuneForge.Games.Hosting;
using FortuneForge.Games.DesertTreasures;
using FortuneForge.Games.DesertTreasures.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = DesertTreasuresEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<DesertTreasuresRoundStore>();
        builder.Services.AddSingleton<DesertTreasuresRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapDesertTreasuresPaidRounds(resolveUserId.Invoke);
        var outcomes = new DesertTreasuresPaidSlotOutcomes(app.Services.GetRequiredService<IGameRandomSource>());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
