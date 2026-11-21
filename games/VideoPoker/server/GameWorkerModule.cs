using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.VideoPoker;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal static string GameId => VideoPokerBackend.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddSingleton<VideoPokerFirestoreStore>();
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapVideoPoker(resolveUserId.Invoke);
        // Retain the compute contract for older gateway deployments during cutover.
        app.MapGameAction(VideoPokerCompute.DealRoute, (VideoPokerComputeDealRequest request) =>
        {
            if (request.HandCount is not (1 or 3 or 5))
                throw new ArgumentOutOfRangeException(nameof(request), "Choose one, three, or five hands.");
            if (request.PrivateDecks is { } supplied)
            {
                if (supplied.Count != request.HandCount)
                    throw new ArgumentException("The private deck count must match the hand count.");
                return VideoPokerRoundEngine.Deal(supplied, request.CoinsWagered);
            }
            var decks = new PlayingCard[request.HandCount][];
            for (var index = 0; index < decks.Length; index++)
            {
                decks[index] = StandardDeck.Create().ToArray();
                RandomNumberGenerator.Shuffle(decks[index].AsSpan());
            }
            return VideoPokerRoundEngine.Deal(decks, request.CoinsWagered);
        });
        app.MapGameAction(VideoPokerCompute.DrawRoute, (VideoPokerComputeDrawRequest request) =>
            VideoPokerRoundEngine.Draw(VideoPokerRoundEngine.Deal(request.PrivateDecks, request.CoinsWagered),
                new VideoPokerHeldCardPositions(request.HeldPositions)));
    }
}
