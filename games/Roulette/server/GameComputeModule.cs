using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Roulette;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => RouletteModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction("/internal/v1/rounds/spin", (SpinRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.Bets);
            var round = RouletteEngine.OpenRound(request.RoundId);
            foreach (var bet in request.Bets)
            {
                ArgumentNullException.ThrowIfNull(bet);
                round = RouletteEngine.PlaceBet(round, new RouletteBet(
                    bet.PlayerId, bet.Kind, bet.Stake, bet.Number, [.. bet.Numbers ?? []]));
            }
            return RouletteEngine.Spin(round, new RoulettePocket(RandomNumberGenerator.GetInt32(37)));
        }, isRuleViolation: exception => exception is RouletteRuleException);

    private sealed record SpinRequest(string RoundId, IReadOnlyList<BetInput> Bets);
    private sealed record BetInput(string PlayerId, RouletteBetKind Kind, decimal Stake, int? Number = null,
        IReadOnlyList<int>? Numbers = null);
}
