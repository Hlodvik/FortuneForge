using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Asteroids;

public static class AsteroidsModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "asteroids",
            "Asteroids",
            GameCategory.Arcade,
            "0.1.0",
            "/games/asteroids",
            "/api/games/asteroids",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
