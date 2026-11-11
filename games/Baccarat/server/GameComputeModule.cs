using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => BaccaratModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction("/internal/v1/rounds/resolve", (BaccaratComputeRequest request) =>
        {
            var round = PuntoBancoRoundDealer.Deal(request.OrderedShoe.Select(CardCode.Parse).ToArray());
            return new
            {
                Round = round,
                MainBet = PuntoBancoPaytable.Settle(request.BetSide, request.Stake, round),
                PlayerPair = PuntoBancoPaytable.SettlePair(request.PlayerPairStake, round.PlayerHand),
                BankerPair = PuntoBancoPaytable.SettlePair(request.BankerPairStake, round.BankerHand),
            };
        });

    private sealed record BaccaratComputeRequest(IReadOnlyList<string> OrderedShoe, BaccaratBetSide BetSide,
        decimal Stake, decimal PlayerPairStake = 0m, decimal BankerPairStake = 0m);
}
