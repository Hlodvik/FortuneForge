using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Mines;

public static class MinesModule
{
    public static GameDescriptor Descriptor { get; } = new(
        "mines", "Mines", GameCategory.Casino, "0.1.0", "/games/mines", "/api/games/mines",
        GameCapability.Credits | GameCapability.FreePlay | GameCapability.History);
}
