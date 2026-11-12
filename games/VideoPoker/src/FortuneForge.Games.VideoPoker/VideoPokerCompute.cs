using FortuneForge.Games.Cards;

namespace FortuneForge.Games.VideoPoker;

public static class VideoPokerCompute
{
    public const string DealRoute = "/internal/v1/rounds/deal";
    public const string DrawRoute = "/internal/v1/rounds/draw";
}

public sealed record VideoPokerComputeDealRequest(int CoinsWagered, int HandCount = 1,
    IReadOnlyList<IReadOnlyList<PlayingCard>>? PrivateDecks = null);

public sealed record VideoPokerComputeDrawRequest(IReadOnlyList<IReadOnlyList<PlayingCard>> PrivateDecks,
    int CoinsWagered, IReadOnlyList<VideoPokerCardPosition> HeldPositions);
