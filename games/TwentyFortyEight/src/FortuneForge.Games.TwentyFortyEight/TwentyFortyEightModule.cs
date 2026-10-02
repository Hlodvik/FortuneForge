using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.TwentyFortyEight;

public static class TwentyFortyEightModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "2048",
            "2048",
            GameCategory.Arcade,
            "0.1.0",
            "/games/2048",
            "/api/games/2048",
            GameCapability.FreePlay | GameCapability.History);
        descriptor.Validate();
        return descriptor;
    }
}
