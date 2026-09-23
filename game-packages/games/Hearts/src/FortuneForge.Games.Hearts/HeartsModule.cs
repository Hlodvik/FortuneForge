using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.Hearts;

public static class HeartsModule
{
    public static GameDescriptor Descriptor { get; } = CreateDescriptor();

    private static GameDescriptor CreateDescriptor()
    {
        var descriptor = new GameDescriptor(
            "hearts",
            "Hearts",
            GameCategory.Card,
            "0.1.1",
            "/cards/hearts",
            "/api/games/hearts",
            GameCapability.FreePlay | GameCapability.Multiplayer | GameCapability.Bots);
        descriptor.Validate();
        return descriptor;
    }
}
