using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.HorseFlight;

public static class HorseFlightModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "horse-flight",
            "Horse Flight",
            GameCategory.Arcade,
            "0.1.0",
            "/games/horse-flight",
            "/api/games/horse-flight",
            GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
