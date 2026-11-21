using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Server.Cards.VideoPoker;

public static class VideoPokerBackend
{
    public static string GameId => VideoPokerModule.Descriptor.Id;
    public const string Owner = "video-poker-worker-v1";
    public const string FinancialEvidenceContract = "video-poker-protected-wallet-evidence-v1";
    public const string RoundCollection = "videoPokerRounds";
    public const string OperationCollection = "videoPokerOperations";
}
