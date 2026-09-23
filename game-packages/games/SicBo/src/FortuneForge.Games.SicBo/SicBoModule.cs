using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.SicBo;

public static class SicBoModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "sic-bo",
            "Sic Bo",
            GameCategory.Casino,
            "0.1.0",
            "/games/sic-bo",
            "/api/games/sic-bo",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
