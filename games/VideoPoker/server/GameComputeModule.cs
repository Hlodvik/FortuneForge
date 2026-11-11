using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => VideoPokerModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/rounds/deal", (DealRequest request) =>
        {
            if (request.HandCount is not (1 or 3 or 5))
                throw new ArgumentOutOfRangeException(nameof(request), "Choose one, three, or five hands.");

            var decks = new PlayingCard[request.HandCount][];
            for (var index = 0; index < decks.Length; index++)
            {
                var deck = StandardDeck.Create().ToArray();
                RandomNumberGenerator.Shuffle(deck.AsSpan());
                decks[index] = deck;
            }

            var round = VideoPokerRoundEngine.Deal(decks, request.CoinsWagered);
            return new { round.InitialDeal, round.CoinsWagered, round.HandCount, PrivateDecks = decks };
        });

        app.MapComputeAction("/internal/v1/rounds/draw", (DrawRequest request) =>
        {
            var round = VideoPokerRoundEngine.Deal(request.PrivateDecks, request.CoinsWagered);
            var result = VideoPokerRoundEngine.Draw(round, new VideoPokerHeldCardPositions(request.HeldPositions));
            return new { result.Status, result.Draws, result.Results };
        });
    }

    private sealed record DealRequest(int CoinsWagered, int HandCount = 1);
    private sealed record DrawRequest(IReadOnlyList<IReadOnlyList<PlayingCard>> PrivateDecks, int CoinsWagered,
        IReadOnlyList<VideoPokerCardPosition> HeldPositions);
}
