using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    private const int ShoeDeckCount = 8;

    internal static GameDescriptor Descriptor => CasinoWarModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/rounds/start", (StartRequest request) =>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(request.TieStake);
            var shoe = Enumerable.Repeat(StandardDeck.Create(), ShoeDeckCount).SelectMany(deck => deck).ToArray();
            RandomNumberGenerator.Shuffle(shoe.AsSpan());
            var round = CasinoWarRoundEngine.Start(shoe, request.PrimaryStake);
            var tieSettlement = request.TieStake > 0
                ? CasinoWarTieBetPaytable.Settle(round.Opening, request.TieStake)
                : null;
            return new { Round = round, TieSettlement = tieSettlement, PrivateShoe = shoe };
        });

        app.MapComputeAction("/internal/v1/rounds/decide", (DecisionRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateShoe);
            var standardDeck = StandardDeck.Create();
            if (request.PrivateShoe.Count != ShoeDeckCount * standardDeck.Count ||
                request.PrivateShoe.GroupBy(card => card).Any(group =>
                    group.Count() != ShoeDeckCount || !standardDeck.Contains(group.Key)))
            {
                throw new ArgumentException("A complete private Casino War shoe is required.");
            }

            var round = CasinoWarRoundEngine.Start(request.PrivateShoe, request.PrimaryStake);
            if (round.Phase != CasinoWarRoundPhase.AwaitingTieDecision)
                throw new ArgumentException("A Casino War decision requires an opening tie.");
            return CasinoWarRoundEngine.Decide(round, request.Decision);
        });
    }

    private sealed record StartRequest(decimal PrimaryStake, decimal TieStake = 0);
    private sealed record DecisionRequest(IReadOnlyList<PlayingCard> PrivateShoe, decimal PrimaryStake,
        CasinoWarTieDecision Decision);
}
