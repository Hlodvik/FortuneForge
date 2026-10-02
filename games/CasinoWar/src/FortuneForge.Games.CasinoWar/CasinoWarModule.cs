using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.CasinoWar;

public static class CasinoWarModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "casino-war",
            "Casino War",
            GameCategory.Casino,
            "0.1.0",
            "/games/casino-war",
            "/api/games/casino-war",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
