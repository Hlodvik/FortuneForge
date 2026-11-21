using FortuneForge.Games.Hosting;
using FortuneForge.Games.SamuraiFortune;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = SamuraiFortuneEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, CryptographicSlotRandom>();
        builder.Services.AddSingleton<SamuraiFortuneRoundStore>();
        builder.Services.AddSingleton<SamuraiFortuneRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapSamuraiFortunePaidRounds(resolveUserId.Invoke);
        var outcomes = new SamuraiFortunePaidSlotOutcomes(app.Services.GetRequiredService<IGameRandomSource>());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
            outcomes.Resolve, accountSelector: request => request.PlayerId);
    }
}
