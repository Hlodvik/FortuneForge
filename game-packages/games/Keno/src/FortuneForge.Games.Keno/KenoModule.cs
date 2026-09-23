using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Keno;

public static class KenoModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "keno",
            "Keno",
            GameCategory.Casino,
            "0.1.0",
            "/games/keno",
            "/api/games/keno",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
