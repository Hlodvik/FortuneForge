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
        app.MapComputeAction(VideoPokerCompute.DealRoute, (VideoPokerComputeDealRequest request) =>
        {
            if (request.HandCount is not (1 or 3 or 5))
                throw new ArgumentOutOfRangeException(nameof(request), "Choose one, three, or five hands.");

            if (request.PrivateDecks is { } suppliedDecks)
            {
                if (suppliedDecks.Count != request.HandCount)
                    throw new ArgumentException("The private deck count must match the hand count.");
                return VideoPokerRoundEngine.Deal(suppliedDecks, request.CoinsWagered);
            }

            var decks = new PlayingCard[request.HandCount][];
            for (var index = 0; index < decks.Length; index++)
            {
                var deck = StandardDeck.Create().ToArray();
                RandomNumberGenerator.Shuffle(deck.AsSpan());
                decks[index] = deck;
            }

            return VideoPokerRoundEngine.Deal(decks, request.CoinsWagered);
        });

        app.MapComputeAction(VideoPokerCompute.DrawRoute, (VideoPokerComputeDrawRequest request) =>
        {
            var round = VideoPokerRoundEngine.Deal(request.PrivateDecks, request.CoinsWagered);
            return VideoPokerRoundEngine.Draw(round, new VideoPokerHeldCardPositions(request.HeldPositions));
        });
    }
}
