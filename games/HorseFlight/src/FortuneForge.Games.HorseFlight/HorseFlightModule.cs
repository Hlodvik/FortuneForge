using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.HorseFlight;

public static class HorseFlightModule
{
    public const string GameId = "horse-flight";

    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            GameId,
            "Horse Flight",
            GameCategory.Arcade,
            "0.1.0",
            "/games/horse-flight",
            "/api/games/horse-flight",
            GameCapability.Credits | GameCapability.FreePlay);
        descriptor.Validate();
        return descriptor;
    }
}
