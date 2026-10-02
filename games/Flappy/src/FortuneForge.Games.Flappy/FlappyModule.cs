using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Flappy;

public static class FlappyModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "flappy",
            "Flappy",
            GameCategory.Arcade,
            "0.1.0",
            "/games/flappy",
            "/api/games/flappy",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
