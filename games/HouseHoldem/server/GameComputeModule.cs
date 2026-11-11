using FortuneForge.Games.Abstractions;
using FortuneForge.Games.HouseHoldem;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor { get; } = new(
        "house-holdem", "House Hold’em", GameCategory.Card, "0.1.0", "/cards/house-holdem",
        "/api/games/house-holdem", GameCapability.Credits | GameCapability.History);

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/rounds/start", (StartRequest request) =>
            Snapshot(HouseHoldemRules.StartRound(HouseHoldemRules.CreateShuffledDeck(), request.AnteCents, request.TripsCents)));
        app.MapComputeAction("/internal/v1/rounds/act", (ActRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.Round);
            HouseHoldemRules.ApplyAction(request.Round, request.Action);
            return Snapshot(request.Round);
        }, isRuleViolation: error => error is InvalidOperationException);
        app.MapComputeAction("/internal/v1/rounds/advance", (HouseHoldemRound round) =>
        {
            HouseHoldemRules.Advance(round);
            return Snapshot(round);
        }, isRuleViolation: error => error is InvalidOperationException);
    }

    private static RoundSnapshot Snapshot(HouseHoldemRound round) => new(round, HouseHoldemRules.ToPlayerView(round));

    private sealed record StartRequest(long AnteCents, long TripsCents = 0);
    private sealed record ActRequest(HouseHoldemRound Round, string Action);
    private sealed record RoundSnapshot(HouseHoldemRound PrivateRound, HouseHoldemPlayerView PlayerView);
}
