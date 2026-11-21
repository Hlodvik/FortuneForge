using FortuneForge.Games.Hosting;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Cards.HouseHoldem;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = HouseHoldemBackend.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount)
    {
        app.MapHouseHoldem(resolveAccount.Invoke);
        // Keep the signed compute URLs available during rolling gateway upgrades.
        app.MapGameAction("/internal/v1/rounds/start", (StartRequest request) =>
            Snapshot(HouseHoldemRules.StartRound(request.Deck, request.AnteCents, request.TripsCents)));
        app.MapGameAction("/internal/v1/rounds/act", (ActRequest request) =>
            Transition(request.Round, () => HouseHoldemRules.ApplyAction(request.Round, request.Action)));
        app.MapGameAction("/internal/v1/rounds/advance", (HouseHoldemRound round) =>
            Transition(round, () => HouseHoldemRules.Advance(round)));
    }

    private static RoundSnapshot Transition(HouseHoldemRound round, Action apply)
    {
        ArgumentNullException.ThrowIfNull(round);
        try { apply(); }
        catch (InvalidOperationException error) { throw new ArgumentException(error.Message, error); }
        return Snapshot(round);
    }

    private static RoundSnapshot Snapshot(HouseHoldemRound round) => new(round, HouseHoldemRules.ToPlayerView(round));
    private sealed record StartRequest(IReadOnlyList<string> Deck, long AnteCents, long TripsCents);
    private sealed record ActRequest(HouseHoldemRound Round, string Action);
    private sealed record RoundSnapshot(HouseHoldemRound PrivateRound, HouseHoldemPlayerView PlayerView);
}
