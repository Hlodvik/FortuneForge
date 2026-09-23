using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Roulette;

public static class RouletteModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "roulette",
            "Roulette",
            GameCategory.Casino,
            "0.1.1",
            "/games/roulette",
            "/api/games/roulette",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
