using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.VideoPoker;

public static class VideoPokerModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "video-poker",
            "Video Poker",
            GameCategory.Casino,
            "0.1.0",
            "/games/video-poker",
            "/api/games/video-poker",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
