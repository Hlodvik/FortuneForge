using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Snake;

public static class SnakeModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "snake",
            "Snake",
            GameCategory.Arcade,
            "0.1.0",
            "/games/snake",
            "/api/games/snake",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
