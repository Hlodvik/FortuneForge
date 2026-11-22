using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ClassicSlot.Server;

public sealed class GameBackendPackage : IGameBackendPackage
{
    public string GameId => SelectedEngine.GameId;

    public WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = WebApplication.CreateBuilder(options);
        configure(builder);
        var app = builder.Build();
        app.MapGet("/healthz", () => Results.Ok(new { gameId = GameId, engineVersion = SelectedEngine.EngineVersion }));
        app.UseGameWorkerAuthentication(GameId);
        var outcomes = new ClassicPaidSlotOutcomes(new CryptographicSlotRandom());
        app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(
            PaidSlotOutcomeProtocol.Route, outcomes.Resolve, accountSelector: request => request.PlayerId);
        return app;
    }
}
