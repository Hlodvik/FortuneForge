using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = ArcaneArchivesEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ArcaneFeatureStore>();
        builder.Services.AddSingleton<ArcaneRoundStore>();
        builder.Services.AddSingleton<ArcaneRoundService>();
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.TryAddSingleton(TimeProvider.System);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapArcaneFeatures((context, _) => Task.FromResult(resolveUserId(context)));
        app.MapArcanePaidRounds(resolveUserId.Invoke);
        var outcomes = new ArcanePaidSlotOutcomes(new CryptographicSlotRandom());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
