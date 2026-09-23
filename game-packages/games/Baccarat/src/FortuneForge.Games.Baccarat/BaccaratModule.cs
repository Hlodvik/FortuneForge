using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Baccarat;

public static class BaccaratModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "baccarat",
            "Baccarat",
            GameCategory.Casino,
            "0.1.0",
            "/games/baccarat",
            "/api/games/baccarat",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
