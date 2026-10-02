using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Craps;

public static class CrapsModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "craps",
            "Craps",
            GameCategory.Casino,
            "0.1.1",
            "/games/craps",
            "/api/games/craps",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
